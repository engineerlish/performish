using System;
using System.Drawing;
using System.Windows.Forms;
using Performish.Core.Models;

namespace Performish.Views
{
    /// <summary>Risk shown three ways so it never relies on color alone: a text label (SAFE / MOD / ADV),
    /// a fill style (outline / amber outline / solid red) and a fixed slot.</summary>
    public sealed class RiskBadge : Control
    {
        private RiskLevel _risk;

        public static int BadgeWidth => UiStyle.CharWidth * 4 + 14;
        public static int BadgeHeight => UiStyle.Mono.Height + 2;

        public RiskBadge()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            TabStop = false;
            Size = new Size(BadgeWidth, BadgeHeight);
            AccessibleRole = AccessibleRole.StaticText;
        }

        public RiskLevel Risk
        {
            get => _risk;
            set { _risk = value; AccessibleName = $"{_risk} risk"; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(UiStyle.Background);
            Paint(e.Graphics, Point.Empty, _risk);
        }

        public static string Label(RiskLevel r) => r switch { RiskLevel.Safe => "SAFE", RiskLevel.Moderate => "MOD", _ => "ADV" };

        public static void Paint(Graphics g, Point at, RiskLevel risk)
        {
            var rect = new Rectangle(at.X, at.Y, BadgeWidth, BadgeHeight);
            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
            switch (risk)
            {
                case RiskLevel.Safe:
                    using (var p = new Pen(UiStyle.LineHi)) g.DrawRectangle(p, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
                    TextRenderer.DrawText(g, Label(risk), UiStyle.Mono, rect, UiStyle.Dim, flags);
                    break;
                case RiskLevel.Moderate:
                    using (var p = new Pen(UiStyle.GradientMid)) g.DrawRectangle(p, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
                    TextRenderer.DrawText(g, Label(risk), UiStyle.MonoBold, rect, UiStyle.GradientMid, flags);
                    break;
                default:
                    using (var b = new SolidBrush(UiStyle.Error)) g.FillRectangle(b, rect);
                    TextRenderer.DrawText(g, Label(risk), UiStyle.MonoBold, rect, Color.Black, flags);
                    break;
            }
        }
    }

    /// <summary>One tweak as a selectable card. A real focusable control (roving tab stop: only the
    /// current card is a Tab stop, arrows move between cards) with an accessible name, role
    /// (check button) and checked state, so screen readers and keyboard users get the same
    /// information a sighted mouse user does.</summary>
    public sealed class TweakCard : Control
    {
        private TweakDefinition _tweak;
        private bool _checked, _isCurrent, _hover;
        private TweakState _state = TweakState.Unknown;

        public event EventHandler ToggleRequested;
        public event EventHandler ActivateRequested;
        public event Action<Keys> NavKey;

        public TweakDefinition Tweak => _tweak;
        public bool IsChecked => _checked;
        public bool IsCurrent => _isCurrent;

        public TweakCard()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
            TabStop = false;
            Font = UiStyle.Mono;
            BackColor = UiStyle.Panel;
            AccessibleRole = AccessibleRole.CheckButton;
        }

        public void Bind(TweakDefinition tweak, bool isChecked, TweakState state, bool isCurrent)
        {
            _tweak = tweak;
            _checked = isChecked;
            _state = state;
            _isCurrent = isCurrent;
            TabStop = isCurrent;
            AccessibleName = Describe();
            Invalidate();
        }

        /// <summary>The title to show for a tweak: never blank and never a type name.</summary>
        public static string DisplayTitle(TweakDefinition t) => string.IsNullOrWhiteSpace(t?.Title) ? TweakDefinition.UntitledPlaceholder : t.Title;

        /// <summary>Same action the Space key and the accessibility default action perform.</summary>
        public void Toggle() => ToggleRequested?.Invoke(this, EventArgs.Empty);

        public static string StateText(TweakState s) => s switch
        {
            TweakState.Applied => "applied",
            TweakState.NotApplicable => "not applicable",
            TweakState.NotApplied => "not applied",
            _ => "state unknown"
        };

        private string Describe() => _tweak == null ? "" : $"{DisplayTitle(_tweak)}, {_tweak.Risk} risk, {StateText(_state)}, {_tweak.Category}";

        protected override AccessibleObject CreateAccessibilityInstance() => new CardAccessible(this);

        private sealed class CardAccessible : ControlAccessibleObject
        {
            private readonly TweakCard _card;
            public CardAccessible(TweakCard card) : base(card) { _card = card; }
            public override AccessibleRole Role => AccessibleRole.CheckButton;
            public override string Name { get => _card.Describe(); set { } }
            public override string DefaultAction => "Toggle selection";
            public override AccessibleStates State =>
                AccessibleStates.Focusable | AccessibleStates.Selectable
                | (_card._checked ? AccessibleStates.Checked : AccessibleStates.None)
                | (_card.Focused ? AccessibleStates.Focused : AccessibleStates.None)
                | (_card._isCurrent ? AccessibleStates.Selected : AccessibleStates.None);
            public override void DoDefaultAction() => _card.ToggleRequested?.Invoke(_card, EventArgs.Empty);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up: case Keys.Down: case Keys.Left: case Keys.Right:
                case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End: case Keys.Space:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) { ToggleRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
            else if (e.KeyCode is Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End)
            { NavKey?.Invoke(e.KeyCode); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.Button == MouseButtons.Left)
            {
                if (e.X > Width - 52 && e.Y < 40) ToggleRequested?.Invoke(this, EventArgs.Empty);
                else ActivateRequested?.Invoke(this, EventArgs.Empty);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = new Rectangle(0, 0, Width, Height);
            using (var fill = new SolidBrush(_isCurrent ? UiStyle.Panel2 : UiStyle.Panel)) g.FillRectangle(fill, r);
            if (_tweak == null) return;

            if (_checked) using (var b = new SolidBrush(UiStyle.Accent)) g.FillRectangle(b, 0, 0, Width, 3);

            var borderColor = _isCurrent ? UiStyle.Accent : (_hover ? UiStyle.LineHi : UiStyle.Line);
            using (var p = new Pen(borderColor, _isCurrent ? 2 : 1) { Alignment = System.Drawing.Drawing2D.PenAlignment.Inset })
                g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            if (Focused)
                using (var p = new Pen(UiStyle.BrightAccent, 2) { Alignment = System.Drawing.Drawing2D.PenAlignment.Inset })
                    g.DrawRectangle(p, 2, 2, Width - 5, Height - 5);

            const TextFormatFlags f = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoClipping;
            RiskBadge.Paint(g, new Point(12, 14), _tweak.Risk);
            int cw = UiStyle.CharWidth, lh = UiStyle.Mono.Height;
            if (_state == TweakState.Applied)
                TextRenderer.DrawText(g, "+ applied", UiStyle.MonoSmall, new Point(12 + RiskBadge.BadgeWidth + 10, 15), UiStyle.Accent, f);

            var box = _checked ? "[x]" : "[ ]";
            TextRenderer.DrawText(g, box, UiStyle.Mono, new Point(Width - 12 - box.Length * cw, 14), _checked ? UiStyle.Accent : UiStyle.Dim, f);

            int chars = Math.Max(8, (Width - 24) / cw);
            int y = 44;
            foreach (var line in Wrap(DisplayTitle(_tweak), chars, 2))
            {
                TextRenderer.DrawText(g, line, UiStyle.MonoBold, new Point(12, y), UiStyle.Foreground, f);
                y += lh + 2;
            }
            TextRenderer.DrawText(g, _tweak.Category.ToString().ToLowerInvariant(), UiStyle.MonoSmall, new Point(12, Height - lh - 8), UiStyle.Faint, f);
        }

        public static System.Collections.Generic.List<string> Wrap(string text, int chars, int maxLines)
        {
            var lines = new System.Collections.Generic.List<string>();
            var words = new System.Collections.Generic.List<string>();
            foreach (var raw in (text ?? "").Split(' '))
            {
                var w = raw;
                while (w.Length > chars && chars > 4) { words.Add(w.Substring(0, chars)); w = w.Substring(chars); }
                words.Add(w);
            }
            var cur = "";
            foreach (var word in words)
            {
                if (cur.Length == 0) cur = word;
                else if (cur.Length + 1 + word.Length <= chars) cur += " " + word;
                else { lines.Add(cur); cur = word; }
            }
            if (cur.Length > 0) lines.Add(cur);
            if (lines.Count > maxLines)
            {
                lines = lines.GetRange(0, maxLines);
                var last = lines[maxLines - 1] + " ...";
                lines[maxLines - 1] = last.Length <= chars ? last : last.Substring(0, chars - 3) + "...";
            }
            return lines;
        }
    }

    /// <summary>A big-number summary tile that doubles as a filter button (results screen).</summary>
    public sealed class CounterTile : Control
    {
        private bool _active, _hover;

        public int Count { get; private set; }
        public string Caption { get; private set; } = "";
        public Color Tint { get; private set; } = UiStyle.Accent;
        public bool Active => _active;
        public event EventHandler Clicked;

        public CounterTile()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            TabStop = true;
            Size = new Size(220, 118);
            AccessibleRole = AccessibleRole.PushButton;
        }

        public void Set(int count, string caption, Color tint, bool active)
        {
            Count = count; Caption = caption; Tint = tint; _active = active;
            AccessibleName = $"{count} {caption.ToLowerInvariant()}. {(active ? "Filter on. Activate to clear." : "Activate to filter.")}";
            Invalidate();
        }

        public void PerformActivate() => Clicked?.Invoke(this, EventArgs.Empty);

        protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Enter or Keys.Space || base.IsInputKey(keyData);
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Enter or Keys.Space) { PerformActivate(); e.Handled = true; } base.OnKeyDown(e); }
        protected override void OnMouseDown(MouseEventArgs e) { Focus(); if (e.Button == MouseButtons.Left) PerformActivate(); base.OnMouseDown(e); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(_active ? UiStyle.Panel2 : UiStyle.Panel)) g.FillRectangle(b, 0, 0, Width, Height);
            using (var p = new Pen(_active ? Tint : (_hover ? UiStyle.LineHi : UiStyle.Line), _active ? 2 : 1) { Alignment = System.Drawing.Drawing2D.PenAlignment.Inset })
                g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            using (var b = new SolidBrush(Tint)) g.FillRectangle(b, 0, 0, 4, Height);
            if (Focused)
                using (var p = new Pen(UiStyle.BrightAccent, 2) { Alignment = System.Drawing.Drawing2D.PenAlignment.Inset })
                    g.DrawRectangle(p, 3, 3, Width - 7, Height - 7);
            const TextFormatFlags f = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoClipping;
            TextRenderer.DrawText(g, Count.ToString(), UiStyle.Huge, new Point(22, 12), Count == 0 ? UiStyle.Dim : Tint, f);
            TextRenderer.DrawText(g, Caption, UiStyle.MonoBold, new Point(22, Height - UiStyle.Mono.Height - 14), UiStyle.Dim, f);
        }
    }
}
