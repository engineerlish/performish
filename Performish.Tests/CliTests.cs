using System.Collections.Generic;
using System.IO;
using Performish.Core;
using Performish.Core.Cli;
using Performish.Core.Models;
using Xunit;

namespace Performish.Tests
{
    public class CliOptionsParserTests
    {
        [Fact]
        public void NoArgs_ReturnsDefaultOptions_NoError()
        {
            var options = CliOptionsParser.Parse(new string[0]);
            Assert.Null(options.ParseError);
            Assert.False(options.ShowHelp);
        }

        [Fact]
        public void Help_SetsShowHelp()
        {
            var options = CliOptionsParser.Parse(new[] { "--help" });
            Assert.True(options.ShowHelp);
        }

        [Fact]
        public void PresetBalanced_DryRunByDefault_NoError()
        {
            var options = CliOptionsParser.Parse(new[] { "--preset", "balanced" });
            Assert.Null(options.ParseError);
            Assert.Equal(Preset.Balanced, options.SelectedPreset);
            Assert.True(options.DryRun);
        }

        [Fact]
        public void PresetIsCaseInsensitive()
        {
            var options = CliOptionsParser.Parse(new[] { "--preset", "AGGRESSIVE" });
            Assert.Equal(Preset.Aggressive, options.SelectedPreset);
        }

        [Fact]
        public void UnknownPreset_IsAnError()
        {
            var options = CliOptionsParser.Parse(new[] { "--preset", "nonsense" });
            Assert.NotNull(options.ParseError);
        }

        [Fact]
        public void PresetCustom_IsRejected()
        {
            var options = CliOptionsParser.Parse(new[] { "--preset", "custom" });
            Assert.NotNull(options.ParseError);
        }

        [Fact]
        public void NoPresetAndNoRevertAll_IsAnError()
        {
            var options = CliOptionsParser.Parse(new[] { "--dry-run" });
            Assert.NotNull(options.ParseError);
        }

        [Fact]
        public void PresetAndRevertAllTogether_IsAnError()
        {
            var options = CliOptionsParser.Parse(new[] { "--preset", "balanced", "--revert-all" });
            Assert.NotNull(options.ParseError);
        }

        [Fact]
        public void ApplyWithoutYes_IsAnError()
        {
            var options = CliOptionsParser.Parse(new[] { "--preset", "balanced", "--apply" });
            Assert.NotNull(options.ParseError);
        }

        [Fact]
        public void ApplyWithYes_NoError_DryRunIsFalse()
        {
            var options = CliOptionsParser.Parse(new[] { "--preset", "balanced", "--apply", "--yes" });
            Assert.Null(options.ParseError);
            Assert.False(options.DryRun);
        }

        [Fact]
        public void RevertAll_NoPresetNeeded()
        {
            var options = CliOptionsParser.Parse(new[] { "--revert-all", "--dry-run" });
            Assert.Null(options.ParseError);
            Assert.True(options.RevertAll);
        }

        [Fact]
        public void ReportPath_IsCaptured()
        {
            var options = CliOptionsParser.Parse(new[] { "--preset", "balanced", "--report", "out.html" });
            Assert.Equal("out.html", options.ReportPath);
        }

        [Fact]
        public void MissingValueForPreset_IsAnError()
        {
            var options = CliOptionsParser.Parse(new[] { "--preset" });
            Assert.NotNull(options.ParseError);
        }

        [Fact]
        public void UnknownArgument_IsAnError()
        {
            var options = CliOptionsParser.Parse(new[] { "--bogus" });
            Assert.NotNull(options.ParseError);
        }

        [Fact]
        public void Silent_IsCaptured()
        {
            var options = CliOptionsParser.Parse(new[] { "--preset", "conservative", "--silent" });
            Assert.True(options.Silent);
        }

        [Fact]
        public void Benchmark_IsCaptured_QuickByDefault()
        {
            var options = CliOptionsParser.Parse(new[] { "--preset", "conservative", "--benchmark" });
            Assert.Null(options.ParseError);
            Assert.True(options.Benchmark);
            Assert.False(options.BenchmarkFull);
        }

        [Fact]
        public void BenchmarkFull_AlsoSetsBenchmark()
        {
            var options = CliOptionsParser.Parse(new[] { "--preset", "conservative", "--benchmark-full" });
            Assert.True(options.Benchmark);
            Assert.True(options.BenchmarkFull);
        }

        [Fact]
        public void BenchmarkNetwork_WithoutBenchmark_IsAnError()
        {
            var options = CliOptionsParser.Parse(new[] { "--preset", "conservative", "--benchmark-network" });
            Assert.NotNull(options.ParseError);
        }

        [Fact]
        public void BenchmarkNetwork_WithBenchmark_NoError()
        {
            var options = CliOptionsParser.Parse(new[] { "--preset", "conservative", "--benchmark", "--benchmark-network" });
            Assert.Null(options.ParseError);
            Assert.True(options.BenchmarkNetwork);
        }

        [Fact]
        public void NoBenchmarkFlags_BenchmarkDefaultsOff()
        {
            var options = CliOptionsParser.Parse(new[] { "--preset", "conservative" });
            Assert.False(options.Benchmark);
        }
    }

    /// <summary>CliRunner tests use AppServices.BuildFake() exclusively - including for the
    /// `--apply --yes` (real, non-dry-run) path, which is safe here specifically because "real" only
    /// means "the Fake* backends' in-memory state changes", never anything on this machine. This is
    /// the one place in the suite that exercises DryRun=false end-to-end without violating the hard
    /// safety rule, because the backends underneath are fake regardless of that flag.</summary>
    public class CliRunnerTests : System.IDisposable
    {
        private readonly List<string> _output = new List<string>();
        private readonly string _tempReportPath;

        public CliRunnerTests()
        {
            _tempReportPath = Path.Combine(Path.GetTempPath(), "performish-cli-test-" + System.Guid.NewGuid() + ".html");
        }

        public void Dispose()
        {
            try { if (File.Exists(_tempReportPath)) File.Delete(_tempReportPath); } catch { }
        }

        [Fact]
        public void Run_Help_ReturnsSuccessAndPrintsHelp()
        {
            var options = CliOptionsParser.Parse(new[] { "--help" });
            var result = new CliRunner().Run(options, AppServices.BuildFake(), _output.Add);

            Assert.Equal(CliExitCode.Success, result);
            Assert.Contains(_output, l => l.Contains("Performish command-line mode"));
        }

        [Fact]
        public void Run_ArgumentError_ReturnsArgumentErrorExitCode()
        {
            var options = CliOptionsParser.Parse(new[] { "--bogus" });
            var result = new CliRunner().Run(options, AppServices.BuildFake(), _output.Add);

            Assert.Equal(CliExitCode.ArgumentError, result);
        }

        [Fact]
        public void Run_DryRunBalancedPreset_ReturnsSuccess_NoFailures()
        {
            var options = CliOptionsParser.Parse(new[] { "--preset", "balanced", "--dry-run" });
            var result = new CliRunner().Run(options, AppServices.BuildFake(), _output.Add);

            Assert.Equal(CliExitCode.Success, result);
            Assert.Contains(_output, l => l.Contains("Done."));
        }

        [Theory]
        [InlineData("conservative")]
        [InlineData("balanced")]
        [InlineData("aggressive")]
        public void Run_DryRunEveryPreset_ReturnsSuccess(string preset)
        {
            var options = CliOptionsParser.Parse(new[] { "--preset", preset, "--dry-run" });
            var result = new CliRunner().Run(options, AppServices.BuildFake(), _output.Add);

            Assert.Equal(CliExitCode.Success, result);
        }

        [Fact]
        public void Run_RealApplyAgainstFakeBackends_Succeeds()
        {
            // "Real" (DryRun=false) but the AppServices instance is entirely Fake-backed - see class
            // doc comment. Confirms the CLI path doesn't require dry-run specifically, only that the
            // backends underneath are what's fake in this session.
            var options = CliOptionsParser.Parse(new[] { "--preset", "conservative", "--apply", "--yes" });
            // isElevated overridden to true - this asserts CliRunner's own logic, not whatever
            // elevation state the test process happens to run under (usually unelevated). See
            // CliRunner.Run's isElevated parameter doc comment.
            var result = new CliRunner().Run(options, AppServices.BuildFake(), _output.Add, () => true);

            Assert.Equal(CliExitCode.Success, result);
        }

        [Fact]
        public void Run_RealApply_NotElevated_RefusesWithNotElevatedExitCode()
        {
            var options = CliOptionsParser.Parse(new[] { "--preset", "conservative", "--apply", "--yes" });
            var result = new CliRunner().Run(options, AppServices.BuildFake(), _output.Add, () => false);

            Assert.Equal(CliExitCode.NotElevated, result);
        }

        [Fact]
        public void Run_WithReportPath_WritesAReportFile()
        {
            var options = CliOptionsParser.Parse(new[] { "--preset", "balanced", "--dry-run", "--report", _tempReportPath });
            new CliRunner().Run(options, AppServices.BuildFake(), _output.Add);

            Assert.True(File.Exists(_tempReportPath));
            var content = File.ReadAllText(_tempReportPath);
            Assert.Contains("<!DOCTYPE html>", content);
        }

        [Fact]
        public void Run_Silent_ProducesNoOutputLines()
        {
            var options = CliOptionsParser.Parse(new[] { "--preset", "balanced", "--dry-run", "--silent" });
            new CliRunner().Run(options, AppServices.BuildFake(), _output.Add);

            Assert.Empty(_output);
        }

        [Fact]
        public void Run_RevertAllWithNothingApplied_ReturnsSuccess()
        {
            var options = CliOptionsParser.Parse(new[] { "--revert-all", "--dry-run" });
            var result = new CliRunner().Run(options, AppServices.BuildFake(), _output.Add);

            Assert.Equal(CliExitCode.Success, result);
            Assert.Contains(_output, l => l.Contains("Nothing to revert"));
        }

        [Fact]
        public void Run_WithBenchmarkFlag_DryRun_MeasuresBaselineButNotAfter()
        {
            var options = CliOptionsParser.Parse(new[] { "--preset", "conservative", "--dry-run", "--benchmark" });
            var result = new CliRunner().Run(options, AppServices.BuildFake(), _output.Add);

            Assert.Equal(CliExitCode.Success, result);
            Assert.Contains(_output, l => l.Contains("Benchmarking (baseline"));
            Assert.Contains(_output, l => l.Contains("not measured after (dry run)"));
        }

        [Fact]
        public void Run_WithBenchmarkFlag_RealApply_MeasuresBeforeAndAfter()
        {
            var options = CliOptionsParser.Parse(new[] { "--preset", "conservative", "--apply", "--yes", "--benchmark" });
            var result = new CliRunner().Run(options, AppServices.BuildFake(), _output.Add, () => true);

            Assert.Equal(CliExitCode.Success, result);
            Assert.Contains(_output, l => l.Contains("improved") || l.Contains("worse"));
        }

        [Fact]
        public void Run_WithBenchmarkAndReportPath_IncludesRealBenchmarkSectionInReport()
        {
            var options = CliOptionsParser.Parse(new[] { "--preset", "conservative", "--apply", "--yes", "--benchmark", "--report", _tempReportPath });
            new CliRunner().Run(options, AppServices.BuildFake(), _output.Add, () => true);

            var content = File.ReadAllText(_tempReportPath);
            Assert.Contains("Real benchmark comparison", content);
        }

        [Fact]
        public void Run_WithoutBenchmarkFlag_NeverMentionsBenchmarking()
        {
            var options = CliOptionsParser.Parse(new[] { "--preset", "conservative", "--dry-run" });
            new CliRunner().Run(options, AppServices.BuildFake(), _output.Add);

            Assert.DoesNotContain(_output, l => l.Contains("Benchmarking"));
        }

        [Fact]
        public void Run_RealApplyThenRevertAll_UndoesWhatWasApplied()
        {
            var services = AppServices.BuildFake();

            var applyOptions = CliOptionsParser.Parse(new[] { "--preset", "conservative", "--apply", "--yes" });
            new CliRunner().Run(applyOptions, services, _output.Add, () => true);

            Assert.NotEmpty(services.Runner.CurrentlyAppliedTweakIds());

            var revertOptions = CliOptionsParser.Parse(new[] { "--revert-all", "--apply", "--yes" });
            var revertResult = new CliRunner().Run(revertOptions, services, _output.Add, () => true);

            Assert.Equal(CliExitCode.Success, revertResult);
            Assert.Empty(services.Runner.CurrentlyAppliedTweakIds());
        }
    }
}
