using System.Collections.Generic;
using System.Linq;
using Performish.Core.Hardware;
using Xunit;

namespace Performish.Tests
{
    /// <summary>Display logic for the Hardware & firmware dialog, against all-present, all-missing, partial
    /// and unusual simulated hardware - no dependency on the machine running the tests.</summary>
    public class HardwarePresenterTests
    {
        private static HardwareReport Build(HardwareRawSnapshot raw) => HardwareReportBuilder.Build(raw, HardwareFixtures.Now);

        public static IEnumerable<object[]> AllFixtures() => new[]
        {
            new object[] { "desktop" }, new object[] { "laptop" }, new object[] { "empty" }, new object[] { "partial" }, new object[] { "unusual" }
        };

        private static HardwareReport Fixture(string name) => Build(name switch
        {
            "desktop" => HardwareFixtures.Desktop(),
            "laptop" => HardwareFixtures.XpsLaptop(),
            "empty" => HardwareFixtures.Empty(),
            "partial" => HardwareFixtures.Partial(),
            _ => HardwareFixtures.Unusual()
        });

        // ---- shared -------------------------------------------------------------------------------

        [Theory]
        [MemberData(nameof(AllFixtures))]
        public void DisplayValue_NeverLooksLikeDataForUnreadableReadings(string fixture)
        {
            foreach (var r in Fixture(fixture).AllReadings.Where(r => !r.IsReadable))
                Assert.Contains(HardwareReportText.DisplayValue(r), new[] { "not available", "n/a" });
        }

        [Fact]
        public void ReadingDetail_InferredValue_IsLabeledInferred_AndShowsRelatedFinding()
        {
            var report = Build(HardwareFixtures.Desktop(4800));
            var lines = HardwareTones.ReadingDetail(report, report.Find("mem.profile"));
            Assert.Contains(lines, l => l.Text.Contains("inferred, not read directly") && l.Tone == Tone.Info);
            Assert.Contains(lines, l => l.Text.StartsWith("[TIP]") && l.Tone == Tone.Tip);
        }

        [Fact]
        public void ReadingDetail_Unavailable_SaysNothingIsGuessed_AndWhy()
        {
            var report = Build(HardwareFixtures.Empty());
            var lines = HardwareTones.ReadingDetail(report, report.Find("cpu.voltage"));
            Assert.Contains(lines, l => l.Text.Contains("rather than a guess"));
            Assert.Contains(lines, l => l.Text.StartsWith("Why:"));
        }

        // ---- component list / readings / detail ---------------------------------------------------

        [Theory]
        [MemberData(nameof(AllFixtures))]
        public void Dialog_OneSummaryPerComponent_CountsAddUp(string fixture)
        {
            var report = Fixture(fixture);
            var summaries = ComponentPresenter.Summaries(report);
            Assert.Equal(5, summaries.Count);
            foreach (var s in summaries)
            {
                var rows = ComponentPresenter.Rows(report, s.Component);
                Assert.Equal(s.Total, rows.Count);
                Assert.Equal(s.Readable, report.Group(s.Component).Readings.Count(r => r.IsReadable));
                Assert.Contains($"({s.Readable}/{s.Total})", s.Text);
            }
        }

        [Fact]
        public void Dialog_AllMissing_ComponentsAreDim_NoTips()
        {
            var summaries = ComponentPresenter.Summaries(Build(HardwareFixtures.Empty()));
            Assert.All(summaries, s => { Assert.Equal(0, s.Readable); Assert.Equal(0, s.Tips); Assert.Equal(Tone.Dim, s.Tone); });
        }

        [Fact]
        public void Dialog_TipIsCountedOnItsComponent()
        {
            var report = Build(HardwareFixtures.Desktop(4800, 256L * 1024 * 1024));
            var summaries = ComponentPresenter.Summaries(report);
            Assert.Equal(1, summaries.Single(s => s.Component == HardwareComponent.Memory).Tips);
            Assert.Equal(1, summaries.Single(s => s.Component == HardwareComponent.Gpu).Tips);
            Assert.Equal(Tone.Tip, summaries.Single(s => s.Component == HardwareComponent.Gpu).Tone);
            Assert.StartsWith("[TIP]", ComponentPresenter.FindingLines(report, HardwareComponent.Memory)[0].Text);
        }

        [Fact]
        public void Dialog_RowsCarryStatusTags()
        {
            var rows = ComponentPresenter.Rows(Build(HardwareFixtures.XpsLaptop()), HardwareComponent.Firmware);
            Assert.Equal("", rows.Single(r => r.Key == "fw.tpm").Tag);
            Assert.Equal("not available", rows.Single(r => r.Key == "fw.tpm").Value);
            Assert.Equal("[likely]", rows.Single(r => r.Key == "fw.virtualization").Tag);
            Assert.Equal("", rows.Single(r => r.Key == "fw.bios_version").Tag);
        }

        // ---- text report / service ------------------------------------------------------------------

        [Theory]
        [MemberData(nameof(AllFixtures))]
        public void TextReport_ListsEveryGroupAndReading(string fixture)
        {
            var report = Fixture(fixture);
            var text = HardwareReportText.Format(report);
            foreach (var g in report.Groups) Assert.Contains(g.Title.ToUpperInvariant(), text);
            foreach (var r in report.AllReadings) Assert.Contains(r.Label, text);
            Assert.Contains("none of this feeds the health score", text);
        }

        [Fact]
        public void Service_PassesWakeOptionThrough()
        {
            var fake = new FakeHardwareInfoBackend(HardwareFixtures.XpsLaptop());
            new HardwareInfoService(fake).ReadReport(new HardwareReadOptions { WakeDiscreteGpu = true });
            Assert.True(fake.Calls.Single().WakeDiscreteGpu);
        }
    }
}
