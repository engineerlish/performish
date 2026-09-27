using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Performish.Core.Hardware
{
    /// <summary>Turns a HardwareRawSnapshot into display readings and guide-only findings. Pure: no I/O,
    /// clock injected, so every classification rule is unit-tested against hand-built snapshots (all
    /// fields present, all missing, partial, unusual). Every value it emits is either measured, a
    /// clearly-labeled inference, or explicitly unavailable with a reason - never a guess.</summary>
    public static class HardwareReportBuilder
    {
        public const string NeedsSensorDriver =
            "Needs a low-level hardware sensor driver (PawnIO). Performish never installs drivers; reading this through a PawnIO you installed yourself is planned for a later version.";
        public const string NeedsVendorGpuLibrary =
            "Live clocks and power for AMD and Intel GPUs need AMD ADLX / Intel IGCL, which aren't integrated yet.";
        public const string NotReportedByWindows = "Windows didn't report this on this machine.";

        /// <summary>BIOS older than this gets an informational "check for an update" finding.</summary>
        public const int OldBiosDays = 3 * 365;

        private static readonly string[] Placeholders =
        {
            "default string", "to be filled by o.e.m.", "system product name", "system manufacturer",
            "not applicable", "not specified", "unknown", "none", "n/a", "o.e.m.", "base board product name"
        };

        public static HardwareReport Build(HardwareRawSnapshot raw, DateTime nowUtc)
        {
            raw ??= new HardwareRawSnapshot();
            var report = new HardwareReport
            {
                TakenAtUtc = nowUtc,
                IsLaptop = raw.IsLaptop,
                DiscreteGpuSkipped = raw.NvmlState == NvmlState.SkippedToAvoidWaking
            };

            var memory = MemoryAnalysis.AnalyzeProfile(raw.MemoryModules, raw.CpuName, raw.IsLaptop);

            report.Groups.Add(BuildFirmware(raw, nowUtc));
            report.Groups.Add(BuildMotherboard(raw));
            report.Groups.Add(BuildCpu(raw));
            report.Groups.Add(BuildMemory(raw, memory));
            report.Groups.Add(BuildGpu(raw));

            report.Findings.AddRange(BuildFindings(raw, report, memory, nowUtc));
            return report;
        }

        // ---- groups ------------------------------------------------------------------------------

        private static HardwareGroup BuildFirmware(HardwareRawSnapshot raw, DateTime nowUtc)
        {
            var g = new HardwareGroup { Component = HardwareComponent.Firmware, Title = "BIOS / firmware" };
            const HardwareComponent c = HardwareComponent.Firmware;

            g.Readings.Add(Text("fw.bios_vendor", "BIOS vendor", raw.BiosManufacturer, "WMI Win32_BIOS.Manufacturer", c));
            g.Readings.Add(Text("fw.bios_version", "BIOS version", raw.BiosVersion, "WMI Win32_BIOS.SMBIOSBIOSVersion", c));

            if (raw.BiosReleaseDate.HasValue && raw.BiosReleaseDate.Value.Year >= 1990 && raw.BiosReleaseDate.Value <= nowUtc.AddDays(1))
            {
                var age = AgeText(raw.BiosReleaseDate.Value, nowUtc);
                g.Readings.Add(Measured("fw.bios_date", "BIOS release date",
                    $"{raw.BiosReleaseDate.Value:yyyy-MM-dd} ({age})", "WMI Win32_BIOS.ReleaseDate", c));
            }
            else
                g.Readings.Add(Unavailable("fw.bios_date", "BIOS release date",
                    raw.BiosReleaseDate.HasValue ? "Windows reported an implausible date, so it isn't shown." : NotReportedByWindows, c));

            g.Readings.Add(raw.FirmwareType switch
            {
                FirmwareKind.Uefi => Measured("fw.type", "Boot mode", "UEFI", "GetFirmwareType()", c),
                FirmwareKind.Legacy => Measured("fw.type", "Boot mode", "Legacy BIOS (CSM)", "GetFirmwareType()", c,
                    "Legacy boot rules out Secure Boot and Resizable BAR."),
                _ => Unavailable("fw.type", "Boot mode", NotReportedByWindows, c)
            });

            if (raw.FirmwareType == FirmwareKind.Legacy)
                g.Readings.Add(NotApplicable("fw.secure_boot", "Secure Boot", "Secure Boot needs UEFI boot mode.", c));
            else
                g.Readings.Add(OnOff("fw.secure_boot", "Secure Boot", raw.SecureBootEnabled,
                    @"Registry HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot\State\UEFISecureBootEnabled", c));

            if (raw.TpmAccessDenied)
                g.Readings.Add(Unavailable("fw.tpm", "TPM", "Reading TPM details needs Performish to run as administrator.", c));
            else if (raw.TpmPresent == false)
                g.Readings.Add(Measured("fw.tpm", "TPM", "Not found", "WMI Win32_Tpm", c,
                    "No TPM visible to Windows. It may be switched off in the BIOS (often called fTPM or PTT)."));
            else if (raw.TpmPresent == true)
            {
                var version = FirstSpecVersion(raw.TpmSpecVersion);
                var state = raw.TpmEnabled == false ? "present, disabled" : "present";
                g.Readings.Add(Measured("fw.tpm", "TPM", version != null ? $"{state}, version {version}" : state, "WMI Win32_Tpm", c));
            }
            else
                g.Readings.Add(Unavailable("fw.tpm", "TPM", NotReportedByWindows, c));

            // Win32_Processor.VirtualizationFirmwareEnabled reads false whenever a hypervisor (Hyper-V,
            // VBS, WSL2) is already running, even though virtualization is obviously on - check first.
            if (raw.HypervisorPresent == true)
                g.Readings.Add(Inferred("fw.virtualization", "CPU virtualization (VT-x / AMD-V)", "On",
                    "WMI Win32_ComputerSystem.HypervisorPresent", c,
                    "A hypervisor is running, which requires virtualization to be enabled in firmware."));
            else
                g.Readings.Add(OnOff("fw.virtualization", "CPU virtualization (VT-x / AMD-V)", raw.VirtualizationFirmwareEnabled,
                    "WMI Win32_Processor.VirtualizationFirmwareEnabled", c));

            var vbs = g.Readings.Count;
            g.Readings.Add(raw.VbsStatus switch
            {
                2 => Measured("fw.vbs", "Virtualization-based security", "Running", "WMI Win32_DeviceGuard", c),
                1 => Measured("fw.vbs", "Virtualization-based security", "Enabled, not running", "WMI Win32_DeviceGuard", c),
                0 => Measured("fw.vbs", "Virtualization-based security", "Off", "WMI Win32_DeviceGuard", c),
                _ => Unavailable("fw.vbs", "Virtualization-based security", NotReportedByWindows, c)
            });
            g.Readings.Add(raw.HvciRunning.HasValue
                ? Measured("fw.hvci", "Memory Integrity (HVCI)", raw.HvciRunning.Value ? "On" : "Off", "WMI Win32_DeviceGuard.SecurityServicesRunning", c)
                : Unavailable("fw.hvci", "Memory Integrity (HVCI)", NotReportedByWindows, c));
            g.Readings[vbs].Concern = ReadingConcern.Performance;
            g.Readings[vbs + 1].Concern = ReadingConcern.Performance;

            return g;
        }

        private static HardwareGroup BuildMotherboard(HardwareRawSnapshot raw)
        {
            var g = new HardwareGroup { Component = HardwareComponent.Motherboard, Title = "Motherboard / system" };
            const HardwareComponent c = HardwareComponent.Motherboard;

            g.Readings.Add(Text("board.system", "System",
                Join(Clean(raw.SystemManufacturer), Clean(raw.SystemModel)), "WMI Win32_ComputerSystem", c));
            g.Readings.Add(Text("board.board", "Motherboard",
                Join(Clean(raw.BoardManufacturer), Clean(raw.BoardProduct)), "WMI Win32_BaseBoard", c));

            var chipset = Clean(raw.ChipsetDescription);
            g.Readings.Add(chipset != null
                ? Measured("board.chipset", "Chipset", chipset, "Windows device list (LPC/eSPI bridge)", c)
                : Unavailable("board.chipset", "Chipset", "Windows doesn't name the chipset on this platform (common on AMD systems).", c));

            g.Readings.Add(Unavailable("board.vrm", "VRM (power delivery)",
                "Windows has no interface for VRM details. Some boards expose a VRM temperature through a sensor driver only.", c));
            return g;
        }

        private static HardwareGroup BuildCpu(HardwareRawSnapshot raw)
        {
            var g = new HardwareGroup { Component = HardwareComponent.Cpu, Title = "CPU" };
            const HardwareComponent c = HardwareComponent.Cpu;

            g.Readings.Add(Text("cpu.name", "Processor", Clean(raw.CpuName), "WMI Win32_Processor.Name", c));

            if (raw.CpuCores > 0 && raw.CpuThreads > 0)
                g.Readings.Add(Measured("cpu.cores", "Cores / threads", $"{raw.CpuCores} / {raw.CpuThreads}", "WMI Win32_Processor", c));
            else
                g.Readings.Add(Unavailable("cpu.cores", "Cores / threads", NotReportedByWindows, c));

            g.Readings.Add(Mhz("cpu.base_clock", "Base clock", raw.CpuBaseClockMhz, "WMI Win32_Processor.MaxClockSpeed", c,
                ReadingConcern.Performance, "Windows labels this \"max clock\", but it's normally the base clock."));
            g.Readings.Add(Mhz("cpu.effective_clock", "Current clock (right now)", raw.CpuEffectiveClockMhz,
                @"Performance counter \Processor Information(_Total)\% Processor Performance", c,
                ReadingConcern.Performance, "A single moment's reading. It rises under load and drops at idle, same as Task Manager's \"Speed\"."));
            g.Readings.Add(Unavailable("cpu.boost_clock", "Rated boost clock", "Windows doesn't report the rated boost clock; check the CPU's spec page.", c, ReadingConcern.Performance));
            g.Readings.Add(Unavailable("cpu.voltage", "Core voltage", NeedsSensorDriver, c, ReadingConcern.Performance));
            g.Readings.Add(Unavailable("cpu.power_limits", "Power limits", NeedsSensorDriver, c, ReadingConcern.Performance));
            g.Readings.Add(Unavailable("cpu.temperature", "Temperature", NeedsSensorDriver, c, ReadingConcern.Performance));
            return g;
        }

        private static HardwareGroup BuildMemory(HardwareRawSnapshot raw, MemoryProfileAnalysis analysis)
        {
            var g = new HardwareGroup { Component = HardwareComponent.Memory, Title = "Memory (RAM)" };
            const HardwareComponent c = HardwareComponent.Memory;
            const string src = "WMI Win32_PhysicalMemory";
            var modules = (raw.MemoryModules ?? new List<MemoryModuleRaw>()).Where(m => m != null).ToList();

            if (modules.Count == 0)
            {
                g.Readings.Add(Unavailable("mem.total", "Installed", "Windows didn't list any memory modules.", c));
                g.Readings.Add(Unavailable("mem.speed", "Running speed", NotReportedByWindows, c, ReadingConcern.Performance));
                g.Readings.Add(Unavailable("mem.profile", "XMP / EXPO profile", "Needs the memory module list, which Windows didn't provide.", c, ReadingConcern.Performance));
                g.Readings.Add(Unavailable("mem.timings", "Timings", NeedsSensorDriver, c, ReadingConcern.Performance));
                return g;
            }

            var total = modules.Where(m => m.CapacityBytes > 0).Sum(m => m.CapacityBytes.Value);
            g.Readings.Add(total > 0
                ? Measured("mem.total", "Installed", $"{FormatGb(total)} in {modules.Count} module{(modules.Count == 1 ? "" : "s")}", src, c)
                : Unavailable("mem.total", "Installed", NotReportedByWindows, c));

            g.Readings.Add(analysis.MemoryTypeName != null
                ? Measured("mem.type", "Type", analysis.MemoryTypeName + (modules.Any(m => m.FormFactor == 12) ? " SO-DIMM" : ""), src + ".SMBIOSMemoryType", c)
                : Unavailable("mem.type", "Type", NotReportedByWindows, c));

            g.Readings.Add(analysis.ConfiguredMts.HasValue
                ? Measured("mem.speed", "Running speed", $"{analysis.ConfiguredMts} MT/s", src + ".ConfiguredClockSpeed", c, concern: ReadingConcern.Performance)
                : Unavailable("mem.speed", "Running speed", NotReportedByWindows, c, ReadingConcern.Performance));

            g.Readings.Add(analysis.RatedMts.HasValue
                ? Measured("mem.rated", "Module rating", $"{analysis.RatedMts} MT/s", src + ".Speed", c,
                    "The modules' standard (JEDEC) rating. A kit's faster XMP/EXPO rating isn't visible to Windows.", ReadingConcern.Performance)
                : Unavailable("mem.rated", "Module rating", NotReportedByWindows, c, ReadingConcern.Performance));

            if (analysis.CpuSupportedMaxMts.HasValue)
                g.Readings.Add(Measured("mem.cpu_max", "CPU's supported maximum", $"{analysis.CpuSupportedMaxMts} MT/s",
                    "Performish CPU memory-speed table (vendor spec sheets)", c, concern: ReadingConcern.Performance));

            g.Readings.Add(analysis.Verdict switch
            {
                MemoryProfileVerdict.ProfileLikelyOn => Inferred("mem.profile", "XMP / EXPO profile", "Likely on", "Running speed vs standard speed", c, analysis.Explanation, ReadingConcern.Performance),
                MemoryProfileVerdict.StandardSpeedCheckKit => Inferred("mem.profile", "XMP / EXPO profile", "Possibly off - check your kit", "Running speed vs standard speed", c, analysis.Explanation, ReadingConcern.Performance),
                MemoryProfileVerdict.BelowModuleRating => Inferred("mem.profile", "XMP / EXPO profile", "Running below rating", "Running speed vs module rating", c, analysis.Explanation, ReadingConcern.Performance),
                MemoryProfileVerdict.AtCpuSupportedMaximum => NotApplicable("mem.profile", "XMP / EXPO profile", analysis.Explanation, c, ReadingConcern.Performance),
                MemoryProfileVerdict.LaptopAtRatedSpeed => NotApplicable("mem.profile", "XMP / EXPO profile", analysis.Explanation, c, ReadingConcern.Performance),
                _ => Unavailable("mem.profile", "XMP / EXPO profile", analysis.Explanation ?? NotReportedByWindows, c, ReadingConcern.Performance)
            });

            g.Readings.Add(Unavailable("mem.timings", "Timings", NeedsSensorDriver, c, ReadingConcern.Performance));

            var slots = modules.Select(m => Clean(m.DeviceLocator) ?? Clean(m.BankLabel)).Where(s => s != null).ToList();
            if (modules.Count == 1)
                g.Readings.Add(Inferred("mem.channels", "Channel layout", "Single module (single-channel)", src, c,
                    raw.IsLaptop ? "Laptops with soldered memory can list it as one module even when it's dual-channel." : "One module can only use one memory channel.",
                    ReadingConcern.Performance));
            else
                g.Readings.Add(Inferred("mem.channels", "Channel layout",
                    $"{modules.Count} modules" + (slots.Count > 0 ? $" in {string.Join(", ", slots)}" : ""), src, c,
                    "Windows doesn't report the channel mode directly. Slot names are shown so you can compare with the manual.", ReadingConcern.Performance));

            for (int i = 0; i < modules.Count; i++)
            {
                var m = modules[i];
                var parts = new List<string>();
                if (m.CapacityBytes > 0) parts.Add(FormatGb(m.CapacityBytes.Value));
                var vendor = MemoryAnalysis.DecodeManufacturer(m.Manufacturer);
                if (vendor != null) parts.Add(vendor);
                var part = Clean(m.PartNumber);
                if (part != null) parts.Add(part);
                var slot = Clean(m.DeviceLocator) ?? Clean(m.BankLabel);
                var label = $"Module {i + 1}" + (slot != null ? $" ({slot})" : "");
                g.Readings.Add(parts.Count > 0
                    ? Measured($"mem.module{i}", label, string.Join(", ", parts), src, c)
                    : Unavailable($"mem.module{i}", label, NotReportedByWindows, c));
            }
            return g;
        }

        private static HardwareGroup BuildGpu(HardwareRawSnapshot raw)
        {
            var g = new HardwareGroup { Component = HardwareComponent.Gpu, Title = "Graphics (GPU)" };
            const HardwareComponent c = HardwareComponent.Gpu;
            var gpus = (raw.Gpus ?? new List<GpuRaw>()).Where(x => x != null).ToList();

            if (gpus.Count == 0)
            {
                g.Readings.Add(Unavailable("gpu.none", "Graphics adapter", "Windows didn't list a graphics adapter.", c));
                return g;
            }

            if (raw.TotalGpu3DUtilizationPercent.HasValue && raw.TotalGpu3DUtilizationPercent.Value >= 0)
                g.Readings.Add(Measured("gpu.util_all", "3D load, all GPUs (right now)", $"{Math.Min(100, raw.TotalGpu3DUtilizationPercent.Value):0}%",
                    "Performance counter GPU Engine (engtype_3D)", c, concern: ReadingConcern.Performance));
            else
                g.Readings.Add(Unavailable("gpu.util_all", "3D load, all GPUs (right now)", NotReportedByWindows, c, ReadingConcern.Performance));

            for (int i = 0; i < gpus.Count; i++)
            {
                var gpu = gpus[i];
                var p = $"gpu{i}.";
                var name = Clean(gpu.Name) ?? $"GPU {i + 1}";
                var prefix = gpus.Count > 1 ? $"{name}: " : "";
                var isNvidia = IsNvidia(gpu);

                g.Readings.Add(Measured(p + "name", gpus.Count > 1 ? $"GPU {i + 1}" : "GPU", name, "WMI Win32_VideoController.Name", c));
                g.Readings.Add(Text(p + "driver", prefix + "Driver version", Clean(gpu.DriverVersion), "WMI Win32_VideoController.DriverVersion", c));

                var vram = gpu.Nvidia?.VramTotalBytes > 0 ? gpu.Nvidia.VramTotalBytes : gpu.DedicatedMemoryBytes;
                g.Readings.Add(vram > 0
                    ? Measured(p + "vram", prefix + "Dedicated memory", FormatGb(vram.Value),
                        gpu.Nvidia?.VramTotalBytes > 0 ? "NVML nvmlDeviceGetMemoryInfo" : "Registry HardwareInformation.qwMemorySize", c)
                    : Unavailable(p + "vram", prefix + "Dedicated memory", "No dedicated memory reported (integrated GPUs share system RAM).", c));

                if (gpu.Nvidia != null)
                {
                    var n = gpu.Nvidia;
                    g.Readings.Add(Mhz(p + "core_clock", prefix + "Core clock (right now)", n.CoreClockMhz, "NVML nvmlDeviceGetClockInfo", c, ReadingConcern.Performance, null));
                    g.Readings.Add(Mhz(p + "max_clock", prefix + "Max core clock", n.MaxCoreClockMhz, "NVML nvmlDeviceGetMaxClockInfo", c, ReadingConcern.Performance, null));
                    g.Readings.Add(Mhz(p + "mem_clock", prefix + "Memory clock (right now)", n.MemoryClockMhz, "NVML nvmlDeviceGetClockInfo", c, ReadingConcern.Performance, null));
                    g.Readings.Add(Watts(p + "power", prefix + "Power draw (right now)", n.PowerDrawWatts, "NVML nvmlDeviceGetPowerUsage", c));
                    g.Readings.Add(Watts(p + "power_limit", prefix + "Power limit", n.PowerLimitWatts, "NVML nvmlDeviceGetEnforcedPowerLimit", c));
                    g.Readings.Add(n.TemperatureC is > 0 and < 150
                        ? Measured(p + "temp", prefix + "Temperature", $"{n.TemperatureC} °C", "NVML nvmlDeviceGetTemperature", c, concern: ReadingConcern.Performance)
                        : Unavailable(p + "temp", prefix + "Temperature", "The driver didn't return a plausible temperature.", c, ReadingConcern.Performance));
                    g.Readings.Add(n.UtilizationPercent is >= 0 and <= 100
                        ? Measured(p + "util", prefix + "Load (right now)", $"{n.UtilizationPercent}%", "NVML nvmlDeviceGetUtilizationRates", c, concern: ReadingConcern.Performance)
                        : Unavailable(p + "util", prefix + "Load (right now)", "The driver didn't return a valid load value.", c, ReadingConcern.Performance));
                }
                else
                {
                    var reason = isNvidia ? NvmlReason(raw.NvmlState) : NeedsVendorGpuLibrary;
                    g.Readings.Add(Unavailable(p + "live", prefix + "Clocks, power, temperature", reason, c, ReadingConcern.Performance));
                }

                g.Readings.Add(ResizableBar(p + "rebar", prefix + "Resizable BAR", gpu, raw, isNvidia));
            }
            return g;
        }

        private static HardwareReading ResizableBar(string key, string label, GpuRaw gpu, HardwareRawSnapshot raw, bool isNvidia)
        {
            const HardwareComponent c = HardwareComponent.Gpu;
            if (!isNvidia)
            {
                if (IsIntegrated(gpu))
                    return NotApplicable(key, label, "Integrated graphics share system memory, so Resizable BAR doesn't apply.", c, ReadingConcern.Performance);
                return Unavailable(key, label, "Detection for AMD and Intel graphics cards isn't validated on real hardware yet, so it isn't shown.", c, ReadingConcern.Performance);
            }
            if (raw.FirmwareType == FirmwareKind.Legacy)
                return Measured(key, label, "Off", "GetFirmwareType()", c, "Resizable BAR needs UEFI boot mode.", ReadingConcern.Performance);
            if (UnsupportedNvidiaRebar(gpu.Name))
                return NotApplicable(key, label, "GeForce GTX and RTX 20-series GPUs don't support Resizable BAR.", c, ReadingConcern.Performance);

            var n = gpu.Nvidia;
            if (n?.Bar1TotalBytes > 0 && n.VramTotalBytes > 0)
            {
                var bar1 = n.Bar1TotalBytes.Value;
                var detail = $"BAR1 window {FormatMb(bar1)} vs {FormatGb(n.VramTotalBytes.Value)} VRAM.";
                if (bar1 >= n.VramTotalBytes.Value * 0.9)
                    return Measured(key, label, "On", "NVML nvmlDeviceGetBAR1MemoryInfo", c, detail, ReadingConcern.Performance);
                if (bar1 <= 300L * 1024 * 1024)
                    return Measured(key, label, "Off", "NVML nvmlDeviceGetBAR1MemoryInfo", c, detail, ReadingConcern.Performance);
                return Measured(key, label, "Partial", "NVML nvmlDeviceGetBAR1MemoryInfo", c, detail, ReadingConcern.Performance);
            }
            return Unavailable(key, label, n == null ? NvmlReason(raw.NvmlState) : "The driver didn't report the BAR1 size.", c, ReadingConcern.Performance);
        }

        // ---- findings ---------------------------------------------------------------------------

        private static IEnumerable<HardwareFinding> BuildFindings(HardwareRawSnapshot raw, HardwareReport report, MemoryProfileAnalysis memory, DateTime nowUtc)
        {
            switch (memory.Verdict)
            {
                case MemoryProfileVerdict.ProfileLikelyOn:
                    yield return Finding("find.mem_profile_on", FindingKind.Good, HardwareComponent.Memory,
                        "Memory is running at its fast profile speed", memory.Explanation,
                        "Nothing to do. If you ever see random crashes, the memory profile is one of the first things to test (turn it off, see if they stop).",
                        "mem.speed", "mem.profile");
                    break;
                case MemoryProfileVerdict.StandardSpeedCheckKit:
                    yield return Finding("find.mem_check_kit", FindingKind.Opportunity, HardwareComponent.Memory,
                        "Memory may be running slower than your kit allows", memory.Explanation,
                        "If the kit is rated faster, enable its profile in the BIOS: \"XMP\" on Intel boards, \"EXPO\" on AMD (ASUS calls it \"D.O.C.P.\", MSI \"A-XMP\" on older AMD boards). " +
                        "The profile is itself a mild memory overclock; if the machine becomes unstable, turn it back off. Performish doesn't change BIOS settings.",
                        "mem.speed", "mem.rated", "mem.profile");
                    break;
                case MemoryProfileVerdict.BelowModuleRating when !raw.IsLaptop:
                    yield return Finding("find.mem_below_rating", FindingKind.Opportunity, HardwareComponent.Memory,
                        "Memory is running below the modules' rating", memory.Explanation,
                        "Check the memory speed setting in the BIOS (often \"DRAM Frequency\" or \"Memory Frequency\"). Performish doesn't change BIOS settings.",
                        "mem.speed", "mem.rated");
                    break;
                case MemoryProfileVerdict.AtCpuSupportedMaximum:
                    yield return Finding("find.mem_cpu_limited", FindingKind.Good, HardwareComponent.Memory,
                        "Memory is at the CPU's supported maximum speed", memory.Explanation, "Nothing to do.", "mem.speed", "mem.cpu_max");
                    break;
            }

            var modules = (raw.MemoryModules ?? new List<MemoryModuleRaw>()).Where(m => m != null).ToList();
            if (modules.Count == 1 && !raw.IsLaptop)
                yield return Finding("find.mem_single_channel", FindingKind.Opportunity, HardwareComponent.Memory,
                    "Only one memory module installed",
                    "A single module runs in single-channel mode, which roughly halves memory bandwidth compared with two matched modules.",
                    "Adding a matching second module (in the slots your motherboard manual recommends for two modules) enables dual-channel.",
                    "mem.channels");

            var parts = modules.Select(m => Clean(m.PartNumber)).Where(p => p != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (parts.Count > 1)
                yield return Finding("find.mem_mixed", FindingKind.Info, HardwareComponent.Memory,
                    "Memory modules are different models",
                    $"Installed part numbers: {string.Join(", ", parts)}. Mixed modules usually work, but they run at the slowest common speed and are more likely to be unstable with a memory profile enabled.",
                    "Nothing to do unless you see instability.", "mem.total");

            var gpuGroup = report.Group(HardwareComponent.Gpu);
            foreach (var rebar in gpuGroup.Readings.Where(r => r.Key.EndsWith(".rebar") && r.Status == ReadingStatus.Measured))
            {
                if (rebar.Value == "Off")
                    yield return Finding("find.rebar_off." + rebar.Key, FindingKind.Opportunity, HardwareComponent.Gpu,
                        "Resizable BAR is off",
                        "This GPU supports Resizable BAR, but it isn't active. Some games run a few percent faster with it on; most are unaffected.",
                        "In the BIOS: switch boot mode to UEFI with CSM off, enable \"Above 4G Decoding\", then \"Re-Size BAR Support\". " +
                        "Some older GPUs also need a VBIOS update from the card maker. Performish doesn't change BIOS settings.",
                        rebar.Key);
                else if (rebar.Value == "On")
                    yield return Finding("find.rebar_on." + rebar.Key, FindingKind.Good, HardwareComponent.Gpu,
                        "Resizable BAR is on", "The GPU can access its full memory in one window.", "Nothing to do.", rebar.Key);
            }

            if (raw.BiosReleaseDate.HasValue && raw.BiosReleaseDate.Value.Year >= 1990 && raw.BiosReleaseDate.Value <= nowUtc
                && (nowUtc - raw.BiosReleaseDate.Value).TotalDays >= OldBiosDays)
            {
                var who = Clean(raw.SystemManufacturer) ?? Clean(raw.BoardManufacturer) ?? "your PC or motherboard maker";
                var model = Clean(raw.SystemModel) ?? Clean(raw.BoardProduct);
                yield return Finding("find.bios_old", FindingKind.Info, HardwareComponent.Firmware,
                    "BIOS is over three years old",
                    $"This BIOS was released {AgeText(raw.BiosReleaseDate.Value, nowUtc)}. Performish can't check whether a newer one exists; there's no vendor-neutral way to do that.",
                    $"Check {who}'s support site{(model != null ? $" for {model}" : "")}. BIOS updates can fix stability and security problems, but a failed update can stop the machine booting, so follow the vendor's instructions exactly. Performish never updates firmware.",
                    "fw.bios_date");
            }

            var virt = report.Find("fw.virtualization");
            if (virt != null && virt.Status == ReadingStatus.Measured && virt.Value == "Off")
                yield return Finding("find.virt_off", FindingKind.Info, HardwareComponent.Firmware,
                    "CPU virtualization is off in the firmware",
                    "It doesn't affect everyday speed, but WSL2, Windows Sandbox, Android apps and some games' anti-cheat need it.",
                    "If you need any of those, enable \"Intel Virtualization Technology\" / \"VT-x\" or \"SVM Mode\" (AMD) in the BIOS.",
                    "fw.virtualization");

            if (raw.HvciRunning == true || raw.VbsStatus == 2)
                yield return Finding("find.vbs_on", FindingKind.Info, HardwareComponent.Firmware,
                    "Virtualization-based security is running",
                    "VBS and Memory Integrity protect Windows against kernel-level malware. They can cost a few percent of performance in some games; the effect varies by CPU and title.",
                    "This is a security trade-off, so Performish only reports it. It's changed in Windows Security > Device security > Core isolation. Benchmark before and after if you decide to change it.",
                    "fw.vbs", "fw.hvci");

            if (raw.FirmwareType == FirmwareKind.Uefi && raw.SecureBootEnabled == false)
                yield return Finding("find.secure_boot_off", FindingKind.Info, HardwareComponent.Firmware,
                    "Secure Boot is off",
                    "It has no effect on performance, but some games' anti-cheat and Windows 11 security features require it.",
                    "It's enabled in the BIOS boot or security settings. Turning it off never makes a PC faster.",
                    "fw.secure_boot");
        }

        // ---- reading helpers --------------------------------------------------------------------

        private static HardwareReading Measured(string key, string label, string value, string source, HardwareComponent c,
            string note = null, ReadingConcern concern = ReadingConcern.Informational) =>
            new HardwareReading { Key = key, Label = label, Value = value, Status = ReadingStatus.Measured, Source = source, Note = note, Component = c, Concern = concern };

        private static HardwareReading Inferred(string key, string label, string value, string source, HardwareComponent c,
            string note, ReadingConcern concern = ReadingConcern.Informational) =>
            new HardwareReading { Key = key, Label = label, Value = value, Status = ReadingStatus.Inferred, Source = source, Note = note, Component = c, Concern = concern };

        private static HardwareReading Unavailable(string key, string label, string reason, HardwareComponent c,
            ReadingConcern concern = ReadingConcern.Informational) =>
            new HardwareReading { Key = key, Label = label, Status = ReadingStatus.Unavailable, Note = reason, Component = c, Concern = concern };

        private static HardwareReading NotApplicable(string key, string label, string reason, HardwareComponent c,
            ReadingConcern concern = ReadingConcern.Informational) =>
            new HardwareReading { Key = key, Label = label, Status = ReadingStatus.NotApplicable, Note = reason, Component = c, Concern = concern };

        private static HardwareReading Text(string key, string label, string value, string source, HardwareComponent c)
        {
            var v = Clean(value);
            return v != null ? Measured(key, label, v, source, c) : Unavailable(key, label, NotReportedByWindows, c);
        }

        private static HardwareReading OnOff(string key, string label, bool? value, string source, HardwareComponent c) =>
            value.HasValue ? Measured(key, label, value.Value ? "On" : "Off", source, c) : Unavailable(key, label, NotReportedByWindows, c);

        private static HardwareReading Mhz(string key, string label, int? mhz, string source, HardwareComponent c, ReadingConcern concern, string note) =>
            mhz is > 0 and < 20000
                ? Measured(key, label, $"{mhz} MHz", source, c, note, concern)
                : Unavailable(key, label, mhz.HasValue ? "The reported value wasn't plausible, so it isn't shown." : NotReportedByWindows, c, concern);

        private static HardwareReading Watts(string key, string label, double? watts, string source, HardwareComponent c) =>
            watts is > 0 and < 2000
                ? Measured(key, label, $"{watts:0} W", source, c, concern: ReadingConcern.Performance)
                : Unavailable(key, label, watts.HasValue ? "The reported value wasn't plausible, so it isn't shown." : "The driver didn't report this.", c, ReadingConcern.Performance);

        private static HardwareFinding Finding(string key, FindingKind kind, HardwareComponent c, string title, string detail, string guidance, params string[] readingKeys) =>
            new HardwareFinding { Key = key, Kind = kind, Component = c, Title = title, Detail = detail, Guidance = guidance, ReadingKeys = readingKeys.ToList() };

        private static string NvmlReason(NvmlState state) => state switch
        {
            NvmlState.LibraryNotFound => "The NVIDIA driver's monitoring library (nvml.dll) wasn't found. Updating the NVIDIA driver usually installs it.",
            NvmlState.InitFailed => "The NVIDIA driver's monitoring library didn't respond (driver problem, or a remote desktop session).",
            NvmlState.SkippedToAvoidWaking => "Not read, so the NVIDIA GPU isn't woken up. On switchable-graphics laptops it's usually asleep at idle. Use \"Read the NVIDIA GPU too\" to read it.",
            _ => "The NVIDIA driver didn't return readings for this GPU."
        };

        public static string Clean(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var t = s.Trim();
            return Placeholders.Contains(t.ToLowerInvariant()) ? null : t;
        }

        private static string Join(string a, string b)
        {
            if (a == null) return b;
            if (b == null) return a;
            return b.StartsWith(a, StringComparison.OrdinalIgnoreCase) ? b : $"{a} {b}";
        }

        private static bool IsNvidia(GpuRaw gpu) =>
            gpu.Nvidia != null
            || (gpu.Name ?? "").IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0
            || (gpu.AdapterCompatibility ?? "").IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsIntegrated(GpuRaw gpu)
        {
            var name = (gpu.Name ?? "").ToUpperInvariant();
            return name.Contains("UHD GRAPHICS") || name.Contains("IRIS") || name.Contains("HD GRAPHICS")
                || (name.Contains("RADEON") && name.Contains("GRAPHICS") && !name.Contains(" RX "))
                || name == "INTEL(R) GRAPHICS" || name.Contains("ARC GRAPHICS");
        }

        private static bool UnsupportedNvidiaRebar(string name)
        {
            var n = (name ?? "").ToUpperInvariant();
            return n.Contains("GTX") || n.Contains("RTX 20");
        }

        private static string FirstSpecVersion(string spec)
        {
            var first = Clean(spec)?.Split(',')[0].Trim();
            return string.IsNullOrEmpty(first) ? null : first;
        }

        private static string AgeText(DateTime date, DateTime now)
        {
            var days = (now - date).TotalDays;
            if (days < 60) return "less than two months ago";
            if (days < 365) return $"{Math.Round(days / 30.4)} months ago";
            var years = days / 365.25;
            return $"{years.ToString("0.#", CultureInfo.InvariantCulture)} years ago";
        }

        public static string FormatGb(long bytes) => $"{bytes / 1024.0 / 1024.0 / 1024.0:0.#} GB";
        private static string FormatMb(long bytes) => $"{bytes / 1024.0 / 1024.0:0} MB";
    }
}
