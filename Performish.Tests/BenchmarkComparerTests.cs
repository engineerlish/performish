using System.Collections.Generic;
using Performish.Core.Benchmark;
using Xunit;

namespace Performish.Tests
{
    public class BenchmarkComparerTests
    {
        private static readonly Dictionary<string, bool> HigherIsBetter = new Dictionary<string, bool>
        {
            ["CPU idle"] = true,
            ["Process count"] = false,
            ["Flaky metric"] = true,
        };

        private static MetricSampleResult Reliable(string name, string unit, double median, double stdDev = 0.5) =>
            new MetricSampleResult
            {
                Name = name, Unit = unit, Category = MetricCategory.System,
                Available = true, IsReliable = true, Median = median, StdDev = stdDev,
                Samples = new List<double> { median }
            };

        private static BenchmarkRun MakeRun(BenchmarkRunKind kind, params MetricSampleResult[] metrics)
        {
            var run = new BenchmarkRun { Kind = kind };
            run.Metrics.AddRange(metrics);
            return run;
        }

        [Fact]
        public void ClearImprovement_HigherIsBetter_IsMarkedImproved()
        {
            var before = MakeRun(BenchmarkRunKind.Baseline, Reliable("CPU idle", "%", 50));
            var after = MakeRun(BenchmarkRunKind.PostApply, Reliable("CPU idle", "%", 90));

            var report = BenchmarkComparer.Compare(before, after, HigherIsBetter);

            Assert.Equal(ComparisonVerdict.Improved, report.Metrics[0].Verdict);
        }

        [Fact]
        public void ClearRegression_HigherIsBetter_IsMarkedWorse()
        {
            var before = MakeRun(BenchmarkRunKind.Baseline, Reliable("CPU idle", "%", 90));
            var after = MakeRun(BenchmarkRunKind.PostApply, Reliable("CPU idle", "%", 50));

            var report = BenchmarkComparer.Compare(before, after, HigherIsBetter);

            Assert.Equal(ComparisonVerdict.Worse, report.Metrics[0].Verdict);
        }

        [Fact]
        public void LowerIsBetterMetric_DecreaseIsImproved()
        {
            var before = MakeRun(BenchmarkRunKind.Baseline, Reliable("Process count", "processes", 200, 1));
            var after = MakeRun(BenchmarkRunKind.PostApply, Reliable("Process count", "processes", 150, 1));

            var report = BenchmarkComparer.Compare(before, after, HigherIsBetter);

            Assert.Equal(ComparisonVerdict.Improved, report.Metrics[0].Verdict);
        }

        [Fact]
        public void ChangeSmallerThanVariance_IsFlat_NeverClaimedAsImprovement()
        {
            // Delta is 1, but both runs have stddev 2 - well within the combined noise band.
            var before = MakeRun(BenchmarkRunKind.Baseline, Reliable("CPU idle", "%", 50, stdDev: 2));
            var after = MakeRun(BenchmarkRunKind.PostApply, Reliable("CPU idle", "%", 51, stdDev: 2));

            var report = BenchmarkComparer.Compare(before, after, HigherIsBetter);

            Assert.Equal(ComparisonVerdict.Flat, report.Metrics[0].Verdict);
        }

        [Fact]
        public void UnreliableEitherSide_NeverGivesAnImprovedOrWorseVerdict()
        {
            var flaky = new MetricSampleResult { Name = "Flaky metric", Unit = "x", Category = MetricCategory.System, Available = true, IsReliable = false, Median = 10 };
            var before = MakeRun(BenchmarkRunKind.Baseline, Reliable("Flaky metric", "x", 10));
            var after = MakeRun(BenchmarkRunKind.PostApply, flaky);

            var report = BenchmarkComparer.Compare(before, after, HigherIsBetter);

            Assert.Equal(ComparisonVerdict.Unreliable, report.Metrics[0].Verdict);
        }

        [Fact]
        public void UnavailableMetric_IsMarkedUnavailable_NeverGuessed()
        {
            var unavailable = MetricSampleResult.Unavailable("Gateway ping latency", "ms", MetricCategory.Network, "no gateway found");
            var before = MakeRun(BenchmarkRunKind.Baseline, unavailable);
            var after = MakeRun(BenchmarkRunKind.PostApply, unavailable);

            var report = BenchmarkComparer.Compare(before, after, HigherIsBetter);

            Assert.Equal(ComparisonVerdict.Unavailable, report.Metrics[0].Verdict);
            Assert.Null(report.Metrics[0].Before);
            Assert.Null(report.Metrics[0].After);
        }

        [Fact]
        public void DryRunAfter_NeverProducesAVerdict_AndNeverShowsAFabricatedAfterNumber()
        {
            var before = MakeRun(BenchmarkRunKind.Baseline, Reliable("CPU idle", "%", 50));
            var after = new BenchmarkRun { Kind = BenchmarkRunKind.PostApply, IsDryRunPreview = true }; // no metrics - never measured

            var report = BenchmarkComparer.Compare(before, after, HigherIsBetter);

            Assert.Equal(ComparisonVerdict.Unavailable, report.Metrics[0].Verdict);
            Assert.Null(report.Metrics[0].After);
            Assert.Contains("dry run", report.Metrics[0].PlainLanguageRead, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void MetricMissingFromOneRunEntirely_IsStillReportedAsUnavailable_NotSilentlyOmitted()
        {
            var before = MakeRun(BenchmarkRunKind.Baseline, Reliable("CPU idle", "%", 50));
            var after = MakeRun(BenchmarkRunKind.PostApply); // no metrics at all

            var report = BenchmarkComparer.Compare(before, after, HigherIsBetter);

            Assert.Single(report.Metrics);
            Assert.Equal(ComparisonVerdict.Unavailable, report.Metrics[0].Verdict);
        }

        [Fact]
        public void SingleTweak_NoAttributionDisclaimer()
        {
            var before = MakeRun(BenchmarkRunKind.Baseline, Reliable("CPU idle", "%", 50));
            var after = MakeRun(BenchmarkRunKind.PostApply, Reliable("CPU idle", "%", 90));
            after.TweakIds.Add("debloat.cortana");

            var report = BenchmarkComparer.Compare(before, after, HigherIsBetter);

            Assert.False(report.MultipleTweaksApplied);
            Assert.Null(report.AttributionDisclaimer);
        }

        [Fact]
        public void MultipleTweaks_ShowsAttributionDisclaimer_NeverFabricatesPerTweakNumbers()
        {
            var before = MakeRun(BenchmarkRunKind.Baseline, Reliable("CPU idle", "%", 50));
            var after = MakeRun(BenchmarkRunKind.PostApply, Reliable("CPU idle", "%", 90));
            after.TweakIds.AddRange(new[] { "debloat.cortana", "perf.sysmain_disable" });

            var report = BenchmarkComparer.Compare(before, after, HigherIsBetter);

            Assert.True(report.MultipleTweaksApplied);
            Assert.Contains("2 tweaks", report.AttributionDisclaimer);
            Assert.Contains("cannot be attributed to any single tweak", report.AttributionDisclaimer);
        }

        [Fact]
        public void HealthScoreDelta_ComputedWhenBothPresent()
        {
            var before = new BenchmarkRun { Kind = BenchmarkRunKind.Baseline, HealthScore = 50 };
            var after = new BenchmarkRun { Kind = BenchmarkRunKind.PostApply, HealthScore = 74 };

            var report = BenchmarkComparer.Compare(before, after, HigherIsBetter);

            Assert.Equal(24, report.HealthScoreDelta);
        }

        [Fact]
        public void HealthScoreDelta_NullWhenEitherMissing()
        {
            var before = new BenchmarkRun { Kind = BenchmarkRunKind.Baseline, HealthScore = null };
            var after = new BenchmarkRun { Kind = BenchmarkRunKind.PostApply, HealthScore = 74 };

            var report = BenchmarkComparer.Compare(before, after, HigherIsBetter);

            Assert.Null(report.HealthScoreDelta);
        }
    }
}
