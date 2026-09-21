using System.Windows.Forms;

namespace Performish.Dialogs
{
    /// <summary>A single-button informational dialog styled to match the rest of the app - used
    /// instead of System.Windows.Forms.MessageBox so every dialog in Performish shares one palette
    /// (MessageBox always renders with the OS theme, which would be a jarring black/white/system-blue
    /// mismatch against everything else here).</summary>
    public static class MessageDialog
    {
        public static void Show(IWin32Window owner, string title, string message)
        {
            using var form = new Form
            {
                Text = title,
                Width = 480,
                Height = 220,
                BackColor = UiStyle.Background,
                Font = UiStyle.Mono,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowIcon = false
            };

            var label = UiStyle.MakeLabel(message, UiStyle.Foreground);
            label.MaximumSize = new System.Drawing.Size(440, 0);
            var host = new Panel { Dock = DockStyle.Fill, BackColor = UiStyle.Background, Padding = new Padding(16) };
            host.Controls.Add(label);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                BackColor = UiStyle.Background,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(10)
            };
            var okButton = UiStyle.MakeButton("OK");
            okButton.Click += (s, e) => form.Close();
            buttonPanel.Controls.Add(okButton);

            form.Controls.Add(host);
            form.Controls.Add(buttonPanel);
            form.AcceptButton = okButton;
            form.CancelButton = okButton;

            form.ShowDialog(owner);
        }
    }
}
