using System;
using System.Collections.Generic;

namespace Performish.Core.Scanner
{
    public sealed class GpuInfo
    {
        public string Name { get; set; }
        public string Vendor { get; set; } // NVIDIA / AMD / Intel / Unknown
        public string DriverVersion { get; set; }
    }

    public sealed class StartupItemInfo
    {
        public string Name { get; set; }
        public string Command { get; set; }
        public string Source { get; set; } // e.g. "HKCU Run", "Startup folder"
    }

    public sealed class ScheduledTaskInfo
    {
        public string Path { get; set; }
        public bool Enabled { get; set; }
    }

    public sealed class ServiceInfo
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string StartMode { get; set; }
        public bool Running { get; set; }
    }

    /// <summary>Read-only snapshot of the machine's relevant state - the output of a scan, and the
    /// "before" half of a before/after benchmark comparison. Building one never writes anything.</summary>
    public sealed class SystemSnapshot
    {
        public DateTime TakenAtUtc { get; set; } = DateTime.UtcNow;

        public string WindowsProductName { get; set; }
        public string WindowsEdition { get; set; }
        public int BuildNumber { get; set; }
        public int UpdateBuildRevision { get; set; }
        public bool IsWindows11 { get; set; }
        public bool IsDomainJoined { get; set; }
        public bool IsMdmManaged { get; set; }

        public string CpuName { get; set; }
        public int CpuCoreCount { get; set; }
        public int CpuLogicalProcessorCount { get; set; }

        public List<GpuInfo> Gpus { get; set; } = new List<GpuInfo>();

        public long TotalRamBytes { get; set; }
        public long AvailableRamBytes { get; set; }

        public bool SystemDriveIsSsd { get; set; }
        public string ActivePowerPlanName { get; set; }
        public bool IsLaptop { get; set; }

        public List<StartupItemInfo> StartupItems { get; set; } = new List<StartupItemInfo>();
        public List<ScheduledTaskInfo> ScheduledTasks { get; set; } = new List<ScheduledTaskInfo>();
        public List<ServiceInfo> Services { get; set; } = new List<ServiceInfo>();

        public int ProcessCount { get; set; }

        /// <summary>System (C:) drive free/total space, in bytes - cheap (no WMI, just
        /// System.IO.DriveInfo) and read-only. Feeds the health score and the Maintenance tweaks'
        /// "space that could be freed" framing.</summary>
        public long FreeDiskSpaceBytes { get; set; }
        public long TotalDiskSpaceBytes { get; set; }

        /// <summary>Time since last boot - a cheap, honest proxy for "has this machine been running
        /// a long time without a fresh start", one of the health score's measurable inputs.</summary>
        public double UptimeHours { get; set; }

        /// <summary>Running process names that matched a known VPN/RMM/endpoint-security agent -
        /// see Performish.Core.Compatibility.KnownAgents. Empty list means none detected, never
        /// null. Detection is informational only - it changes nothing and blocks nothing by itself.</summary>
        public List<string> DetectedManagementAgents { get; set; } = new List<string>();

        public long UsedRamBytes => TotalRamBytes - AvailableRamBytes;
        public long UsedDiskSpaceBytes => TotalDiskSpaceBytes - FreeDiskSpaceBytes;
    }
}
