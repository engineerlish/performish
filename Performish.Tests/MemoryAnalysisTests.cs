using System.Collections.Generic;
using Performish.Core.Hardware;
using Xunit;

namespace Performish.Tests
{
    public class MemoryAnalysisTests
    {
        private static List<MemoryModuleRaw> Modules(int? rated, int? configured, int type, int formFactor = 8, int count = 2)
        {
            var list = new List<MemoryModuleRaw>();
            for (int i = 0; i < count; i++)
                list.Add(new MemoryModuleRaw { RatedSpeedMts = rated, ConfiguredSpeedMts = configured, SmbiosMemoryType = type, FormFactor = formFactor });
            return list;
        }

        [Theory]
        [InlineData("Intel(R) Core(TM) i7-10750H CPU @ 2.60GHz", 26, 2933)]
        [InlineData("AMD Ryzen 7 7800X3D 8-Core Processor", 34, 5200)]
        [InlineData("AMD Ryzen 9 9950X3D 16-Core Processor", 34, 5600)]
        [InlineData("AMD Ryzen 7 5800X3D 8-Core Processor", 26, 3200)]
        [InlineData("Intel(R) Core(TM) i9-14900K", 34, 5600)]
        [InlineData("Intel(R) Core(TM) i9-14900K", 26, 3200)]
        [InlineData("Intel(R) Core(TM) Ultra 9 285K", 34, 6400)]
        public void CpuTable_KnownCpus(string cpu, int type, int expected)
        {
            Assert.Equal(expected, MemoryAnalysis.CpuSupportedMaxMts(cpu, type));
        }

        [Theory]
        [InlineData("AMD Ryzen 7 7840HS w/ Radeon 780M Graphics", 34)] // mobile, not in table
        [InlineData("Intel(R) Core(TM) i5-13400", 34)]                  // non-K excluded on purpose
        [InlineData("", 26)]
        [InlineData(null, 26)]
        [InlineData("AMD Ryzen 7 7800X3D 8-Core Processor", 26)]       // wrong memory type for the platform
        public void CpuTable_UnknownOrMismatched_ReturnsNull(string cpu, int type)
        {
            Assert.Null(MemoryAnalysis.CpuSupportedMaxMts(cpu, type));
        }

        [Fact]
        public void RunningAboveStandard_ProfileLikelyOn()
        {
            var a = MemoryAnalysis.AnalyzeProfile(Modules(4800, 6000, 34), "AMD Ryzen 7 7800X3D 8-Core Processor", false);
            Assert.Equal(MemoryProfileVerdict.ProfileLikelyOn, a.Verdict);
        }

        [Fact]
        public void RunningAboveModuleBaseRating_EvenBelowStandardMax_ProfileLikelyOn()
        {
            var a = MemoryAnalysis.AnalyzeProfile(Modules(4800, 5200, 34), "AMD Ryzen 7 7800X3D 8-Core Processor", false);
            Assert.Equal(MemoryProfileVerdict.ProfileLikelyOn, a.Verdict);
        }

        [Fact]
        public void DesktopAtStandardSpeed_IsCheckKitHint_NotACertainty()
        {
            var a = MemoryAnalysis.AnalyzeProfile(Modules(4800, 4800, 34), "AMD Ryzen 7 7800X3D 8-Core Processor", false);
            Assert.Equal(MemoryProfileVerdict.StandardSpeedCheckKit, a.Verdict);
            Assert.Contains("can't see", a.Explanation);
        }

        [Fact]
        public void LaptopCappedByCpu_IsAtCpuMaximum()
        {
            var a = MemoryAnalysis.AnalyzeProfile(Modules(3200, 2933, 26, 12), "Intel(R) Core(TM) i7-10750H CPU @ 2.60GHz", true);
            Assert.Equal(MemoryProfileVerdict.AtCpuSupportedMaximum, a.Verdict);
            Assert.Equal(2933, a.ConfiguredMts);
            Assert.Equal(3200, a.RatedMts);
        }

        [Fact]
        public void LaptopUnknownCpu_BelowRating_IsBelowRating_WithLaptopWording()
        {
            var a = MemoryAnalysis.AnalyzeProfile(Modules(3200, 2666, 26, 12), "Some Future CPU", true);
            Assert.Equal(MemoryProfileVerdict.BelowModuleRating, a.Verdict);
            Assert.Contains("Laptop firmware", a.Explanation);
        }

        [Fact]
        public void LaptopAtRating_NoXmpConcept()
        {
            var a = MemoryAnalysis.AnalyzeProfile(Modules(3200, 3200, 26, 12), "Some Future CPU", true);
            Assert.Equal(MemoryProfileVerdict.LaptopAtRatedSpeed, a.Verdict);
        }

        [Fact]
        public void MismatchedSpeeds_UsesTheSlowest()
        {
            var modules = new List<MemoryModuleRaw>
            {
                new MemoryModuleRaw { RatedSpeedMts = 3600, ConfiguredSpeedMts = 3600, SmbiosMemoryType = 26 },
                new MemoryModuleRaw { RatedSpeedMts = 3200, ConfiguredSpeedMts = 3200, SmbiosMemoryType = 26 },
            };
            var a = MemoryAnalysis.AnalyzeProfile(modules, null, false);
            Assert.Equal(3200, a.ConfiguredMts);
            Assert.Equal(3200, a.RatedMts);
        }

        [Fact]
        public void NoSpeedReported_Unknown()
        {
            Assert.Equal(MemoryProfileVerdict.Unknown, MemoryAnalysis.AnalyzeProfile(Modules(null, null, 26), null, false).Verdict);
            Assert.Equal(MemoryProfileVerdict.Unknown, MemoryAnalysis.AnalyzeProfile(Modules(0, 0, 26), null, false).Verdict);
            Assert.Equal(MemoryProfileVerdict.Unknown, MemoryAnalysis.AnalyzeProfile(null, null, false).Verdict);
        }

        [Theory]
        [InlineData("80CE000000000000", "Samsung")]
        [InlineData("80AD000000000000", "SK Hynix")]
        [InlineData("802C", "Micron")]
        [InlineData("Kingston", "Kingston")]
        [InlineData("  Corsair  ", "Corsair")]
        [InlineData("0000000000000000", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        [InlineData("ABCD000000000000", "unknown vendor (code ABCD)")]
        public void DecodeManufacturer(string raw, string expected)
        {
            Assert.Equal(expected, MemoryAnalysis.DecodeManufacturer(raw));
        }
    }
}
