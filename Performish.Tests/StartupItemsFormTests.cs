using System.Collections.Generic;
using System.Linq;
using Performish.Core.Scanner;
using Performish.Dialogs;
using Xunit;

namespace Performish.Tests
{
    /// <summary>Button/state-handling tests for the Startup Items dialog, same simulated-input
    /// pattern as DialogUiTests (never a real blocking modal loop, never a real backend).</summary>
    public class StartupItemsFormTests
    {
        private static List<StartupItemInfo> ThreeItems() => new List<StartupItemInfo>
        {
            new StartupItemInfo { Name = "OneDrive", Command = @"C:\OneDrive\OneDrive.exe", Source = "HKCU Run" },
            new StartupItemInfo { Name = "RingCentral", Command = @"C:\RC\rc.exe", Source = "HKCU Run" },
            new StartupItemInfo { Name = "VendorHelper", Command = @"C:\Vendor\helper.exe", Source = "HKLM Run" },
        };

        [StaFact]
        public void StartsWithNoSelection_ReviewButtonDisabled()
        {
            using var form = new StartupItemsForm(ThreeItems());
            form.Show();

            Assert.False(form.ReviewButton.Enabled);
            Assert.Empty(form.SelectedIds);
            Assert.Equal(3, form.Items.Count);
        }

        [StaFact]
        public void CheckingAnItem_EnablesReviewButtonAndTracksSelection()
        {
            using var form = new StartupItemsForm(ThreeItems());
            form.Show();

            var first = form.Items[0];
            form.SetChecked(first.Id, true);

            Assert.True(form.ReviewButton.Enabled);
            Assert.Contains(first.Id, form.SelectedIds);
        }

        [StaFact]
        public void UncheckingAnItem_DisablesReviewButtonAgain()
        {
            using var form = new StartupItemsForm(ThreeItems());
            form.Show();

            var first = form.Items[0];
            form.SetChecked(first.Id, true);
            form.SetChecked(first.Id, false);

            Assert.False(form.ReviewButton.Enabled);
            Assert.Empty(form.SelectedIds);
        }

        [StaFact]
        public void ClickingReviewWithSelection_PopulatesConfirmedSelectionAndClosesOk()
        {
            using var form = new StartupItemsForm(ThreeItems());
            form.Show();

            var first = form.Items[0];
            form.SetChecked(first.Id, true);
            form.ReviewButton.PerformClick();

            Assert.Equal(System.Windows.Forms.DialogResult.OK, form.DialogResult);
            Assert.Single(form.ConfirmedSelection);
            Assert.Equal(first.Id, form.ConfirmedSelection[0].Id);
        }

        [StaFact]
        public void EveryItem_IsNeverPartOfAnyPreset()
        {
            using var form = new StartupItemsForm(ThreeItems());
            form.Show();

            Assert.All(form.Items, t => Assert.Empty(t.IncludedInPresets));
        }

        [StaFact]
        public void NoItems_ReviewButtonStaysDisabled()
        {
            using var form = new StartupItemsForm(new List<StartupItemInfo>());
            form.Show();

            Assert.False(form.ReviewButton.Enabled);
            Assert.Empty(form.Items);
        }

        [StaFact]
        public void ItemIds_AreUniqueAndStable()
        {
            using var form = new StartupItemsForm(ThreeItems());
            form.Show();

            var ids = form.Items.Select(t => t.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.Contains("startup.hkcu.OneDrive", ids);
            Assert.Contains("startup.hklm.VendorHelper", ids);
        }
    }
}
