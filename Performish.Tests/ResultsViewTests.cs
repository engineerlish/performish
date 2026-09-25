using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Performish.Core;
using Performish.Core.Backup;
using Performish.Core.Models;
using Performish.Core.Tweaks;
using Performish.Views;
using Xunit;

namespace Performish.Tests
{
    internal static class AsyncUi
    {
        /// <summary>Waits for a UI operation while pumping Win32 messages, as the shipped app's message loop
        /// does. (A plain await inside [StaFact] does not pump, so marshaled callbacks would never run.)</summary>
        public static T Await<T>(Task<T> task, int timeoutMs = 30000)
        {
            var end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!task.IsCompleted && DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(5); }
            Assert.True(task.IsCompleted, "the UI operation did not complete in time");
            Application.DoEvents();
            return task.Result;
        }
    }

    /// <summary>Async batch runs through the in-window results view (never AppServices.BuildReal(); "real"
    /// here only ever means the in-memory fakes change). Ports RunningFormTests' coverage, including the
    /// handle-creation-race regression, to the in-window view.</summary>
    public class ResultsViewRunTests
    {
        [StaTheory]
        [InlineData(Preset.Conservative)]
        [InlineData(Preset.Balanced)]
        [InlineData(Preset.Aggressive)]
        public void RunAsync_DryRunApplyOfEveryPreset_CompletesWithoutThrowing(Preset preset)
        {
            var services = AppServices.BuildFake();
            var tweaks = services.TweakRegistry.ByPreset(preset).ToList();
            var view = new ResultsView();
            using var form = ViewHost.Show(view);

            var result = AsyncUi.Await(view.RunAsync(services, "Applying", tweaks, ChangeLogAction.Apply, dryRun: true));

            Assert.Equal(tweaks.Count, result.Results.Count);
            Assert.All(result.Results, r => Assert.False(r.Failed));
            Assert.All(result.Results, r => Assert.True(r.Result.WasDryRun));
            Assert.True(view.IsShowingResults);
            Assert.False(view.IsRunning);
        }

        [StaFact]
        public void RunAsync_RepeatedRuns_NeverRacesTheHandle()
        {
            var services = AppServices.BuildFake();
            var view = new ResultsView();
            using var form = ViewHost.Show(view);

            for (var i = 0; i < 5; i++)
            {
                var result = AsyncUi.Await(view.RunAsync(services, "Applying", services.TweakRegistry.All, ChangeLogAction.Apply, dryRun: true));
                Assert.Equal(services.TweakRegistry.All.Count, result.Results.Count);
            }
        }

        [StaFact]
        public void RunAsync_RealApplyAgainstFakeBackends_ShowsAppliedCounterAndLogsProgress()
        {
            var services = AppServices.BuildFake();
            var tweaks = services.TweakRegistry.ByPreset(Preset.Conservative).ToList();
            var view = new ResultsView();
            using var form = ViewHost.Show(view);

            var result = AsyncUi.Await(view.RunAsync(services, "Applying", tweaks, ChangeLogAction.Apply, dryRun: false));

            // Some tweaks report Skipped against the fake backends (nothing to do); the rest apply.
            var applied = result.Results.Count(r => !r.Failed && r.Result.Outcome == OperationOutcome.Success);
            var skipped = result.Results.Count(r => !r.Failed && r.Result.Outcome == OperationOutcome.Skipped);
            Assert.All(result.Results, r => Assert.False(r.Failed));
            Assert.True(applied > 0);
            Assert.Equal(applied, view.GoodTile.Count);
            Assert.Equal(skipped, view.SkippedTile.Count);
            Assert.Equal(0, view.FailedTile.Count);
            Assert.Contains($"{applied} applied", view.SummaryText);
        }

        [StaFact]
        public void RunAsync_WhileRunning_ShowsLogAndCancelButton_ThenSwitchesToResults()
        {
            var services = AppServices.BuildFake();
            var view = new ResultsView();
            using var form = ViewHost.Show(view);

            var task = view.RunAsync(services, "Applying 3 tweak(s)...", services.TweakRegistry.All.Take(3).ToList(), ChangeLogAction.Apply, dryRun: true);
            Assert.True(view.IsRunning);
            Assert.True(view.CancelButton.Visible);
            Assert.Equal("Applying 3 tweak(s)...", view.HeaderText);

            AsyncUi.Await(task);
            Application.DoEvents(); // log lines are marshaled to the UI thread and land on the next pump

            Assert.False(view.CancelButton.Visible);
            Assert.NotEmpty(view.LogText.Trim()); // the live log kept what the batch reported
        }

        [StaFact]
        public void RunAsync_UndoBatch_ReportsResultsForEachTweak()
        {
            var services = AppServices.BuildFake();
            var tweaks = services.TweakRegistry.ByPreset(Preset.Conservative).Take(3).ToList();
            var view = new ResultsView();
            using var form = ViewHost.Show(view);

            AsyncUi.Await(view.RunAsync(services, "Applying", tweaks, ChangeLogAction.Apply, dryRun: false));
            var undo = AsyncUi.Await(view.RunAsync(services, "Undoing", tweaks, ChangeLogAction.Undo, dryRun: false));

            Assert.Equal(tweaks.Count, undo.Results.Count);
            Assert.Equal("Undo complete", view.HeaderText);
        }

        [StaFact]
        public void RunAsync_EmptyTweakList_CompletesWithZeroResults()
        {
            var services = AppServices.BuildFake();
            var view = new ResultsView();
            using var form = ViewHost.Show(view);

            var result = AsyncUi.Await(view.RunAsync(services, "Applying", Array.Empty<TweakDefinition>(), ChangeLogAction.Apply, dryRun: true));

            Assert.Empty(result.Results);
            Assert.Empty(view.ResultRows);
        }
    }

    /// <summary>Directly exercises the results display (grouping, colors, markers, filters, retry) with
    /// hand-built batch results - no batch runs, so no backend distinction matters.</summary>
    public class ResultsViewDisplayTests
    {
        private static TweakDefinition MakeTweak(string id, string title) => new TweakDefinition(
            id, title, "desc", TweakCategory.Debloat, RiskLevel.Safe, TweakScope.CurrentUser, false, "source",
            _ => TweakState.Unknown, _ => TweakOperationResult.Success("ok"), _ => TweakOperationResult.Skipped("n/a"));

        private static TweakRunResult MakeResult(string id, string title, TweakOperationResult result, Exception ex = null) =>
            new TweakRunResult { Tweak = MakeTweak(id, title), Result = result, Exception = ex };

        private static ResultsView Show(BatchRunResult batch, bool dryRun = false, AppServices services = null)
        {
            var view = new ResultsView();
            view.ShowResults(batch, services ?? AppServices.BuildFake(), ChangeLogAction.Apply, dryRun);
            return view;
        }

        [StaFact]
        public void Empty_ShowsHintAndNoResults()
        {
            var view = new ResultsView();
            using var form = ViewHost.Show(view);

            Assert.True(view.EmptyHintVisible);
            Assert.False(view.IsShowingResults);
        }

        [StaFact]
        public void AllSuccess_SummaryIsGreenAndEveryRowMarkedOk()
        {
            var batch = new BatchRunResult();
            batch.Results.Add(MakeResult("t1", "Disable telemetry", TweakOperationResult.Success("done")));
            batch.Results.Add(MakeResult("t2", "Disable Cortana", TweakOperationResult.Success("done")));

            var view = Show(batch);

            Assert.Contains("2 applied", view.SummaryText);
            Assert.Contains("0 failed", view.SummaryText);
            Assert.Equal(UiStyle.Accent, view.SummaryColor);
            Assert.All(view.ResultRows, r => Assert.StartsWith("[OK]", r.Text));
            Assert.All(view.ResultRows, r => Assert.Equal(UiStyle.Accent, r.Color));
            Assert.Equal(2, view.GoodTile.Count);
        }

        [StaFact]
        public void AllFailed_SummaryIsRedAndRowsShowShortReasonNeverAStackTrace()
        {
            var batch = new BatchRunResult();
            batch.Results.Add(MakeResult("t1", "Disable telemetry", TweakOperationResult.Failed("Access denied"), new InvalidOperationException("Access denied")));
            batch.Results.Add(MakeResult("t2", "Disable Cortana", TweakOperationResult.Failed("Registry key not found"), new InvalidOperationException("Registry key not found")));

            var view = Show(batch);

            Assert.Contains("2 failed", view.SummaryText);
            Assert.Equal(UiStyle.Error, view.SummaryColor);
            Assert.All(view.ResultRows, r => Assert.StartsWith("[FAILED]", r.Text));
            Assert.All(view.ResultRows, r => Assert.Equal(UiStyle.Error, r.Color));
            Assert.Contains(view.ResultRows, r => r.Text.Contains("Access denied"));
            Assert.DoesNotContain(view.ResultRows, r => r.Text.Contains("   at "));
            Assert.Equal(2, view.FailedTile.Count);
        }

        [StaFact]
        public void Mixed_FailedRowsSortBeforeSkippedBeforeSuccess()
        {
            var batch = new BatchRunResult();
            batch.Results.Add(MakeResult("t1", "Zzz success tweak", TweakOperationResult.Success("done")));
            batch.Results.Add(MakeResult("t2", "Aaa skipped tweak", TweakOperationResult.Skipped("not present")));
            batch.Results.Add(MakeResult("t3", "Mmm failed tweak", TweakOperationResult.Failed("Access denied")));

            var view = Show(batch);

            var rows = view.ResultRows;
            Assert.StartsWith("[FAILED]", rows[0].Text);
            Assert.StartsWith("[SKIPPED]", rows[1].Text);
            Assert.StartsWith("[OK]", rows[2].Text);
            Assert.Contains("1 applied", view.SummaryText);
            Assert.Contains("1 failed", view.SummaryText);
            Assert.Contains("1 skipped", view.SummaryText);
        }

        [StaFact]
        public void DryRun_NeverStyledAsSuccessGreen_AndCounterSaysWouldApply()
        {
            var batch = new BatchRunResult();
            batch.Results.Add(MakeResult("t1", "Disable telemetry", TweakOperationResult.Preview("would set registry value")));

            var view = Show(batch, dryRun: true);

            Assert.StartsWith("[DRY RUN]", view.ResultRows[0].Text);
            Assert.NotEqual(UiStyle.Accent, view.ResultRows[0].Color);
            Assert.NotEqual(UiStyle.Error, view.ResultRows[0].Color);
            Assert.Equal("WOULD APPLY", view.GoodTile.Caption);
            Assert.Equal("Dry run complete", view.HeaderText);
        }

        [StaFact]
        public void CancelledBatch_SaysSoInTheSummary()
        {
            var batch = new BatchRunResult { Cancelled = true };
            batch.Results.Add(MakeResult("t1", "Disable telemetry", TweakOperationResult.Success("done")));

            var view = Show(batch);

            Assert.Contains("cancelled", view.SummaryText);
        }

        [StaFact]
        public void CounterTiles_FilterTheList_AndClickingAgainClearsTheFilter()
        {
            var batch = new BatchRunResult();
            batch.Results.Add(MakeResult("t1", "Success one", TweakOperationResult.Success("done")));
            batch.Results.Add(MakeResult("t2", "Skipped one", TweakOperationResult.Skipped("n/a")));
            batch.Results.Add(MakeResult("t3", "Failed one", TweakOperationResult.Failed("nope")));
            var view = Show(batch);
            Assert.Equal(3, view.ResultRows.Count);

            view.FailedTile.PerformActivate();
            Assert.Single(view.ResultRows);
            Assert.StartsWith("[FAILED]", view.ResultRows[0].Text);
            Assert.True(view.FailedTile.Active);

            view.SkippedTile.PerformActivate();
            Assert.Single(view.ResultRows);
            Assert.StartsWith("[SKIPPED]", view.ResultRows[0].Text);

            view.SkippedTile.PerformActivate();
            Assert.Equal(3, view.ResultRows.Count);
            Assert.False(view.SkippedTile.Active);
        }

        [StaFact]
        public void CounterTiles_AreKeyboardReachableAndExposeAccessibleNames()
        {
            var batch = new BatchRunResult();
            batch.Results.Add(MakeResult("t1", "Failed one", TweakOperationResult.Failed("nope")));
            var view = Show(batch);

            Assert.True(view.FailedTile.TabStop);
            Assert.Equal(AccessibleRole.PushButton, view.FailedTile.AccessibleRole);
            Assert.Contains("1 failed", view.FailedTile.AccessibleName);
        }

        [StaFact]
        public void LongList_AllRowsPresent()
        {
            var batch = new BatchRunResult();
            for (var i = 0; i < 80; i++) batch.Results.Add(MakeResult($"t{i}", $"Tweak number {i}", TweakOperationResult.Success("done")));

            Assert.Equal(80, Show(batch).ResultRows.Count);
        }

        [StaFact]
        public void SelectingFailedRow_EnablesRetry_AndShowsErrorDetail()
        {
            var batch = new BatchRunResult();
            batch.Results.Add(MakeResult("t1", "Disable telemetry", TweakOperationResult.Failed("Access denied")));

            var view = Show(batch);
            view.SelectResultRow(0);

            Assert.True(view.RetryButton.Enabled);
            Assert.Contains("Access denied", view.DetailText);
            Assert.Contains("left unchanged", view.DetailText);
        }

        [StaFact]
        public void SelectingSuccessRow_RetryStaysDisabled()
        {
            var batch = new BatchRunResult();
            batch.Results.Add(MakeResult("t1", "Disable telemetry", TweakOperationResult.Success("done")));

            var view = Show(batch);
            view.SelectResultRow(0);

            Assert.False(view.RetryButton.Enabled);
        }

        [StaFact]
        public void FailedRowInADryRunBatch_RetryStaysDisabled()
        {
            var batch = new BatchRunResult();
            batch.Results.Add(MakeResult("t1", "Disable telemetry", TweakOperationResult.Failed("Access denied")));

            var view = Show(batch, dryRun: true);
            view.SelectResultRow(0);

            Assert.False(view.RetryButton.Enabled);
        }

        [StaFact]
        public void Retry_OnFailedRow_ReRunsJustThatTweak_AndUpdatesRowAndCounters()
        {
            var services = AppServices.BuildFake();
            var tweak = services.TweakRegistry.Find("debloat.advertising_id");
            var batch = new BatchRunResult();
            batch.Results.Add(new TweakRunResult { Tweak = tweak, Result = TweakOperationResult.Failed("Access denied") });

            var view = Show(batch, services: services);
            view.SelectResultRow(0);
            Assert.True(view.RetryButton.Enabled);
            Assert.Equal(1, view.FailedTile.Count);

            view.RetryButtonClickForTesting();

            Assert.StartsWith("[OK]", view.ResultRows[0].Text);
            Assert.Equal(0, view.FailedTile.Count);
            Assert.Equal(1, view.GoodTile.Count);
        }

        [StaFact]
        public void BackAndExport_RaiseTheirEvents()
        {
            var view = new ResultsView();
            using var form = ViewHost.Show(view);
            int back = 0, export = 0;
            view.BackRequested += () => back++;
            view.ExportRequested += () => export++;

            var buttons = Descendants(view).OfType<Button>().ToList();
            buttons.First(b => b.Text.StartsWith("< Back")).PerformClick();
            buttons.First(b => b.Text.StartsWith("Export")).PerformClick();

            Assert.Equal(1, back);
            Assert.Equal(1, export);
        }

        private static System.Collections.Generic.IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control c in root.Controls)
            {
                yield return c;
                foreach (var d in Descendants(c)) yield return d;
            }
        }
    }
}
