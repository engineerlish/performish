using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Performish.Core.Backup
{
    /// <summary>Per-tweak "what to restore" storage. A tweak's Apply() reads the live state before
    /// changing it and saves that snapshot here under its own tweak id; its Undo() loads it back.
    /// This is what makes undo work even in a later session (not just later in the same run) - the
    /// snapshot is what the timestamped pre-change backup actually consists of.</summary>
    public interface IUndoStore
    {
        void Save<T>(string tweakId, T snapshot);
        T Load<T>(string tweakId);
        bool Has(string tweakId);
        void Clear(string tweakId);
    }

    /// <summary>Real store: one JSON file per tweak id under the backup directory, plus a companion
    /// timestamped copy on every save so a full history of snapshots is kept, not just the latest -
    /// satisfying "timestamped backup ... before modification" as a durable trail, while Load always
    /// reads the latest (the fast path Undo needs).</summary>
    public sealed class FileUndoStore : IUndoStore
    {
        private readonly string _directory;

        public FileUndoStore(string directory)
        {
            _directory = directory;
            Directory.CreateDirectory(_directory);
        }

        private string LatestPath(string tweakId) => Path.Combine(_directory, Sanitize(tweakId) + ".latest.json");

        private static string Sanitize(string id)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                id = id.Replace(c, '_');
            return id;
        }

        public void Save<T>(string tweakId, T snapshot)
        {
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(LatestPath(tweakId), json);

            var timestampedPath = Path.Combine(_directory, $"{Sanitize(tweakId)}.{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}.json");
            try { File.WriteAllText(timestampedPath, json); } catch { /* best-effort history copy */ }
        }

        public T Load<T>(string tweakId)
        {
            var path = LatestPath(tweakId);
            if (!File.Exists(path)) return default;
            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<T>(json);
            }
            catch
            {
                return default;
            }
        }

        public bool Has(string tweakId) => File.Exists(LatestPath(tweakId));

        public void Clear(string tweakId)
        {
            var path = LatestPath(tweakId);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>In-memory store - the only IUndoStore this dev/test session constructs.</summary>
    public sealed class InMemoryUndoStore : IUndoStore
    {
        private readonly Dictionary<string, string> _data = new Dictionary<string, string>();

        public void Save<T>(string tweakId, T snapshot) =>
            _data[tweakId] = JsonSerializer.Serialize(snapshot);

        public T Load<T>(string tweakId) =>
            _data.TryGetValue(tweakId, out var json) ? JsonSerializer.Deserialize<T>(json) : default;

        public bool Has(string tweakId) => _data.ContainsKey(tweakId);

        public void Clear(string tweakId) => _data.Remove(tweakId);
    }
}
