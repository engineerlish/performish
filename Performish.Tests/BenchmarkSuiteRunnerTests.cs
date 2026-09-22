using System.Linq;
using Performish.Core.Benchmark;
using Performish.Core.Scanner;
using Xunit;

namespace Performish.Tests
{
    /// <summary>Runs the real suite runner against real collectors (small sample counts to keep the
    /// suite fast) - permitted per the task's safety-rule carve-out for this feature.</summary>
    public class BenchmarkSuiteRunnerTests
    {
        private static BenchmarkSuiteRunner MakeRunner() => new BenchmarkSuiteRunner(new FakeSystemInfoBackend());

        [Fact]
        public void QuickMode_UsesTheMinimalCollectorSet()
        {
            var runner = MakeRunner();
            var collectors = runner.BuildCollectors(BenchmarkOptions.Quick());

            Assert.Equal(3, collectors.Count);
            Assert.Contains(collectors, c => c.Name == "CPU idle");
            Assert.Contains(collectors, c => c.Name == "Available memory");
            Assert.Contains(collectors, c => c.Name == "Process count");
            Assert.DoesNotContain(collectors, c => c.Category == MetricCategory.Disk);
        }

        [Fact]
        public void FullMode_IncludesDiskCollectors()
        {
            var runner = MakeRunner();
            var collectors = runner.BuildCollectors(BenchmarkOptions.Full());

            Assert.Contains(collectors, c => c.Name == "Disk write throughput");
            Assert.Contains(collectors, c => c.Name == "Disk read throughput");
            Assert.Contains(collectors, c => c.Name == "Directory listing latency");
            Assert.Contains(collectors, c => c.Name == "Thread count");
        }

        [Fact]
        public void NetworkNotIncludedByDefault_InEitherMode()
        {
            var runner = MakeRunner();

            Assert.DoesNotContain(runner.BuildCollectors(BenchmarkOptions.Quick()), c => c.Category == MetricCategory.Network);
            Assert.DoesNotContain(runner.BuildCollectors(BenchmarkOptions.Full()), c => c.Category == MetricCategory.Network);
        }

        [Fact]
        public void NetworkOptedIn_IsIncluded()
        {
            var runner = MakeRunner();
            var options = BenchmarkOptions.Full();
            options.IncludeNetwork = true;

            var collectors = runner.BuildCollectors(options);

            Assert.Contains(collectors, c => c.Name == "DNS resolution latency");
            Assert.Contains(collectors, c => c.Name == "Gateway ping latency");
        }

        [Fact]
        public void Run_QuickMode_ProducesRealMeasuredMetrics()
        {
            var runner = MakeRunner();
            var options = BenchmarkOptions.Quick();
            options.SampleCount = 2;
            options.WarmupCount = 0;

            var run = runner.Run(options, BenchmarkRunKind.Standalone, "test checkpoint");

            Assert.Equal(3, run.Metrics.Count);
            Assert.All(run.Metrics, m => Assert.True(m.Available, m.UnavailableReason));
            Assert.False(run.IsDryRunPreview);
        }

        [Fact]
        public void Run_DryRunPostApply_NeverMeasuresAnything_MetricsStayEmpty()
        {
            var runner = MakeRunner();

            var run = runner.Run(BenchmarkOptions.Quick(), BenchmarkRunKind.PostApply, "after (dry run)", isDryRunPreview: true);

            Assert.Empty(run.Metrics);
            Assert.True(run.IsDryRunPreview);
        }

        [Fact]
        public void Run_DryRunButBaselineKind_StillMeasuresForReal()
        {
            // A dry-run BATCH still has a real "before" - only the "after" is skipped, since nothing
            // real changed to measure.
            var runner = MakeRunner();
            var options = BenchmarkOptions.Quick();
            options.SampleCount = 1;

            var run = runner.Run(options, BenchmarkRunKind.Baseline, "before (dry run batch)", isDryRunPreview: true);

            Assert.NotEmpty(run.Metrics);
        }

        [Fact]
        public void Run_RecordsLabelKindTweakIdsAndHealthScore()
        {
            var runner = MakeRunner();
            var options = BenchmarkOptions.Quick();
            options.SampleCount = 1;

            var run = runner.Run(options, BenchmarkRunKind.Baseline, "Before: Balanced preset",
                tweakIds: new[] { "debloat.cortana", "perf.sysmain_disable" }, healthScore: 74, pairId: "abc123");

            Assert.Equal("Before: Balanced preset", run.Label);
            Assert.Equal(BenchmarkRunKind.Baseline, run.Kind);
            Assert.Equal(2, run.TweakIds.Count);
            Assert.Equal(74, run.HealthScore);
            Assert.Equal("abc123", run.PairId);
        }

        [Fact]
        public void HigherIsBetterByMetric_MatchesEachCollectorsOwnFlag()
        {
            var runner = MakeRunner();
            var map = runner.HigherIsBetterByMetric(BenchmarkOptions.Full());

            Assert.True(map["CPU idle"]);
            Assert.True(map["Available memory"]);
            Assert.False(map["Process count"]);
            Assert.True(map["Disk read throughput"]);
            Assert.False(map["Directory listing latency"]);
        }

        [Fact]
        public void EstimateSeconds_QuickIsFasterThanFull()
        {
            Assert.True(BenchmarkSuiteRunner.EstimateSeconds(BenchmarkOptions.Quick()) < BenchmarkSuiteRunner.EstimateSeconds(BenchmarkOptions.Full()));
        }

        [Fact]
        public void EstimateSeconds_NetworkAddsToTheEstimate()
        {
            var withoutNetwork = BenchmarkOptions.Full();
            var withNetwork = BenchmarkOptions.Full();
            withNetwork.IncludeNetwork = true;

            Assert.True(BenchmarkSuiteRunner.EstimateSeconds(withNetwork) > BenchmarkSuiteRunner.EstimateSeconds(withoutNetwork));
        }
    }
}
