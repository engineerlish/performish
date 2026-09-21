using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Performish.Core.Models;
using Performish.Core.Tweaks;

namespace Performish.Dialogs
{
    /// <summary>One button per preset - button replacement for the old 1/2/3/Esc keybind screen.</summary>
    public sealed class PresetPickerForm : Form
    {
        public Preset? Chosen { get; private set; }

        private readonly System.Collections.Generic.Dictionary<Preset, Button> _presetButtons =
            new System.Collections.Generic.Dictionary<Preset, Button>();

        /// <summary>Test-only accessor - see Dialogs/DialogUiTests.cs.</summary>
        public void ClickPreset(Preset preset) => _presetButtons[preset].PerformClick();

        public PresetPickerForm(TweakRegistry registry)
        {
            Text = "Presets";
            Width = 640;
            Height = 420;
            BackColor = UiStyle.Background;
            Font = UiStyle.Mono;
            StartPosition = FormStartPosition.CenterParent;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = UiStyle.Background,
                Padding = new Padding(16)
            };

            foreach (var preset in new[] { Preset.Conservative, Preset.Balanced, Preset.Aggressive })
            {
                var count = registry.ByPreset(preset).Count();
                var row = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0, 0, 0, 14) };

                var button = UiStyle.MakeButton($"{Presets.Name(preset)} ({count} tweaks)");
                button.Width = 560;
                button.Click += (s, e) => { Chosen = preset; DialogResult = DialogResult.OK; Close(); };
                _presetButtons[preset] = button;

                var description = UiStyle.MakeLabel(Presets.Description(preset), UiStyle.Dim);
                description.MaximumSize = new Size(560, 0);

                row.Controls.Add(button);
                row.Controls.Add(description);
                layout.Controls.Add(row);
            }

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                BackColor = UiStyle.Background,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(10)
            };
            var cancelButton = UiStyle.MakeButton("Cancel", UiStyle.Dim);
            cancelButton.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            buttonPanel.Controls.Add(cancelButton);

            Controls.Add(layout);
            Controls.Add(buttonPanel);
            CancelButton = cancelButton;
        }
    }
}
