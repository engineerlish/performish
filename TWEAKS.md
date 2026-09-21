# Tweak reference

60 tweaks total, generated from the live registry (`Performish.Core.Tweaks.TweakRegistry.BuildDefault()`) - this file is regenerated, not hand-maintained, so it can't drift from the code.

## Debloat (29)

| Id | Name | Risk | Scope | Reboot | Presets | Source |
|---|---|---|---|---|---|---|
| `debloat.tips_notifications` | Disable "get tips and tricks" notifications | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | HKCU\...\ContentDeliveryManager\SubscribedContent-338389Enabled |
| `debloat.cortana` | Disable Cortana | Safe | Machine | no | Conservative, Balanced, Aggressive | HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search\AllowCortana |
| `debloat.lockscreen_ads` | Disable lock screen tips and ads (Windows Spotlight extras) | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | HKCU\...\ContentDeliveryManager\SubscribedContent-338387Enabled |
| `debloat.settings_suggestions` | Disable Settings app suggested content | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | HKCU\...\ContentDeliveryManager\SubscribedContent-338393Enabled |
| `debloat.start_suggestions` | Disable Start menu app suggestions | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | HKCU\...\ContentDeliveryManager\SubscribedContent-338388Enabled (Start suggestions) |
| `debloat.advertising_id` | Disable the advertising ID | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo\Enabled |
| `debloat.bing_search` | Disable web results in Start search | Safe | CurrentUser | no | Balanced, Aggressive | HKCU\Software\Microsoft\Windows\CurrentVersion\Search\BingSearchEnabled |
| `debloat.start_recommendations` | Hide "Recommended" recent files in Start | Safe | CurrentUser | no | Balanced, Aggressive | HKCU\...\Explorer\Advanced\Start_TrackDocs |
| `debloat.chat_icon` | Hide the Chat (Teams) icon on the taskbar | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | HKCU\...\Explorer\Advanced\TaskbarMn (Show Chat icon) |
| `debloat.widgets_button` | Hide the Widgets button on the taskbar | Safe | CurrentUser | no | Balanced, Aggressive | HKCU\...\Explorer\Advanced\TaskbarDa (Show widgets button) |
| `debloat.appx.cortana_app` | Remove app: Cortana | Safe | CurrentUser | no | Balanced, Aggressive | Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal cmdlets) targeting package family Microsoft.549981C3F5F10 |
| `debloat.appx.feedback_hub` | Remove app: Feedback Hub | Safe | CurrentUser | no | Balanced, Aggressive | Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal cmdlets) targeting package family Microsoft.WindowsFeedbackHub |
| `debloat.appx.get_help` | Remove app: Get Help | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal cmdlets) targeting package family Microsoft.GetHelp |
| `debloat.appx.media_player_legacy` | Remove app: Groove Music | Safe | CurrentUser | no | Balanced, Aggressive | Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal cmdlets) targeting package family Microsoft.ZuneMusic |
| `debloat.appx.maps` | Remove app: Maps | Safe | CurrentUser | no | Aggressive | Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal cmdlets) targeting package family Microsoft.WindowsMaps |
| `debloat.appx.movies_tv` | Remove app: Movies & TV | Safe | CurrentUser | no | Balanced, Aggressive | Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal cmdlets) targeting package family Microsoft.ZuneVideo |
| `debloat.appx.bing_news` | Remove app: News | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal cmdlets) targeting package family Microsoft.BingNews |
| `debloat.appx.office_hub` | Remove app: Office (promo hub) | Safe | CurrentUser | no | Balanced, Aggressive | Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal cmdlets) targeting package family Microsoft.MicrosoftOfficeHub |
| `debloat.appx.people` | Remove app: People | Safe | CurrentUser | no | Aggressive | Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal cmdlets) targeting package family Microsoft.People |
| `debloat.appx.solitaire` | Remove app: Solitaire Collection | Safe | CurrentUser | no | Balanced, Aggressive | Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal cmdlets) targeting package family Microsoft.MicrosoftSolitaireCollection |
| `debloat.appx.tips` | Remove app: Tips | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal cmdlets) targeting package family Microsoft.Getstarted |
| `debloat.appx.bing_weather` | Remove app: Weather | Safe | CurrentUser | no | Balanced, Aggressive | Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal cmdlets) targeting package family Microsoft.BingWeather |
| `debloat.telemetry` | Disable diagnostic data collection | Moderate | Machine | no | Balanced, Aggressive | Microsoft policy key: Configure diagnostic data collection (HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection\AllowTelemetry) |
| `debloat.appx.phone_link` | Remove app: Phone Link | Moderate | CurrentUser | no | Aggressive | Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal cmdlets) targeting package family Microsoft.YourPhone |
| `debloat.appx.consumer_teams` | Remove app: Teams (consumer) | Moderate | CurrentUser | no | Balanced, Aggressive | Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal cmdlets) targeting package family MicrosoftTeams |
| `debloat.appx.xbox_console_companion` | Remove app: Xbox Console Companion (legacy) | Moderate | CurrentUser | no | Aggressive | Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal cmdlets) targeting package family Microsoft.XboxApp |
| `debloat.onedrive_startup` | Stop OneDrive from starting automatically | Moderate | CurrentUser | no | Aggressive | HKCU\Software\Microsoft\Windows\CurrentVersion\Run\OneDrive (removes the auto-run entry) |
| `debloat.copilot` | Turn off Windows Copilot | Moderate | CurrentUser | yes | Balanced, Aggressive | HKCU\Software\Policies\Microsoft\Windows\WindowsCopilot\TurnOffWindowsCopilot |
| `debloat.recall` | Disable Windows Recall / AI data analysis | Advanced | Machine | yes | Aggressive | HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI\DisableAIDataAnalysis |

## Performance (13)

| Id | Name | Risk | Scope | Reboot | Presets | Source |
|---|---|---|---|---|---|---|
| `perf.temp_cleanup` | Clean up old temporary files | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | Standard temp-folder cleanup (equivalent to Disk Cleanup's "Temporary files" category) |
| `perf.transparency_effects` | Disable transparency effects | Safe | CurrentUser | no | Balanced, Aggressive | HKCU\...\Themes\Personalize\EnableTransparency |
| `perf.storage_sense_enable` | Enable Storage Sense | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | HKCU\...\StorageSense\Parameters\StoragePolicy\01 (global enable switch) |
| `perf.fast_startup_enable` | Ensure Fast Startup is on | Safe | Machine | yes | Balanced, Aggressive | HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power\HiberbootEnabled |
| `perf.menu_show_delay` | Remove the menu/tooltip open delay | Safe | CurrentUser | no | Balanced, Aggressive | HKCU\Control Panel\Desktop\MenuShowDelay |
| `perf.visual_fx_best_performance` | Set visual effects to "Best performance" | Safe | CurrentUser | no | Aggressive | HKCU\...\Explorer\VisualEffects\VisualFXSetting (2 = best performance) |
| `perf.sysmain_disable` | Disable SysMain (Superfetch) | Moderate | Machine | no | Aggressive | sc.exe config SysMain (SysMain/Superfetch service) |
| `perf.diagtrack_disable` | Disable the Connected User Experiences and Telemetry service | Moderate | Machine | no | Balanced, Aggressive | sc.exe config DiagTrack |
| `perf.gpu_scheduling` | Enable Hardware-accelerated GPU scheduling | Moderate | Machine | yes | Aggressive | HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\HwSchMode (2 = on) |
| `perf.search_indexer_manual` | Set Windows Search to Manual start | Moderate | Machine | no | Aggressive | sc.exe config WSearch (Windows Search service) |
| `perf.background_apps_disable` | Stop apps from running in the background | Moderate | CurrentUser | no | Aggressive | HKCU\...\BackgroundAccessApplications\GlobalUserDisabled |
| `perf.power_high_performance` | Switch to the "High performance" power plan | Moderate | Machine | no | Balanced, Aggressive | powercfg /setactive (built-in "High performance" GUID) |
| `perf.power_ultimate_performance` | Switch to the "Ultimate Performance" power plan | Advanced | Machine | no | Aggressive | powercfg -duplicatescheme (Microsoft's documented well-known Ultimate Performance source GUID) + powercfg /setactive |

## Gaming (9)

| Id | Name | Risk | Scope | Reboot | Presets | Source |
|---|---|---|---|---|---|---|
| `gaming.game_dvr_disable` | Disable Game DVR background recording | Safe | CurrentUser | no | Balanced, Aggressive | HKCU\System\GameConfigStore\GameDVR_Enabled |
| `gaming.mouse_precision_disable` | Disable mouse acceleration ("Enhance pointer precision") | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | HKCU\Control Panel\Mouse\MouseSpeed + MouseThreshold1/2 = "0" (the documented way to disable pointer acceleration, same 3 values the Mouse Properties checkbox writes) |
| `gaming.game_mode_enable` | Enable Game Mode | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled |
| `gaming.xbox_live_auth_manual` | Set Xbox Live Auth Manager to Manual start | Safe | Machine | no | Balanced, Aggressive | sc.exe config XblAuthManager |
| `gaming.xbox_networking_manual` | Set Xbox Live Networking Service to Manual start | Safe | Machine | no | Balanced, Aggressive | sc.exe config XboxNetApiSvc |
| `gaming.game_bar_startup_panel_disable` | Stop the Game Bar "welcome" panel from popping up | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | HKCU\Software\Microsoft\GameBar\ShowStartupPanel |
| `gaming.game_dvr_policy_disable` | Disable Game DVR at the policy level | Moderate | Machine | no | Aggressive | HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR\AllowGameDVR |
| `gaming.priority_separation` | Favor the foreground app's CPU scheduling | Moderate | Machine | no | Aggressive | HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl\Win32PrioritySeparation (0x26) |
| `gaming.fullscreen_optimizations_default_off` | Turn off fullscreen optimizations by default | Moderate | CurrentUser | no | Aggressive | HKCU\System\GameConfigStore\GameDVR_FSEBehaviorMode + GameDVR_HonorUserFSEBehaviorMode |

## Network (4)

| Id | Name | Risk | Scope | Reboot | Presets | Source |
|---|---|---|---|---|---|---|
| `net.nagle_disable` | Disable Nagle's algorithm on all network adapters | Moderate | Machine | no | - | Per-adapter HKLM\...\Tcpip\Parameters\Interfaces\{id}\TcpAckFrequency=1, TcpNoDelay=1 (long-standing documented low-latency network tweak, not a hidden/undocumented key) |
| `net.throttling_disable` | Disable network throttling for multimedia/games | Moderate | Machine | no | - | HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\NetworkThrottlingIndex = 0xFFFFFFFF |
| `net.tcp_timestamps_disable` | Disable TCP timestamps | Moderate | Machine | no | - | netsh int tcp set global timestamps=disabled / =enabled |
| `net.system_responsiveness` | Prioritize multimedia/game threads over background tasks | Moderate | Machine | no | - | HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\SystemResponsiveness |

## Maintenance (5)

| Id | Name | Risk | Scope | Reboot | Presets | Source |
|---|---|---|---|---|---|---|
| `maint.directx_shader_cache` | Clear the DirectX shader cache | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | Standard Disk Cleanup "DirectX Shader Cache" category (available since Windows 10 1809) - the global cache, not any single game's own separate shader cache folder. |
| `maint.thumbnail_cache` | Clear the thumbnail cache | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | Standard Disk Cleanup "Thumbnails" category target folder. |
| `maint.windows_update_cache` | Clear the Windows Update download cache | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | Microsoft-documented: clearing the contents of SoftwareDistribution\Download is the standard, supported way to reclaim space from stale update downloads (equivalent to Disk Cleanup's "Windows Update Cleanup"). |
| `maint.chrome_cache` | Clear the Google Chrome browser cache | Moderate | CurrentUser | no | Balanced, Aggressive | Cache subfolder within Chrome's documented user-data profile layout (User Data\Default\Cache) - the same folder Chrome's own "Clear browsing data" > "Cached images and files" option clears. |
| `maint.edge_cache` | Clear the Microsoft Edge browser cache | Moderate | CurrentUser | no | Balanced, Aggressive | Cache subfolder within Edge's documented user-data profile layout (User Data\Default\Cache) - the same folder Edge's own "Clear browsing data" > "Cached images and files" option clears. |

## Protected - never touched by any tweak

Services: `wuauserv`, `UsoSvc`, `WaaSMedicSvc`, `WinDefend`, `SecurityHealthService`, `wscsvc`, `MpsSvc`, `Dnscache`, `Dhcp`, `NlaSvc`, `netprofm`, `AudioSrv`, `AudioEndpointBuilder`, `PlugPlay`, `DcomLaunch`, `RpcSs`, `BFE`

Appx package prefixes: `Microsoft.WindowsStore`, `Microsoft.StorePurchaseApp`, `Microsoft.DesktopAppInstaller`, `Microsoft.SecHealthUI`, `Microsoft.VCLibs`, `Microsoft.NET.Native`, `Microsoft.UI.Xaml`, `Microsoft.WindowsNotepad`, `Microsoft.Paint`, `Microsoft.ScreenSketch`, `Microsoft.WindowsCalculator`, `Microsoft.WindowsCamera`
