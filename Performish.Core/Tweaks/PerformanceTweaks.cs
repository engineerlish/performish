using System.Collections.Generic;
using Microsoft.Win32;
using Performish.Core.Models;

namespace Performish.Core.Tweaks
{
    /// <summary>Performance library: power plan, visual effects, background apps, startup-impact
    /// services, storage cleanup, and GPU scheduling. Power-plan and temp-cleanup tweaks are
    /// hand-written (not TweakFactory shapes) because their state isn't a single registry value.</summary>
    public static class PerformanceTweaks
    {
        private const int TempCleanupOlderThanDays = 7;

        public static IEnumerable<TweakDefinition> All()
        {
            yield return HighPerformancePlan();
            yield return UltimatePerformancePlan();

            yield return TweakFactory.RegistryDword(
                "perf.visual_fx_best_performance",
                "Set visual effects to \"Best performance\"",
                "Equivalent to System Properties > Performance Options > Adjust for best performance - " +
                "turns off Windows's decorative animations/shadows/fades as a single documented switch.",
                TweakCategory.Performance, RiskLevel.Safe, TweakScope.CurrentUser, false,
                "HKCU\\...\\Explorer\\VisualEffects\\VisualFXSetting (2 = best performance)",
                RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 2,
                new[] { Preset.Aggressive });

            yield return TweakFactory.RegistryDword(
                "perf.transparency_effects",
                "Disable transparency effects",
                "Turns off the frosted-glass transparency used by the taskbar/Start/Settings.",
                TweakCategory.Performance, RiskLevel.Safe, TweakScope.CurrentUser, false,
                "HKCU\\...\\Themes\\Personalize\\EnableTransparency",
                RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0,
                new[] { Preset.Balanced, Preset.Aggressive });

            yield return TweakFactory.RegistryString(
                "perf.menu_show_delay",
                "Remove the menu/tooltip open delay",
                "Sets the delay before a menu expands to 0ms (default 400ms) - snappier navigation, " +
                "no visual effect disabled, purely a wait timer.",
                TweakCategory.Performance, RiskLevel.Safe, TweakScope.CurrentUser, false,
                "HKCU\\Control Panel\\Desktop\\MenuShowDelay",
                RegistryHive.CurrentUser, @"Control Panel\Desktop", "MenuShowDelay", "0",
                new[] { Preset.Balanced, Preset.Aggressive });

            yield return TweakFactory.RegistryDword(
                "perf.background_apps_disable",
                "Stop apps from running in the background",
                "Disables the global \"let apps run in the background\" switch - apps you're not " +
                "actively using stop consuming CPU/network/battery while minimized.",
                TweakCategory.Performance, RiskLevel.Moderate, TweakScope.CurrentUser, false,
                "HKCU\\...\\BackgroundAccessApplications\\GlobalUserDisabled",
                RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1,
                new[] { Preset.Aggressive });

            yield return TweakFactory.RegistryDword(
                "perf.storage_sense_enable",
                "Enable Storage Sense",
                "Turns on automatic cleanup of temporary files and Recycle Bin contents.",
                TweakCategory.Performance, RiskLevel.Safe, TweakScope.CurrentUser, false,
                "HKCU\\...\\StorageSense\\Parameters\\StoragePolicy\\01 (global enable switch)",
                RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy", "01", 1,
                new[] { Preset.Conservative, Preset.Balanced, Preset.Aggressive });

            yield return TweakFactory.RegistryDword(
                "perf.fast_startup_enable",
                "Ensure Fast Startup is on",
                "Explicitly enables hybrid boot (hiberboot) in case an OEM image shipped with it off - " +
                "shortens cold boot time. Has no effect on sleep/hibernate itself.",
                TweakCategory.Performance, RiskLevel.Safe, TweakScope.Machine, true,
                "HKLM\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power\\HiberbootEnabled",
                RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 1,
                new[] { Preset.Balanced, Preset.Aggressive });

            yield return TweakFactory.RegistryDword(
                "perf.gpu_scheduling",
                "Enable Hardware-accelerated GPU scheduling",
                "Lets the GPU manage its own video memory queue instead of a high-priority Windows " +
                "thread - can reduce latency on supported GPU/driver combinations. No effect (and " +
                "harmless) if the GPU/driver doesn't support it.",
                TweakCategory.Performance, RiskLevel.Moderate, TweakScope.Machine, true,
                "HKLM\\SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers\\HwSchMode (2 = on)",
                RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2,
                new[] { Preset.Aggressive });

            yield return TweakFactory.ServiceStartMode(
                "perf.search_indexer_manual",
                "Set Windows Search to Manual start",
                "Search still works on demand, it just doesn't index continuously in the background. " +
                "Start-menu/File Explorer search becomes slower on the first query after boot.",
                TweakCategory.Performance, RiskLevel.Moderate, TweakScope.Machine, false,
                "sc.exe config WSearch (Windows Search service)",
                "WSearch", System.ServiceProcess.ServiceStartMode.Manual, stopWhenDisabling: false,
                presets: new[] { Preset.Aggressive });

            yield return TweakFactory.ServiceStartMode(
                "perf.sysmain_disable",
                "Disable SysMain (Superfetch)",
                "Stops the service that preloads frequently-used apps into RAM. Commonly recommended " +
                "off on SSDs with ample RAM; can help on some systems and do nothing on others.",
                TweakCategory.Performance, RiskLevel.Moderate, TweakScope.Machine, false,
                "sc.exe config SysMain (SysMain/Superfetch service)",
                "SysMain", System.ServiceProcess.ServiceStartMode.Disabled,
                presets: new[] { Preset.Aggressive });

            yield return TweakFactory.ServiceStartMode(
                "perf.diagtrack_disable",
                "Disable the Connected User Experiences and Telemetry service",
                "Stops the DiagTrack service, which collects and uploads diagnostic data in the " +
                "background. Complements the diagnostic-data-collection policy tweak.",
                TweakCategory.Performance, RiskLevel.Moderate, TweakScope.Machine, false,
                "sc.exe config DiagTrack",
                "DiagTrack", System.ServiceProcess.ServiceStartMode.Disabled,
                presets: new[] { Preset.Balanced, Preset.Aggressive });

            yield return TempCleanupTweak();
        }

        private static TweakDefinition HighPerformancePlan()
        {
            // Well-known built-in scheme GUID Windows ships for "High performance".
            const string highPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

            TweakState Check(TweakExecutionContext ctx) =>
                ctx.Power.GetActivePlanGuid() == highPerformanceGuid ? TweakState.Applied : TweakState.NotApplied;

            TweakOperationResult Apply(TweakExecutionContext ctx)
            {
                const string descr = "Switch the active power plan to \"High performance\"";
                if (ctx.DryRun) return TweakOperationResult.Preview(descr);

                ctx.UndoStore.Save("perf.power_high_performance", ctx.Power.GetActivePlanGuid());
                ctx.Power.SetActivePlan(highPerformanceGuid);
                return TweakOperationResult.Success(descr);
            }

            TweakOperationResult Undo(TweakExecutionContext ctx)
            {
                const string descr = "Restore the previously-active power plan";
                if (ctx.DryRun) return TweakOperationResult.Preview(descr);

                if (!ctx.UndoStore.Has("perf.power_high_performance"))
                    return TweakOperationResult.Skipped("No prior plan recorded - was this tweak ever applied by Performish?");

                var previous = ctx.UndoStore.Load<string>("perf.power_high_performance");
                if (!string.IsNullOrEmpty(previous)) ctx.Power.SetActivePlan(previous);
                return TweakOperationResult.Success(descr);
            }

            return new TweakDefinition(
                "perf.power_high_performance", "Switch to the \"High performance\" power plan",
                "Windows's built-in maximum-performance plan - higher idle power draw and heat, " +
                "shorter battery life on laptops than Balanced.",
                TweakCategory.Performance, RiskLevel.Moderate, TweakScope.Machine, false,
                "powercfg /setactive (built-in \"High performance\" GUID)",
                Check, Apply, Undo, new[] { Preset.Balanced, Preset.Aggressive });
        }

        private static TweakDefinition UltimatePerformancePlan()
        {
            TweakState Check(TweakExecutionContext ctx)
            {
                var active = ctx.Power.GetActivePlanGuid();
                var plans = ctx.Power.ListPlans();
                foreach (var p in plans)
                    if (p.Guid == active && p.Name.IndexOf("Ultimate Performance", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return TweakState.Applied;
                return TweakState.NotApplied;
            }

            TweakOperationResult Apply(TweakExecutionContext ctx)
            {
                const string descr = "Import (if needed) and switch to the hidden \"Ultimate Performance\" power plan";
                if (ctx.DryRun) return TweakOperationResult.Preview(descr);

                ctx.UndoStore.Save("perf.power_ultimate_performance", ctx.Power.GetActivePlanGuid());
                var guid = ctx.Power.EnsureUltimatePerformancePlanImported();
                if (string.IsNullOrEmpty(guid))
                    return TweakOperationResult.Failed("Could not import the Ultimate Performance plan.");
                ctx.Power.SetActivePlan(guid);
                return TweakOperationResult.Success(descr);
            }

            TweakOperationResult Undo(TweakExecutionContext ctx)
            {
                const string descr = "Restore the previously-active power plan";
                if (ctx.DryRun) return TweakOperationResult.Preview(descr);

                if (!ctx.UndoStore.Has("perf.power_ultimate_performance"))
                    return TweakOperationResult.Skipped("No prior plan recorded - was this tweak ever applied by Performish?");

                var previous = ctx.UndoStore.Load<string>("perf.power_ultimate_performance");
                if (!string.IsNullOrEmpty(previous)) ctx.Power.SetActivePlan(previous);
                return TweakOperationResult.Success(descr);
            }

            return new TweakDefinition(
                "perf.power_ultimate_performance", "Switch to the \"Ultimate Performance\" power plan",
                "Removes remaining micro-latency power-saving parks Windows applies even under High " +
                "performance. Desktop-recommended: on a laptop this meaningfully shortens battery life " +
                "for a gain that is rarely perceptible outside of benchmarks.",
                TweakCategory.Performance, RiskLevel.Advanced, TweakScope.Machine, false,
                "powercfg -duplicatescheme (Microsoft's documented well-known Ultimate Performance " +
                "source GUID) + powercfg /setactive",
                Check, Apply, Undo, new[] { Preset.Aggressive });
        }

        private static TweakDefinition TempCleanupTweak() => TweakFactory.SpaceCleanup(
            "perf.temp_cleanup", "Clean up old temporary files",
            $"Deletes files older than {TempCleanupOlderThanDays} days from %TEMP% and " +
            "C:\\Windows\\Temp. Skips (does not fail on) locked/in-use files.",
            TweakCategory.Performance, RiskLevel.Safe,
            "Standard temp-folder cleanup (equivalent to Disk Cleanup's \"Temporary files\" category)",
            () => new[]
            {
                System.Environment.GetEnvironmentVariable("TEMP"),
                System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows), "Temp")
            },
            TempCleanupOlderThanDays,
            new[] { Preset.Conservative, Preset.Balanced, Preset.Aggressive });
    }
}
