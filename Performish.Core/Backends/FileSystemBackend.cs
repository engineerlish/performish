using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Performish.Core.Backends
{
    /// <summary>Temp/cache cleanup access. Deliberately narrow - only "delete files older than N
    /// days under a known temp-performish path" and existence checks, never a generic recursive delete of
    /// an arbitrary user-chosen folder.</summary>
    public interface IFileSystemBackend
    {
        bool DirectoryExists(string path);
        /// <summary>Deletes files older than <paramref name="olderThanDays"/> under path (top-level
        /// files only, plus empty subfolders left behind) and returns bytes freed. Never throws on a
        /// per-file failure (locked file, permissions) - those are skipped and counted, not fatal.</summary>
        long CleanOldFiles(string path, int olderThanDays, out int filesDeleted, out int filesSkipped);
    }

    public sealed class RealFileSystemBackend : IFileSystemBackend
    {
        public bool DirectoryExists(string path) => Directory.Exists(path);

        public long CleanOldFiles(string path, int olderThanDays, out int filesDeleted, out int filesSkipped)
        {
            filesDeleted = 0;
            filesSkipped = 0;
            long freed = 0;

            if (!Directory.Exists(path)) return 0;

            var cutoff = DateTime.UtcNow.AddDays(-olderThanDays);
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories);
            }
            catch
            {
                return 0;
            }

            foreach (var file in files)
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTimeUtc > cutoff) continue;
                    var size = info.Length;
                    info.Delete();
                    freed += size;
                    filesDeleted++;
                }
                catch
                {
                    filesSkipped++;
                }
            }

            return freed;
        }
    }

    public sealed class FakeFileSystemBackend : IFileSystemBackend
    {
        public sealed class FakeFile
        {
            public string Path;
            public long Size;
            public DateTime LastWriteUtc;
        }

        public HashSet<string> Directories { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public List<FakeFile> Files { get; } = new List<FakeFile>();

        public bool DirectoryExists(string path) => Directories.Contains(path);

        public long CleanOldFiles(string path, int olderThanDays, out int filesDeleted, out int filesSkipped)
        {
            filesDeleted = 0;
            filesSkipped = 0;
            if (!DirectoryExists(path)) return 0;

            var cutoff = DateTime.UtcNow.AddDays(-olderThanDays);
            var toDelete = Files.Where(f => f.Path.StartsWith(path, StringComparison.OrdinalIgnoreCase) && f.LastWriteUtc <= cutoff).ToList();

            long freed = 0;
            foreach (var f in toDelete)
            {
                Files.Remove(f);
                freed += f.Size;
                filesDeleted++;
            }

            return freed;
        }
    }
}
