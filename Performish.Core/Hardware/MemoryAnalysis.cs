using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Performish.Core.Hardware
{
    public enum MemoryProfileVerdict
    {
        /// <summary>Not enough data to say anything.</summary>
        Unknown,
        /// <summary>Running above the standard (JEDEC) speed or above the modules' own base rating - an
        /// XMP/EXPO profile or manual tuning is almost certainly on.</summary>
        ProfileLikelyOn,
        /// <summary>Running at the fastest memory speed the CPU officially supports - expected, and not
        /// something a BIOS profile setting should "fix" (e.g. i7-10750H: DDR4-3200 modules at 2933).</summary>
        AtCpuSupportedMaximum,
        /// <summary>Running slower than the modules' reported rating, for no reason Performish can see.</summary>
        BelowModuleRating,
        /// <summary>Desktop running at a standard JEDEC speed. The kit may or may not be sold as faster -
        /// Windows can't see an XMP/EXPO rating, so this is a "check your kit" hint, not a verdict.</summary>
        StandardSpeedCheckKit,
        /// <summary>Laptop memory at its rated speed - laptops normally have no XMP/EXPO setting.</summary>
        LaptopAtRatedSpeed
    }

    public sealed class MemoryProfileAnalysis
    {
        public MemoryProfileVerdict Verdict { get; set; }
        public int? ConfiguredMts { get; set; }
        public int? RatedMts { get; set; }
        public int? CpuSupportedMaxMts { get; set; }
        public string MemoryTypeName { get; set; }
        public string Explanation { get; set; }
    }

    /// <summary>Pure memory-configuration interpretation. Everything here is heuristic and is reported as
    /// Inferred, never Measured. The CPU memory-speed table matters: without it, a laptop whose CPU caps
    /// memory below the modules' rating would be told to "turn on XMP" - wrong advice that was caught on
    /// real hardware during the feasibility study (see FEASIBILITY_BIOS_OVERCLOCK.md).</summary>
    public static class MemoryAnalysis
    {
        public static string MemoryTypeName(int? smbiosType) => smbiosType switch
        {
            24 => "DDR3",
            26 => "DDR4",
            30 => "LPDDR4",
            34 => "DDR5",
            35 => "LPDDR5",
            _ => null
        };

        /// <summary>Highest standard (JEDEC) speed common consumer platforms run without a profile. Above
        /// this, a profile or manual tuning is almost certainly active. Null = unknown type, no threshold.</summary>
        public static int? StandardMaxMts(int? smbiosType) => smbiosType switch
        {
            24 => 1600,
            26 => 3200,
            34 => 5600,
            _ => null
        };

        private sealed class CpuMemoryLimit
        {
            public Regex Pattern;
            public int? Ddr4;
            public int? Ddr5;
        }

        // Official maximum memory speed per CPU family (vendor spec sheets). Deliberately short and
        // conservative: an unknown CPU simply gets no CPU-limit reasoning, which never produces advice,
        // whereas a wrong entry could. Extend only from vendor spec pages.
        private static readonly CpuMemoryLimit[] CpuLimits =
        {
            new CpuMemoryLimit { Pattern = new Regex(@"\bRyzen \d 9\d\d0(X3D|X|F)?(\s|$)", RegexOptions.IgnoreCase), Ddr5 = 5600 },
            new CpuMemoryLimit { Pattern = new Regex(@"\bRyzen \d 7\d\d0(X3D|X|F)?(\s|$)", RegexOptions.IgnoreCase), Ddr5 = 5200 },
            new CpuMemoryLimit { Pattern = new Regex(@"\bRyzen \d 5\d\d0(X3D|X|G|GE)?(\s|$)", RegexOptions.IgnoreCase), Ddr4 = 3200 },
            new CpuMemoryLimit { Pattern = new Regex(@"\bUltra [579] 2\d5(K|KF|F)?\b", RegexOptions.IgnoreCase), Ddr5 = 6400 },
            new CpuMemoryLimit { Pattern = new Regex(@"\bi[579]-1[34]\d{3}(K|KF|KS)\b", RegexOptions.IgnoreCase), Ddr4 = 3200, Ddr5 = 5600 },
            new CpuMemoryLimit { Pattern = new Regex(@"\bi[3579]-12\d{3}(K|KF|F)?\b", RegexOptions.IgnoreCase), Ddr4 = 3200, Ddr5 = 4800 },
            new CpuMemoryLimit { Pattern = new Regex(@"\bi[3579]-11\d{3}(K|KF|F)?\b", RegexOptions.IgnoreCase), Ddr4 = 3200 },
            new CpuMemoryLimit { Pattern = new Regex(@"\bi[579]-10\d{3}H\b", RegexOptions.IgnoreCase), Ddr4 = 2933 },
            new CpuMemoryLimit { Pattern = new Regex(@"\bi[79]-10\d{3}(K|KF|F)?\b", RegexOptions.IgnoreCase), Ddr4 = 2933 },
        };

        /// <summary>The CPU's official maximum memory speed for this memory type, or null if unknown.</summary>
        public static int? CpuSupportedMaxMts(string cpuName, int? smbiosType)
        {
            if (string.IsNullOrWhiteSpace(cpuName)) return null;
            foreach (var limit in CpuLimits)
            {
                if (!limit.Pattern.IsMatch(cpuName)) continue;
                return smbiosType switch
                {
                    26 => limit.Ddr4,
                    34 => limit.Ddr5,
                    _ => null
                };
            }
            return null;
        }

        public static MemoryProfileAnalysis AnalyzeProfile(IReadOnlyList<MemoryModuleRaw> modules, string cpuName, bool isLaptop)
        {
            var result = new MemoryProfileAnalysis { Verdict = MemoryProfileVerdict.Unknown };
            var usable = (modules ?? Array.Empty<MemoryModuleRaw>()).Where(m => m != null).ToList();

            // The memory controller runs every channel at one speed; the slowest reported value is the
            // truthful one if modules disagree.
            var configured = usable.Select(m => m.ConfiguredSpeedMts).Where(v => v.HasValue && v.Value > 0).Select(v => v.Value).DefaultIfEmpty(0).Min();
            var rated = usable.Select(m => m.RatedSpeedMts).Where(v => v.HasValue && v.Value > 0).Select(v => v.Value).DefaultIfEmpty(0).Min();
            var type = usable.Select(m => m.SmbiosMemoryType).FirstOrDefault(t => t.HasValue && MemoryTypeName(t) != null);

            result.ConfiguredMts = configured > 0 ? configured : (int?)null;
            result.RatedMts = rated > 0 ? rated : (int?)null;
            result.MemoryTypeName = MemoryTypeName(type);
            result.CpuSupportedMaxMts = CpuSupportedMaxMts(cpuName, type);

            if (result.ConfiguredMts == null)
            {
                result.Explanation = "Windows didn't report the memory's running speed.";
                return result;
            }

            var standardMax = StandardMaxMts(type);
            var threshold = standardMax.HasValue ? Math.Max(standardMax.Value, result.CpuSupportedMaxMts ?? 0) : (int?)null;
            var isSodimm = isLaptop || usable.Any(m => m.FormFactor == 12);

            if ((threshold.HasValue && configured > threshold.Value) || (result.RatedMts.HasValue && configured > result.RatedMts.Value))
            {
                result.Verdict = MemoryProfileVerdict.ProfileLikelyOn;
                result.Explanation = $"Running at {configured} MT/s, above the standard speed for this memory, so an XMP/EXPO profile or manual memory tuning is almost certainly on.";
                return result;
            }

            if (result.CpuSupportedMaxMts.HasValue && configured >= result.CpuSupportedMaxMts.Value
                && (!result.RatedMts.HasValue || result.RatedMts.Value >= result.CpuSupportedMaxMts.Value))
            {
                result.Verdict = MemoryProfileVerdict.AtCpuSupportedMaximum;
                result.Explanation = result.RatedMts.HasValue && result.RatedMts.Value > configured
                    ? $"Running at {configured} MT/s: the fastest speed this CPU officially supports. The modules are rated {result.RatedMts} MT/s, but the CPU sets the limit here, so this is expected, not a missed setting."
                    : $"Running at {configured} MT/s, the fastest speed this CPU officially supports.";
                return result;
            }

            if (result.RatedMts.HasValue && configured < result.RatedMts.Value)
            {
                result.Verdict = MemoryProfileVerdict.BelowModuleRating;
                result.Explanation = isSodimm
                    ? $"Running at {configured} MT/s, below the modules' rated {result.RatedMts} MT/s. Laptop firmware usually doesn't let you change this."
                    : $"Running at {configured} MT/s, below the modules' rated {result.RatedMts} MT/s. The BIOS memory settings may be set lower than the kit supports.";
                return result;
            }

            if (isSodimm)
            {
                result.Verdict = MemoryProfileVerdict.LaptopAtRatedSpeed;
                result.Explanation = $"Running at {configured} MT/s, the modules' rated speed. Laptop memory normally has no XMP/EXPO profile.";
                return result;
            }

            if (standardMax.HasValue)
            {
                result.Verdict = MemoryProfileVerdict.StandardSpeedCheckKit;
                result.Explanation = $"Running at {configured} MT/s, a standard speed. If your kit is sold as faster (for example DDR5-6000), its XMP/EXPO profile is probably off. Windows can't see a kit's XMP/EXPO rating, so check the kit's label or part number.";
                return result;
            }

            result.Explanation = $"Running at {configured} MT/s.";
            return result;
        }

        // JEDEC JEP106 manufacturer IDs as Windows reports them in Win32_PhysicalMemory.Manufacturer
        // (continuation-bank byte + ID byte, parity bit included). Only well-known, verified codes.
        private static readonly Dictionary<string, string> JedecVendors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["80CE"] = "Samsung",
            ["80AD"] = "SK Hynix",
            ["802C"] = "Micron",
            ["859B"] = "Crucial",
            ["0198"] = "Kingston",
            ["029E"] = "Corsair",
            ["04CD"] = "G.Skill",
            ["04CB"] = "ADATA",
            ["830B"] = "Nanya",
        };

        /// <summary>Turns "80CE000000000000" into "Samsung". Plain names pass through; unknown codes are
        /// returned as-is (labeled) rather than guessed.</summary>
        public static string DecodeManufacturer(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var trimmed = raw.Trim();
            if (trimmed.Length >= 4 && Regex.IsMatch(trimmed, "^[0-9A-Fa-f]+$"))
            {
                if (trimmed.Trim('0').Length == 0) return null; // all-zero placeholder, not a vendor
                if (JedecVendors.TryGetValue(trimmed.Substring(0, 4), out var name)) return name;
                return $"unknown vendor (code {trimmed.TrimEnd('0')})";
            }
            return trimmed;
        }
    }
}
