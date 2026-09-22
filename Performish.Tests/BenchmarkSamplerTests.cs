using System;
using System.Collections.Generic;
using Performish.Core.Benchmark;
using Xunit;

namespace Performish.Tests
{
    public class BenchmarkSamplerTests
    {
        [Fact]
        public void Sample_ReturnsMedianOfCollectedSamples()
        {
            var values = new Queue<double>(new[] { 10.0, 20.0, 30.0 }); // odd count -> exact median
            var result = BenchmarkSampler.Sample("m", "u", MetricCategory.System, () => values.Dequeue(), sampleCount: 3, warmupCount: 0);

            Assert.True(result.Available);
            Assert.Equal(20.0, result.Median);
            Assert.Equal(10.0, result.Min);
            Assert.Equal(30.0, result.Max);
        }

        [Fact]
        public void Sample_DiscardsWarmupReadings_NeverIncludesThemInTheResult()
        {
            // First 2 calls are warm-up (999 each, would badly skew the result if counted); real
            // samples are a stable 5.
            var values = new Queue<double>(new[] { 999.0, 999.0, 5.0, 5.0, 5.0 });
            var result = BenchmarkSampler.Sample("m", "u", MetricCategory.System, () => values.Dequeue(), sampleCount: 3, warmupCount: 2);

            Assert.Equal(5.0, result.Median);
            Assert.Equal(5.0, result.Max);
            Assert.Equal(3, result.Samples.Count);
        }

        [Fact]
        public void Sample_StableValues_IsFlaggedReliable()
        {
            var values = new Queue<double>(new[] { 100.0, 101.0, 99.0, 100.0, 100.0 });
            var result = BenchmarkSampler.Sample("m", "u", MetricCategory.System, () => values.Dequeue(), sampleCount: 5, warmupCount: 0);

            Assert.True(result.IsReliable);
        }

        [Fact]
        public void Sample_HighlyVariableValues_IsFlaggedUnreliable()
        {
            var values = new Queue<double>(new[] { 1.0, 500.0, 2.0, 600.0, 3.0 });
            var result = BenchmarkSampler.Sample("m", "u", MetricCategory.System, () => values.Dequeue(), sampleCount: 5, warmupCount: 0);

            Assert.False(result.IsReliable);
        }

        [Fact]
        public void Sample_MeasurementThrows_ReturnsUnavailable_NeverThrowsPastTheBoundary()
        {
            var result = BenchmarkSampler.Sample("m", "u", MetricCategory.System,
                () => throw new InvalidOperationException("permission denied"), sampleCount: 3, warmupCount: 0);

            Assert.False(result.Available);
            Assert.Contains("permission denied", result.UnavailableReason);
            Assert.False(result.IsReliable);
            Assert.Empty(result.Samples);
        }

        [Fact]
        public void Sample_ThrowsDuringWarmup_AlsoReturnsUnavailable()
        {
            var calls = 0;
            var result = BenchmarkSampler.Sample("m", "u", MetricCategory.System, () =>
            {
                calls++;
                if (calls == 1) throw new InvalidOperationException("boom during warmup");
                return 1.0;
            }, sampleCount: 3, warmupCount: 1);

            Assert.False(result.Available);
        }

        [Fact]
        public void Sample_ZeroInvalidSampleCount_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                BenchmarkSampler.Sample("m", "u", MetricCategory.System, () => 1.0, sampleCount: 0, warmupCount: 0));
        }

        [Fact]
        public void Sample_NullMeasureDelegate_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                BenchmarkSampler.Sample("m", "u", MetricCategory.System, null, sampleCount: 1, warmupCount: 0));
        }

        [Fact]
        public void Sample_EvenCount_MedianIsAverageOfMiddleTwo()
        {
            var values = new Queue<double>(new[] { 10.0, 20.0, 30.0, 40.0 });
            var result = BenchmarkSampler.Sample("m", "u", MetricCategory.System, () => values.Dequeue(), sampleCount: 4, warmupCount: 0);

            Assert.Equal(25.0, result.Median);
        }

        [Fact]
        public void Sample_SingleSample_HasZeroStdDevAndIsReliable()
        {
            var result = BenchmarkSampler.Sample("m", "u", MetricCategory.System, () => 42.0, sampleCount: 1, warmupCount: 0);

            Assert.Equal(0.0, result.StdDev);
            Assert.True(result.IsReliable);
            Assert.Equal(42.0, result.Median);
        }

        [Fact]
        public void Sample_AllZeroValues_DoesNotDivideByZero_IsReliable()
        {
            var result = BenchmarkSampler.Sample("m", "u", MetricCategory.System, () => 0.0, sampleCount: 5, warmupCount: 0);

            Assert.True(result.Available);
            Assert.Equal(0.0, result.Median);
            Assert.True(result.IsReliable);
        }

        [Fact]
        public void Summarize_EmptySamples_ReturnsUnavailable()
        {
            var result = BenchmarkSampler.Summarize("m", "u", MetricCategory.System, Array.Empty<double>());

            Assert.False(result.Available);
        }

        [Fact]
        public void Unavailable_FactoryMethod_SetsExpectedFields()
        {
            var result = MetricSampleResult.Unavailable("m", "u", MetricCategory.Gaming, "no capture imported");

            Assert.False(result.Available);
            Assert.False(result.IsReliable);
            Assert.Equal("no capture imported", result.UnavailableReason);
            Assert.Equal(MetricCategory.Gaming, result.Category);
        }
    }
}
