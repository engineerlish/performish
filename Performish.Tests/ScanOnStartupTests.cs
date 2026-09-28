using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using Performish.Core;
using Performish.Core.Scanner;
using Performish.Views;
using Xunit;

namespace Performish.Tests
{
    /// <summary>MainForm's "scan automatically on startup" behavior, driven the same way
    /// MainFormFlowTests drives the whole window (real Show(), WindowsFormsSynchronizationContext,
    /// pumped waits) - the startup scan goes through RunScanAsync()'s real Task.Run offload, which
    /// needs a real message loop to marshal back onto, same as every other async flow here. Never
    /// touches AppServices.BuildReal() or a real machine.</summary>
    public class ScanOnStartupTests
    {
        private static T Find<T>(Control root, Func<T, bool> pred = null) where T : Control
        {
            foreach (Control c in root.Controls)
            {
                if (c is T t && (pred == null || pred(t))) return t;
                var inner = Find(c, pred);
                if (inner != null) return inner;
            }
            return null;
        }

        private static void Pump(Func<bool> until, int timeoutMs = 20000)
        {
            var end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!until() && DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(5); }
            Application.DoEvents();
            Assert.True(until(), "timed out waiting for the UI");
        }

        private static MainForm Start(AppServices services, AppSettings settings)
        {
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            var form = new MainForm(services, settings) { StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(10, 10) };
            form.Show();
            return form;
        }

        private static HomeView Home(MainForm form) => Find<HomeView>(form);
        private static RichTextBox Console(MainForm form) => Home(form).Console;

        /// <summary>Every ISystemInfoBackend method throws - simulates a scan that fails outright, the
        /// same way a real scan can fail (WMI unavailable, permissions, etc).</summary>
        private sealed class ThrowingSystemInfoBackend : ISystemInfoBackend
        {
            private static Exception Fail() => new InvalidOperationException("Simulated scan failure");
            public (string ProductName, string EditionId, int BuildNumber, int Ubr) GetOsInfo() => throw Fail();
            public bool IsDomainJoined() => throw Fail();
            public bool IsMdmManaged() => throw Fail();
            public (string Name, int Cores) GetCpuInfo() => throw Fail();
            public int GetCpuLogicalProcessorCount() => throw Fail();
            public List<GpuInfo> GetGpus() => throw Fail();
            public (long TotalBytes, long AvailableBytes) GetMemory() => throw Fail();
            public bool SystemDriveIsSsd() => throw Fail();
            public bool IsLaptop() => throw Fail();
            public List<StartupItemInfo> GetStartupItems() => throw Fail();
            public List<ScheduledTaskInfo> GetScheduledTasks() => throw Fail();
            public List<ServiceInfo> GetServices() => throw Fail();
            public int GetProcessCount() => throw Fail();
            public (long FreeBytes, long TotalBytes) GetSystemDriveSpace() => throw Fail();
            public double GetUptimeHours() => throw Fail();
            public List<string> GetRunningProcessNames() => throw Fail();
        }

        [Fact]
        public void DefaultsToOff()
        {
            Assert.False(new AppSettings().ScanOnStartup);
        }

        [StaFact]
        public void Enabled_RunsAScanBeforeTheUserClicksAnything()
        {
            var services = AppServices.BuildFake();
            var settings = new AppSettings { ScanOnStartup = true, BenchmarkModeDefault = BenchmarkMode.Off };
            using var form = Start(services, settings);

            Pump(() => Console(form).Text.Contains("Scan complete."));

            var home = Home(form);
            Assert.False(home.ShowsEmptyNote);
            Assert.False(string.IsNullOrWhiteSpace(home.RowText("cpu")));
        }

        [StaFact]
        public void Disabled_NeverScans_HomeStaysAtNoScanYet()
        {
            var services = AppServices.BuildFake();
            var settings = new AppSettings { ScanOnStartup = false, BenchmarkModeDefault = BenchmarkMode.Off };
            using var form = Start(services, settings);

            Pump(() => Find<Button>(form, b => b.Text == "Scan")?.Enabled == true);

            Assert.True(Home(form).ShowsEmptyNote);
            Assert.True(string.IsNullOrEmpty(Home(form).RowText("cpu")));
            Assert.DoesNotContain("Scan complete.", Console(form).Text);
        }

        [StaFact]
        public void FailedStartupScan_IsSurfaced_NeverCrashesStartupOrSwallowsIt()
        {
            var services = AppServices.BuildFake(systemInfo: new ThrowingSystemInfoBackend());
            var settings = new AppSettings { ScanOnStartup = true, BenchmarkModeDefault = BenchmarkMode.Off };
            using var form = Start(services, settings);

            Pump(() => Console(form).Text.Contains("Scan failed"));

            // Same wording a manual Scan-button failure would show; no scan data was faked into Home.
            Assert.True(Home(form).ShowsEmptyNote);
            Assert.True(string.IsNullOrEmpty(Home(form).RowText("cpu")));
            Assert.True(Find<Button>(form, b => b.Text == "Scan").Enabled); // startup didn't get stuck busy
        }
    }
}
