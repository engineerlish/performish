using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Performish.Core.Benchmark;

namespace Performish.Dialogs
{
    /// <summary>Browse stored benchmark runs and pick any two to compare - not just the most recent
    /// pair (the task's "trend over time, not just the most recent" requirement). Selecting exactly
    /// two rows and clicking Compare opens the same BenchmarkComparisonForm the apply flow uses,
    /// ordered older-as-Before/newer-as-After regardless of which order they were clicked in.</summary>
    public sealed class BenchmarkHistoryForm : Form
    {
        private readonly List<BenchmarkRun> _runs;
        private ListView _list;
        private Button _compareButton;
        private Label _countLabel;

        public (BenchmarkRun Older, BenchmarkRun Newer)? SelectedPair { get; private set; }

        /// <summary>Test-only accessors, same pattern as every other dialog's test surface.</summary>
        public Button CompareButton => _compareButton;
        public int RunCount => _list.Items.Count;

        public void SelectRows(params int[] indexes)
        {
            foreach (ListViewItem item in _list.Items) item.Selected = false;
            foreach (var i in indexes) _list.Items[i].Selected = true;
            UpdateCompareButtonState();
        }

        public BenchmarkHistoryForm(IReadOnlyList<BenchmarkRun> runs)
        {
            _runs = runs.OrderByDescending(r => r.TimestampUtc).ToList();

            Text = "Benchmark history";
            Width = 780;
            Height = 520;
            BackColor = UiStyle.Background;
            Font = UiStyle.Mono;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(560, 360);

            BuildLayout();
            Populate();
        }

        private void BuildLayout()
        {
            _countLabel = UiStyle.MakeLabel("", UiStyle.Dim);
            _countLabel.Dock = DockStyle.Top;
            _countLabel.Height = 28;
            _countLabel.Padding = new Padding(12, 6, 0, 2);

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = true,
                GridLines = false,
                HeaderStyle = ColumnHeaderStyle.None,
                BackColor = UiStyle.Background,
                ForeColor = UiStyle.Foreground,
                Font = UiStyle.Mono,
                BorderStyle = BorderStyle.None
            };
            _list.Columns.Add("Run", 720);
            _list.ItemSelectionChanged += (s, e) => UpdateCompareButtonState();

            var listHost = new Panel { Dock = DockStyle.Fill, BackColor = UiStyle.Background, Padding = new Padding(12, 0, 12, 0) };
            listHost.Controls.Add(_list);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                BackColor = UiStyle.Background,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(10)
            };

            var closeButton = UiStyle.MakeButton("Close", UiStyle.Dim);
            closeButton.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            _compareButton = UiStyle.MakeButton("Compare selected", UiStyle.Accent);
            _compareButton.Enabled = false;
            _compareButton.Click += (s, e) => CompareSelected();

            buttonPanel.Controls.Add(closeButton);
            buttonPanel.Controls.Add(_compareButton);

            Controls.Add(listHost);
            Controls.Add(buttonPanel);
            Controls.Add(_countLabel);

            CancelButton = closeButton;
        }

        private void Populate()
        {
            _countLabel.Text = _runs.Count == 0
                ? "No benchmark runs saved yet - use \"Run benchmark now\" or apply some tweaks with benchmarking on."
                : $"{_runs.Count} run(s) - select exactly two to compare (in any order).";

            _list.Items.Clear();
            foreach (var run in _runs)
            {
                var text = $"{run.TimestampUtc.ToLocalTime():yyyy-MM-dd HH:mm}  [{run.Kind}]  {run.Label}" +
                    (run.HealthScore.HasValue ? $"  (health {run.HealthScore})" : "") +
                    (run.IsDryRunPreview ? "  (dry run - not measured)" : "");
                _list.Items.Add(new ListViewItem(text) { Tag = run });
            }
        }

        private void UpdateCompareButtonState() => _compareButton.Enabled = _list.SelectedItems.Count == 2;

        private void CompareSelected()
        {
            if (_list.SelectedItems.Count != 2) return;

            var a = (BenchmarkRun)_list.SelectedItems[0].Tag;
            var b = (BenchmarkRun)_list.SelectedItems[1].Tag;
            SelectedPair = a.TimestampUtc <= b.TimestampUtc ? (a, b) : (b, a);

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
