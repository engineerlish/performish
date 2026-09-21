using System;

namespace Performish.Core.Models
{
    /// <summary>One tweak, defined entirely as data plus three small delegates. New tweaks are added
    /// by appending to a library file (Tweaks/*.cs in Performish.Core) - the engine, UI, backup, and test
    /// harness never need to change. This is the "tweaks as data" architecture the task requires.</summary>
    public sealed class TweakDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public TweakCategory Category { get; }
        public RiskLevel Risk { get; }
        public TweakScope Scope { get; }
        public bool RebootRequired { get; }

        /// <summary>Citation/justification for why this tweak is here - shown in the tweak reference
        /// table (README) and in the UI's detail view. Never a bare "trust me".</summary>
        public string Source { get; }

        /// <summary>True for the small protected set (Windows Update core, Defender, networking,
        /// audio, drivers, Store) that must never be touched by a preset/"apply all" - only by an
        /// explicit, individually-confirmed selection, if ever exposed at all.</summary>
        public bool Protected { get; }

        public Preset[] IncludedInPresets { get; }

        public Func<TweakExecutionContext, TweakState> Check { get; }
        public Func<TweakExecutionContext, TweakOperationResult> Apply { get; }
        public Func<TweakExecutionContext, TweakOperationResult> Undo { get; }

        public TweakDefinition(
            string id,
            string name,
            string description,
            TweakCategory category,
            RiskLevel risk,
            TweakScope scope,
            bool rebootRequired,
            string source,
            Func<TweakExecutionContext, TweakState> check,
            Func<TweakExecutionContext, TweakOperationResult> apply,
            Func<TweakExecutionContext, TweakOperationResult> undo,
            Preset[] includedInPresets = null,
            bool isProtected = false)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Tweak id is required.", nameof(id));

            Id = id;
            Name = name;
            Description = description;
            Category = category;
            Risk = risk;
            Scope = scope;
            RebootRequired = rebootRequired;
            Source = source;
            Check = check ?? (_ => TweakState.Unknown);
            Apply = apply ?? throw new ArgumentNullException(nameof(apply));
            Undo = undo ?? (_ => TweakOperationResult.Skipped("No undo defined for this tweak."));
            IncludedInPresets = includedInPresets ?? Array.Empty<Preset>();
            Protected = isProtected;
        }
    }
}
