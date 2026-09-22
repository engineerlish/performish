using System;
using System.Collections.Generic;

namespace Performish.Core.Benchmark
{
    public enum BenchmarkRunKind
    {
        /// <summary>A standalone "run benchmark now" checkpoint, not tied to applying anything.</summary>
        Standalone,
        Baseline,
        PostApply
    }

    /// <summary>One real measurement checkpoint - every field here was actually read from this
    /// machine at TimestampUtc (or, for IsDryRunPreview, explicitly marked as not a real
    /// post-apply measurement - see BenchmarkSuiteRunner). Stored with its full per-metric sample
    /// sets (not just medians) so a saved run can be re-analyzed later without re-measuring.</summary>
    public sealed class BenchmarkRun
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public string Label { get; set; } = "";
        public BenchmarkRunKind Kind { get; set; } = BenchmarkRunKind.Standalone;

        /// <summary>Links a Baseline run to its matching PostApply run (both share the same value);
        /// null for a Standalone run.</summary>
        public string PairId { get; set; }

        /// <summary>Which tweaks were (about to be, or were just) applied - empty for a Standalone
        /// checkpoint. More than one entry means a batch - see BenchmarkComparer's attribution
        /// disclaimer.</summary>
        public List<string> TweakIds { get; set; } = new List<string>();

        /// <summary>True only for a PostApply run that was never actually measured because the batch
        /// was a dry run (nothing changed, so there is nothing real to measure) - see
        /// BenchmarkComparer, which refuses to compute a verdict against a preview run.</summary>
        public bool IsDryRunPreview { get; set; }

        public int? HealthScore { get; set; }
        public List<MetricSampleResult> Metrics { get; set; } = new List<MetricSampleResult>();

        /// <summary>Present only when the gaming category was included and a frame-time CSV was
        /// available to import - never fabricated when no capture exists (see
        /// GamingMetricAdapter.ToMetricSampleResults for how this becomes report rows).</summary>
        public FrameTimeReport GamingFrameTime { get; set; }

        public MetricSampleResult FindMetric(string name) => Metrics.Find(m => m.Name == name);
    }
}
