using Microsoft.Win32;
using Performish.Core.Models;
using Performish.Core.Tweaks;
using Xunit;

namespace Performish.Tests
{
    public class TweakFactoryTests
    {
        [Fact]
        public void RegistryDword_ApplyThenCheck_ReportsApplied()
        {
            var bundle = FakeContextFactory.Create();
            var tweak = TweakFactory.RegistryDword(
                "t.dword", "name", "desc", TweakCategory.Debloat, RiskLevel.Safe, TweakScope.CurrentUser, false, "source",
                RegistryHive.CurrentUser, @"Software\Test", "Value", 1);

            Assert.Equal(TweakState.NotApplied, tweak.Check(bundle.Context));

            var applyResult = tweak.Apply(bundle.Context);
            Assert.Equal(OperationOutcome.Success, applyResult.Outcome);
            Assert.False(applyResult.WasDryRun);

            Assert.Equal(TweakState.Applied, tweak.Check(bundle.Context));
        }

        [Fact]
        public void RegistryDword_ApplyThenUndo_RestoresPriorMissingValue()
        {
            var bundle = FakeContextFactory.Create();
            var tweak = TweakFactory.RegistryDword(
                "t.dword2", "name", "desc", TweakCategory.Debloat, RiskLevel.Safe, TweakScope.CurrentUser, false, "source",
                RegistryHive.CurrentUser, @"Software\Test", "Value", 1);

            tweak.Apply(bundle.Context);
            Assert.Equal(TweakState.Applied, tweak.Check(bundle.Context));

            var undoResult = tweak.Undo(bundle.Context);
            Assert.Equal(OperationOutcome.Success, undoResult.Outcome);
            Assert.Equal(TweakState.NotApplied, tweak.Check(bundle.Context));

            var snap = bundle.Registry.Read(RegistryHive.CurrentUser, @"Software\Test", "Value");
            Assert.False(snap.Existed);
        }

        [Fact]
        public void RegistryDword_ApplyThenUndo_RestoresPriorExistingValue()
        {
            var bundle = FakeContextFactory.Create();
            bundle.Registry.Seed(RegistryHive.CurrentUser, @"Software\Test", "Value", 7, RegistryValueKind.DWord);

            var tweak = TweakFactory.RegistryDword(
                "t.dword3", "name", "desc", TweakCategory.Debloat, RiskLevel.Safe, TweakScope.CurrentUser, false, "source",
                RegistryHive.CurrentUser, @"Software\Test", "Value", 1);

            tweak.Apply(bundle.Context);
            var afterApply = bundle.Registry.Read(RegistryHive.CurrentUser, @"Software\Test", "Value");
            Assert.Equal(1, afterApply.Value);

            tweak.Undo(bundle.Context);
            var afterUndo = bundle.Registry.Read(RegistryHive.CurrentUser, @"Software\Test", "Value");
            Assert.True(afterUndo.Existed);
            Assert.Equal(7, afterUndo.Value);
        }

        [Fact]
        public void RegistryDword_DryRun_NeverWritesAndReturnsPreview()
        {
            var bundle = FakeContextFactory.Create(dryRun: true);
            var tweak = TweakFactory.RegistryDword(
                "t.dword4", "name", "desc", TweakCategory.Debloat, RiskLevel.Safe, TweakScope.CurrentUser, false, "source",
                RegistryHive.CurrentUser, @"Software\Test", "Value", 1);

            var result = tweak.Apply(bundle.Context);

            Assert.True(result.WasDryRun);
            var snap = bundle.Registry.Read(RegistryHive.CurrentUser, @"Software\Test", "Value");
            Assert.False(snap.Existed);
            Assert.Equal(TweakState.NotApplied, tweak.Check(bundle.Context));
        }

        [Fact]
        public void RegistryDword_Undo_WithoutPriorApply_IsSkippedNotThrown()
        {
            var bundle = FakeContextFactory.Create();
            var tweak = TweakFactory.RegistryDword(
                "t.dword5", "name", "desc", TweakCategory.Debloat, RiskLevel.Safe, TweakScope.CurrentUser, false, "source",
                RegistryHive.CurrentUser, @"Software\Test", "Value", 1);

            var result = tweak.Undo(bundle.Context);
            Assert.Equal(OperationOutcome.Skipped, result.Outcome);
        }

        [Fact]
        public void RegistryString_RoundTrips()
        {
            var bundle = FakeContextFactory.Create();
            var tweak = TweakFactory.RegistryString(
                "t.string1", "name", "desc", TweakCategory.Performance, RiskLevel.Safe, TweakScope.CurrentUser, false, "source",
                RegistryHive.CurrentUser, @"Control Panel\Desktop", "MenuShowDelay", "0");

            tweak.Apply(bundle.Context);
            Assert.Equal(TweakState.Applied, tweak.Check(bundle.Context));

            tweak.Undo(bundle.Context);
            Assert.Equal(TweakState.NotApplied, tweak.Check(bundle.Context));
        }

        [Fact]
        public void ServiceStartMode_ApplyThenUndo_RestoresModeAndRunningState()
        {
            var bundle = FakeContextFactory.Create();
            bundle.Services.Seed("TestSvc", System.ServiceProcess.ServiceStartMode.Automatic, running: true);

            var tweak = TweakFactory.ServiceStartMode(
                "t.service1", "name", "desc", TweakCategory.Performance, RiskLevel.Moderate, TweakScope.Machine, false, "source",
                "TestSvc", System.ServiceProcess.ServiceStartMode.Disabled);

            Assert.Equal(TweakState.NotApplied, tweak.Check(bundle.Context));
            tweak.Apply(bundle.Context);
            Assert.Equal(TweakState.Applied, tweak.Check(bundle.Context));
            Assert.False(bundle.Services.Read("TestSvc").WasRunning);

            tweak.Undo(bundle.Context);
            var restored = bundle.Services.Read("TestSvc");
            Assert.Equal(System.ServiceProcess.ServiceStartMode.Automatic, restored.StartMode);
            Assert.True(restored.WasRunning);
        }

        [Fact]
        public void ServiceStartMode_MissingService_IsNotApplicable()
        {
            var bundle = FakeContextFactory.Create();
            var tweak = TweakFactory.ServiceStartMode(
                "t.service2", "name", "desc", TweakCategory.Performance, RiskLevel.Moderate, TweakScope.Machine, false, "source",
                "MissingSvc", System.ServiceProcess.ServiceStartMode.Disabled);

            Assert.Equal(TweakState.NotApplicable, tweak.Check(bundle.Context));
            var result = tweak.Apply(bundle.Context);
            Assert.Equal(OperationOutcome.Skipped, result.Outcome);
        }

        [Fact]
        public void ScheduledTask_ApplyThenUndo_RoundTrips()
        {
            var bundle = FakeContextFactory.Create();
            bundle.Tasks.Seed(@"\Microsoft\Windows\Test\Task", true);

            var tweak = TweakFactory.ScheduledTask(
                "t.task1", "name", "desc", TweakCategory.Debloat, RiskLevel.Safe, TweakScope.Machine, false, "source",
                @"\Microsoft\Windows\Test\Task", desiredEnabled: false);

            tweak.Apply(bundle.Context);
            Assert.False(bundle.Tasks.Read(@"\Microsoft\Windows\Test\Task").WasEnabled);

            tweak.Undo(bundle.Context);
            Assert.True(bundle.Tasks.Read(@"\Microsoft\Windows\Test\Task").WasEnabled);
        }

        [Fact]
        public void AppxRemove_ApplyRemovesInstallAndProvisioning_UndoRestoresInstallOnly()
        {
            var bundle = FakeContextFactory.Create();
            bundle.Appx.Seed("Microsoft.Test", installed: true, provisioned: true);

            var tweak = TweakFactory.AppxRemove(
                "t.appx1", "name", "desc", TweakCategory.Debloat, RiskLevel.Safe, "source", "Microsoft.Test");

            Assert.Equal(TweakState.NotApplied, tweak.Check(bundle.Context));
            var applyResult = tweak.Apply(bundle.Context);
            Assert.Equal(OperationOutcome.Success, applyResult.Outcome);

            var afterApply = bundle.Appx.Read("Microsoft.Test");
            Assert.False(afterApply.InstalledForCurrentUser);
            Assert.False(afterApply.Provisioned);
            Assert.Equal(TweakState.Applied, tweak.Check(bundle.Context));

            tweak.Undo(bundle.Context);
            var afterUndo = bundle.Appx.Read("Microsoft.Test");
            Assert.True(afterUndo.InstalledForCurrentUser);
            // Deprovisioning is documented as not reversible - see AppxRemove's Undo() comment.
            Assert.False(afterUndo.Provisioned);
        }

        [Fact]
        public void AppxRemove_NotInstalled_ApplyIsSkipped()
        {
            var bundle = FakeContextFactory.Create();
            var tweak = TweakFactory.AppxRemove(
                "t.appx2", "name", "desc", TweakCategory.Debloat, RiskLevel.Safe, "source", "Microsoft.NotThere");

            var result = tweak.Apply(bundle.Context);
            Assert.Equal(OperationOutcome.Skipped, result.Outcome);
        }

        [Fact]
        public void ProcessCommandPair_Apply_RunsApplyCommandAndSucceedsOnZeroExitCode()
        {
            var bundle = FakeContextFactory.Create();
            var tweak = TweakFactory.ProcessCommandPair(
                "t.cmd1", "name", "desc", TweakCategory.Network, RiskLevel.Moderate, TweakScope.Machine, false, "source",
                "netsh.exe", "int tcp set global timestamps=disabled",
                "netsh.exe", "int tcp set global timestamps=enabled");

            var result = tweak.Apply(bundle.Context);
            Assert.Equal(OperationOutcome.Success, result.Outcome);
            Assert.Single(bundle.Process.Calls);
            Assert.Equal("netsh.exe", bundle.Process.Calls[0].FileName);
            Assert.Contains("disabled", bundle.Process.Calls[0].Arguments);

            tweak.Undo(bundle.Context);
            Assert.Equal(2, bundle.Process.Calls.Count);
            Assert.Contains("enabled", bundle.Process.Calls[1].Arguments);
        }

        [Fact]
        public void ProcessCommandPair_NonZeroExitCode_IsFailedNotThrown()
        {
            var bundle = FakeContextFactory.Create();
            bundle.Process.Script("badcommand", new Performish.Core.Backends.ProcessRunResult { ExitCode = 1, StandardError = "boom" });

            var tweak = TweakFactory.ProcessCommandPair(
                "t.cmd2", "name", "desc", TweakCategory.Network, RiskLevel.Moderate, TweakScope.Machine, false, "source",
                "badcommand.exe", "badcommand", "badcommand.exe", "badcommand");

            var result = tweak.Apply(bundle.Context);
            Assert.Equal(OperationOutcome.Failed, result.Outcome);
        }
    }
}
