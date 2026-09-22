using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Performish.Core.Models;
using Performish.Core.Scanner;
using Performish.Core.Tweaks;

namespace Performish.Dialogs
{
    /// <summary>Lets the user review this machine's own sign-in startup list (HKCU/HKLM ...\Run) and
    /// pick specific entries to stop auto-launching - the biggest single fixable gap the health-score
    /// audit found (see HEALTH_ANALYSIS.md/DECISIONS.md "Health score accuracy audit"). Unlike the
    /// fixed tweak catalog (TweakBrowserForm), every row here is a tweak built on the fly from this
    /// scan (StartupItemTweaks.BuildFor) - nothing here is ever part of a preset; every pick is
    /// explicit, matching the task's "recommend-first" rule for anything discovered rather than
    /// reviewed ahead of time. Same button-UI/checklist/detail-pane pattern as TweakBrowserForm.</summary>
    public sealed class StartupItemsForm : Form
    {
        private readonly List<TweakDefinition> _tweaks;

        private CheckedListBox _list;
        private RichTextBox _detail;
        private Button _reviewButton;
        private Label _countLabel;
        private readonly HashSet<string> _selectedIds = new HashSet<string>();

        public List<TweakDefinition> ConfirmedSelection { get; private set; }

        /// <summary>Test-only accessors, same pattern as TweakBrowserForm.</summary>
        public Button ReviewButton => _reviewButton;
        public IReadOnlyList<TweakDefinition> Items => _tweaks;
        public IReadOnlyCollection<string> SelectedIds => _selectedIds;

        public void SetChecked(string tweakId, bool value)
        {
            var index = _tweaks.FindIndex(t => t.Id == tweakId);
            if (index < 0) throw new ArgumentException($"'{tweakId}' is not in this list.");
            _list.SetItemChecked(index, value);
        }

        public StartupItemsForm(IReadOnlyList<StartupItemInfo> items)
        {
            _tweaks = items.Select(StartupItemTweaks.BuildFor).OrderBy(t => t.Title).ToList();

            Text = "Startup items";
            Width = 900;
            Height = 620;
            BackColor = UiStyle.Background;
            Font = UiStyle.Mono;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(700, 420);

            BuildLayout();
            Populate();
        }

        private void BuildLayout()
        {
            _countLabel = UiStyle.MakeLabel("", UiStyle.Dim);
            _countLabel.Dock = DockStyle.Top;
            _countLabel.Height = 24;
            _countLabel.Padding = new Padding(12, 4, 0, 2);

            var introLabel = UiStyle.MakeLabel(
                "These launch automatically when you sign in. Stopping one here only removes its automatic " +
                "launch - the app itself is not uninstalled, and you can still open it manually. Nothing is " +
                "changed until you click Review & Apply.", UiStyle.Dim);
            introLabel.Dock = DockStyle.Top;
            introLabel.Height = 40;
            introLabel.Padding = new Padding(12, 0, 12, 6);
            introLabel.AutoEllipsis = true;

            _list = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                BackColor = UiStyle.Background,
                ForeColor = UiStyle.Foreground,
                Font = UiStyle.Mono,
                BorderStyle = BorderStyle.None,
                CheckOnClick = true,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 22,
                IntegralHeight = false
            };
            _list.DrawItem += ListOnDrawItem;
            _list.ItemCheck += ListOnItemCheck;
            _list.SelectedIndexChanged += (s, e) => RenderDetail();

            _detail = UiStyle.MakeConsole();
            _detail.Height = 90;

            var listHost = new Panel { Dock = DockStyle.Fill, BackColor = UiStyle.Background, Padding = new Padding(12, 0, 12, 0) };
            listHost.Controls.Add(_list);

            var detailHost = new Panel { Dock = DockStyle.Bottom, Height = 100, BackColor = UiStyle.Background, Padding = new Padding(12, 4, 12, 4) };
            detailHost.Controls.Add(_detail);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                BackColor = UiStyle.Background,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(10)
            };

            var cancelButton = UiStyle.MakeButton("Close", UiStyle.Dim);
            cancelButton.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            _reviewButton = UiStyle.MakeButton("Review & Apply...", UiStyle.Accent);
            _reviewButton.Enabled = false;
            _reviewButton.Click += (s, e) =>
            {
                ConfirmedSelection = _tweaks.Where(t => _selectedIds.Contains(t.Id)).ToList();
                DialogResult = DialogResult.OK;
                Close();
            };

            buttonPanel.Controls.Add(cancelButton);
            buttonPanel.Controls.Add(_reviewButton);

            var topPanel = new Panel { Dock = DockStyle.Top, Height = 24 + 40, BackColor = UiStyle.Background };
            topPanel.Controls.Add(introLabel);
            topPanel.Controls.Add(_countLabel);

            Controls.Add(listHost);
            Controls.Add(detailHost);
            Controls.Add(buttonPanel);
            Controls.Add(topPanel);

            CancelButton = cancelButton;
        }

        private void Populate()
        {
            _list.ItemCheck -= ListOnItemCheck;
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var t in _tweaks) _list.Items.Add(t, false);
            _list.EndUpdate();
            _list.ItemCheck += ListOnItemCheck;

            UpdateCountAndButton();
            RenderDetail();
        }

        private void UpdateCountAndButton()
        {
            _countLabel.Text = _tweaks.Count == 0
                ? "No startup items found (HKCU/HKLM Run keys are empty)."
                : $"{_tweaks.Count} startup item(s) - {_selectedIds.Count} selected to stop";
            _reviewButton.Enabled = _selectedIds.Count > 0;
        }

        private void ListOnItemCheck(object sender, ItemCheckEventArgs e)
        {
            var tweak = _tweaks[e.Index];
            if (e.NewValue == CheckState.Checked) _selectedIds.Add(tweak.Id);
            else _selectedIds.Remove(tweak.Id);
            UpdateCountAndButton();
        }

        private void ListOnDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _tweaks.Count) return;
            var tweak = _tweaks[e.Index];
            var isChecked = _list.GetItemChecked(e.Index);
            var isCursor = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            using (var bg = new SolidBrush(isCursor ? UiStyle.ButtonHover : UiStyle.Background))
                e.Graphics.FillRectangle(bg, e.Bounds);

            var checkRect = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + (e.Bounds.Height - 14) / 2, 14, 14);
            ControlPaint.DrawCheckBox(e.Graphics, checkRect,
                (isChecked ? ButtonState.Checked : ButtonState.Normal) | ButtonState.Flat);

            var text = $"{tweak.Title,-45} {tweak.Scope}";
            using var textBrush = new SolidBrush(UiStyle.Foreground);
            e.Graphics.DrawString(text, e.Font ?? UiStyle.Mono, textBrush, checkRect.Right + 8, e.Bounds.Y + 3);

            if (isCursor) e.DrawFocusRectangle();
        }

        private void RenderDetail()
        {
            _detail.Clear();
            if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _tweaks.Count)
            {
                AppendDetail(_tweaks.Count == 0 ? "Nothing to show." : "Select an item to see its detail.", UiStyle.Dim);
                return;
            }

            var t = _tweaks[_list.SelectedIndex];
            AppendDetail(t.Title, UiStyle.BrightAccent);
            AppendDetail(t.Description, UiStyle.Foreground);
            AppendDetail("", UiStyle.Foreground);
            AppendDetail($"Scope: {t.Scope}   Reboot needed: no   Undo: restores this exact entry.", UiStyle.Dim);
            AppendDetail("Source: " + t.Source, UiStyle.Dim);
        }

        private void AppendDetail(string text, Color color)
        {
            _detail.SelectionStart = _detail.TextLength;
            _detail.SelectionColor = color;
            _detail.AppendText(text + Environment.NewLine);
        }
    }
}
