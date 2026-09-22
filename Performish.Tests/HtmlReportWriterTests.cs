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

        // ---- Real (sampled-timing) benchmark section ------------------------------------------------

        private static readonly Dictionary<string, bool> HigherIsBetter = new Dictionary<string, bool> { ["CPU idle"] = true };

        private static MetricSampleResult Reliable(string name, double median) => new MetricSampleResult
        {
            Name = name, Unit = "%", Category = MetricCategory.System, Available = true, IsReliable = true, Median = median, StdDev = 0.1
        };

        [Fact]
        public void Render_RealBenchmark_IncludesMeasuredBeforeAfterAndVerdict()
        {
            var before = new BenchmarkRun { Kind = BenchmarkRunKind.Baseline, HealthScore = 50 };
            before.Metrics.Add(Reliable("CPU idle", 50));
            var after = new BenchmarkRun { Kind = BenchmarkRunKind.PostApply, HealthScore = 74 };
            after.Metrics.Add(Reliable("CPU idle", 90));

            var report = BenchmarkComparer.Compare(before, after, HigherIsBetter);
            var html = HtmlReportWriter.Render(new ReportData { Scan = MakeSnapshot(), RealBenchmark = report });

            Assert.Contains("Real benchmark comparison", html);
            Assert.Contains("CPU idle", html);
            Assert.Contains("Improved", html);
            Assert.Contains("Health score change: +24", html);
        }

        [Fact]
        public void Render_RealBenchmark_MultipleTweaks_IncludesAttributionDisclaimer()
        {
            var before = new BenchmarkRun { Kind = BenchmarkRunKind.Baseline };
            before.Metrics.Add(Reliable("CPU idle", 50));
            var after = new BenchmarkRun { Kind = BenchmarkRunKind.PostApply };
            after.Metrics.Add(Reliable("CPU idle", 90));
            after.TweakIds.AddRange(new[] { "a", "b" });

            var report = BenchmarkComparer.Compare(before, after, HigherIsBetter);
            var html = HtmlReportWriter.Render(new ReportData { RealBenchmark = report });

            Assert.Contains("cannot be attributed to any single tweak", html);
        }

        [Fact]
        public void Render_RealBenchmark_UnavailableMetric_NeverShowsAFabricatedNumber()
        {
            var before = new BenchmarkRun { Kind = BenchmarkRunKind.Baseline };
            before.Metrics.Add(MetricSampleResult.Unavailable("Gateway ping latency", "ms", MetricCategory.Network, "no gateway"));
            var after = new BenchmarkRun { Kind = BenchmarkRunKind.PostApply };
            after.Metrics.Add(MetricSampleResult.Unavailable("Gateway ping latency", "ms", MetricCategory.Network, "no gateway"));

            var report = BenchmarkComparer.Compare(before, after, HigherIsBetter);
            var html = HtmlReportWriter.Render(new ReportData { RealBenchmark = report });

            Assert.Contains("Unavailable", html);
            Assert.DoesNotContain("NaN", html);
        }

        [Fact]
        public void Render_NoRealBenchmark_OmitsTheSection()
        {
            var html = HtmlReportWriter.Render(new ReportData { Scan = MakeSnapshot() });

            Assert.DoesNotContain("Real benchmark comparison", html);
        }
    }
}
