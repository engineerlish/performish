using Microsoft.Win32;
using Performish.Core.Models;
using Performish.Core.Scanner;
using Performish.Core.Tweaks;
using Xunit;

namespace Performish.Tests
{
    public class StartupItemTweaksTests
    {
        private static StartupItemInfo HkcuItem(string name, string command = @"C:\App\app.exe") =>
            new StartupItemInfo { Name = name, Command = command, Source = "HKCU Run" };

        private static StartupItemInfo HklmItem(string name, string command = @"C:\App\app.exe") =>
            new StartupItemInfo { Name = name, Command = command, Source = "HKLM Run" };

        [Fact]
        public void BuildFor_HkcuItem_UsesCurrentUserScopeAndId()
        {
            var tweak = StartupItemTweaks.BuildFor(HkcuItem("RingCentral"));

            Assert.Equal("startup.hkcu.RingCentral", tweak.Id);
            Assert.Equal(TweakScope.CurrentUser, tweak.Scope);
            Assert.Equal("Stop 'RingCentral' from starting automatically", tweak.Title);
            Assert.Empty(tweak.IncludedInPresets); // never part of a preset
            Assert.Equal(RiskLevel.Moderate, tweak.Risk);
        }

        [Fact]
        public void BuildFor_HklmItem_UsesMachineScopeAndId()
        {
            var tweak = StartupItemTweaks.BuildFor(HklmItem("SomeVendorHelper"));

            Assert.Equal("startup.hklm.SomeVendorHelper", tweak.Id);
            Assert.Equal(TweakScope.Machine, tweak.Scope);
        }

        [Fact]
        public void Check_ValuePresent_ReportsNotApplied()
        {
            var bundle = FakeContextFactory.Create();
            var tweak = StartupItemTweaks.BuildFor(HkcuItem("RingCentral"));
            bundle.Registry.Write(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "RingCentral", @"C:\App\app.exe", RegistryValueKind.String);

            Assert.Equal(TweakState.NotApplied, tweak.Check(bundle.Context));
        }

        [Fact]
        public void Check_ValueAbsent_ReportsApplied()
        {
            var bundle = FakeContextFactory.Create();
            var tweak = StartupItemTweaks.BuildFor(HkcuItem("RingCentral"));

            Assert.Equal(TweakState.Applied, tweak.Check(bundle.Context));
        }

        [Fact]
        public void Apply_RemovesTheValue_AndCanBeUndone()
        {
            var bundle = FakeContextFactory.Create();
            var tweak = StartupItemTweaks.BuildFor(HkcuItem("RingCentral", @"C:\App\app.exe --autostart"));
            bundle.Registry.Write(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "RingCentral", @"C:\App\app.exe --autostart", RegistryValueKind.String);

            var applyResult = tweak.Apply(bundle.Context);
            Assert.Equal(OperationOutcome.Success, applyResult.Outcome);
            Assert.Equal(TweakState.Applied, tweak.Check(bundle.Context));

            var undoResult = tweak.Undo(bundle.Context);
            Assert.Equal(OperationOutcome.Success, undoResult.Outcome);
            Assert.Equal(TweakState.NotApplied, tweak.Check(bundle.Context));
            var restored = bundle.Registry.Read(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "RingCentral");
            Assert.Equal(@"C:\App\app.exe --autostart", restored.Value);
        }

        [Fact]
        public void Apply_DryRun_NeverTouchesTheRegistry()
        {
            var bundle = FakeContextFactory.Create(dryRun: true);
            var tweak = StartupItemTweaks.BuildFor(HkcuItem("RingCentral"));
            bundle.Registry.Write(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "RingCentral", @"C:\App\app.exe", RegistryValueKind.String);

            var result = tweak.Apply(bundle.Context);

            Assert.True(result.WasDryRun);
            Assert.True(bundle.Registry.Read(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "RingCentral").Existed);
        }

        [Fact]
        public void Apply_AlreadyAbsent_IsSkippedNotFailed()
        {
            var bundle = FakeContextFactory.Create();
            var tweak = StartupItemTweaks.BuildFor(HkcuItem("RingCentral"));

            var result = tweak.Apply(bundle.Context);

            Assert.Equal(OperationOutcome.Skipped, result.Outcome);
        }

        [Fact]
        public void Undo_NeverApplied_IsSkippedNotFailed()
        {
            var bundle = FakeContextFactory.Create();
            var tweak = StartupItemTweaks.BuildFor(HkcuItem("RingCentral"));

            var result = tweak.Undo(bundle.Context);

            Assert.Equal(OperationOutcome.Skipped, result.Outcome);
        }

        // ---- Id round-trip / TryResolveFromId - what makes History's undo work in a later session ---

        [Theory]
        [InlineData("startup.hkcu.OneDrive")]
        [InlineData("startup.hklm.SomeVendorHelper")]
        [InlineData("startup.hkcu.Microsoft.Lists")] // a value name that itself contains a dot
        public void TryResolveFromId_RoundTripsRealIds(string id)
        {
            var resolved = StartupItemTweaks.TryResolveFromId(id);

            Assert.NotNull(resolved);
            Assert.Equal(id, resolved.Id);
        }

        [Theory]
        [InlineData("debloat.advertising_id")] // a real, unrelated fixed-catalog id
        [InlineData("startup.")]
        [InlineData("startup.hkcu")] // no value name
        [InlineData("startup.unknownhive.Foo")]
        [InlineData("")]
        [InlineData(null)]
        public void TryResolveFromId_ReturnsNullForAnythingNotAStartupItemId(string id) =>
            Assert.Null(StartupItemTweaks.TryResolveFromId(id));

        [Fact]
        public void ResolvedFromId_HasWorkingCheckApplyUndo_EvenWithoutTheOriginalScanData()
        {
            var bundle = FakeContextFactory.Create();
            bundle.Registry.Write(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "RingCentral", @"C:\App\app.exe", RegistryValueKind.String);

            var resolved = StartupItemTweaks.TryResolveFromId("startup.hkcu.RingCentral");

            Assert.Equal(TweakState.NotApplied, resolved.Check(bundle.Context));
            Assert.Equal(OperationOutcome.Success, resolved.Apply(bundle.Context).Outcome);
            Assert.Equal(OperationOutcome.Success, resolved.Undo(bundle.Context).Outcome);
        }

        [Fact]
        public void TweakRegistry_Find_ResolvesAStartupItemId_EvenThoughItIsNeverInTheFixedCatalog()
        {
            var registry = TweakRegistry.BuildDefault();

            var found = registry.Find("startup.hkcu.SomeApp");

            Assert.NotNull(found);
            Assert.Equal("startup.hkcu.SomeApp", found.Id);
        }

        [Fact]
        public void TweakRegistry_Find_StillReturnsNullForATrulyUnknownId()
        {
            var registry = TweakRegistry.BuildDefault();

            Assert.Null(registry.Find("not.a.real.id"));
        }
    }
}
