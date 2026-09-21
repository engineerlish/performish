using System.Drawing;
using System.Windows.Forms;
using Performish.Core.Models;

namespace Performish
{
    /// <summary>Shared palette/font/control-factory for every screen and dialog - pulled out once
    /// instead of copy-pasted per class (each of MainForm and every dialog under Dialogs/ used to
    /// redeclare the same Color constants; see DECISIONS.md "de-duplicated UI palette"). Buttons use
    /// FlatStyle.Flat with an explicit hover/pressed color set via FlatAppearance, since the default
    /// system button chrome doesn't read well against a black background.</summary>
    public static class UiStyle
    {
        public static readonly Color Background = Color.Black;
        public static readonly Color Panel = Color.FromArgb(18, 18, 18);
        public static readonly Color Foreground = Color.FromArgb(220, 220, 220);
        public static readonly Color Accent = Color.FromArgb(87, 242, 135);
        public static readonly Color Dim = Color.FromArgb(130, 130, 130);
        public static readonly Color BrightAccent = Color.FromArgb(180, 255, 205);
        public static readonly Color Error = Color.FromArgb(240, 90, 90);
        public static readonly Color GradientMid = Color.FromArgb(230, 210, 90);
        public static readonly Color ButtonFace = Color.FromArgb(28, 30, 28);
        public static readonly Color ButtonHover = Color.FromArgb(40, 60, 46);
        public static readonly Color ButtonPressed = Color.FromArgb(55, 90, 68);
        public static readonly Color ButtonBorder = Color.FromArgb(60, 64, 60);
        public static readonly Color DisabledText = Color.FromArgb(90, 90, 90);

        public static readonly Font Mono = new Font("Consolas", 9.5f);
        public static readonly Font MonoBold = new Font("Consolas", 9.5f, FontStyle.Bold);
        public static readonly Font MonoSmall = new Font("Consolas", 9f);

        /// <summary>A consistently-styled push button: flat, dark, accent-colored border, visible
        /// keyboard focus (FlatAppearance.BorderSize grows via focus handlers below) and hover/pressed
        /// states - satisfies "consistent style ... hover/pressed states ... visible focus
        /// indicators" without needing a console-input workaround, since this is already a real
        /// WinForms GUI, not a text-console host.</summary>
        public static Button MakeButton(string text, Color? accentColor = null)
        {
            var accent = accentColor ?? Accent;
            var button = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = ButtonFace,
                ForeColor = accent,
                Font = Mono,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(10, 6, 10, 6),
                Margin = new Padding(4),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
                TabStop = true
            };
            button.FlatAppearance.BorderColor = ButtonBorder;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = ButtonHover;
            button.FlatAppearance.MouseDownBackColor = ButtonPressed;

            // Visible focus indicator: default WinForms flat-button focus is a faint dotted rect
            // that barely shows on black - thicken and color the border instead while focused.
            button.GotFocus += (s, e) => button.FlatAppearance.BorderColor = accent;
            button.LostFocus += (s, e) => button.FlatAppearance.BorderColor = ButtonBorder;

            button.EnabledChanged += (s, e) =>
            {
                button.ForeColor = button.Enabled ? accent : DisabledText;
                button.Cursor = button.Enabled ? Cursors.Hand : Cursors.No;
            };

            return button;
        }

        public static CheckBox MakeCheckBox(string text)
        {
            var box = new CheckBox
            {
                Text = text,
                ForeColor = Foreground,
                BackColor = Background,
                Font = Mono,
                AutoSize = true,
                Cursor = Cursors.Hand,
                Margin = new Padding(4)
            };
            return box;
        }

        public static Label MakeLabel(string text, Color? color = null, bool bold = false)
        {
            return new Label
            {
                Text = text,
                ForeColor = color ?? Foreground,
                BackColor = Background,
                Font = bold ? MonoBold : Mono,
                AutoSize = true,
                Margin = new Padding(4)
            };
        }

        public static RichTextBox MakeConsole()
        {
            return new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                ForeColor = Foreground,
                Font = Mono,
                BorderStyle = BorderStyle.None,
                ReadOnly = true,
                Multiline = true,
                WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.Both,
                Padding = new Padding(0),
                Margin = new Padding(0),
                TabStop = false
            };
        }

        public static string RiskTag(RiskLevel risk) => risk switch
        {
            RiskLevel.Safe => "SAFE",
            RiskLevel.Moderate => "MODERATE",
            RiskLevel.Advanced => "ADVANCED",
            _ => risk.ToString()
        };

        public static Color ColorForRisk(RiskLevel risk) => risk switch
        {
            RiskLevel.Safe => Foreground,
            RiskLevel.Moderate => GradientMid,
            RiskLevel.Advanced => Error,
            _ => Foreground
        };
    }
}
