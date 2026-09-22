using System;
using System.Collections.Generic;
using System.Linq;

namespace Performish.Core.Benchmark
{
    public enum ComparisonVerdict
    {
        Improved,
        Worse,
        Flat,
        Unavailable,
        Unreliable
    }

    public sealed class MetricComparisonRow
    {
        public string Name { get; set; }
        public string Unit { get; set; }
        public MetricCategory Category { get; set; }

        public double? Before { get; set; }
        public double? After { get; set; }
        public double? DeltaAbsolute => Before.HasValue && After.HasValue ? After - Before : null;
        public double? DeltaPercent => Before.HasValue && After.HasValue && Math.Abs(Before.Value) > 1e-9
            ? (After - Before) / Math.Abs(Before.Value) * 100.0
            : null;

        public ComparisonVerdict Verdict { get; set; }
        public string PlainLanguageRead { get; set; }
    }

    /// <summary>Compares any two BenchmarkRuns (not just a consecutive before/after pair - the task's
    /// "compare any two runs, not just the most recent") and produces one row per metric present in
    /// either run, with a variance-aware verdict. Never claims Improved/Worse for a metric that's
    /// unavailable in either run or flagged unreliable in either run - see BENCHMARK_PLAN.md
    /// "unreliable flagging" and the task's "never claim improvement for a change smaller than the
    /// measured variance" rule.</summary>
    public static class BenchmarkComparer
    {
        public static BenchmarkComparisonReport Compare(BenchmarkRun before, BenchmarkRun after, IReadOnlyDictionary<string, bool> higherIsBetterByMetric)
        {
            if (before == null) throw new ArgumentNullException(nameof(before));
            if (after == null) throw new ArgumentNullException(nameof(after));

            var rows = new List<MetricComparisonRow>();
            var names = before.Metrics.Select(m => m.Name).Union(after.Metrics.Select(m => m.Name)).Distinct();

            foreach (var name in names)
            {
                var beforeMetric = before.FindMetric(name);
                var afterMetric = after.FindMetric(name);
                var higherIsBetter = higherIsBetterByMetric != null && higherIsBetterByMetric.TryGetValue(name, out var h) ? h : true;
                rows.Add(CompareOne(name, beforeMetric, afterMetric, higherIsBetter, after.IsDryRunPreview));
            }

            return new BenchmarkComparisonReport
            {
                Before = before,
                After = after,
                Metrics = rows.OrderBy(r => r.Category).ThenBy(r => r.Name).ToList()
            };
        }

        private static MetricComparisonRow CompareOne(string name, MetricSampleResult before, MetricSampleResult after,
            bool higherIsBetter, bool afterIsDryRunPreview)
        {
            var unit = after?.Unit ?? before?.Unit ?? "";
            var category = after?.Category ?? before?.Category ?? MetricCategory.System;

            var row = new MetricComparisonRow { Name = name, Unit = unit, Category = category };

            if (afterIsDryRunPreview)
            {
                row.Before = before?.Available == true ? before.Median : (double?)null;
                row.Verdict = ComparisonVerdict.Unavailable;
                row.PlainLanguageRead = "Not measured - dry run (no real change was applied to measure).";
                return row;
            }

            var beforeAvailable = before != null && before.Available;
            var afterAvailable = after != null && after.Available;
            if (!beforeAvailable || !afterAvailable)
            {
                row.Before = beforeAvailable ? before.Median : (double?)null;
                row.After = afterAvailable ? after.Median : (double?)null;
                row.Verdict = ComparisonVerdict.Unavailable;
                row.PlainLanguageRead = !beforeAvailable
                    ? "Unavailable before this run: " + (before?.UnavailableReason ?? "not measured.")
                    : "Unavailable after this run: " + (after?.UnavailableReason ?? "not measured.");
                return row;
            }

            row.Before = before.Median;
            row.After = after.Median;

            if (!before.IsReliable || !after.IsReliable)
            {
                row.Verdict = ComparisonVerdict.Unreliable;
                row.PlainLanguageRead = "Measurement variance was too high to trust this comparison - re-run for a clearer read.";
                return row;
            }

            var delta = after.Median - before.Median;
            // Never claim a change smaller than the measured variance - the noise band is the sum of
            // both runs' standard deviations (a simple, honest two-sample noise estimate; see
            // DECISIONS.md).
            var noiseBand = before.StdDev + after.StdDev;
            if (Math.Abs(delta) <= noiseBand || Math.Abs(delta) < 1e-9)
            {
                row.Verdict = ComparisonVerdict.Flat;
                row.PlainLanguageRead = "No real change - within measurement noise.";
                return row;
            }

            var improved = higherIsBetter ? delta > 0 : delta < 0;
            row.Verdict = improved ? ComparisonVerdict.Improved : ComparisonVerdict.Worse;
            var pct = row.DeltaPercent;
            var pctText = pct.HasValue ? $" ({Math.Abs(pct.Value):0.#}%)" : "";
            row.PlainLanguageRead = improved
                ? $"Improved by {Math.Abs(delta):0.##} {unit}{pctText}."
                : $"Got worse by {Math.Abs(delta):0.##} {unit}{pctText}.";
            return row;
        }
    }

    public sealed class BenchmarkComparisonReport
    {
        public BenchmarkRun Before { get; set; }
        public BenchmarkRun After { get; set; }
        public List<MetricComparisonRow> Metrics { get; set; } = new List<MetricComparisonRow>();

        public int? HealthScoreDelta => Before?.HealthScore.HasValue == true && After?.HealthScore.HasValue == true
            ? After.HealthScore - Before.HealthScore
            : (int?)null;

        /// <summary>True when the After run's tweak batch had more than one tweak - drives the honest
        /// "can't attribute to a single tweak" disclaimer (task requirement: "if precise per-tweak
        /// attribution isn't reliable, say so plainly rather than fabricating a per-tweak number").</summary>
        public bool MultipleTweaksApplied => (After?.TweakIds?.Count ?? 0) > 1;

        public string AttributionDisclaimer => MultipleTweaksApplied
            ? $"{After.TweakIds.Count} tweaks were applied together in this run; the numbers above reflect " +
              "their combined effect and cannot be attributed to any single tweak. Use \"Benchmark each tweak " +
              "individually\" for per-tweak numbers."
            : null;
    }
}
