using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Performish.Core;
using Performish.Core.Backup;
using Performish.Core.Models;

namespace Performish.Dialogs
{
    /// <summary>Change-log "what changed" timeline - button replacement for the old H/E/Esc keybind
    /// screen, now with per-row Undo instead of only the all-or-nothing Revert Everything on the home
    /// screen. A ListView (not the console RichTextBox every other screen uses) is used here
    /// specifically because a single row needs to be selectable and acted on individually - the
    /// console's freeform text has no notion of "this line".</summary>
    public sealed class HistoryForm : Form
    {
        private readonly AppServices _services;
        private readonly Func<bool> _getDryRun;
        private ListView _list;
        private Button _undoButton;

        public HistoryForm(AppServices services, Func<bool> getDryRun)
        {
            _services = services;
            _getDryRun = getDryRun;

            Text = "History";
            Width = 960;
            Height = 640;
            BackColor = UiStyle.Background;
            Font = UiStyle.Mono;
            StartPosition = FormStartPosition.CenterParent;

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                GridLines = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BackColor = UiStyle.Background,
                ForeColor = UiStyle.Foreground,
                Font = UiStyle.Mono,
                BorderStyle = BorderStyle.None
            };
            _list.Columns.Add("Time (UTC)", 160);
            _list.Columns.Add("Action", 70);
            _list.Columns.Add("Outcome", 90);
            _list.Columns.Add("Tweak", 460);
            _list.SelectedIndexChanged += (s, e) => UpdateUndoButtonState();

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                BackColor = UiStyle.Background,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(10)
            };

            var closeButton = UiStyle.MakeButton("Close", UiStyle.Dim);
            closeButton.Click += (s, e) => Close();

            var exportButton = UiStyle.MakeButton("Export CSV...");
            exportButton.Click += (s, e) => ExportCsv();

            _undoButton = UiStyle.MakeButton("Undo this tweak...", UiStyle.Error);
            _undoButton.Enabled = false;
            _undoButton.Click += async (s, e) => await UndoSelectedAsync();

            buttonPanel.Controls.Add(closeButton);
            buttonPanel.Controls.Add(exportButton);
            buttonPanel.Controls.Add(_undoButton);

            var host = new Panel { Dock = DockStyle.Fill, BackColor = UiStyle.Background, Padding = new Padding(12) };
            host.Controls.Add(_list);

            Controls.Add(host);
            Controls.Add(buttonPanel);
            CancelButton = closeButton;

            RefreshList();
        }

        // Only the current session's still-applied count needs to stay in sync while this dialog is
        // open (a single Undo click can only change one), so it's recomputed once per RefreshList()
        // rather than per row - cheap, and correct even after an undo changes what's "applied".
        private System.Collections.Generic.HashSet<string> _currentlyAppliedIds = new System.Collections.Generic.HashSet<string>();

        private void RefreshList()
        {
            _list.Items.Clear();
            _currentlyAppliedIds = new System.Collections.Generic.HashSet<string>(_services.Runner.CurrentlyAppliedTweakIds());

            const int maxDisplayed = 500;
            // ReadRecent(), not ReadAll() - the display only ever shows a bounded slice, so there's
            // no reason to load the entire (potentially multi-year) change log into memory just to
            // throw most of it away. Export still uses the full history via ExportCsv().
            var entries = _services.ChangeLog.ReadRecent(maxDisplayed);

            if (entries.Count == 0)
            {
                var empty = new ListViewItem(new[] { "", "", "", "No changes recorded yet." });
                empty.ForeColor = UiStyle.Dim;
                _list.Items.Add(empty);
                UpdateUndoButtonState();
                return;
            }

            foreach (var e in entries)
            {
                var item = new ListViewItem(new[]
                {
                    e.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                    e.Action.ToString(),
                    e.Outcome + (e.DryRun ? " [dry]" : ""),
                    e.TweakName
                })
                {
                    Tag = e,
                    ForeColor = e.Outcome == OperationOutcome.Failed ? UiStyle.Error : (e.DryRun ? UiStyle.Dim : UiStyle.Accent)
                };
                _list.Items.Add(item);
            }

            UpdateUndoButtonState();
        }

        private void UpdateUndoButtonState()
        {
            _undoButton.Enabled = TryGetUndoableEntry(out _);
        }

        private bool TryGetUndoableEntry(out ChangeLogEntry entry)
        {
            entry = null;
            if (_list.SelectedItems.Count == 0) return false;
            if (_list.SelectedItems[0].Tag is not ChangeLogEntry e) return false;

            // Only a successful, real (non-dry-run) Apply whose tweak is still recorded as currently
            // applied can be undone from here - anything else (a Skipped/Failed row, a dry-run
            // preview row, an already-undone tweak, or a row for a tweak no longer in the library) has
            // nothing meaningful to undo.
            if (e.Action != ChangeLogAction.Apply || e.Outcome != OperationOutcome.Success || e.DryRun) return false;
            if (!_currentlyAppliedIds.Contains(e.TweakId)) return false;
            if (_services.TweakRegistry.Find(e.TweakId) == null) return false;

            entry = e;
            return true;
        }

        private async Task UndoSelectedAsync()
        {
            if (!TryGetUndoableEntry(out var entry)) return;
            var tweak = _services.TweakRegistry.Find(entry.TweakId);
            if (tweak == null) return;

            var dryRun = _getDryRun();
            var lines = new[]
            {
                ($"Undo just this one tweak: {tweak.Title}", UiStyle.Foreground),
                ($"    {tweak.Description}", UiStyle.Dim),
                ("", UiStyle.Foreground),
                ($"Mode: {(dryRun ? "DRY RUN (nothing will actually change)" : "REAL - this will be reverted")}",
                    dryRun ? UiStyle.BrightAccent : UiStyle.Error)
            };

            var confirmed = ConfirmDialogForm.Show(this, "Undo this tweak", lines, "Undo", "Cancel");
            if (!confirmed) return;

            await RunningForm.RunAsync(this, "Undoing", _services, new[] { tweak }, ChangeLogAction.Undo, dryRun);

            RefreshList();
        }

        private void ExportCsv()
        {
            using var dialog = new SaveFileDialog
            {
                Title = "Export change log",
                Filter = "CSV files (*.csv)|*.csv",
                FileName = $"performish-changelog-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                _services.ChangeLog.ExportCsv(dialog.FileName);
                MessageDialog.Show(this, "Exported", $"Exported to {dialog.FileName}");
            }
            catch (Exception ex)
            {
                MessageDialog.Show(this, "Export failed", ex.Message);
            }
        }
    }
}
