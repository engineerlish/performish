using System;
using System.Collections.Generic;
using System.Linq;

namespace Performish.Core.Benchmark
{
    public enum MetricCategory
    {
        System,
        Disk,
        Network,
        Gaming,
        Score
    }

    /// <summary>One metric's result from one benchmark run - the full sample set is kept (not just
    /// the summary statistic) so a stored run can be re-analyzed later without re-measuring. Never
    /// constructed with a fabricated number: either <see cref="Available"/> is true and every sample
    /// was a real measurement, or it's false and <see cref="UnavailableReason"/> explains why, per the
    /// task's "never present a number that wasn't actually measured" rule.</summary>
    public sealed class MetricSampleResult
    {
        public string Name { get; set; }
        public string Unit { get; set; }
        public MetricCategory Category { get; set; }

        public bool Available { get; set; } = true;
        public string UnavailableReason { get; set; }

        public IReadOnlyList<double> Samples { get; set; } = Array.Empty<double>();

        public double Median { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public double StdDev { get; set; }

        /// <summary>True when this metric's coefficient of variation (StdDev / Median) is low enough
        /// that the median can be trusted as representative - see BenchmarkSampler.
        /// MaxCoefficientOfVariation for the threshold and its rationale.</summary>
        public bool IsReliable { get; set; } = true;

        public static MetricSampleResult Unavailable(string name, string unit, MetricCategory category, string reason) =>
            new MetricSampleResult
            {
                Name = name,
                Unit = unit,
                Category = category,
                Available = false,
                UnavailableReason = reason,
                IsReliable = false
            };
    }

    /// <summary>Pure sampling/statistics logic - takes any zero-argument measurement delegate, runs it
    /// with warm-up, and reduces the results to a robust summary. Deliberately knows nothing about
    /// what's being measured (CPU, disk, memory, ...) so it's testable once with synthetic delegates
    /// instead of once per collector.</summary>
    public static class BenchmarkSampler
    {
        public const int DefaultSampleCount = 5;
        public const int DefaultWarmupCount = 1;

        /// <summary>Coefficient of variation (StdDev/Median) above which a result is flagged
        /// unreliable rather than trusted at face value - see BENCHMARK_PLAN.md "unreliable flagging"
        /// for why 35% was chosen (comfortably above normal measurement jitter, well below
        /// meaningless).</summary>
        public const double MaxCoefficientOfVariation = 0.35;

        /// <summary>Runs <paramref name="measure"/> warmupCount + sampleCount times, discards the
        /// warm-up readings, and summarizes the rest. Any exception from <paramref name="measure"/> -
        /// on any call, including during warm-up - is caught and turned into an Unavailable result;
        /// a metric collector never has to handle its own error path twice.</summary>
        public static MetricSampleResult Sample(string name, string unit, MetricCategory category,
            Func<double> measure, int sampleCount = DefaultSampleCount, int warmupCount = DefaultWarmupCount)
        {
            if (measure == null) throw new ArgumentNullException(nameof(measure));
            if (sampleCount < 1) throw new ArgumentOutOfRangeException(nameof(sampleCount), "At least one real sample is required.");

            try
            {
                for (var i = 0; i < warmupCount; i++) measure();

                var samples = new List<double>(sampleCount);
                for (var i = 0; i < sampleCount; i++) samples.Add(measure());

                return Summarize(name, unit, category, samples);
            }
            catch (Exception ex)
            {
                return MetricSampleResult.Unavailable(name, unit, category, ex.Message);
            }
        }

        public static MetricSampleResult Summarize(string name, string unit, MetricCategory category, IReadOnlyList<double> samples)
        {
            if (samples == null || samples.Count == 0)
                return MetricSampleResult.Unavailable(name, unit, category, "No samples were collected.");

            var sorted = samples.OrderBy(x => x).ToList();
            var median = Median(sorted);
            var mean = sorted.Average();
            var variance = sorted.Count > 1 ? sorted.Sum(x => (x - mean) * (x - mean)) / (sorted.Count - 1) : 0.0;
            var stdDev = Math.Sqrt(variance);
            // A zero/near-zero median makes the coefficient of variation blow up on essentially-noise-
            // free data (e.g. every sample exactly 0) - treat that case as reliable rather than
            // dividing by ~zero.
            var coefficientOfVariation = Math.Abs(median) > 1e-9 ? stdDev / Math.Abs(median) : 0.0;

            return new MetricSampleResult
            {
                Name = name,
                Unit = unit,
                Category = category,
                Available = true,
                Samples = sorted,
                Median = median,
                Min = sorted[0],
                Max = sorted[sorted.Count - 1],
                StdDev = stdDev,
                IsReliable = coefficientOfVariation <= MaxCoefficientOfVariation
            };
        }

        private static double Median(List<double> sortedAscending)
        {
            var n = sortedAscending.Count;
            var mid = n / 2;
            return n % 2 == 1 ? sortedAscending[mid] : (sortedAscending[mid - 1] + sortedAscending[mid]) / 2.0;
        }
    }
}
