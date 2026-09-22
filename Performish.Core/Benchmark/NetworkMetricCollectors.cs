using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;

namespace Performish.Core.Benchmark
{
    /// <summary>Network metrics - both are RunByDefault = false (see BENCHMARK_PLAN.md "scope"): real
    /// network activity, so only ever run when the user explicitly opts in, never as part of the
    /// default before/after suite for an unrelated tweak batch.</summary>
    public sealed class DnsLatencyCollector : IMetricCollector
    {
        public string Name => "DNS resolution latency";
        public string Unit => "ms";
        public MetricCategory Category => MetricCategory.Network;
        public bool RunByDefault => false;

        // Fixed, stable, well-known hostnames - not a specific resolver (the system's configured
        // resolver answers whichever is asked) - see BENCHMARK_PLAN.md's "deliberate simplification".
        private static readonly string[] DefaultHostnames = { "www.microsoft.com", "www.cloudflare.com" };

        private readonly string[] _hostnames;

        public DnsLatencyCollector(string[] hostnames = null) => _hostnames = hostnames is { Length: > 0 } ? hostnames : DefaultHostnames;

        public MetricSampleResult Collect(int sampleCount, int warmupCount) =>
            BenchmarkSampler.Sample(Name, Unit, Category, MeasureOnce, sampleCount, warmupCount);

        private double MeasureOnce()
        {
            var hostname = _hostnames[Random.Shared.Next(_hostnames.Length)];
            var sw = Stopwatch.StartNew();
            Dns.GetHostEntry(hostname);
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds;
        }
    }

    public sealed class GatewayPingLatencyCollector : IMetricCollector
    {
        public string Name => "Gateway ping latency";
        public string Unit => "ms";
        public MetricCategory Category => MetricCategory.Network;
        public bool RunByDefault => false;

        private const int TimeoutMs = 1000;

        public MetricSampleResult Collect(int sampleCount, int warmupCount)
        {
            var gateway = FindDefaultGateway();
            if (gateway == null)
                return MetricSampleResult.Unavailable(Name, Unit, Category, "No default gateway found on any active network adapter.");

            return BenchmarkSampler.Sample(Name, Unit, Category, () => MeasureOnce(gateway), sampleCount, warmupCount);
        }

        private static double MeasureOnce(IPAddress gateway)
        {
            using var ping = new Ping();
            var reply = ping.Send(gateway, TimeoutMs);
            if (reply.Status != IPStatus.Success)
                throw new InvalidOperationException($"Ping to gateway failed: {reply.Status}.");
            return reply.RoundtripTime;
        }

        internal static IPAddress FindDefaultGateway() =>
            NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
                .SelectMany(ni => ni.GetIPProperties().GatewayAddresses)
                .Select(g => g.Address)
                .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
    }
}
