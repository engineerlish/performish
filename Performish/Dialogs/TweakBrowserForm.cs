using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Performish.Core;
using Performish.Core.Models;
using Performish.Core.Tweaks;

namespace Performish.Dialogs
{
    /// <summary>Tweak browser as a real, clickable control set - the button-UI replacement for the
    /// old keybind-driven list (Up/Down/Space/A/N/0-4/Enter/Esc). A native owner-drawn
    /// CheckedListBox gives real mouse click-to-toggle, native keyboard Up/Down + Space, and a
    /// visible focus rectangle for free; owner-draw is used only to keep the risk-level color coding
    /// the console version had, not to reimplement input handling.</summary>
    public sealed class TweakBrowserForm : Form
    {
        private readonly List<TweakDefinition> _allTweaks;
        private readonly Performish.Core.AppServices _services;
        private readonly HashSet<string> _selectedIds;

        private TweakCategory? _filterCategory;
        private List<TweakDefinition> _visibleTweaks = new List<TweakDefinition>();
        // Not a Dictionary<TweakCategory?, Button>: System.Collections.Generic.Dictionary throws
        // ArgumentNullException on a null key even when TKey is a Nullable<T> (confirmed by
        // DialogUiTests catching this at construction time - the "All" button's key is null). A
        // small linear-scan list is simpler than a null-sentinel workaround for 5 buttons.
        private readonly List<(TweakCategory? Category, Button Button)> _categoryButtons = new List<(TweakCategory?, Button)>();
        // Check() results are cached per open of this form (one real-backend read per tweak, not one
        // per repaint) - DrawItem fires on every scroll/paint, and re-running Check() there would mean
        // re-issuing a registry/service/PowerShell read on every single redraw. See DECISIONS.md
        // "tweak state is read once per browser session, not once per repaint".
        private readonly Dictionary<string, TweakState> _stateCache = new Dictionary<string, TweakState>();

        private CheckedListBox _list;
        private RichTextBox _detail;
        private Button _reviewButton;
        private Label _countLabel;
        private TextBox _searchBox;
        private string _searchText = "";

        public List<TweakDefinition> ConfirmedSelection { get; private set; }

        /// <summary>Test-only accessors (also harmless for any future caller) so Performish.Tests can
        /// simulate real input events - PerformClick() on category/review buttons, toggling a check
        /// state the same way CheckOnClick does - without going through ShowDialog()'s blocking modal
        /// loop. See Dialogs/DialogUiTests.cs.</summary>
        public Button ReviewButton => _reviewButton;
        public IReadOnlyList<TweakDefinition> VisibleTweaks => _visibleTweaks;
        public IReadOnlyCollection<string> SelectedIds => _selectedIds;

        public void ClickCategory(TweakCategory? category) =>
            _categoryButtons.First(b => b.Category == category).Button.PerformClick();

        /// <summary>Test-only accessor - sets the search box text and triggers the same filtering a
        /// real keystroke would (TextChanged), without needing to simulate individual key events.</summary>
        public void SetSearchText(string text) => _searchBox.Text = text;

        public void SetChecked(string tweakId, bool value)
        {
            var index = _visibleTweaks.FindIndex(t => t.Id == tweakId);
            if (index < 0) throw new ArgumentException($"'{tweakId}' is not in the currently visible tweak list.");
            _list.SetItemChecked(index, value);
        }

        public TweakBrowserForm(IReadOnlyList<TweakDefinition> allTweaks, Performish.Core.AppServices services,
            IEnumerable<string> initialSelectedIds, TweakCategory? initialCategory)
        {
            _allTweaks = allTweaks.ToList();
            _services = services;
            _selectedIds = new HashSet<string>(initialSelectedIds);
            _filterCategory = initialCategory;

            Text = "Browse tweaks";
            Width = 1000;
            Height = 700;
            BackColor = UiStyle.Background;
            Font = UiStyle.Mono;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 480);

            BuildLayout();
            ApplyFilter(initialCategory);
        }

        private void BuildLayout()
        {
            var categoryPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = UiStyle.Background,
                Padding = new Padding(10, 6, 10, 6)
            };
            AddCategoryButton(categoryPanel, "All", null);
            AddCategoryButton(categoryPanel, "Debloat", TweakCategory.Debloat);
            AddCategoryButton(categoryPanel, "Performance", TweakCategory.Performance);
            AddCategoryButton(categoryPanel, "Gaming", TweakCategory.Gaming);
            AddCategoryButton(categoryPanel, "Network", TweakCategory.Network);
            AddCategoryButton(categoryPanel, "Maintenance", TweakCategory.Maintenance);

            var searchPanel = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = UiStyle.Background, Padding = new Padding(12, 4, 12, 4) };
            var searchLabel = UiStyle.MakeLabel("Search:", UiStyle.Dim);
            searchLabel.Dock = DockStyle.Left;
            searchLabel.TextAlign = ContentAlignment.MiddleLeft;
            _searchBox = new TextBox
            {
                Dock = DockStyle.Fill,
                BackColor = UiStyle.ButtonFace,
                ForeColor = UiStyle.Foreground,
                Font = UiStyle.Mono,
                BorderStyle = BorderStyle.FixedSingle
            };
            // Matches title/description (visible, primary) and id (hidden fallback - so a technician
            // who knows the id can still jump straight to it, without the id being what's displayed).
            _searchBox.TextChanged += (s, e) => { _searchText = _searchBox.Text.Trim(); ApplyFilter(_filterCategory); };
            searchPanel.Controls.Add(_searchBox);
            searchPanel.Controls.Add(searchLabel);

            _countLabel = UiStyle.MakeLabel("", UiStyle.Dim);

            var topPanel = new Panel { Dock = DockStyle.Top, Height = 44 + 24 + 34, BackColor = UiStyle.Background };
            categoryPanel.Dock = DockStyle.Top;
            searchPanel.Dock = DockStyle.Top;
            _countLabel.Dock = DockStyle.Top;
            _countLabel.Padding = new Padding(12, 2, 0, 2);
            topPanel.Controls.Add(_countLabel);
            topPanel.Controls.Add(searchPanel);
            topPanel.Controls.Add(categoryPanel);

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
            _reviewButton.Click += (s, e) =>
            {
                ConfirmedSelection = _allTweaks.Where(t => _selectedIds.Contains(t.Id)).ToList();
                DialogResult = DialogResult.OK;
                Close();
            };

            var selectAllButton = UiStyle.MakeButton("Select all (in view)");
            selectAllButton.Click += (s, e) => { foreach (var t in _visibleTweaks) _selectedIds.Add(t.Id); RefreshCheckStates(); };

            var selectNoneButton = UiStyle.MakeButton("Select none (in view)");
            selectNoneButton.Click += (s, e) => { foreach (var t in _visibleTweaks) _selectedIds.Remove(t.Id); RefreshCheckStates(); };

            buttonPanel.Controls.Add(cancelButton);
            buttonPanel.Controls.Add(_reviewButton);
            buttonPanel.Controls.Add(selectNoneButton);
            buttonPanel.Controls.Add(selectAllButton);

            Controls.Add(listHost);
            Controls.Add(detailHost);
            Controls.Add(buttonPanel);
            Controls.Add(topPanel);

            CancelButton = cancelButton;
        }

        private void AddCategoryButton(Control parent, string label, TweakCategory? category)
        {
            var button = UiStyle.MakeButton(label);
            button.Click += (s, e) => ApplyFilter(category);
            _categoryButtons.Add((category, button));
            parent.Controls.Add(button);
        }

        private void ApplyFilter(TweakCategory? category)
        {
            _filterCategory = category;
            foreach (var (buttonCategory, button) in _categoryButtons)
            {
                var active = buttonCategory == category;
                button.BackColor = active ? UiStyle.ButtonPressed : UiStyle.ButtonFace;
                button.FlatAppearance.BorderColor = active ? UiStyle.Accent : UiStyle.ButtonBorder;
            }

            IEnumerable<TweakDefinition> filtered = category.HasValue ? _allTweaks.Where(t => t.Category == category.Value) : _allTweaks;
            if (!string.IsNullOrEmpty(_searchText))
                filtered = filtered.Where(t => Matches(t, _searchText));

            _visibleTweaks = filtered
                .OrderBy(t => t.Category).ThenBy(t => t.Risk).ThenBy(t => t.Title).ToList();

            EnsureStatesCached(_visibleTweaks);
            RefreshCheckStates();
        }

        /// <summary>Search matches on title and description (what's shown), and the id as a hidden
        /// fallback (never displayed, just makes the id itself searchable for anyone who knows it -
        /// e.g. from a change-log CSV or a support conversation).</summary>
        private static bool Matches(TweakDefinition t, string query) =>
            t.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
            t.Description.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
            t.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        private void EnsureStatesCached(IEnumerable<TweakDefinition> tweaks)
        {
            var ctx = _services.CreateContext(dryRun: true);
            foreach (var t in tweaks)
            {
                if (_stateCache.ContainsKey(t.Id)) continue;
                try { _stateCache[t.Id] = t.Check(ctx); }
                catch { _stateCache[t.Id] = TweakState.Unknown; }
            }
        }

        private void RefreshCheckStates()
        {
            _list.ItemCheck -= ListOnItemCheck;
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var t in _visibleTweaks)
                _list.Items.Add(t, _selectedIds.Contains(t.Id));
            _list.EndUpdate();
            _list.ItemCheck += ListOnItemCheck;

            var selectedInView = _visibleTweaks.Count(t => _selectedIds.Contains(t.Id));
            _countLabel.Text = $"{_visibleTweaks.Count} tweak(s) in this view - {selectedInView} selected here, {_selectedIds.Count} selected overall";
            _reviewButton.Enabled = _selectedIds.Count > 0;
            RenderDetail();
        }

        private void ListOnItemCheck(object sender, ItemCheckEventArgs e)
        {
            var tweak = _visibleTweaks[e.Index];
            if (e.NewValue == CheckState.Checked) _selectedIds.Add(tweak.Id);
            else _selectedIds.Remove(tweak.Id);

            // _selectedIds (above) is already correct at this point - e.NewValue is the state the
            // check is ABOUT to become, so no need to wait for it to "commit" or read
            // _list.CheckedItems (which would still show the pre-toggle state here). An earlier
            // version deferred this via BeginInvoke on the mistaken assumption that it needed to wait
            // a tick; that just meant the button/count never updated in a test driving SetItemChecked
            // directly (no message loop pumping the posted callback) - caught by DialogUiTests.
            var selectedInView = _visibleTweaks.Count(t => _selectedIds.Contains(t.Id));
            _countLabel.Text = $"{_visibleTweaks.Count} tweak(s) in this view - {selectedInView} selected here, {_selectedIds.Count} selected overall";
            _reviewButton.Enabled = _selectedIds.Count > 0;
        }

        /// <summary>How many characters of the title the list row has room for before it must be
        /// truncated - the row is a fixed-width monospace layout (risk tag, title, state, scope), so a
        /// character count is an exact width, not an approximation. The full, untruncated title is
        /// always still shown in the detail pane below.</summary>
        public const int TitleColumnWidth = 56;

        /// <summary>Builds the exact row text for a tweak, placeholder-safe and truncated - pulled out
        /// of ListOnDrawItem so tests can assert on it directly without driving a real owner-draw paint
        /// event. Never shows the type name or the raw id; an empty/missing title shows
        /// TweakDefinition.UntitledPlaceholder instead of falling back silently.</summary>
        public static string BuildRowText(TweakDefinition tweak, TweakState state)
        {
            var title = string.IsNullOrWhiteSpace(tweak.Title) ? TweakDefinition.UntitledPlaceholder : tweak.Title;
            if (title.Length > TitleColumnWidth) title = title.Substring(0, TitleColumnWidth - 1) + "…";

            var stateText = state switch
            {
                TweakState.Applied => "applied",
                TweakState.NotApplied => "not applied",
                TweakState.NotApplicable => "n/a",
                _ => "unknown"
            };

            return $"[{UiStyle.RiskTag(tweak.Risk),-8}] {title,-TitleColumnWidth} {stateText,-12} {tweak.Scope}" +
                (tweak.RebootRequired ? "  (reboot)" : "");
        }

        private void ListOnDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _visibleTweaks.Count) return;
            var tweak = _visibleTweaks[e.Index];
            var isChecked = _list.GetItemChecked(e.Index);
            var isCursor = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            using (var bg = new SolidBrush(isCursor ? UiStyle.ButtonHover : UiStyle.Background))
                e.Graphics.FillRectangle(bg, e.Bounds);

            var checkRect = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + (e.Bounds.Height - 14) / 2, 14, 14);
            ControlPaint.DrawCheckBox(e.Graphics, checkRect,
                (isChecked ? ButtonState.Checked : ButtonState.Normal) | ButtonState.Flat);

            var state = _stateCache.TryGetValue(tweak.Id, out var cached) ? cached : TweakState.Unknown;
            var text = BuildRowText(tweak, state);

            using var textBrush = new SolidBrush(UiStyle.ColorForRisk(tweak.Risk));
            e.Graphics.DrawString(text, e.Font ?? UiStyle.Mono, textBrush, checkRect.Right + 8, e.Bounds.Y + 3);

            if (isCursor) e.DrawFocusRectangle();
        }

        /// <summary>Expands the full detail for the selected tweak on click/selection: what it
        /// changes and why (Description), risk level, reboot/admin requirements, and what Undo does -
        /// everything the task's "clicking a tweak opens the detailed explanation" spec asks for,
        /// beyond just the raw Description field.</summary>
        private void RenderDetail()
        {
            _detail.Clear();
            if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _visibleTweaks.Count)
            {
                AppendDetail("Select a tweak to see its full explanation.", UiStyle.Dim);
                return;
            }

            var t = _visibleTweaks[_list.SelectedIndex];
            AppendDetail(string.IsNullOrWhiteSpace(t.Title) ? TweakDefinition.UntitledPlaceholder : t.Title, UiStyle.BrightAccent);
            AppendDetail(t.Description, UiStyle.Foreground);
            AppendDetail("", UiStyle.Foreground);
            AppendDetail($"Risk: {UiStyle.RiskTag(t.Risk)}   Scope: {t.Scope}   " +
                $"Reboot needed: {(t.RebootRequired ? "yes" : "no")}   " +
                $"Admin rights: {(t.Scope == TweakScope.Machine ? "required" : "usually required (Performish runs elevated)")}",
                UiStyle.Dim);
            AppendDetail(UndoDescription(t), UiStyle.Dim);
            AppendDetail("Source: " + t.Source, UiStyle.Dim);
        }

        private static string UndoDescription(TweakDefinition t) =>
            t.Category == TweakCategory.Maintenance
                ? "Undo: not possible - this tweak deletes files, and deleted files cannot be restored."
                : "Undo: restores the exact previous value/state this tweak is about to change.";

        private void AppendDetail(string text, Color color)
        {
            _detail.SelectionStart = _detail.TextLength;
            _detail.SelectionColor = color;
            _detail.AppendText(text + Environment.NewLine);
        }
    }
}
