using System.Collections.Generic;
using Microsoft.Win32;
using Performish.Core.Models;

namespace Performish.Core.Tweaks
{
    /// <summary>Debloat library: telemetry/ads/suggestions/Widgets/Copilot/Recall/Search, plus Appx
    /// removal for common OEM/consumer bloat apps. Every registry path here is a documented Group
    /// Policy-backed or long-standing user-preference key (see each Source string) - none of these
    /// are undocumented/reverse-engineered values.</summary>
    public static class DebloatTweaks
    {
        public static IEnumerable<TweakDefinition> All()
        {
            yield return TweakFactory.RegistryDword(
                "debloat.telemetry",
                "Disable diagnostic data collection",
                "Sets telemetry to the minimum level Windows exposes via policy (\"Security\"/\"Basic\" " +
                "depending on edition). Has no effect on Windows Update's own function.",
                TweakCategory.Debloat, RiskLevel.Moderate, TweakScope.Machine, false,
                "Microsoft policy key: Configure diagnostic data collection " +
                "(HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection\\AllowTelemetry)",
                RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0,
                new[] { Preset.Balanced, Preset.Aggressive });

            yield return TweakFactory.RegistryDword(
                "debloat.advertising_id",
                "Disable advertising ID tracking",
                "Stops apps from being given a per-user ID for personalized ads across apps.",
                TweakCategory.Debloat, RiskLevel.Safe, TweakScope.CurrentUser, false,
                "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\AdvertisingInfo\\Enabled",
                RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0,
                new[] { Preset.Conservative, Preset.Balanced, Preset.Aggressive });

            yield return TweakFactory.RegistryDword(
                "debloat.start_suggestions",
                "Disable Start menu app suggestions",
                "Turns off \"Show suggestions occasionally in Start\" (suggested/sponsored apps).",
                TweakCategory.Debloat, RiskLevel.Safe, TweakScope.CurrentUser, false,
                "HKCU\\...\\ContentDeliveryManager\\SubscribedContent-338388Enabled (Start suggestions)",
                RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338388Enabled", 0,
                new[] { Preset.Conservative, Preset.Balanced, Preset.Aggressive });

            yield return TweakFactory.RegistryDword(
                "debloat.settings_suggestions",
                "Disable Settings app suggested content",
                "Turns off promotional tips/suggestions shown inside the Settings app.",
                TweakCategory.Debloat, RiskLevel.Safe, TweakScope.CurrentUser, false,
                "HKCU\\...\\ContentDeliveryManager\\SubscribedContent-338393Enabled",
                RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338393Enabled", 0,
                new[] { Preset.Conservative, Preset.Balanced, Preset.Aggressive });

            yield return TweakFactory.RegistryDword(
                "debloat.tips_notifications",
                "Disable \"get tips and tricks\" notifications",
                "Turns off Windows-generated welcome-experience and tips notifications.",
                TweakCategory.Debloat, RiskLevel.Safe, TweakScope.CurrentUser, false,
                "HKCU\\...\\ContentDeliveryManager\\SubscribedContent-338389Enabled",
                RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338389Enabled", 0,
                new[] { Preset.Conservative, Preset.Balanced, Preset.Aggressive });

            yield return TweakFactory.RegistryDword(
                "debloat.lockscreen_ads",
                "Disable lock screen tips and ads",
                "Turns off promotional overlays on the lock screen while leaving Spotlight's own " +
                "background rotation untouched.",
                TweakCategory.Debloat, RiskLevel.Safe, TweakScope.CurrentUser, false,
                "HKCU\\...\\ContentDeliveryManager\\SubscribedContent-338387Enabled",
                RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338387Enabled", 0,
                new[] { Preset.Conservative, Preset.Balanced, Preset.Aggressive });

            yield return TweakFactory.RegistryDword(
                "debloat.widgets_button",
                "Hide the Widgets button",
                "Removes the Widgets icon from the taskbar. The Widgets board itself remains present " +
                "in Windows, only the entry point is hidden.",
                TweakCategory.Debloat, RiskLevel.Safe, TweakScope.CurrentUser, false,
                "HKCU\\...\\Explorer\\Advanced\\TaskbarDa (Show widgets button)",
                RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa", 0,
                new[] { Preset.Balanced, Preset.Aggressive });

            yield return TweakFactory.RegistryDword(
                "debloat.chat_icon",
                "Hide the Teams chat icon",
                "Removes the consumer Chat/Teams icon from the taskbar.",
                TweakCategory.Debloat, RiskLevel.Safe, TweakScope.CurrentUser, false,
                "HKCU\\...\\Explorer\\Advanced\\TaskbarMn (Show Chat icon)",
                RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarMn", 0,
                new[] { Preset.Conservative, Preset.Balanced, Preset.Aggressive });

            yield return TweakFactory.RegistryDword(
                "debloat.copilot",
                "Turn off Windows Copilot",
                "Disables the Copilot app/entry point via the documented policy key. Fully enforced " +
                "on Pro/Enterprise; on Home this hides the taskbar entry point where supported.",
                TweakCategory.Debloat, RiskLevel.Moderate, TweakScope.CurrentUser, true,
                "HKCU\\Software\\Policies\\Microsoft\\Windows\\WindowsCopilot\\TurnOffWindowsCopilot",
                RegistryHive.CurrentUser, @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1,
                new[] { Preset.Balanced, Preset.Aggressive });

            yield return TweakFactory.RegistryDword(
                "debloat.recall",
                "Disable Windows Recall snapshots",
                "Turns off Recall's snapshotting on systems where the feature is present, via the " +
                "documented Windows AI policy key.",
                TweakCategory.Debloat, RiskLevel.Advanced, TweakScope.Machine, true,
                "HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsAI\\DisableAIDataAnalysis",
                RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1,
                new[] { Preset.Aggressive });

            yield return TweakFactory.RegistryDword(
                "debloat.bing_search",
                "Disable web results in Start search",
                "Keeps Start search local-only (apps, files, settings) instead of also querying Bing.",
                TweakCategory.Debloat, RiskLevel.Safe, TweakScope.CurrentUser, false,
                "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Search\\BingSearchEnabled",
                RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0,
                new[] { Preset.Balanced, Preset.Aggressive });

            yield return TweakFactory.RegistryDword(
                "debloat.cortana",
                "Disable Cortana search integration",
                "Disables Cortana via the documented Windows Search policy key.",
                TweakCategory.Debloat, RiskLevel.Safe, TweakScope.Machine, false,
                "HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\Windows Search\\AllowCortana",
                RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", 0,
                new[] { Preset.Conservative, Preset.Balanced, Preset.Aggressive });

            yield return TweakFactory.RegistryDword(
                "debloat.start_recommendations",
                "Hide recommended files in Start menu",
                "Turns off recently-opened-file recommendations shown in the Start menu.",
                TweakCategory.Debloat, RiskLevel.Safe, TweakScope.CurrentUser, false,
                "HKCU\\...\\Explorer\\Advanced\\Start_TrackDocs",
                RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_TrackDocs", 0,
                new[] { Preset.Balanced, Preset.Aggressive });

            yield return TweakFactory.RegistryDword(
                "debloat.onedrive_startup",
                "Stop OneDrive from starting automatically",
                "Prevents the OneDrive client from auto-launching at sign-in. Does not uninstall " +
                "OneDrive or touch any files already syncing.",
                TweakCategory.Debloat, RiskLevel.Moderate, TweakScope.CurrentUser, false,
                "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\\OneDrive (removes the auto-run entry)",
                RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "OneDrive", 0,
                new[] { Preset.Aggressive });

            // ---- Appx removal: common consumer/OEM bloat, per-user + deprovision -----------------
            // None of these overlap ProtectedList.AppxPackagePrefixes.

            foreach (var app in AppxCandidates)
            {
                yield return TweakFactory.AppxRemove(
                    $"debloat.appx.{app.Slug}",
                    app.Title,
                    app.Description,
                    TweakCategory.Debloat, app.Risk,
                    "Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal " +
                    "cmdlets) targeting package family " + app.Prefix,
                    app.Prefix,
                    alsoDeprovision: true,
                    presets: app.Presets);
            }
        }

        private sealed class AppxCandidate
        {
            public string Slug;
            public string Prefix;
            public string DisplayName;
            /// <summary>The tweak's Title - a short, action-led "Remove X app" phrase, distinct from
            /// DisplayName (used in Description prose) so the title reads naturally rather than as
            /// "Remove app: X".</summary>
            public string Title;
            public string Description;
            public RiskLevel Risk;
            public Preset[] Presets;
        }

        private static readonly AppxCandidate[] AppxCandidates =
        {
            new AppxCandidate { Slug = "cortana_app", Prefix = "Microsoft.549981C3F5F10", DisplayName = "Cortana",
                Title = "Remove the Cortana app",
                Description = "Standalone Cortana app (separate from the Cortana search integration tweak).",
                Risk = RiskLevel.Safe, Presets = new[] { Preset.Balanced, Preset.Aggressive } },

            new AppxCandidate { Slug = "bing_news", Prefix = "Microsoft.BingNews", DisplayName = "News",
                Title = "Remove the News app",
                Description = "Microsoft/Bing News app.", Risk = RiskLevel.Safe,
                Presets = new[] { Preset.Conservative, Preset.Balanced, Preset.Aggressive } },

            new AppxCandidate { Slug = "bing_weather", Prefix = "Microsoft.BingWeather", DisplayName = "Weather",
                Title = "Remove the Weather app",
                Description = "Microsoft/Bing Weather app.", Risk = RiskLevel.Safe,
                Presets = new[] { Preset.Balanced, Preset.Aggressive } },

            new AppxCandidate { Slug = "get_help", Prefix = "Microsoft.GetHelp", DisplayName = "Get Help",
                Title = "Remove the Get Help app",
                Description = "Microsoft support app.", Risk = RiskLevel.Safe,
                Presets = new[] { Preset.Conservative, Preset.Balanced, Preset.Aggressive } },

            new AppxCandidate { Slug = "tips", Prefix = "Microsoft.Getstarted", DisplayName = "Tips",
                Title = "Remove the Windows Tips app",
                Description = "Windows \"Tips\" / Get Started app.", Risk = RiskLevel.Safe,
                Presets = new[] { Preset.Conservative, Preset.Balanced, Preset.Aggressive } },

            new AppxCandidate { Slug = "office_hub", Prefix = "Microsoft.MicrosoftOfficeHub", DisplayName = "Office (promo hub)",
                Title = "Remove the Office promo app",
                Description = "The promotional \"Office\" app that links to Office.com/install prompts - not real Office.",
                Risk = RiskLevel.Safe, Presets = new[] { Preset.Balanced, Preset.Aggressive } },

            new AppxCandidate { Slug = "solitaire", Prefix = "Microsoft.MicrosoftSolitaireCollection", DisplayName = "Solitaire Collection",
                Title = "Remove Solitaire Collection",
                Description = "Microsoft Solitaire Collection (ad-supported).", Risk = RiskLevel.Safe,
                Presets = new[] { Preset.Balanced, Preset.Aggressive } },

            new AppxCandidate { Slug = "people", Prefix = "Microsoft.People", DisplayName = "People",
                Title = "Remove the People contacts app",
                Description = "The People contacts app.", Risk = RiskLevel.Safe,
                Presets = new[] { Preset.Aggressive } },

            new AppxCandidate { Slug = "feedback_hub", Prefix = "Microsoft.WindowsFeedbackHub", DisplayName = "Feedback Hub",
                Title = "Remove the Feedback Hub app",
                Description = "Windows Feedback Hub.", Risk = RiskLevel.Safe,
                Presets = new[] { Preset.Balanced, Preset.Aggressive } },

            new AppxCandidate { Slug = "maps", Prefix = "Microsoft.WindowsMaps", DisplayName = "Maps",
                Title = "Remove the Maps app",
                Description = "Windows Maps app.", Risk = RiskLevel.Safe,
                Presets = new[] { Preset.Aggressive } },

            new AppxCandidate { Slug = "phone_link", Prefix = "Microsoft.YourPhone", DisplayName = "Phone Link",
                Title = "Remove the Phone Link app",
                Description = "Phone Link (Your Phone) companion app.", Risk = RiskLevel.Moderate,
                Presets = new[] { Preset.Aggressive } },

            new AppxCandidate { Slug = "media_player_legacy", Prefix = "Microsoft.ZuneMusic", DisplayName = "Groove Music",
                Title = "Remove the Groove Music app",
                Description = "Legacy Groove Music app (distinct from the modern Media Player).", Risk = RiskLevel.Safe,
                Presets = new[] { Preset.Balanced, Preset.Aggressive } },

            new AppxCandidate { Slug = "movies_tv", Prefix = "Microsoft.ZuneVideo", DisplayName = "Movies & TV",
                Title = "Remove the Movies & TV app",
                Description = "Movies & TV app.", Risk = RiskLevel.Safe,
                Presets = new[] { Preset.Balanced, Preset.Aggressive } },

            new AppxCandidate { Slug = "consumer_teams", Prefix = "MicrosoftTeams", DisplayName = "Teams (consumer)",
                Title = "Remove the consumer Teams app",
                Description = "Consumer Teams chat app (not Teams for work/school, which installs separately).",
                Risk = RiskLevel.Moderate, Presets = new[] { Preset.Balanced, Preset.Aggressive } },

            new AppxCandidate { Slug = "xbox_console_companion", Prefix = "Microsoft.XboxApp", DisplayName = "Xbox Console Companion (legacy)",
                Title = "Remove the legacy Xbox Companion app",
                Description = "Legacy Xbox Console Companion app - superseded by the modern Xbox app. " +
                    "Game Bar and Game Mode (Performance/Gaming tweaks) are unaffected.",
                Risk = RiskLevel.Moderate, Presets = new[] { Preset.Aggressive } },
        };
    }
}
