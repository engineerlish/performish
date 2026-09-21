using System.Linq;
using Microsoft.Win32;
using Performish.Core;
using Performish.Core.Backup;
using Performish.Core.Models;
using Performish.Core.Tweaks;
using Performish.Dialogs;
using Xunit;

namespace Performish.Tests
{
    /// <summary>Covers the History dialog's per-row Undo state handling - the button/state-handling
    /// test category this project's test suite tracks separately from pure Core logic (see
    /// DialogUiTests' doc comment for why UI-threading bugs need UI-level tests). Fake-backed only.</summary>
    public class HistoryFormTests
    {
        [StaFact]
        public void NoHistory_UndoButtonDisabled()
        {
            var services = AppServices.BuildFake();
            using var form = new HistoryForm(services, () => true);
            form.Show();

            var undoButton = FindButton(form, "Undo this tweak...");
            Assert.False(undoButton.Enabled);
        }

        [StaFact]
        public void AppliedTweakSelected_UndoButtonEnabled()
        {
            var services = AppServices.BuildFake();
            var tweak = services.TweakRegistry.All.First();
            var ctx = services.CreateContext(dryRun: false);
            services.Runner.ApplyBatch(new[] { tweak }, ctx, createRestorePoint: false);

            using var form = new HistoryForm(services, () => true);
            form.Show();

            var list = FindListView(form);
            Assert.NotEmpty(list.Items);
            list.Items[list.Items.Count - 1].Selected = true; // most recent entry, our Apply

            var undoButton = FindButton(form, "Undo this tweak...");
            Assert.True(undoButton.Enabled);
        }

        [StaFact]
        public void DryRunAppliedTweak_CannotBeUndoneFromHistory()
        {
            var services = AppServices.BuildFake();
            var tweak = services.TweakRegistry.All.First();
            var ctx = services.CreateContext(dryRun: true); // dry-run apply - never really "applied"
            services.Runner.ApplyBatch(new[] { tweak }, ctx, createRestorePoint: false);

            using var form = new HistoryForm(services, () => true);
            form.Show();

            var list = FindListView(form);
            list.Items[list.Items.Count - 1].Selected = true;

            var undoButton = FindButton(form, "Undo this tweak...");
            Assert.False(undoButton.Enabled);
        }

        [StaFact]
        public void FailedTweak_CannotBeUndoneFromHistory()
        {
            var services = AppServices.BuildFake();
            var changeLog = services.ChangeLog;
            changeLog.Append(new ChangeLogEntry
            {
                TimestampUtc = System.DateTime.UtcNow,
                TweakId = "debloat.telemetry",
                TweakName = "Disable diagnostic data collection",
                Action = ChangeLogAction.Apply,
                Outcome = OperationOutcome.Failed,
                Message = "boom",
                DryRun = false
            });

            using var form = new HistoryForm(services, () => true);
            form.Show();

            var list = FindListView(form);
            list.Items[list.Items.Count - 1].Selected = true;

            var undoButton = FindButton(form, "Undo this tweak...");
            Assert.False(undoButton.Enabled);
        }

        private static System.Windows.Forms.Button FindButton(System.Windows.Forms.Control root, string text)
        {
            foreach (System.Windows.Forms.Control c in root.Controls)
            {
                if (c is System.Windows.Forms.Button b && b.Text == text) return b;
                var found = FindButton(c, text);
                if (found != null) return found;
            }
            return null;
        }

        private static System.Windows.Forms.ListView FindListView(System.Windows.Forms.Control root)
        {
            foreach (System.Windows.Forms.Control c in root.Controls)
            {
                if (c is System.Windows.Forms.ListView lv) return lv;
                var found = FindListView(c);
                if (found != null) return found;
            }
            return null;
        }
    }
}
