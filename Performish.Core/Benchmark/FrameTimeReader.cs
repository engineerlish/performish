using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Performish.Core.Benchmark
{
    public sealed class FrameTimeReport
    {
        public int SampleCount { get; set; }
        public double AverageFps { get; set; }
        public double P1LowFps { get; set; }   // 99th percentile frame time -> 1% low FPS
        public double P0Point1LowFps { get; set; } // 99.9th percentile frame time -> 0.1% low FPS
        public double AverageFrameTimeMs { get; set; }
    }

    /// <summary>Reads a frame-time CSV the user already captured with PresentMon or CapFrameX - Performish
    /// never launches or bundles a capture tool itself (see DECISIONS.md), it only imports results so
    /// benchmarking claims stay measured, never invented. Understands both tools' "MsBetweenPresents" /
    /// "FrameTime" style column naming.</summary>
    public static class FrameTimeReader
    {
        private static readonly string[] FrameTimeColumnNames = { "MsBetweenPresents", "FrameTime", "msBetweenPresents" };

        public static FrameTimeReport Parse(string csvPath)
        {
            if (!File.Exists(csvPath)) throw new FileNotFoundException("Frame-time CSV not found.", csvPath);
            return ParseLines(File.ReadLines(csvPath));
        }

        public static FrameTimeReport ParseLines(IEnumerable<string> lines)
        {
            using var enumerator = lines.GetEnumerator();
            if (!enumerator.MoveNext()) return Empty();

            var header = SplitCsvLine(enumerator.Current);
            var frameTimeColumn = -1;
            for (var i = 0; i < header.Length; i++)
            {
                if (FrameTimeColumnNames.Any(n => string.Equals(n, header[i], StringComparison.OrdinalIgnoreCase)))
                {
                    frameTimeColumn = i;
                    break;
                }
            }
            if (frameTimeColumn < 0) return Empty();

            var frameTimesMs = new List<double>();
            while (enumerator.MoveNext())
            {
                var fields = SplitCsvLine(enumerator.Current);
                if (frameTimeColumn >= fields.Length) continue;
                if (double.TryParse(fields[frameTimeColumn], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) && ms > 0)
                    frameTimesMs.Add(ms);
            }

            if (frameTimesMs.Count == 0) return Empty();

            frameTimesMs.Sort();
            var avgMs = frameTimesMs.Average();
            var slowest1PctMs = AverageOfSlowest(frameTimesMs, 0.01);     // "1% low" convention
            var slowest0Point1PctMs = AverageOfSlowest(frameTimesMs, 0.001); // "0.1% low" convention

            return new FrameTimeReport
            {
                SampleCount = frameTimesMs.Count,
                AverageFrameTimeMs = avgMs,
                AverageFps = 1000.0 / avgMs,
                P1LowFps = 1000.0 / slowest1PctMs,
                P0Point1LowFps = 1000.0 / slowest0Point1PctMs
            };
        }

        /// <summary>The "X% low" FPS convention used by CapFrameX/PresentMon-based tools: the average
        /// frame time of the slowest X% of frames (not a single percentile point), which is what
        /// actually correlates with perceived stutter.</summary>
        private static double AverageOfSlowest(List<double> sortedAscending, double fraction)
        {
            var count = Math.Max(1, (int)Math.Round(sortedAscending.Count * fraction));
            var slice = sortedAscending.GetRange(sortedAscending.Count - count, count);
            return slice.Average();
        }

        private static FrameTimeReport Empty() => new FrameTimeReport();

        private static string[] SplitCsvLine(string line) => line.Split(',');
    }
}
