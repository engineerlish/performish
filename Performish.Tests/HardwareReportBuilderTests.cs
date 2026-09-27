using System.Linq;
using Performish.Core.Hardware;
using Xunit;

namespace Performish.Tests
{
    public class HardwareReportBuilderTests
    {
        private static HardwareReport Build(HardwareRawSnapshot raw) => HardwareReportBuilder.Build(raw, HardwareFixtures.Now);

        [Fact]
        public void AllFieldsPresent_Desktop_EveryReadableValueIsMeasuredOrInferredWithASource()
        {
            var report = Build(HardwareFixtures.Desktop());

            Assert.Equal(5, report.Groups.Count);
            foreach (var r in report.AllReadings.Where(r => r.Status == ReadingStatus.Measured))
            {
                Assert.False(string.IsNullOrWhiteSpace(r.Value), r.Key);
                Assert.False(string.IsNullOrWhiteSpace(r.Source), r.Key);
            }
            Assert.Equal("4750 MHz", report.Find("cpu.effective_clock").Value);
            Assert.Equal("6000 MT/s", report.Find("mem.speed").Value);
            Assert.Equal("450 W", report.Find("gpu0.power_limit").Value);
            Assert.Equal("38 °C", report.Find("gpu0.temp").Value);
            Assert.Equal("On", report.Find("gpu0.rebar").Value);
            Assert.Equal("UEFI", report.Find("fw.type").Value);
        }

        [Fact]
        public void AllFieldsMissing_NothingIsInvented_EveryReadingSaysWhy()
        {
            var report = Build(HardwareFixtures.Empty());

            Assert.NotEmpty(report.AllReadings);
            Assert.All(report.AllReadings, r =>
            {
                Assert.False(r.IsReadable, $"{r.Key} should not be readable from an empty snapshot");
                Assert.True(string.IsNullOrEmpty(r.Value), $"{r.Key} must not carry a value");
                Assert.False(string.IsNullOrWhiteSpace(r.Note), $"{r.Key} must say why it is unavailable");
            });
            Assert.Empty(report.Findings);
        }

        [Fact]
        public void PartialData_ShowsWhatExists_AndMarksTheRestUnavailable()
        {
            var report = Build(HardwareFixtures.Partial());

            Assert.Equal(ReadingStatus.Measured, report.Find("fw.bios_vendor").Status);
            Assert.Equal(ReadingStatus.Unavailable, report.Find("fw.bios_version").Status);
            Assert.Equal("4 / 8", report.Find("cpu.cores").Value);
            Assert.Equal(ReadingStatus.Unavailable, report.Find("mem.speed").Status);
            Assert.Equal(ReadingStatus.Unavailable, report.Find("mem.profile").Status);
            Assert.Equal(ReadingStatus.NotApplicable, report.Find("gpu0.rebar").Status); // integrated GPU
            Assert.Equal(HardwareReportBuilder.NeedsVendorGpuLibrary, report.Find("gpu0.live").Note);
        }

        [Fact]
        public void UnusualValues_PlaceholdersAndImplausibleNumbersAreNeverShownAsData()
        {
            var report = Build(HardwareFixtures.Unusual());

            Assert.Equal(ReadingStatus.Unavailable, report.Find("fw.bios_vendor").Status);   // whitespace
            Assert.Equal(ReadingStatus.Unavailable, report.Find("fw.bios_version").Status);  // "Default string"
            Assert.Equal(ReadingStatus.Unavailable, report.Find("fw.bios_date").Status);     // 1970
            Assert.Equal(ReadingStatus.Unavailable, report.Find("board.system").Status);     // "To Be Filled By O.E.M."
            Assert.Equal(ReadingStatus.Unavailable, report.Find("cpu.cores").Status);        // 0 / -1
            Assert.Equal(ReadingStatus.Unavailable, report.Find("cpu.base_clock").Status);   // 0 MHz
            Assert.Equal(ReadingStatus.Unavailable, report.Find("cpu.effective_clock").Status); // 999999 MHz
            Assert.Equal(ReadingStatus.Unavailable, report.Find("gpu0.core_clock").Status);  // -1
            Assert.Equal(ReadingStatus.Unavailable, report.Find("gpu0.power").Status);       // 0 W
            Assert.Equal(ReadingStatus.Unavailable, report.Find("gpu0.power_limit").Status); // 99999 W
            Assert.Equal(ReadingStatus.Unavailable, report.Find("gpu0.temp").Status);        // 255 C
            Assert.Equal(ReadingStatus.Unavailable, report.Find("gpu0.util").Status);        // 101 %
            Assert.Equal("100%", report.Find("gpu.util_all").Value);                         // 340% clamped
            Assert.Equal(ReadingStatus.NotApplicable, report.Find("fw.secure_boot").Status); // legacy boot
            Assert.Equal("Off", report.Find("gpu0.rebar").Value);                            // legacy boot => off
            Assert.DoesNotContain(report.AllReadings, r => (r.Value ?? "").Contains("Default string") || (r.Value ?? "").Contains("O.E.M."));
        }

        [Fact]
        public void Laptop_Xps_MemoryAtCpuLimit_IsNotReportedAsProfileOff()
        {
            var report = Build(HardwareFixtures.XpsLaptop());

            var profile = report.Find("mem.profile");
            Assert.Equal(ReadingStatus.NotApplicable, profile.Status);
            Assert.Contains("fastest speed this CPU officially supports", profile.Note);
            Assert.Equal("2933 MT/s", report.Find("mem.cpu_max").Value);
            Assert.DoesNotContain(report.Findings, f => f.Kind == FindingKind.Opportunity && f.Component == HardwareComponent.Memory);
            Assert.Contains(report.Findings, f => f.Key == "find.mem_cpu_limited");
        }

        [Fact]
        public void Laptop_Xps_DecodesJedecVendorsAndTrimsPartNumbers()
        {
            var report = Build(HardwareFixtures.XpsLaptop());
            Assert.Equal("32 GB, Samsung, M471A4G43BB1-CWE", report.Find("mem.module0").Value);
            Assert.Equal("32 GB, SK Hynix, HMAA4GS6AJR8N-XN", report.Find("mem.module1").Value);
            Assert.Contains(report.Findings, f => f.Key == "find.mem_mixed");
        }

        [Fact]
        public void Laptop_Xps_SleepingDiscreteGpu_IsExplainedNotGuessed()
        {
            var report = Build(HardwareFixtures.XpsLaptop());

            Assert.True(report.DiscreteGpuSkipped);
            var live = report.Find("gpu1.live");
            Assert.Equal(ReadingStatus.Unavailable, live.Status);
            Assert.Contains("isn't woken up", live.Note);
            Assert.Equal(ReadingStatus.NotApplicable, report.Find("gpu1.rebar").Status); // GTX: no ReBAR support
            Assert.Equal(ReadingStatus.NotApplicable, report.Find("gpu0.rebar").Status); // integrated
        }

        [Fact]
        public void HypervisorRunning_VirtualizationIsInferredOn_EvenThoughWmiSaysOff()
        {
            var report = Build(HardwareFixtures.XpsLaptop());
            var virt = report.Find("fw.virtualization");
            Assert.Equal(ReadingStatus.Inferred, virt.Status);
            Assert.Equal("On", virt.Value);
            Assert.DoesNotContain(report.Findings, f => f.Key == "find.virt_off");
        }

        [Fact]
        public void VirtualizationOff_WithNoHypervisor_IsAnInfoFinding()
        {
            var raw = HardwareFixtures.Desktop();
            raw.VirtualizationFirmwareEnabled = false;
            var report = Build(raw);
            Assert.Equal("Off", report.Find("fw.virtualization").Value);
            Assert.Equal(FindingKind.Info, report.Findings.Single(f => f.Key == "find.virt_off").Kind);
        }

        [Fact]
        public void TpmAccessDenied_SaysAdministratorIsNeeded()
        {
            var report = Build(HardwareFixtures.XpsLaptop());
            Assert.Equal(ReadingStatus.Unavailable, report.Find("fw.tpm").Status);
            Assert.Contains("administrator", report.Find("fw.tpm").Note);
        }

        [Fact]
        public void OldBios_ProducesInfoFinding_NewBiosDoesNot()
        {
            Assert.Contains(Build(HardwareFixtures.XpsLaptop()).Findings, f => f.Key == "find.bios_old" && f.Kind == FindingKind.Info);
            Assert.DoesNotContain(Build(HardwareFixtures.Desktop()).Findings, f => f.Key == "find.bios_old");
        }

        [Fact]
        public void VbsRunning_IsInformational_NeverAnOpportunity()
        {
            var finding = Build(HardwareFixtures.XpsLaptop()).Findings.Single(f => f.Key == "find.vbs_on");
            Assert.Equal(FindingKind.Info, finding.Kind);
            Assert.Contains("security trade-off", finding.Guidance);
        }

        [Fact]
        public void Desktop_ProfileOn_IsGood_AtStandardSpeed_IsATip()
        {
            Assert.Contains(Build(HardwareFixtures.Desktop(6000)).Findings, f => f.Key == "find.mem_profile_on" && f.Kind == FindingKind.Good);

            var standard = Build(HardwareFixtures.Desktop(4800));
            Assert.Equal(ReadingStatus.Inferred, standard.Find("mem.profile").Status);
            Assert.Contains(standard.Findings, f => f.Key == "find.mem_check_kit" && f.Kind == FindingKind.Opportunity);
        }

        [Fact]
        public void RebarOff_OnSupportedGpu_IsATip_PartialIsReportedAsIs()
        {
            var off = Build(HardwareFixtures.Desktop(bar1Bytes: 256L * 1024 * 1024));
            Assert.Equal("Off", off.Find("gpu0.rebar").Value);
            Assert.Contains(off.Findings, f => f.Key.StartsWith("find.rebar_off") && f.Kind == FindingKind.Opportunity);

            var partial = Build(HardwareFixtures.Desktop(bar1Bytes: 8L << 30));
            Assert.Equal("Partial", partial.Find("gpu0.rebar").Value);
            Assert.DoesNotContain(partial.Findings, f => f.Key.StartsWith("find.rebar"));
        }

        [Fact]
        public void SensorDriverReadings_AreAlwaysUnavailable_InThisVersion()
        {
            foreach (var raw in new[] { HardwareFixtures.Desktop(), HardwareFixtures.XpsLaptop(), HardwareFixtures.Empty() })
            {
                var report = Build(raw);
                foreach (var key in new[] { "cpu.voltage", "cpu.power_limits", "cpu.temperature", "mem.timings" })
                {
                    Assert.Equal(ReadingStatus.Unavailable, report.Find(key).Status);
                    Assert.Equal(HardwareReportBuilder.NeedsSensorDriver, report.Find(key).Note);
                }
            }
        }

        [Fact]
        public void NvmlMissing_OnNvidiaDesktop_ExplainsTheDriverLibrary()
        {
            var raw = HardwareFixtures.Desktop();
            raw.Gpus[0].Nvidia = null;
            raw.NvmlState = NvmlState.LibraryNotFound;
            var report = Build(raw);
            Assert.Contains("nvml.dll", report.Find("gpu0.live").Note);
            Assert.Equal(ReadingStatus.Measured, report.Find("gpu0.vram").Status); // registry fallback
        }

        [Fact]
        public void KeysAreUniqueAcrossTheReport()
        {
            foreach (var raw in new[] { HardwareFixtures.Desktop(), HardwareFixtures.XpsLaptop(), HardwareFixtures.Empty(), HardwareFixtures.Partial(), HardwareFixtures.Unusual() })
            {
                var keys = Build(raw).AllReadings.Select(r => r.Key).ToList();
                Assert.Equal(keys.Count, keys.Distinct().Count());
            }
        }

        [Fact]
        public void NullSnapshot_IsTreatedAsEmpty()
        {
            var report = HardwareReportBuilder.Build(null, HardwareFixtures.Now);
            Assert.All(report.AllReadings, r => Assert.False(r.IsReadable));
        }

        [Fact]
        public void EveryFindingIsGuideOnly_WithGuidanceAndLinkedReadings()
        {
            foreach (var raw in new[] { HardwareFixtures.Desktop(4800, 256L * 1024 * 1024), HardwareFixtures.XpsLaptop() })
            {
                var report = Build(raw);
                Assert.All(report.Findings, f =>
                {
                    Assert.False(string.IsNullOrWhiteSpace(f.Guidance));
                    Assert.NotEmpty(f.ReadingKeys);
                    Assert.All(f.ReadingKeys, k => Assert.NotNull(report.Find(k)));
                });
            }
        }
    }
}
