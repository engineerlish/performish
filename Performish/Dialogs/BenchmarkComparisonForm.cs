using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Performish.Core.Benchmark;

namespace Performish.Dialogs
{
    /// <summary>Real, measured before/after results for one benchmark comparison - every row is
    /// something actually sampled on this machine (or explicitly marked Unavailable/dry-run preview,
    /// never a guess). Same list+detail pane pattern and green/red/neutral result styling as
    /// HealthScoreForm/RunningForm - failures-first-style grouping doesn't apply here (there's no
    /// "failure", just a verdict per metric), but the marker/color contract is identical: a color is
    /// never the only signal, every row carries a bracketed text marker too.</summary>
    public sealed class BenchmarkComparisonForm : Form
    {
        private readonly BenchmarkComparisonReport _report;
        private ListView _list;
        private RichTextBox _detail;
        private Label _summaryLabel;
        private Label _disclaimerLabel;

        /// <summary>Test-only accessors, same pattern as HealthScoreForm/RunningForm.</summary>
        public string SummaryText => _summaryLabel.Text;
        public Color SummaryColor => _summaryLabel.ForeColor;
        public string DisclaimerText => _disclaimerLabel.Text;
        public IReadOnlyList<(string Text, Color Color)> Rows =>
            _list.Items.Cast<ListViewItem>().Select(i => (i.Text, i.ForeColor)).ToList();
        public string DetailText => _detail.Text;

        public void SelectRow(int index)
        {
            if (index < 0 || index >= _list.Items.Count) return;
            foreach (ListViewItem item in _list.Items) item.Selected = false;
            var target = _list.Items[index];
            target.Selected = true;
            RenderDetail(target.Tag as MetricComparisonRow);
        }

        public BenchmarkComparisonForm(BenchmarkComparisonReport report)
        {
            _report = report ?? throw new ArgumentNullException(nameof(report));

            Text = "Benchmark comparison";
            Width = 820;
            Height = 600;
            BackColor = UiStyle.Background;
            Font = UiStyle.Mono;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(600, 420);

            BuildLayout();
            Populate();
        }

        private void BuildLayout()
        {
            _summaryLabel = UiStyle.MakeLabel("", UiStyle.Foreground, bold: true);
            _summaryLabel.Dock = DockStyle.Top;
            _summaryLabel.Height = 28;
            _summaryLabel.Padding = new Padding(12, 6, 0, 2);

            _disclaimerLabel = UiStyle.MakeLabel("", UiStyle.GradientMid);
            _disclaimerLabel.Dock = DockStyle.Top;
            _disclaimerLabel.Height = 36;
            _disclaimerLabel.Padding = new Padding(12, 0, 12, 4);
            _disclaimerLabel.AutoEllipsis = true;

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
            _list.Columns.Add("Metric", 720);
            _list.SelectedIndexChanged += (s, e) =>
                RenderDetail(_list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as MetricComparisonRow : null);

            _detail = UiStyle.MakeConsole();
            _detail.Height = 120;

            var listHost = new Panel { Dock = DockStyle.Fill, BackColor = UiStyle.Background, Padding = new Padding(12, 0, 12, 0) };
            listHost.Controls.Add(_list);

            var detailHost = new Panel { Dock = DockStyle.Bottom, Height = 130, BackColor = UiStyle.Background, Padding = new Padding(12, 4, 12, 4) };
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

            var topPanel = new Panel { Dock = DockStyle.Top, Height = 28 + 36, BackColor = UiStyle.Background };
            topPanel.Controls.Add(_disclaimerLabel);
            topPanel.Controls.Add(_summaryLabel);

            Controls.Add(listHost);
            Controls.Add(detailHost);
            Controls.Add(buttonPanel);
            Controls.Add(topPanel);

            CancelButton = closeButton;
        }

        private void Populate()
        {
            var improved = _report.Metrics.Count(m => m.Verdict == ComparisonVerdict.Improved);
            var worse = _report.Metrics.Count(m => m.Verdict == ComparisonVerdict.Worse);
            var flat = _report.Metrics.Count(m => m.Verdict == ComparisonVerdict.Flat);
            var unavailable = _report.Metrics.Count(m => m.Verdict == ComparisonVerdict.Unavailable || m.Verdict == ComparisonVerdict.Unreliable);

            var summaryParts = new List<string> { $"{improved} improved", $"{worse} worse", $"{flat} flat" };
            if (unavailable > 0) summaryParts.Add($"{unavailable} unavailable/unreliable");
            var healthPart = _report.HealthScoreDelta.HasValue
                ? $" - health score {(_report.HealthScoreDelta >= 0 ? "+" : "")}{_report.HealthScoreDelta}"
                : "";
            _summaryLabel.Text = string.Join(", ", summaryParts) + healthPart;
            _summaryLabel.ForeColor = worse > 0 ? UiStyle.Error : (improved > 0 ? UiStyle.Accent : UiStyle.GradientMid);

            _disclaimerLabel.Text = _report.AttributionDisclaimer ?? "";
            _disclaimerLabel.Visible = _report.AttributionDisclaimer != null;

            _list.Items.Clear();
            foreach (var m in _report.Metrics)
            {
                var (marker, color) = StyleFor(m.Verdict);
                var valueText = m.Before.HasValue && m.After.HasValue
                    ? $"{m.Before.Value:0.##} -> {m.After.Value:0.##} {m.Unit}"
                    : m.Before.HasValue ? $"{m.Before.Value:0.##} {m.Unit} (before only)"
                    : "no data";
                var text = $"{marker}  {m.Name} - {valueText}";
                _list.Items.Add(new ListViewItem(text) { Tag = m, ForeColor = color });
            }

            if (_list.Items.Count > 0) SelectRow(0);
        }

        private static (string Marker, Color Color) StyleFor(ComparisonVerdict verdict) => verdict switch
        {
            ComparisonVerdict.Improved => ("[BETTER]", UiStyle.Accent),
            ComparisonVerdict.Worse => ("[WORSE]", UiStyle.Error),
            ComparisonVerdict.Flat => ("[FLAT]", UiStyle.Dim),
            ComparisonVerdict.Unreliable => ("[UNRELIABLE]", UiStyle.GradientMid),
            _ => ("[N/A]", UiStyle.Dim)
        };

        private void RenderDetail(MetricComparisonRow m)
        {
            _detail.Clear();
            if (m == null)
            {
                AppendDetail("Select a metric to see its full before/after detail.", UiStyle.Dim);
                return;
            }

            AppendDetail(m.Name, UiStyle.BrightAccent);
            AppendDetail(
                m.Before.HasValue ? $"Before: {m.Before.Value:0.##} {m.Unit}" : "Before: not measured",
                UiStyle.Foreground);
            AppendDetail(
                m.After.HasValue ? $"After: {m.After.Value:0.##} {m.Unit}" : "After: not measured",
                UiStyle.Foreground);
            if (m.DeltaAbsolute.HasValue)
                AppendDetail($"Change: {(m.DeltaAbsolute >= 0 ? "+" : "")}{m.DeltaAbsolute.Value:0.##} {m.Unit}" +
                    (m.DeltaPercent.HasValue ? $" ({(m.DeltaPercent >= 0 ? "+" : "")}{m.DeltaPercent.Value:0.#}%)" : ""),
                    UiStyle.Dim);
            AppendDetail("", UiStyle.Foreground);
            AppendDetail(m.PlainLanguageRead, UiStyle.Dim);
        }

        private void AppendDetail(string text, Color color)
        {
            _detail.SelectionStart = _detail.TextLength;
            _detail.SelectionColor = color;
            _detail.AppendText(text + Environment.NewLine);
        }
    }
}
