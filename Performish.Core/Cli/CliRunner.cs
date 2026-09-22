using System;
using System.IO;
using System.Linq;
using Performish.Core.Benchmark;
using Performish.Core.Reporting;
using Performish.Core.Tweaks;

namespace Performish.Core.Cli
{
    public static class CliExitCode
    {
        public const int Success = 0;
        public const int SomeTweaksFailed = 1;
        public const int ArgumentError = 2;
        public const int NotElevated = 3;
    }

    /// <summary>Executes a parsed CliOptions against a real (or, in tests, fake) AppServices - the
    /// same TweakRunner/AppServices path the button UI uses, so a headless run and an interactive one
    /// behave identically for the same preset/options. No Console.Write calls happen unless
    /// `options.Silent` is false, and `write` is injected (not a hardcoded Console.WriteLine) so tests
    /// can capture output without touching the real console.</summary>
    public sealed class CliRunner
    {
        /// <param name="isElevated">Defaults to the real Elevation.IsElevated() check. Injectable so
        /// tests can exercise the real-apply path deterministically regardless of whether the test
        /// process itself happens to be elevated - the elevation check is about the OS process, not
        /// about whether AppServices' backends are Fake or Real, so it has nothing to do with the
        /// hard safety rule either way (a test overriding this never causes a real backend call).</param>
        public int Run(CliOptions options, AppServices services, Action<string> write, Func<bool> isElevated = null)
        {
            isElevated ??= Elevation.IsElevated;
            void Write(string s) { if (!options.Silent) write(s); }

            if (options.ShowHelp)
            {
                Write(CliOptionsParser.HelpText);
                return CliExitCode.Success;
            }

            if (options.ParseError != null)
            {
                Write("Error: " + options.ParseError);
                Write(CliOptionsParser.HelpText);
                return CliExitCode.ArgumentError;
            }

            if (!options.DryRun && !isElevated())
            {
                Write("Error: a real (non-dry-run) run requires Performish to be running elevated (Administrator).");
                return CliExitCode.NotElevated;
            }

            Write($"Scanning...");
            var scanBefore = services.Scanner.Scan();
            var healthBefore = HealthScore.Compute(scanBefore);
            Write($"Health score: {healthBefore.Score}/{HealthScore.TotalMax}");

            if (scanBefore.IsDomainJoined || scanBefore.IsMdmManaged)
                Write("Warning: this machine is domain-joined and/or MDM-managed - policy-scope tweaks may be overridden by Group Policy/Intune.");
            if (scanBefore.DetectedManagementAgents.Count > 0)
                Write("Warning: detected running VPN/RMM/endpoint-security software: " + string.Join(", ", scanBefore.DetectedManagementAgents));

            var tweaks = options.RevertAll
                ? services.TweakRegistry.All.Where(t => services.Runner.CurrentlyAppliedTweakIds().Contains(t.Id)).ToList()
                : services.TweakRegistry.ByPreset(options.SelectedPreset.Value).ToList();

            if (tweaks.Count == 0)
            {
                Write(options.RevertAll ? "Nothing to revert - no tweak is currently recorded as applied." : "Preset has no tweaks (unexpected).");
                return CliExitCode.Success;
            }

            Write($"{(options.RevertAll ? "Reverting" : "Applying")} {tweaks.Count} tweak(s), {(options.DryRun ? "DRY RUN" : "REAL")}:");

            var ctx = services.CreateContext(options.DryRun, line => Write("  " + line));

            BenchmarkSnapshot before = null;
            if (!options.DryRun) before = BenchmarkSnapshot.FromSystemSnapshot(scanBefore);

            var tweakIds = tweaks.Select(t => t.Id).ToList();
            var pairId = Guid.NewGuid().ToString("N");
            BenchmarkRun realBaseline = null;
            if (options.Benchmark)
            {
                Write($"Benchmarking (baseline, {(options.BenchmarkFull ? "full" : "quick")})...");
                var benchOptions = options.BenchmarkFull ? BenchmarkOptions.Full() : BenchmarkOptions.Quick();
                benchOptions.IncludeNetwork = options.BenchmarkNetwork;
                realBaseline = services.Benchmarks.Run(benchOptions, BenchmarkRunKind.Baseline,
                    $"CLI before: {(options.RevertAll ? "revert-all" : options.SelectedPreset.ToString())}",
                    tweakIds, isDryRunPreview: options.DryRun, healthBefore.Score, pairId);
                services.BenchmarkHistory.Save(realBaseline);
            }

            var wantsRestorePoint = !options.DryRun && !options.RevertAll && tweaks.Any(t => t.Risk != Models.RiskLevel.Safe);
            var batchResult = options.RevertAll
                ? services.Runner.UndoBatch(tweaks, ctx)
                : services.Runner.ApplyBatch(tweaks, ctx, wantsRestorePoint);

            var succeeded = batchResult.Results.Count(r => !r.Failed);
            var failed = batchResult.Results.Count(r => r.Failed);
            Write($"Done. {succeeded} succeeded, {failed} failed.");

            BenchmarkComparison benchmark = null;
            BenchmarkComparisonReport realBenchmark = null;
            var scanAfter = scanBefore;
            HealthScoreResult healthAfter = healthBefore;
            if (!options.DryRun && before != null)
            {
                scanAfter = services.Scanner.Scan();
                healthAfter = HealthScore.Compute(scanAfter);
                benchmark = new BenchmarkComparison { Before = before, After = BenchmarkSnapshot.FromSystemSnapshot(scanAfter) };
                Write($"Health score after: {healthAfter.Score}/{HealthScore.TotalMax}");
            }

            if (realBaseline != null)
            {
                var benchOptions = options.BenchmarkFull ? BenchmarkOptions.Full() : BenchmarkOptions.Quick();
                benchOptions.IncludeNetwork = options.BenchmarkNetwork;
                var realAfter = services.Benchmarks.Run(benchOptions, BenchmarkRunKind.PostApply,
                    $"CLI after: {(options.RevertAll ? "revert-all" : options.SelectedPreset.ToString())}",
                    tweakIds, isDryRunPreview: options.DryRun, !options.DryRun ? healthAfter.Score : (int?)null, pairId);
                services.BenchmarkHistory.Save(realAfter);

                realBenchmark = BenchmarkComparer.Compare(realBaseline, realAfter, services.Benchmarks.HigherIsBetterByMetric(benchOptions));
                var improved = realBenchmark.Metrics.Count(m => m.Verdict == ComparisonVerdict.Improved);
                var worse = realBenchmark.Metrics.Count(m => m.Verdict == ComparisonVerdict.Worse);
                Write(options.DryRun
                    ? "Benchmark: not measured after (dry run)."
                    : $"Benchmark: {improved} improved, {worse} worse (see report for detail).");
            }

            if (!string.IsNullOrEmpty(options.ReportPath))
            {
                var report = new ReportData
                {
                    MachineName = Environment.MachineName,
                    Scan = scanAfter,
                    HealthBefore = healthBefore,
                    HealthAfter = !options.DryRun ? healthAfter : null,
                    BatchResults = batchResult.Results,
                    WasDryRun = options.DryRun,
                    Benchmark = benchmark,
                    RealBenchmark = realBenchmark
                };
                File.WriteAllText(options.ReportPath, HtmlReportWriter.Render(report));
                Write($"Report written to {options.ReportPath}");
            }

            return failed > 0 ? CliExitCode.SomeTweaksFailed : CliExitCode.Success;
        }
    }
}
