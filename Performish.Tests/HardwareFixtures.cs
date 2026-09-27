using System;
using System.Collections.Generic;
using Performish.Core.Hardware;

namespace Performish.Tests
{
    /// <summary>Hand-built raw hardware snapshots for the hardware-visibility tests. The laptop fixture
    /// mirrors a real Dell XPS 15 9500 read (i7-10750H, DDR4-3200 SO-DIMMs running at 2933, GTX 1650 Ti
    /// on switchable graphics); the desktop mirrors a Ryzen 7 7800X3D + RTX 4090 build.</summary>
    public static class HardwareFixtures
    {
        public static readonly DateTime Now = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);

        public static HardwareRawSnapshot Desktop(int configuredMts = 6000, long bar1Bytes = 32L * 1024 * 1024 * 1024) => new HardwareRawSnapshot
        {
            IsLaptop = false,
            BiosManufacturer = "American Megatrends International, LLC.",
            BiosVersion = "3040",
            BiosReleaseDate = new DateTime(2025, 11, 3, 0, 0, 0, DateTimeKind.Utc),
            FirmwareType = FirmwareKind.Uefi,
            SecureBootEnabled = true,
            TpmPresent = true,
            TpmEnabled = true,
            TpmSpecVersion = "2.0, 0, 1.59",
            VirtualizationFirmwareEnabled = true,
            HypervisorPresent = false,
            VbsStatus = 0,
            HvciRunning = false,
            SystemManufacturer = "System manufacturer",
            SystemModel = "System Product Name",
            BoardManufacturer = "ASUSTeK COMPUTER INC.",
            BoardProduct = "ROG STRIX X670E-E GAMING WIFI",
            ChipsetDescription = null,
            CpuName = "AMD Ryzen 7 7800X3D 8-Core Processor",
            CpuManufacturer = "AuthenticAMD",
            CpuCores = 8,
            CpuThreads = 16,
            CpuBaseClockMhz = 4201,
            CpuEffectiveClockMhz = 4750,
            MemoryModules = new List<MemoryModuleRaw>
            {
                new MemoryModuleRaw { Manufacturer = "G Skill Intl", PartNumber = "F5-6000J3038F16G", CapacityBytes = 16L << 30, RatedSpeedMts = 4800, ConfiguredSpeedMts = configuredMts, SmbiosMemoryType = 34, FormFactor = 8, DeviceLocator = "DIMM_A2", BankLabel = "BANK 1" },
                new MemoryModuleRaw { Manufacturer = "G Skill Intl", PartNumber = "F5-6000J3038F16G", CapacityBytes = 16L << 30, RatedSpeedMts = 4800, ConfiguredSpeedMts = configuredMts, SmbiosMemoryType = 34, FormFactor = 8, DeviceLocator = "DIMM_B2", BankLabel = "BANK 3" },
            },
            Gpus = new List<GpuRaw>
            {
                new GpuRaw
                {
                    Name = "NVIDIA GeForce RTX 4090", AdapterCompatibility = "NVIDIA", DriverVersion = "32.0.15.8157", DedicatedMemoryBytes = 24L << 30,
                    Nvidia = new NvidiaTelemetry { Name = "NVIDIA GeForce RTX 4090", CoreClockMhz = 210, MaxCoreClockMhz = 3120, MemoryClockMhz = 405, PowerDrawWatts = 21.4, PowerLimitWatts = 450, TemperatureC = 38, UtilizationPercent = 2, VramTotalBytes = 24L << 30, Bar1TotalBytes = bar1Bytes }
                }
            },
            TotalGpu3DUtilizationPercent = 1.5,
            NvmlState = NvmlState.Ok
        };

        public static HardwareRawSnapshot XpsLaptop(NvmlState nvml = NvmlState.SkippedToAvoidWaking) => new HardwareRawSnapshot
        {
            IsLaptop = true,
            BiosManufacturer = "Dell Inc.",
            BiosVersion = "1.24.1",
            BiosReleaseDate = new DateTime(2023, 8, 15, 0, 0, 0, DateTimeKind.Utc),
            FirmwareType = FirmwareKind.Uefi,
            SecureBootEnabled = true,
            TpmAccessDenied = true,
            VirtualizationFirmwareEnabled = false,
            HypervisorPresent = true,
            VbsStatus = 2,
            HvciRunning = true,
            SystemManufacturer = "Dell Inc.",
            SystemModel = "XPS 15 9500",
            BoardManufacturer = "Dell Inc.",
            BoardProduct = "0RHXRG",
            ChipsetDescription = "Intel(R) 400 Series Chipset Family LPC Controller (HM470) - 068D",
            CpuName = "Intel(R) Core(TM) i7-10750H CPU @ 2.60GHz",
            CpuManufacturer = "GenuineIntel",
            CpuCores = 6,
            CpuThreads = 12,
            CpuBaseClockMhz = 2592,
            CpuEffectiveClockMhz = 3900,
            MemoryModules = new List<MemoryModuleRaw>
            {
                new MemoryModuleRaw { Manufacturer = "80CE000000000000", PartNumber = "M471A4G43BB1-CWE    ", CapacityBytes = 34359738368, RatedSpeedMts = 3200, ConfiguredSpeedMts = 2933, SmbiosMemoryType = 26, FormFactor = 12, DeviceLocator = "DIMM A", BankLabel = "BANK 0" },
                new MemoryModuleRaw { Manufacturer = "80AD000000000000", PartNumber = "HMAA4GS6AJR8N-XN    ", CapacityBytes = 34359738368, RatedSpeedMts = 3200, ConfiguredSpeedMts = 2933, SmbiosMemoryType = 26, FormFactor = 12, DeviceLocator = "DIMM B", BankLabel = "BANK 2" },
            },
            Gpus = new List<GpuRaw>
            {
                new GpuRaw { Name = "Intel(R) UHD Graphics", AdapterCompatibility = "Intel Corporation", DriverVersion = "31.0.101.2137" },
                new GpuRaw { Name = "NVIDIA GeForce GTX 1650 Ti", AdapterCompatibility = "NVIDIA", DriverVersion = "32.0.16.1088", DedicatedMemoryBytes = 4L << 30 },
            },
            TotalGpu3DUtilizationPercent = 3,
            NvmlState = nvml
        };

        /// <summary>Every field missing - a machine (or sandbox) where Windows reports nothing.</summary>
        public static HardwareRawSnapshot Empty() => new HardwareRawSnapshot();

        /// <summary>Some fields present, some missing - the most common real-world shape.</summary>
        public static HardwareRawSnapshot Partial() => new HardwareRawSnapshot
        {
            BiosManufacturer = "Dell Inc.",
            BiosVersion = null,
            FirmwareType = FirmwareKind.Uefi,
            CpuName = "Intel(R) Core(TM) i5-8250U CPU @ 1.60GHz",
            CpuCores = 4,
            CpuThreads = 8,
            MemoryModules = new List<MemoryModuleRaw> { new MemoryModuleRaw { CapacityBytes = 8L << 30 } },
            Gpus = new List<GpuRaw> { new GpuRaw { Name = "Intel(R) UHD Graphics 620" } },
        };

        /// <summary>Implausible, placeholder and garbage values Windows really does return on some boards.</summary>
        public static HardwareRawSnapshot Unusual() => new HardwareRawSnapshot
        {
            BiosManufacturer = "   ",
            BiosVersion = "Default string",
            BiosReleaseDate = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            FirmwareType = FirmwareKind.Legacy,
            SecureBootEnabled = false,
            SystemManufacturer = "To Be Filled By O.E.M.",
            SystemModel = "To Be Filled By O.E.M.",
            BoardManufacturer = "",
            BoardProduct = "Default string",
            CpuName = "  ",
            CpuCores = 0,
            CpuThreads = -1,
            CpuBaseClockMhz = 0,
            CpuEffectiveClockMhz = 999999,
            MemoryModules = new List<MemoryModuleRaw>
            {
                new MemoryModuleRaw { Manufacturer = "0000000000000000", PartNumber = "   ", CapacityBytes = -5, RatedSpeedMts = 0, ConfiguredSpeedMts = 0, SmbiosMemoryType = 99 },
                null
            },
            Gpus = new List<GpuRaw>
            {
                new GpuRaw
                {
                    Name = "NVIDIA GeForce RTX 3080", AdapterCompatibility = "NVIDIA",
                    Nvidia = new NvidiaTelemetry { CoreClockMhz = -1, PowerDrawWatts = 0, PowerLimitWatts = 99999, TemperatureC = 255, UtilizationPercent = 101 }
                },
                null
            },
            TotalGpu3DUtilizationPercent = 340,
            NvmlState = NvmlState.Ok
        };
    }
}
