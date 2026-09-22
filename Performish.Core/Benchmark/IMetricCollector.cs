namespace Performish.Core.Benchmark
{
    /// <summary>One real, measurable metric a benchmark run can collect. Implementations read real,
    /// non-destructive system state (timing, resource counters) - the hard safety rule's carve-out for
    /// this feature - and never fabricate a value: an unmeasurable metric returns
    /// MetricSampleResult.Unavailable, never a guess.</summary>
    public interface IMetricCollector
    {
        string Name { get; }
        string Unit { get; }
        MetricCategory Category { get; }

        /// <summary>False for collectors that involve network activity (DNS, gateway ping) or that
        /// otherwise shouldn't run unless the user explicitly asks - see BENCHMARK_PLAN.md "scope".</summary>
        bool RunByDefault { get; }

        /// <summary>True if a larger number is the better outcome (e.g. CPU idle %, available memory,
        /// disk throughput); false if smaller is better (e.g. latency, process/thread count). Drives
        /// BenchmarkComparer's improved/worse direction - kept on the collector, next to the metric it
        /// describes, rather than guessed from the metric name in the comparison layer.</summary>
        bool HigherIsBetter { get; }

        MetricSampleResult Collect(int sampleCount, int warmupCount);
    }
}
