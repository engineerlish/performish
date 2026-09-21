using System.Collections.Generic;
using Performish.Core.Benchmark;
using Performish.Core.Scanner;
using Xunit;

namespace Performish.Tests
{
    public class HealthScoreTests
    {
        private static SystemSnapshot PristineSnapshot() => new SystemSnapshot
        {
            StartupItems = new List<StartupItemInfo>(),
            FreeDiskSpaceBytes = 400L * 1024 * 1024 * 1024,
            TotalDiskSpaceBytes = 500L * 1024 * 1024 * 1024, // 80% free
            UptimeHours = 2,
            ProcessCount = 90,
            ScheduledTasks = new List<ScheduledTaskInfo>(),
            Services = new List<ServiceInfo>()
        };

        [Fact]
        public void PristineMachine_ScoresAtOrNearMaximum()
        {
            var result = HealthScore.Compute(PristineSnapshot());
            Assert.Equal(HealthScore.TotalMax, result.Score);
        }

        [Fact]
        public void Score_NeverExceedsTotalMax_EvenForAnImpossiblyCleanMachine()
        {
            var s = PristineSnapshot();
            s.FreeDiskSpaceBytes = s.TotalDiskSpaceBytes; // 100% free
            s.UptimeHours = 0;
            s.ProcessCount = 0;

            var result = HealthScore.Compute(s);
            Assert.True(result.Score <= HealthScore.TotalMax);
        }

        [Fact]
        public void ManyStartupItems_LowersScore()
        {
            var clean = HealthScore.Compute(PristineSnapshot());

            var bloated = PristineSnapshot();
            for (var i = 0; i < 30; i++) bloated.StartupItems.Add(new StartupItemInfo { Name = "x" + i });

            var result = HealthScore.Compute(bloated);
            Assert.True(result.Score < clean.Score);
        }

        [Fact]
        public void LowFreeDiskSpace_LowersScore()
        {
            var clean = HealthScore.Compute(PristineSnapshot());

            var full = PristineSnapshot();
            full.FreeDiskSpaceBytes = 10L * 1024 * 1024 * 1024; // 2% free of 500GB

            var result = HealthScore.Compute(full);
            Assert.True(result.Score < clean.Score);
        }

        [Fact]
        public void ZeroTotalDiskSpace_DoesNotThrow_ScoresDiskFactorZero()
        {
            var s = PristineSnapshot();
            s.TotalDiskSpaceBytes = 0;
            s.FreeDiskSpaceBytes = 0;

            var result = HealthScore.Compute(s);
            var diskFactor = result.Factors.Find(f => f.Name == "Free disk space");
            Assert.NotNull(diskFactor);
            Assert.Equal(0, diskFactor.Points);
        }

        [Fact]
        public void LongUptime_LowersScore()
        {
            var clean = HealthScore.Compute(PristineSnapshot());

            var stale = PristineSnapshot();
            stale.UptimeHours = 500; // ~20 days

            var result = HealthScore.Compute(stale);
            Assert.True(result.Score < clean.Score);
        }

        [Fact]
        public void ManyRunningServices_LowersScore()
        {
            var clean = HealthScore.Compute(PristineSnapshot());

            var bloated = PristineSnapshot();
            for (var i = 0; i < 200; i++) bloated.Services.Add(new ServiceInfo { Name = "svc" + i, Running = true });

            var result = HealthScore.Compute(bloated);
            Assert.True(result.Score < clean.Score);
        }

        [Fact]
        public void FactorPoints_NeverNegative()
        {
            var s = PristineSnapshot();
            s.StartupItems.Clear();
            for (var i = 0; i < 1000; i++) s.StartupItems.Add(new StartupItemInfo { Name = "x" + i });
            s.ProcessCount = 100000;
            s.UptimeHours = 999999;

            var result = HealthScore.Compute(s);
            Assert.All(result.Factors, f => Assert.True(f.Points >= 0));
        }
    }
}
