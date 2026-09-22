using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Performish.Core.Scanner;

namespace Performish.Core.Benchmark
{
    /// <summary>System-wide idle-load metrics: CPU, memory, process/thread count. All read-only,
    /// require no elevated rights, and use only APIs already available to a standard .NET/Windows
    /// process (no new package dependency - see BENCHMARK_PLAN.md "why GetSystemTimes over
    /// PerformanceCounter").</summary>
    public sealed class CpuIdleCollector : IMetricCollector
    {
        public string Name => "CPU idle";
        public string Unit => "%";
        public MetricCategory Category => MetricCategory.System;
        public bool RunByDefault => true;

        private readonly int _intervalMs;

        /// <param name="intervalMs">How long each sample's internal before/after GetSystemTimes
        /// reading is spaced apart - a real measurement window, not a fixed value; a shorter interval
        /// samples faster but is noisier.</param>
        public CpuIdleCollector(int intervalMs = 200) => _intervalMs = intervalMs;

        public MetricSampleResult Collect(int sampleCount, int warmupCount) =>
            BenchmarkSampler.Sample(Name, Unit, Category, MeasureOnce, sampleCount, warmupCount);

        private double MeasureOnce()
        {
            if (!GetSystemTimes(out var idle1, out var kernel1, out var user1))
                throw new InvalidOperationException($"GetSystemTimes failed (Win32 error {Marshal.GetLastWin32Error()}).");
            Thread.Sleep(_intervalMs);
            if (!GetSystemTimes(out var idle2, out var kernel2, out var user2))
                throw new InvalidOperationException($"GetSystemTimes failed (Win32 error {Marshal.GetLastWin32Error()}).");

            var idleDelta = ToUInt64(idle2) - ToUInt64(idle1);
            // Per the Win32 GetSystemTimes docs, kernel time already includes idle time, so total
            // busy+idle time is kernelDelta + userDelta - no separate "total" counter exists.
            var totalDelta = (ToUInt64(kernel2) - ToUInt64(kernel1)) + (ToUInt64(user2) - ToUInt64(user1));
            if (totalDelta == 0) return 100.0; // no measurable elapsed CPU time - treat as fully idle, not divide-by-zero
            return (double)idleDelta / totalDelta * 100.0;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        private static ulong ToUInt64(FILETIME ft) => ((ulong)ft.dwHighDateTime << 32) | ft.dwLowDateTime;
    }

    /// <summary>Reuses ISystemInfoBackend.GetMemory() (already used by the scanner) instead of a new
    /// P/Invoke - same real/fake backend this collector is tested against as every other Core reader.</summary>
    public sealed class MemoryAvailableCollector : IMetricCollector
    {
        public string Name => "Available memory";
        public string Unit => "MB";
        public MetricCategory Category => MetricCategory.System;
        public bool RunByDefault => true;

        private readonly ISystemInfoBackend _info;

        public MemoryAvailableCollector(ISystemInfoBackend info) => _info = info ?? throw new ArgumentNullException(nameof(info));

        public MetricSampleResult Collect(int sampleCount, int warmupCount) =>
            BenchmarkSampler.Sample(Name, Unit, Category, MeasureOnce, sampleCount, warmupCount);

        private double MeasureOnce()
        {
            var (_, availableBytes) = _info.GetMemory();
            return availableBytes / 1024.0 / 1024.0;
        }
    }

    public sealed class ProcessCountCollector : IMetricCollector
    {
        public string Name => "Process count";
        public string Unit => "processes";
        public MetricCategory Category => MetricCategory.System;
        public bool RunByDefault => true;

        public MetricSampleResult Collect(int sampleCount, int warmupCount) =>
            BenchmarkSampler.Sample(Name, Unit, Category, () => Process.GetProcesses().Length, sampleCount, warmupCount);
    }

    public sealed class ThreadCountCollector : IMetricCollector
    {
        public string Name => "Thread count";
        public string Unit => "threads";
        public MetricCategory Category => MetricCategory.System;
        public bool RunByDefault => true;

        public MetricSampleResult Collect(int sampleCount, int warmupCount) =>
            BenchmarkSampler.Sample(Name, Unit, Category, MeasureOnce, sampleCount, warmupCount);

        private static double MeasureOnce()
        {
            var total = 0;
            foreach (var p in Process.GetProcesses())
            {
                try { total += p.Threads.Count; }
                catch { /* a protected/exiting process can deny thread enumeration - skip, don't fail the whole sample */ }
            }
            return total;
        }
    }
}
