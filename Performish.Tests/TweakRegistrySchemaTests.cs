using System;
using System.Collections.Generic;
using Microsoft.Win32;
using Performish.Core.Models;
using Performish.Core.Tweaks;
using Xunit;

namespace Performish.Tests
{
    /// <summary>Schema validation for TweakDefinition/TweakRegistry - catches a malformed tweak at
    /// load time with a clear, tweak-naming error message instead of letting it surface later as a
    /// confusing null-reference or "why is this tweak invisible" bug (added per the Balanced-preset
    /// bug fix's regression-test requirement, even though the actual bug here was a UI threading
    /// issue, not a schema one - see DECISIONS.md).</summary>
    public class TweakRegistrySchemaTests
    {
        private static TweakDefinition MakeValidTweak(string id = "t.valid") => TweakFactory.RegistryDword(
            id, "Valid tweak", "A valid description", TweakCategory.Debloat, RiskLevel.Safe, TweakScope.CurrentUser, false,
            "Valid source", RegistryHive.CurrentUser, @"Software\Test", "Value", 1);

        [Fact]
        public void BuildDefault_DoesNotThrow()
        {
            var ex = Record.Exception(() => TweakRegistry.BuildDefault());
            Assert.Null(ex);
        }

        [Fact]
        public void DuplicateId_ThrowsAndNamesTheId()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                new TweakRegistry(new[] { MakeValidTweak("dup.id"), MakeValidTweak("dup.id") }));

            Assert.Contains("dup.id", ex.Message);
        }

        [Fact]
        public void EmptyName_ThrowsAndNamesTheTweak()
        {
            var bad = new TweakDefinition("t.badname", "", "desc", TweakCategory.Debloat, RiskLevel.Safe,
                TweakScope.CurrentUser, false, "source", _ => TweakState.Unknown,
                _ => TweakOperationResult.Success("ok"), _ => TweakOperationResult.Skipped("n/a"));

            var ex = Assert.Throws<InvalidOperationException>(() => new TweakRegistry(new[] { bad }));

            Assert.Contains("t.badname", ex.Message);
            Assert.Contains("Name", ex.Message);
        }

        [Fact]
        public void EmptyDescription_ThrowsAndNamesTheTweak()
        {
            var bad = new TweakDefinition("t.baddesc", "Name", "  ", TweakCategory.Debloat, RiskLevel.Safe,
                TweakScope.CurrentUser, false, "source", _ => TweakState.Unknown,
                _ => TweakOperationResult.Success("ok"), _ => TweakOperationResult.Skipped("n/a"));

            var ex = Assert.Throws<InvalidOperationException>(() => new TweakRegistry(new[] { bad }));

            Assert.Contains("t.baddesc", ex.Message);
            Assert.Contains("Description", ex.Message);
        }

        [Fact]
        public void EmptySource_ThrowsAndNamesTheTweak()
        {
            var bad = new TweakDefinition("t.badsource", "Name", "desc", TweakCategory.Debloat, RiskLevel.Safe,
                TweakScope.CurrentUser, false, null, _ => TweakState.Unknown,
                _ => TweakOperationResult.Success("ok"), _ => TweakOperationResult.Skipped("n/a"));

            var ex = Assert.Throws<InvalidOperationException>(() => new TweakRegistry(new[] { bad }));

            Assert.Contains("t.badsource", ex.Message);
            Assert.Contains("Source", ex.Message);
        }

        [Fact]
        public void UndefinedRiskLevel_ThrowsAndNamesTheTweak()
        {
            var bad = new TweakDefinition("t.badrisk", "Name", "desc", TweakCategory.Debloat, (RiskLevel)99,
                TweakScope.CurrentUser, false, "source", _ => TweakState.Unknown,
                _ => TweakOperationResult.Success("ok"), _ => TweakOperationResult.Skipped("n/a"));

            var ex = Assert.Throws<InvalidOperationException>(() => new TweakRegistry(new[] { bad }));

            Assert.Contains("t.badrisk", ex.Message);
            Assert.Contains("RiskLevel", ex.Message);
        }

        [Fact]
        public void UndefinedCategory_ThrowsAndNamesTheTweak()
        {
            var bad = new TweakDefinition("t.badcategory", "Name", "desc", (TweakCategory)99, RiskLevel.Safe,
                TweakScope.CurrentUser, false, "source", _ => TweakState.Unknown,
                _ => TweakOperationResult.Success("ok"), _ => TweakOperationResult.Skipped("n/a"));

            var ex = Assert.Throws<InvalidOperationException>(() => new TweakRegistry(new[] { bad }));

            Assert.Contains("t.badcategory", ex.Message);
            Assert.Contains("TweakCategory", ex.Message);
        }

        [Fact]
        public void UndefinedScope_ThrowsAndNamesTheTweak()
        {
            var bad = new TweakDefinition("t.badscope", "Name", "desc", TweakCategory.Debloat, RiskLevel.Safe,
                (TweakScope)99, false, "source", _ => TweakState.Unknown,
                _ => TweakOperationResult.Success("ok"), _ => TweakOperationResult.Skipped("n/a"));

            var ex = Assert.Throws<InvalidOperationException>(() => new TweakRegistry(new[] { bad }));

            Assert.Contains("t.badscope", ex.Message);
            Assert.Contains("TweakScope", ex.Message);
        }

        [Fact]
        public void PresetCustomInIncludedInPresets_ThrowsAndExplainsWhy()
        {
            var bad = new TweakDefinition("t.custompreset", "Name", "desc", TweakCategory.Debloat, RiskLevel.Safe,
                TweakScope.CurrentUser, false, "source", _ => TweakState.Unknown,
                _ => TweakOperationResult.Success("ok"), _ => TweakOperationResult.Skipped("n/a"),
                new[] { Preset.Custom });

            var ex = Assert.Throws<InvalidOperationException>(() => new TweakRegistry(new[] { bad }));

            Assert.Contains("t.custompreset", ex.Message);
            Assert.Contains("Preset.Custom", ex.Message);
        }

        [Fact]
        public void UndefinedPresetValue_ThrowsAndNamesTheTweak()
        {
            var bad = new TweakDefinition("t.badpreset", "Name", "desc", TweakCategory.Debloat, RiskLevel.Safe,
                TweakScope.CurrentUser, false, "source", _ => TweakState.Unknown,
                _ => TweakOperationResult.Success("ok"), _ => TweakOperationResult.Skipped("n/a"),
                new[] { (Preset)99 });

            var ex = Assert.Throws<InvalidOperationException>(() => new TweakRegistry(new[] { bad }));

            Assert.Contains("t.badpreset", ex.Message);
        }

        [Fact]
        public void MultipleErrors_AreAllReported_NotJustTheFirst()
        {
            var bad1 = new TweakDefinition("t.bad1", "", "desc", TweakCategory.Debloat, RiskLevel.Safe,
                TweakScope.CurrentUser, false, "source", _ => TweakState.Unknown,
                _ => TweakOperationResult.Success("ok"), _ => TweakOperationResult.Skipped("n/a"));
            var bad2 = new TweakDefinition("t.bad2", "Name", "", TweakCategory.Debloat, RiskLevel.Safe,
                TweakScope.CurrentUser, false, "source", _ => TweakState.Unknown,
                _ => TweakOperationResult.Success("ok"), _ => TweakOperationResult.Skipped("n/a"));

            var ex = Assert.Throws<InvalidOperationException>(() => new TweakRegistry(new[] { bad1, bad2 }));

            Assert.Contains("t.bad1", ex.Message);
            Assert.Contains("t.bad2", ex.Message);
        }

        [Fact]
        public void ValidTweak_DoesNotThrow()
        {
            var ex = Record.Exception(() => new TweakRegistry(new[] { MakeValidTweak() }));
            Assert.Null(ex);
        }
    }
}
