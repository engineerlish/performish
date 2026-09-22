using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Performish.Core.Benchmark;

namespace Performish.Dialogs
{
    /// <summary>Transparent breakdown of the health score - every factor, its points earned out of
    /// possible, its current measured value, and plain-language guidance (including whether Performish
    /// itself can help, or it needs the user's own action / isn't something software can fix). Built
    /// for the health-score task's Step 2 requirement that the score be explainable, not just a number
    /// - see HEALTH_ANALYSIS.md. Same list/detail-pane pattern and green/red/neutral result styling as
    /// TweakBrowserForm and RunningForm, not a new visual language.</summary>
    public sealed class HealthScoreForm : Form
    {
        private readonly HealthScoreResult _result;
        private ListView _list;
        private RichTextBox _detail;
        private Label _summaryLabel;

        /// <summary>Test-only accessors, same pattern as RunningForm.</summary>
        public string SummaryText => _summaryLabel.Text;
        public Color SummaryColor => _summaryLabel.ForeColor;
        public System.Collections.Generic.IReadOnlyList<(string Text, Color Color)> Rows =>
            _list.Items.Cast<ListViewItem>().Select(i => (i.Text, i.ForeColor)).ToList();
        public string DetailText => _detail.Text;

        /// <summary>Selects a row and updates the detail pane directly, without depending on the
        /// native SelectedIndexChanged event - a ListView only reflects Selected-state changes through
        /// that event once it has a real native HWND, which this dialog doesn't have unless actually
        /// shown via ShowDialog(). Same fix as RunningForm's SelectRow()/OnRowSelected() - see that
        /// file's comment for the full story (an earlier version of this pattern forced native handle
        /// creation instead, which turned out to leak per-thread WinForms state badly enough to hang
        /// test runs after ~8-9 sequential tests).</summary>
        public void SelectRow(int index)
        {
            if (index < 0 || index >= _list.Items.Count) return;
            foreach (ListViewItem item in _list.Items) item.Selected = false;
            var target = _list.Items[index];
            target.Selected = true;
            RenderDetail(target.Tag as HealthScoreFactor);
        }

        public HealthScoreForm(HealthScoreResult result)
        {
            _result = result;

            Text = "Health score";
            Width = 760;
            Height = 560;
            BackColor = UiStyle.Background;
            Font = UiStyle.Mono;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(560, 400);

            BuildLayout();
            Populate();
        }

        private void BuildLayout()
        {
            _summaryLabel = UiStyle.MakeLabel("", UiStyle.Foreground, bold: true);
            _summaryLabel.Dock = DockStyle.Top;
            _summaryLabel.Height = 30;
            _summaryLabel.Padding = new Padding(12, 6, 0, 4);

            _list = new ListView
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
                BorderStyle = BorderStyle.None
            };
            _list.Columns.Add("Factor", 660);
            _list.SelectedIndexChanged += (s, e) =>
                RenderDetail(_list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as HealthScoreFactor : null);

            _detail = UiStyle.MakeConsole();
            _detail.Height = 110;

            var listHost = new Panel { Dock = DockStyle.Fill, BackColor = UiStyle.Background, Padding = new Padding(12, 0, 12, 0) };
            listHost.Controls.Add(_list);

            var detailHost = new Panel { Dock = DockStyle.Bottom, Height = 120, BackColor = UiStyle.Background, Padding = new Padding(12, 4, 12, 4) };
            detailHost.Controls.Add(_detail);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                BackColor = UiStyle.Background,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(10)
            };
            var closeButton = UiStyle.MakeButton("Close");
            closeButton.Click += (s, e) => Close();
            buttonPanel.Controls.Add(closeButton);

            Controls.Add(listHost);
            Controls.Add(detailHost);
            Controls.Add(buttonPanel);
            Controls.Add(_summaryLabel);

            CancelButton = closeButton;
        }

        private void Populate()
        {
            _summaryLabel.Text = $"Overall score: {_result.Score} / 100";
            _summaryLabel.ForeColor = _result.Score >= 75 ? UiStyle.Accent
                : _result.Score >= 50 ? UiStyle.GradientMid
                : UiStyle.Error;

            _list.Items.Clear();
            foreach (var f in _result.Factors)
            {
                var (marker, color) = StyleFor(f);
                var text = $"{marker}  {f.Name} - {f.Points}/{f.MaxPoints}  ({f.Detail})";
                _list.Items.Add(new ListViewItem(text) { Tag = f, ForeColor = color });
            }

            if (_list.Items.Count > 0) SelectRow(0);
        }

        private static (string Marker, Color Color) StyleFor(HealthScoreFactor f)
        {
            if (f.MaxPoints <= 0) return ("[N/A]", UiStyle.Dim);
            if (f.Points >= f.MaxPoints) return ("[OK]", UiStyle.Accent); // green - full marks
            if (f.Points == 0) return ("[LOW]", UiStyle.Error); // red - zero
            return ("[MID]", UiStyle.GradientMid); // neutral - partial credit
        }

        private void RenderDetail(HealthScoreFactor f)
        {
            _detail.Clear();
            if (f == null)
            {
                AppendDetail("Select a factor to see detail and guidance.", UiStyle.Dim);
                return;
            }

            AppendDetail($"{f.Name}: {f.Points} / {f.MaxPoints} points", UiStyle.BrightAccent);
            AppendDetail($"Current value: {f.Detail}", UiStyle.Foreground);
            AppendDetail("", UiStyle.Foreground);
            AppendDetail(f.Guidance, UiStyle.Dim);
            if (!f.ToolCanAct && f.Points < f.MaxPoints)
                AppendDetail("Performish does not change this directly - see the guidance above.", UiStyle.GradientMid);
        }

        private void AppendDetail(string text, Color color)
        {
            _detail.SelectionStart = _detail.TextLength;
            _detail.SelectionColor = color;
            _detail.AppendText(text + Environment.NewLine);
        }
    }
}
