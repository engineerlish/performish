using System.Linq;
using Performish.Core.Benchmark;
using Performish.Dialogs;
using Xunit;

namespace Performish.Tests
{
    public class HealthScoreFormTests
    {
        private static HealthScoreResult MakeResult() => new HealthScoreResult
        {
            Score = 74,
            Factors =
            {
                new HealthScoreFactor { Name = "Startup items", Points = 20, MaxPoints = 20, Detail = "0 startup item(s)", Guidance = "Nothing to change.", ToolCanAct = false },
                new HealthScoreFactor { Name = "Free disk space", Points = 0, MaxPoints = 20, Detail = "2% free", Guidance = "Free up space.", ToolCanAct = true },
                new HealthScoreFactor { Name = "Uptime", Points = 8, MaxPoints = 15, Detail = "100 hour(s)", Guidance = "A reboot may help.", ToolCanAct = false },
            }
        };

        [StaFact]
        public void Populate_ShowsSummaryScoreAndEveryFactorRow()
        {
            using var form = new HealthScoreForm(MakeResult());
            form.Show();

            Assert.Contains("74", form.SummaryText);
            Assert.Equal(3, form.Rows.Count);
            Assert.Contains(form.Rows, r => r.Text.Contains("Startup items"));
            Assert.Contains(form.Rows, r => r.Text.Contains("Free disk space"));
            Assert.Contains(form.Rows, r => r.Text.Contains("Uptime"));
        }

        [StaFact]
        public void FullMarksFactor_IsStyledOk_NotError()
        {
            using var form = new HealthScoreForm(MakeResult());
            form.Show();

            var row = form.Rows.First(r => r.Text.Contains("Startup items"));
            Assert.StartsWith("[OK]", row.Text);
            Assert.Equal(UiStyle.Accent, row.Color);
        }

        [StaFact]
        public void ZeroPointsFactor_IsStyledLow_Error()
        {
            using var form = new HealthScoreForm(MakeResult());
            form.Show();

            var row = form.Rows.First(r => r.Text.Contains("Free disk space"));
            Assert.StartsWith("[LOW]", row.Text);
            Assert.Equal(UiStyle.Error, row.Color);
        }

        [StaFact]
        public void PartialCreditFactor_IsStyledMid_NeitherOkNorLow()
        {
            using var form = new HealthScoreForm(MakeResult());
            form.Show();

            var row = form.Rows.First(r => r.Text.Contains("Uptime"));
            Assert.StartsWith("[MID]", row.Text);
            Assert.NotEqual(UiStyle.Accent, row.Color);
            Assert.NotEqual(UiStyle.Error, row.Color);
        }

        [StaFact]
        public void SelectingAFactor_ShowsItsGuidanceInDetailPane()
        {
            using var form = new HealthScoreForm(MakeResult());
            form.Show();

            var diskIndex = form.Rows.ToList().FindIndex(r => r.Text.Contains("Free disk space"));
            form.SelectRow(diskIndex);

            Assert.Contains("Free up space.", form.DetailText);
            Assert.Contains("2% free", form.DetailText);
        }

        [StaFact]
        public void SelectingANonToolFactor_NotesItIsNotDirectlyFixableByPerformish()
        {
            using var form = new HealthScoreForm(MakeResult());
            form.Show();

            var uptimeIndex = form.Rows.ToList().FindIndex(r => r.Text.Contains("Uptime"));
            form.SelectRow(uptimeIndex);

            Assert.Contains("does not change this directly", form.DetailText);
        }

        [StaFact]
        public void OverallScoreColor_MatchesThreshold()
        {
            using var lowForm = new HealthScoreForm(new HealthScoreResult { Score = 30, Factors = { new HealthScoreFactor { Name = "X", Points = 0, MaxPoints = 10, Detail = "d", Guidance = "g" } } });
            lowForm.Show();
            Assert.Equal(UiStyle.Error, lowForm.SummaryColor);

            using var highForm = new HealthScoreForm(new HealthScoreResult { Score = 90, Factors = { new HealthScoreFactor { Name = "X", Points = 10, MaxPoints = 10, Detail = "d", Guidance = "g" } } });
            highForm.Show();
            Assert.Equal(UiStyle.Accent, highForm.SummaryColor);
        }
    }
}
