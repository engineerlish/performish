using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Performish.Core.Hardware;
using Performish.Hardware;

namespace Performish.Views
{
    /// <summary>The Hardware sidebar screen: a read-only view of the BIOS, motherboard, CPU, memory and
    /// GPU. Grouped by component - a component list on the left, that component's findings and readings
    /// on the right, a detail pane underneath saying where each value came from (or why it couldn't be
    /// read). Same list/detail pattern and [OK]/[TIP] text markers as HealthScoreForm. This is where any
    /// future hardware/BIOS-related screen lives, not just this visibility report - a dedicated screen
    /// (its own list/detail sections) rather than a single flat button list, so it can grow. Nothing here
    /// changes the machine.</summary>
    public sealed class HardwareView : UserControl
    {
        private readonly HardwareInfoService _service;
        private HardwareReport _report;
        private HardwareComponent _component = HardwareComponent.Firmware;
        private bool _wakeGpu;
        // Guards against re-entry: selecting an item in code raises SelectedIndexChanged once a native handle exists.
        private bool _syncing;

        private Label _header, _summary, _note;
        private ListView _components, _readings;
        private RichTextBox _findings, _detail;
        private Button _refreshButton, _copyButton, _wakeButton;

        // ---- test surface (same approach as HealthScoreForm: select directly, no native events) ----
        public string HeaderText => _header.Text;
        public string SummaryText => _summary.Text;
        public bool WakeGpuButtonVisible => _wakeButton.Visible;
        public IReadOnlyList<string> ComponentRows => _components.Items.Cast<ListViewItem>().Select(i => i.Text).ToList();
        public IReadOnlyList<string> ReadingRows => _readings.Items.Cast<ListViewItem>().Select(i => $"{i.Text} | {i.SubItems[1].Text} | {i.SubItems[2].Text}").ToList();
        public string FindingsText => _findings.Text;
        public string DetailText => _detail.Text;
        public HardwareReport Report => _report;
        public Button RefreshButton => _refreshButton;
        public Button CopyButton => _copyButton;
        public void ClickWakeGpu() => _wakeButton.PerformClick();
        public bool IsLoaded => _report != null;

        public HardwareView(HardwareInfoService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));

            BackColor = UiStyle.Background;
            ForeColor = UiStyle.Foreground;
            Font = UiStyle.Mono;
            AccessibleName = "Hardware";
            AccessibleRole = AccessibleRole.Pane;

            BuildLayout();

            // Force this control's (and its children's, including the two ListViews) native handle to
            // exist now, synchronously, on the UI thread - before this view is ever shown or its first
            // async load runs. Same fix as RunningForm/ResultsView's own "force the handle before any
            // background work starts" (see DECISIONS.md "RunningForm handle-creation race"): creating a
            // ListView's handle for the first time deep inside an async continuation triggered by a
            // click (the first SelectComponent()/SelectReading() after a real scan/load) raced WinForms'
            // own handle-creation bookkeeping when this view lives inside the already-shown MainForm.
            _ = Handle;
        }

        private void BuildLayout()
        {
            _header = new Label { Text = "Hardware", Font = UiStyle.Big, ForeColor = UiStyle.Foreground, AutoSize = false, Dock = DockStyle.Top, Height = 40, Padding = new Padding(24, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft };

            _summary = UiStyle.MakeLabel("Reading hardware (read-only)...", UiStyle.Foreground, bold: true);
            _summary.Dock = DockStyle.Top;
            _summary.AutoSize = false;
            _summary.Height = 28;
            _summary.Padding = new Padding(24, 6, 0, 0);

            _note = UiStyle.MakeLabel(HardwareUi.NotScoredNote, UiStyle.Dim);
            _note.Dock = DockStyle.Top;
            _note.AutoSize = false;
            _note.Height = 24;
            _note.Padding = new Padding(24, 0, 0, 4);

            _components = MakeList();
            _components.Columns.Add("Component", 300);
            _components.SelectedIndexChanged += (s, e) => { if (!_syncing && _components.SelectedItems.Count > 0) SelectComponent(_components.SelectedIndices[0]); };
            var left = new Panel { Dock = DockStyle.Left, Width = 320, BackColor = UiStyle.Background, Padding = new Padding(24, 4, 8, 0) };
            left.Controls.Add(_components);

            _readings = MakeList();
            // No column header: the native header renders light-on-dark; the rows read as label / value / tag.
            _readings.Columns.Add("Reading", 230);
            _readings.Columns.Add("Value", 340);
            _readings.Columns.Add("", 80);
            _readings.SelectedIndexChanged += (s, e) => { if (!_syncing && _readings.SelectedItems.Count > 0) SelectReading(_readings.SelectedIndices[0]); };
            UiStyle.UseDarkScrollbars(_readings);

            _findings = UiStyle.MakeConsole();
            _findings.WordWrap = true;
            _findings.ScrollBars = RichTextBoxScrollBars.Vertical;
            var findingsHost = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = UiStyle.Background, Padding = new Padding(0, 4, 24, 4) };
            findingsHost.Controls.Add(_findings);

            var right = new Panel { Dock = DockStyle.Fill, BackColor = UiStyle.Background, Padding = new Padding(4, 4, 0, 0) };
            right.Controls.Add(_readings);
            right.Controls.Add(findingsHost);

            _detail = UiStyle.MakeConsole();
            _detail.WordWrap = true;
            _detail.ScrollBars = RichTextBoxScrollBars.Vertical;
            var detailHost = new Panel { Dock = DockStyle.Bottom, Height = 150, BackColor = UiStyle.Panel, Padding = new Padding(24, 10, 24, 6) };
            detailHost.Paint += (s, e) => { using var p = new Pen(UiStyle.Line); e.Graphics.DrawLine(p, 0, 0, detailHost.Width, 0); };
            _detail.BackColor = UiStyle.Panel;
            detailHost.Controls.Add(_detail);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 62, BackColor = UiStyle.Background, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 24, 8) };
            buttons.Paint += (s, e) => { using var p = new Pen(UiStyle.Line); e.Graphics.DrawLine(p, 0, 0, buttons.Width, 0); };
            _wakeButton = UiStyle.MakeButton(HardwareUi.WakeGpuLabel, UiStyle.Foreground);
            _wakeButton.AccessibleDescription = HardwareUi.WakeGpuDescription;
            _wakeButton.Visible = false;
            _wakeButton.Click += async (s, e) => { _wakeGpu = true; await LoadAsync(); };
            _refreshButton = UiStyle.MakeButton("Refresh", UiStyle.Foreground);
            _refreshButton.Click += async (s, e) => await LoadAsync();
            _copyButton = UiStyle.MakeButton("Copy as text", UiStyle.Foreground);
            _copyButton.Click += (s, e) => HardwareUi.CopyToClipboard(_report);
            buttons.Controls.AddRange(new Control[] { _copyButton, _refreshButton, _wakeButton });

            Controls.Add(right);
            Controls.Add(left);
            Controls.Add(detailHost);
            Controls.Add(buttons);
            Controls.Add(_note);
            Controls.Add(_summary);
            Controls.Add(_header);
        }

        private static ListView MakeList() => new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            GridLines = false,
            HeaderStyle = ColumnHeaderStyle.None,
            BackColor = UiStyle.Background,
            ForeColor = UiStyle.Foreground,
            Font = UiStyle.Mono,
            BorderStyle = BorderStyle.None
        };

        public async System.Threading.Tasks.Task LoadAsync()
        {
            _refreshButton.Enabled = _wakeButton.Enabled = false;
            _summary.Text = "Reading hardware (read-only)...";
            _summary.ForeColor = UiStyle.Dim;
            var (report, error) = await HardwareUi.ReadAsync(_service, _wakeGpu);
            // The read takes a moment (WMI, NVML); the view may have been torn down meanwhile.
            if (IsDisposed || Disposing) return;
            _refreshButton.Enabled = _wakeButton.Enabled = true;
            if (error != null)
            {
                _summary.Text = $"Couldn't read hardware: {error}";
                _summary.ForeColor = UiStyle.Error;
                return;
            }
            ShowReport(report);
        }

        /// <summary>Populates the view from a report - the async load calls this, and tests call it directly.</summary>
        public void ShowReport(HardwareReport report)
        {
            _report = report;
            _summary.Text = HardwareTones.ReadableSummary(report);
            _summary.ForeColor = UiStyle.Foreground;
            _wakeButton.Visible = report.DiscreteGpuSkipped;

            _components.Items.Clear();
            foreach (var s in ComponentPresenter.Summaries(report))
                _components.Items.Add(new ListViewItem(s.Text) { Tag = s.Component, ForeColor = HardwareUi.ColorFor(s.Tone) });

            var index = _components.Items.Cast<ListViewItem>().ToList().FindIndex(i => (HardwareComponent)i.Tag == _component);
            SelectComponent(Math.Max(0, index));
        }

        public void SelectComponent(int index)
        {
            if (_report == null || index < 0 || index >= _components.Items.Count) return;
            _syncing = true;
            foreach (ListViewItem item in _components.Items) item.Selected = false;
            _components.Items[index].Selected = true;
            _syncing = false;
            _component = (HardwareComponent)_components.Items[index].Tag;

            var findings = ComponentPresenter.FindingLines(_report, _component);
            HardwareUi.WriteLines(_findings, findings.Count > 0 ? findings
                : new List<ToneLine> { new ToneLine("Nothing to flag for this component.", Tone.Dim) });

            _readings.BeginUpdate();
            _readings.Items.Clear();
            foreach (var row in ComponentPresenter.Rows(_report, _component))
            {
                var item = new ListViewItem(row.Label) { Tag = row.Key, ForeColor = HardwareUi.ColorFor(row.Tone), UseItemStyleForSubItems = true };
                item.SubItems.Add(row.Value);
                item.SubItems.Add(row.Tag);
                _readings.Items.Add(item);
            }
            _readings.EndUpdate();
            SelectReading(0);
        }

        public void SelectReading(int index)
        {
            if (_report == null || index < 0 || index >= _readings.Items.Count)
            {
                HardwareUi.WriteLines(_detail, HardwareTones.ReadingDetail(_report ?? new HardwareReport(), null));
                return;
            }
            _syncing = true;
            foreach (ListViewItem item in _readings.Items) item.Selected = false;
            _readings.Items[index].Selected = true;
            _syncing = false;
            HardwareUi.WriteLines(_detail, HardwareTones.ReadingDetail(_report, _report.Find((string)_readings.Items[index].Tag)));
        }

        /// <summary>Clears both ListViews' columns before the normal disposal cascade reaches them.
        /// Disposing a populated ListView with column headers can call ColumnHeader.get_Width(), which
        /// sends a blocking SendMessage to the native control - if that happens partway through a larger
        /// parent Form's own teardown (its handle already being torn down), the send can hang forever
        /// instead of returning. Clearing the columns first means there is nothing left to query.</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _components?.Columns.Clear();
                _readings?.Columns.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
