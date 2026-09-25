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

        [Fact]
        public void TweakDefinition_ToString_ReturnsTitleNeverTheTypeName()
        {
            var tweak = new TweakDefinition("t1", "Disable something", "desc", TweakCategory.Debloat,
                RiskLevel.Safe, TweakScope.CurrentUser, false, "source",
                _ => TweakState.Unknown, _ => TweakOperationResult.Success("ok"), _ => TweakOperationResult.Skipped("n/a"));

            Assert.Equal("Disable something", tweak.ToString());
        }

        [Fact]
        public void TweakDefinition_EmptyTitle_ToStringShowsPlaceholder_NotBlankOrTypeName()
        {
            // Bypasses TweakRegistry (which refuses to load an empty title) to prove the display-layer
            // safety net holds even for a TweakDefinition built directly.
            var tweak = new TweakDefinition("t1", "", "desc", TweakCategory.Debloat,
                RiskLevel.Safe, TweakScope.CurrentUser, false, "source",
                _ => TweakState.Unknown, _ => TweakOperationResult.Success("ok"), _ => TweakOperationResult.Skipped("n/a"));

            Assert.Equal("[Untitled tweak]", tweak.ToString());
            Assert.Equal("[Untitled tweak]", TweakDefinition.UntitledPlaceholder);

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
    }
}
