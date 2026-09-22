using System;
using System.Collections.Generic;
using System.Linq;
using Performish.Core.Scanner;

namespace Performish.Core.Benchmark
{
    public sealed class BenchmarkOptions
    {
        /// <summary>Quick mode: CPU/memory/process count only, ~1 second, used as the default in the
        /// normal apply flow so benchmarking never makes routine tweak application feel slow. Full
        /// mode adds thread count and the disk collectors - a few seconds, opt-in or via "Run
        /// benchmark now".</summary>
        public bool QuickMode { get; set; } = true;

        public bool IncludeNetwork { get; set; }
        public int SampleCount { get; set; } = BenchmarkSampler.DefaultSampleCount;
        public int WarmupCount { get; set; } = BenchmarkSampler.DefaultWarmupCount;

        public static BenchmarkOptions Quick() => new BenchmarkOptions { QuickMode = true, SampleCount = 2, WarmupCount = 0 };
        public static BenchmarkOptions Full() => new BenchmarkOptions { QuickMode = false };
    }

    /// <summary>Builds the right set of collectors for a given options/context and runs them into a
    /// BenchmarkRun. Doesn't know about tweaks, presets, or the UI - callers (the apply flow, the
    /// standalone "run benchmark now" button, the CLI) decide when to call this and what Label/Kind/
    /// TweakIds to attach.</summary>
    public sealed class BenchmarkSuiteRunner
    {
        private readonly ISystemInfoBackend _info;

        public BenchmarkSuiteRunner(ISystemInfoBackend info) => _info = info ?? throw new ArgumentNullException(nameof(info));

        public IReadOnlyList<IMetricCollector> BuildCollectors(BenchmarkOptions options)
        {
            var collectors = new List<IMetricCollector>
            {
                new CpuIdleCollector(),
                new MemoryAvailableCollector(_info),
                new ProcessCountCollector()
            };

            if (!options.QuickMode)
            {
                collectors.Add(new ThreadCountCollector());
                collectors.Add(new DiskWriteThroughputCollector());
                collectors.Add(new DiskReadThroughputCollector());
                collectors.Add(new DirectoryListingLatencyCollector());
            }

            if (options.IncludeNetwork)
            {
                collectors.Add(new DnsLatencyCollector());
                collectors.Add(new GatewayPingLatencyCollector());
            }

            return collectors;
        }

        /// <summary>A rough, always-shown-before-running time estimate, in seconds - the task's "show
        /// an estimated time before running a full suite" requirement. Deliberately conservative
        /// (assumes the slowest realistic per-sample cost for each collector) rather than tuned to
        /// this specific machine.</summary>
        public static double EstimateSeconds(BenchmarkOptions options)
        {
            var perSampleSeconds = 0.25; // CPU idle's own sampling interval dominates
            var collectorCount = options.QuickMode ? 3 : 7;
            if (options.IncludeNetwork) collectorCount += 2;
            return collectorCount * options.SampleCount * perSampleSeconds;
        }

        /// <summary>Runs every applicable collector for real and returns a BenchmarkRun. Real
        /// measurement is always safe here - reading CPU/memory/disk/network timing is exactly what
        /// the task's safety-rule carve-out permits for this feature, unlike constructing a real tweak
        /// backend. A dry-run PostApply request never measures anything (there is nothing real to
        /// measure - see BenchmarkComparer's dry-run handling), returning an empty, clearly-marked
        /// preview run instead.</summary>
        public BenchmarkRun Run(BenchmarkOptions options, BenchmarkRunKind kind, string label,
            IEnumerable<string> tweakIds = null, bool isDryRunPreview = false, int? healthScore = null, string pairId = null)
        {
            var run = new BenchmarkRun
            {
                Kind = kind,
                Label = label ?? "",
                TweakIds = tweakIds?.ToList() ?? new List<string>(),
                IsDryRunPreview = isDryRunPreview,
                HealthScore = healthScore,
                PairId = pairId
            };

            if (isDryRunPreview && kind == BenchmarkRunKind.PostApply)
                return run; // no real "after" exists yet - Metrics stays empty, IsDryRunPreview says why

            foreach (var collector in BuildCollectors(options))
                run.Metrics.Add(collector.Collect(options.SampleCount, options.WarmupCount));

            return run;
        }

        /// <summary>Name -> HigherIsBetter for every collector this runner knows about - feeds
        /// BenchmarkComparer.Compare() without the comparer needing to construct collectors itself.</summary>
        public IReadOnlyDictionary<string, bool> HigherIsBetterByMetric(BenchmarkOptions options) =>
            BuildCollectors(options).ToDictionary(c => c.Name, c => c.HigherIsBetter);
    }
}
