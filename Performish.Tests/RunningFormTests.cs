using System.Linq;
using System.Threading.Tasks;
using Performish.Core;
using Performish.Core.Backup;
using Performish.Core.Models;
using Performish.Core.Tweaks;
using Performish.Dialogs;
using Xunit;

namespace Performish.Tests
{
    /// <summary>Regression coverage for the "Balanced preset dry run crashes with 'Failed to set
    /// Win32 parent window of the Control'" bug, plus coverage for the color-coded results view
    /// (grouping, summary, retry). Root cause of the original bug: RunningForm.RunAsync started the
    /// background batch (Task.Run) before the dialog's Win32 handle existed - see DECISIONS.md
    /// "RunningForm handle-creation race" for the full writeup.
    ///
    /// Unlike DialogUiTests (which never calls the real, blocking ShowDialog()), these tests DO go
    /// through RunningForm's actual modal loop - that's the only way to exercise the exact race the
    /// original bug lived in, and the only way to exercise ShowResults()/RetrySelected() as the
    /// shipped app actually calls them. They all pass autoCloseForTesting: true so the dialog closes
    /// itself the instant results are shown instead of sitting open waiting for someone to click
    /// Close - without it, ShowDialog() pops a real, visible window and blocks the test (and whoever
    /// is at the keyboard) indefinitely. Only AppServices.BuildFake() is used, per the hard safety
    /// rule. [StaFact]/[StaTheory] run on a dedicated STA thread, required for WinForms controls.</summary>
    public class RunningFormTests
    {
        [StaTheory]
        [InlineData(Preset.Conservative)]
        [InlineData(Preset.Balanced)]
        [InlineData(Preset.Aggressive)]
        public async Task RunAsync_DryRunApplyOfEveryPreset_CompletesWithoutThrowing(Preset preset)
        {
            var services = AppServices.BuildFake();
            var tweaks = services.TweakRegistry.ByPreset(preset).ToList();
            Assert.NotEmpty(tweaks);

            var result = await RunningForm.RunAsync(null, "Applying", services, tweaks, ChangeLogAction.Apply, dryRun: true, autoCloseForTesting: true);

            Assert.Equal(tweaks.Count, result.Results.Count);
            Assert.All(result.Results, r => Assert.False(r.Failed));
        }

        [StaFact]
        public async Task RunAsync_AllTweaksDryRun_RepeatedRuns_NeverRacesTheDialogHandle()
        {
            // Repeats the exact race a handful of times - a timing bug doesn't always reproduce on
            // the first try, even with the fix reverted. All tweaks, not just one preset, for the
            // widest possible surface.
            var services = AppServices.BuildFake();

            for (var i = 0; i < 5; i++)
            {
                var result = await RunningForm.RunAsync(null, "Applying", services, services.TweakRegistry.All, ChangeLogAction.Apply, dryRun: true, autoCloseForTesting: true);
                Assert.Equal(services.TweakRegistry.All.Count, result.Results.Count);
            }
        }

        [StaFact]
        public async Task RunAsync_MixedOutcomes_DryRunEntriesAreNeverClassifiedAsPlainSuccess()
        {
            // Every entry in a dry-run batch must carry WasDryRun=true, which ShowResults() uses to
            // style it distinctly from a real success (never green) - this asserts the data contract
            // ShowResults() depends on, at the actual TweakRunner level.
            var services = AppServices.BuildFake();
            var tweaks = services.TweakRegistry.ByPreset(Preset.Conservative).ToList();

            var result = await RunningForm.RunAsync(null, "Applying", services, tweaks, ChangeLogAction.Apply, dryRun: true, autoCloseForTesting: true);

            Assert.All(result.Results, r => Assert.True(r.Result.WasDryRun));
        }

        [StaFact]
        public async Task RunAsync_RealApplyAgainstFakeBackends_Succeeds()
        {
            // DryRun=false but AppServices.BuildFake() - "real" only ever means the in-memory fakes
            // change, never anything on this machine. Same pattern as CliRunnerTests.
            var services = AppServices.BuildFake();
            var tweaks = services.TweakRegistry.ByPreset(Preset.Conservative).ToList();

            var result = await RunningForm.RunAsync(null, "Applying", services, tweaks, ChangeLogAction.Apply, dryRun: false, autoCloseForTesting: true);

            Assert.Equal(tweaks.Count, result.Results.Count);
            Assert.All(result.Results, r => Assert.False(r.Failed));
            Assert.All(result.Results, r => Assert.False(r.Result.WasDryRun));
        }

        [StaFact]
        public async Task RunAsync_UndoBatch_ReportsResultsForEachTweak()
        {
            var services = AppServices.BuildFake();
            var tweaks = services.TweakRegistry.ByPreset(Preset.Conservative).Take(3).ToList();

            await RunningForm.RunAsync(null, "Applying", services, tweaks, ChangeLogAction.Apply, dryRun: false, autoCloseForTesting: true);
            var undoResult = await RunningForm.RunAsync(null, "Undoing", services, tweaks, ChangeLogAction.Undo, dryRun: false, autoCloseForTesting: true);

            Assert.Equal(tweaks.Count, undoResult.Results.Count);
        }

        [StaFact]
        public async Task RunAsync_EmptyTweakList_CompletesWithZeroResults()
        {
            var services = AppServices.BuildFake();

            var result = await RunningForm.RunAsync(null, "Applying", services, System.Array.Empty<TweakDefinition>(), ChangeLogAction.Apply, dryRun: true, autoCloseForTesting: true);

            Assert.Empty(result.Results);
        }
    }

    /// <summary>Directly exercises RunningForm's color-coded results view (ShowResults) via
    /// SetResultsForTesting() - hand-built BatchRunResult scenarios (all-success, all-failed, mixed,
    /// empty, long list, dry-run) without a real batch run or the blocking modal loop. Simulated
    /// results only; no Fake/Real backend distinction matters here since nothing executes a tweak.</summary>
    public class RunningFormResultsViewTests
    {
        private static TweakDefinition MakeTweak(string id, string title) => new TweakDefinition(
            id, title, "desc", TweakCategory.Debloat, RiskLevel.Safe, TweakScope.CurrentUser, false, "source",
            _ => TweakState.Unknown, _ => TweakOperationResult.Success("ok"), _ => TweakOperationResult.Skipped("n/a"));

        private static TweakRunResult MakeResult(string id, string title, TweakOperationResult result, System.Exception ex = null) =>
            new TweakRunResult { Tweak = MakeTweak(id, title), Result = result, Exception = ex };

        [StaFact]
        public void ShowResults_AllSuccess_SummaryIsGreenAndEveryRowMarkedOk()
        {
            using var form = new RunningForm("Applying");
            var batch = new BatchRunResult();
            batch.Results.Add(MakeResult("t1", "Disable telemetry", TweakOperationResult.Success("done")));
            batch.Results.Add(MakeResult("t2", "Disable Cortana", TweakOperationResult.Success("done")));

            form.SetResultsForTesting(batch, AppServices.BuildFake(), ChangeLogAction.Apply, dryRun: false);

            Assert.Contains("2 applied", form.SummaryText);
            Assert.Contains("0 failed", form.SummaryText);
            Assert.Equal(UiStyle.Accent, form.SummaryColor);
            Assert.All(form.ResultRows, r => Assert.StartsWith("[OK]", r.Text));
            Assert.All(form.ResultRows, r => Assert.Equal(UiStyle.Accent, r.Color));
        }

        [StaFact]
        public void ShowResults_AllFailed_SummaryIsRedAndEveryRowMarkedFailed()
        {
            using var form = new RunningForm("Applying");
            var batch = new BatchRunResult();
            batch.Results.Add(MakeResult("t1", "Disable telemetry", null, new System.InvalidOperationException("Access denied")));
            batch.Results.Add(MakeResult("t2", "Disable Cortana", null, new System.InvalidOperationException("Registry key not found")));
            // TweakRunner sets Result to a Failed TweakOperationResult even on exception - mirror that.
            batch.Results[0].Result = TweakOperationResult.Failed("Access denied");
            batch.Results[1].Result = TweakOperationResult.Failed("Registry key not found");

            form.SetResultsForTesting(batch, AppServices.BuildFake(), ChangeLogAction.Apply, dryRun: false);

            Assert.Contains("2 failed", form.SummaryText);
            Assert.Equal(UiStyle.Error, form.SummaryColor);
            Assert.All(form.ResultRows, r => Assert.StartsWith("[FAILED]", r.Text));
            Assert.All(form.ResultRows, r => Assert.Equal(UiStyle.Error, r.Color));
            // Reason is shown inline, one line, no stack trace markers.
            Assert.Contains(form.ResultRows, r => r.Text.Contains("Access denied"));
            Assert.DoesNotContain(form.ResultRows, r => r.Text.Contains("   at ")); // stack-trace-shaped text
        }

        [StaFact]
        public void ShowResults_Mixed_FailedRowsSortBeforeSkippedBeforeSuccess()
        {
            using var form = new RunningForm("Applying");
            var batch = new BatchRunResult();
            batch.Results.Add(MakeResult("t1", "Zzz success tweak", TweakOperationResult.Success("done")));
            batch.Results.Add(MakeResult("t2", "Aaa skipped tweak", TweakOperationResult.Skipped("not present")));
            batch.Results.Add(MakeResult("t3", "Mmm failed tweak", TweakOperationResult.Failed("Access denied")));

            form.SetResultsForTesting(batch, AppServices.BuildFake(), ChangeLogAction.Apply, dryRun: false);

            var rows = form.ResultRows;
            Assert.StartsWith("[FAILED]", rows[0].Text); // failed always first, regardless of title sort
            Assert.StartsWith("[SKIPPED]", rows[1].Text);
            Assert.StartsWith("[OK]", rows[2].Text);
            Assert.Contains("1 applied", form.SummaryText);
            Assert.Contains("1 failed", form.SummaryText);
            Assert.Contains("1 skipped", form.SummaryText);
        }

        [StaFact]
        public void ShowResults_DryRun_NeverStyledAsSuccessGreen()
        {
            using var form = new RunningForm("Applying");
            var batch = new BatchRunResult();
            batch.Results.Add(MakeResult("t1", "Disable telemetry", TweakOperationResult.Preview("would set registry value")));

            form.SetResultsForTesting(batch, AppServices.BuildFake(), ChangeLogAction.Apply, dryRun: true);

            Assert.StartsWith("[DRY RUN]", form.ResultRows[0].Text);
            Assert.NotEqual(UiStyle.Accent, form.ResultRows[0].Color); // never the real-success green
            Assert.NotEqual(UiStyle.Error, form.ResultRows[0].Color); // and not styled as a failure either
        }

        [StaFact]
        public void ShowResults_Empty_ShowsZeroSummaryAndNoRows()
        {
            using var form = new RunningForm("Applying");

            form.SetResultsForTesting(new BatchRunResult(), AppServices.BuildFake(), ChangeLogAction.Apply, dryRun: false);

            Assert.Empty(form.ResultRows);
            Assert.Contains("0 applied", form.SummaryText);
        }

        [StaFact]
        public void ShowResults_LongList_AllRowsPresent_ListViewHandlesScrollingNatively()
        {
            using var form = new RunningForm("Applying");
            var batch = new BatchRunResult();
            for (var i = 0; i < 80; i++)
                batch.Results.Add(MakeResult($"t{i}", $"Tweak number {i}", TweakOperationResult.Success("done")));

            form.SetResultsForTesting(batch, AppServices.BuildFake(), ChangeLogAction.Apply, dryRun: false);

            Assert.Equal(80, form.ResultRows.Count);
        }

        [StaFact]
        public void SelectingFailedRow_EnablesRetryButton_AndShowsErrorDetail()
        {
            using var form = new RunningForm("Applying");
            var batch = new BatchRunResult();
            batch.Results.Add(MakeResult("t1", "Disable telemetry", TweakOperationResult.Failed("Access denied")));

            form.SetResultsForTesting(batch, AppServices.BuildFake(), ChangeLogAction.Apply, dryRun: false);
            form.SelectResultRow(0);

            Assert.True(form.RetryButton.Enabled);
            Assert.Contains("Access denied", form.DetailText);
            Assert.Contains("left unchanged", form.DetailText);
        }

        [StaFact]
        public void SelectingSuccessRow_RetryButtonStaysDisabled()
        {
            using var form = new RunningForm("Applying");
            var batch = new BatchRunResult();
            batch.Results.Add(MakeResult("t1", "Disable telemetry", TweakOperationResult.Success("done")));

            form.SetResultsForTesting(batch, AppServices.BuildFake(), ChangeLogAction.Apply, dryRun: false);
            form.SelectResultRow(0);

            Assert.False(form.RetryButton.Enabled);
        }

        [StaFact]
        public void SelectingFailedRow_InDryRunBatch_RetryStaysDisabled()
        {
            // Retrying a dry-run "failure" (which shouldn't normally happen, but defensively) makes
            // no sense - retry is real-apply-only.
            using var form = new RunningForm("Applying");
            var batch = new BatchRunResult();
            batch.Results.Add(MakeResult("t1", "Disable telemetry", TweakOperationResult.Failed("Access denied")));

            form.SetResultsForTesting(batch, AppServices.BuildFake(), ChangeLogAction.Apply, dryRun: true);
            form.SelectResultRow(0);

            Assert.False(form.RetryButton.Enabled);
        }

        [StaFact]
        public void RetryButton_OnFailedRow_ReRunsJustThatTweak_AndUpdatesRow()
        {
            var services = AppServices.BuildFake();
            // Seed the fake registry so the real tweak (debloat.advertising_id) succeeds on retry.
            var tweak = services.TweakRegistry.Find("debloat.advertising_id");

            using var form = new RunningForm("Applying");
            var batch = new BatchRunResult();
            // Simulate an initial failure for a tweak that will actually succeed when really run.
            batch.Results.Add(new TweakRunResult { Tweak = tweak, Result = TweakOperationResult.Failed("Access denied") });

            form.SetResultsForTesting(batch, services, ChangeLogAction.Apply, dryRun: false);
            form.SelectResultRow(0);
            Assert.True(form.RetryButton.Enabled);

            form.RetryButtonClickForTesting(); // PerformClick() no-ops without a shown dialog - see RunningForm

            Assert.StartsWith("[OK]", form.ResultRows[0].Text);
        }
    }
}
