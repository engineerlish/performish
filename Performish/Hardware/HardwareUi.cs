using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Performish.Core.Hardware;

namespace Performish.Hardware
{
    /// <summary>Helpers for the Hardware & firmware dialog: tone-to-color mapping on the existing UiStyle
    /// palette (no new colors), the detail-pane writer, and the async read that keeps WMI/NVML reads off
    /// the UI thread.</summary>
    public static class HardwareUi
    {
        public const string NotScoredNote = "Read-only and informational - not part of the health score. Performish never changes BIOS, clock, voltage or power settings.";

        public static Color ColorFor(Tone tone) => tone switch
        {
            Tone.Heading => UiStyle.BrightAccent,
            Tone.Dim => UiStyle.Dim,
            Tone.Good => UiStyle.Accent,
            Tone.Tip => UiStyle.GradientMid,
            _ => UiStyle.Foreground
        };

        public static void WriteLines(RichTextBox box, IEnumerable<ToneLine> lines)
        {
            box.Clear();
            foreach (var line in lines)
            {
                box.SelectionStart = box.TextLength;
                box.SelectionLength = 0;
                box.SelectionColor = ColorFor(line.Tone);
                box.AppendText(line.Text + Environment.NewLine);
            }
            box.SelectionStart = 0;
        }

        /// <summary>Runs the read off the UI thread; the continuation comes back on it (WinForms
        /// synchronization context), same pattern MainForm uses for scans.</summary>
        public static async Task<(HardwareReport Report, string Error)> ReadAsync(HardwareInfoService service, bool wakeDiscreteGpu)
        {
            try
            {
                var report = await Task.Run(() => service.ReadReport(new HardwareReadOptions { WakeDiscreteGpu = wakeDiscreteGpu }));
                return (report, null);
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }

        public static void CopyToClipboard(HardwareReport report)
        {
            if (report == null) return;
            try { Clipboard.SetText(HardwareReportText.Format(report)); } catch { /* clipboard busy: best-effort */ }
        }

        public const string WakeGpuLabel = "Read the NVIDIA GPU too";
        public const string WakeGpuDescription = "On switchable-graphics laptops the NVIDIA GPU sleeps at idle; reading it wakes it briefly (uses a little battery). Nothing is changed.";
    }
}
