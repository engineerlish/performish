namespace Performish.Core.Models
{
    /// <summary>How risky a tweak is to apply. Drives default preset membership and whether
    /// the UI requires an extra confirmation before running it.</summary>
    public enum RiskLevel
    {
        Safe,
        Moderate,
        Advanced
    }

    public enum TweakCategory
    {
        Debloat,
        Performance,
        Gaming,
        Network,
        /// <summary>Disk-space reclamation (caches, leftovers) - split out from Performance because
        /// these tweaks free space rather than change a setting, and (unlike every other tweak) their
        /// Undo is always Skipped (deleted files can't come back) - grouping them makes that honest
        /// up front in the UI instead of surprising the user tweak-by-tweak.</summary>
        Maintenance
    }

    /// <summary>Who/what a tweak's effect applies to - shown in the UI so the user knows the blast
    /// radius before applying.</summary>
    public enum TweakScope
    {
        CurrentUser,
        AllUsers,
        Machine
    }

    /// <summary>Result of running a tweak's Check() against the live/simulated system.</summary>
    public enum TweakState
    {
        Applied,
        NotApplied,
        NotApplicable,
        Unknown
    }

    public enum Preset
    {
        Custom,
        Conservative,
        Balanced,
        Aggressive
    }

    public enum OperationOutcome
    {
        Success,
        Skipped,
        Failed
    }
}
