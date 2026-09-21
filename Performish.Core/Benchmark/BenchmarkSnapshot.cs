using System;
using Performish.Core.Scanner;

namespace Performish.Core.Benchmark
{
    /// <summary>Cheap, always-available "before/after" numbers - no external tool required. Built
    /// directly from a SystemSnapshot plus a couple of process-table facts, so it costs nothing to
    /// take one before and one after a tweak batch.</summary>
    public sealed class BenchmarkSnapshot
    {
        public DateTime TakenAtUtc { get; set; }
        public long UsedRamBytes { get; set; }
        public int ProcessCount { get; set; }
        public int RunningServiceCount { get; set; }
        public int EnabledStartupItemCount { get; set; }
        public int EnabledScheduledTaskCount { get; set; }
        /// <summary>Rough proxy for boot-time impact: how many things Windows has to start at logon.
        /// Not a measured boot time - see BenchmarkReport for why an actual reboot timer isn't used.</summary>
        public int BootImpactItemCount => EnabledStartupItemCount + EnabledScheduledTaskCount;

        public static BenchmarkSnapshot FromSystemSnapshot(SystemSnapshot snapshot)
        {
            var runningServices = 0;
            foreach (var s in snapshot.Services) if (s.Running) runningServices++;

            var enabledTasks = 0;
            foreach (var t in snapshot.ScheduledTasks) if (t.Enabled) enabledTasks++;

            return new BenchmarkSnapshot
            {
                TakenAtUtc = snapshot.TakenAtUtc,
                UsedRamBytes = snapshot.UsedRamBytes,
                ProcessCount = snapshot.ProcessCount,
                RunningServiceCount = runningServices,
                EnabledStartupItemCount = snapshot.StartupItems.Count,
                EnabledScheduledTaskCount = enabledTasks
            };
        }
    }

    public sealed class BenchmarkComparison
    {
        public BenchmarkSnapshot Before { get; set; }
        public BenchmarkSnapshot After { get; set; }

        public long UsedRamDeltaBytes => After.UsedRamBytes - Before.UsedRamBytes;
        public int ProcessCountDelta => After.ProcessCount - Before.ProcessCount;
        public int RunningServiceCountDelta => After.RunningServiceCount - Before.RunningServiceCount;
        public int BootImpactItemCountDelta => After.BootImpactItemCount - Before.BootImpactItemCount;
    }
}
