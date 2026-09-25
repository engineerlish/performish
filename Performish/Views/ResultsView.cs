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

namespace Performish.Views
{
    /// <summary>The in-window replacement for the apply/undo progress + results popup. While a batch
    /// runs it shows a live log and a Cancel button; when it finishes it shows three counters (which
    /// double as filters), a failures-first list with a plain-text marker on every row, a detail panel,
    /// and Retry for a failed tweak. Nothing here opens a window.</summary>
    public sealed class ResultsView : UserControl
    {
        private enum Mode { Empty, Running, Done }

        private readonly Label _header, _summary, _empty;
        private readonly CounterTile _tileGood, _tileFailed, _tileSkipped;
        private readonly Panel _tiles, _listHost;
        private readonly RichTextBox _log, _detail;
        private readonly ListView _list;
        private readonly Button _backButton, _exportButton, _retryButton, _cancelButton;
        private CancellationTokenSource _cts;

        private Mode _mode = Mode.Empty;
        private AppServices _services;
        private ChangeLogAction _action;
        private bool _dryRun;
        private int _filter; // 0 all, 1 good, 2 failed, 3 skipped
        // Tracked explicitly rather than read back from _list.SelectedItems: a ListView only reflects
        // selection changes once it has a native handle (see DECISIONS.md, RunningForm SelectRow).
        private TweakRunResult _selected;

        public event Action BackRequested;
        public event Action ExportRequested;

        public BatchRunResult Result { get; private set; }
        public bool IsRunning => _mode == Mode.Running;
        public bool IsShowingResults => _mode == Mode.Done;

        // ---- test/automation surface ---------------------------------------------------------------

        public string HeaderText => _header.Text;
        public string SummaryText => _summary.Text;
        public Color SummaryColor => _summary.ForeColor;
        public IReadOnlyList<(string Text, Color Color)> ResultRows => _list.Items.Cast<ListViewItem>().Select(i => (i.Text, i.ForeColor)).ToList();
        public string DetailText => _detail.Text;
        public string LogText => _log.Text;
        public Button RetryButton => _retryButton;
        public Button CancelButton => _cancelButton;
        public CounterTile GoodTile => _tileGood;
        public CounterTile FailedTile => _tileFailed;
        public CounterTile SkippedTile => _tileSkipped;
        public bool EmptyHintVisible => _empty.Visible;
        public void SelectResultRow(int index) => SelectRow(index);
        public void RetryButtonClickForTesting() => RetrySelected();
        public void CancelForTesting() => RequestCancel();

        public ResultsView()
        {
            BackColor = UiStyle.Background;
            ForeColor = UiStyle.Foreground;
            Font = UiStyle.Mono;
            AccessibleName = "Results";
            AccessibleRole = AccessibleRole.Pane;

            _header = new Label { Font = UiStyle.Big, ForeColor = UiStyle.Foreground, AutoSize = false, Dock = DockStyle.Top, Height = 40, TextAlign = ContentAlignment.MiddleLeft };
            _summary = new Label { Font = UiStyle.MonoBold, ForeColor = UiStyle.Foreground, AutoSize = false, Dock = DockStyle.Top, Height = 28, TextAlign = ContentAlignment.MiddleLeft };

            _tileGood = new CounterTile { Location = new Point(0, 4) };
            _tileFailed = new CounterTile { Location = new Point(236, 4) };
            _tileSkipped = new CounterTile { Location = new Point(472, 4) };
            _tileGood.Clicked += (s, e) => SetFilter(1);
            _tileFailed.Clicked += (s, e) => SetFilter(2);
            _tileSkipped.Clicked += (s, e) => SetFilter(3);
            _tiles = new Panel { Dock = DockStyle.Top, Height = 132, BackColor = UiStyle.Background, Visible = false };
            _tiles.Controls.Add(_tileGood);
            _tiles.Controls.Add(_tileFailed);
            _tiles.Controls.Add(_tileSkipped);

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HeaderStyle = ColumnHeaderStyle.None,
                BackColor = UiStyle.Background,
                ForeColor = UiStyle.Foreground,
                Font = UiStyle.Mono,
                BorderStyle = BorderStyle.None,
                Visible = false,
                AccessibleName = "Results, failures first"
            };
            _list.Columns.Add("Status", 1400);
            _list.Resize += (s, e) => { if (_list.Columns.Count > 0) _list.Columns[0].Width = Math.Max(200, _list.ClientSize.Width - 4); };
            UiStyle.UseDarkScrollbars(_list);
            // Owner-drawn so the selected row is a dark highlight with the row's own status color kept,
            // not the system's white inverted bar.
            _list.OwnerDraw = true;
            _list.DrawColumnHeader += (s, e) => e.DrawDefault = true;
            _list.DrawItem += (s, e) =>
            {
                using (var back = new SolidBrush(e.Item.Selected ? UiStyle.Panel2 : UiStyle.Background)) e.Graphics.FillRectangle(back, e.Bounds);
                if (e.Item.Selected) using (var bar = new SolidBrush(UiStyle.Accent)) e.Graphics.FillRectangle(bar, e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.Item.Text, UiStyle.Mono, new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height),
                    e.Item.ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                if (e.Item.Focused && _list.Focused) using (var p = new Pen(UiStyle.BrightAccent)) e.Graphics.DrawRectangle(p, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
            };
            _list.SelectedIndexChanged += (s, e) =>
                OnRowSelected(_list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as TweakRunResult : null);

            _log = UiStyle.MakeConsole();
            _log.Visible = false;
            _log.AccessibleName = "Live log";

            _empty = new Label { Text = "No batch has run yet. Choose tweaks, review them, and apply - the outcome appears here.", ForeColor = UiStyle.Dim, Font = UiStyle.Mono, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft, Padding = new Padding(4, 16, 0, 0) };

            _listHost = new Panel { Dock = DockStyle.Fill, BackColor = UiStyle.Background, Padding = new Padding(0, 6, 0, 0) };
            _listHost.Controls.Add(_list);
            _listHost.Controls.Add(_log);
            _listHost.Controls.Add(_empty);

            _detail = UiStyle.MakeConsole();
            _detail.WordWrap = true;
            _detail.ScrollBars = RichTextBoxScrollBars.Vertical;
            _detail.AccessibleName = "Result detail";
            var detailHost = new Panel { Dock = DockStyle.Bottom, Height = 112, BackColor = UiStyle.Panel, Padding = new Padding(16, 10, 16, 6), Visible = false };
            detailHost.Paint += (s, e) => { using var p = new Pen(UiStyle.Line); e.Graphics.DrawLine(p, 0, 0, detailHost.Width, 0); };
            _detail.BackColor = UiStyle.Panel;
            detailHost.Controls.Add(_detail);

            var bar = new Panel { Dock = DockStyle.Bottom, Height = 62, BackColor = UiStyle.Background, Padding = new Padding(0, 8, 0, 8) };
            bar.Paint += (s, e) => { using var p = new Pen(UiStyle.Line); e.Graphics.DrawLine(p, 0, 0, bar.Width, 0); };
            var left = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, BackColor = UiStyle.Background, WrapContents = false };
            _backButton = UiStyle.MakeButton("< Back to tweaks", UiStyle.Foreground);
            _backButton.Click += (s, e) => BackRequested?.Invoke();
            _exportButton = UiStyle.MakeButton("Export report...", UiStyle.Foreground);
            _exportButton.Click += (s, e) => ExportRequested?.Invoke();
            left.Controls.Add(_backButton);
            left.Controls.Add(_exportButton);
            var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, BackColor = UiStyle.Background, WrapContents = false };
            _cancelButton = UiStyle.MakeButton("Cancel batch", UiStyle.Error);
            _cancelButton.Visible = false;
            _cancelButton.Click += (s, e) => RequestCancel();
            _retryButton = UiStyle.MakeButton("Retry this tweak", UiStyle.GradientMid);
            _retryButton.Enabled = false;
            _retryButton.Visible = false;
            _retryButton.Click += (s, e) => RetrySelected();
            right.Controls.Add(_cancelButton);
            right.Controls.Add(_retryButton);
            bar.Controls.Add(left);
            bar.Controls.Add(right);

            Controls.Add(_listHost);
            Controls.Add(detailHost);
            Controls.Add(bar);
            Controls.Add(_tiles);
            Controls.Add(_summary);
            Controls.Add(_header);
            _detailHost = detailHost;

            _header.Text = "Results";
            _summary.Text = "";
        }

        private readonly Panel _detailHost;

        // ---- running -------------------------------------------------------------------------------

        /// <summary>Runs the batch on a background thread, streaming log lines into the live log, then
        /// switches to the results display. Never blocks the UI thread; Cancel stays clickable.</summary>
        public async Task<BatchRunResult> RunAsync(AppServices services, string title,
            IReadOnlyList<TweakDefinition> tweaks, ChangeLogAction action, bool dryRun, bool wantsRestorePoint = false)
        {
            _services = services;
            _action = action;
            _dryRun = dryRun;

            // Force the native handle to exist on the UI thread BEFORE any background work starts: with
            // no handle, InvokeRequired is false and the first log line would touch the control from
            // the worker thread (the crash described in DECISIONS.md "RunningForm handle-creation race").
            _ = Handle;

            EnterRunning(title);
            var tweakList = tweaks.ToList();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            BatchRunResult result;
            try
            {
                // ConfigureAwait(false) + an explicit hop back below: this view never relies on whatever
                // ambient SynchronizationContext the caller happens to have to keep control access on the
                // UI thread (touching a control from a pool thread deadlocks rather than throws).
                result = await Task.Run(() =>
                {
                    var ctx = services.CreateContext(dryRun, line => AppendLog("  " + line, UiStyle.Dim));
                    return action == ChangeLogAction.Apply
                        ? services.Runner.ApplyBatch(tweakList, ctx, wantsRestorePoint, token)
                        : services.Runner.UndoBatch(tweakList, ctx, token);
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await OnUiThreadAsync(() =>
                {
                    AppendLog("", UiStyle.Foreground);
                    AppendLog($"  Unexpected error: {ex.GetBaseException().Message}", UiStyle.Error);
                }).ConfigureAwait(false);
                result = new BatchRunResult();
            }

            await OnUiThreadAsync(() => ShowResults(result, services, action, dryRun)).ConfigureAwait(false);
            return result;
        }

        private Task OnUiThreadAsync(Action action)
        {
            if (!InvokeRequired) { action(); return Task.CompletedTask; }
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            BeginInvoke((Action)(() =>
            {
                try { action(); tcs.SetResult(true); }
                catch (Exception ex) { tcs.SetException(ex); }
            }));
            return tcs.Task;
        }

        private void EnterRunning(string title)
        {
            _mode = Mode.Running;
            _header.Text = title;
            _summary.Text = "Working... the log below updates live. Nothing else can start until this finishes.";
            _summary.ForeColor = UiStyle.Dim;
            _log.Clear();
            _tiles.Visible = false;
            _detailHost.Visible = false;
            _list.Visible = false;
            _empty.Visible = false;
            _log.Visible = true;
            _retryButton.Visible = false;
            _cancelButton.Visible = true;
            _cancelButton.Enabled = true;
            _cancelButton.Text = "Cancel batch";
            _backButton.Enabled = false;
            _exportButton.Enabled = false;
        }

        private void RequestCancel()
        {
            _cts?.Cancel();
            _cancelButton.Enabled = false;
            _cancelButton.Text = "Cancelling...";
        }

        private void AppendLog(string text, Color color)
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

        // ---- results -------------------------------------------------------------------------------

        /// <summary>Shows a finished batch (also used directly by tests with a hand-built result).</summary>
        public void ShowResults(BatchRunResult result, AppServices services, ChangeLogAction action, bool dryRun)
        {
            _services = services;
            _action = action;
            _dryRun = dryRun;
            Result = result;
            _mode = Mode.Done;
            _filter = 0;
            RenderResults();
        }

        private void RenderResults()
        {
            var result = Result;
            int good = result.Results.Count(r => Classify(r) == ResultStatus.Success);
            int failed = result.Results.Count(r => Classify(r) == ResultStatus.Failed);
            int skipped = result.Results.Count(r => Classify(r) == ResultStatus.Skipped);
            int dry = result.Results.Count(r => Classify(r) == ResultStatus.DryRunPreview);

            var parts = new List<string>();
            if (dry > 0) parts.Add($"{dry} would apply");
            parts.Add($"{good} applied");
            parts.Add($"{failed} failed");
            parts.Add($"{skipped} skipped");
            _summary.Text = string.Join(", ", parts) + (result.Cancelled ? " (cancelled - remaining tweaks not started)" : "");
            _summary.ForeColor = failed > 0 ? UiStyle.Error : (dry > 0 ? UiStyle.BrightAccent : UiStyle.Accent);
            _header.Text = (_action == ChangeLogAction.Apply ? (_dryRun ? "Dry run complete" : "Apply complete") : (_dryRun ? "Undo dry run complete" : "Undo complete"));

            _tileGood.Set(good + dry, dry > 0 ? "WOULD APPLY" : "APPLIED", UiStyle.Accent, _filter == 1);
            _tileFailed.Set(failed, "FAILED", UiStyle.Error, _filter == 2);
            _tileSkipped.Set(skipped, "SKIPPED", UiStyle.Dim, _filter == 3);

            _list.Items.Clear();
            foreach (var r in result.Results.OrderBy(r => SortOrder(Classify(r))).ThenBy(r => r.Tweak.Title))
            {
                var status = Classify(r);
                if (_filter == 1 && status != ResultStatus.Success && status != ResultStatus.DryRunPreview) continue;
                if (_filter == 2 && status != ResultStatus.Failed) continue;
                if (_filter == 3 && status != ResultStatus.Skipped) continue;
                var (marker, color) = StyleFor(status);
                var reason = ShortReason(r);
                var text = string.IsNullOrEmpty(reason) ? $"{marker}  {r.Tweak.Title}" : $"{marker}  {r.Tweak.Title} - {reason}";
                _list.Items.Add(new ListViewItem(text) { Tag = r, ForeColor = color });
            }

            _log.Visible = false;
            _empty.Visible = false;
            _tiles.Visible = true;
            _detailHost.Visible = true;
            _list.Visible = true;
            _cancelButton.Visible = false;
            _retryButton.Visible = true;
            _backButton.Enabled = true;
            _exportButton.Enabled = true;

            if (_list.Items.Count > 0) SelectRow(0);
            else OnRowSelected(null);
        }

        private void SetFilter(int filter)
        {
            _filter = _filter == filter ? 0 : filter;
            RenderResults();
        }

        private void SelectRow(int index)
        {
            if (index < 0 || index >= _list.Items.Count) return;
            foreach (ListViewItem item in _list.Items) item.Selected = false;
            var target = _list.Items[index];
            target.Selected = true;
            OnRowSelected(target.Tag as TweakRunResult);
        }

        private void OnRowSelected(TweakRunResult r)
        {
            _selected = r;
            _detail.Clear();
            if (r == null) { _retryButton.Enabled = false; return; }

            var status = Classify(r);
            AppendDetail(r.Tweak.Title, UiStyle.BrightAccent);
            AppendDetail($"Attempted: {(_action == ChangeLogAction.Apply ? "Apply" : "Undo")} - {r.Tweak.Description}", UiStyle.Foreground);
            switch (status)
            {
                case ResultStatus.Failed:
                    AppendDetail("Error: " + (r.Result?.Message ?? r.Exception?.Message ?? "Unknown error"), UiStyle.Error);
                    AppendDetail("The tweak was left unchanged - nothing partial was applied. You can retry, or check the likely cause " +
                        "(access denied: confirm Performish is running as Administrator; key not found: the setting may not exist on this Windows build).", UiStyle.Dim);
                    break;
                case ResultStatus.Skipped:
                    AppendDetail("Skipped: " + (r.Result?.Message ?? ""), UiStyle.Dim);
                    break;
                case ResultStatus.DryRunPreview:
                    AppendDetail("Dry run - would have done: " + (r.Result?.Message ?? ""), UiStyle.GradientMid);
                    break;
                default:
                    AppendDetail("Succeeded: " + (r.Result?.Message ?? ""), UiStyle.Accent);
                    break;
            }
            // Retry only ever makes sense for a real (non-dry-run) failure.
            _retryButton.Enabled = status == ResultStatus.Failed && !_dryRun;
        }

        private void AppendDetail(string text, Color color)
        {
            _detail.SelectionStart = _detail.TextLength;
            _detail.SelectionColor = color;
            _detail.AppendText(text + Environment.NewLine);
        }

        /// <summary>Retries a single failed tweak synchronously (exactly one tweak, so the brief block is
        /// unnoticeable) and refreshes the display with its new outcome.</summary>
        private void RetrySelected()
        {
            if (_selected is not TweakRunResult failed || Classify(failed) != ResultStatus.Failed || _services == null) return;

            _retryButton.Enabled = false;
            _retryButton.Text = "Retrying...";
            Application.DoEvents();

            var tweak = failed.Tweak;
            var ctx = _services.CreateContext(_dryRun);
            var batch = _action == ChangeLogAction.Apply
                ? _services.Runner.ApplyBatch(new[] { tweak }, ctx, createRestorePoint: false)
                : _services.Runner.UndoBatch(new[] { tweak }, ctx);
            var retryResult = batch.Results.Count > 0 ? batch.Results[0] : null;
            _retryButton.Text = "Retry this tweak";
            if (retryResult == null) return;

            var index = Result.Results.FindIndex(r => r.Tweak.Id == tweak.Id);
            if (index >= 0) Result.Results[index] = retryResult;
            RenderResults();

            var newIndex = _list.Items.Cast<ListViewItem>().ToList().FindIndex(i => i.Tag is TweakRunResult r && r.Tweak.Id == tweak.Id);
            if (newIndex >= 0) SelectRow(newIndex);
        }
    }
}
