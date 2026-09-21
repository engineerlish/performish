using System.Linq;
using System.Threading.Tasks;
using Performish.Core.Backends;
using Performish.Core.Compatibility;

namespace Performish.Core.Scanner
{
    /// <summary>Turns raw ISystemInfoBackend/IPowerBackend reads into one SystemSnapshot. All the
    /// "what counts as Windows 11", "what counts as a laptop" classification logic lives here so it
    /// can be unit tested against a FakeSystemInfoBackend independent of the real WMI/registry
    /// plumbing.</summary>
    public sealed class SystemScanner
    {
        // Windows 11's first public build is 22000; anything below is Windows 10 even though the
        // registry's ProductName string can lag behind on some upgrade paths.
        private const int Windows11MinBuild = 22000;

        private readonly ISystemInfoBackend _info;
        private readonly IPowerBackend _power;

        public SystemScanner(ISystemInfoBackend info, IPowerBackend power)
        {
            _info = info;
            _power = power;
        }

        /// <summary>Every one of these calls is an independent read (a separate WMI class, registry
        /// key, or process spawn) with no shared state and no ordering dependency on any other, so
        /// they run on the thread pool in parallel instead of one after another - Phase 5 "run
        /// independent scan queries in parallel where safe". Composing the SystemSnapshot from the
        /// results is still done on the calling thread, after every task has completed.</summary>
        public SystemSnapshot Scan()
        {
            var osInfoTask = Task.Run(() => _info.GetOsInfo());
            var domainJoinedTask = Task.Run(() => _info.IsDomainJoined());
            var mdmManagedTask = Task.Run(() => _info.IsMdmManaged());
            var cpuInfoTask = Task.Run(() => _info.GetCpuInfo());
            var logicalProcessorsTask = Task.Run(() => _info.GetCpuLogicalProcessorCount());
            var gpusTask = Task.Run(() => _info.GetGpus());
            var memoryTask = Task.Run(() => _info.GetMemory());
            var ssdTask = Task.Run(() => _info.SystemDriveIsSsd());
            var laptopTask = Task.Run(() => _info.IsLaptop());
            var startupItemsTask = Task.Run(() => _info.GetStartupItems());
            var scheduledTasksTask = Task.Run(() => _info.GetScheduledTasks());
            var servicesTask = Task.Run(() => _info.GetServices());
            var processCountTask = Task.Run(() => _info.GetProcessCount());
            var diskSpaceTask = Task.Run(() => _info.GetSystemDriveSpace());
            var uptimeTask = Task.Run(() => _info.GetUptimeHours());
            var processNamesTask = Task.Run(() => _info.GetRunningProcessNames());
            // One ListPlans() call, not ListPlans() + a separate GetActivePlanGuid() (which is itself
            // just ListPlans().FirstOrDefault(IsActive) under the hood) - avoids a redundant
            // powercfg.exe spawn on top of RealPowerBackend's own instance-level cache.
            var powerPlansTask = Task.Run(() => _power.ListPlans());

            Task.WaitAll(osInfoTask, domainJoinedTask, mdmManagedTask, cpuInfoTask, logicalProcessorsTask,
                gpusTask, memoryTask, ssdTask, laptopTask, startupItemsTask, scheduledTasksTask, servicesTask,
                processCountTask, diskSpaceTask, uptimeTask, processNamesTask, powerPlansTask);

            var (productName, editionId, build, ubr) = osInfoTask.Result;
            var (total, available) = memoryTask.Result;
            var (cpuName, cpuCores) = cpuInfoTask.Result;
            var (freeDisk, totalDisk) = diskSpaceTask.Result;
            var activePlanName = powerPlansTask.Result.FirstOrDefault(p => p.IsActive)?.Name ?? "Unknown";
            var detectedAgents = KnownAgents.Detect(processNamesTask.Result).Select(a => a.DisplayName).ToList();

            return new SystemSnapshot
            {
                WindowsProductName = productName,
                WindowsEdition = editionId,
                BuildNumber = build,
                UpdateBuildRevision = ubr,
                IsWindows11 = build >= Windows11MinBuild,
                IsDomainJoined = domainJoinedTask.Result,
                IsMdmManaged = mdmManagedTask.Result,
                CpuName = cpuName,
                CpuCoreCount = cpuCores,
                CpuLogicalProcessorCount = logicalProcessorsTask.Result,
                Gpus = gpusTask.Result,
                TotalRamBytes = total,
                AvailableRamBytes = available,
                SystemDriveIsSsd = ssdTask.Result,
                ActivePowerPlanName = activePlanName,
                IsLaptop = laptopTask.Result,
                StartupItems = startupItemsTask.Result,
                ScheduledTasks = scheduledTasksTask.Result,
                Services = servicesTask.Result,
                ProcessCount = processCountTask.Result,
                FreeDiskSpaceBytes = freeDisk,
                TotalDiskSpaceBytes = totalDisk,
                UptimeHours = uptimeTask.Result,
                DetectedManagementAgents = detectedAgents
            };
        }
    }
}
