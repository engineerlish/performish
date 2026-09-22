using System;
using System.Diagnostics;
using System.IO;

namespace Performish.Core.Benchmark
{
    /// <summary>Disk throughput/latency collectors. Every temp file these create is a fresh,
    /// Performish-owned path under the given directory (the system Temp folder by default), deleted
    /// in a finally block regardless of success/failure - never touches, reads, or risks any of the
    /// user's own files, per the task's "must clean them up completely and never touch real user
    /// data" rule.</summary>
    public sealed class DiskWriteThroughputCollector : IMetricCollector
    {
        public string Name => "Disk write throughput";
        public string Unit => "MB/s";
        public MetricCategory Category => MetricCategory.Disk;
        public bool RunByDefault => true;
        public bool HigherIsBetter => true;

        // 8 MB: large enough that per-call filesystem overhead doesn't dominate the timing, small/
        // fast enough (well under a second even on a slow drive) not to be disruptive.
        internal const int FileSizeBytes = 8 * 1024 * 1024;

        private readonly string _directory;

        public DiskWriteThroughputCollector(string directory = null) => _directory = directory ?? Path.GetTempPath();

        public MetricSampleResult Collect(int sampleCount, int warmupCount)
        {
            var path = Path.Combine(_directory, $"performish-bench-write-{Guid.NewGuid():N}.tmp");
            try
            {
                var buffer = new byte[FileSizeBytes]; // content is irrelevant, only size - zero-filled is fine
                return BenchmarkSampler.Sample(Name, Unit, Category, () => MeasureOnce(path, buffer), sampleCount, warmupCount);
            }
            finally
            {
                TryDelete(path);
            }
        }

        private static double MeasureOnce(string path, byte[] buffer)
        {
            var sw = Stopwatch.StartNew();
            // WriteThrough asks Windows to commit to disk rather than just to the OS write cache -
            // a more honest "how fast can this drive actually write" number, at the cost of being
            // slower than a cached write would report.
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                fs.Write(buffer, 0, buffer.Length);
            sw.Stop();

            var seconds = sw.Elapsed.TotalSeconds;
            return seconds > 0 ? buffer.Length / 1024.0 / 1024.0 / seconds : 0.0;
        }

        internal static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort cleanup */ }
        }
    }

    public sealed class DiskReadThroughputCollector : IMetricCollector
    {
        public string Name => "Disk read throughput";
        public string Unit => "MB/s";
        public MetricCategory Category => MetricCategory.Disk;
        public bool RunByDefault => true;
        public bool HigherIsBetter => true;

        private const int FileSizeBytes = DiskWriteThroughputCollector.FileSizeBytes;
        private readonly string _directory;

        public DiskReadThroughputCollector(string directory = null) => _directory = directory ?? Path.GetTempPath();

        public MetricSampleResult Collect(int sampleCount, int warmupCount)
        {
            var path = Path.Combine(_directory, $"performish-bench-read-{Guid.NewGuid():N}.tmp");
            try
            {
                // Written once, untimed, purely so there is real content to read - not part of the
                // measurement itself.
                File.WriteAllBytes(path, new byte[FileSizeBytes]);
                var readBuffer = new byte[FileSizeBytes];
                return BenchmarkSampler.Sample(Name, Unit, Category, () => MeasureOnce(path, readBuffer), sampleCount, warmupCount);
            }
            finally
            {
                DiskWriteThroughputCollector.TryDelete(path);
            }
        }

        private static double MeasureOnce(string path, byte[] buffer)
        {
            var sw = Stopwatch.StartNew();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan))
            {
                var offset = 0;
                int read;
                while (offset < buffer.Length && (read = fs.Read(buffer, offset, buffer.Length - offset)) > 0)
                    offset += read;
            }
            sw.Stop();

            var seconds = sw.Elapsed.TotalSeconds;
            return seconds > 0 ? buffer.Length / 1024.0 / 1024.0 / seconds : 0.0;
        }
    }

    /// <summary>How long it takes to enumerate a fixed, always-present, Performish-owned folder - a
    /// cheap proxy for filesystem responsiveness that doesn't depend on the size of any real user
    /// folder. Uses the system Temp directory (same one the disk throughput collectors use) rather
    /// than a user folder like Documents, so the result never depends on how much the user has
    /// stored there.</summary>
    public sealed class DirectoryListingLatencyCollector : IMetricCollector
    {
        public string Name => "Directory listing latency";
        public string Unit => "ms";
        public MetricCategory Category => MetricCategory.Disk;
        public bool RunByDefault => true;
        public bool HigherIsBetter => false;

        private readonly string _directory;

        public DirectoryListingLatencyCollector(string directory = null) => _directory = directory ?? Path.GetTempPath();

        public MetricSampleResult Collect(int sampleCount, int warmupCount) =>
            BenchmarkSampler.Sample(Name, Unit, Category, MeasureOnce, sampleCount, warmupCount);

        private double MeasureOnce()
        {
            var sw = Stopwatch.StartNew();
            var count = 0;
            foreach (var _ in Directory.EnumerateFileSystemEntries(_directory)) count++;
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds;
        }
    }
}
