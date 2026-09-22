using System;
using Performish.Core.Models;

namespace Performish.Core.Cli
{
    public sealed class CliOptions
    {
        public bool ShowHelp { get; set; }
        public string ParseError { get; set; }

        public Preset? SelectedPreset { get; set; }
        public bool RevertAll { get; set; }
        public bool DryRun { get; set; } = true; // safe default, same as the UI's own default
        public bool Yes { get; set; }
        public bool Silent { get; set; }
        public string ReportPath { get; set; }

        /// <summary>Off by default even in a headless run - fleet/RMM use means every extra second
        /// multiplies across many machines, so benchmarking here is opt-in via --benchmark/
        /// --benchmark-full, unlike the UI's Quick-by-default. See CliRunner.</summary>
        public bool Benchmark { get; set; }
        public bool BenchmarkFull { get; set; }
        public bool BenchmarkNetwork { get; set; }
    }

    /// <summary>Parses `Performish.exe --preset balanced --dry-run --report out.html` style
    /// arguments for headless/RMM use (see ROADMAP.md "IT and fleet use"). Pure - no I/O, no
    /// Environment.Exit - so it's fully unit-testable, and Program.cs is the only place that ever
    /// calls Environment.Exit with whatever exit code CliRunner returns.</summary>
    public static class CliOptionsParser
    {
        public static CliOptions Parse(string[] args)
        {
            var options = new CliOptions();
            if (args == null || args.Length == 0) return options;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--help":
                    case "-h":
                    case "/?":
                        options.ShowHelp = true;
                        break;

                    case "--preset":
                        if (i + 1 >= args.Length) { options.ParseError = "--preset requires a value (conservative|balanced|aggressive)."; return options; }
                        i++;
                        if (!Enum.TryParse<Preset>(args[i], ignoreCase: true, out var preset) || preset == Preset.Custom)
                        {
                            options.ParseError = $"Unknown preset '{args[i]}' - expected conservative, balanced, or aggressive.";
                            return options;
                        }
                        options.SelectedPreset = preset;
                        break;

                    case "--revert-all":
                        options.RevertAll = true;
                        break;

                    case "--dry-run":
                        options.DryRun = true;
                        break;

                    case "--apply":
                        // Explicit, deliberately not the default - a headless real apply needs both
                        // --apply AND --yes, so a script can never real-apply by omission/typo.
                        options.DryRun = false;
                        break;

                    case "--yes":
                        options.Yes = true;
                        break;

                    case "--silent":
                        options.Silent = true;
                        break;

                    case "--report":
                        if (i + 1 >= args.Length) { options.ParseError = "--report requires a file path."; return options; }
                        i++;
                        options.ReportPath = args[i];
                        break;

                    case "--benchmark":
                        options.Benchmark = true;
                        break;

                    case "--benchmark-full":
                        options.Benchmark = true;
                        options.BenchmarkFull = true;
                        break;

                    case "--benchmark-network":
                        options.BenchmarkNetwork = true;
                        break;

                    default:
                        options.ParseError = $"Unknown argument: {args[i]}";
                        return options;
                }
            }

            if (!options.ShowHelp && options.ParseError == null)
            {
                if (options.SelectedPreset == null && !options.RevertAll)
                    options.ParseError = "Specify --preset <conservative|balanced|aggressive> or --revert-all.";
                else if (options.SelectedPreset != null && options.RevertAll)
                    options.ParseError = "--preset and --revert-all cannot both be given.";
                else if (!options.DryRun && !options.Yes)
                    options.ParseError = "A real (non-dry-run) run needs --apply AND --yes explicitly - refusing to guess.";
                else if (options.BenchmarkNetwork && !options.Benchmark)
                    options.ParseError = "--benchmark-network requires --benchmark or --benchmark-full.";
            }

            return options;
        }

        public const string HelpText =
@"Performish command-line mode

  Performish.exe --preset <conservative|balanced|aggressive> [--dry-run|--apply --yes] [--report <path.html>] [--silent] [--benchmark|--benchmark-full] [--benchmark-network]
  Performish.exe --revert-all [--dry-run|--apply --yes] [--report <path.html>] [--silent] [--benchmark|--benchmark-full] [--benchmark-network]
  Performish.exe --help

Options:
  --preset <name>   Which preset to apply (conservative, balanced, aggressive).
  --revert-all      Undo every tweak currently recorded as applied, instead of applying a preset.
  --dry-run         Preview only - this is the default if neither --dry-run nor --apply is given.
  --apply           Really apply/revert. Requires --yes as well - refusing to guess on an unattended run.
  --yes             Required alongside --apply to confirm a real, non-dry-run run.
  --report <path>   Write an HTML report to this path (scan, health score, tweak-by-tweak result).
  --silent          Suppress console output (a --report file, if given, is still written).
  --benchmark       Capture a real, sampled before/after benchmark (CPU/memory/disk) around this run.
                    Off by default even headlessly - every extra second multiplies across a fleet.
  --benchmark-full  Same, plus thread count and disk read/write throughput (a few seconds slower).
  --benchmark-network  Also measure DNS/gateway ping latency (real network activity) - requires
                    --benchmark or --benchmark-full.

With no arguments, Performish launches its normal graphical UI.
Exit codes: 0 = success, 1 = one or more tweaks failed, 2 = argument error, 3 = not elevated.";
    }
}
