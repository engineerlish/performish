using System.Collections.Generic;
using Performish.Core.Benchmark;
using Performish.Core.Models;
using Performish.Core.Reporting;
using Performish.Core.Scanner;
using Performish.Core.Tweaks;
using Xunit;

namespace Performish.Tests
{
    public class HtmlReportWriterTests
    {
        private static SystemSnapshot MakeSnapshot() => new SystemSnapshot
        {
            WindowsProductName = "Windows 11 Pro",
            WindowsEdition = "Professional",
            BuildNumber = 26100,
            CpuName = "Test CPU",
            Gpus = new List<GpuInfo>(),
            TotalRamBytes = 16L * 1024 * 1024 * 1024,
            AvailableRamBytes = 8L * 1024 * 1024 * 1024,
            FreeDiskSpaceBytes = 100L * 1024 * 1024 * 1024,
            TotalDiskSpaceBytes = 500L * 1024 * 1024 * 1024,
            Services = new List<ServiceInfo>(),
            StartupItems = new List<StartupItemInfo>()
        };

        [Fact]
        public void Render_ScanOnly_ProducesValidHtmlWithScanData()
        {
            var data = new ReportData { Scan = MakeSnapshot(), HealthBefore = HealthScore.Compute(MakeSnapshot()) };
            var html = HtmlReportWriter.Render(data);

            Assert.StartsWith("<!DOCTYPE html>", html);
            Assert.Contains("</html>", html);
            Assert.Contains("Windows 11 Pro", html);
            Assert.Contains("Test CPU", html);
        }

        [Fact]
        public void Render_WithBatchResults_ListsEachTweak()
        {
            var tweak = TweakFactory.RegistryDword("t.test", "Test Tweak", "desc", TweakCategory.Debloat,
                RiskLevel.Safe, TweakScope.CurrentUser, false, "source",
                Microsoft.Win32.RegistryHive.CurrentUser, @"Software\Test", "V", 1);

            var data = new ReportData
            {
                Scan = MakeSnapshot(),
                HealthBefore = HealthScore.Compute(MakeSnapshot()),
                BatchResults = new List<TweakRunResult>
                {
                    new TweakRunResult { Tweak = tweak, Result = TweakOperationResult.Success("did it") }
                },
                WasDryRun = true
            };

            var html = HtmlReportWriter.Render(data);

            Assert.Contains("Test Tweak", html);
            Assert.Contains("t.test", html);
            Assert.Contains("dry run", html, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Render_EscapesHtmlInNames_NoRawTagInjection()
        {
            var tweak = TweakFactory.RegistryDword("t.xss", "<script>alert(1)</script>", "desc", TweakCategory.Debloat,
                RiskLevel.Safe, TweakScope.CurrentUser, false, "source",
                Microsoft.Win32.RegistryHive.CurrentUser, @"Software\Test", "V", 1);

            var data = new ReportData
            {
                Scan = MakeSnapshot(),
                BatchResults = new List<TweakRunResult> { new TweakRunResult { Tweak = tweak, Result = TweakOperationResult.Success("ok") } }
            };

            var html = HtmlReportWriter.Render(data);

            Assert.DoesNotContain("<script>alert(1)</script>", html);
            Assert.Contains("&lt;script&gt;", html);
        }

        [Fact]
        public void Render_WithBenchmark_ShowsDeltas()
        {
            var before = new BenchmarkSnapshot { UsedRamBytes = 5_000_000_000, ProcessCount = 150 };
            var after = new BenchmarkSnapshot { UsedRamBytes = 4_500_000_000, ProcessCount = 140 };

            var data = new ReportData
            {
                Scan = MakeSnapshot(),
                Benchmark = new BenchmarkComparison { Before = before, After = after }
            };

            var html = HtmlReportWriter.Render(data);
            Assert.Contains("Before / after", html);
        }

        [Fact]
        public void Render_NoScanNoData_StillProducesValidHtml()
        {
            var html = HtmlReportWriter.Render(new ReportData());
            Assert.StartsWith("<!DOCTYPE html>", html);
            Assert.Contains("</html>", html);
        }
    }
}
