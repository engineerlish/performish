using System;
using System.Linq;
using Microsoft.Win32;
using Performish.Core.Backends;
using Performish.Core.Models;

namespace Performish.Core.Tweaks
{
    /// <summary>Reusable builders for the common tweak shapes (a registry DWORD toggle, a service
    /// start-mode change, a scheduled task enable/disable, an Appx removal). Every tweak library file
    /// composes tweaks out of these instead of hand-writing Check/Apply/Undo closures each time, which
    /// is what keeps "tweaks as data" true in practice - a new tweak is one method call with data, not
    /// new control flow.</summary>
    public static class TweakFactory
    {
        // ---- Registry DWORD toggle --------------------------------------------------------------

        /// <summary>A tweak whose "applied" state is exactly one DWORD registry value equaling
        /// <paramref name="desiredValue"/>. Apply snapshots the prior value/absence for Undo.</summary>
        public static TweakDefinition RegistryDword(
            string id, string name, string description, TweakCategory category, RiskLevel risk,
            TweakScope scope, bool rebootRequired, string source,
            RegistryHive hive, string subKeyPath, string valueName, int desiredValue,
            Preset[] presets = null, bool isProtected = false)
        {
            TweakState Check(TweakExecutionContext ctx)
            {
                var snap = ctx.Registry.Read(hive, subKeyPath, valueName);
                if (!snap.Existed) return TweakState.NotApplied;
                return (snap.Value is int i && i == desiredValue) ? TweakState.Applied : TweakState.NotApplied;
            }

            TweakOperationResult Apply(TweakExecutionContext ctx)
            {
                var before = ctx.Registry.Read(hive, subKeyPath, valueName);
                var description2 = $"Set {hive}\\{subKeyPath} [{valueName}] = {desiredValue} (DWORD)";
                if (ctx.DryRun) return TweakOperationResult.Preview(description2);

                ctx.UndoStore.Save(id, before);
                ctx.Registry.EnsureKey(hive, subKeyPath);
                ctx.Registry.Write(hive, subKeyPath, valueName, desiredValue, RegistryValueKind.DWord);
                return TweakOperationResult.Success(description2);
            }

            TweakOperationResult Undo(TweakExecutionContext ctx)
            {
                var description2 = $"Restore {hive}\\{subKeyPath} [{valueName}] to its previous value";
                if (ctx.DryRun) return TweakOperationResult.Preview(description2);

                if (!ctx.UndoStore.Has(id))
                    return TweakOperationResult.Skipped("No prior snapshot recorded - was this tweak ever applied by Performish?");

                var snap = ctx.UndoStore.Load<RegistryValueSnapshot>(id);
                ctx.Registry.Restore(snap);
                return TweakOperationResult.Success(description2);
            }

            return new TweakDefinition(id, name, description, category, risk, scope, rebootRequired, source,
                Check, Apply, Undo, presets, isProtected);
        }

        // ---- Registry REG_SZ toggle ---------------------------------------------------------------

        /// <summary>Same shape as RegistryDword but for the handful of legacy values (menu/tooltip
        /// delays, some Explorer settings) that Windows still stores as a string of digits rather
        /// than a real DWORD.</summary>
        public static TweakDefinition RegistryString(
            string id, string name, string description, TweakCategory category, RiskLevel risk,
            TweakScope scope, bool rebootRequired, string source,
            RegistryHive hive, string subKeyPath, string valueName, string desiredValue,
            Preset[] presets = null, bool isProtected = false)
        {
            TweakState Check(TweakExecutionContext ctx)
            {
                var snap = ctx.Registry.Read(hive, subKeyPath, valueName);
                if (!snap.Existed) return TweakState.NotApplied;
                return (snap.Value as string) == desiredValue ? TweakState.Applied : TweakState.NotApplied;
            }

            TweakOperationResult Apply(TweakExecutionContext ctx)
            {
                var descr = $"Set {hive}\\{subKeyPath} [{valueName}] = \"{desiredValue}\" (string)";
                if (ctx.DryRun) return TweakOperationResult.Preview(descr);

                var before = ctx.Registry.Read(hive, subKeyPath, valueName);
                ctx.UndoStore.Save(id, before);
                ctx.Registry.EnsureKey(hive, subKeyPath);
                ctx.Registry.Write(hive, subKeyPath, valueName, desiredValue, RegistryValueKind.String);
                return TweakOperationResult.Success(descr);
            }

            TweakOperationResult Undo(TweakExecutionContext ctx)
            {
                var descr = $"Restore {hive}\\{subKeyPath} [{valueName}] to its previous value";
                if (ctx.DryRun) return TweakOperationResult.Preview(descr);

                if (!ctx.UndoStore.Has(id))
                    return TweakOperationResult.Skipped("No prior snapshot recorded - was this tweak ever applied by Performish?");

                var snap = ctx.UndoStore.Load<RegistryValueSnapshot>(id);
                ctx.Registry.Restore(snap);
                return TweakOperationResult.Success(descr);
            }

            return new TweakDefinition(id, name, description, category, risk, scope, rebootRequired, source,
                Check, Apply, Undo, presets, isProtected);
        }

        // ---- Service start mode ------------------------------------------------------------------

        public static TweakDefinition ServiceStartMode(
            string id, string name, string description, TweakCategory category, RiskLevel risk,
            TweakScope scope, bool rebootRequired, string source,
            string serviceName, System.ServiceProcess.ServiceStartMode desiredMode, bool stopWhenDisabling = true,
            Preset[] presets = null, bool isProtected = false)
        {
            TweakState Check(TweakExecutionContext ctx)
            {
                if (!ctx.Services.Exists(serviceName)) return TweakState.NotApplicable;
                var snap = ctx.Services.Read(serviceName);
                return snap.StartMode == desiredMode ? TweakState.Applied : TweakState.NotApplied;
            }

            TweakOperationResult Apply(TweakExecutionContext ctx)
            {
                var descr = $"Set service '{serviceName}' start type to {desiredMode}" + (stopWhenDisabling && desiredMode == System.ServiceProcess.ServiceStartMode.Disabled ? " and stop it" : "");
                if (ctx.DryRun) return TweakOperationResult.Preview(descr);

                if (!ctx.Services.Exists(serviceName))
                    return TweakOperationResult.Skipped($"Service '{serviceName}' not present on this system.");

                var before = ctx.Services.Read(serviceName);
                ctx.UndoStore.Save(id, before);
                ctx.Services.SetStartMode(serviceName, desiredMode);
                if (stopWhenDisabling && desiredMode == System.ServiceProcess.ServiceStartMode.Disabled)
                    ctx.Services.Stop(serviceName);
                return TweakOperationResult.Success(descr);
            }

            TweakOperationResult Undo(TweakExecutionContext ctx)
            {
                var descr = $"Restore service '{serviceName}' to its previous start type and running state";
                if (ctx.DryRun) return TweakOperationResult.Preview(descr);

                if (!ctx.UndoStore.Has(id))
                    return TweakOperationResult.Skipped("No prior snapshot recorded - was this tweak ever applied by Performish?");

                var snap = ctx.UndoStore.Load<ServiceSnapshot>(id);
                ctx.Services.Restore(snap);
                return TweakOperationResult.Success(descr);
            }

            return new TweakDefinition(id, name, description, category, risk, scope, rebootRequired, source,
                Check, Apply, Undo, presets, isProtected);
        }

        // ---- Scheduled task enable/disable -------------------------------------------------------

        public static TweakDefinition ScheduledTask(
            string id, string name, string description, TweakCategory category, RiskLevel risk,
            TweakScope scope, bool rebootRequired, string source,
            string taskPath, bool desiredEnabled,
            Preset[] presets = null, bool isProtected = false)
        {
            TweakState Check(TweakExecutionContext ctx)
            {
                if (!ctx.Tasks.Exists(taskPath)) return TweakState.NotApplicable;
                var snap = ctx.Tasks.Read(taskPath);
                return snap.WasEnabled == desiredEnabled ? TweakState.Applied : TweakState.NotApplied;
            }

            TweakOperationResult Apply(TweakExecutionContext ctx)
            {
                var descr = $"{(desiredEnabled ? "Enable" : "Disable")} scheduled task '{taskPath}'";
                if (ctx.DryRun) return TweakOperationResult.Preview(descr);

                if (!ctx.Tasks.Exists(taskPath))
                    return TweakOperationResult.Skipped($"Scheduled task '{taskPath}' not present on this system.");

                var before = ctx.Tasks.Read(taskPath);
                ctx.UndoStore.Save(id, before);
                ctx.Tasks.SetEnabled(taskPath, desiredEnabled);
                return TweakOperationResult.Success(descr);
            }

            TweakOperationResult Undo(TweakExecutionContext ctx)
            {
                var descr = $"Restore scheduled task '{taskPath}' to its previous enabled state";
                if (ctx.DryRun) return TweakOperationResult.Preview(descr);

                if (!ctx.UndoStore.Has(id))
                    return TweakOperationResult.Skipped("No prior snapshot recorded - was this tweak ever applied by Performish?");

                var snap = ctx.UndoStore.Load<ScheduledTaskSnapshot>(id);
                ctx.Tasks.Restore(snap);
                return TweakOperationResult.Success(descr);
            }

            return new TweakDefinition(id, name, description, category, risk, scope, rebootRequired, source,
                Check, Apply, Undo, presets, isProtected);
        }

        // ---- Space cleanup (delete-old-files-under-a-path, e.g. caches/leftovers) -----------------

        /// <summary>A tweak that deletes files older than <paramref name="olderThanDays"/> under one
        /// or more known-safe paths and reports space freed. Generalizes what was originally a single
        /// hand-written temp-cleanup tweak (see PerformanceTweaks.TempCleanupTweak) so every
        /// Maintenance-category cleanup tweak shares one implementation. Undo is always Skipped -
        /// deleted files cannot come back, and every caller's description says so up front rather than
        /// silently no-op'ing. Check() is always NotApplicable for the same reason a completed action
        /// (not a persistent state) has no "is this currently applied" meaning.</summary>
        public static TweakDefinition SpaceCleanup(
            string id, string name, string description, TweakCategory category, RiskLevel risk, string source,
            Func<string[]> resolvePaths, int olderThanDays,
            Preset[] presets = null, bool isProtected = false)
        {
            TweakOperationResult Apply(TweakExecutionContext ctx)
            {
                var descr = $"Delete files older than {olderThanDays} day(s) under: " + string.Join(", ", resolvePaths());
                if (ctx.DryRun) return TweakOperationResult.Preview(descr);

                long totalFreed = 0;
                int totalDeleted = 0, totalSkipped = 0, pathsFound = 0;

                foreach (var path in resolvePaths())
                {
                    if (string.IsNullOrEmpty(path) || !ctx.FileSystem.DirectoryExists(path)) continue;
                    pathsFound++;
                    totalFreed += ctx.FileSystem.CleanOldFiles(path, olderThanDays, out var deleted, out var skipped);
                    totalDeleted += deleted;
                    totalSkipped += skipped;
                }

                if (pathsFound == 0)
                    return TweakOperationResult.Skipped("None of the target folders exist on this system - nothing to clean.");

                return TweakOperationResult.Success(
                    $"Freed {totalFreed / 1024 / 1024} MB across {totalDeleted} file(s) ({totalSkipped} skipped - in use or access denied).");
            }

            TweakOperationResult Undo(TweakExecutionContext ctx) =>
                TweakOperationResult.Skipped("Deleted files cannot be restored - there is nothing to undo. " +
                    $"This tweak only ever deletes files older than {olderThanDays} day(s) under its documented target folder(s).");

            return new TweakDefinition(id, name, description, category, risk, TweakScope.CurrentUser, false, source,
                _ => TweakState.NotApplicable, Apply, Undo, presets, isProtected);
        }

        // ---- External command pair (netsh/powercfg toggles with no clean registry read) -----------

        /// <summary>For the handful of tweaks whose real interface is an external command
        /// (netsh, bcdedit) rather than the registry, where a reliable Check() would mean parsing
        /// version-specific CLI text output. Deliberately returns TweakState.Unknown from Check
        /// rather than a guessed parse - the UI shows "Unknown / re-run to confirm" instead of
        /// silently misreporting state. Apply/Undo carry no live queryable state to snapshot -
        /// the undo command is the fixed inverse of the apply command, defined up front.</summary>
        public static TweakDefinition ProcessCommandPair(
            string id, string name, string description, TweakCategory category, RiskLevel risk,
            TweakScope scope, bool rebootRequired, string source,
            string applyFileName, string applyArguments,
            string undoFileName, string undoArguments,
            Preset[] presets = null, bool isProtected = false)
        {
            TweakOperationResult Apply(TweakExecutionContext ctx)
            {
                var descr = $"Run: {applyFileName} {applyArguments}";
                if (ctx.DryRun) return TweakOperationResult.Preview(descr);

                var result = ctx.Process.Run(applyFileName, applyArguments);
                return result.ExitCode == 0
                    ? TweakOperationResult.Success(descr)
                    : TweakOperationResult.Failed($"{descr} - exit code {result.ExitCode}: {result.StandardError}");
            }

            TweakOperationResult Undo(TweakExecutionContext ctx)
            {
                var descr = $"Run: {undoFileName} {undoArguments}";
                if (ctx.DryRun) return TweakOperationResult.Preview(descr);

                var result = ctx.Process.Run(undoFileName, undoArguments);
                return result.ExitCode == 0
                    ? TweakOperationResult.Success(descr)
                    : TweakOperationResult.Failed($"{descr} - exit code {result.ExitCode}: {result.StandardError}");
            }

            return new TweakDefinition(id, name, description, category, risk, scope, rebootRequired, source,
                _ => TweakState.Unknown, Apply, Undo, presets, isProtected);
        }

        // ---- Appx removal (per-user + deprovision) ------------------------------------------------

        public static TweakDefinition AppxRemove(
            string id, string name, string description, TweakCategory category, RiskLevel risk,
            string source, string packageFamilyPrefix, bool alsoDeprovision = true,
            Preset[] presets = null, bool isProtected = false)
        {
            TweakState Check(TweakExecutionContext ctx)
            {
                var snap = ctx.Appx.Read(packageFamilyPrefix);
                var stillPresent = snap.InstalledForCurrentUser || (alsoDeprovision && snap.Provisioned);
                return stillPresent ? TweakState.NotApplied : TweakState.Applied;
            }

            TweakOperationResult Apply(TweakExecutionContext ctx)
            {
                var descr = $"Remove Appx package(s) matching '{packageFamilyPrefix}*' for the current user" +
                    (alsoDeprovision ? ", and deprovision so it doesn't reinstall for new profiles" : "");
                if (ctx.DryRun) return TweakOperationResult.Preview(descr);

                var before = ctx.Appx.Read(packageFamilyPrefix);
                if (!before.InstalledForCurrentUser && !before.Provisioned)
                    return TweakOperationResult.Skipped($"'{packageFamilyPrefix}*' is not installed or provisioned - nothing to do.");

                ctx.UndoStore.Save(id, before);
                if (before.InstalledForCurrentUser) ctx.Appx.RemoveForCurrentUser(packageFamilyPrefix);
                if (alsoDeprovision && before.Provisioned) ctx.Appx.Deprovision(packageFamilyPrefix);
                return TweakOperationResult.Success(descr);
            }

            TweakOperationResult Undo(TweakExecutionContext ctx)
            {
                var descr = $"Reinstall '{packageFamilyPrefix}*' for the current user (deprovisioning cannot be reversed - " +
                    "Windows does not keep the original package after Remove-AppxProvisionedPackage, so re-provisioning for " +
                    "new user profiles is not possible; only the current user's install is restored)";
                if (ctx.DryRun) return TweakOperationResult.Preview(descr);

                if (!ctx.UndoStore.Has(id))
                    return TweakOperationResult.Skipped("No prior snapshot recorded - was this tweak ever applied by Performish?");

                var snap = ctx.UndoStore.Load<AppxSnapshot>(id);
                if (snap.InstalledForCurrentUser) ctx.Appx.ReinstallForCurrentUser(packageFamilyPrefix);
                return TweakOperationResult.Success(descr);
            }

            return new TweakDefinition(id, name, description, category, risk, TweakScope.CurrentUser, false, source,
                Check, Apply, Undo, presets, isProtected);
        }
    }
}
