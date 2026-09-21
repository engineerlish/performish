using System.Collections.Generic;
using Performish.Core.Backends;
using Performish.Core.Benchmark;
using Performish.Core.Scanner;
using Xunit;

namespace Performish.Tests
{
    public class ScannerTests
    {
        [Fact]
        public void Scan_ClassifiesWindows11ByBuildNumber()
        {
            var info = new FakeSystemInfoBackend { BuildNumber = 22621 };
            var power = new FakePowerBackend();
            var scanner = new SystemScanner(info, power);

            var snapshot = scanner.Scan();

            Assert.True(snapshot.IsWindows11);
        }

        [Fact]
        public void Scan_Windows10Build_IsNotWindows11()
        {
            var info = new FakeSystemInfoBackend { BuildNumber = 19045 };
            var power = new FakePowerBackend();
            var scanner = new SystemScanner(info, power);

            var snapshot = scanner.Scan();

            Assert.False(snapshot.IsWindows11);
        }

        [Fact]
        public void Scan_ReportsActivePowerPlanName()
        {
            var info = new FakeSystemInfoBackend();
            var power = new FakePowerBackend();
            var scanner = new SystemScanner(info, power);

            var snapshot = scanner.Scan();

            Assert.Equal("Balanced", snapshot.ActivePowerPlanName);
        }

        [Fact]
        public void Scan_CarriesThroughGpuAndMemoryFacts()
        {
            var info = new FakeSystemInfoBackend();
            info.Gpus = new List<GpuInfo> { new GpuInfo { Name = "RTX Test", Vendor = "NVIDIA", DriverVersion = "9.9" } };
            info.TotalRam = 16L * 1024 * 1024 * 1024;
            info.AvailableRam = 4L * 1024 * 1024 * 1024;

            var scanner = new SystemScanner(info, new FakePowerBackend());
            var snapshot = scanner.Scan();

            Assert.Single(snapshot.Gpus);
            Assert.Equal("NVIDIA", snapshot.Gpus[0].Vendor);
            Assert.Equal(12L * 1024 * 1024 * 1024, snapshot.UsedRamBytes);
        }

        [Fact]
        public void Scan_CarriesThroughDiskSpaceAndUptime()
        {
            var info = new FakeSystemInfoBackend { FreeDiskBytes = 50L * 1024 * 1024 * 1024, TotalDiskBytes = 200L * 1024 * 1024 * 1024, UptimeHours = 72.5 };
            var scanner = new SystemScanner(info, new FakePowerBackend());

            var snapshot = scanner.Scan();

            Assert.Equal(50L * 1024 * 1024 * 1024, snapshot.FreeDiskSpaceBytes);
            Assert.Equal(200L * 1024 * 1024 * 1024, snapshot.TotalDiskSpaceBytes);
            Assert.Equal(150L * 1024 * 1024 * 1024, snapshot.UsedDiskSpaceBytes);
            Assert.Equal(72.5, snapshot.UptimeHours);
        }

        [Fact]
        public void Scan_DetectsKnownManagementAgentsFromRunningProcessNames()
        {
            var info = new FakeSystemInfoBackend { RunningProcessNames = new List<string> { "explorer", "teamviewer", "chrome" } };
            var scanner = new SystemScanner(info, new FakePowerBackend());

            var snapshot = scanner.Scan();

            Assert.Contains("TeamViewer", snapshot.DetectedManagementAgents);
        }

        [Fact]
        public void Scan_NoKnownAgentsRunning_DetectedListIsEmpty()
        {
            var info = new FakeSystemInfoBackend { RunningProcessNames = new List<string> { "explorer", "chrome" } };
            var scanner = new SystemScanner(info, new FakePowerBackend());

            var snapshot = scanner.Scan();

            Assert.Empty(snapshot.DetectedManagementAgents);
        }
    }

    public class BenchmarkTests
    {
        [Fact]
        public void FromSystemSnapshot_CountsRunningServicesAndEnabledTasksOnly()
        {
            var snapshot = new SystemSnapshot
            {
                Services = new List<ServiceInfo>
                {
                    new ServiceInfo { Name = "a", Running = true },
                    new ServiceInfo { Name = "b", Running = false }
                },
                ScheduledTasks = new List<ScheduledTaskInfo>
                {
                    new ScheduledTaskInfo { Path = "t1", Enabled = true },
                    new ScheduledTaskInfo { Path = "t2", Enabled = false },
                    new ScheduledTaskInfo { Path = "t3", Enabled = true }
                },
                StartupItems = new List<StartupItemInfo> { new StartupItemInfo { Name = "x" } }
            };

            var bench = BenchmarkSnapshot.FromSystemSnapshot(snapshot);

            Assert.Equal(1, bench.RunningServiceCount);
            Assert.Equal(2, bench.EnabledScheduledTaskCount);
            Assert.Equal(1, bench.EnabledStartupItemCount);
            Assert.Equal(3, bench.BootImpactItemCount);
        }

        [Fact]
        public void Comparison_ComputesDeltasBetweenBeforeAndAfter()
        {
            var before = new BenchmarkSnapshot { UsedRamBytes = 4_000_000_000, ProcessCount = 150, RunningServiceCount = 100 };
            var after = new BenchmarkSnapshot { UsedRamBytes = 3_500_000_000, ProcessCount = 140, RunningServiceCount = 90 };

            var comparison = new BenchmarkComparison { Before = before, After = after };

            Assert.Equal(-500_000_000, comparison.UsedRamDeltaBytes);
            Assert.Equal(-10, comparison.ProcessCountDelta);
            Assert.Equal(-10, comparison.RunningServiceCountDelta);
        }
    }

    public class FrameTimeReaderTests
    {
        [Fact]
        public void Parse_PresentMonStyleCsv_ComputesAverageAnd1PercentLow()
        {
            var lines = new List<string> { "Application,MsBetweenPresents" };
            // 99 frames at ~16.67ms (60fps) + 1 slow frame at 50ms (20fps) -> a clear 1% low outlier.
            for (var i = 0; i < 99; i++) lines.Add("game.exe,16.667");
            lines.Add("game.exe,50.0");

            var report = FrameTimeReader.ParseLines(lines);

            Assert.Equal(100, report.SampleCount);
            Assert.InRange(report.AverageFps, 55, 61);
            Assert.True(report.P1LowFps < report.AverageFps);
        }

        [Fact]
        public void Parse_UnknownColumns_ReturnsEmptyReportRatherThanThrowing()
        {
            var lines = new List<string> { "Foo,Bar", "1,2" };
            var report = FrameTimeReader.ParseLines(lines);
            Assert.Equal(0, report.SampleCount);
        }

        [Fact]
        public void Parse_EmptyFile_ReturnsEmptyReport()
        {
            var report = FrameTimeReader.ParseLines(new List<string>());
            Assert.Equal(0, report.SampleCount);
        }
    }
}
