using System.Drawing;
using Performish.Core.Models;
using Performish.Core.Tweaks;

namespace Performish
{
    public enum ResultStatus { Failed, Skipped, DryRunPreview, Success }

    /// <summary>How a tweak run result is classified, ordered, marked and summarized - shared by the
    /// in-window Results view and the RunningForm dialog (still used by History's per-tweak undo) so
    /// the two can never disagree about what counts as a failure or how it is worded.</summary>
    public static class ResultPresenter
    {
        public static ResultStatus Classify(TweakRunResult r)
        {
            if (r.Failed) return ResultStatus.Failed;
            if (r.Result?.WasDryRun == true) return ResultStatus.DryRunPreview;
            if (r.Result?.Outcome == OperationOutcome.Skipped) return ResultStatus.Skipped;
            return ResultStatus.Success;
        }

        /// <summary>Failed first, then skipped, then dry-run previews, then success - failures can't be missed.</summary>
        public static int SortOrder(ResultStatus s) => s switch
        {
            ResultStatus.Failed => 0,
            ResultStatus.Skipped => 1,
            ResultStatus.DryRunPreview => 2,
            ResultStatus.Success => 3,
            _ => 4
        };

        /// <summary>Never color alone - every row also carries a bracketed text marker.</summary>
        public static (string Marker, Color Color) StyleFor(ResultStatus status) => status switch
        {
            ResultStatus.Failed => ("[FAILED]", UiStyle.Error),
            ResultStatus.Skipped => ("[SKIPPED]", UiStyle.Dim),
            ResultStatus.DryRunPreview => ("[DRY RUN]", UiStyle.GradientMid),
            ResultStatus.Success => ("[OK]", UiStyle.Accent),
            _ => ("[?]", UiStyle.Dim)
        };

        /// <summary>One line, never a raw stack trace.</summary>
        public static string ShortReason(TweakRunResult r)
        {
            var message = r.Result?.Message ?? r.Exception?.Message ?? "";
            var firstLine = message.Split('\n')[0].Trim().TrimEnd('\r');
            return firstLine.Length > 100 ? firstLine.Substring(0, 97) + "..." : firstLine;
        }
    }
}
