using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Performish.Views
{
    /// <summary>The one small, consistent interruption: a confirmation card centered over a darkened
    /// snapshot of the current view, inside the main window (not a separate window). Used for apply,
    /// revert-everything and drift-reapply. Focus starts on Cancel so Enter-mashing cancels rather than
    /// applies; Esc cancels; Tab is trapped inside the card while it is open.</summary>
    public sealed class ConfirmOverlay : Panel
    {
        private readonly Panel _card;
        private readonly Label _title;
        private readonly RichTextBox _body;
        private readonly Button _confirm, _cancel;
        private TaskCompletionSource<bool> _tcs;
        private Color _border = UiStyle.Accent;

        public bool IsOpen => Visible && _tcs != null;
        public Button ConfirmButton => _confirm;
        public Button CancelActionButton => _cancel;
        public string TitleText => _title.Text;
        public string BodyText => _body.Text;
        public void ConfirmForTesting() => Finish(true);
        public void CancelForTesting() => Finish(false);

        public ConfirmOverlay()
        {
            Dock = DockStyle.Fill;
            Visible = false;
            BackColor = Color.FromArgb(6, 7, 6);
            BackgroundImageLayout = ImageLayout.Stretch;
            AccessibleName = "Confirmation";
            AccessibleRole = AccessibleRole.Dialog;

            _card = new Panel { BackColor = UiStyle.Panel, Padding = new Padding(24, 20, 24, 16) };
            _card.Paint += (s, e) =>
            {
                using var p = new Pen(_border, 2) { Alignment = System.Drawing.Drawing2D.PenAlignment.Inset };
                e.Graphics.DrawRectangle(p, 0, 0, _card.Width - 1, _card.Height - 1);
            };
            _title = new Label { Font = UiStyle.Big, ForeColor = UiStyle.Foreground, BackColor = UiStyle.Panel, AutoSize = false, Dock = DockStyle.Top, Height = 40, TextAlign = ContentAlignment.MiddleLeft };
            _body = UiStyle.MakeConsole();
            _body.BackColor = UiStyle.Panel;
            _body.WordWrap = true;
            _body.ScrollBars = RichTextBoxScrollBars.Vertical;
            _body.AccessibleName = "What will happen";
            _body.TabStop = true;

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, FlowDirection = FlowDirection.RightToLeft, BackColor = UiStyle.Panel, Padding = new Padding(0, 10, 0, 0) };
            _confirm = UiStyle.MakeButton("Confirm", UiStyle.Error);
            _confirm.Click += (s, e) => Finish(true);
            _cancel = UiStyle.MakeButton("Cancel", UiStyle.Foreground);
            _cancel.Click += (s, e) => Finish(false);
            // Right-to-left: first added is rightmost. Confirm sits left of Cancel and Cancel has focus.
            buttons.Controls.Add(_cancel);
            buttons.Controls.Add(_confirm);

            var bodyHost = new Panel { Dock = DockStyle.Fill, BackColor = UiStyle.Panel };
            bodyHost.Controls.Add(_body);
            _card.Controls.Add(bodyHost);
            _card.Controls.Add(buttons);
            _card.Controls.Add(_title);
            Controls.Add(_card);
            Resize += (s, e) => CenterCard();
        }

        /// <summary>Shows the card and completes with true on Confirm, false on Cancel/Esc.</summary>
        /// <param name="backdropSource">The control whose current appearance becomes the dimmed backdrop.</param>
        public Task<bool> ShowAsync(Control backdropSource, string title,
            IEnumerable<(string Text, Color Color)> lines, string confirmLabel, string cancelLabel, bool danger)
        {
            if (_tcs != null) return _tcs.Task; // already open: never stack two confirmations

            var lineList = lines.ToList();
            _title.Text = title;
            _border = danger ? UiStyle.Error : UiStyle.Accent;
            _confirm.Text = confirmLabel;
            _confirm.ForeColor = danger ? UiStyle.Error : UiStyle.Accent;
            _cancel.Text = cancelLabel;
            _body.Clear();
            foreach (var (text, color) in lineList)
            {
                _body.SelectionStart = _body.TextLength;
                _body.SelectionColor = color;
                _body.AppendText(text + Environment.NewLine);
            }
            _body.SelectionStart = 0;
            _body.SelectionLength = 0;

            int bodyHeight = Math.Min(280, Math.Max(80, lineList.Count * (UiStyle.Mono.Height + 3) + 16));
            _card.Height = 20 + 40 + bodyHeight + 54 + 16;

            SetBackdrop(backdropSource);

            _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Visible = true;
            BringToFront();
            CenterCard();
            _cancel.Focus();
            return _tcs.Task;
        }

        private void SetBackdrop(Control source)
        {
            var old = BackgroundImage;
            BackgroundImage = null;
            old?.Dispose();
            try
            {
                if (source == null || source.Width <= 0 || source.Height <= 0) return;
                var bmp = new Bitmap(source.Width, source.Height);
                source.DrawToBitmap(bmp, new Rectangle(0, 0, source.Width, source.Height));
                using (var g = Graphics.FromImage(bmp))
                using (var dim = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
                    g.FillRectangle(dim, 0, 0, bmp.Width, bmp.Height);
                BackgroundImage = bmp;
            }
            catch
            {
                // A missing snapshot only costs the dimmed backdrop; the solid dark panel still hides the view.
            }
        }

        private void CenterCard()
        {
            if (_card == null) return;
            _card.Width = Math.Min(620, Math.Max(360, ClientSize.Width - 48));
            _card.Left = (ClientSize.Width - _card.Width) / 2;
            _card.Top = Math.Max(16, (ClientSize.Height - _card.Height) / 2);
        }

        private void Finish(bool result)
        {
            var tcs = _tcs;
            if (tcs == null) return;
            _tcs = null;
            Visible = false;
            var old = BackgroundImage;
            BackgroundImage = null;
            old?.Dispose();
            tcs.TrySetResult(result);
        }

        /// <summary>Tab/Shift+Tab cycle only through this card's own controls. The views behind stay
        /// enabled (toggling Enabled across the whole control tree is many native calls and slow on
        /// machines with window-hooking security software); the backdrop covers them for the mouse
        /// and this keeps the keyboard inside the card.</summary>
        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (IsOpen && (keyData & Keys.KeyCode) == Keys.Tab)
            {
                var ring = new Control[] { _body, _confirm, _cancel };
                int i = Array.FindIndex(ring, c => c.Focused);
                int dir = (keyData & Keys.Shift) != 0 ? -1 : 1;
                i = i < 0 ? 2 : (i + dir + ring.Length) % ring.Length;
                ring[i].Focus();
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && IsOpen) { Finish(false); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
