using System;
using System.Collections.Generic;

namespace Performish.Core.Hardware
{
    public enum FirmwareKind
    {
        Unknown,
        Legacy,
        Uefi
    }

    public enum NvmlState
    {
        /// <summary>No NVIDIA GPU, so NVML was never loaded.</summary>
        NotAttempted,
        /// <summary>An NVIDIA GPU exists but nvml.dll (installed with the NVIDIA driver) wasn't found.</summary>
        LibraryNotFound,
        /// <summary>nvml.dll loaded but nvmlInit failed (driver problem, remote session, ...).</summary>
        InitFailed,
        /// <summary>Switchable-graphics laptop: deliberately not read so the GPU isn't woken.</summary>
        SkippedToAvoidWaking,
        Ok
    }

    /// <summary>Raw, unclassified facts straight from Windows/the GPU driver - every field nullable,
    /// because any one of them can be missing on a real machine. HardwareReportBuilder turns this into
    /// display readings and findings; keeping the two apart makes all interpretation unit-testable
    /// against hand-built snapshots, same split as ISystemInfoBackend/SystemScanner.</summary>
    public sealed class HardwareRawSnapshot
    {
        public bool IsLaptop { get; set; }

        public string BiosManufacturer { get; set; }
        public string BiosVersion { get; set; }
        public DateTime? BiosReleaseDate { get; set; }
        public FirmwareKind FirmwareType { get; set; }

        public bool? SecureBootEnabled { get; set; }

        /// <summary>True when the TPM query was refused (it needs administrator rights).</summary>
        public bool TpmAccessDenied { get; set; }
        public bool? TpmPresent { get; set; }
        public bool? TpmEnabled { get; set; }
        public string TpmSpecVersion { get; set; }

        public bool? VirtualizationFirmwareEnabled { get; set; }
        public bool? HypervisorPresent { get; set; }
        /// <summary>Win32_DeviceGuard.VirtualizationBasedSecurityStatus: 0 off, 1 enabled not running, 2 running.</summary>
        public int? VbsStatus { get; set; }
        public bool? HvciRunning { get; set; }

        public string SystemManufacturer { get; set; }
        public string SystemModel { get; set; }
        public string BoardManufacturer { get; set; }
        public string BoardProduct { get; set; }
        /// <summary>Name of the chipset's LPC/eSPI bridge as Windows lists it, e.g. "Intel(R) 400 Series
        /// Chipset Family LPC Controller (HM470) - 068D". Null when not found (common on AMD).</summary>
        public string ChipsetDescription { get; set; }

        public string CpuName { get; set; }
        public string CpuManufacturer { get; set; }
        public int? CpuCores { get; set; }
        public int? CpuThreads { get; set; }
        /// <summary>Win32_Processor.MaxClockSpeed - despite the name, normally the base clock.</summary>
        public int? CpuBaseClockMhz { get; set; }
        /// <summary>Processor frequency x % Processor Performance, the figure Task Manager shows as "Speed".</summary>
        public int? CpuEffectiveClockMhz { get; set; }

        public List<MemoryModuleRaw> MemoryModules { get; set; } = new List<MemoryModuleRaw>();

        public List<GpuRaw> Gpus { get; set; } = new List<GpuRaw>();
        /// <summary>Sum of every GPU's 3D-engine utilization from Windows' GPU performance counters.</summary>
        public double? TotalGpu3DUtilizationPercent { get; set; }
        public NvmlState NvmlState { get; set; }
    }

    public sealed class MemoryModuleRaw
    {
        /// <summary>Win32_PhysicalMemory.Manufacturer - often a raw JEDEC code such as "80CE000000000000".</summary>
        public string Manufacturer { get; set; }
        public string PartNumber { get; set; }
        public long? CapacityBytes { get; set; }
        /// <summary>Win32_PhysicalMemory.Speed (MT/s) - usually the JEDEC SPD rating, not an XMP/EXPO rating.</summary>
        public int? RatedSpeedMts { get; set; }
        public int? ConfiguredSpeedMts { get; set; }
        /// <summary>SMBIOS memory type: 24 DDR3, 26 DDR4, 34 DDR5, 30/35 LPDDR4/LPDDR5.</summary>
        public int? SmbiosMemoryType { get; set; }
        /// <summary>8 = DIMM, 12 = SODIMM.</summary>
        public int? FormFactor { get; set; }
        public string DeviceLocator { get; set; }
        public string BankLabel { get; set; }
    }

    public sealed class GpuRaw
    {
        public string Name { get; set; }
        public string AdapterCompatibility { get; set; }
        public string DriverVersion { get; set; }
        public long? DedicatedMemoryBytes { get; set; }
        /// <summary>Present only for NVIDIA GPUs that NVML read successfully.</summary>
        public NvidiaTelemetry Nvidia { get; set; }
    }

    public sealed class NvidiaTelemetry
    {
        public string Name { get; set; }
        public int? CoreClockMhz { get; set; }
        public int? MaxCoreClockMhz { get; set; }
        public int? MemoryClockMhz { get; set; }
        public double? PowerDrawWatts { get; set; }
        public double? PowerLimitWatts { get; set; }
        public int? TemperatureC { get; set; }
        public int? UtilizationPercent { get; set; }
        public long? VramTotalBytes { get; set; }
        public long? Bar1TotalBytes { get; set; }
    }
}
