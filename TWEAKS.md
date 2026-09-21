# Tweak reference

60 tweaks total, generated from the live registry (`Performish.Core.Tweaks.TweakRegistry.BuildDefault()`) - this file is regenerated, not hand-maintained, so it can't drift from the code.

## Debloat (29)

| Id | Title | Risk | Scope | Reboot | Presets | Source |
|---|---|---|---|---|---|---|
| `debloat.advertising_id` | Disable advertising ID tracking | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo\Enabled |
| `debloat.appx.bing_news` | Remove the News app | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal cmdlets) targeting package family Microsoft.BingNews |
| `debloat.appx.bing_weather` | Remove the Weather app | Safe | CurrentUser | no | Balanced, Aggressive | Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal cmdlets) targeting package family Microsoft.BingWeather |
| `debloat.appx.consumer_teams` | Remove the consumer Teams app | Moderate | CurrentUser | no | Balanced, Aggressive | Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal cmdlets) targeting package family MicrosoftTeams |
| `debloat.appx.cortana_app` | Remove the Cortana app | Safe | CurrentUser | no | Balanced, Aggressive | Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal cmdlets) targeting package family Microsoft.549981C3F5F10 |
| `debloat.appx.feedback_hub` | Remove the Feedback Hub app | Safe | CurrentUser | no | Balanced, Aggressive | Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal cmdlets) targeting package family Microsoft.WindowsFeedbackHub |
| `debloat.appx.get_help` | Remove the Get Help app | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal cmdlets) targeting package family Microsoft.GetHelp |
| `debloat.appx.maps` | Remove the Maps app | Safe | CurrentUser | no | Aggressive | Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal cmdlets) targeting package family Microsoft.WindowsMaps |
| `debloat.appx.media_player_legacy` | Remove the Groove Music app | Safe | CurrentUser | no | Balanced, Aggressive | Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal cmdlets) targeting package family Microsoft.ZuneMusic |
| `debloat.appx.movies_tv` | Remove the Movies & TV app | Safe | CurrentUser | no | Balanced, Aggressive | Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal cmdlets) targeting package family Microsoft.ZuneVideo |
| `debloat.appx.office_hub` | Remove the Office promo app | Safe | CurrentUser | no | Balanced, Aggressive | Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal cmdlets) targeting package family Microsoft.MicrosoftOfficeHub |
| `debloat.appx.people` | Remove the People contacts app | Safe | CurrentUser | no | Aggressive | Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal cmdlets) targeting package family Microsoft.People |
| `debloat.appx.phone_link` | Remove the Phone Link app | Moderate | CurrentUser | no | Aggressive | Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal cmdlets) targeting package family Microsoft.YourPhone |
| `debloat.appx.solitaire` | Remove Solitaire Collection | Safe | CurrentUser | no | Balanced, Aggressive | Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal cmdlets) targeting package family Microsoft.MicrosoftSolitaireCollection |
| `debloat.appx.tips` | Remove the Windows Tips app | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal cmdlets) targeting package family Microsoft.Getstarted |
| `debloat.appx.xbox_console_companion` | Remove the legacy Xbox Companion app | Moderate | CurrentUser | no | Aggressive | Remove-AppxPackage / Remove-AppxProvisionedPackage (standard Windows Appx removal cmdlets) targeting package family Microsoft.XboxApp |
| `debloat.bing_search` | Disable web results in Start search | Safe | CurrentUser | no | Balanced, Aggressive | HKCU\Software\Microsoft\Windows\CurrentVersion\Search\BingSearchEnabled |
| `debloat.chat_icon` | Hide the Teams chat icon | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | HKCU\...\Explorer\Advanced\TaskbarMn (Show Chat icon) |
| `debloat.copilot` | Turn off Windows Copilot | Moderate | CurrentUser | yes | Balanced, Aggressive | HKCU\Software\Policies\Microsoft\Windows\WindowsCopilot\TurnOffWindowsCopilot |
| `debloat.cortana` | Disable Cortana search integration | Safe | Machine | no | Conservative, Balanced, Aggressive | HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search\AllowCortana |
| `debloat.lockscreen_ads` | Disable lock screen tips and ads | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | HKCU\...\ContentDeliveryManager\SubscribedContent-338387Enabled |
| `debloat.onedrive_startup` | Stop OneDrive from starting automatically | Moderate | CurrentUser | no | Aggressive | HKCU\Software\Microsoft\Windows\CurrentVersion\Run\OneDrive (removes the auto-run entry) |
| `debloat.recall` | Disable Windows Recall snapshots | Advanced | Machine | yes | Aggressive | HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI\DisableAIDataAnalysis |
| `debloat.settings_suggestions` | Disable Settings app suggested content | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | HKCU\...\ContentDeliveryManager\SubscribedContent-338393Enabled |
| `debloat.start_recommendations` | Hide recommended files in Start menu | Safe | CurrentUser | no | Balanced, Aggressive | HKCU\...\Explorer\Advanced\Start_TrackDocs |
| `debloat.start_suggestions` | Disable Start menu app suggestions | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | HKCU\...\ContentDeliveryManager\SubscribedContent-338388Enabled (Start suggestions) |
| `debloat.telemetry` | Disable diagnostic data collection | Moderate | Machine | no | Balanced, Aggressive | Microsoft policy key: Configure diagnostic data collection (HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection\AllowTelemetry) |
| `debloat.tips_notifications` | Disable "get tips and tricks" notifications | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | HKCU\...\ContentDeliveryManager\SubscribedContent-338389Enabled |
| `debloat.widgets_button` | Hide the Widgets button | Safe | CurrentUser | no | Balanced, Aggressive | HKCU\...\Explorer\Advanced\TaskbarDa (Show widgets button) |

## Performance (13)

| Id | Title | Risk | Scope | Reboot | Presets | Source |
|---|---|---|---|---|---|---|
| `perf.background_apps_disable` | Stop apps from running in the background | Moderate | CurrentUser | no | Aggressive | HKCU\...\BackgroundAccessApplications\GlobalUserDisabled |
| `perf.diagtrack_disable` | Stop the telemetry tracking service | Moderate | Machine | no | Balanced, Aggressive | sc.exe config DiagTrack |
| `perf.fast_startup_enable` | Turn on Fast Startup | Safe | Machine | yes | Balanced, Aggressive | HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power\HiberbootEnabled |
| `perf.gpu_scheduling` | Enable Hardware-accelerated GPU scheduling | Moderate | Machine | yes | Aggressive | HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\HwSchMode (2 = on) |
| `perf.menu_show_delay` | Remove menu and tooltip delay | Safe | CurrentUser | no | Balanced, Aggressive | HKCU\Control Panel\Desktop\MenuShowDelay |
| `perf.power_high_performance` | Switch to High Performance power plan | Moderate | Machine | no | Balanced, Aggressive | powercfg /setactive (built-in "High performance" GUID) |
| `perf.power_ultimate_performance` | Switch to Ultimate Performance power plan | Advanced | Machine | no | Aggressive | powercfg -duplicatescheme (Microsoft's documented well-known Ultimate Performance source GUID) + powercfg /setactive |
| `perf.search_indexer_manual` | Set Windows Search to manual start | Moderate | Machine | no | Aggressive | sc.exe config WSearch (Windows Search service) |
| `perf.storage_sense_enable` | Enable Storage Sense | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | HKCU\...\StorageSense\Parameters\StoragePolicy\01 (global enable switch) |
| `perf.sysmain_disable` | Disable Superfetch memory pre-loading | Moderate | Machine | no | Aggressive | sc.exe config SysMain (SysMain/Superfetch service) |
| `perf.temp_cleanup` | Clean up old temporary files | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | Standard temp-folder cleanup (equivalent to Disk Cleanup's "Temporary files" category) |
| `perf.transparency_effects` | Disable transparency effects | Safe | CurrentUser | no | Balanced, Aggressive | HKCU\...\Themes\Personalize\EnableTransparency |
| `perf.visual_fx_best_performance` | Set visual effects for best performance | Safe | CurrentUser | no | Aggressive | HKCU\...\Explorer\VisualEffects\VisualFXSetting (2 = best performance) |

## Gaming (9)

| Id | Title | Risk | Scope | Reboot | Presets | Source |
|---|---|---|---|---|---|---|
| `gaming.fullscreen_optimizations_default_off` | Turn off fullscreen optimizations | Moderate | CurrentUser | no | Aggressive | HKCU\System\GameConfigStore\GameDVR_FSEBehaviorMode + GameDVR_HonorUserFSEBehaviorMode |
| `gaming.game_bar_startup_panel_disable` | Stop the Game Bar welcome popup | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | HKCU\Software\Microsoft\GameBar\ShowStartupPanel |
| `gaming.game_dvr_disable` | Disable Game DVR background recording | Safe | CurrentUser | no | Balanced, Aggressive | HKCU\System\GameConfigStore\GameDVR_Enabled |
| `gaming.game_dvr_policy_disable` | Block Game DVR at the system level | Moderate | Machine | no | Aggressive | HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR\AllowGameDVR |
| `gaming.game_mode_enable` | Enable Game Mode | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled |
| `gaming.mouse_precision_disable` | Disable mouse pointer acceleration | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | HKCU\Control Panel\Mouse\MouseSpeed + MouseThreshold1/2 = "0" (the documented way to disable pointer acceleration, same 3 values the Mouse Properties checkbox writes) |
| `gaming.priority_separation` | Favor the foreground app's CPU scheduling | Moderate | Machine | no | Aggressive | HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl\Win32PrioritySeparation (0x26) |
| `gaming.xbox_live_auth_manual` | Delay the Xbox sign-in service | Safe | Machine | no | Balanced, Aggressive | sc.exe config XblAuthManager |
| `gaming.xbox_networking_manual` | Delay the Xbox networking service | Safe | Machine | no | Balanced, Aggressive | sc.exe config XboxNetApiSvc |

## Network (4)

| Id | Title | Risk | Scope | Reboot | Presets | Source |
|---|---|---|---|---|---|---|
| `net.nagle_disable` | Disable Nagle's algorithm for lower latency | Moderate | Machine | no | (none - Custom only) | Per-adapter HKLM\...\Tcpip\Parameters\Interfaces\{id}\TcpAckFrequency=1, TcpNoDelay=1 (long-standing documented low-latency network tweak, not a hidden/undocumented key) |
| `net.system_responsiveness` | Prioritize game threads over background tasks | Moderate | Machine | no | (none - Custom only) | HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\SystemResponsiveness |
| `net.tcp_timestamps_disable` | Disable TCP timestamps | Moderate | Machine | no | (none - Custom only) | netsh int tcp set global timestamps=disabled / =enabled |
| `net.throttling_disable` | Disable network throttling during gaming | Moderate | Machine | no | (none - Custom only) | HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\NetworkThrottlingIndex = 0xFFFFFFFF |

## Maintenance (5)

| Id | Title | Risk | Scope | Reboot | Presets | Source |
|---|---|---|---|---|---|---|
| `maint.chrome_cache` | Clear the Google Chrome browser cache | Moderate | CurrentUser | no | Balanced, Aggressive | Cache subfolder within Chrome's documented user-data profile layout (User Data\Default\Cache) - the same folder Chrome's own "Clear browsing data" > "Cached images and files" option clears. |
| `maint.directx_shader_cache` | Clear the DirectX shader cache | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | Standard Disk Cleanup "DirectX Shader Cache" category (available since Windows 10 1809) - the global cache, not any single game's own separate shader cache folder. |
| `maint.edge_cache` | Clear the Microsoft Edge browser cache | Moderate | CurrentUser | no | Balanced, Aggressive | Cache subfolder within Edge's documented user-data profile layout (User Data\Default\Cache) - the same folder Edge's own "Clear browsing data" > "Cached images and files" option clears. |
| `maint.thumbnail_cache` | Clear the thumbnail cache | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | Standard Disk Cleanup "Thumbnails" category target folder. |
| `maint.windows_update_cache` | Clear the Windows Update download cache | Safe | CurrentUser | no | Conservative, Balanced, Aggressive | Microsoft-documented: clearing the contents of SoftwareDistribution\Download is the standard, supported way to reclaim space from stale update downloads (equivalent to Disk Cleanup's "Windows Update Cleanup"). |

