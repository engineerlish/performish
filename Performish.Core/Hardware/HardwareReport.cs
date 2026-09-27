using System;
using System.Collections.Generic;
using System.Linq;

namespace Performish.Core.Hardware
{
    /// <summary>How a hardware reading was obtained - shown next to every value so a user can always tell
    /// a direct read from an inference or a gap, per the "never present a number that wasn't measured"
    /// rule (same rule MetricSampleResult.Unavailable enforces for benchmarks).</summary>
    public enum ReadingStatus
    {
        /// <summary>Read directly from Windows or the GPU driver on this machine, just now.</summary>
        Measured,
        /// <summary>Derived from measured values by a stated rule (e.g. "running above the standard
        /// speed, so an XMP/EXPO profile is probably on") - never shown as certain.</summary>
        Inferred,
        /// <summary>Could not be read on this machine; <see cref="HardwareReading.Note"/> says why.</summary>
        Unavailable,
        /// <summary>Doesn't apply to this hardware (e.g. Resizable BAR on a GPU that can't support it).</summary>
        NotApplicable
    }

    public enum HardwareComponent
    {
        Firmware,
        Motherboard,
        Cpu,
        Memory,
        Gpu
    }

    /// <summary>Whether a reading bears on how fast the machine runs, or is identity/security context.
    /// The dialog groups by component and doesn't use this yet; it's kept so benchmark hardware context
    /// (FEASIBILITY_BIOS_OVERCLOCK.md, section 4.4) can pick out the performance-relevant readings.</summary>
    public enum ReadingConcern
    {
        Performance,
        Informational
    }

    public sealed class HardwareReading
    {
        public string Key { get; set; }
        public string Label { get; set; }
        /// <summary>Display value. Null/empty when <see cref="Status"/> is Unavailable or NotApplicable.</summary>
        public string Value { get; set; }
        public ReadingStatus Status { get; set; }
        public ReadingConcern Concern { get; set; } = ReadingConcern.Informational;
        public HardwareComponent Component { get; set; }
        /// <summary>Where the value came from (WMI class, registry key, NVML call) - never blank for a
        /// Measured reading, so every number is traceable.</summary>
        public string Source { get; set; }
        /// <summary>Why a value is unavailable/inferred/not applicable, or a short caveat.</summary>
        public string Note { get; set; }

        public bool IsReadable => Status == ReadingStatus.Measured || Status == ReadingStatus.Inferred;
    }

    public enum FindingKind
    {
        /// <summary>Checked and fine - worth confirming to the user.</summary>
        Good,
        /// <summary>A likely free performance gain the user can act on outside Performish (BIOS, vendor tool).</summary>
        Opportunity,
        /// <summary>A trade-off or fact worth knowing; no recommendation either way.</summary>
        Info
    }

    /// <summary>One plain-language conclusion drawn from the readings. Every finding is guide-only:
    /// Performish never changes firmware, clocks, voltages or power limits (see
    /// FEASIBILITY_BIOS_OVERCLOCK.md), so there is deliberately no "apply" action here.</summary>
    public sealed class HardwareFinding
    {
        public string Key { get; set; }
        public FindingKind Kind { get; set; }
        public HardwareComponent Component { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }
        public string Guidance { get; set; }
        /// <summary>Keys of the readings this finding was drawn from.</summary>
        public List<string> ReadingKeys { get; set; } = new List<string>();
    }

    public sealed class HardwareGroup
    {
        public HardwareComponent Component { get; set; }
        public string Title { get; set; }
        public List<HardwareReading> Readings { get; set; } = new List<HardwareReading>();
    }

    /// <summary>The read-only result of one hardware/firmware read. Building one never writes anything.</summary>
    public sealed class HardwareReport
    {
        public DateTime TakenAtUtc { get; set; } = DateTime.UtcNow;
        public bool IsLaptop { get; set; }
        public List<HardwareGroup> Groups { get; set; } = new List<HardwareGroup>();
        public List<HardwareFinding> Findings { get; set; } = new List<HardwareFinding>();

        /// <summary>True when the NVIDIA GPU on a switchable-graphics laptop was left asleep instead of
        /// being woken to read it - the UI offers an explicit "read it anyway" refresh.</summary>
        public bool DiscreteGpuSkipped { get; set; }

        public IEnumerable<HardwareReading> AllReadings => Groups.SelectMany(g => g.Readings);

        public HardwareReading Find(string key) => AllReadings.FirstOrDefault(r => r.Key == key);

        public HardwareGroup Group(HardwareComponent component) => Groups.FirstOrDefault(g => g.Component == component);
    }

    /// <summary>Options for one read. Plain data so the UI and tests can pass it around.</summary>
    public sealed class HardwareReadOptions
    {
        /// <summary>On a switchable-graphics laptop the NVIDIA GPU is usually powered off at idle, and
        /// initializing NVML wakes it. Off by default there; always read on desktops.</summary>
        public bool WakeDiscreteGpu { get; set; }
    }
}
