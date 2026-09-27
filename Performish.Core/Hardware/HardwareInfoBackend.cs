using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Performish.Core.Scanner;

namespace Performish.Core.Hardware
{
    /// <summary>The one seam hardware/firmware reads go through, so every interpretation rule is testable
    /// against a FakeHardwareInfoBackend. There is deliberately nothing here but a read.</summary>
    public interface IHardwareInfoBackend
    {
        HardwareRawSnapshot Read(HardwareReadOptions options);
    }

    /// <summary>Real backend: WMI queries (SELECT only), read-only registry opens, Windows performance
    /// counters (via their WMI classes, so no new package), GetFirmwareType, and NVML queries. Every read
    /// is best-effort and independent: one failure leaves that one field null (shown as unavailable) and
    /// never stops the rest, the same approach RealSystemInfoBackend takes.
    ///
    /// Nothing here writes: no ManagementObject.Put/InvokeMethod, no writable registry opens, no
    /// SetFirmwareEnvironmentVariable, no NVML setters, no process launches. HardwareNoWritePathTests
    /// scans this folder's source to keep it that way.</summary>
    public sealed class RealHardwareInfoBackend : IHardwareInfoBackend
    {
        private readonly ISystemInfoBackend _system;

        public RealHardwareInfoBackend(ISystemInfoBackend system)
        {
            _system = system ?? throw new ArgumentNullException(nameof(system));
        }

        public HardwareRawSnapshot Read(HardwareReadOptions options)
        {
            options ??= new HardwareReadOptions();
            var raw = new HardwareRawSnapshot();

            // Independent reads, run in parallel like SystemScanner.Scan() - each writes only its own fields.
            var tasks = new[]
            {
                Task.Run(() => Safe(() => raw.IsLaptop = _system.IsLaptop())),
                Task.Run(() => ReadBios(raw)),
                Task.Run(() => Safe(() => raw.FirmwareType = ReadFirmwareType())),
                Task.Run(() => Safe(() => raw.SecureBootEnabled = ReadSecureBoot())),
                Task.Run(() => ReadTpm(raw)),
                Task.Run(() => ReadDeviceGuard(raw)),
                Task.Run(() => ReadComputerSystem(raw)),
                Task.Run(() => ReadBaseBoard(raw)),
                Task.Run(() => Safe(() => raw.ChipsetDescription = ReadChipset())),
                Task.Run(() => ReadProcessor(raw)),
                Task.Run(() => Safe(() => raw.CpuEffectiveClockMhz = ReadEffectiveClock())),
                Task.Run(() => Safe(() => raw.MemoryModules = ReadMemoryModules())),
                Task.Run(() => Safe(() => raw.Gpus = ReadVideoControllers())),
                Task.Run(() => Safe(() => raw.TotalGpu3DUtilizationPercent = ReadGpu3DUtilization())),
            };
            Task.WaitAll(tasks);

            ReadNvidia(raw, options);
            return raw;
        }

        private static void Safe(Action read)
        {
            try { read(); } catch { /* best-effort read: the field stays null and is shown as unavailable */ }
        }

        private static IEnumerable<ManagementBaseObject> Query(string wql, string scope = null)
        {
            using var searcher = scope == null ? new ManagementObjectSearcher(wql) : new ManagementObjectSearcher(scope, wql);
            foreach (var mo in searcher.Get()) yield return mo;
        }

        private static int? ToInt(object value)
        {
            try { return value == null ? (int?)null : Convert.ToInt32(value); } catch { return null; }
        }

        private static long? ToLong(object value)
        {
            try { return value == null ? (long?)null : Convert.ToInt64(value); } catch { return null; }
        }

        private static void ReadBios(HardwareRawSnapshot raw) => Safe(() =>
        {
            foreach (var mo in Query("SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS"))
            {
                raw.BiosManufacturer = mo["Manufacturer"] as string;
                raw.BiosVersion = mo["SMBIOSBIOSVersion"] as string;
                if (mo["ReleaseDate"] is string d && !string.IsNullOrWhiteSpace(d))
                    Safe(() => raw.BiosReleaseDate = ManagementDateTimeConverter.ToDateTime(d).ToUniversalTime());
                return;
            }
        });

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFirmwareType(out uint firmwareType);

        private static FirmwareKind ReadFirmwareType()
        {
            if (!GetFirmwareType(out var type)) return FirmwareKind.Unknown;
            return type switch { 1 => FirmwareKind.Legacy, 2 => FirmwareKind.Uefi, _ => FirmwareKind.Unknown };
        }

        private static bool? ReadSecureBoot()
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            var value = key?.GetValue("UEFISecureBootEnabled");
            return value is int i ? i == 1 : (bool?)null;
        }

        private static void ReadTpm(HardwareRawSnapshot raw)
        {
            try
            {
                var found = false;
                foreach (var mo in Query("SELECT IsEnabled_InitialValue, SpecVersion FROM Win32_Tpm", @"root\cimv2\Security\MicrosoftTpm"))
                {
                    found = true;
                    raw.TpmEnabled = mo["IsEnabled_InitialValue"] as bool?;
                    raw.TpmSpecVersion = mo["SpecVersion"] as string;
                    break;
                }
                raw.TpmPresent = found;
            }
            catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.AccessDenied)
            {
                raw.TpmAccessDenied = true;
            }
            catch (UnauthorizedAccessException)
            {
                raw.TpmAccessDenied = true;
            }
            catch { /* namespace missing or broken: leave unknown */ }
        }

        private static void ReadDeviceGuard(HardwareRawSnapshot raw) => Safe(() =>
        {
            foreach (var mo in Query("SELECT VirtualizationBasedSecurityStatus, SecurityServicesRunning FROM Win32_DeviceGuard", @"root\Microsoft\Windows\DeviceGuard"))
            {
                raw.VbsStatus = ToInt(mo["VirtualizationBasedSecurityStatus"]);
                // 2 = Hypervisor-enforced Code Integrity (Memory Integrity).
                raw.HvciRunning = mo["SecurityServicesRunning"] is uint[] running ? running.Contains(2u) : (bool?)null;
                return;
            }
        });

        private static void ReadComputerSystem(HardwareRawSnapshot raw) => Safe(() =>
        {
            foreach (var mo in Query("SELECT Manufacturer, Model, HypervisorPresent FROM Win32_ComputerSystem"))
            {
                raw.SystemManufacturer = mo["Manufacturer"] as string;
                raw.SystemModel = mo["Model"] as string;
                raw.HypervisorPresent = mo["HypervisorPresent"] as bool?;
                return;
            }
        });

        private static void ReadBaseBoard(HardwareRawSnapshot raw) => Safe(() =>
        {
            foreach (var mo in Query("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
            {
                raw.BoardManufacturer = mo["Manufacturer"] as string;
                raw.BoardProduct = mo["Product"] as string;
                return;
            }
        });

        private static string ReadChipset()
        {
            foreach (var mo in Query("SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%LPC Controller%' OR Name LIKE '%eSPI Controller%'"))
                if (mo["Name"] is string name && !string.IsNullOrWhiteSpace(name)) return name.Trim();
            return null;
        }

        private static void ReadProcessor(HardwareRawSnapshot raw) => Safe(() =>
        {
            foreach (var mo in Query("SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, VirtualizationFirmwareEnabled FROM Win32_Processor"))
            {
                raw.CpuName = (mo["Name"] as string)?.Trim();
                raw.CpuManufacturer = mo["Manufacturer"] as string;
                raw.CpuCores = ToInt(mo["NumberOfCores"]);
                raw.CpuThreads = ToInt(mo["NumberOfLogicalProcessors"]);
                raw.CpuBaseClockMhz = ToInt(mo["MaxClockSpeed"]);
                raw.VirtualizationFirmwareEnabled = mo["VirtualizationFirmwareEnabled"] as bool?;
                return;
            }
        });

        /// <summary>ProcessorFrequency x % Processor Performance - the same calculation Task Manager uses
        /// for "Speed". Formatted perf classes need a first sample to prime rate counters, so read twice.</summary>
        private static int? ReadEffectiveClock()
        {
            int? result = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                foreach (var mo in Query("SELECT ProcessorFrequency, PercentProcessorPerformance FROM Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name='_Total'"))
                {
                    var freq = ToLong(mo["ProcessorFrequency"]);
                    var pct = ToLong(mo["PercentProcessorPerformance"]);
                    result = freq > 0 && pct > 0 ? (int)Math.Round(freq.Value * pct.Value / 100.0) : (int?)null;
                }
                if (attempt == 0) Thread.Sleep(250);
            }
            return result;
        }

        private static List<MemoryModuleRaw> ReadMemoryModules()
        {
            var modules = new List<MemoryModuleRaw>();
            foreach (var mo in Query("SELECT Manufacturer, PartNumber, Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType, FormFactor, DeviceLocator, BankLabel FROM Win32_PhysicalMemory"))
            {
                modules.Add(new MemoryModuleRaw
                {
                    Manufacturer = mo["Manufacturer"] as string,
                    PartNumber = (mo["PartNumber"] as string)?.Trim(),
                    CapacityBytes = ToLong(mo["Capacity"]),
                    RatedSpeedMts = ToInt(mo["Speed"]),
                    ConfiguredSpeedMts = ToInt(mo["ConfiguredClockSpeed"]),
                    SmbiosMemoryType = ToInt(mo["SMBIOSMemoryType"]),
                    FormFactor = ToInt(mo["FormFactor"]),
                    DeviceLocator = mo["DeviceLocator"] as string,
                    BankLabel = mo["BankLabel"] as string
                });
            }
            return modules;
        }

        private const string DisplayClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

        private static List<GpuRaw> ReadVideoControllers()
        {
            var gpus = new List<GpuRaw>();
            foreach (var mo in Query("SELECT Name, AdapterCompatibility, DriverVersion FROM Win32_VideoController"))
            {
                var gpu = new GpuRaw
                {
                    Name = mo["Name"] as string,
                    AdapterCompatibility = mo["AdapterCompatibility"] as string,
                    DriverVersion = mo["DriverVersion"] as string
                };
                // Win32_VideoController.AdapterRAM is 32-bit and caps at 4 GB, so read the driver's own
                // 64-bit value from the display class key instead.
                Safe(() => gpu.DedicatedMemoryBytes = ReadDedicatedMemory(gpu.Name));
                gpus.Add(gpu);
            }
            return gpus;
        }

        private static long? ReadDedicatedMemory(string adapterName)
        {
            if (string.IsNullOrWhiteSpace(adapterName)) return null;
            using var cls = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
            if (cls == null) return null;
            foreach (var sub in cls.GetSubKeyNames())
            {
                if (sub.Length != 4 || !sub.All(char.IsDigit)) continue;
                using var key = cls.OpenSubKey(sub);
                if (!string.Equals(key?.GetValue("DriverDesc") as string, adapterName, StringComparison.OrdinalIgnoreCase)) continue;
                var value = key.GetValue("HardwareInformation.qwMemorySize");
                if (value is long l) return l;
                if (value is byte[] bytes && bytes.Length >= 8) return BitConverter.ToInt64(bytes, 0);
                var legacy = key.GetValue("HardwareInformation.MemorySize");
                if (legacy is int i) return (uint)i;
                if (legacy is byte[] b4 && b4.Length >= 4) return BitConverter.ToUInt32(b4, 0);
            }
            return null;
        }

        private static double? ReadGpu3DUtilization()
        {
            double? total = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                double sum = 0;
                var any = false;
                foreach (var mo in Query("SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine"))
                {
                    if (!((mo["Name"] as string) ?? "").Contains("engtype_3D", StringComparison.OrdinalIgnoreCase)) continue;
                    any = true;
                    sum += ToLong(mo["UtilizationPercentage"]) ?? 0;
                }
                total = any ? sum : (double?)null;
                if (attempt == 0) Thread.Sleep(250);
            }
            return total;
        }

        private static void ReadNvidia(HardwareRawSnapshot raw, HardwareReadOptions options)
        {
            var nvidia = raw.Gpus.Where(g => (g.Name ?? "").IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0
                || (g.AdapterCompatibility ?? "").IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (nvidia.Count == 0) { raw.NvmlState = NvmlState.NotAttempted; return; }

            // Switchable graphics: an NVIDIA GPU alongside another adapter on a laptop is normally powered
            // down at idle, and NVML init wakes it. Only read it when the user asked to.
            var hybrid = raw.IsLaptop && raw.Gpus.Count > nvidia.Count;
            if (hybrid && !options.WakeDiscreteGpu) { raw.NvmlState = NvmlState.SkippedToAvoidWaking; return; }

            var (state, devices) = NvmlReader.ReadAll();
            raw.NvmlState = state;
            if (state != NvmlState.Ok) return;

            var unmatched = new List<NvidiaTelemetry>(devices);
            foreach (var gpu in nvidia)
            {
                var match = unmatched.FirstOrDefault(d => string.Equals(d.Name, gpu.Name?.Trim(), StringComparison.OrdinalIgnoreCase))
                    ?? unmatched.FirstOrDefault();
                if (match == null) break;
                gpu.Nvidia = match;
                unmatched.Remove(match);
            }
        }
    }
}
