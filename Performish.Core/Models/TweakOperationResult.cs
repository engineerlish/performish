namespace Performish.Core.Models
{
    /// <summary>Outcome of one Apply/Undo call. A tweak batch keeps going past a Failed result for
    /// one tweak - see TweakRunner - so this must never throw for expected failure modes (missing
    /// key, access denied, etc.); only truly unexpected exceptions propagate.</summary>
    public sealed class TweakOperationResult
    {
        public OperationOutcome Outcome { get; }
        public string Message { get; }
        public bool WasDryRun { get; }

        private TweakOperationResult(OperationOutcome outcome, string message, bool wasDryRun)
        {
            Outcome = outcome;
            Message = message;
            WasDryRun = wasDryRun;
        }

        public static TweakOperationResult Success(string message) =>
            new TweakOperationResult(OperationOutcome.Success, message, false);

        public static TweakOperationResult Skipped(string message) =>
            new TweakOperationResult(OperationOutcome.Skipped, message, false);

        public static TweakOperationResult Failed(string message) =>
            new TweakOperationResult(OperationOutcome.Failed, message, false);

        /// <summary>Dry-run preview - no backend write happened. Always Success outcome so batch UI
        /// treats a preview run the same as a real one for progress purposes, but WasDryRun tells the
        /// caller not to record it as an applied change.</summary>
        public static TweakOperationResult Preview(string message) =>
            new TweakOperationResult(OperationOutcome.Success, message, true);
    }
}
