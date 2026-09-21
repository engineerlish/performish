using System.Linq;
using System.Threading.Tasks;
using Performish.Core.Backends;
using Performish.Core.Backup;
using Performish.Core.Models;
using Performish.Core.Tweaks;
using Performish.Dialogs;
using Xunit;

namespace Performish.Tests
{
    /// <summary>Regression coverage for the "Balanced preset dry run crashes with 'Failed to set
    /// Win32 parent window of the Control'" bug. Root cause: RunningForm.RunAsync started the
    /// background batch (Task.Run) before the dialog's Win32 handle existed. A dry-run batch applies
    /// near-instantly (no real registry/service/PowerShell I/O - see TweakOperationResult.Preview),
    /// so its very first ctx.Log() call could easily fire before ShowDialog() below had created the
    /// handle; Control.InvokeRequired silently returns false with no handle anywhere in the parent
    /// chain, so that first log line ran directly on the background thread instead of being marshaled
    /// - corrupting the control's thread affinity right as the UI thread tried to create the real
    /// Win32 window a moment later. Fixed by forcing `dialog.Handle` on the UI thread before starting
    /// any background work - see RunningForm.RunAsync and DECISIONS.md.
    ///
    /// Unlike DialogUiTests (which never calls the real, blocking ShowDialog()), these tests DO go
    /// through RunningForm's actual modal loop - that's the only way to exercise the exact race this
    /// bug lived in. It's safe here because the dialog closes itself once the background batch
    /// finishes (the same self-closing behavior the shipped app relies on), so the modal loop always
    /// returns; nothing waits on human input. Only Fake* backends are used, per the hard safety rule.
    /// [StaFact]/[StaTheory] run on a dedicated STA thread, required for WinForms controls.</summary>
    public class RunningFormTests
    {
        private static TweakExecutionContext MakeDryRunContext(System.Action<string> onLog) => new TweakExecutionContext(
            true,
            new FakeRegistryBackend(), new FakeServiceBackend(), new FakeScheduledTaskBackend(),
            new FakeAppxBackend(), new FakeProcessRunner(), new FakeFileSystemBackend(),
            new FakePowerBackend(), new InMemoryUndoStore(), onLog);

        [StaTheory]
        [InlineData(Preset.Conservative)]
        [InlineData(Preset.Balanced)]
        [InlineData(Preset.Aggressive)]
        public async Task RunAsync_DryRunApplyOfEveryPreset_CompletesWithoutThrowing(Preset preset)
        {
            var registry = TweakRegistry.BuildDefault();
            var tweaks = registry.ByPreset(preset).ToList();
            Assert.NotEmpty(tweaks);

            var runner = new TweakRunner(new InMemoryChangeLogStore(), new FakeRestorePointBackend());

            var result = await RunningForm.RunAsync(null, "Applying", (token, onLog) => Task.Run(() =>
            {
                var ctx = MakeDryRunContext(onLog);
                return runner.ApplyBatch(tweaks, ctx, createRestorePoint: false, token);
            }));

            Assert.Equal(tweaks.Count, result.Results.Count);
            Assert.All(result.Results, r => Assert.False(r.Failed));
        }

        [StaFact]
        public async Task RunAsync_AllTweaksDryRun_RepeatedRuns_NeverRacesTheDialogHandle()
        {
            // Repeats the exact race a handful of times - a timing bug doesn't always reproduce on
            // the first try, even with the fix reverted. All 55 tweaks, not just one preset, for the
            // widest possible surface.
            var registry = TweakRegistry.BuildDefault();
            var runner = new TweakRunner(new InMemoryChangeLogStore(), new FakeRestorePointBackend());

            for (var i = 0; i < 5; i++)
            {
                var result = await RunningForm.RunAsync(null, "Applying", (token, onLog) => Task.Run(() =>
                {
                    var ctx = MakeDryRunContext(onLog);
                    return runner.ApplyBatch(registry.All, ctx, createRestorePoint: false, token);
                }));

                Assert.Equal(registry.All.Count, result.Results.Count);
            }
        }
    }
}
