using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Performish.Core.Benchmark;
using Performish.Core.Scanner;

namespace Performish.Views
{
    /// <summary>The home screen: the banner/log console and grouped action buttons on the left, and a
    /// system + health panel on the right that fills in after a scan.</summary>
    public sealed class HomeView : UserControl
    {
        private readonly Dictionary<string, Label> _rows = new Dictionary<string, Label>();
        private readonly Label _healthValue, _healthOutOf, _healthNote, _emptyNote;
        private readonly Panel _healthBar;
        private double _healthFraction;
        private Color _healthColor = UiStyle.Dim;

        /// <summary>The banner, status and log console.</summary>
        public RichTextBox Console { get; }
        private readonly Panel _left;

        /// <summary>Docks a control (the grouped action-button sections) under the console.</summary>
        public void AddBottom(Control control) { control.Dock = DockStyle.Bottom; _left.Controls.Add(control); }

        // test surface
        public string RowText(string key) => _rows[key].Text;
        public string HealthText => _healthValue.Text;
        public string HealthNote => _healthNote.Text;
        public bool ShowsEmptyNote => _emptyNote.Visible;

        private static readonly string[] RowKeys = { "os", "cpu", "gpu", "memory", "power", "disk", "uptime", "startup" };

        public HomeView()
        {
            BackColor = UiStyle.Background;
            ForeColor = UiStyle.Foreground;
            Font = UiStyle.Mono;
            AccessibleName = "Home";
            AccessibleRole = AccessibleRole.Pane;

            Console = UiStyle.MakeConsole();
            Console.AccessibleName = "Status and log";
            _left = new Panel { Dock = DockStyle.Fill, BackColor = UiStyle.Background, Padding = new Padding(24, 16, 12, 8) };
            _left.Controls.Add(Console);

            var side = new Panel { Dock = DockStyle.Right, Width = 340, BackColor = UiStyle.Background, Padding = new Padding(0, 16, 24, 16) };
            var box = new Panel { Dock = DockStyle.Top, Height = 420, BackColor = UiStyle.Background };
            box.Paint += (s, e) => { using var p = new Pen(UiStyle.Line); e.Graphics.DrawRectangle(p, 0, 0, box.Width - 1, box.Height - 1); };
            side.Controls.Add(box);

            var systemHeader = new Label { Text = "SYSTEM", Font = UiStyle.MonoSmall, ForeColor = UiStyle.Faint, AutoSize = true, Location = new Point(16, 14) };
            box.Controls.Add(systemHeader);
            _emptyNote = new Label { Text = "No scan yet. Click Scan to read this machine (read-only, nothing changes).", ForeColor = UiStyle.Dim, Font = UiStyle.Mono, AutoSize = false, Location = new Point(16, 44), Size = new Size(290, 80) };
            box.Controls.Add(_emptyNote);

            int y = 44;
            foreach (var key in RowKeys)
            {
                var k = new Label { Text = key, ForeColor = UiStyle.Dim, Font = UiStyle.Mono, AutoSize = false, Location = new Point(16, y), Size = new Size(68, UiStyle.Mono.Height + 4) };
                var v = new Label { Text = "", ForeColor = UiStyle.Foreground, Font = UiStyle.Mono, AutoSize = false, Location = new Point(88, y), Size = new Size(230, UiStyle.Mono.Height + 4) };
                k.Visible = v.Visible = false;
                k.AccessibleName = key; v.AccessibleName = key;
                box.Controls.Add(k);
                box.Controls.Add(v);
                _rows[key] = v;
                _rowKeys.Add(k);
                y += UiStyle.Mono.Height + 8;
            }

            var healthHeader = new Label { Text = "HEALTH", Font = UiStyle.MonoSmall, ForeColor = UiStyle.Faint, AutoSize = true, Location = new Point(16, y + 12), Visible = false };
            _healthValue = new Label { Font = UiStyle.Huge, ForeColor = UiStyle.GradientMid, AutoSize = true, Location = new Point(12, y + 30), Visible = false };
            _healthOutOf = new Label { Text = "/ 100", ForeColor = UiStyle.Dim, Font = UiStyle.Mono, AutoSize = true, Location = new Point(110, y + 66), Visible = false };
            _healthBar = new Panel { Location = new Point(16, y + 100), Size = new Size(300, 6), BackColor = UiStyle.Background, Visible = false };
            _healthBar.Paint += (s, e) =>
            {
                using var track = new SolidBrush(UiStyle.Line);
                using var fill = new SolidBrush(_healthColor);
                e.Graphics.FillRectangle(track, 0, 1, _healthBar.Width, 4);
                e.Graphics.FillRectangle(fill, 0, 1, (int)(_healthBar.Width * _healthFraction), 4);
            };
            _healthNote = new Label { ForeColor = UiStyle.Dim, Font = UiStyle.Mono, AutoSize = true, Location = new Point(16, y + 114), Visible = false };
            _healthGroup = new Control[] { healthHeader, _healthValue, _healthOutOf, _healthBar, _healthNote };
            foreach (var c in _healthGroup) box.Controls.Add(c);
            box.Height = y + 150;

            Controls.Add(_left);
            Controls.Add(side);
        }

        private readonly List<Label> _rowKeys = new List<Label>();
        private readonly Control[] _healthGroup;

        public void ShowNoScan()
        {
            _emptyNote.Visible = true;
            foreach (var k in _rowKeys) k.Visible = false;
            foreach (var v in _rows.Values) v.Visible = false;
            foreach (var c in _healthGroup) c.Visible = false;
        }

        public void ShowScan(SystemSnapshot s)
        {
            static string Fit(string t, int max) => t.Length <= max ? t : t.Substring(0, max - 3) + "...";
            static string Gb(long bytes) => $"{bytes / 1024.0 / 1024.0 / 1024.0:0.0} GB";
            _emptyNote.Visible = false;
            _rows["os"].Text = Fit($"{s.WindowsProductName}, build {s.BuildNumber}" + (s.IsWindows11 ? "" : " [not Windows 11]"), 30);
            _rows["cpu"].Text = $"{s.CpuCoreCount} cores / {s.CpuLogicalProcessorCount} threads";
            _rows["gpu"].Text = Fit(s.Gpus.Count == 0 ? "(none detected)" : string.Join(", ", s.Gpus.Select(g => g.Name)), 30);
            _rows["memory"].Text = $"{Gb(s.UsedRamBytes)} / {Gb(s.TotalRamBytes)} used";
            _rows["power"].Text = Fit(s.ActivePowerPlanName + (s.IsLaptop ? " (laptop)" : ""), 30);
            var freePct = s.TotalDiskSpaceBytes > 0 ? (double)s.FreeDiskSpaceBytes / s.TotalDiskSpaceBytes * 100.0 : 0;
            _rows["disk"].Text = $"{Gb(s.FreeDiskSpaceBytes)} free ({freePct:0}%)";
            _rows["uptime"].Text = $"{s.UptimeHours:0.0} hours";
            _rows["startup"].Text = $"{s.StartupItems.Count} items";
            foreach (var k in _rowKeys) k.Visible = true;
            foreach (var v in _rows.Values) v.Visible = true;
            _rows["disk"].ForeColor = freePct < 10 ? UiStyle.Error : UiStyle.Foreground;

            var health = HealthScore.Compute(s);
            _healthValue.Text = health.Score.ToString();
            _healthColor = health.Score >= 75 ? UiStyle.Accent : (health.Score >= 50 ? UiStyle.GradientMid : UiStyle.Error);
            _healthValue.ForeColor = _healthColor;
            _healthOutOf.Text = $"/ {HealthScore.TotalMax}";
            _healthFraction = HealthScore.TotalMax > 0 ? Math.Min(1.0, (double)health.Score / HealthScore.TotalMax) : 0;
            int improvable = health.Factors.Count(f => f.Points < f.MaxPoints);
            _healthNote.Text = improvable == 0 ? "Every factor is at full marks" : $"{improvable} factor{(improvable == 1 ? "" : "s")} can be improved";
            foreach (var c in _healthGroup) c.Visible = true;
            _healthBar.Invalidate();
        }
    }
}
