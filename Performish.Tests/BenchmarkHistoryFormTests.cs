using System;
using System.Collections.Generic;
using Performish.Core.Benchmark;
using Performish.Dialogs;
using Xunit;

namespace Performish.Tests
{
    public class BenchmarkHistoryFormTests
    {
        private static BenchmarkRun MakeRun(DateTime timestampUtc, string label) => new BenchmarkRun
        {
            TimestampUtc = timestampUtc,
            Label = label,
            Kind = BenchmarkRunKind.Standalone
        };

        [StaFact]
        public void NoRuns_CompareButtonDisabled_ShowsEmptyMessage()
        {
            using var form = new BenchmarkHistoryForm(new List<BenchmarkRun>());
            form.Show();

            Assert.Equal(0, form.RunCount);
            Assert.False(form.CompareButton.Enabled);
        }

        [StaFact]
        public void EveryRun_IsListed_NewestFirst()
        {
            var runs = new List<BenchmarkRun>
            {
                MakeRun(DateTime.UtcNow.AddDays(-2), "oldest"),
                MakeRun(DateTime.UtcNow, "newest"),
                MakeRun(DateTime.UtcNow.AddDays(-1), "middle"),
            };
            using var form = new BenchmarkHistoryForm(runs);
            form.Show();

            Assert.Equal(3, form.RunCount);
        }

        [StaFact]
        public void SelectingOneRow_CompareButtonStaysDisabled()
        {
            var runs = new List<BenchmarkRun> { MakeRun(DateTime.UtcNow, "a"), MakeRun(DateTime.UtcNow.AddHours(-1), "b") };
            using var form = new BenchmarkHistoryForm(runs);
            form.Show();

            form.SelectRows(0);

            Assert.False(form.CompareButton.Enabled);
        }

        [StaFact]
        public void SelectingExactlyTwoRows_EnablesCompareButton()
        {
            var runs = new List<BenchmarkRun> { MakeRun(DateTime.UtcNow, "a"), MakeRun(DateTime.UtcNow.AddHours(-1), "b"), MakeRun(DateTime.UtcNow.AddHours(-2), "c") };
            using var form = new BenchmarkHistoryForm(runs);
            form.Show();

            form.SelectRows(0, 1);

            Assert.True(form.CompareButton.Enabled);
        }

        [StaFact]
        public void SelectingThreeRows_CompareButtonStaysDisabled()
        {
            var runs = new List<BenchmarkRun> { MakeRun(DateTime.UtcNow, "a"), MakeRun(DateTime.UtcNow.AddHours(-1), "b"), MakeRun(DateTime.UtcNow.AddHours(-2), "c") };
            using var form = new BenchmarkHistoryForm(runs);
            form.Show();

            form.SelectRows(0, 1, 2);

            Assert.False(form.CompareButton.Enabled);
        }

        [StaFact]
        public void CompareSelected_OrdersPairOlderFirst_RegardlessOfSelectionOrder()
        {
            var older = MakeRun(DateTime.UtcNow.AddHours(-5), "older");
            var newer = MakeRun(DateTime.UtcNow, "newer");
            // History list is newest-first, so index 0 = newer, index 1 = older.
            using var form = new BenchmarkHistoryForm(new List<BenchmarkRun> { older, newer });
            form.Show();

            form.SelectRows(0, 1);
            form.CompareButton.PerformClick();

            Assert.NotNull(form.SelectedPair);
            Assert.Equal("older", form.SelectedPair.Value.Older.Label);
            Assert.Equal("newer", form.SelectedPair.Value.Newer.Label);
            Assert.Equal(System.Windows.Forms.DialogResult.OK, form.DialogResult);
        }
    }
}
