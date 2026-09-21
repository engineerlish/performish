using System.Drawing;
using System.Linq;
using Performish.Core;
using Performish.Core.Models;
using Performish.Dialogs;
using Xunit;

namespace Performish.Tests
{
    /// <summary>Button/action-mapping and state-handling tests for the Phase 4 button UI, using
    /// simulated input events (Button.PerformClick(), CheckedListBox.SetItemChecked()) against real
    /// WinForms control instances - never a real ShowDialog() modal loop (which would block the test
    /// thread forever waiting for a human), and never AppServices.BuildReal() (Fake-backed
    /// AppServices.BuildFake() only, per the hard safety rule). [StaFact] runs each test on a
    /// dedicated STA thread, which WinForms controls require even off-screen.</summary>
    public class DialogUiTests
    {
        private static AppServices FakeServices() => AppServices.BuildFake();

        [StaFact]
        public void TweakBrowser_StartsWithNoSelection_ReviewButtonDisabled()
        {
            var services = FakeServices();
            using var form = new TweakBrowserForm(services.TweakRegistry.All, services, Enumerable.Empty<string>(), null);
            form.Show();

            Assert.False(form.ReviewButton.Enabled);
            Assert.Empty(form.SelectedIds);
        }

        [StaFact]
        public void TweakBrowser_CheckingAnItem_EnablesReviewButtonAndTracksSelection()
        {
            var services = FakeServices();
            using var form = new TweakBrowserForm(services.TweakRegistry.All, services, Enumerable.Empty<string>(), null);
            form.Show();

            var firstTweak = form.VisibleTweaks[0];
            form.SetChecked(firstTweak.Id, true);

            Assert.True(form.ReviewButton.Enabled);
            Assert.Contains(firstTweak.Id, form.SelectedIds);
        }

        [StaFact]
        public void TweakBrowser_UncheckingAnItem_RemovesItAndCanDisableReviewButtonAgain()
        {
            var services = FakeServices();
            using var form = new TweakBrowserForm(services.TweakRegistry.All, services, Enumerable.Empty<string>(), null);
            form.Show();

            var firstTweak = form.VisibleTweaks[0];
            form.SetChecked(firstTweak.Id, true);
            Assert.True(form.ReviewButton.Enabled);

            form.SetChecked(firstTweak.Id, false);

            Assert.False(form.ReviewButton.Enabled);
            Assert.DoesNotContain(firstTweak.Id, form.SelectedIds);
        }

        [StaFact]
        public void TweakBrowser_ClickingReviewWithSelection_PopulatesConfirmedSelectionAndClosesOk()
        {
            var services = FakeServices();
            using var form = new TweakBrowserForm(services.TweakRegistry.All, services, Enumerable.Empty<string>(), null);
            form.Show();

            var firstTweak = form.VisibleTweaks[0];
            form.SetChecked(firstTweak.Id, true);
            form.ReviewButton.PerformClick();

            Assert.Equal(System.Windows.Forms.DialogResult.OK, form.DialogResult);
            Assert.Single(form.ConfirmedSelection);
            Assert.Equal(firstTweak.Id, form.ConfirmedSelection[0].Id);
        }

        [StaFact]
        public void TweakBrowser_CategoryFilter_OnlyShowsThatCategory()
        {
            var services = FakeServices();
            using var form = new TweakBrowserForm(services.TweakRegistry.All, services, Enumerable.Empty<string>(), null);
            form.Show();

            form.ClickCategory(TweakCategory.Network);

            Assert.NotEmpty(form.VisibleTweaks);
            Assert.All(form.VisibleTweaks, t => Assert.Equal(TweakCategory.Network, t.Category));
        }

        [StaFact]
        public void TweakBrowser_PreselectedIds_StartCheckedAndReviewButtonEnabled()
        {
            var services = FakeServices();
            var preselected = services.TweakRegistry.All.Take(2).Select(t => t.Id).ToList();
            using var form = new TweakBrowserForm(services.TweakRegistry.All, services, preselected, null);
            form.Show();

            Assert.True(form.ReviewButton.Enabled);
            Assert.Equal(2, form.SelectedIds.Count);
        }

        [StaFact]
        public void ConfirmDialog_ClickingConfirm_SetsConfirmedTrueAndDialogResultOk()
        {
            using var form = new ConfirmDialogForm("Review & apply",
                new[] { ("Apply this tweak", Color.White) }, "Apply", "Cancel");
            form.Show();

            form.ConfirmButton.PerformClick();

            Assert.True(form.Confirmed);
            Assert.Equal(System.Windows.Forms.DialogResult.OK, form.DialogResult);
        }

        [StaFact]
        public void ConfirmDialog_ClickingCancel_SetsConfirmedFalseAndDialogResultCancel()
        {
            using var form = new ConfirmDialogForm("Review & apply",
                new[] { ("Apply this tweak", Color.White) }, "Apply", "Cancel");
            form.Show();

            form.CancelActionButton.PerformClick();

            Assert.False(form.Confirmed);
            Assert.Equal(System.Windows.Forms.DialogResult.Cancel, form.DialogResult);
        }

        [StaFact]
        public void ConfirmDialog_DefaultFocus_IsCancelButton_NotConfirm()
        {
            // Safety default: accidental Enter-mashing on a destructive confirmation must cancel,
            // not apply - see ConfirmDialogForm's constructor comment.
            using var form = new ConfirmDialogForm("Revert everything",
                new[] { ("This will undo 3 tweaks", Color.White) }, "Revert", "Cancel");

            Assert.Same(form.CancelActionButton, form.ActiveControl);
            Assert.Same(form.CancelActionButton, form.CancelButton);
        }

        [StaFact]
        public void PresetPicker_ClickingBalanced_SetsChosenAndDialogResultOk()
        {
            var services = FakeServices();
            using var form = new PresetPickerForm(services.TweakRegistry);
            form.Show();

            form.ClickPreset(Preset.Balanced);

            Assert.Equal(Preset.Balanced, form.Chosen);
            Assert.Equal(System.Windows.Forms.DialogResult.OK, form.DialogResult);
        }
    }
}
