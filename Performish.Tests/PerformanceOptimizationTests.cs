using System.Collections.Generic;
using System.Linq;
using Performish.Core.Backends;
using Performish.Core.Backup;
using Performish.Core.Models;
using Performish.Core.Scanner;
using Xunit;

namespace Performish.Tests
{
    /// <summary>Locks in the Phase 5 optimizations so a future change can't silently regress them -
    /// still entirely against Fake* backends (RealAppxBackend/RealPowerBackend here are exercised
    /// with a FakeProcessRunner standing in for powershell.exe/powercfg.exe, so no real process is
    /// ever spawned, per the hard safety rule; only the *call count* to the fake is asserted).</summary>
    public class PerformanceOptimizationTests
    {
        [Fact]
        public void RealAppxBackend_MultipleReads_OnlySpawnsPowerShellOnce()
        {
            var process = new FakeProcessRunner();
            process.Script("Get-AppxPackage", new ProcessRunResult { ExitCode = 0, StandardOutput = "Microsoft.BingNews\nMicrosoft.BingWeather\n" });
            process.Script("Get-AppxProvisionedPackage", new ProcessRunResult { ExitCode = 0, StandardOutput = "Microsoft.BingNews\n" });

            var backend = new RealAppxBackend(process);

            backend.Read("Microsoft.BingNews");
            backend.Read("Microsoft.BingWeather");
            backend.Read("Microsoft.GetHelp");

            // 2 calls total (one Get-AppxPackage, one Get-AppxProvisionedPackage) for 3 Read()s -
            // the old implementation spawned 2 PER Read(), i.e. 6 here.
            Assert.Equal(2, process.Calls.Count);
        }

        [Fact]
        public void RealAppxBackend_ReadReflectsCache_PrefixMatchedCorrectly()
        {
            var process = new FakeProcessRunner();
            process.Script("Get-AppxPackage", new ProcessRunResult { ExitCode = 0, StandardOutput = "Microsoft.BingNews_8wekyb3d8bbwe\n" });
            process.Script("Get-AppxProvisionedPackage", new ProcessRunResult { ExitCode = 0, StandardOutput = "" });

            var backend = new RealAppxBackend(process);
            var snap = backend.Read("Microsoft.BingNews");

            Assert.True(snap.InstalledForCurrentUser);
            Assert.False(snap.Provisioned);
        }

        [Fact]
        public void RealAppxBackend_WriteInvalidatesCache_NextReadReSpawns()
        {
            var process = new FakeProcessRunner();
            process.Script("Get-AppxPackage", new ProcessRunResult { ExitCode = 0, StandardOutput = "Microsoft.BingNews\n" });
            process.Script("Get-AppxProvisionedPackage", new ProcessRunResult { ExitCode = 0, StandardOutput = "" });
            process.Script("Remove-AppxPackage", new ProcessRunResult { ExitCode = 0 });

            var backend = new RealAppxBackend(process);
            backend.Read("Microsoft.BingNews");
            var callsAfterFirstRead = process.Calls.Count;

            backend.RemoveForCurrentUser("Microsoft.BingNews");
            backend.Read("Microsoft.BingNews"); // must re-query, not serve the stale cached "installed" answer

            Assert.True(process.Calls.Count > callsAfterFirstRead + 1); // +1 for the Remove call, plus a fresh re-cache
        }

        [Fact]
        public void RealPowerBackend_MultipleListPlansCalls_OnlySpawnsPowercfgOnce()
        {
            var process = new FakeProcessRunner();
            process.Script("/list", new ProcessRunResult
            {
                ExitCode = 0,
                StandardOutput = "Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced) *\n"
            });

            var backend = new RealPowerBackend(process);
            backend.ListPlans();
            backend.GetActivePlanGuid();
            backend.ListPlans();

            Assert.Single(process.Calls);
        }

        [Fact]
        public void RealPowerBackend_SetActivePlan_InvalidatesCache()
        {
            var process = new FakeProcessRunner();
            process.Script("/list", new ProcessRunResult
            {
                ExitCode = 0,
                StandardOutput = "Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced) *\n"
            });

            var backend = new RealPowerBackend(process);
            backend.ListPlans();
            backend.SetActivePlan("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
            backend.ListPlans();

            // /list called twice (once before, once after the cache-invalidating SetActivePlan) plus
            // one /setactive call.
            Assert.Equal(3, process.Calls.Count);
        }

        [Fact]
        public void SystemScanner_ParallelizedScan_StillProducesCorrectSnapshot()
        {
            var info = new FakeSystemInfoBackend
            {
                BuildNumber = 26100,
                CpuName = "Test CPU",
                CpuCores = 6,
                Services = Enumerable.Range(0, 50).Select(i => new ServiceInfo { Name = "s" + i, Running = i % 2 == 0 }).ToList()
            };
            var power = new FakePowerBackend();
            var scanner = new SystemScanner(info, power);

            // Run several times - a parallelized scan with a shared-state bug would show up as
            // flaky/incorrect results across repeated runs, not necessarily the first one.
            for (var i = 0; i < 10; i++)
            {
                var snapshot = scanner.Scan();
                Assert.True(snapshot.IsWindows11);
                Assert.Equal("Test CPU", snapshot.CpuName);
                Assert.Equal(6, snapshot.CpuCoreCount);
                Assert.Equal(50, snapshot.Services.Count);
                Assert.Equal(25, snapshot.Services.Count(s => s.Running));
                Assert.Equal("Balanced", snapshot.ActivePowerPlanName);
            }
        }

        [Fact]
        public void ChangeLog_ReadRecent_ReturnsNewestFirst_BoundedCount()
        {
            var store = new InMemoryChangeLogStore();
            var baseTime = System.DateTime.UtcNow;
            for (var i = 0; i < 20; i++)
            {
                store.Append(new ChangeLogEntry
                {
                    TimestampUtc = baseTime.AddMinutes(i),
                    TweakId = "t" + i,
                    Action = ChangeLogAction.Apply,
                    Outcome = OperationOutcome.Success
                });
            }

            var recent = store.ReadRecent(5);

            Assert.Equal(5, recent.Count);
            Assert.Equal("t19", recent[0].TweakId); // newest first
            Assert.Equal("t15", recent[4].TweakId);
        }

        [Fact]
        public void ChangeLog_ReadRecent_FewerEntriesThanRequested_ReturnsAllOfThem()
        {
            var store = new InMemoryChangeLogStore();
            store.Append(new ChangeLogEntry { TimestampUtc = System.DateTime.UtcNow, TweakId = "only", Action = ChangeLogAction.Apply, Outcome = OperationOutcome.Success });

            var recent = store.ReadRecent(500);

            Assert.Single(recent);
        }
    }
}
