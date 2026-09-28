using Performish.Dialogs;
using Xunit;

namespace Performish.Tests
{
    public class SettingsFormTests
    {
        [StaFact]
        public void Populate_ChecksReflectSettingsValues()
        {
            var settings = new AppSettings { CreateRestorePointByDefault = false, BenchmarkIncludeNetwork = true, ScanOnStartup = true };
            using var form = new SettingsForm(settings);
            form.Show();

            Assert.False(form.RestorePointCheckBox.Checked);
            Assert.True(form.BenchmarkNetworkCheckBox.Checked);
            Assert.True(form.ScanOnStartupCheckBox.Checked);
        }

        [StaFact]
        public void ScanOnStartupCheckBox_DefaultsUnchecked_MatchingAppSettingsDefault()
        {
            using var form = new SettingsForm(new AppSettings());
            form.Show();

            Assert.False(form.ScanOnStartupCheckBox.Checked);
        }

        [StaFact]
        public void TogglingScanOnStartupCheckBox_SavesImmediately()
        {
            var settings = new AppSettings { ScanOnStartup = false };
            using var form = new SettingsForm(settings);
            form.Show();

            form.ScanOnStartupCheckBox.Checked = true;

            Assert.True(settings.ScanOnStartup);
        }

        [StaFact]
        public void TogglingRestorePointCheckBox_SavesImmediately()
        {
            var settings = new AppSettings { CreateRestorePointByDefault = true };
            using var form = new SettingsForm(settings);
            form.Show();

            form.RestorePointCheckBox.Checked = false;

            Assert.False(settings.CreateRestorePointByDefault);
        }

        [StaFact]
        public void TogglingBenchmarkNetworkCheckBox_SavesImmediately()
        {
            var settings = new AppSettings { BenchmarkIncludeNetwork = false };
            using var form = new SettingsForm(settings);
            form.Show();

            form.BenchmarkNetworkCheckBox.Checked = true;

            Assert.True(settings.BenchmarkIncludeNetwork);
        }
    }
}
