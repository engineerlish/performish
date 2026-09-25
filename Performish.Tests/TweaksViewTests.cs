using System.Linq;
using System.Windows.Forms;
using Performish.Core;
using Performish.Core.Models;
using Performish.Views;
using Xunit;

namespace Performish.Tests
{
    /// <summary>Hosts a view in a shown, off-to-the-side form (WinForms controls need a real handle
    /// for layout and PerformClick) - fake services only, per the hard safety rule.</summary>
    internal static class ViewHost
    {
        public static Form Show(Control view, int width = 1136, int height = 800)
        {
            var form = new Form
            {
                ClientSize = new System.Drawing.Size(width, height),
                StartPosition = FormStartPosition.Manual,
                Location = new System.Drawing.Point(10, 10),
                BackColor = System.Drawing.Color.Black
            };
            view.Dock = DockStyle.Fill;
            form.Controls.Add(view);
            form.Show();
            Application.DoEvents();
            return form;
        }
    }

    /// <summary>Tests for the in-window tweak browser (cards, paging, presets, search, detail),
    /// replacing the old Tweak Browser dialog and Presets picker tests.</summary>
    public class TweaksViewTests
    {
        private static TweaksView Make(AppServices services, System.Collections.Generic.IEnumerable<string> preselected = null, TweakCategory? category = null) =>
            new TweaksView(services.TweakRegistry.All, services, preselected ?? Enumerable.Empty<string>(), category);

        [StaFact]
        public void StartsWithNoSelection_ReviewButtonDisabled()
        {
            var services = AppServices.BuildFake();
            var view = Make(services);
            using var form = ViewHost.Show(view);

            Assert.False(view.ReviewButton.Enabled);
            Assert.Empty(view.SelectedIds);
        }

        [StaFact]
        public void CheckingAnItem_EnablesReviewButtonAndTracksSelection()
        {
            var services = AppServices.BuildFake();
            var view = Make(services);
            using var form = ViewHost.Show(view);

            var first = view.VisibleTweaks[0];
            view.SetChecked(first.Id, true);

            Assert.True(view.ReviewButton.Enabled);
            Assert.Contains(first.Id, view.SelectedIds);
            Assert.Contains("1 selected", view.CountText);
        }

        [StaFact]
        public void UncheckingAnItem_RemovesItAndDisablesReviewAgain()
        {
            var services = AppServices.BuildFake();
            var view = Make(services);
            using var form = ViewHost.Show(view);

            var first = view.VisibleTweaks[0];
            view.SetChecked(first.Id, true);
            view.SetChecked(first.Id, false);

            Assert.False(view.ReviewButton.Enabled);
            Assert.DoesNotContain(first.Id, view.SelectedIds);
        }

        [StaFact]
        public void ClickingReview_RaisesReviewRequestedWithTheSelectedTweaks()
        {
            var services = AppServices.BuildFake();
            var view = Make(services);
            using var form = ViewHost.Show(view);
            System.Collections.Generic.List<TweakDefinition> received = null;
            view.ReviewRequested += s => received = s;

            var first = view.VisibleTweaks[0];
            view.SetChecked(first.Id, true);
            view.ReviewButton.PerformClick();

            Assert.NotNull(received);
            Assert.Single(received);
            Assert.Equal(first.Id, received[0].Id);
        }

        [StaFact]
        public void SetBusy_DisablesReview_AndIgnoresAReviewRequest()
        {
            var services = AppServices.BuildFake();
            var view = Make(services, services.TweakRegistry.All.Take(2).Select(t => t.Id));
            using var form = ViewHost.Show(view);
            var raised = false;
            view.ReviewRequested += _ => raised = true;

            view.SetBusy(true);
            view.RaiseReviewForTesting();

            Assert.False(view.ReviewButton.Enabled);
            Assert.False(raised);

            view.SetBusy(false);
            Assert.True(view.ReviewButton.Enabled);
        }

        [StaFact]
        public void CategoryFilter_OnlyShowsThatCategory()
        {
            var services = AppServices.BuildFake();
            var view = Make(services);
            using var form = ViewHost.Show(view);

            view.ClickCategory(TweakCategory.Network);

            Assert.NotEmpty(view.VisibleTweaks);
            Assert.All(view.VisibleTweaks, t => Assert.Equal(TweakCategory.Network, t.Category));
            Assert.Equal(TweakCategory.Network, view.FilterCategory);
        }

        [StaFact]
        public void PreselectedIds_StartCheckedAndReviewEnabled()
        {
            var services = AppServices.BuildFake();
            var preselected = services.TweakRegistry.All.Take(2).Select(t => t.Id).ToList();
            var view = Make(services, preselected);
            using var form = ViewHost.Show(view);

            Assert.True(view.ReviewButton.Enabled);
            Assert.Equal(2, view.SelectedIds.Count);
        }

        [StaFact]
        public void PresetChip_SelectsExactlyThatPresetsTweaks()
        {
            var services = AppServices.BuildFake();
            var view = Make(services);
            using var form = ViewHost.Show(view);

            view.ClickPreset(Preset.Balanced);

            var expected = services.TweakRegistry.ByPreset(Preset.Balanced).Select(t => t.Id).ToHashSet();
            Assert.True(expected.SetEquals(view.SelectedIds));
            Assert.True(view.ReviewButton.Enabled);
        }

        [StaFact]
        public void Search_MatchesTitleDescriptionAndHiddenId()
        {
            var services = AppServices.BuildFake();
            var view = Make(services);
            using var form = ViewHost.Show(view);
            var target = services.TweakRegistry.All.First();

            view.SetSearchText(target.Title.Substring(0, 12));
            Assert.Contains(target, view.VisibleTweaks);

            view.SetSearchText(target.Id); // id is a hidden fallback, never displayed
            Assert.Contains(target, view.VisibleTweaks);

            view.SetSearchText("zzzz-no-such-tweak");
            Assert.Empty(view.VisibleTweaks);
            Assert.Contains("No tweaks match", view.DetailTitle);
        }

        // ---- paging: no scrolling, every tweak reachable ------------------------------------------------

        [StaFact]
        public void Paging_ShowsOnlyAPageOfCards_AndEveryTweakIsReachable()
        {
            var services = AppServices.BuildFake();
            var view = Make(services);
            using var form = ViewHost.Show(view);

            Assert.True(view.PageSize >= 4);
            Assert.True(view.VisibleTweaks.Count > view.PageSize, "test needs more tweaks than one page");

            var seen = new System.Collections.Generic.HashSet<string>();
            int pages = (view.VisibleTweaks.Count + view.PageSize - 1) / view.PageSize;
            for (int p = 0; p < pages; p++)
            {
                view.SetCursor(p * view.PageSize);
                var shown = view.Cards.Where(c => c.Visible && c.Tweak != null).ToList();
                Assert.True(shown.Count <= view.PageSize);
                foreach (var c in shown) Assert.True(seen.Add(c.Tweak.Id), "a tweak appeared on two pages");
                Assert.Contains($"page {p + 1} of {pages}", view.PageText);
            }
            Assert.Equal(view.VisibleTweaks.Count, seen.Count);
        }

        [StaFact]
        public void PagerButtons_DisabledAtTheEnds()
        {
            var services = AppServices.BuildFake();
            var view = Make(services);
            using var form = ViewHost.Show(view);

            Assert.False(view.PrevButton.Enabled);
            Assert.True(view.NextButton.Enabled);

            view.PressNavKey(Keys.End);
            Assert.True(view.PrevButton.Enabled);
            Assert.False(view.NextButton.Enabled);
        }

        // ---- keyboard navigation -----------------------------------------------------------------------

        [StaFact]
        public void ArrowKeys_MoveTheCurrentCardInTwoDimensions_AndClampAtTheEnds()
        {
            var services = AppServices.BuildFake();
            var view = Make(services);
            using var form = ViewHost.Show(view);

            view.PressNavKey(Keys.Left);
            Assert.Equal(0, view.CursorIndex); // clamped at the start

            view.PressNavKey(Keys.Right);
            Assert.Equal(1, view.CursorIndex);

            view.PressNavKey(Keys.Down);
            Assert.Equal(1 + view.Columns, view.CursorIndex);

            view.PressNavKey(Keys.Up);
            Assert.Equal(1, view.CursorIndex);

            view.PressNavKey(Keys.PageDown);
            Assert.Equal(1 + view.PageSize, view.CursorIndex);

            view.PressNavKey(Keys.End);
            Assert.Equal(view.VisibleTweaks.Count - 1, view.CursorIndex);

            view.PressNavKey(Keys.Home);
            Assert.Equal(0, view.CursorIndex);
        }

        [StaFact]
        public void OnlyTheCurrentCard_IsATabStop()
        {
            var services = AppServices.BuildFake();
            var view = Make(services);
            using var form = ViewHost.Show(view);

            view.PressNavKey(Keys.Right);

            var tabStops = view.Cards.Where(c => c.Visible && c.TabStop).ToList();
            Assert.Single(tabStops);
            Assert.Same(view.VisibleTweaks[view.CursorIndex], tabStops[0].Tweak);
        }

        // ---- accessibility -----------------------------------------------------------------------------

        [StaFact]
        public void Cards_ExposeTitleRiskStateAndCheckedStateToScreenReaders()
        {
            var services = AppServices.BuildFake();
            var view = Make(services);
            using var form = ViewHost.Show(view);
            view.SetChecked(view.VisibleTweaks[0].Id, true);

            var card = view.Cards.First(c => c.Visible && c.Tweak != null && c.IsChecked);
            var acc = card.AccessibilityObject;

            Assert.Equal(AccessibleRole.CheckButton, acc.Role);
            Assert.Contains(card.Tweak.Title, acc.Name);
            Assert.Contains("risk", acc.Name);
            Assert.True((acc.State & AccessibleStates.Checked) != 0);

            var unchecked1 = view.Cards.First(c => c.Visible && c.Tweak != null && !c.IsChecked);
            Assert.True((unchecked1.AccessibilityObject.State & AccessibleStates.Checked) == 0);
        }

        [StaFact]
        public void CardToggle_ViaTheAccessibilityDefaultAction_TogglesSelection()
        {
            var services = AppServices.BuildFake();
            var view = Make(services);
            using var form = ViewHost.Show(view);

            var card = view.Cards.First(c => c.Visible && c.Tweak != null);
            card.AccessibilityObject.DoDefaultAction();

            Assert.Contains(card.Tweak.Id, view.SelectedIds);
        }

        // ---- title/detail rules carried over from the old browser --------------------------------------

        [StaFact]
        public void EveryCategoryAndSearchResult_CardsNeverShowATypeNameOrRawId()
        {
            var services = AppServices.BuildFake();
            var view = Make(services);
            using var form = ViewHost.Show(view);

            void AssertClean()
            {
                Assert.NotEmpty(view.VisibleTweaks);
                foreach (var card in view.Cards.Where(c => c.Visible && c.Tweak != null))
                {
                    Assert.DoesNotContain("Performish.Core.Models", card.AccessibilityObject.Name);
                    Assert.DoesNotContain("TweakDefinition", card.AccessibilityObject.Name);
                    Assert.DoesNotContain(card.Tweak.Id, card.AccessibilityObject.Name);
                }
            }

            foreach (var category in new TweakCategory?[] { null, TweakCategory.Debloat, TweakCategory.Performance, TweakCategory.Gaming, TweakCategory.Network, TweakCategory.Maintenance })
            {
                view.ClickCategory(category);
                AssertClean();
            }
            view.ClickCategory(null);
            view.SetSearchText("disable");
            AssertClean();
        }

        [Fact]
        public void DisplayTitle_EmptyTitle_ShowsPlaceholder_NotBlankOrTypeName()
        {
            var tweak = new TweakDefinition("t1", "", "desc", TweakCategory.Debloat, RiskLevel.Safe, TweakScope.CurrentUser, false, "source",
                _ => TweakState.Unknown, _ => TweakOperationResult.Success("ok"), _ => TweakOperationResult.Skipped("n/a"));

            Assert.Equal("[Untitled tweak]", TweakCard.DisplayTitle(tweak));
            Assert.Equal("[Untitled tweak]", TweakCard.DisplayTitle(null));
        }

        [Fact]
        public void LongTitle_IsWrappedToTwoLinesAndEllipsized_NeverOverflowing()
        {
            var longTitle = "Disable the extremely long and verbose setting that goes on for a very long time indeed and then some more words";

            var lines = TweakCard.Wrap(longTitle, 26, 2);

            Assert.Equal(2, lines.Count);
            Assert.All(lines, l => Assert.True(l.Length <= 26));
            Assert.EndsWith("...", lines[1]);
        }

        [StaFact]
        public void Detail_ShowsFullTitleUndoNoteAndSource_AndMaintenanceUndoIsHonest()
        {
            var services = AppServices.BuildFake();
            var view = Make(services);
            using var form = ViewHost.Show(view);

            var t = view.VisibleTweaks[0];
            Assert.Contains(t.Title, view.DetailText);
            Assert.Contains("Undo:", view.DetailText);
            Assert.Contains("Source:", view.DetailText);

            view.ClickCategory(TweakCategory.Maintenance);
            Assert.Contains("not possible", view.DetailText);
        }

        [StaFact]
        public void DetailToggleButton_FollowsCurrentCardAndSelection()
        {
            var services = AppServices.BuildFake();
            var view = Make(services);
            using var form = ViewHost.Show(view);

            Assert.Equal("Select for apply", view.ToggleButton.Text);
            view.ToggleButton.PerformClick();
            Assert.Equal("Deselect", view.ToggleButton.Text);
            Assert.Contains(view.VisibleTweaks[0].Id, view.SelectedIds);
        }

        [StaFact]
        public void InvalidateStates_RereadsLiveState_AfterTheSystemChanges()
        {
            var services = AppServices.BuildFake();
            var view = Make(services);
            using var form = ViewHost.Show(view);
            var target = view.VisibleTweaks[0];
            var before = view.Cards.First(c => c.Tweak == target).AccessibilityObject.Name;

            var ctx = services.CreateContext(dryRun: false);
            target.Apply(ctx);
            view.InvalidateStates();

            var after = view.Cards.First(c => c.Tweak == target).AccessibilityObject.Name;
            Assert.Contains("not applied", before);
            Assert.DoesNotContain("not applied", after);
            Assert.Contains("applied", after);
        }
    }
}
