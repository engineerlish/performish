using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Performish.Core.Benchmark
{
    /// <summary>Durable history of benchmark runs - same append-only, one-file-per-month JSON-Lines
    /// shape as FileChangeLogStore, under the same LocalStateRoot (never %AppData%, for the OneDrive-
    /// redirection reason the change log already documents). Lets the user see a trend over time and
    /// compare any two runs, not just the most recent.</summary>
    public interface IBenchmarkRunStore
    {
        void Save(BenchmarkRun run);
        List<BenchmarkRun> ReadAll();
        List<BenchmarkRun> ReadRecent(int maxRuns);
        BenchmarkRun Find(string id);
    }

    public sealed class FileBenchmarkRunStore : IBenchmarkRunStore
    {
        private readonly string _directory;

        public FileBenchmarkRunStore(string directory)
        {
            _directory = directory;
            Directory.CreateDirectory(_directory);
        }

        private string PathForMonth(DateTime utc) => Path.Combine(_directory, $"benchmark-{utc:yyyy-MM}.jsonl");

        public void Save(BenchmarkRun run)
        {
            var json = JsonSerializer.Serialize(run);
            File.AppendAllText(PathForMonth(run.TimestampUtc), json + Environment.NewLine);
        }

        public List<BenchmarkRun> ReadAll()
        {
            var runs = new List<BenchmarkRun>();
            if (!Directory.Exists(_directory)) return runs;

            foreach (var file in Directory.GetFiles(_directory, "benchmark-*.jsonl").OrderBy(f => f))
                runs.AddRange(ReadFile(file));

            return runs.OrderBy(r => r.TimestampUtc).ToList();
        }

        public List<BenchmarkRun> ReadRecent(int maxRuns)
        {
            var result = new List<BenchmarkRun>();
            if (!Directory.Exists(_directory)) return result;

            foreach (var file in Directory.GetFiles(_directory, "benchmark-*.jsonl").OrderByDescending(f => f))
            {
                foreach (var run in ReadFile(file).OrderByDescending(r => r.TimestampUtc))
                {
                    result.Add(run);
                    if (result.Count >= maxRuns) return result;
                }
            }
            return result;
        }

        public BenchmarkRun Find(string id) => ReadAll().FirstOrDefault(r => r.Id == id);

        private static List<BenchmarkRun> ReadFile(string path)
        {
            var runs = new List<BenchmarkRun>();
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try { runs.Add(JsonSerializer.Deserialize<BenchmarkRun>(line)); }
                catch { /* skip a corrupt line rather than fail the whole read */ }
            }
            return runs;
        }
    }

    /// <summary>In-memory store - the only IBenchmarkRunStore this dev/test session constructs.</summary>
    public sealed class InMemoryBenchmarkRunStore : IBenchmarkRunStore
    {
        private readonly List<BenchmarkRun> _runs = new List<BenchmarkRun>();

        public void Save(BenchmarkRun run) => _runs.Add(run);
        public List<BenchmarkRun> ReadAll() => _runs.OrderBy(r => r.TimestampUtc).ToList();
        public List<BenchmarkRun> ReadRecent(int maxRuns) => _runs.OrderByDescending(r => r.TimestampUtc).Take(maxRuns).ToList();
        public BenchmarkRun Find(string id) => _runs.FirstOrDefault(r => r.Id == id);
    }
}
