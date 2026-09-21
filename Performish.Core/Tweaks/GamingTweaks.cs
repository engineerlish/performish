using System.Collections.Generic;
using Microsoft.Win32;
using Performish.Core.Backends;
using Performish.Core.Models;

namespace Performish.Core.Tweaks
{
    /// <summary>Gaming/FPS library: Game Mode, Game DVR/Game Bar overhead, fullscreen optimizations,
    /// mouse input, and foreground-process scheduling priority. Nothing here touches a driver, hooks
    /// another process's memory, or does anything an anti-cheat system would flag - every tweak is a
    /// standard, documented Windows setting also exposed somewhere in Settings/Group Policy.</summary>
    public static class GamingTweaks
    {
        public static IEnumerable<TweakDefinition> All()
        {
            yield return TweakFactory.RegistryDword(
                "gaming.game_mode_enable",
                "Enable Game Mode",
                "Ensures Windows's Game Mode (deprioritizes background work while a game has focus) is on.",
                TweakCategory.Gaming, RiskLevel.Safe, TweakScope.CurrentUser, false,
                "HKCU\\Software\\Microsoft\\GameBar\\AutoGameModeEnabled",
                RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1,
                new[] { Preset.Conservative, Preset.Balanced, Preset.Aggressive });

            yield return TweakFactory.RegistryDword(
                "gaming.game_dvr_disable",
                "Disable Game DVR background recording",
                "Turns off the always-on background game-clip recording buffer Game Bar keeps while " +
                "you play - a commonly measurable FPS/frame-time cost on lower-end GPUs.",
                TweakCategory.Gaming, RiskLevel.Safe, TweakScope.CurrentUser, false,
                "HKCU\\System\\GameConfigStore\\GameDVR_Enabled",
                RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 0,
                new[] { Preset.Balanced, Preset.Aggressive });

            yield return TweakFactory.RegistryDword(
                "gaming.game_dvr_policy_disable",
                "Block Game DVR at the system level",
                "Machine-wide policy backing the per-user Game DVR toggle, so it can't be silently " +
                "re-enabled by a per-user setting change or an app.",
                TweakCategory.Gaming, RiskLevel.Moderate, TweakScope.Machine, false,
                "HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\GameDVR\\AllowGameDVR",
                RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0,
                new[] { Preset.Aggressive });

            yield return TweakFactory.RegistryDword(
                "gaming.game_bar_startup_panel_disable",
                "Stop the Game Bar welcome popup",
                "Suppresses the \"Do you want to open Xbox Game Bar?\" prompt Windows shows the first " +
                "time it detects a full-screen app. Game Bar itself, and Game Mode, are unaffected.",
                TweakCategory.Gaming, RiskLevel.Safe, TweakScope.CurrentUser, false,
                "HKCU\\Software\\Microsoft\\GameBar\\ShowStartupPanel",
                RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "ShowStartupPanel", 0,
                new[] { Preset.Conservative, Preset.Balanced, Preset.Aggressive });

            yield return TweakFactory.RegistryDword(
                "gaming.priority_separation",
                "Favor the foreground app's CPU scheduling",
                "Sets Win32PrioritySeparation to short, fixed quantums with a foreground boost - the " +
                "documented System Properties > Advanced > Performance Options > \"Programs\" setting, " +
                "just set directly. Affects scheduling for every process, not only games.",
                TweakCategory.Gaming, RiskLevel.Moderate, TweakScope.Machine, false,
                "HKLM\\SYSTEM\\CurrentControlSet\\Control\\PriorityControl\\Win32PrioritySeparation (0x26)",
                RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 38,
                new[] { Preset.Aggressive });

            yield return TweakFactory.ServiceStartMode(
                "gaming.xbox_networking_manual",
                "Delay the Xbox networking service",
                "Still starts on demand when a game needs it; stops it idling in the background between " +
                "sessions. Xbox Game Bar, Game Mode, and controller support are unaffected.",
                TweakCategory.Gaming, RiskLevel.Safe, TweakScope.Machine, false,
                "sc.exe config XboxNetApiSvc",
                "XboxNetApiSvc", System.ServiceProcess.ServiceStartMode.Manual, stopWhenDisabling: false,
                presets: new[] { Preset.Balanced, Preset.Aggressive });

            yield return TweakFactory.ServiceStartMode(
                "gaming.xbox_live_auth_manual",
                "Delay the Xbox sign-in service",
                "Same idea as the Xbox networking service tweak - on-demand instead of always running.",
                TweakCategory.Gaming, RiskLevel.Safe, TweakScope.Machine, false,
                "sc.exe config XblAuthManager",
                "XblAuthManager", System.ServiceProcess.ServiceStartMode.Manual, stopWhenDisabling: false,
                presets: new[] { Preset.Balanced, Preset.Aggressive });

            yield return FullscreenOptimizationsTweak();
            yield return MousePrecisionTweak();
        }

        private static TweakDefinition FullscreenOptimizationsTweak()
        {
            const string subKey = @"System\GameConfigStore";
            const string id = "gaming.fullscreen_optimizations_default_off";

            TweakState Check(TweakExecutionContext ctx)
            {
                var mode = ctx.Registry.Read(RegistryHive.CurrentUser, subKey, "GameDVR_FSEBehaviorMode");
                var honor = ctx.Registry.Read(RegistryHive.CurrentUser, subKey, "GameDVR_HonorUserFSEBehaviorMode");
                var applied = mode.Existed && mode.Value is int mv && mv == 2
                    && honor.Existed && honor.Value is int hv && hv == 1;
                return applied ? TweakState.Applied : TweakState.NotApplied;
            }

            TweakOperationResult Apply(TweakExecutionContext ctx)
            {
                const string descr = "Set fullscreen optimizations' default to OFF for new games (per-game override in " +
                    "each game's Properties > Compatibility still wins if the user sets one)";
                if (ctx.DryRun) return TweakOperationResult.Preview(descr);

                var beforeMode = ctx.Registry.Read(RegistryHive.CurrentUser, subKey, "GameDVR_FSEBehaviorMode");
                var beforeHonor = ctx.Registry.Read(RegistryHive.CurrentUser, subKey, "GameDVR_HonorUserFSEBehaviorMode");
                ctx.UndoStore.Save(id + ".mode", beforeMode);
                ctx.UndoStore.Save(id + ".honor", beforeHonor);

                ctx.Registry.EnsureKey(RegistryHive.CurrentUser, subKey);
                ctx.Registry.Write(RegistryHive.CurrentUser, subKey, "GameDVR_FSEBehaviorMode", 2, RegistryValueKind.DWord);
                ctx.Registry.Write(RegistryHive.CurrentUser, subKey, "GameDVR_HonorUserFSEBehaviorMode", 1, RegistryValueKind.DWord);
                return TweakOperationResult.Success(descr);
            }

            TweakOperationResult Undo(TweakExecutionContext ctx)
            {
                const string descr = "Restore fullscreen optimizations to their previous values";
                if (ctx.DryRun) return TweakOperationResult.Preview(descr);

                if (!ctx.UndoStore.Has(id + ".mode"))
                    return TweakOperationResult.Skipped("No prior snapshot recorded - was this tweak ever applied by Performish?");

                ctx.Registry.Restore(ctx.UndoStore.Load<RegistryValueSnapshot>(id + ".mode"));
                ctx.Registry.Restore(ctx.UndoStore.Load<RegistryValueSnapshot>(id + ".honor"));
                return TweakOperationResult.Success(descr);
            }

            return new TweakDefinition(id, "Turn off fullscreen optimizations",
                "Some games render measurably better in true exclusive fullscreen than under Windows's " +
                "compositor-managed \"fullscreen optimizations\". This changes the system default; any " +
                "individual game can still be overridden per-title.",
                TweakCategory.Gaming, RiskLevel.Moderate, TweakScope.CurrentUser, false,
                "HKCU\\System\\GameConfigStore\\GameDVR_FSEBehaviorMode + GameDVR_HonorUserFSEBehaviorMode",
                Check, Apply, Undo, new[] { Preset.Aggressive });
        }

        private static TweakDefinition MousePrecisionTweak()
        {
            const string subKey = @"Control Panel\Mouse";
            const string id = "gaming.mouse_precision_disable";
            var valueNames = new[] { "MouseSpeed", "MouseThreshold1", "MouseThreshold2" };

            TweakState Check(TweakExecutionContext ctx)
            {
                foreach (var v in valueNames)
                {
                    var snap = ctx.Registry.Read(RegistryHive.CurrentUser, subKey, v);
                    if (!snap.Existed || (snap.Value as string) != "0") return TweakState.NotApplied;
                }
                return TweakState.Applied;
            }

            TweakOperationResult Apply(TweakExecutionContext ctx)
            {
                const string descr = "Disable \"Enhance pointer precision\" (mouse acceleration) - raw 1:1 mouse input";
                if (ctx.DryRun) return TweakOperationResult.Preview(descr);

                foreach (var v in valueNames)
                {
                    var before = ctx.Registry.Read(RegistryHive.CurrentUser, subKey, v);
                    ctx.UndoStore.Save(id + "." + v, before);
                    ctx.Registry.EnsureKey(RegistryHive.CurrentUser, subKey);
                    ctx.Registry.Write(RegistryHive.CurrentUser, subKey, v, "0", RegistryValueKind.String);
                }
                return TweakOperationResult.Success(descr);
            }

            TweakOperationResult Undo(TweakExecutionContext ctx)
            {
                const string descr = "Restore previous mouse acceleration settings";
                if (ctx.DryRun) return TweakOperationResult.Preview(descr);

                if (!ctx.UndoStore.Has(id + "." + valueNames[0]))
                    return TweakOperationResult.Skipped("No prior snapshot recorded - was this tweak ever applied by Performish?");

                foreach (var v in valueNames)
                    ctx.Registry.Restore(ctx.UndoStore.Load<RegistryValueSnapshot>(id + "." + v));
                return TweakOperationResult.Success(descr);
            }

            return new TweakDefinition(id, "Disable mouse pointer acceleration",
                "Windows's non-linear mouse acceleration curve makes aim inconsistent at different " +
                "movement speeds - most competitive players turn it off in favor of a flat sensitivity.",
                TweakCategory.Gaming, RiskLevel.Safe, TweakScope.CurrentUser, false,
                "HKCU\\Control Panel\\Mouse\\MouseSpeed + MouseThreshold1/2 = \"0\" (the documented way " +
                "to disable pointer acceleration, same 3 values the Mouse Properties checkbox writes)",
                Check, Apply, Undo, new[] { Preset.Conservative, Preset.Balanced, Preset.Aggressive });
        }
    }
}
