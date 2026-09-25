using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Performish.Core;
using Performish.Core.Models;
using Performish.Core.Tweaks;

namespace Performish.Views
{
    /// <summary>The in-window replacement for the old Tweak Browser dialog and Presets picker: category
    /// and preset chips plus search up top, a paged grid of tweak cards (no scrolling), a detail panel
    /// under the grid that follows the current card, and an action bar. Nothing here opens a window;
    /// "Review & apply" just raises <see cref="ReviewRequested"/> and the shell shows its in-window
    /// confirmation.</summary>
    public sealed class TweaksView : UserControl
    {
        private const int CardMinWidth = 236, CardHeight = 112, Gap = 14, DetailHeight = 214;

        private readonly List<TweakDefinition> _all;
        private readonly AppServices _services;
        private readonly HashSet<string> _selectedIds;
        // Check() runs once per tweak per view refresh, never per repaint (see DECISIONS.md "tweak
        // state is read once per browser session, not once per repaint").
        private readonly Dictionary<string, TweakState> _stateCache = new Dictionary<string, TweakState>();
        private readonly List<(TweakCategory? Category, Button Button)> _categoryButtons = new List<(TweakCategory?, Button)>();
        private readonly List<(Preset Preset, Button Button)> _presetButtons = new List<(Preset, Button)>();
        private readonly List<TweakCard> _cards = new List<TweakCard>();

        private bool _built;
        private TweakCategory? _filterCategory;
        private string _searchText = "";
        private List<TweakDefinition> _visible = new List<TweakDefinition>();
        private int _cursor, _cols = 4, _pageSize = 12;

        private Panel _grid;
        private Button _prevButton, _nextButton;
        private Label _pageLabel, _countLabel;
        private TextBox _searchBox;
        private Button _reviewButton, _clearButton, _selectAllButton;
        private FlowLayoutPanel _presetFlow, _detailLeft, _detailRight;
        private Label _dTitle, _dMeta, _dDescription, _dNotes, _dSource, _dState;
        private RiskBadge _dBadge;
        private Button _toggleButton;

        public event Action<List<TweakDefinition>> ReviewRequested;

        private bool _busy;

        /// <summary>While the shell is scanning/applying, the actions that could start another batch are
        /// disabled (a few buttons, not the whole control tree).</summary>
        public void SetBusy(bool busy) { _busy = busy; RefreshAll(); }

        // ---- test/automation surface --------------------------------------------------------------

        public Button ReviewButton => _reviewButton;
        public Button ToggleButton => _toggleButton;
        public IReadOnlyList<TweakDefinition> VisibleTweaks => _visible;
        public IReadOnlyCollection<string> SelectedIds => _selectedIds;
        public IReadOnlyList<TweakCard> Cards => _cards;
        public int PageSize => _pageSize;
        public int Columns => _cols;
        public int CursorIndex => _cursor;
        public string CountText => _countLabel.Text;
        public string DetailTitle => _dTitle.Text;
        public string DetailText => string.Join("\n", new[] { _dTitle.Text, _dMeta.Text, _dDescription.Text, _dNotes.Text, _dSource.Text });
        public string PageText => _pageLabel.Text;
        public TweakCategory? FilterCategory => _filterCategory;
        public Button PrevButton => _prevButton;
        public Button NextButton => _nextButton;

        public void ClickCategory(TweakCategory? category) => _categoryButtons.First(b => b.Category == category).Button.PerformClick();
        public void ClickPreset(Preset preset) => _presetButtons.First(b => b.Preset == preset).Button.PerformClick();
        public void SetSearchText(string text) => _searchBox.Text = text;
        public void SetChecked(string tweakId, bool value)
        {
            if (value) _selectedIds.Add(tweakId); else _selectedIds.Remove(tweakId);
            RefreshAll();
        }
        public void SetCursor(int index) { SetCursorCore(index, focus: false); }
        public void RaiseReviewForTesting() => RaiseReview();
        public void GoToNextPage() => SetCursorCore(Math.Min(_visible.Count - 1, (_cursor / _pageSize + 1) * _pageSize), false);
        public void GoToPreviousPage() => SetCursorCore(Math.Max(0, (_cursor / _pageSize - 1) * _pageSize), false);
        /// <summary>Test hook: the same movement a card's arrow/PgUp/PgDn key produces.</summary>
        public void PressNavKey(Keys key) => MoveCursor(key);

        // ---- construction -------------------------------------------------------------------------

        public TweaksView(IReadOnlyList<TweakDefinition> allTweaks, AppServices services, IEnumerable<string> initialSelectedIds, TweakCategory? initialCategory)
        {
            _all = allTweaks.ToList();
            _services = services;
            _selectedIds = new HashSet<string>(initialSelectedIds ?? Enumerable.Empty<string>());
            _filterCategory = initialCategory;

            BackColor = UiStyle.Background;
            ForeColor = UiStyle.Foreground;
            Font = UiStyle.Mono;
            DoubleBuffered = true;
            AccessibleName = "Tweaks";
            AccessibleRole = AccessibleRole.Pane;

            BuildLayout();
            _built = true;
            ApplyFilter(initialCategory);
        }

        private void BuildLayout()
        {
            // --- top: title + category chips, preset chips + search ---
            var rowA = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = UiStyle.Background };
            var title = new Label { Text = "Tweaks", Font = UiStyle.Big, ForeColor = UiStyle.Foreground, AutoSize = false, Width = 110, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleLeft };
            var categoryFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = UiStyle.Background, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
            AddCategoryButton(categoryFlow, "All", null);
            foreach (var c in Enum.GetValues<TweakCategory>()) AddCategoryButton(categoryFlow, c.ToString(), c);
            rowA.Controls.Add(categoryFlow);
            rowA.Controls.Add(title);

            var rowB = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = UiStyle.Background };
            var presetLabel = new Label { Text = "Start from a preset:", ForeColor = UiStyle.Dim, Font = UiStyle.Mono, AutoSize = false, Width = 20 * UiStyle.CharWidth, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleLeft };
            _presetFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = UiStyle.Background, WrapContents = false, Padding = new Padding(0, 4, 0, 0) };
            foreach (var p in new[] { Preset.Conservative, Preset.Balanced, Preset.Aggressive })
            {
                var preset = p;
                var button = UiStyle.MakeButton($"{Presets.Name(p)} ({_services.TweakRegistry.ByPreset(p).Count()})", UiStyle.Foreground);
                button.AccessibleDescription = Presets.Description(p);
                button.Click += (s, e) => ApplyPreset(preset);
                _presetButtons.Add((p, button));
                _presetFlow.Controls.Add(button);
            }
            var searchHost = new Panel { Dock = DockStyle.Right, Width = 330, BackColor = UiStyle.Background, Padding = new Padding(0, 7, 0, 0) };
            var searchLabel = new Label { Text = "Search:", ForeColor = UiStyle.Dim, Font = UiStyle.Mono, AutoSize = false, Width = 9 * UiStyle.CharWidth, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleLeft };
            _searchBox = new TextBox { Dock = DockStyle.Fill, BackColor = UiStyle.ButtonFace, ForeColor = UiStyle.Foreground, Font = UiStyle.Mono, BorderStyle = BorderStyle.FixedSingle, AccessibleName = "Search tweaks" };
            // Matches title/description (shown) and id (hidden fallback for someone who knows it from a
            // change-log CSV or a support conversation).
            _searchBox.TextChanged += (s, e) => { _searchText = _searchBox.Text.Trim(); ApplyFilter(_filterCategory); };
            searchHost.Controls.Add(_searchBox);
            searchHost.Controls.Add(searchLabel);
            rowB.Controls.Add(_presetFlow);
            rowB.Controls.Add(presetLabel);
            rowB.Controls.Add(searchHost);

            // --- grid + pager ---
            _grid = new Panel { Dock = DockStyle.Fill, BackColor = UiStyle.Background };
            _grid.Resize += (s, e) => LayoutCards();
            _grid.MouseWheel += OnWheel;

            var pager = new Panel { Dock = DockStyle.Bottom, Height = 40, BackColor = UiStyle.Background };
            _prevButton = UiStyle.MakeButton("<");
            _prevButton.AccessibleName = "Previous page";
            _prevButton.Click += (s, e) => GoToPreviousPage();
            _nextButton = UiStyle.MakeButton(">");
            _nextButton.AccessibleName = "Next page";
            _nextButton.Click += (s, e) => GoToNextPage();
            _pageLabel = new Label { ForeColor = UiStyle.Dim, Font = UiStyle.Mono, AutoSize = false, Width = 18 * UiStyle.CharWidth, TextAlign = ContentAlignment.MiddleCenter, Height = 32 };
            pager.Controls.Add(_prevButton);
            pager.Controls.Add(_pageLabel);
            pager.Controls.Add(_nextButton);
            pager.Resize += (s, e) => LayoutPager(pager);

            var gridHost = new Panel { Dock = DockStyle.Fill, BackColor = UiStyle.Background, Padding = new Padding(0, 8, 0, 0) };
            gridHost.Controls.Add(_grid);
            gridHost.Controls.Add(pager);

            // --- detail panel (in place, under the grid) ---
            var detail = new Panel { Dock = DockStyle.Bottom, Height = DetailHeight, BackColor = UiStyle.Panel, Padding = new Padding(20, 14, 20, 10) };
            detail.Paint += (s, e) => { using var p = new Pen(UiStyle.Line); e.Graphics.DrawLine(p, 0, 0, detail.Width, 0); };
            _detailLeft = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = UiStyle.Panel };
            _detailRight = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 470, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = UiStyle.Panel };
            _dTitle = new Label { Font = UiStyle.Big, ForeColor = UiStyle.Foreground, BackColor = UiStyle.Panel, AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
            _dDescription = new Label { Font = UiStyle.Mono, ForeColor = UiStyle.Foreground, BackColor = UiStyle.Panel, AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
            _dBadge = new RiskBadge { BackColor = UiStyle.Panel, Margin = new Padding(0, 2, 0, 6) };
            _dMeta = new Label { Font = UiStyle.Mono, ForeColor = UiStyle.Dim, BackColor = UiStyle.Panel, AutoSize = true, Margin = new Padding(0, 0, 0, 2) };
            _dState = new Label { Font = UiStyle.Mono, ForeColor = UiStyle.Accent, BackColor = UiStyle.Panel, AutoSize = true, Margin = new Padding(0, 0, 0, 2) };
            _dNotes = new Label { Font = UiStyle.Mono, ForeColor = UiStyle.Dim, BackColor = UiStyle.Panel, AutoSize = true, Margin = new Padding(0, 0, 0, 2) };
            _dSource = new Label { Font = UiStyle.MonoSmall, ForeColor = UiStyle.Faint, BackColor = UiStyle.Panel, AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
            _toggleButton = UiStyle.MakeButton("Select for apply");
            _toggleButton.Margin = new Padding(0, 12, 0, 0);
            _toggleButton.Click += (s, e) => { if (CurrentTweak != null) ToggleSelected(CurrentTweak.Id); };
            _detailLeft.Controls.Add(_dTitle);
            _detailLeft.Controls.Add(_dDescription);
            _detailLeft.Controls.Add(_toggleButton);
            _detailRight.Controls.Add(_dBadge);
            _detailRight.Controls.Add(_dMeta);
            _detailRight.Controls.Add(_dState);
            _detailRight.Controls.Add(_dNotes);
            _detailRight.Controls.Add(_dSource);
            detail.Controls.Add(_detailLeft);
            detail.Controls.Add(_detailRight);
            detail.Resize += (s, e) => FitDetailLabels();
            _detailLeft.Resize += (s, e) => FitDetailLabels();

            // --- action bar ---
            var bar = new Panel { Dock = DockStyle.Bottom, Height = 62, BackColor = UiStyle.Background, Padding = new Padding(0, 8, 0, 8) };
            bar.Paint += (s, e) => { using var p = new Pen(UiStyle.Line); e.Graphics.DrawLine(p, 0, 0, bar.Width, 0); };
            _countLabel = new Label { Font = UiStyle.MonoBold, ForeColor = UiStyle.Foreground, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 0, 0, 0) };
            var barButtons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, BackColor = UiStyle.Background, WrapContents = false };
            _reviewButton = UiStyle.MakeButton("Review & apply...");
            _reviewButton.Click += (s, e) => RaiseReview();
            _clearButton = UiStyle.MakeButton("Clear", UiStyle.Dim);
            _clearButton.Click += (s, e) => { _selectedIds.Clear(); RefreshAll(); };
            _selectAllButton = UiStyle.MakeButton("Select all (in view)", UiStyle.Dim);
            _selectAllButton.Click += (s, e) => { foreach (var t in _visible) _selectedIds.Add(t.Id); RefreshAll(); };
            barButtons.Controls.Add(_reviewButton);
            barButtons.Controls.Add(_clearButton);
            barButtons.Controls.Add(_selectAllButton);
            bar.Controls.Add(_countLabel);
            bar.Controls.Add(barButtons);

            // Fill first, then bottoms (last added docks outermost), then tops.
            Controls.Add(gridHost);
            Controls.Add(detail);
            Controls.Add(bar);
            Controls.Add(rowB);
            Controls.Add(rowA);
        }

        private void LayoutPager(Panel pager)
        {
            int total = _prevButton.Width + 8 + _pageLabel.Width + 8 + _nextButton.Width;
            int x = Math.Max(0, (pager.Width - total) / 2);
            _prevButton.Location = new Point(x, 2);
            _pageLabel.Location = new Point(x + _prevButton.Width + 8, 4);
            _nextButton.Location = new Point(x + _prevButton.Width + 8 + _pageLabel.Width + 8, 2);
        }

        private void FitDetailLabels()
        {
            int w = Math.Max(100, _detailLeft.ClientSize.Width - 8);
            _dTitle.MaximumSize = new Size(w, 0);
            _dDescription.MaximumSize = new Size(w, 0);
            int rw = Math.Max(100, _detailRight.ClientSize.Width - 8);
            _dMeta.MaximumSize = new Size(rw, 0);
            _dNotes.MaximumSize = new Size(rw, 0);
            _dSource.MaximumSize = new Size(rw, 0);
        }

        private void AddCategoryButton(Control parent, string label, TweakCategory? category)
        {
            var button = UiStyle.MakeButton(label, UiStyle.Foreground);
            button.Click += (s, e) => { _cursor = 0; ApplyFilter(category); };
            _categoryButtons.Add((category, button));
            parent.Controls.Add(button);
        }

        // ---- filtering / selection ----------------------------------------------------------------

        private TweakDefinition CurrentTweak => _cursor >= 0 && _cursor < _visible.Count ? _visible[_cursor] : null;

        private static bool Matches(TweakDefinition t, string query) =>
            t.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
            t.Description.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
            t.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        private void ApplyFilter(TweakCategory? category)
        {
            _filterCategory = category;
            foreach (var (buttonCategory, button) in _categoryButtons)
            {
                var active = buttonCategory == category;
                button.BackColor = active ? UiStyle.ButtonPressed : UiStyle.ButtonFace;
                button.FlatAppearance.BorderColor = active ? UiStyle.Accent : UiStyle.ButtonBorder;
                button.AccessibleName = active ? $"{button.Text} category, selected" : $"{button.Text} category";
            }

            IEnumerable<TweakDefinition> filtered = category.HasValue ? _all.Where(t => t.Category == category.Value) : _all;
            if (!string.IsNullOrEmpty(_searchText)) filtered = filtered.Where(t => Matches(t, _searchText));
            _visible = filtered.OrderBy(t => t.Category).ThenBy(t => t.Risk).ThenBy(t => t.Title).ToList();
            _cursor = Math.Min(_cursor, Math.Max(0, _visible.Count - 1));

            EnsureStatesCached(_visible);
            LayoutCards();
        }

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

        /// <summary>Forget cached live states (call after a batch changed the system) and re-read.</summary>
        public void InvalidateStates()
        {
            _stateCache.Clear();
            EnsureStatesCached(_visible);
            RefreshAll();
        }

        public void ApplyPreset(Preset preset)
        {
            _selectedIds.Clear();
            foreach (var t in _services.TweakRegistry.ByPreset(preset)) _selectedIds.Add(t.Id);
            RefreshAll();
        }

        public void SetSelection(IEnumerable<string> ids)
        {
            _selectedIds.Clear();
            foreach (var id in ids) _selectedIds.Add(id);
            RefreshAll();
        }

        public void ShowCategory(TweakCategory? category) { _cursor = 0; ApplyFilter(category); }
        public void FocusPresets() { if (_presetFlow.Controls.Count > 0) _presetFlow.Controls[1 % _presetFlow.Controls.Count].Focus(); }

        private void ToggleSelected(string id)
        {
            if (!_selectedIds.Remove(id)) _selectedIds.Add(id);
            RefreshAll();
        }

        private void RaiseReview()
        {
            var selection = _all.Where(t => _selectedIds.Contains(t.Id)).ToList();
            if (!_busy && selection.Count > 0) ReviewRequested?.Invoke(selection);
        }

        // ---- paging / layout ----------------------------------------------------------------------

        private void LayoutCards()
        {
            if (!_built) return;
            int gw = _grid.ClientSize.Width, gh = _grid.ClientSize.Height;
            _cols = Math.Max(2, (gw + Gap) / (CardMinWidth + Gap));
            int rows = Math.Max(1, (gh + Gap) / (CardHeight + Gap));
            _pageSize = _cols * rows;
            int cardW = Math.Max(120, (gw - Gap * (_cols - 1)) / _cols);

            while (_cards.Count < _pageSize) _cards.Add(NewCard());
            while (_cards.Count > _pageSize)
            {
                var extra = _cards[_cards.Count - 1];
                _cards.RemoveAt(_cards.Count - 1);
                _grid.Controls.Remove(extra);
                extra.Dispose();
            }
            for (int k = 0; k < _cards.Count; k++)
                _cards[k].SetBounds((k % _cols) * (cardW + Gap), (k / _cols) * (CardHeight + Gap), cardW, CardHeight);
            RefreshAll();
        }

        private TweakCard NewCard()
        {
            var card = new TweakCard();
            card.MouseWheel += OnWheel;
            card.ToggleRequested += (s, e) => { if (card.Tweak != null) { SetCursorCore(_visible.IndexOf(card.Tweak), false); ToggleSelected(card.Tweak.Id); } };
            card.ActivateRequested += (s, e) => { if (card.Tweak != null) SetCursorCore(_visible.IndexOf(card.Tweak), false); };
            card.NavKey += MoveCursor;
            _grid.Controls.Add(card);
            return card;
        }

        private void OnWheel(object sender, MouseEventArgs e)
        {
            if (e.Delta > 0) GoToPreviousPage(); else GoToNextPage();
        }

        private void MoveCursor(Keys key)
        {
            int idx = _cursor;
            switch (key)
            {
                case Keys.Up: idx -= _cols; break;
                case Keys.Down: idx += _cols; break;
                case Keys.Left: idx -= 1; break;
                case Keys.Right: idx += 1; break;
                case Keys.PageUp: idx -= _pageSize; break;
                case Keys.PageDown: idx += _pageSize; break;
                case Keys.Home: idx = 0; break;
                case Keys.End: idx = _visible.Count - 1; break;
            }
            SetCursorCore(idx, focus: true);
        }

        private void SetCursorCore(int index, bool focus)
        {
            if (_visible.Count == 0) { _cursor = 0; RefreshAll(); return; }
            _cursor = Math.Max(0, Math.Min(_visible.Count - 1, index));
            RefreshAll();
            if (focus)
            {
                var card = _cards.FirstOrDefault(c => c.Tweak == CurrentTweak);
                card?.Focus();
            }
        }

        // ---- rendering ----------------------------------------------------------------------------

        private void RefreshAll()
        {
            if (!_built || _pageSize <= 0) return;
            int page = _cursor / _pageSize;
            for (int k = 0; k < _cards.Count; k++)
            {
                int idx = page * _pageSize + k;
                var card = _cards[k];
                if (idx < _visible.Count)
                {
                    var t = _visible[idx];
                    card.Visible = true;
                    card.Bind(t, _selectedIds.Contains(t.Id), _stateCache.TryGetValue(t.Id, out var s) ? s : TweakState.Unknown, idx == _cursor);
                }
                else card.Visible = false;
            }

            int pages = Math.Max(1, (_visible.Count + _pageSize - 1) / _pageSize);
            _pageLabel.Text = $"page {page + 1} of {pages}";
            _prevButton.Enabled = page > 0;
            _nextButton.Enabled = page < pages - 1;

            int n = _selectedIds.Count;
            int safe = _all.Count(t => _selectedIds.Contains(t.Id) && t.Risk == RiskLevel.Safe);
            int mod = _all.Count(t => _selectedIds.Contains(t.Id) && t.Risk == RiskLevel.Moderate);
            int adv = _all.Count(t => _selectedIds.Contains(t.Id) && t.Risk == RiskLevel.Advanced);
            _countLabel.Text = $"{n} selected   ({safe} safe, {mod} moderate, {adv} advanced)   -   {_visible.Count} tweak(s) in this view";
            _reviewButton.Enabled = n > 0 && !_busy;
            _clearButton.Enabled = n > 0 && !_busy;
            RenderDetail();
        }

        private static string UndoDescription(TweakDefinition t) =>
            t.Category == TweakCategory.Maintenance
                ? "Undo: not possible - this tweak deletes files, and deleted files cannot be restored."
                : "Undo: restores the exact previous value/state this tweak is about to change.";

        private void RenderDetail()
        {
            var t = CurrentTweak;
            if (t == null)
            {
                _dTitle.Text = _visible.Count == 0 ? "No tweaks match" : "Select a tweak";
                _dDescription.Text = _visible.Count == 0 ? "Try a different category or search." : "";
                _dBadge.Visible = false; _dMeta.Text = ""; _dState.Text = ""; _dNotes.Text = ""; _dSource.Text = "";
                _toggleButton.Visible = false;
                return;
            }
            _dTitle.Text = TweakCard.DisplayTitle(t);
            _dDescription.Text = t.Description;
            _dBadge.Visible = true;
            _dBadge.Risk = t.Risk;
            _dMeta.Text = $"{t.Category}  |  scope: {t.Scope}  |  reboot: {(t.RebootRequired ? "yes" : "no")}";
            var state = _stateCache.TryGetValue(t.Id, out var s) ? s : TweakState.Unknown;
            _dState.Text = state == TweakState.Applied ? "+ currently applied" : "";
            _dNotes.Text = UndoDescription(t);
            _dSource.Text = "Source: " + t.Source;
            var chosen = _selectedIds.Contains(t.Id);
            _toggleButton.Visible = true;
            _toggleButton.Text = chosen ? "Deselect" : "Select for apply";
            FitDetailLabels();
        }
    }
}
