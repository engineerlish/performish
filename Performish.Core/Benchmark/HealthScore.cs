using System;
using System.Collections.Generic;
using System.Linq;
using Performish.Core.Scanner;

namespace Performish.Core.Benchmark
{
    public sealed class HealthScoreFactor
    {
        public string Name { get; set; }
        public int Points { get; set; }
        public int MaxPoints { get; set; }
        public string Detail { get; set; }
    }

    public sealed class HealthScoreResult
    {
        public int Score { get; set; } // 0-100
        public List<HealthScoreFactor> Factors { get; set; } = new List<HealthScoreFactor>();
    }

    /// <summary>A 0-100 "is this machine in good shape" score computed ONLY from numbers the scanner
    /// already measures - no estimate, no guess, nothing that isn't a real read of this machine right
    /// now (task requirement: "the health score may only use measurable inputs"). Every factor's
    /// weight and thresholds are named constants here, not hidden magic numbers, so the score is
    /// auditable and each factor is independently testable.</summary>
    public static class HealthScore
    {
        private const int StartupItemsMax = 20;
        private const int DiskSpaceMax = 20;
        private const int UptimeMax = 15;
        private const int ProcessCountMax = 15;
        private const int ScheduledTasksMax = 15;
        private const int ServicesRunningMax = 15;
        public const int TotalMax = StartupItemsMax + DiskSpaceMax + UptimeMax + ProcessCountMax + ScheduledTasksMax + ServicesRunningMax;

        public static HealthScoreResult Compute(SystemSnapshot s)
        {
            var factors = new List<HealthScoreFactor>
            {
                ScoreStartupItems(s),
                ScoreDiskSpace(s),
                ScoreUptime(s),
                ScoreProcessCount(s),
                ScoreScheduledTasks(s),
                ScoreRunningServices(s)
            };

            return new HealthScoreResult
            {
                Score = factors.Sum(f => f.Points),
                Factors = factors
            };
        }

        private static HealthScoreFactor ScoreStartupItems(SystemSnapshot s)
        {
            // 0 items = full marks, 20+ items = zero - linear in between. 20 was chosen as the point
            // past which a Run-key startup list is unambiguously bloated on a typical Windows 11
            // install (a handful of first-party entries is normal; dozens is not).
            var count = s.StartupItems.Count;
            var points = Math.Max(0, StartupItemsMax - (int)Math.Round(count * (StartupItemsMax / 20.0)));
            return new HealthScoreFactor { Name = "Startup items", Points = points, MaxPoints = StartupItemsMax, Detail = $"{count} startup item(s)" };
        }

        private static HealthScoreFactor ScoreDiskSpace(SystemSnapshot s)
        {
            if (s.TotalDiskSpaceBytes <= 0)
                return new HealthScoreFactor { Name = "Free disk space", Points = 0, MaxPoints = DiskSpaceMax, Detail = "unknown" };

            var freePct = (double)s.FreeDiskSpaceBytes / s.TotalDiskSpaceBytes * 100.0;
            // Below 5% free = 0, at or above 20% free = full marks - Windows itself starts warning
            // around the 5-10% mark, and update/temp headroom problems concentrate below 20%.
            var points = (int)Math.Round(Math.Clamp((freePct - 5.0) / 15.0, 0.0, 1.0) * DiskSpaceMax);
            return new HealthScoreFactor { Name = "Free disk space", Points = points, MaxPoints = DiskSpaceMax, Detail = $"{freePct:0.0}% free" };
        }

        private static HealthScoreFactor ScoreUptime(SystemSnapshot s)
        {
            // Full marks under 48h uptime, zero at 14+ days - long uptimes correlate with accumulated
            // memory/handle bloat and deferred updates, not a hard rule but a reasonable proxy.
            var hours = s.UptimeHours;
            var points = (int)Math.Round(Math.Clamp(1.0 - (hours - 48.0) / (336.0 - 48.0), 0.0, 1.0) * UptimeMax);
            return new HealthScoreFactor { Name = "Uptime", Points = points, MaxPoints = UptimeMax, Detail = $"{hours:0.0} hour(s)" };
        }

        private static HealthScoreFactor ScoreProcessCount(SystemSnapshot s)
        {
            // Full marks at or under 120 processes (a clean, lightly-loaded Windows 11 desktop is
            // commonly in the 100-150 range), zero at 300+.
            var count = s.ProcessCount;
            var points = (int)Math.Round(Math.Clamp(1.0 - (count - 120.0) / (300.0 - 120.0), 0.0, 1.0) * ProcessCountMax);
            return new HealthScoreFactor { Name = "Process count", Points = points, MaxPoints = ProcessCountMax, Detail = $"{count} process(es)" };
        }

        private static HealthScoreFactor ScoreScheduledTasks(SystemSnapshot s)
        {
            var enabledCount = s.ScheduledTasks.Count(t => t.Enabled);
            // Full marks under 40 enabled tasks, zero at 100+ - most of a default Windows 11 task
            // library is disabled/conditional; a large enabled count usually means OEM/third-party
            // bloat, not built-in Windows maintenance tasks.
            var points = (int)Math.Round(Math.Clamp(1.0 - (enabledCount - 40.0) / (100.0 - 40.0), 0.0, 1.0) * ScheduledTasksMax);
            return new HealthScoreFactor { Name = "Enabled scheduled tasks", Points = points, MaxPoints = ScheduledTasksMax, Detail = $"{enabledCount} enabled" };
        }

        private static HealthScoreFactor ScoreRunningServices(SystemSnapshot s)
        {
            var runningCount = s.Services.Count(sv => sv.Running);
            // Full marks under 90 running services, zero at 180+ - a stock Windows 11 install
            // typically runs on the order of 100-130; a much higher count usually means third-party
            // agents/bloat rather than anything Windows itself needs.
            var points = (int)Math.Round(Math.Clamp(1.0 - (runningCount - 90.0) / (180.0 - 90.0), 0.0, 1.0) * ServicesRunningMax);
            return new HealthScoreFactor { Name = "Running services", Points = points, MaxPoints = ServicesRunningMax, Detail = $"{runningCount} running" };
        }
    }
}
