using System;
using System.IO;

namespace Performish.Core.Backup
{
    public sealed class MigrationResult
    {
        public bool LegacyFolderFound { get; set; }
        public bool MigratedSettings { get; set; }
        public int FilesMigrated { get; set; }
        public bool AnythingMigrated => MigratedSettings || FilesMigrated > 0;
    }

    /// <summary>Where Performish's durable state lives. Settings follow the template's %AppData%
    /// (Roaming) convention; backups/undo snapshots/change log deliberately do NOT - see
    /// DECISIONS.md ("change log / backups live under %LOCALAPPDATA%, not %AppData%") - because the
    /// task requires they stay off any OneDrive-synced path, and %LOCALAPPDATA% is never subject to
    /// OneDrive's Known Folder Move.
    ///
    /// The app was previously named "Ish" - RoamingRootOverride/LocalRootOverride exist purely so
    /// Performish.Tests can point migration at a temp directory instead of the real special folders
    /// (production code never sets them; see DECISIONS.md "migration testability").</summary>
    public static class DataPaths
    {
        private const string AppFolderName = "Performish";
        private const string LegacyAppFolderName = "Ish";

        // Public (not internal) so Performish.Tests can set them without an InternalsVisibleTo wire-up -
        // production code (AppServices.BuildReal(), Program.cs) never touches these.
        public static string RoamingRootOverride;
        public static string LocalRootOverride;

        private static string RoamingRoot => RoamingRootOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        private static string LocalRoot => LocalRootOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        public static string SettingsDirectory
        {
            get
            {
                var dir = Path.Combine(RoamingRoot, AppFolderName);
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string LocalStateRoot
        {
            get
            {
                var dir = Path.Combine(LocalRoot, AppFolderName);
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string BackupDirectory
        {
            get
            {
                var dir = Path.Combine(LocalStateRoot, "backups");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string ChangeLogDirectory
        {
            get
            {
                var dir = Path.Combine(LocalStateRoot, "changelog");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string ExportsDirectory
        {
            get
            {
                var dir = Path.Combine(LocalStateRoot, "exports");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string BenchmarksDirectory
        {
            get
            {
                var dir = Path.Combine(LocalStateRoot, "benchmarks");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private static string LegacySettingsDirectory => Path.Combine(RoamingRoot, LegacyAppFolderName);
        private static string LegacyLocalStateRoot => Path.Combine(LocalRoot, LegacyAppFolderName);

        /// <summary>One-time-per-launch migration from the old "Ish" product name's data folders -
        /// called once from the composition root before anything else touches DataPaths (see
        /// AppServices.BuildReal()). Never overwrites a file the new "Performish" folder already has,
        /// so it is safe to call on every launch and never orphans or clobbers newer data. A user's
        /// undo snapshots, change log, and settings all carry over automatically the first time they
        /// run a build named Performish.</summary>
        public static MigrationResult MigrateFromLegacyInstall()
        {
            var result = new MigrationResult();

            var legacySettingsFile = Path.Combine(LegacySettingsDirectory, "settings.json");
            var newSettingsFile = Path.Combine(SettingsDirectory, "settings.json");
            if (File.Exists(legacySettingsFile) && !File.Exists(newSettingsFile))
            {
                File.Copy(legacySettingsFile, newSettingsFile);
                result.MigratedSettings = true;
            }

            if (Directory.Exists(LegacyLocalStateRoot))
            {
                result.LegacyFolderFound = true;
                result.FilesMigrated += CopyDirectoryIfMissing(Path.Combine(LegacyLocalStateRoot, "backups"), BackupDirectory);
                result.FilesMigrated += CopyDirectoryIfMissing(Path.Combine(LegacyLocalStateRoot, "changelog"), ChangeLogDirectory);
                result.FilesMigrated += CopyDirectoryIfMissing(Path.Combine(LegacyLocalStateRoot, "exports"), ExportsDirectory);
            }

            return result;
        }

        private static int CopyDirectoryIfMissing(string sourceDir, string destDir)
        {
            if (!Directory.Exists(sourceDir)) return 0;

            Directory.CreateDirectory(destDir);
            var copied = 0;
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                if (File.Exists(destFile)) continue; // never overwrite something the new install already wrote
                File.Copy(file, destFile);
                copied++;
            }
            return copied;
        }
    }
}
