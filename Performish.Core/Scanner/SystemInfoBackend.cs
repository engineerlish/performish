using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using Microsoft.Win32;

namespace Performish.Core.Scanner
{
    /// <summary>Raw read-only facts about the machine - the one seam SystemScanner depends on, so
    /// the scanner's composition logic (deciding what counts as "laptop", mapping GPU vendor
    /// strings, etc.) is unit-testable against a FakeSystemInfoBackend without WMI or the registry.</summary>
    public interface ISystemInfoBackend
    {
        (string ProductName, string EditionId, int BuildNumber, int Ubr) GetOsInfo();
        bool IsDomainJoined();
        bool IsMdmManaged();
        /// <summary>Name + core count in one call - a single "SELECT Name, NumberOfCores FROM
        /// Win32_Processor" instead of two separate WMI round trips for the same class, halving the
        /// CPU-related WMI cost of a scan (Phase 5 "avoid repeated calls to slow interfaces").</summary>
        (string Name, int Cores) GetCpuInfo();
        int GetCpuLogicalProcessorCount();
        List<GpuInfo> GetGpus();
        (long TotalBytes, long AvailableBytes) GetMemory();
        bool SystemDriveIsSsd();
        bool IsLaptop();
        List<StartupItemInfo> GetStartupItems();
        List<ScheduledTaskInfo> GetScheduledTasks();
        List<ServiceInfo> GetServices();
        int GetProcessCount();
        (long FreeBytes, long TotalBytes) GetSystemDriveSpace();
        double GetUptimeHours();
        /// <summary>Distinct running process names (no extension, no path) - used only to check
        /// against Performish.Core.Compatibility.KnownAgents. Never used to identify or act on any
        /// specific process beyond that name match.</summary>
        List<string> GetRunningProcessNames();
    }

    /// <summary>Real backend: WMI (System.Management) for hardware/OS facts, registry Run keys and
    /// the Startup folder for startup items, schtasks.exe output for scheduled tasks, ServiceController
    /// for services. Every read here is exactly that - read-only - satisfying "this does not limit
    /// read-only scanning" even when nothing else in the app is allowed to touch the real machine.</summary>
    public sealed class RealSystemInfoBackend : ISystemInfoBackend
    {
        private readonly Backends.IProcessRunner _process;

        public RealSystemInfoBackend(Backends.IProcessRunner process)
        {
            _process = process ?? throw new ArgumentNullException(nameof(process));
        }

        public (string ProductName, string EditionId, int BuildNumber, int Ubr) GetOsInfo()
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var productName = key?.GetValue("ProductName") as string ?? "Unknown";
            var editionId = key?.GetValue("EditionID") as string ?? "Unknown";
            var buildStr = key?.GetValue("CurrentBuildNumber") as string;
            int.TryParse(buildStr, out var build);
            var ubr = key?.GetValue("UBR");
            var ubrInt = ubr is int i ? i : 0;
            return (productName, editionId, build, ubrInt);
        }

        public bool IsDomainJoined()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT PartOfDomain FROM Win32_ComputerSystem");
                foreach (ManagementObject mo in searcher.Get())
                    return (bool)(mo["PartOfDomain"] ?? false);
            }
            catch { /* best-effort read */ }
            return false;
        }

        public bool IsMdmManaged()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Enrollments");
                return key?.GetSubKeyNames().Length > 0;
            }
            catch { return false; }
        }

        public (string Name, int Cores) GetCpuInfo()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores FROM Win32_Processor");
                foreach (ManagementObject mo in searcher.Get())
                {
                    var name = (mo["Name"] as string)?.Trim() ?? "Unknown CPU";
                    var cores = mo["NumberOfCores"] != null ? Convert.ToInt32(mo["NumberOfCores"]) : Environment.ProcessorCount;
                    return (name, cores);
                }
            }
            catch { /* fall through */ }
            return ("Unknown CPU", Environment.ProcessorCount);
        }

        public int GetCpuLogicalProcessorCount() => Environment.ProcessorCount;

        public List<GpuInfo> GetGpus()
        {
            var gpus = new List<GpuInfo>();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion, AdapterCompatibility FROM Win32_VideoController");
                foreach (ManagementObject mo in searcher.Get())
                {
                    var name = mo["Name"] as string ?? "Unknown GPU";
                    var compat = mo["AdapterCompatibility"] as string ?? string.Empty;
                    gpus.Add(new GpuInfo
                    {
                        Name = name,
                        DriverVersion = mo["DriverVersion"] as string ?? "Unknown",
                        Vendor = ClassifyVendor(name, compat)
                    });
                }
            }
            catch { /* best-effort read */ }
            return gpus;
        }

        private static string ClassifyVendor(string name, string compat)
        {
            var s = (name + " " + compat).ToUpperInvariant();
            if (s.Contains("NVIDIA")) return "NVIDIA";
            if (s.Contains("AMD") || s.Contains("ATI")) return "AMD";
            if (s.Contains("INTEL")) return "Intel";
            return "Unknown";
        }

        public (long TotalBytes, long AvailableBytes) GetMemory()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
                foreach (ManagementObject mo in searcher.Get())
                {
                    var totalKb = Convert.ToInt64(mo["TotalVisibleMemorySize"]);
                    var freeKb = Convert.ToInt64(mo["FreePhysicalMemory"]);
                    return (totalKb * 1024, freeKb * 1024);
                }
            }
            catch { /* fall through */ }
            return (0, 0);
        }

        public bool SystemDriveIsSsd()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Storage",
                    "SELECT MediaType FROM MSFT_PhysicalDisk");
                foreach (ManagementObject mo in searcher.Get())
                {
                    // MediaType: 3 = HDD, 4 = SSD, 5 = SCM. Any non-HDD counts as "not spinning rust".
                    var mediaType = Convert.ToInt32(mo["MediaType"]);
                    if (mediaType != 3) return true;
                }
            }
            catch { /* storage WMI namespace can be unavailable; assume SSD (safer default: skip defrag-style tweaks) */ }
            return true;
        }

        public bool IsLaptop()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure");
                foreach (ManagementObject mo in searcher.Get())
                {
                    if (mo["ChassisTypes"] is ushort[] types)
                    {
                        // 8,9,10,11,12,14,18,21 are laptop/notebook/sub-notebook chassis codes.
                        var laptopCodes = new HashSet<int> { 8, 9, 10, 11, 12, 14, 18, 21 };
                        if (types.Any(t => laptopCodes.Contains(t))) return true;
                    }
                }
            }
            catch { /* best-effort */ }
            try
            {
                using var battery = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");
                return battery.Get().Count > 0;
            }
            catch { return false; }
        }

        public List<StartupItemInfo> GetStartupItems()
        {
            var items = new List<StartupItemInfo>();
            AddRunKeyItems(items, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU Run");
            AddRunKeyItems(items, Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM Run");
            return items;
        }

        private static void AddRunKeyItems(List<StartupItemInfo> items, RegistryKey hive, string path, string source)
        {
            try
            {
                using var key = hive.OpenSubKey(path);
                if (key == null) return;
                foreach (var name in key.GetValueNames())
                    items.Add(new StartupItemInfo { Name = name, Command = key.GetValue(name) as string, Source = source });
            }
            catch { /* best-effort */ }
        }

        public List<ScheduledTaskInfo> GetScheduledTasks()
        {
            var tasks = new List<ScheduledTaskInfo>();
            try
            {
                var result = _process.Run("schtasks.exe", "/Query /FO CSV /NH");
                foreach (var line in result.StandardOutput.Split('\n'))
                {
                    var trimmed = line.Trim().Trim('\r');
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;
                    var fields = trimmed.Split(',').Select(f => f.Trim('"')).ToArray();
                    if (fields.Length < 3) continue;
                    tasks.Add(new ScheduledTaskInfo
                    {
                        Path = fields[0],
                        Enabled = fields[2].IndexOf("Ready", StringComparison.OrdinalIgnoreCase) >= 0
                            || fields[2].IndexOf("Running", StringComparison.OrdinalIgnoreCase) >= 0
                    });
                }
            }
            catch { /* best-effort read */ }
            return tasks;
        }

        public List<ServiceInfo> GetServices()
        {
            var services = new List<ServiceInfo>();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, DisplayName, StartMode, State FROM Win32_Service");
                foreach (ManagementObject mo in searcher.Get())
                {
                    services.Add(new ServiceInfo
                    {
                        Name = mo["Name"] as string,
                        DisplayName = mo["DisplayName"] as string,
                        StartMode = mo["StartMode"] as string,
                        Running = (mo["State"] as string) == "Running"
                    });
                }
            }
            catch { /* best-effort */ }
            return services;
        }

        public int GetProcessCount() => Process.GetProcesses().Length;

        public (long FreeBytes, long TotalBytes) GetSystemDriveSpace()
        {
            try
            {
                var systemDrive = Environment.GetFolderPath(Environment.SpecialFolder.Windows).Substring(0, 1) + ":\\";
                var drive = new System.IO.DriveInfo(systemDrive);
                return (drive.AvailableFreeSpace, drive.TotalSize);
            }
            catch
            {
                return (0, 0);
            }
        }

        public double GetUptimeHours() => Environment.TickCount64 / 1000.0 / 60.0 / 60.0;

        public List<string> GetRunningProcessNames()
        {
            try
            {
                return Process.GetProcesses().Select(p => p.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch
            {
                return new List<string>();
            }
        }
    }

    /// <summary>Seeded, fully in-memory - the only ISystemInfoBackend this dev/test session uses.</summary>
    public sealed class FakeSystemInfoBackend : ISystemInfoBackend
    {
        public string ProductName = "Windows 11 Pro";
        public string EditionId = "Professional";
        public int BuildNumber = 26200;
        public int Ubr = 1000;
        public bool DomainJoined;
        public bool MdmManaged;
        public string CpuName = "Fake CPU";
        public int CpuCores = 8;
        public int CpuLogical = 16;
        public List<GpuInfo> Gpus = new List<GpuInfo> { new GpuInfo { Name = "Fake GPU", Vendor = "NVIDIA", DriverVersion = "1.0.0.0" } };
        public long TotalRam = 32L * 1024 * 1024 * 1024;
        public long AvailableRam = 16L * 1024 * 1024 * 1024;
        public bool IsSsd = true;
        public bool Laptop;
        public List<StartupItemInfo> StartupItems = new List<StartupItemInfo>();
        public List<ScheduledTaskInfo> ScheduledTasks = new List<ScheduledTaskInfo>();
        public List<ServiceInfo> Services = new List<ServiceInfo>();
        public int ProcessCount = 120;
        public long FreeDiskBytes = 200L * 1024 * 1024 * 1024;
        public long TotalDiskBytes = 500L * 1024 * 1024 * 1024;
        public double UptimeHours = 12.0;
        public List<string> RunningProcessNames = new List<string>();

        public (string ProductName, string EditionId, int BuildNumber, int Ubr) GetOsInfo() => (ProductName, EditionId, BuildNumber, Ubr);
        public bool IsDomainJoined() => DomainJoined;
        public bool IsMdmManaged() => MdmManaged;
        public (string Name, int Cores) GetCpuInfo() => (CpuName, CpuCores);
        public int GetCpuLogicalProcessorCount() => CpuLogical;
        public List<GpuInfo> GetGpus() => Gpus;
        public (long TotalBytes, long AvailableBytes) GetMemory() => (TotalRam, AvailableRam);
        public bool SystemDriveIsSsd() => IsSsd;
        public bool IsLaptop() => Laptop;
        public List<StartupItemInfo> GetStartupItems() => StartupItems;
        public List<ScheduledTaskInfo> GetScheduledTasks() => ScheduledTasks;
        public List<ServiceInfo> GetServices() => Services;
        public int GetProcessCount() => ProcessCount;
        public (long FreeBytes, long TotalBytes) GetSystemDriveSpace() => (FreeDiskBytes, TotalDiskBytes);
        public double GetUptimeHours() => UptimeHours;
        public List<string> GetRunningProcessNames() => RunningProcessNames;
    }
}
