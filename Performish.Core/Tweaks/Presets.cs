using Performish.Core.Models;

namespace Performish.Core.Tweaks
{
    /// <summary>Human-readable copy for each preset - membership itself lives on each
    /// TweakDefinition.IncludedInPresets, this is just the label/description the UI shows.</summary>
    public static class Presets
    {
        public static string Name(Preset preset) => preset switch
        {
            Preset.Conservative => "Conservative",
            Preset.Balanced => "Balanced",
            Preset.Aggressive => "Aggressive",
            _ => "Custom"
        };

        public static string Description(Preset preset) => preset switch
        {
            Preset.Conservative => "Safe-only, reversible, cosmetic/consumer-content tweaks. No services, " +
                "no policy changes, nothing that needs a reboot.",
            Preset.Balanced => "Conservative plus Moderate-risk debloat, power, and startup-impact tweaks. " +
                "The recommended default for most workstations.",
            Preset.Aggressive => "Everything Safe/Moderate, plus every Advanced tweak (Recall, Ultimate " +
                "Performance, GPU scheduling, full fullscreen-optimization override). Review the list " +
                "before applying - this is the \"I know what I'm doing\" preset.",
            _ => "Hand-picked selection, not one of the built-in presets."
        };
    }
}
