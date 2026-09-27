using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Performish.Core.Hardware
{
    /// <summary>Returns a caller-supplied raw snapshot - the only IHardwareInfoBackend tests and fake
    /// app services use. Records the options it was called with so tests can check the "don't wake the
    /// discrete GPU unless asked" plumbing.</summary>
    public sealed class FakeHardwareInfoBackend : IHardwareInfoBackend
    {
        public HardwareRawSnapshot Snapshot { get; set; }
        public List<HardwareReadOptions> Calls { get; } = new List<HardwareReadOptions>();

        public FakeHardwareInfoBackend(HardwareRawSnapshot snapshot = null) => Snapshot = snapshot ?? new HardwareRawSnapshot();

        public HardwareRawSnapshot Read(HardwareReadOptions options)
        {
            Calls.Add(options ?? new HardwareReadOptions());
            return Snapshot;
        }
    }

    /// <summary>Reads and builds a report in one call. The UI depends on this, never on a backend.
    /// Constructed in AppServices (real or fake backend); constructing it does no I/O.</summary>
    public sealed class HardwareInfoService
    {
        private readonly IHardwareInfoBackend _backend;
        private readonly Func<DateTime> _clock;

        public HardwareInfoService(IHardwareInfoBackend backend, Func<DateTime> clock = null)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _clock = clock ?? (() => DateTime.UtcNow);
        }

        public HardwareReport ReadReport(HardwareReadOptions options = null) =>
            HardwareReportBuilder.Build(_backend.Read(options ?? new HardwareReadOptions()), _clock());
    }

    /// <summary>Plain-text rendering of a report for the dialog's "Copy as text" - handy for pasting a
    /// machine's readings into a bug report or comparing two machines.</summary>
    public static class HardwareReportText
    {
        /// <summary>Only inferred values get a tag: an unreadable value already says "not available"/"n/a"
        /// as its display value, so a second marker would just repeat it.</summary>
        public static string StatusTag(ReadingStatus s) => s == ReadingStatus.Inferred ? "[likely]" : "";

        public static string FindingTag(FindingKind k) => k switch
        {
            FindingKind.Good => "[OK]",
            FindingKind.Opportunity => "[TIP]",
            _ => "[INFO]"
        };

        /// <summary>What to show in a value column: the value, or a dim stand-in that never looks like data.</summary>
        public static string DisplayValue(HardwareReading r) => r.Status switch
        {
            ReadingStatus.Measured => r.Value ?? "",
            ReadingStatus.Inferred => r.Value ?? "",
            ReadingStatus.NotApplicable => "n/a",
            _ => "not available"
        };

        public static string Format(HardwareReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Performish hardware report (read-only) - {report.TakenAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}");
            sb.AppendLine("Informational only: none of this feeds the health score, and Performish changes none of it.");

            foreach (var group in report.Groups)
            {
                sb.AppendLine();
                sb.AppendLine(group.Title.ToUpperInvariant());
                var width = Math.Min(34, group.Readings.Select(r => r.Label.Length).DefaultIfEmpty(10).Max());
                foreach (var r in group.Readings)
                {
                    sb.Append("  ").Append(r.Label.PadRight(width)).Append("  ").Append(DisplayValue(r));
                    if (r.Status == ReadingStatus.Inferred) sb.Append(" [likely]");
                    sb.AppendLine();
                    // The reason goes on its own indented line so it wraps cleanly in a narrow console or paste.
                    if ((!r.IsReadable || r.Status == ReadingStatus.Inferred) && !string.IsNullOrEmpty(r.Note))
                        sb.Append("  ").Append(new string(' ', width)).Append("    ").AppendLine(r.Note);
                }
            }

            if (report.Findings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("WORTH KNOWING");
                foreach (var f in report.Findings.OrderBy(f => f.Kind == FindingKind.Opportunity ? 0 : f.Kind == FindingKind.Info ? 1 : 2))
                {
                    sb.AppendLine($"  {FindingTag(f.Kind)} {f.Title}");
                    sb.AppendLine($"       {f.Detail}");
                    sb.AppendLine($"       What to do: {f.Guidance}");
                }
            }
            return sb.ToString();
        }
    }
}
