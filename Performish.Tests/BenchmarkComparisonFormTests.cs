using System.Linq;
using Performish.Core.Benchmark;
using Performish.Dialogs;
using Xunit;

namespace Performish.Tests
{
    public class BenchmarkComparisonFormTests
    {
        private static MetricSampleResult Reliable(string name, string unit, double median) => new MetricSampleResult
        {
            Name = name, Unit = unit, Category = MetricCategory.System, Available = true, IsReliable = true, Median = median, StdDev = 0.1
        };

        private static readonly System.Collections.Generic.Dictionary<string, bool> HigherIsBetter = new()
        {
            ["CPU idle"] = true,
            ["Process count"] = false,
        };

        private static BenchmarkComparisonReport MakeReport(int tweakCount = 1)
        {
            var before = new BenchmarkRun { Kind = BenchmarkRunKind.Baseline, HealthScore = 50 };
            before.Metrics.Add(Reliable("CPU idle", "%", 50));
            before.Metrics.Add(Reliable("Process count", "processes", 200));

            var after = new BenchmarkRun { Kind = BenchmarkRunKind.PostApply, HealthScore = 74 };
            after.Metrics.Add(Reliable("CPU idle", "%", 90)); // improved (higher is better)
            after.Metrics.Add(Reliable("Process count", "processes", 190)); // lower is better here too -> also improved
            for (var i = 0; i < tweakCount; i++) after.TweakIds.Add("tweak" + i);

            return BenchmarkComparer.Compare(before, after, HigherIsBetter);
        }

        [StaFact]
        public void Populate_ShowsSummaryAndEveryMetricRow()
        {
            using var form = new BenchmarkComparisonForm(MakeReport());
            form.Show();

            Assert.Contains("improved", form.SummaryText);
            Assert.Contains("health score +24", form.SummaryText);
            Assert.Equal(2, form.Rows.Count);
        }

        [StaFact]
        public void ImprovedMetric_IsStyledBetter_Green()
        {
            using var form = new BenchmarkComparisonForm(MakeReport());
            form.Show();

            var row = form.Rows.First(r => r.Text.Contains("CPU idle"));
            Assert.StartsWith("[BETTER]", row.Text);
            Assert.Equal(UiStyle.Accent, row.Color);
        }

        [StaFact]
        public void SingleTweak_NoDisclaimerShown()
        {
            using var form = new BenchmarkComparisonForm(MakeReport(tweakCount: 1));
            form.Show();

            Assert.Empty(form.DisclaimerText);
        }

        [StaFact]
        public void MultipleTweaks_ShowsAttributionDisclaimer()
        {
            using var form = new BenchmarkComparisonForm(MakeReport(tweakCount: 3));
            form.Show();

            Assert.Contains("3 tweaks", form.DisclaimerText);
        }

        [StaFact]
        public void SelectingARow_ShowsBeforeAfterAndPlainLanguageReadInDetailPane()
        {
            using var form = new BenchmarkComparisonForm(MakeReport());
            form.Show();

            var index = form.Rows.ToList().FindIndex(r => r.Text.Contains("CPU idle"));
            form.SelectRow(index);

            Assert.Contains("Before: 50", form.DetailText);
            Assert.Contains("After: 90", form.DetailText);
            Assert.Contains("Improved", form.DetailText);
        }

        [StaFact]
        public void UnavailableMetric_ShowsNoDataStyling()
        {
            var before = new BenchmarkRun { Kind = BenchmarkRunKind.Baseline };
            var after = new BenchmarkRun { Kind = BenchmarkRunKind.PostApply, IsDryRunPreview = true };
            before.Metrics.Add(Reliable("CPU idle", "%", 50));
            var report = BenchmarkComparer.Compare(before, after, HigherIsBetter);

            using var form = new BenchmarkComparisonForm(report);
            form.Show();

            var row = form.Rows.First(r => r.Text.Contains("CPU idle"));
            Assert.StartsWith("[N/A]", row.Text);
        }
    }
}
