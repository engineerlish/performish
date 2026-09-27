using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Performish.Core;
using Performish.Core.Hardware;
using Performish.Hardware;
using Performish.Views;
using Xunit;

namespace Performish.Tests
{
    /// <summary>The Hardware & firmware dialog and its Diagnostics button, against simulated hardware
    /// (FakeHardwareInfoBackend via AppServices.BuildFake) - no real hardware is ever read under test.</summary>
    public class HardwareDialogUiTests
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

        private static HardwareInfoService Service(HardwareRawSnapshot raw) =>
            new HardwareInfoService(new FakeHardwareInfoBackend(raw), () => HardwareFixtures.Now);

        private static HardwareReport Report(HardwareRawSnapshot raw) => Service(raw).ReadReport();

        private static MainForm Start()
        {
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            var settings = new AppSettings { DryRunByDefault = true, BenchmarkModeDefault = BenchmarkMode.Off };
            var form = new MainForm(AppServices.BuildFake(HardwareFixtures.Desktop()), settings)
            {
                StartPosition = FormStartPosition.Manual,
                Location = new System.Drawing.Point(10, 10)
            };
            form.Show();
            Pump(() => Find<Button>(form, b => b.Text == "Browse tweaks...")?.Enabled == true);
            return form;
        }

        [StaFact]
        public void HomeScreen_HasHardwareButtonUnderDiagnostics_AndEveryExistingControl()
        {
            using var form = Start();

            var hardware = Find<Button>(form, b => b.Text == "Hardware & firmware...");
            Assert.NotNull(hardware);
            Assert.True(hardware.Enabled);
            Assert.Contains(hardware, Find<Button>(form, b => b.Text == "Health score...").Parent.Controls.Cast<Control>());

            foreach (var text in new[] { "Scan", "Browse tweaks...", "Presets...", "Revert everything...", "Health score...", "Startup items...",
                "Check for drift...", "Run benchmark now", "Benchmark history...", "Import frame-time CSV...", "History...", "Export report...", "Settings..." })
                Assert.NotNull(Find<Button>(form, b => b.Text == text));
            foreach (var nav in new[] { "Home", "Tweaks", "Results" })
                Assert.NotNull(Find<Button>(form, b => b.Text == nav));
        }

        [StaFact]
        public void ExistingFlows_StillWork()
        {
            using var form = Start();
            Find<Button>(form, b => b.Text == "Browse tweaks...").PerformClick();
            Assert.True(Find<TweaksView>(form).Visible);
            Find<Button>(form, b => b.Text == "Home").PerformClick();
            Assert.True(Find<HomeView>(form).Visible);
        }

        [StaFact]
        public void Dialog_LoadsOnShow_OffTheUiThread()
        {
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            using var dialog = new HardwareDialogForm(Service(HardwareFixtures.Desktop()));
            dialog.Show();
            Pump(() => dialog.Report != null);
            Assert.Equal(5, dialog.ComponentRows.Count);
        }

        [StaFact]
        public void Dialog_ShowsFiveComponents_AndTheirReadings()
        {
            using var dialog = new HardwareDialogForm(Service(HardwareFixtures.Desktop()));
            dialog.ShowReport(Report(HardwareFixtures.Desktop()));
            dialog.Show();

            Assert.Equal(5, dialog.ComponentRows.Count);
            Assert.Contains("of", dialog.SummaryText);
            Assert.False(dialog.WakeGpuButtonVisible);

            dialog.SelectComponent(dialog.ComponentRows.ToList().FindIndex(r => r.StartsWith("CPU")));
            Assert.Contains(dialog.ReadingRows, r => r.StartsWith("Current clock (right now) | 4750 MHz"));
            Assert.Contains(dialog.ReadingRows, r => r == "Core voltage | not available | ");
        }

        [StaFact]
        public void Dialog_AllMissing_ShowsNotAvailable_WithReasons()
        {
            using var dialog = new HardwareDialogForm(Service(HardwareFixtures.Empty()));
            dialog.ShowReport(Report(HardwareFixtures.Empty()));
            Assert.All(dialog.ReadingRows, r => Assert.Contains("not available", r));
            Assert.Contains("rather than a guess", dialog.DetailText);
        }

        [StaFact]
        public void Dialog_LaptopMemoryAtCpuLimit_AndSleepingGpu()
        {
            using var dialog = new HardwareDialogForm(Service(HardwareFixtures.XpsLaptop()));
            dialog.ShowReport(Report(HardwareFixtures.XpsLaptop()));
            dialog.Show(); // a child control only reports Visible once its form is shown (same as HealthScoreFormTests)

            Assert.True(dialog.WakeGpuButtonVisible);
            dialog.SelectComponent(dialog.ComponentRows.ToList().FindIndex(r => r.StartsWith("Memory")));
            Assert.Contains(dialog.ReadingRows, r => r.StartsWith("XMP / EXPO profile | n/a"));
            Assert.Contains("[OK] Memory is at the CPU's supported maximum speed", dialog.FindingsText);
        }

        [StaFact]
        public void Dialog_WakeGpu_RereadsWithTheWakeOption()
        {
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            var fake = new FakeHardwareInfoBackend(HardwareFixtures.XpsLaptop());
            using var dialog = new HardwareDialogForm(new HardwareInfoService(fake, () => HardwareFixtures.Now));
            dialog.Show();
            Pump(() => dialog.Report != null);
            var first = dialog.Report;
            Assert.False(fake.Calls.Last().WakeDiscreteGpu);

            // A real click arrives through the message loop with WinForms' context installed; a click from
            // test code between DoEvents pumps doesn't have it, so install it the same way Start() does.
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            dialog.ClickWakeGpu();
            Pump(() => dialog.Report != first); // the reread has fully finished, not just started
            Assert.Equal(2, fake.Calls.Count);
            Assert.True(fake.Calls.Last().WakeDiscreteGpu);
        }

        [StaFact]
        public void Dialog_ClosedWhileReading_DoesNotThrow()
        {
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            var gate = new ManualResetEventSlim(false);
            var backend = new GatedBackend(HardwareFixtures.Desktop(), gate);
            var dialog = new HardwareDialogForm(new HardwareInfoService(backend, () => HardwareFixtures.Now));
            dialog.Show();
            Pump(() => backend.Started);
            dialog.Close();
            dialog.Dispose();
            gate.Set(); // the read completes after the dialog is gone
            var end = DateTime.UtcNow.AddMilliseconds(500);
            while (DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(5); }
            Assert.True(dialog.IsDisposed);
            Assert.Null(dialog.Report);
        }

        private sealed class GatedBackend : IHardwareInfoBackend
        {
            private readonly HardwareRawSnapshot _raw;
            private readonly ManualResetEventSlim _gate;
            public volatile bool Started;
            public GatedBackend(HardwareRawSnapshot raw, ManualResetEventSlim gate) { _raw = raw; _gate = gate; }
            public HardwareRawSnapshot Read(HardwareReadOptions options) { Started = true; _gate.Wait(5000); return _raw; }
        }
    }
}
