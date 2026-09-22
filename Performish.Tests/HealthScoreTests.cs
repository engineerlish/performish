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
            ScheduledTasks = new List<ScheduledTaskInfo>(),
            Services = new List<ServiceInfo>()
        };

        [Fact]
        public void PristineMachine_ScoresAtOrNearMaximum()
        {
            var result = HealthScore.Compute(PristineSnapshot());
            Assert.Equal(100, result.Score);
        }

        [Fact]
        public void Score_NeverExceeds100_EvenForAnImpossiblyCleanMachine()
        {
            var s = PristineSnapshot();
            s.FreeDiskSpaceBytes = s.TotalDiskSpaceBytes; // 100% free
            s.UptimeHours = 0;

            var result = HealthScore.Compute(s);
            Assert.True(result.Score <= 100);
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
        public void FactorPoints_NeverNegative()
        {
            var s = PristineSnapshot();
            s.StartupItems.Clear();
            for (var i = 0; i < 1000; i++) s.StartupItems.Add(new StartupItemInfo { Name = "x" + i });
            s.UptimeHours = 999999;
            for (var i = 0; i < 500; i++)
                s.ScheduledTasks.Add(new ScheduledTaskInfo { Path = $@"\SomeVendor\Task{i}", Enabled = true });
            for (var i = 0; i < 500; i++)
                s.Services.Add(new ServiceInfo { Name = "svc" + i, Running = true, BinaryPath = $@"C:\Program Files\Vendor{i}\svc.exe" });

            var result = HealthScore.Compute(s);
            Assert.All(result.Factors, f => Assert.True(f.Points >= 0));
        }

        // ---- Regression coverage for the accuracy audit (see DECISIONS.md "Health score accuracy
        // audit"): the raw "all enabled tasks"/"all running services" counts were dominated by
        // Windows's own baseline on every real machine, so no install could ever score well on them.
        // These tests lock in the fix: only third-party tasks/services should move the score. ---------

        [Fact]
        public void WindowsOwnedScheduledTasks_NeverLowerTheScore_NoMatterHowMany()
        {
            var clean = HealthScore.Compute(PristineSnapshot());

            var s = PristineSnapshot();
            for (var i = 0; i < 300; i++)
                s.ScheduledTasks.Add(new ScheduledTaskInfo { Path = $@"\Microsoft\Windows\SomeArea\Task{i}", Enabled = true });

            var result = HealthScore.Compute(s);
            Assert.Equal(clean.Score, result.Score);
        }

        [Fact]
        public void ThirdPartyScheduledTasks_LowerTheScore()
        {
            var clean = HealthScore.Compute(PristineSnapshot());

            var s = PristineSnapshot();
            for (var i = 0; i < 40; i++)
                s.ScheduledTasks.Add(new ScheduledTaskInfo { Path = $@"\SomeVendor\UpdateTask{i}", Enabled = true });

            var result = HealthScore.Compute(s);
            Assert.True(result.Score < clean.Score);
        }

        [Fact]
        public void DisabledThirdPartyScheduledTasks_DoNotCount()
        {
            var clean = HealthScore.Compute(PristineSnapshot());

            var s = PristineSnapshot();
            for (var i = 0; i < 40; i++)
                s.ScheduledTasks.Add(new ScheduledTaskInfo { Path = $@"\SomeVendor\UpdateTask{i}", Enabled = false });

            var result = HealthScore.Compute(s);
            Assert.Equal(clean.Score, result.Score);
        }

        [Fact]
        public void WindowsOwnedRunningServices_NeverLowerTheScore_NoMatterHowMany()
        {
            var clean = HealthScore.Compute(PristineSnapshot());

            var s = PristineSnapshot();
            for (var i = 0; i < 300; i++)
                s.Services.Add(new ServiceInfo { Name = "svc" + i, Running = true, BinaryPath = @"C:\Windows\System32\svchost.exe -k netsvcs" });

            var result = HealthScore.Compute(s);
            Assert.Equal(clean.Score, result.Score);
        }

        [Fact]
        public void ManyThirdPartyRunningServices_LowersScore()
        {
            var clean = HealthScore.Compute(PristineSnapshot());

            var bloated = PristineSnapshot();
            for (var i = 0; i < 40; i++)
                bloated.Services.Add(new ServiceInfo { Name = "svc" + i, Running = true, BinaryPath = $@"C:\Program Files\Vendor{i}\svc.exe" });

            var result = HealthScore.Compute(bloated);
            Assert.True(result.Score < clean.Score);
        }

        [Fact]
        public void StoppedThirdPartyServices_DoNotCount()
        {
            var clean = HealthScore.Compute(PristineSnapshot());

            var s = PristineSnapshot();
            for (var i = 0; i < 40; i++)
                s.Services.Add(new ServiceInfo { Name = "svc" + i, Running = false, BinaryPath = $@"C:\Program Files\Vendor{i}\svc.exe" });

            var result = HealthScore.Compute(s);
            Assert.Equal(clean.Score, result.Score);
        }

        [Fact]
        public void UnknownServiceBinaryPath_IsNeverCountedAsThirdParty()
        {
            // A missing measurement (backend couldn't read PathName) must never inflate a penalty.
            var clean = HealthScore.Compute(PristineSnapshot());

            var s = PristineSnapshot();
            for (var i = 0; i < 40; i++)
                s.Services.Add(new ServiceInfo { Name = "svc" + i, Running = true, BinaryPath = null });

            var result = HealthScore.Compute(s);
            Assert.Equal(clean.Score, result.Score);
        }

        // ---- Path-classification helpers, tested directly ------------------------------------------

        [Theory]
        [InlineData(@"\Microsoft\Windows\Defrag\ScheduledDefrag", true)]
        [InlineData(@"\Microsoft\Windows\WindowsUpdate\Scheduled Start", true)]
        [InlineData(@"\Microsoft\Office\Office Feature Updates", false)] // a Microsoft *product*, not the OS itself
        [InlineData(@"\OneDrive Startup Task-S-1-5-21", false)]
        [InlineData(@"\NVIDIA App SelfUpdate", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsWindowsCoreScheduledTaskPath_ClassifiesCorrectly(string path, bool expected) =>
            Assert.Equal(expected, HealthScore.IsWindowsCoreScheduledTaskPath(path));

        [Theory]
        [InlineData(@"C:\Windows\system32\svchost.exe -k netsvcs", true)]
        [InlineData(@"""C:\Windows\System32\svchost.exe"" -k LocalService", true)]
        [InlineData(@"C:\Windows\SysWOW64\SomeLegacy.exe", true)]
        [InlineData(@"""C:\Program Files\NVIDIA Corporation\Display\nvcontainer.exe""", false)]
        [InlineData(@"C:\Windows\System32\DriverStore\FileRepository\realtek.inf_amd64\RtkAudUService64.exe", true)] // conservative: driver-store-hosted, never flagged as removable bloat
        [InlineData("", true)] // unreadable path -> never penalize
        [InlineData(null, true)]
        public void IsWindowsCoreServicePath_ClassifiesCorrectly(string binaryPath, bool expected) =>
            Assert.Equal(expected, HealthScore.IsWindowsCoreServicePath(binaryPath));

        [Theory]
        [InlineData(@"C:\Windows\System32\svchost.exe -k netsvcs", @"C:\Windows\System32\svchost.exe")]
        [InlineData(@"""C:\Program Files\Vendor\app.exe"" -flag", @"C:\Program Files\Vendor\app.exe")]
        [InlineData(@"C:\NoArgsHere.exe", @"C:\NoArgsHere.exe")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void ExtractExecutablePath_HandlesQuotedAndUnquotedCommandLines(string pathName, string expected) =>
            Assert.Equal(expected, HealthScore.ExtractExecutablePath(pathName));
    }
}
