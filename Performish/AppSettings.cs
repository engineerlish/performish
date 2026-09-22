using System.IO;
using System.Text.Json;
using Performish.Core.Backup;

namespace Performish
{
    /// <summary>Small UI-level preferences. Uses Performish.Core.Backup.DataPaths.SettingsDirectory
    /// (rather than duplicating the %AppData% path-building logic here, as the pre-rename version
    /// did) so this settings file also benefits from DataPaths' legacy-install migration for free.</summary>
    public enum BenchmarkMode { Off, Quick, Full }

    public sealed class AppSettings
    {
        public bool DryRunByDefault { get; set; } = true;
        public bool CreateRestorePointByDefault { get; set; } = true;

        /// <summary>Which benchmark suite (if any) runs automatically around an apply/revert batch -
        /// Quick by default so benchmarking never makes routine tweak application feel slow (the
        /// task's own "no regressions to apply speed" rule); the user can turn it Off or up to Full.
        /// Never includes network metrics automatically - see BenchmarkIncludeNetwork.</summary>
        public BenchmarkMode BenchmarkModeDefault { get; set; } = BenchmarkMode.Quick;
        public bool BenchmarkIncludeNetwork { get; set; } = false;

        private static string SettingsPath => Path.Combine(DataPaths.SettingsDirectory, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null) return settings;
                }
            }
            catch
            {
                // Corrupt or unreadable settings file; fall back to defaults rather than crash on launch.
            }

            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // Best-effort; not being able to persist settings shouldn't crash the app.
            }
        }
    }
}
