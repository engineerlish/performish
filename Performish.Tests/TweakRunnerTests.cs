using System;
using System.Linq;
using Microsoft.Win32;
using Performish.Core.Backup;
using Performish.Core.Models;
using Performish.Core.Tweaks;
using Xunit;

namespace Performish.Tests
{
    public class TweakRunnerTests
    {
        private static TweakDefinition MakeRegistryTweak(string id, RiskLevel risk = RiskLevel.Safe) =>
            TweakFactory.RegistryDword(id, $"Test tweak {id}", "desc", TweakCategory.Debloat, risk, TweakScope.CurrentUser, false, "source",
                RegistryHive.CurrentUser, @"Software\Test", id, 1);

        private static TweakDefinition MakeAlwaysThrowingTweak(string id) => new TweakDefinition(
            id, $"Test throwing tweak {id}", "desc", TweakCategory.Debloat, RiskLevel.Safe, TweakScope.CurrentUser, false, "source",
            _ => TweakState.Unknown,
            _ => throw new InvalidOperationException("boom"),
            _ => TweakOperationResult.Skipped("n/a"));

        [Fact]
        public void ApplyBatch_OneFailureDoesNotStopTheRestOfTheBatch()
        {
            var bundle = FakeContextFactory.Create();
            var changeLog = new InMemoryChangeLogStore();
            var restorePoint = new FakeRestorePointBackend();
            var runner = new TweakRunner(changeLog, restorePoint);

            var t1 = MakeRegistryTweak("t1");
            var throwing = MakeAlwaysThrowingTweak("t2");
            var t3 = MakeRegistryTweak("t3");

            var result = runner.ApplyBatch(new[] { t1, throwing, t3 }, bundle.Context, createRestorePoint: false);

            Assert.Equal(3, result.Results.Count);
            Assert.True(result.Results[1].Failed);
            Assert.False(result.Results[0].Failed);
            Assert.False(result.Results[2].Failed);

            // Both t1 and t3 actually ran despite t2 throwing in between.
            Assert.Equal(TweakState.Applied, t1.Check(bundle.Context));
            Assert.Equal(TweakState.Applied, t3.Check(bundle.Context));
        }

        [Fact]
        public void ApplyBatch_LogsEveryOutcomeToTheChangeLog()
        {
            var bundle = FakeContextFactory.Create();
            var changeLog = new InMemoryChangeLogStore();
            var runner = new TweakRunner(changeLog, new FakeRestorePointBackend());

            runner.ApplyBatch(new[] { MakeRegistryTweak("t1") }, bundle.Context, createRestorePoint: false);

            var entries = changeLog.ReadAll();
            Assert.Single(entries);
            Assert.Equal(ChangeLogAction.Apply, entries[0].Action);
            Assert.Equal(OperationOutcome.Success, entries[0].Outcome);
        }

        [Fact]
        public void ApplyBatch_CreatesRestorePointWhenRequested_AndNotInDryRun()
        {
            var changeLog = new InMemoryChangeLogStore();
            var restorePoint = new FakeRestorePointBackend();
            var runner = new TweakRunner(changeLog, restorePoint);

            var real = FakeContextFactory.Create(dryRun: false);
            runner.ApplyBatch(new[] { MakeRegistryTweak("t1") }, real.Context, createRestorePoint: true);
            Assert.Equal(1, restorePoint.CreateCallCount);

            var dry = FakeContextFactory.Create(dryRun: true);
            runner.ApplyBatch(new[] { MakeRegistryTweak("t2") }, dry.Context, createRestorePoint: true);
            Assert.Equal(1, restorePoint.CreateCallCount); // unchanged - dry run never creates one
        }

        [Fact]
        public void RevertEverything_UndoesOnlyTweaksCurrentlyApplied()
        {
            var bundle = FakeContextFactory.Create();
            var changeLog = new InMemoryChangeLogStore();
            var runner = new TweakRunner(changeLog, new FakeRestorePointBackend());

            var t1 = MakeRegistryTweak("t1");
            var t2 = MakeRegistryTweak("t2");
            var registry = new TweakRegistry(new[] { t1, t2 });

            runner.ApplyBatch(new[] { t1, t2 }, bundle.Context, createRestorePoint: false);
            runner.UndoBatch(new[] { t1 }, bundle.Context); // t1 already reverted by hand

            var revertResult = runner.RevertEverything(registry, bundle.Context);

            // Only t2 should have been undone by RevertEverything (t1's last action was already Undo).
            Assert.Single(revertResult.Results);
            Assert.Equal("t2", revertResult.Results[0].Tweak.Id);
            Assert.Equal(TweakState.NotApplied, t2.Check(bundle.Context));
        }

        [Fact]
        public void CurrentlyAppliedTweakIds_ExcludesDryRunApplies()
        {
            var bundle = FakeContextFactory.Create(dryRun: true);
            var changeLog = new InMemoryChangeLogStore();
            var runner = new TweakRunner(changeLog, new FakeRestorePointBackend());

            runner.ApplyBatch(new[] { MakeRegistryTweak("t1") }, bundle.Context, createRestorePoint: false);

            Assert.Empty(runner.CurrentlyAppliedTweakIds());
        }

        [Fact]
        public void DetectDrift_NoneApplied_ReturnsEmpty()
        {
            var bundle = FakeContextFactory.Create();
            var changeLog = new InMemoryChangeLogStore();
            var runner = new TweakRunner(changeLog, new FakeRestorePointBackend());
            var t1 = MakeRegistryTweak("t1");
            var registry = new TweakRegistry(new[] { t1 });

            var drifted = runner.DetectDrift(registry, bundle.Context);

            Assert.Empty(drifted);
        }

        [Fact]
        public void DetectDrift_AppliedTweak_StillAppliedInBackend_IsNotDrift()
        {
            var bundle = FakeContextFactory.Create();
            var changeLog = new InMemoryChangeLogStore();
            var runner = new TweakRunner(changeLog, new FakeRestorePointBackend());
            var t1 = MakeRegistryTweak("t1");
            var registry = new TweakRegistry(new[] { t1 });

            runner.ApplyBatch(new[] { t1 }, bundle.Context, createRestorePoint: false);

            var drifted = runner.DetectDrift(registry, bundle.Context);

            Assert.Empty(drifted);
        }

        [Fact]
        public void DetectDrift_AppliedTweak_ThenBackendChangedUnderneath_IsDrift()
        {
            var bundle = FakeContextFactory.Create();
            var changeLog = new InMemoryChangeLogStore();
            var runner = new TweakRunner(changeLog, new FakeRestorePointBackend());
            var t1 = MakeRegistryTweak("t1");
            var registry = new TweakRegistry(new[] { t1 });

            runner.ApplyBatch(new[] { t1 }, bundle.Context, createRestorePoint: false);
            Assert.Equal(TweakState.Applied, t1.Check(bundle.Context));

            // Simulate "something reverted it without Performish's knowledge" - directly mutate the
            // fake registry back, bypassing the tweak's own Apply/Undo (a real-world Group Policy
            // refresh or Windows Update wouldn't go through Performish either).
            bundle.Registry.Delete(RegistryHive.CurrentUser, @"Software\Test", "t1");

            var drifted = runner.DetectDrift(registry, bundle.Context);

            Assert.Single(drifted);
            Assert.Equal("t1", drifted[0].Id);
        }

        [Fact]
        public void DetectDrift_TweakRemovedFromRegistry_IsSkippedNotThrown()
        {
            var bundle = FakeContextFactory.Create();
            var changeLog = new InMemoryChangeLogStore();
            var runner = new TweakRunner(changeLog, new FakeRestorePointBackend());
            var t1 = MakeRegistryTweak("t1");
            var registryWithT1 = new TweakRegistry(new[] { t1 });

            runner.ApplyBatch(new[] { t1 }, bundle.Context, createRestorePoint: false);

            // A different registry that no longer contains t1 (simulating a build where a tweak was
            // removed from the library after a user applied it on an older build).
            var emptyRegistry = new TweakRegistry(System.Array.Empty<TweakDefinition>());

            var ex = Record.Exception(() => runner.DetectDrift(emptyRegistry, bundle.Context));
            Assert.Null(ex);
        }

        // ---- "Partially applied" state (health-score task Step 2: "Test with simulated state for
        // applied, not applied, partially applied, and drift"). TweakState has no PartiallyApplied
        // value - a multi-value tweak's Check() must collapse a partial match to NotApplied, never
        // silently report Applied, so a drift/undo caller never trusts a half-true reading. Verified
        // directly against gaming.mouse_precision_disable (Performish.Core.Tweaks.GamingTweaks), the
        // library's clearest multi-registry-value tweak (3 values must all match). ------------------

        private static TweakDefinition MousePrecisionTweak() =>
            Performish.Core.Tweaks.GamingTweaks.All().Single(t => t.Id == "gaming.mouse_precision_disable");

        [Fact]
        public void MultiValueTweak_AllValuesMatch_ReportsApplied()
        {
            var bundle = FakeContextFactory.Create();
            var tweak = MousePrecisionTweak();
            bundle.Registry.Write(RegistryHive.CurrentUser, @"Control Panel\Mouse", "MouseSpeed", "0", RegistryValueKind.String);
            bundle.Registry.Write(RegistryHive.CurrentUser, @"Control Panel\Mouse", "MouseThreshold1", "0", RegistryValueKind.String);
            bundle.Registry.Write(RegistryHive.CurrentUser, @"Control Panel\Mouse", "MouseThreshold2", "0", RegistryValueKind.String);

            Assert.Equal(TweakState.Applied, tweak.Check(bundle.Context));
        }

        [Fact]
        public void MultiValueTweak_OnlySomeValuesMatch_ReportsNotAppliedNeverAppliedOrPartial()
        {
            var bundle = FakeContextFactory.Create();
            var tweak = MousePrecisionTweak();
            // Only 1 of the 3 required values set correctly - simulates another tool, or a partial/
            // interrupted apply, leaving the setting in a mixed state.
            bundle.Registry.Write(RegistryHive.CurrentUser, @"Control Panel\Mouse", "MouseSpeed", "0", RegistryValueKind.String);
            bundle.Registry.Write(RegistryHive.CurrentUser, @"Control Panel\Mouse", "MouseThreshold1", "1", RegistryValueKind.String);
            bundle.Registry.Write(RegistryHive.CurrentUser, @"Control Panel\Mouse", "MouseThreshold2", "1", RegistryValueKind.String);

            Assert.Equal(TweakState.NotApplied, tweak.Check(bundle.Context));
        }

        [Fact]
        public void MultiValueTweak_NoValuesSet_ReportsNotApplied()
        {
            var bundle = FakeContextFactory.Create();
            var tweak = MousePrecisionTweak();

            Assert.Equal(TweakState.NotApplied, tweak.Check(bundle.Context));
        }
    }
}
