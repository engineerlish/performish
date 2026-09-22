using System;
using System.Collections.Generic;
using System.Linq;
using Performish.Core.Models;

namespace Performish.Core.Tweaks
{
    /// <summary>Holds every tweak definition the app knows about, gathered from the library classes
    /// in this folder. Adding a tweak means adding one TweakDefinition to a library file's
    /// All() list - nothing here changes.</summary>
    public sealed class TweakRegistry
    {
        private readonly List<TweakDefinition> _tweaks;

        public TweakRegistry(IEnumerable<TweakDefinition> tweaks)
        {
            _tweaks = tweaks.ToList();

            var duplicateIds = _tweaks.GroupBy(t => t.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (duplicateIds.Count > 0)
                throw new InvalidOperationException("Duplicate tweak id(s): " + string.Join(", ", duplicateIds));

            ValidateSchema();
        }

        /// <summary>Fails fast at load time, naming the offending tweak, instead of letting a
        /// malformed definition surface later as a confusing null-reference or "why is this tweak
        /// invisible" bug somewhere in the UI. Every message below is written so the person fixing it
        /// knows exactly which tweak and which field to look at.</summary>
        private void ValidateSchema()
        {
            var errors = new List<string>();

            var titleOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // title -> first tweak id that used it

            foreach (var t in _tweaks)
            {
                var label = string.IsNullOrWhiteSpace(t.Id) ? "(tweak with no id)" : t.Id;

                if (string.IsNullOrWhiteSpace(t.Id)) errors.Add($"{label}: Id is null/empty.");

                if (string.IsNullOrWhiteSpace(t.Title))
                {
                    errors.Add($"{label}: Title is null/empty - every tweak needs a plain-language, " +
                        "action-led title (e.g. \"Disable advertising ID tracking\"), not just an id.");
                }
                else
                {
                    if (string.Equals(t.Title.Trim(), t.Id.Trim(), StringComparison.OrdinalIgnoreCase))
                        errors.Add($"{label}: Title is the same as the Id (\"{t.Title}\") - the title must be a " +
                            "human-readable label, not the internal identifier.");

                    if (titleOwners.TryGetValue(t.Title.Trim(), out var firstOwner))
                        errors.Add($"{label}: Title \"{t.Title}\" is already used by tweak \"{firstOwner}\" - " +
                            "titles must be unique so users can tell tweaks apart.");
                    else
                        titleOwners[t.Title.Trim()] = t.Id;
                }

                if (string.IsNullOrWhiteSpace(t.Description)) errors.Add($"{label}: Description is null/empty.");
                if (string.IsNullOrWhiteSpace(t.Source)) errors.Add($"{label}: Source is null/empty.");

                if (!Enum.IsDefined(typeof(TweakCategory), t.Category))
                    errors.Add($"{label}: Category value {(int)t.Category} is not a defined TweakCategory.");
                if (!Enum.IsDefined(typeof(RiskLevel), t.Risk))
                    errors.Add($"{label}: Risk value {(int)t.Risk} is not a defined RiskLevel.");
                if (!Enum.IsDefined(typeof(TweakScope), t.Scope))
                    errors.Add($"{label}: Scope value {(int)t.Scope} is not a defined TweakScope.");

                if (t.IncludedInPresets == null)
                {
                    errors.Add($"{label}: IncludedInPresets is null (should be an empty array, never null).");
                }
                else
                {
                    foreach (var p in t.IncludedInPresets)
                    {
                        if (p == Preset.Custom)
                            errors.Add($"{label}: IncludedInPresets lists Preset.Custom, which ByPreset() always " +
                                "treats as empty - this tweak would silently never appear in any preset. Remove it " +
                                "from IncludedInPresets (Custom means \"hand-picked, no preset\", it is never a " +
                                "membership itself).");
                        else if (!Enum.IsDefined(typeof(Preset), p))
                            errors.Add($"{label}: IncludedInPresets contains value {(int)p}, which is not a defined Preset.");
                    }
                }
            }

            if (errors.Count > 0)
                throw new InvalidOperationException(
                    $"{errors.Count} tweak definition error(s) found at load time:{Environment.NewLine}  " +
                    string.Join(Environment.NewLine + "  ", errors));
        }

        /// <summary>Builds the registry from every shipped tweak library (Debloat, Performance,
        /// Gaming, Network). This is the single place that has to change if a new library file is
        /// added.</summary>
        public static TweakRegistry BuildDefault() => new TweakRegistry(
            DebloatTweaks.All()
                .Concat(PerformanceTweaks.All())
                .Concat(GamingTweaks.All())
                .Concat(NetworkTweaks.All())
                .Concat(MaintenanceTweaks.All()));

        public IReadOnlyList<TweakDefinition> All => _tweaks;

        /// <summary>Falls back to StartupItemTweaks.TryResolveFromId() when the id isn't in this
        /// registry's own fixed list - a startup-item tweak (see StartupItemTweaks) is never part of
        /// the fixed catalog BuildDefault() assembles, but its id alone is enough to reconstruct a
        /// working Check/Apply/Undo, which is what lets a change-log entry for one stay undoable from
        /// the History dialog (TweakRegistry.Find(entry.TweakId)) in any session, not just the one
        /// that originally showed it.</summary>
        public TweakDefinition Find(string id) =>
            _tweaks.FirstOrDefault(t => t.Id == id) ?? StartupItemTweaks.TryResolveFromId(id);

        public IEnumerable<TweakDefinition> ByCategory(TweakCategory category) => _tweaks.Where(t => t.Category == category);

        public IEnumerable<TweakDefinition> ByPreset(Preset preset) =>
            preset == Preset.Custom ? Enumerable.Empty<TweakDefinition>() : _tweaks.Where(t => t.IncludedInPresets.Contains(preset));

        public IEnumerable<TweakDefinition> Unprotected => _tweaks.Where(t => !t.Protected);
    }
}
