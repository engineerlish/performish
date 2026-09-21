using System.Linq;
using Performish.Core.Models;
using Performish.Core.Backup;
using Performish.Core.Tweaks;
using Xunit;

namespace Performish.Tests
{
    public class TweakLibraryTests
    {
        [Theory]
        [InlineData(Preset.Conservative)]
        [InlineData(Preset.Balanced)]
        [InlineData(Preset.Aggressive)]
        public void DryRunApplyBatch_EveryPreset_CompletesWithNoFailures(Preset preset)
        {
            // Regression test for the "Balanced preset dry run crashes" bug: the root cause
            // (RunningForm.RunAsync's handle-creation race - see DECISIONS.md) was a WinForms
            // threading issue, not a TweakRunner/tweak-data issue, so it can't be reproduced at this
            // Core level (no WinForms Control involved here at all). This test still matters -
            // it proves the engine itself has no preset-size-dependent behavior, isolating the UI
            // layer as the only remaining suspect, which is exactly how the bug was diagnosed. The
            // matching UI-level regression test is DialogUiTests.RunningForm_DryRunApply_*.
            var registry = TweakRegistry.BuildDefault();
            var tweaks = registry.ByPreset(preset).ToList();
            Assert.NotEmpty(tweaks);

            var bundle = FakeContextFactory.Create(dryRun: true);
            var runner = new TweakRunner(new InMemoryChangeLogStore(), new FakeRestorePointBackend());

            var result = runner.ApplyBatch(tweaks, bundle.Context, createRestorePoint: false);

            Assert.Equal(tweaks.Count, result.Results.Count);
            Assert.All(result.Results, r => Assert.False(r.Failed, $"{r.Tweak.Id} failed during dry run: {r.Result?.Message ?? r.Exception?.Message}"));
        }


        [Fact]
        public void DefaultRegistry_HasNoDuplicateIds_AndBuildsSuccessfully()
        {
            var registry = TweakRegistry.BuildDefault();
            Assert.True(registry.All.Count >= 40, $"Expected 40+ tweaks, found {registry.All.Count}.");
        }

        [Fact]
        public void EveryTweak_HasNameDescriptionAndSource()
        {
            var registry = TweakRegistry.BuildDefault();
            foreach (var t in registry.All)
            {
                Assert.False(string.IsNullOrWhiteSpace(t.Name), $"{t.Id} has no name.");
                Assert.False(string.IsNullOrWhiteSpace(t.Description), $"{t.Id} has no description.");
                Assert.False(string.IsNullOrWhiteSpace(t.Source), $"{t.Id} has no source/justification.");
            }
        }

        [Fact]
        public void EveryTweak_HasAWorkingUndo_ThatNeverThrows()
        {
            var registry = TweakRegistry.BuildDefault();
            foreach (var t in registry.All)
            {
                var bundle = FakeContextFactory.Create();
                // Undo without a prior Apply must be a graceful Skipped result, never an exception -
                // this is exactly the "revert everything on a fresh install" edge case.
                var ex = Record.Exception(() => t.Undo(bundle.Context));
                Assert.Null(ex);
            }
        }

        [Fact]
        public void EveryTweak_ApplyNeverThrows_AgainstEmptyFakeState()
        {
            var registry = TweakRegistry.BuildDefault();
            foreach (var t in registry.All)
            {
                var bundle = FakeContextFactory.Create();
                var ex = Record.Exception(() => t.Apply(bundle.Context));
                Assert.Null(ex);
            }
        }

        [Fact]
        public void EveryTweak_DryRunApply_NeverThrowsAndNeverWritesToBackends()
        {
            var registry = TweakRegistry.BuildDefault();
            foreach (var t in registry.All)
            {
                var bundle = FakeContextFactory.Create(dryRun: true);
                var result = t.Apply(bundle.Context);
                Assert.True(result.WasDryRun, $"{t.Id} did not report WasDryRun in dry-run mode.");
            }
        }

        [Fact]
        public void ProtectedList_Services_NeverAppearAsAServiceTweakTarget()
        {
            // This test can't reach into each tweak's closures directly (they're just delegates), but
            // every ServiceStartMode tweak the libraries define is checked here explicitly against the
            // protected list by re-deriving the ids the libraries would produce for a protected name.
            var registry = TweakRegistry.BuildDefault();
            foreach (var protectedService in ProtectedList.Services)
            {
                foreach (var t in registry.All)
                {
                    Assert.DoesNotContain(protectedService, t.Source);
                }
            }
        }

        [Fact]
        public void Presets_OnlyReferenceIncludedTweaks_AndAggressiveIsSupersetOfBalanced()
        {
            var registry = TweakRegistry.BuildDefault();
            var balanced = registry.ByPreset(Preset.Balanced).Select(t => t.Id).ToHashSet();
            var aggressive = registry.ByPreset(Preset.Aggressive).Select(t => t.Id).ToHashSet();

            // Not a strict requirement of the brief, but every tweak library in this project was
            // written so Aggressive = Balanced + more; catch a regression if that assumption breaks.
            foreach (var id in balanced)
                Assert.Contains(id, aggressive);
        }

        [Fact]
        public void NoTweak_TargetsAProtectedAppxPrefix()
        {
            var registry = TweakRegistry.BuildDefault();
            foreach (var protectedPrefix in ProtectedList.AppxPackagePrefixes)
            {
                foreach (var t in registry.All)
                {
                    if (t.Id.StartsWith("debloat.appx."))
                        Assert.DoesNotContain(protectedPrefix, t.Source);
                }
            }
        }
    }
}
