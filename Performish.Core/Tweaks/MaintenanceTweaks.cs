using System;
using System.Collections.Generic;
using System.IO;
using Performish.Core.Models;

namespace Performish.Core.Tweaks
{
    /// <summary>Disk-space reclamation: caches and leftovers Windows/browsers regenerate on their
    /// own, so clearing them is low-risk by construction - see each tweak's Source for the specific
    /// Microsoft/vendor documentation calling the target safe to clear. Every tweak here is built on
    /// TweakFactory.SpaceCleanup (see that file's doc comment) - Undo is always Skipped, since
    /// deleted files cannot come back; that limitation is stated in every description, never hidden.</summary>
    public static class MaintenanceTweaks
    {
        private const int DefaultOlderThanDays = 14;

        public static IEnumerable<TweakDefinition> All()
        {
            yield return TweakFactory.SpaceCleanup(
                "maint.windows_update_cache",
                "Clear the Windows Update download cache",
                $"Deletes downloaded update files older than {DefaultOlderThanDays} days from " +
                "C:\\Windows\\SoftwareDistribution\\Download - Windows re-downloads anything it still " +
                "needs. This is the same folder Disk Cleanup's \"Windows Update Cleanup\" category " +
                "targets, and does not touch the Windows Update service itself.",
                TweakCategory.Maintenance, RiskLevel.Safe,
                "Microsoft-documented: clearing the contents of SoftwareDistribution\\Download is the " +
                "standard, supported way to reclaim space from stale update downloads " +
                "(equivalent to Disk Cleanup's \"Windows Update Cleanup\").",
                () => new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download") },
                DefaultOlderThanDays,
                new[] { Preset.Conservative, Preset.Balanced, Preset.Aggressive });

            yield return TweakFactory.SpaceCleanup(
                "maint.thumbnail_cache",
                "Clear the thumbnail cache",
                $"Deletes cached thumbnail/icon database files older than {DefaultOlderThanDays} days " +
                "from %LocalAppData%\\Microsoft\\Windows\\Explorer. File Explorer regenerates thumbnails " +
                "the next time it needs them - the only visible effect is a brief re-render the first " +
                "time you browse a folder with images/videos afterward.",
                TweakCategory.Maintenance, RiskLevel.Safe,
                "Standard Disk Cleanup \"Thumbnails\" category target folder.",
                () => new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Explorer") },
                DefaultOlderThanDays,
                new[] { Preset.Conservative, Preset.Balanced, Preset.Aggressive });

            yield return TweakFactory.SpaceCleanup(
                "maint.directx_shader_cache",
                "Clear the DirectX shader cache",
                $"Deletes cached compiled-shader files older than {DefaultOlderThanDays} days from " +
                "%LocalAppData%\\D3DSCache. Games/apps recompile and re-cache shaders the first time " +
                "they're needed again - this can cause a one-time stutter on next launch of each game " +
                "while shaders recompile, which is the normal, expected trade-off of clearing this cache.",
                TweakCategory.Maintenance, RiskLevel.Safe,
                "Standard Disk Cleanup \"DirectX Shader Cache\" category (available since Windows 10 " +
                "1809) - the global cache, not any single game's own separate shader cache folder.",
                () => new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D3DSCache") },
                DefaultOlderThanDays,
                new[] { Preset.Conservative, Preset.Balanced, Preset.Aggressive });

            yield return TweakFactory.SpaceCleanup(
                "maint.edge_cache",
                "Clear the Microsoft Edge browser cache",
                $"Deletes cached web content older than {DefaultOlderThanDays} days from Edge's " +
                "default-profile Cache folder. Sites reload cached assets from the network the next " +
                "time you visit - no bookmarks, passwords, history, or open tabs are touched (this " +
                "targets only the Cache subfolder, not the profile itself). Best effect if Edge is " +
                "closed first; if it's running, locked files are skipped, not force-deleted.",
                TweakCategory.Maintenance, RiskLevel.Moderate,
                "Cache subfolder within Edge's documented user-data profile layout " +
                "(User Data\\Default\\Cache) - the same folder Edge's own \"Clear browsing data\" > " +
                "\"Cached images and files\" option clears.",
                () => new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge", "User Data", "Default", "Cache") },
                DefaultOlderThanDays,
                new[] { Preset.Balanced, Preset.Aggressive });

            yield return TweakFactory.SpaceCleanup(
                "maint.chrome_cache",
                "Clear the Google Chrome browser cache",
                $"Deletes cached web content older than {DefaultOlderThanDays} days from Chrome's " +
                "default-profile Cache folder. Sites reload cached assets from the network the next " +
                "time you visit - no bookmarks, passwords, history, or open tabs are touched. Best " +
                "effect if Chrome is closed first; if it's running, locked files are skipped, not " +
                "force-deleted.",
                TweakCategory.Maintenance, RiskLevel.Moderate,
                "Cache subfolder within Chrome's documented user-data profile layout " +
                "(User Data\\Default\\Cache) - the same folder Chrome's own \"Clear browsing data\" > " +
                "\"Cached images and files\" option clears.",
                () => new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "User Data", "Default", "Cache") },
                DefaultOlderThanDays,
                new[] { Preset.Balanced, Preset.Aggressive });
        }
    }
}
