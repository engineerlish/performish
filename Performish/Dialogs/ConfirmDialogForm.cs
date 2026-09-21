using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Performish.Dialogs
{
    /// <summary>Generic modal confirmation dialog with explicit Confirm/Cancel buttons - the
    /// button-UI replacement for the old text-screen "ENTER confirm, ESC cancel" pattern. Used for
    /// both "apply these tweaks" and "revert everything". Esc always cancels (Form.CancelButton);
    /// focus defaults to Cancel so accidental Enter-mashing on a Moderate/Advanced batch cancels
    /// rather than applies - Tab still reaches Confirm for anyone who means it.</summary>
    public sealed class ConfirmDialogForm : Form
    {
        public bool Confirmed { get; private set; }

        /// <summary>Exposed (rather than kept private) so Performish.Tests can PerformClick() them
        /// directly without going through the blocking ShowDialog() modal loop - see
        /// Dialogs/DialogUiTests.cs.</summary>
        public Button ConfirmButton { get; }
        public Button CancelActionButton { get; }

        public ConfirmDialogForm(string title, IEnumerable<(string Text, Color Color)> lines, string confirmLabel, string cancelLabel)
        {
            Text = title;
            Width = 720;
            Height = 520;
            BackColor = UiStyle.Background;
            Font = UiStyle.Mono;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;

            var body = UiStyle.MakeConsole();
            foreach (var (text, color) in lines)
            {
                body.SelectionStart = body.TextLength;
                body.SelectionColor = color;
                body.AppendText(text + Environment.NewLine);
            }
            body.SelectionStart = 0;
            body.SelectionLength = 0;

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                BackColor = UiStyle.Background,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(10)
            };

            var cancelButton = UiStyle.MakeButton(cancelLabel, UiStyle.Dim);
            cancelButton.Click += (s, e) => { Confirmed = false; DialogResult = DialogResult.Cancel; Close(); };

            var confirmButton = UiStyle.MakeButton(confirmLabel, UiStyle.Error);
            confirmButton.Click += (s, e) => { Confirmed = true; DialogResult = DialogResult.OK; Close(); };

            // RightToLeft flow means the first-added control lands on the far right - add Cancel
            // first so it's the rightmost/"last" button, matching standard dialog convention
            // (Confirm on the left of Cancel reads oddly; most platforms put Cancel rightmost is also
            // common - what matters here is CancelButton/AcceptButton wiring below, not visual order).
            buttonPanel.Controls.Add(cancelButton);
            buttonPanel.Controls.Add(confirmButton);

            var host = new Panel { Dock = DockStyle.Fill, BackColor = UiStyle.Background, Padding = new Padding(12) };
            host.Controls.Add(body);

            Controls.Add(host);
            Controls.Add(buttonPanel);

            CancelButton = cancelButton;
            ActiveControl = cancelButton;

            ConfirmButton = confirmButton;
            CancelActionButton = cancelButton;
        }

        /// <summary>Shows the dialog modally and returns true only if Confirm was clicked (or
        /// activated via Enter while focused/Tab-reached).</summary>
        public static bool Show(IWin32Window owner, string title, IEnumerable<(string Text, Color Color)> lines,
            string confirmLabel = "Confirm", string cancelLabel = "Cancel")
        {
            using var dialog = new ConfirmDialogForm(title, lines, confirmLabel, cancelLabel);
            dialog.ShowDialog(owner);
            return dialog.Confirmed;
        }
    }
}
