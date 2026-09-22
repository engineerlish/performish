using System.Windows.Forms;

namespace Performish.Dialogs
{
    /// <summary>Settings dialog for the preferences AppSettings already persisted but had no UI for -
    /// CreateRestorePointByDefault and BenchmarkIncludeNetwork (previously only editable by hand-
    /// editing settings.json). Checkboxes save immediately on change, matching MainForm's dry-run
    /// checkbox convention, rather than introducing a separate OK/Cancel save step for two booleans
    /// that carry no real risk if toggled and left as-is.</summary>
    public sealed class SettingsForm : Form
    {
        /// <summary>Exposed (rather than kept private) so Performish.Tests can drive them directly
        /// without a modal ShowDialog() loop - see the DialogUiTests pattern used by every other
        /// dialog's test-only accessors.</summary>
        public CheckBox RestorePointCheckBox { get; }
        public CheckBox BenchmarkNetworkCheckBox { get; }

        public SettingsForm(AppSettings settings)
        {
            Text = "Settings";
            Width = 560;
            Height = 260;
            BackColor = UiStyle.Background;
            Font = UiStyle.Mono;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;

            var body = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = false,
                BackColor = UiStyle.Background,
                Padding = new Padding(16)
            };

            var restoreLabel = UiStyle.MakeLabel("Applying tweaks", UiStyle.Dim, bold: true);
            body.Controls.Add(restoreLabel);

            RestorePointCheckBox = UiStyle.MakeCheckBox("Create a System Restore point before applying (when available)");
            RestorePointCheckBox.Checked = settings.CreateRestorePointByDefault;
            RestorePointCheckBox.CheckedChanged += (s, e) =>
            {
                settings.CreateRestorePointByDefault = RestorePointCheckBox.Checked;
                settings.Save();
            };
            RestorePointCheckBox.Margin = new Padding(4, 4, 4, 16);
            body.Controls.Add(RestorePointCheckBox);

            var benchmarkLabel = UiStyle.MakeLabel("Benchmarking", UiStyle.Dim, bold: true);
            body.Controls.Add(benchmarkLabel);

            BenchmarkNetworkCheckBox = UiStyle.MakeCheckBox("Include network metrics (DNS, gateway ping) when benchmarking");
            BenchmarkNetworkCheckBox.Checked = settings.BenchmarkIncludeNetwork;
            BenchmarkNetworkCheckBox.CheckedChanged += (s, e) =>
            {
                settings.BenchmarkIncludeNetwork = BenchmarkNetworkCheckBox.Checked;
                settings.Save();
            };
            body.Controls.Add(BenchmarkNetworkCheckBox);

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

            Controls.Add(body);
            Controls.Add(buttonPanel);

            AcceptButton = closeButton;
            CancelButton = closeButton;
        }
    }
}
