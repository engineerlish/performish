using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Performish.Core.Models;

namespace Performish.Core.Backup
{
    public enum ChangeLogAction { Apply, Undo }

    public sealed class ChangeLogEntry
    {
        public DateTime TimestampUtc { get; set; }
        public string TweakId { get; set; }
        public string TweakName { get; set; }
        public ChangeLogAction Action { get; set; }
        public OperationOutcome Outcome { get; set; }
        public string Message { get; set; }
        public bool DryRun { get; set; }
    }

    /// <summary>Full, durable, append-only history of every tweak run - "full change log, viewable
    /// and exportable" plus the source of truth "revert everything" uses to find what's currently
    /// applied. Never held fully in memory by the store itself (each Append is a single line write);
    /// callers that need the whole history call ReadAll(), same shape as Watchlish's JSON-Lines log.</summary>
    public interface IChangeLogStore
    {
        void Append(ChangeLogEntry entry);
        List<ChangeLogEntry> ReadAll();

        /// <summary>The most recent <paramref name="maxEntries"/> entries, newest first, without
        /// necessarily reading every historical month file into memory to get them - see
        /// FileChangeLogStore's implementation. CurrentlyAppliedTweakIds() still needs true full
        /// history (a tweak could have last changed months ago) and keeps using ReadAll(); this is
        /// for the History screen, which only ever displays a bounded, recent slice (Phase 5 "avoid
        /// loading the entire log into memory").</summary>
        List<ChangeLogEntry> ReadRecent(int maxEntries);

        string ExportCsv(string destinationPath);
    }

    public sealed class FileChangeLogStore : IChangeLogStore
    {
        private readonly string _directory;

        public FileChangeLogStore(string directory)
        {
            _directory = directory;
            Directory.CreateDirectory(_directory);
        }

        private string PathForMonth(DateTime utc) => Path.Combine(_directory, $"changelog-{utc:yyyy-MM}.jsonl");

        public void Append(ChangeLogEntry entry)
        {
            var json = JsonSerializer.Serialize(entry);
            File.AppendAllText(PathForMonth(entry.TimestampUtc), json + Environment.NewLine);
        }

        public List<ChangeLogEntry> ReadAll()
        {
            var entries = new List<ChangeLogEntry>();
            if (!Directory.Exists(_directory)) return entries;

            foreach (var file in Directory.GetFiles(_directory, "changelog-*.jsonl").OrderBy(f => f))
                entries.AddRange(ReadFile(file));

            return entries.OrderBy(e => e.TimestampUtc).ToList();
        }

        public List<ChangeLogEntry> ReadRecent(int maxEntries)
        {
            var result = new List<ChangeLogEntry>();
            if (!Directory.Exists(_directory)) return result;

            // Files are named changelog-yyyy-MM.jsonl, so a plain descending string sort is already
            // newest-month-first. Stop opening older month files as soon as enough entries are
            // collected - a machine with years of history doesn't need every month read from disk
            // just to show the last 500 rows.
            foreach (var file in Directory.GetFiles(_directory, "changelog-*.jsonl").OrderByDescending(f => f))
            {
                var monthEntries = ReadFile(file).OrderByDescending(e => e.TimestampUtc);
                foreach (var entry in monthEntries)
                {
                    result.Add(entry);
                    if (result.Count >= maxEntries) return result;
                }
            }
            return result;
        }

        private static List<ChangeLogEntry> ReadFile(string path)
        {
            var entries = new List<ChangeLogEntry>();
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try { entries.Add(JsonSerializer.Deserialize<ChangeLogEntry>(line)); }
                catch { /* skip a corrupt line rather than fail the whole read */ }
            }
            return entries;
        }

        public string ExportCsv(string destinationPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("TimestampUtc,TweakId,TweakName,Action,Outcome,DryRun,Message");
            foreach (var e in ReadAll())
            {
                sb.AppendLine(string.Join(",",
                    e.TimestampUtc.ToString("O"),
                    Csv(e.TweakId), Csv(e.TweakName), e.Action, e.Outcome, e.DryRun, Csv(e.Message)));
            }
            File.WriteAllText(destinationPath, sb.ToString());
            return destinationPath;
        }

        private static string Csv(string value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
    }

    /// <summary>In-memory store - the only IChangeLogStore this dev/test session constructs.</summary>
    public sealed class InMemoryChangeLogStore : IChangeLogStore
    {
        private readonly List<ChangeLogEntry> _entries = new List<ChangeLogEntry>();

        public void Append(ChangeLogEntry entry) => _entries.Add(entry);
        public List<ChangeLogEntry> ReadAll() => _entries.OrderBy(e => e.TimestampUtc).ToList();
        public List<ChangeLogEntry> ReadRecent(int maxEntries) =>
            _entries.OrderByDescending(e => e.TimestampUtc).Take(maxEntries).ToList();

        public string ExportCsv(string destinationPath)
        {
            File.WriteAllText(destinationPath, string.Join(Environment.NewLine, _entries.Select(e => e.Message)));
            return destinationPath;
        }
    }
}
