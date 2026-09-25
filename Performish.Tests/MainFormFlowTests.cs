using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Performish.Core;
using Performish.Core.Models;
using Performish.Views;
using Xunit;

namespace Performish.Tests
{
    /// <summary>Drives the whole main window against AppServices.BuildFake() (via the injected-services
    /// constructor, so AppServices.BuildReal() is never called): browse, review, in-window confirmation,
    /// apply, in-window results. "Real" apply here only ever changes the in-memory fakes.</summary>
    public class MainFormFlowTests
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

        // Forms open before this test started (other tests may leak theirs) - popups are counted relative to it.
        private static System.Collections.Generic.HashSet<Form> _formsBefore = new System.Collections.Generic.HashSet<Form>();

        private static int PopupCount(Form main) =>
            Application.OpenForms.OfType<Form>().Count(f => f.Visible && f != main && !_formsBefore.Contains(f));

        private static MainForm Start(AppServices services, bool dryRunByDefault)
        {
            _formsBefore = Application.OpenForms.OfType<Form>().ToHashSet();
            // Application.Run installs this in the shipped app; the test runner's own context does not
            // pump Win32 messages, and the window's async flows resume through the ambient context.
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            var settings = new AppSettings { DryRunByDefault = dryRunByDefault, BenchmarkModeDefault = BenchmarkMode.Off };
            var form = new MainForm(services, settings) { StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(10, 10) };
            form.Show();
            Pump(() => Find<Button>(form, b => b.Text == "Browse tweaks...")?.Enabled == true);
            return form;
        }

        [StaFact]
        public void BrowseTweaks_SwitchesTheContentToTheTweaksView_InsideTheSameWindow()
        {
            var services = AppServices.BuildFake();
            using var form = Start(services, dryRunByDefault: true);
            var home = Find<HomeView>(form);
            var tweaks = Find<TweaksView>(form);
            Assert.True(home.Visible);
            Assert.False(tweaks.Visible);

            Find<Button>(form, b => b.Text == "Browse tweaks...").PerformClick();

            Assert.True(tweaks.Visible);
            Assert.False(home.Visible);
            Assert.Equal(0, PopupCount(form)); // no popup window opened
        }

        [StaFact]
        public void ReviewAndApply_ConfirmsInWindow_ThenShowsResultsInWindow_AndRefreshesTweakStates()
        {
            var services = AppServices.BuildFake();
            using var form = Start(services, dryRunByDefault: false);
            Find<Button>(form, b => b.Text == "Browse tweaks...").PerformClick();
            var tweaks = Find<TweaksView>(form);
            var overlay = Find<ConfirmOverlay>(form);
            var results = Find<ResultsView>(form);

            tweaks.ClickPreset(Preset.Conservative);
            tweaks.ReviewButton.PerformClick();
            Pump(() => overlay.IsOpen);

            Assert.Contains("for real", overlay.TitleText);
            Assert.Contains("REAL", overlay.BodyText);
            Assert.Contains("Restore point", overlay.BodyText, StringComparison.OrdinalIgnoreCase);
            Assert.True(overlay.CancelActionButton.Focused);
            Assert.Equal(0, PopupCount(form)); // the confirmation is a card in this window, not a window
            Assert.False(results.IsShowingResults);

            overlay.ConfirmForTesting();
            Pump(() => results.IsShowingResults);

            Assert.False(overlay.IsOpen);
            Assert.True(results.Visible);
            Assert.False(tweaks.Visible);
            Assert.Equal(services.TweakRegistry.ByPreset(Preset.Conservative).Count(), results.Result.Results.Count);
            Assert.Equal(0, results.FailedTile.Count);
            Assert.True(results.GoodTile.Count > 0);
            Assert.Contains(services.ChangeLog.ReadAll(), e => !e.DryRun && e.Outcome == OperationOutcome.Success);
        }

        [StaFact]
        public void ReviewAndApply_Cancel_ChangesNothing_AndLeavesTheTweaksViewInPlace()
        {
            var services = AppServices.BuildFake();
            using var form = Start(services, dryRunByDefault: false);
            Find<Button>(form, b => b.Text == "Browse tweaks...").PerformClick();
            var tweaks = Find<TweaksView>(form);
            var overlay = Find<ConfirmOverlay>(form);
            var results = Find<ResultsView>(form);
            tweaks.ClickPreset(Preset.Conservative);

            tweaks.ReviewButton.PerformClick();
            Pump(() => overlay.IsOpen);
            overlay.CancelForTesting();
            Pump(() => !overlay.IsOpen);

            Assert.True(tweaks.Visible);
            Assert.False(results.IsShowingResults);
            Assert.Empty(services.ChangeLog.ReadAll());
        }

        [StaFact]
        public void DryRun_ShowsTheDryRunWording_AndNeverChangesState()
        {
            var services = AppServices.BuildFake();
            using var form = Start(services, dryRunByDefault: true);
            Find<Button>(form, b => b.Text == "Browse tweaks...").PerformClick();
            var tweaks = Find<TweaksView>(form);
            var overlay = Find<ConfirmOverlay>(form);
            var results = Find<ResultsView>(form);
            tweaks.ClickPreset(Preset.Conservative);

            tweaks.ReviewButton.PerformClick();
            Pump(() => overlay.IsOpen);
            Assert.Contains("dry run", overlay.TitleText.ToLowerInvariant());
            Assert.Contains("DRY RUN", overlay.BodyText);
            Assert.Equal("Run dry run", overlay.ConfirmButton.Text);

            overlay.ConfirmForTesting();
            Pump(() => results.IsShowingResults);

            Assert.Equal("Dry run complete", results.HeaderText);
            Assert.All(results.Result.Results, r => Assert.True(r.Result.WasDryRun));
            Assert.All(services.ChangeLog.ReadAll(), e => Assert.True(e.DryRun)); // nothing recorded as really applied
        }

        [StaFact]
        public void FunctionKeys_SwitchScreens()
        {
            var services = AppServices.BuildFake();
            using var form = Start(services, dryRunByDefault: true);
            var msg = new Message();
            bool Press(Keys k) => (bool)typeof(Form).GetMethod("ProcessCmdKey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(form, new object[] { msg, k });

            Assert.True(Press(Keys.F2));
            Assert.True(Find<TweaksView>(form).Visible);
            Assert.True(Press(Keys.F3));
            Assert.True(Find<ResultsView>(form).Visible);
            Assert.True(Press(Keys.F1));
            Assert.True(Find<HomeView>(form).Visible);
        }
    }
}
