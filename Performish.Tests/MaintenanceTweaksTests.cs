using System;
using System.Linq;
using Performish.Core.Backends;
using Performish.Core.Models;
using Performish.Core.Tweaks;
using Xunit;

namespace Performish.Tests
{
    public class SpaceCleanupFactoryTests
    {
        private static TweakDefinition MakeCleanupTweak(string path, int olderThanDays = 14) => TweakFactory.SpaceCleanup(
            "t.cleanup", "Test cleanup", "desc", TweakCategory.Maintenance, RiskLevel.Safe, "source",
            () => new[] { path }, olderThanDays);

        [Fact]
        public void Apply_DeletesOnlyFilesOlderThanThreshold_ReportsFreedSpace()
        {
            var bundle = FakeContextFactory.Create();
            bundle.FileSystem.Directories.Add(@"C:\Cache");
            bundle.FileSystem.Files.Add(new FakeFileSystemBackend.FakeFile { Path = @"C:\Cache\old.tmp", Size = 1024 * 1024, LastWriteUtc = DateTime.UtcNow.AddDays(-30) });
            bundle.FileSystem.Files.Add(new FakeFileSystemBackend.FakeFile { Path = @"C:\Cache\new.tmp", Size = 2048 * 1024, LastWriteUtc = DateTime.UtcNow });

            var tweak = MakeCleanupTweak(@"C:\Cache");
            var result = tweak.Apply(bundle.Context);

            Assert.Equal(OperationOutcome.Success, result.Outcome);
            Assert.Contains("1 MB", result.Message);
            Assert.Single(bundle.FileSystem.Files); // only new.tmp remains
            Assert.Equal(@"C:\Cache\new.tmp", bundle.FileSystem.Files[0].Path);
        }

        [Fact]
        public void Apply_DirectoryDoesNotExist_IsSkippedNotFailed()
        {
            var bundle = FakeContextFactory.Create();
            var tweak = MakeCleanupTweak(@"C:\DoesNotExist");

            var result = tweak.Apply(bundle.Context);

            Assert.Equal(OperationOutcome.Skipped, result.Outcome);
        }

        [Fact]
        public void Apply_DryRun_NeverDeletesAnything()
        {
            var bundle = FakeContextFactory.Create(dryRun: true);
            bundle.FileSystem.Directories.Add(@"C:\Cache");
            bundle.FileSystem.Files.Add(new FakeFileSystemBackend.FakeFile { Path = @"C:\Cache\old.tmp", Size = 1024, LastWriteUtc = DateTime.UtcNow.AddDays(-30) });

            var tweak = MakeCleanupTweak(@"C:\Cache");
            var result = tweak.Apply(bundle.Context);

            Assert.True(result.WasDryRun);
            Assert.Single(bundle.FileSystem.Files); // still there
        }

        [Fact]
        public void Undo_AlwaysSkipped_ExplainsWhy()
        {
            var bundle = FakeContextFactory.Create();
            var tweak = MakeCleanupTweak(@"C:\Cache");

            var result = tweak.Undo(bundle.Context);

            Assert.Equal(OperationOutcome.Skipped, result.Outcome);
            Assert.Contains("cannot be restored", result.Message);
        }

        [Fact]
        public void Check_IsAlwaysNotApplicable()
        {
            var bundle = FakeContextFactory.Create();
            var tweak = MakeCleanupTweak(@"C:\Cache");

            Assert.Equal(TweakState.NotApplicable, tweak.Check(bundle.Context));
        }
    }

    public class MaintenanceTweaksLibraryTests
    {
        [Fact]
        public void All_AreInMaintenanceCategory()
        {
            var tweaks = MaintenanceTweaks.All().ToList();
            Assert.NotEmpty(tweaks);
            Assert.All(tweaks, t => Assert.Equal(TweakCategory.Maintenance, t.Category));
        }

        [Fact]
        public void All_HaveUndoThatExplainsItCannotRestoreFiles()
        {
            foreach (var t in MaintenanceTweaks.All())
            {
                var bundle = FakeContextFactory.Create();
                var result = t.Undo(bundle.Context);
                Assert.Equal(OperationOutcome.Skipped, result.Outcome);
            }
        }

        [Fact]
        public void RegistryIncludesMaintenanceTweaks()
        {
            var registry = TweakRegistry.BuildDefault();
            var maintenanceCount = registry.ByCategory(TweakCategory.Maintenance).Count();
            Assert.True(maintenanceCount >= 4, $"Expected at least 4 Maintenance tweaks, found {maintenanceCount}.");
        }
    }
}
