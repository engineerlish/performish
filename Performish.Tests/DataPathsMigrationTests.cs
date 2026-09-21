using System;
using System.IO;
using Performish.Core.Backup;
using Xunit;

namespace Performish.Tests
{
    /// <summary>Exercises DataPaths.MigrateFromLegacyInstall() against temp directories via the
    /// internal RoamingRootOverride/LocalRootOverride seams - never the real %AppData%/%LOCALAPPDATA%,
    /// consistent with the hard safety rule (this is ordinary file I/O in a throwaway temp folder,
    /// not a registry/service/Appx/power change, but it's still kept off the real profile).</summary>
    public class DataPathsMigrationTests : IDisposable
    {
        private readonly string _root;

        public DataPathsMigrationTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "PerformishMigrationTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_root);
            DataPaths.RoamingRootOverride = Path.Combine(_root, "Roaming");
            DataPaths.LocalRootOverride = Path.Combine(_root, "Local");
            Directory.CreateDirectory(DataPaths.RoamingRootOverride);
            Directory.CreateDirectory(DataPaths.LocalRootOverride);
        }

        public void Dispose()
        {
            DataPaths.RoamingRootOverride = null;
            DataPaths.LocalRootOverride = null;
            try { Directory.Delete(_root, recursive: true); } catch { /* best-effort cleanup */ }
        }

        [Fact]
        public void NoLegacyInstall_MigrationIsANoOp()
        {
            var result = DataPaths.MigrateFromLegacyInstall();

            Assert.False(result.LegacyFolderFound);
            Assert.False(result.MigratedSettings);
            Assert.Equal(0, result.FilesMigrated);
        }

        [Fact]
        public void LegacySettings_AreCopiedToNewLocation()
        {
            var legacyDir = Path.Combine(DataPaths.RoamingRootOverride, "Ish");
            Directory.CreateDirectory(legacyDir);
            File.WriteAllText(Path.Combine(legacyDir, "settings.json"), "{\"DryRunByDefault\":false}");

            var result = DataPaths.MigrateFromLegacyInstall();

            Assert.True(result.MigratedSettings);
            var migratedContent = File.ReadAllText(Path.Combine(DataPaths.SettingsDirectory, "settings.json"));
            Assert.Contains("DryRunByDefault", migratedContent);
        }

        [Fact]
        public void LegacySettings_NeverOverwriteExistingNewSettings()
        {
            var legacyDir = Path.Combine(DataPaths.RoamingRootOverride, "Ish");
            Directory.CreateDirectory(legacyDir);
            File.WriteAllText(Path.Combine(legacyDir, "settings.json"), "{\"DryRunByDefault\":false}");

            Directory.CreateDirectory(DataPaths.SettingsDirectory);
            File.WriteAllText(Path.Combine(DataPaths.SettingsDirectory, "settings.json"), "{\"DryRunByDefault\":true}");

            var result = DataPaths.MigrateFromLegacyInstall();

            Assert.False(result.MigratedSettings);
            var content = File.ReadAllText(Path.Combine(DataPaths.SettingsDirectory, "settings.json"));
            Assert.Contains("true", content);
        }

        [Fact]
        public void LegacyBackupsAndChangeLog_AreCopiedWithoutOverwritingNewer()
        {
            var legacyLocal = Path.Combine(DataPaths.LocalRootOverride, "Ish");
            var legacyBackups = Path.Combine(legacyLocal, "backups");
            var legacyChangeLog = Path.Combine(legacyLocal, "changelog");
            Directory.CreateDirectory(legacyBackups);
            Directory.CreateDirectory(legacyChangeLog);
            File.WriteAllText(Path.Combine(legacyBackups, "debloat.telemetry.latest.json"), "{\"Existed\":true}");
            File.WriteAllText(Path.Combine(legacyChangeLog, "changelog-2025-01.jsonl"), "{}\n");

            // A file already present under the new name must survive untouched.
            Directory.CreateDirectory(DataPaths.BackupDirectory);
            File.WriteAllText(Path.Combine(DataPaths.BackupDirectory, "debloat.telemetry.latest.json"), "{\"Existed\":false}");

            var result = DataPaths.MigrateFromLegacyInstall();

            Assert.True(result.LegacyFolderFound);
            Assert.Equal(1, result.FilesMigrated); // only the changelog file - the backup file already existed

            var survivedContent = File.ReadAllText(Path.Combine(DataPaths.BackupDirectory, "debloat.telemetry.latest.json"));
            Assert.Contains("false", survivedContent); // untouched, not overwritten by the legacy copy

            Assert.True(File.Exists(Path.Combine(DataPaths.ChangeLogDirectory, "changelog-2025-01.jsonl")));
        }
    }
}
