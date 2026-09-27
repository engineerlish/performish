using System;
using System.Collections.Generic;
using System.Linq;

namespace Performish.Core.Hardware
{
    /// <summary>Visual tone for a line of text - the WinForms layer maps it onto UiStyle colors, so
    /// presenters stay testable without WinForms. Every tone is also carried by text (a tag like [TIP] or
    /// "not available"), never by color alone.</summary>
    public enum Tone
    {
        Normal,
        Heading,
        Dim,
        Good,
        Tip,
        Info
    }

    public sealed class ToneLine
    {
        public string Text { get; set; }
        public Tone Tone { get; set; }
        public ToneLine(string text, Tone tone) { Text = text; Tone = tone; }
        public override string ToString() => Text;
    }

    public static class HardwareTones
    {
        public static Tone ForStatus(ReadingStatus s) => s switch
        {
            ReadingStatus.Measured => Tone.Normal,
            ReadingStatus.Inferred => Tone.Info,
            _ => Tone.Dim
        };

        public static Tone ForFinding(FindingKind k) => k switch
        {
            FindingKind.Good => Tone.Good,
            FindingKind.Opportunity => Tone.Tip,
            _ => Tone.Info
        };

        /// <summary>The detail-pane body for one reading (title/detail pattern): the value, where it came
        /// from or why it couldn't be read, and any finding drawn from it.</summary>
        public static List<ToneLine> ReadingDetail(HardwareReport report, HardwareReading r)
        {
            var lines = new List<ToneLine>();
            if (r == null) { lines.Add(new ToneLine("Select a reading to see where it comes from.", Tone.Dim)); return lines; }

            lines.Add(new ToneLine(r.Label, Tone.Heading));
            switch (r.Status)
            {
                case ReadingStatus.Measured:
                    lines.Add(new ToneLine(r.Value, Tone.Normal));
                    lines.Add(new ToneLine($"Read from: {r.Source}", Tone.Dim));
                    if (!string.IsNullOrEmpty(r.Note)) lines.Add(new ToneLine(r.Note, Tone.Dim));
                    break;
                case ReadingStatus.Inferred:
                    lines.Add(new ToneLine($"{r.Value} (inferred, not read directly)", Tone.Info));
                    lines.Add(new ToneLine($"Based on: {r.Source}", Tone.Dim));
                    if (!string.IsNullOrEmpty(r.Note)) lines.Add(new ToneLine(r.Note, Tone.Dim));
                    break;
                case ReadingStatus.NotApplicable:
                    lines.Add(new ToneLine("Doesn't apply to this hardware.", Tone.Dim));
                    if (!string.IsNullOrEmpty(r.Note)) lines.Add(new ToneLine(r.Note, Tone.Dim));
                    break;
                default:
                    lines.Add(new ToneLine("Not available on this machine - nothing is shown rather than a guess.", Tone.Dim));
                    if (!string.IsNullOrEmpty(r.Note)) lines.Add(new ToneLine($"Why: {r.Note}", Tone.Dim));
                    break;
            }

            foreach (var f in report.Findings.Where(f => f.ReadingKeys.Contains(r.Key)))
            {
                lines.Add(new ToneLine("", Tone.Normal));
                lines.Add(FindingHeadline(f));
                lines.Add(new ToneLine(f.Detail, Tone.Normal));
                lines.Add(new ToneLine($"What to do: {f.Guidance}", Tone.Dim));
            }
            return lines;
        }

        public static ToneLine FindingHeadline(HardwareFinding f) =>
            new ToneLine($"{HardwareReportText.FindingTag(f.Kind)} {f.Title}", ForFinding(f.Kind));

        public static string ReadableSummary(HardwareReport report)
        {
            var all = report.AllReadings.ToList();
            var readable = all.Count(r => r.IsReadable);
            return $"{readable} of {all.Count} readings available on this machine";
        }
    }

    // ---- Hardware & firmware dialog: component list, readings, detail -----------------------------

    public sealed class ComponentSummary
    {
        public HardwareComponent Component { get; set; }
        public string Title { get; set; }
        public int Readable { get; set; }
        public int Total { get; set; }
        public int Tips { get; set; }
        public string Text { get; set; }
        public Tone Tone { get; set; }
    }

    public sealed class ReadingRow
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public string Value { get; set; }
        public string Tag { get; set; }
        public Tone Tone { get; set; }
    }

    /// <summary>The Hardware & firmware dialog's display logic: a component list on the left, the selected
    /// component's findings and readings on the right, the shared detail pane below. Pure, so it's
    /// tested against simulated hardware without WinForms.</summary>
    public static class ComponentPresenter
    {
        public static List<ComponentSummary> Summaries(HardwareReport report) =>
            report.Groups.Select(g =>
            {
                var readable = g.Readings.Count(r => r.IsReadable);
                var tips = report.Findings.Count(f => f.Component == g.Component && f.Kind == FindingKind.Opportunity);
                var text = $"{g.Title}  ({readable}/{g.Readings.Count})" + (tips > 0 ? $"  {tips} tip{(tips == 1 ? "" : "s")}" : "");
                return new ComponentSummary
                {
                    Component = g.Component,
                    Title = g.Title,
                    Readable = readable,
                    Total = g.Readings.Count,
                    Tips = tips,
                    Text = text,
                    Tone = tips > 0 ? Tone.Tip : readable == 0 ? Tone.Dim : Tone.Normal
                };
            }).ToList();

        public static List<ReadingRow> Rows(HardwareReport report, HardwareComponent component)
        {
            var group = report.Group(component);
            if (group == null) return new List<ReadingRow>();
            return group.Readings.Select(r => new ReadingRow
            {
                Key = r.Key,
                Label = r.Label,
                Value = HardwareReportText.DisplayValue(r),
                Tag = HardwareReportText.StatusTag(r.Status),
                Tone = HardwareTones.ForStatus(r.Status)
            }).ToList();
        }

        /// <summary>Findings for one component, shown above its readings.</summary>
        public static List<ToneLine> FindingLines(HardwareReport report, HardwareComponent component) =>
            report.Findings.Where(f => f.Component == component)
                .OrderBy(f => f.Kind == FindingKind.Opportunity ? 0 : f.Kind == FindingKind.Info ? 1 : 2)
                .Select(HardwareTones.FindingHeadline).ToList();
    }
}
