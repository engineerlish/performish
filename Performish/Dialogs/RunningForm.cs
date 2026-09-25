using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Performish.Core;
using Performish.Core.Backup;
using Performish.Core.Models;
using Performish.Core.Tweaks;
using static Performish.ResultPresenter;

namespace Performish.Dialogs
{
    /// <summary>Modal progress/result dialog for an apply/undo/revert batch. Never blocks the UI
    /// thread - the batch runs on a background task while this dialog's own message loop keeps
    /// pumping, so the live log keeps appending and Cancel stays clickable throughout. Once the batch
    /// finishes, the live log is replaced by a small, color-coded, grouped results list (failures
    /// first) with click-to-expand detail and a Retry button for failed entries - the "apply results
    /// popup" from the task brief. Close is disabled until the batch finishes so the dialog can't be
    /// dismissed mid-run out from under a live TweakExecutionContext.</summary>
    public sealed class RunningForm : Form
    {
        private readonly RichTextBox _log;
        private readonly Label _summaryLabel;
        private readonly ListView _resultsList;
        private readonly RichTextBox _detail;
        private readonly Panel _resultsHost;
        private readonly Button _cancelButton;
        private readonly Button _closeButton;
        private readonly Button _retryButton;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private AppServices _services;
        private ChangeLogAction _action;
        private bool _dryRun;

        // Tracked explicitly rather than read back from _resultsList.SelectedItems: a ListView only
        // reflects Selected-state changes through SelectedItems once it has a real native HWND, which
        // this dialog doesn't have unless it's actually shown via ShowDialog(). Every place that
        // selects a row - the auto-select in ShowResults(), the test-only SelectResultRow(), and a
        // real user click - goes through SelectRow()/OnRowSelected() so behavior is identical either
        // way.
        private TweakRunResult _selectedResult;

        public BatchRunResult Result { get; private set; }

        /// <summary>Test-only accessors - let Performish.Tests assert on what's actually rendered
        /// (summary text/color, per-row text/color, detail panel content, button enabled state)
        /// without needing to know RunningForm's private layout. See Dialogs/RunningFormTests.cs.</summary>
        public string SummaryText => _summaryLabel.Text;
        public Color SummaryColor => _summaryLabel.ForeColor;
        public IReadOnlyList<(string Text, Color Color)> ResultRows =>
            _resultsList.Items.Cast<ListViewItem>().Select(i => (i.Text, i.ForeColor)).ToList();
        public string DetailText => _detail.Text;
        public Button RetryButton => _retryButton;
        public Button CloseButton => _closeButton;
        public bool IsShowingResults => _resultsHost.Visible;

        /// <summary>Test-only: selects a row and updates the detail panel/Retry state directly.</summary>
        public void SelectResultRow(int index) => SelectRow(index);

        /// <summary>Test-only: populates the results view directly from a hand-built BatchRunResult,
        /// without running a real (or fake) batch or going through the blocking modal ShowDialog()
        /// loop - lets tests exercise ShowResults()'s grouping/coloring/summary logic for scenarios
        /// (all-success, all-failed, mixed, empty, long list) that would be tedious to construct via
        /// an actual TweakRunner batch. See Dialogs/RunningFormTests.cs.</summary>
        public void SetResultsForTesting(BatchRunResult result, AppServices services, ChangeLogAction action, bool dryRun)
        {
            _services = services;
            _action = action;
            _dryRun = dryRun;
            ShowResults(result);
        }

        public RunningForm(string title)
        {
            Text = title;
            Width = 720;
            Height = 520;
            BackColor = UiStyle.Background;
            Font = UiStyle.Mono;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ControlBox = false; // closing via the X mid-run would abandon a live context; use Close button

            _log = UiStyle.MakeConsole();

            _summaryLabel = UiStyle.MakeLabel("", UiStyle.Foreground, bold: true);
            _summaryLabel.Dock = DockStyle.Top;
            _summaryLabel.Height = 28;
            _summaryLabel.Padding = new Padding(0, 4, 0, 4);

            _resultsList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                GridLines = false,
                HeaderStyle = ColumnHeaderStyle.None,
                BackColor = UiStyle.Background,
                ForeColor = UiStyle.Foreground,
                Font = UiStyle.Mono,
                BorderStyle = BorderStyle.None,
                Visible = false
            };
            _resultsList.Columns.Add("Status", 620);
            // Real user clicks (handle exists once the dialog is shown via ShowDialog): read the
            // native selection back and route it through the same OnRowSelected() every other
            // selection path uses.
            _resultsList.SelectedIndexChanged += (s, e) =>
                OnRowSelected(_resultsList.SelectedItems.Count > 0 ? _resultsList.SelectedItems[0].Tag as TweakRunResult : null);

            _detail = UiStyle.MakeConsole();
            _detail.Height = 90;
            _detail.Visible = false;

            _resultsHost = new Panel { Dock = DockStyle.Fill, BackColor = UiStyle.Background, Visible = false };
            var detailHost = new Panel { Dock = DockStyle.Bottom, Height = 100, BackColor = UiStyle.Background, Padding = new Padding(0, 4, 0, 0) };
            detailHost.Controls.Add(_detail);
            _resultsHost.Controls.Add(_resultsList);
            _resultsHost.Controls.Add(detailHost);
            _resultsHost.Controls.Add(_summaryLabel);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                BackColor = UiStyle.Background,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(10)
            };

            _closeButton = UiStyle.MakeButton("Close");
            _closeButton.Enabled = false;
            _closeButton.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };

            _cancelButton = UiStyle.MakeButton("Cancel", UiStyle.Error);
            _cancelButton.Click += (s, e) =>
            {
                _cts.Cancel();
                _cancelButton.Enabled = false;
                _cancelButton.Text = "Cancelling...";
            };

            _retryButton = UiStyle.MakeButton("Retry", UiStyle.GradientMid);
            _retryButton.Enabled = false;
            _retryButton.Click += (s, e) => RetrySelected();

            buttonPanel.Controls.Add(_closeButton);
            buttonPanel.Controls.Add(_retryButton);
            buttonPanel.Controls.Add(_cancelButton);

            var host = new Panel { Dock = DockStyle.Fill, BackColor = UiStyle.Background, Padding = new Padding(12) };
            host.Controls.Add(_resultsHost);
            host.Controls.Add(_log);

            Controls.Add(host);
            Controls.Add(buttonPanel);

            CancelButton = null; // Esc must not silently dismiss a running/just-finished batch dialog
        }

        private void AppendLine(string text, Color color)
        {
            void Do()
            {
                _log.SelectionStart = _log.TextLength;
                _log.SelectionLength = 0;
                _log.SelectionColor = color;
                _log.AppendText(text + Environment.NewLine);
                _log.SelectionStart = _log.TextLength;
                _log.ScrollToCaret();
            }

            if (_log.InvokeRequired) _log.BeginInvoke((Action)Do);
            else Do();
        }

        // ---- Results view ------------------------------------------------------------------------

        private void ShowResults(BatchRunResult result)
        {
            Result = result;

            var succeeded = result.Results.Count(r => Classify(r) == ResultStatus.Success);
            var failed = result.Results.Count(r => Classify(r) == ResultStatus.Failed);
            var skipped = result.Results.Count(r => Classify(r) == ResultStatus.Skipped);
            var dryRun = result.Results.Count(r => Classify(r) == ResultStatus.DryRunPreview);

            var summaryParts = new List<string>();
            if (dryRun > 0) summaryParts.Add($"{dryRun} would apply");
            summaryParts.Add($"{succeeded} applied");
            summaryParts.Add($"{failed} failed");
            summaryParts.Add($"{skipped} skipped");
            var cancelledSuffix = result.Cancelled ? " (cancelled - remaining tweaks not started)" : "";
            _summaryLabel.Text = string.Join(", ", summaryParts) + cancelledSuffix;
            _summaryLabel.ForeColor = failed > 0 ? UiStyle.Error : (dryRun > 0 ? UiStyle.BrightAccent : UiStyle.Accent);

            _resultsList.Items.Clear();
            foreach (var r in result.Results.OrderBy(r => SortOrder(Classify(r))).ThenBy(r => r.Tweak.Title))
            {
                var (marker, color) = StyleFor(Classify(r));
                var reason = ShortReason(r);
                var text = string.IsNullOrEmpty(reason) ? $"{marker}  {r.Tweak.Title}" : $"{marker}  {r.Tweak.Title} - {reason}";
                var item = new ListViewItem(text) { Tag = r, ForeColor = color };
                _resultsList.Items.Add(item);
            }

            _log.Visible = false;
            _resultsHost.Visible = true;

            if (_resultsList.Items.Count > 0) SelectRow(0);
        }

        /// <summary>Selects one row by index and updates the detail panel/Retry state to match -
        /// used for the auto-select-first-row on ShowResults(), the test-only SelectResultRow(), and
        /// re-selecting a row after Retry updates it. Sets Selected on the ListViewItem for visual
        /// highlighting, but - unlike the native SelectedIndexChanged path below - never reads that
        /// state back: a ListView only reflects Selected-state changes through SelectedItems once it
        /// has a real native HWND, which this dialog doesn't have unless it's actually shown via
        /// ShowDialog(). Tracking the selected result explicitly in OnRowSelected() keeps every
        /// selection path - programmatic or a real click - working identically either way.</summary>
        private void SelectRow(int index)
        {
            if (index < 0 || index >= _resultsList.Items.Count) return;
            foreach (ListViewItem item in _resultsList.Items) item.Selected = false;
            var target = _resultsList.Items[index];
            target.Selected = true;
            OnRowSelected(target.Tag as TweakRunResult);
        }

        private void OnRowSelected(TweakRunResult r)
        {
            _selectedResult = r;
            _detail.Clear();
            if (r == null)
            {
                _retryButton.Enabled = false;
                return;
            }

            var status = Classify(r);
            AppendDetail(r.Tweak.Title, UiStyle.BrightAccent);
            AppendDetail($"Attempted: {(_action == ChangeLogAction.Apply ? "Apply" : "Undo")} - {r.Tweak.Description}", UiStyle.Foreground);

            if (status == ResultStatus.Failed)
            {
                AppendDetail("Error: " + (r.Result?.Message ?? r.Exception?.Message ?? "Unknown error"), UiStyle.Error);
                AppendDetail("The tweak was left unchanged - nothing partial was applied. " +
                    "You can retry, or check the suggested cause above (common causes: access denied - " +
                    "confirm Performish is running as Administrator; registry key not found - the setting " +
                    "may not exist on this Windows build/edition).", UiStyle.Dim);
            }
            else if (status == ResultStatus.Skipped)
            {
                AppendDetail("Skipped: " + (r.Result?.Message ?? ""), UiStyle.Dim);
            }
            else if (status == ResultStatus.DryRunPreview)
            {
                AppendDetail("Dry run - would have done: " + (r.Result?.Message ?? ""), UiStyle.GradientMid);
            }
            else
            {
                AppendDetail("Succeeded: " + (r.Result?.Message ?? ""), UiStyle.Accent);
            }

            // Retry only ever makes sense for a real (non-dry-run) Failed result - retrying a dry-run
            // preview or an already-successful/skipped row has nothing meaningful to redo.
            _retryButton.Enabled = status == ResultStatus.Failed && !_dryRun;
        }

        private void AppendDetail(string text, Color color)
        {
            _detail.SelectionStart = _detail.TextLength;
            _detail.SelectionColor = color;
            _detail.AppendText(text + Environment.NewLine);
        }

        /// <summary>Retries a single failed tweak. Runs synchronously on the UI thread rather than
        /// via Task.Run - unlike the full batch in RunAsync() (which can cover 60+ tweaks and some
        /// genuinely slow cleanup work), a retry is always exactly one tweak, so the brief block is
        /// unnoticeable. This also sidesteps a real deadlock: Task.Run's continuation resumes through
        /// the auto-installed WindowsFormsSynchronizationContext, which only gets pumped while a real
        /// modal ShowDialog() loop is running - with no dialog shown (as in tests that drive this
        /// through SetResultsForTesting), that continuation would never run.</summary>
        /// <summary>Test-only: invokes the same logic as clicking Retry, without going through
        /// Button.PerformClick() - PerformClick() is a no-op unless the button's whole ancestor chain
        /// (up to and including the Form itself) is Visible, which only happens once the dialog is
        /// actually shown via ShowDialog(). Tests drive the results view through SetResultsForTesting()
        /// without ever showing the dialog, so they call this instead.</summary>
        public void RetryButtonClickForTesting() => RetrySelected();

        private void RetrySelected()
        {
            if (_selectedResult is not TweakRunResult failed) return;
            if (Classify(failed) != ResultStatus.Failed) return;

            _retryButton.Enabled = false;
            _retryButton.Text = "Retrying...";
            Application.DoEvents(); // let the button repaint before the (brief) retry runs

            var tweak = failed.Tweak;
            var ctx = _services.CreateContext(_dryRun, line => AppendLine("  " + line, UiStyle.Dim));
            var batch = _action == ChangeLogAction.Apply
                ? _services.Runner.ApplyBatch(new[] { tweak }, ctx, createRestorePoint: false)
                : _services.Runner.UndoBatch(new[] { tweak }, ctx);
            var retryResult = batch.Results.Count > 0 ? batch.Results[0] : null;

            _retryButton.Text = "Retry";

            if (retryResult == null) return;

            // Replace the old result for this tweak everywhere it's tracked: the ListView row, and
            // Result.Results (so a subsequent Close/report/retry sees the updated outcome, not the
            // stale failure).
            var index = Result.Results.FindIndex(r => r.Tweak.Id == tweak.Id);
            if (index >= 0) Result.Results[index] = retryResult;
            ShowResults(Result);

            var newIndex = _resultsList.Items.Cast<ListViewItem>().ToList()
                .FindIndex(i => i.Tag is TweakRunResult r && r.Tweak.Id == tweak.Id);
            if (newIndex >= 0) SelectRow(newIndex);
        }

        /// <summary>Runs the given batch (apply or undo) on a background thread, streaming live log
        /// lines into the dialog, then shows the color-coded results view once it completes.
        /// <paramref name="autoCloseForTesting"/> is test-only: the shipped app always leaves Close
        /// disabled until the user reviews the results and dismisses the dialog themselves, but a real
        /// ShowDialog() call otherwise blocks indefinitely waiting for that click - tests that need to
        /// exercise the real modal loop (e.g. the Win32-handle-creation race this fixes - see
        /// DECISIONS.md "RunningForm handle-creation race") pass true so the dialog closes itself the
        /// instant results are shown, instead of sitting open waiting for a human.</summary>
        public static async Task<BatchRunResult> RunAsync(IWin32Window owner, string title,
            AppServices services, IReadOnlyList<TweakDefinition> tweaks, ChangeLogAction action, bool dryRun,
            bool wantsRestorePoint = false, bool autoCloseForTesting = false)
        {
            using var dialog = new RunningForm(title);
            dialog._services = services;
            dialog._action = action;
            dialog._dryRun = dryRun;

            // Force the dialog's Win32 window handle to exist, synchronously, on the UI thread -
            // BEFORE any background work starts. Root cause of the "Failed to set Win32 parent
            // window of the Control" crash (see DECISIONS.md "RunningForm handle-creation race"):
            // Control.InvokeRequired silently returns false when nothing in the control's parent
            // chain has a handle yet, so if the background task below calls ctx.Log() -> AppendLine()
            // before ShowDialog() (further down) has created the handle, that first log line runs
            // directly on the background thread instead of being marshaled via BeginInvoke -
            // corrupting the control's thread affinity right as the UI thread tries to create the
            // real Win32 window a moment later. Accessing .Handle here forces WinForms to create the
            // window on this (UI) thread now; ShowDialog(owner) below still re-parents it to `owner`
            // correctly even though the handle already exists (Form.ShowDialog explicitly re-applies
            // GWL_HWNDPARENT in that case) - this is a normal, supported WinForms pattern.
            _ = dialog.Handle;

            var tweakList = tweaks.ToList();
            var runTask = Task.Run(() =>
            {
                var ctx = services.CreateContext(dryRun, line => dialog.AppendLine("  " + line, UiStyle.Dim));
                return action == ChangeLogAction.Apply
                    ? services.Runner.ApplyBatch(tweakList, ctx, wantsRestorePoint, dialog._cts.Token)
                    : services.Runner.UndoBatch(tweakList, ctx, dialog._cts.Token);
            });

            _ = runTask.ContinueWith(t =>
            {
                dialog.BeginInvoke((Action)(() =>
                {
                    if (t.IsFaulted)
                    {
                        dialog.AppendLine("", UiStyle.Foreground);
                        dialog.AppendLine($"  Unexpected error: {t.Exception?.GetBaseException().Message}", UiStyle.Error);
                        dialog.ShowResults(new BatchRunResult());
                    }
                    else
                    {
                        dialog.ShowResults(t.Result);
                    }

                    dialog._cancelButton.Enabled = false;
                    dialog._closeButton.Enabled = true;
                    dialog._closeButton.Focus();

                    if (autoCloseForTesting)
                    {
                        dialog.DialogResult = DialogResult.OK;
                        dialog.Close();
                    }
                }));
            }, TaskScheduler.Default);

            dialog.ShowDialog(owner);
            return dialog.Result ?? new BatchRunResult();
        }
    }
}
