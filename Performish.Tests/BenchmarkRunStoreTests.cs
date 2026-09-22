using System;
using System.IO;
using System.Linq;
using Performish.Core.Benchmark;
using Xunit;

namespace Performish.Tests
{
    public class BenchmarkRunStoreTests
    {
        private static BenchmarkRun MakeRun(DateTime timestampUtc, string label) => new BenchmarkRun
        {
            TimestampUtc = timestampUtc,
            Label = label,
            Kind = BenchmarkRunKind.Standalone,
            HealthScore = 74,
            Metrics = { new MetricSampleResult { Name = "CPU idle", Unit = "%", Category = MetricCategory.System, Available = true, Median = 55, Samples = { 54, 55, 56 } } }
        };

        [Fact]
        public void InMemoryStore_SaveThenReadAll_RoundTrips()
        {
            var store = new InMemoryBenchmarkRunStore();
            var run = MakeRun(DateTime.UtcNow, "test run");

            store.Save(run);
            var all = store.ReadAll();

            Assert.Single(all);
            Assert.Equal(run.Id, all[0].Id);
        }

        [Fact]
        public void InMemoryStore_ReadRecent_NewestFirst_RespectsLimit()
        {
            var store = new InMemoryBenchmarkRunStore();
            store.Save(MakeRun(DateTime.UtcNow.AddHours(-2), "oldest"));
            store.Save(MakeRun(DateTime.UtcNow.AddHours(-1), "middle"));
            store.Save(MakeRun(DateTime.UtcNow, "newest"));

            var recent = store.ReadRecent(2);

            Assert.Equal(2, recent.Count);
            Assert.Equal("newest", recent[0].Label);
            Assert.Equal("middle", recent[1].Label);
        }

        [Fact]
        public void InMemoryStore_Find_ReturnsMatchingRun_OrNull()
        {
            var store = new InMemoryBenchmarkRunStore();
            var run = MakeRun(DateTime.UtcNow, "findme");
            store.Save(run);

            Assert.Equal(run.Id, store.Find(run.Id).Id);
            Assert.Null(store.Find("not-a-real-id"));
        }

        [Fact]
        public void FileStore_SaveThenReadAll_RoundTripsFullMetricData()
        {
            var dir = Path.Combine(Path.GetTempPath(), "performish-bench-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new FileBenchmarkRunStore(dir);
                var run = MakeRun(DateTime.UtcNow, "on-disk run");
                run.TweakIds.Add("debloat.cortana");

                store.Save(run);
                var all = store.ReadAll();

                Assert.Single(all);
                Assert.Equal("on-disk run", all[0].Label);
                Assert.Equal(74, all[0].HealthScore);
                Assert.Single(all[0].TweakIds);
                Assert.Equal("debloat.cortana", all[0].TweakIds[0]);
                Assert.Single(all[0].Metrics);
                Assert.Equal("CPU idle", all[0].Metrics[0].Name);
                Assert.Equal(55, all[0].Metrics[0].Median);
                Assert.Equal(3, all[0].Metrics[0].Samples.Count); // full sample set survives the round trip, not just the median
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void FileStore_ReadRecent_AcrossMultipleMonths_StopsAtLimit()
        {
            var dir = Path.Combine(Path.GetTempPath(), "performish-bench-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new FileBenchmarkRunStore(dir);
                store.Save(MakeRun(new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc), "january"));
                store.Save(MakeRun(new DateTime(2025, 2, 15, 0, 0, 0, DateTimeKind.Utc), "february"));
                store.Save(MakeRun(new DateTime(2025, 3, 15, 0, 0, 0, DateTimeKind.Utc), "march"));

                var recent = store.ReadRecent(2);

                Assert.Equal(2, recent.Count);
                Assert.Equal("march", recent[0].Label);
                Assert.Equal("february", recent[1].Label);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void FileStore_CorruptLine_IsSkipped_DoesNotFailTheWholeRead()
        {
            var dir = Path.Combine(Path.GetTempPath(), "performish-bench-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new FileBenchmarkRunStore(dir);
                var good = MakeRun(DateTime.UtcNow, "good");
                store.Save(good);
                File.AppendAllText(Path.Combine(dir, $"benchmark-{DateTime.UtcNow:yyyy-MM}.jsonl"), "{not valid json" + Environment.NewLine);

                var all = store.ReadAll();

                Assert.Single(all);
                Assert.Equal("good", all[0].Label);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void FileStore_EmptyDirectory_ReturnsEmptyList_NoException()
        {
            var dir = Path.Combine(Path.GetTempPath(), "performish-bench-test-" + Guid.NewGuid().ToString("N"));
            var store = new FileBenchmarkRunStore(dir);

            Assert.Empty(store.ReadAll());
            Assert.Empty(store.ReadRecent(10));

            Directory.Delete(dir, recursive: true);
        }
    }
}
