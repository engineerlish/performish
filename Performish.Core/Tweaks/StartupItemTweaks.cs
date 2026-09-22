using System;
using Microsoft.Win32;
using Performish.Core.Backends;
using Performish.Core.Models;
using Performish.Core.Scanner;

namespace Performish.Core.Tweaks
{
    /// <summary>Turns a scanned startup item (Performish.Core.Scanner.StartupItemInfo - a live HKCU/
    /// HKLM ...\CurrentVersion\Run value) into a real, undoable TweakDefinition, on demand - unlike
    /// every other tweak library, these aren't a fixed catalog: the set of startup items is different
    /// on every machine, discovered fresh by each scan (see HEALTH_ANALYSIS.md - the biggest single
    /// fixable factor found in the health-score audit was third-party startup items with no existing
    /// tweak to address them).
    ///
    /// Never included in any preset (IncludedInPresets is always empty) - a preset is a fixed,
    /// reviewed set of changes; "disable RingCentral" is user- and machine-specific and always needs
    /// its own explicit pick, matching the task's "recommend-first, requires explicit confirmation"
    /// rule for anything that isn't an obviously-safe, reviewed default.
    ///
    /// The tweak id encodes exactly what Apply/Undo/Check need (which hive, which value name) so a
    /// tweak can be reconstructed purely from its id with no other state - see TryResolveFromId(),
    /// which is what lets History's per-tweak undo (TweakRegistry.Find(id)) work for a startup-item
    /// tweak even in a brand new app session, long after the dialog that first showed it is closed.</summary>
    public static class StartupItemTweaks
    {
        private const string IdPrefix = "startup.";
        private const string HkcuTag = "hkcu";
        private const string HklmTag = "hklm";
        private const string HkcuRunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string HklmRunPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        public static TweakDefinition BuildFor(StartupItemInfo item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            var hive = item.Source == "HKLM Run" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
            return Build(hive, item.Name, item.Command);
        }

        /// <summary>Reconstructs a startup-item tweak purely from its id, with no scan/UI state
        /// needed - the id alone is enough to know which registry value Check/Apply/Undo target. The
        /// rebuilt tweak's Title/Description are generic (the original Command text isn't recoverable
        /// from the id alone), which only matters for a History-dialog undo long after the fact - Undo
        /// itself restores the exact original value regardless, from the on-disk snapshot Apply() saved.
        /// Returns null for any id that isn't a startup-item id (the normal case for every other
        /// tweak) - TweakRegistry.Find() falls back to this only when its own fixed list misses.</summary>
        public static TweakDefinition TryResolveFromId(string id)
        {
            if (string.IsNullOrEmpty(id) || !id.StartsWith(IdPrefix, StringComparison.Ordinal)) return null;

            var rest = id.Substring(IdPrefix.Length); // e.g. "hkcu.OneDrive" or "hklm.Some.Vendor.Task"
            var dot = rest.IndexOf('.');
            if (dot < 0) return null;

            var hiveTag = rest.Substring(0, dot);
            var valueName = rest.Substring(dot + 1);
            if (valueName.Length == 0) return null;

            RegistryHive hive;
            if (hiveTag == HkcuTag) hive = RegistryHive.CurrentUser;
            else if (hiveTag == HklmTag) hive = RegistryHive.LocalMachine;
            else return null;

            return Build(hive, valueName, command: null);
        }

        public static string BuildId(RegistryHive hive, string valueName) =>
            IdPrefix + (hive == RegistryHive.LocalMachine ? HklmTag : HkcuTag) + "." + valueName;

        private static TweakDefinition Build(RegistryHive hive, string valueName, string command)
        {
            var id = BuildId(hive, valueName);
            var subKeyPath = hive == RegistryHive.LocalMachine ? HklmRunPath : HkcuRunPath;
            var scope = hive == RegistryHive.LocalMachine ? TweakScope.Machine : TweakScope.CurrentUser;

            TweakState Check(TweakExecutionContext ctx)
            {
                var snap = ctx.Registry.Read(hive, subKeyPath, valueName);
                return snap.Existed ? TweakState.NotApplied : TweakState.Applied;
            }

            TweakOperationResult Apply(TweakExecutionContext ctx)
            {
                const string descrBase = "Remove this entry from Windows startup";
                if (ctx.DryRun) return TweakOperationResult.Preview(descrBase);

                var before = ctx.Registry.Read(hive, subKeyPath, valueName);
                if (!before.Existed)
                    return TweakOperationResult.Skipped("Already not set to start automatically.");

                ctx.UndoStore.Save(id, before);
                ctx.Registry.Delete(hive, subKeyPath, valueName);
                return TweakOperationResult.Success(descrBase + " - it can still be launched manually.");
            }

            TweakOperationResult Undo(TweakExecutionContext ctx)
            {
                const string descr = "Restore this item to Windows startup";
                if (ctx.DryRun) return TweakOperationResult.Preview(descr);

                if (!ctx.UndoStore.Has(id))
                    return TweakOperationResult.Skipped("No prior snapshot recorded - was this ever disabled by Performish?");

                ctx.Registry.Restore(ctx.UndoStore.Load<RegistryValueSnapshot>(id));
                return TweakOperationResult.Success(descr);
            }

            var description = command != null
                ? $"Stops '{valueName}' from launching automatically at sign-in (command: {command}). " +
                  "The app itself is not uninstalled or blocked - only its automatic launch entry is removed."
                : $"Stops '{valueName}' from launching automatically at sign-in. " +
                  "The app itself is not uninstalled or blocked - only its automatic launch entry is removed.";

            return new TweakDefinition(
                id,
                $"Stop '{valueName}' from starting automatically",
                description,
                TweakCategory.Performance,
                RiskLevel.Moderate, // discovered per-machine, not a reviewed catalog entry - never auto-selected
                scope,
                rebootRequired: false,
                source: $"This machine's own startup list: {(hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU")}" +
                    @"\...\CurrentVersion\Run - not a fixed catalog entry.",
                Check, Apply, Undo,
                includedInPresets: null, // Custom-only, always an explicit individual pick
                isProtected: false);
        }
    }
}
