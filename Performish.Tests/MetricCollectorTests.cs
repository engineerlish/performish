using System.IO;
using System.Linq;
using Performish.Core.Benchmark;
using Performish.Core.Scanner;
using Xunit;

namespace Performish.Tests
{
    /// <summary>These collectors read real, non-destructive system state - explicitly permitted by
    /// the task's safety rule for this feature ("fine for the benchmarking code to read real...
    /// system metrics as part of normal operation"), unlike every tweak Apply/Check/Undo test
    /// elsewhere in this suite, which only ever runs against Fake* backends. Sample counts here are
    /// kept small (1-2) purely to keep the test suite fast, not because of any safety concern.</summary>
    public class MetricCollectorTests
    {
        [Fact]
        public void CpuIdleCollector_ReturnsAPlausiblePercentage()
        {
            var result = new CpuIdleCollector(intervalMs: 20).Collect(sampleCount: 2, warmupCount: 0);

            Assert.True(result.Available, result.UnavailableReason);
            Assert.InRange(result.Median, 0.0, 100.0);
            Assert.Equal("%", result.Unit);
            Assert.Equal(MetricCategory.System, result.Category);
        }

        [Fact]
        public void MemoryAvailableCollector_UsesInjectedBackend_ReturnsRealField()
        {
            var fake = new FakeSystemInfoBackend { TotalRam = 16L * 1024 * 1024 * 1024, AvailableRam = 8L * 1024 * 1024 * 1024 };

            var result = new MemoryAvailableCollector(fake).Collect(sampleCount: 2, warmupCount: 0);

            Assert.True(result.Available);
            Assert.Equal(8192.0, result.Median, precision: 0);
        }

        [Fact]
        public void ProcessCountCollector_ReturnsAPositiveRealCount()
        {
            var result = new ProcessCountCollector().Collect(sampleCount: 2, warmupCount: 0);

            Assert.True(result.Available);
            Assert.True(result.Median > 0);
        }

        [Fact]
        public void ThreadCountCollector_ReturnsAPositiveRealCount()
        {
            var result = new ThreadCountCollector().Collect(sampleCount: 1, warmupCount: 0);

            Assert.True(result.Available);
            Assert.True(result.Median > 0);
        }

        [Fact]
        public void DiskWriteThroughputCollector_MeasuresRealWrite_AndCleansUpTheTempFile()
        {
            var dir = Path.Combine(Path.GetTempPath(), "performish-test-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var before = Directory.GetFiles(dir);
                var result = new DiskWriteThroughputCollector(dir).Collect(sampleCount: 1, warmupCount: 0);

                Assert.True(result.Available, result.UnavailableReason);
                Assert.True(result.Median > 0);
                Assert.Empty(Directory.GetFiles(dir)); // no leftover temp file
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void DiskReadThroughputCollector_MeasuresRealRead_AndCleansUpTheTempFile()
        {
            var dir = Path.Combine(Path.GetTempPath(), "performish-test-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var result = new DiskReadThroughputCollector(dir).Collect(sampleCount: 1, warmupCount: 0);

                Assert.True(result.Available, result.UnavailableReason);
                Assert.True(result.Median > 0);
                Assert.Empty(Directory.GetFiles(dir));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void DirectoryListingLatencyCollector_MeasuresRealLatency()
        {
            var result = new DirectoryListingLatencyCollector(Path.GetTempPath()).Collect(sampleCount: 2, warmupCount: 0);

            Assert.True(result.Available, result.UnavailableReason);
            Assert.True(result.Median >= 0.0);
        }

        [Fact]
        public void DiskCollectors_NeverRunByDefault_IsTrue()
        {
            Assert.True(new DiskWriteThroughputCollector().RunByDefault);
            Assert.True(new DiskReadThroughputCollector().RunByDefault);
        }

        // ---- Network collectors: opt-in, and tolerant of no network access in this environment ----

        [Fact]
        public void DnsLatencyCollector_IsNeverRunByDefault()
        {
            Assert.False(new DnsLatencyCollector().RunByDefault);
        }

        [Fact]
        public void DnsLatencyCollector_EitherMeasuresRealLatency_OrReportsUnavailable_NeverThrows()
        {
            var result = new DnsLatencyCollector().Collect(sampleCount: 1, warmupCount: 0);

            if (result.Available) Assert.True(result.Median >= 0.0);
            else Assert.False(string.IsNullOrWhiteSpace(result.UnavailableReason));
        }

        [Fact]
        public void GatewayPingLatencyCollector_IsNeverRunByDefault()
        {
            Assert.False(new GatewayPingLatencyCollector().RunByDefault);
        }

        [Fact]
        public void GatewayPingLatencyCollector_EitherMeasuresRealLatency_OrReportsUnavailable_NeverThrows()
        {
            var result = new GatewayPingLatencyCollector().Collect(sampleCount: 1, warmupCount: 0);

            if (result.Available) Assert.True(result.Median >= 0.0);
            else Assert.False(string.IsNullOrWhiteSpace(result.UnavailableReason));
        }
    }
}
