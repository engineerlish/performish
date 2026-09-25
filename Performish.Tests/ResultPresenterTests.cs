using Performish.Core.Models;
using Performish.Core.Tweaks;
using Xunit;

namespace Performish.Tests
{
    public class ResultPresenterTests
    {
        private static TweakRunResult MakeResult(TweakOperationResult result = null, System.Exception exception = null) =>
            new TweakRunResult { Result = result, Exception = exception };

        [Fact]
        public void Classify_ExceptionThrown_IsFailedEvenIfResultIsNull()
        {
            var r = MakeResult(exception: new System.InvalidOperationException("boom"));

            Assert.Equal(ResultStatus.Failed, ResultPresenter.Classify(r));
        }

        [Fact]
        public void Classify_FailedOutcome_IsFailedEvenIfWasDryRun()
        {
            var r = MakeResult(result: TweakOperationResult.Failed("could not write registry key"));

            Assert.Equal(ResultStatus.Failed, ResultPresenter.Classify(r));
        }

        [Fact]
        public void Classify_DryRunPreview_TakesPrecedenceOverSkipped()
        {
            var r = MakeResult(result: TweakOperationResult.Preview("would delete 3 files"));

            Assert.Equal(ResultStatus.DryRunPreview, ResultPresenter.Classify(r));
        }

        [Fact]
        public void Classify_SkippedOutcome_IsSkipped()
        {
            var r = MakeResult(result: TweakOperationResult.Skipped("nothing to clean"));

            Assert.Equal(ResultStatus.Skipped, ResultPresenter.Classify(r));
        }

        [Fact]
        public void Classify_SuccessOutcome_IsSuccess()
        {
            var r = MakeResult(result: TweakOperationResult.Success("done"));

            Assert.Equal(ResultStatus.Success, ResultPresenter.Classify(r));
        }

        [Theory]
        [InlineData(ResultStatus.Failed, ResultStatus.Skipped)]
        [InlineData(ResultStatus.Skipped, ResultStatus.DryRunPreview)]
        [InlineData(ResultStatus.DryRunPreview, ResultStatus.Success)]
        public void SortOrder_OrdersFailuresBeforeLaterStatuses(ResultStatus earlier, ResultStatus later)
        {
            Assert.True(ResultPresenter.SortOrder(earlier) < ResultPresenter.SortOrder(later));
        }

        [Theory]
        [InlineData(ResultStatus.Failed, "[FAILED]")]
        [InlineData(ResultStatus.Skipped, "[SKIPPED]")]
        [InlineData(ResultStatus.DryRunPreview, "[DRY RUN]")]
        [InlineData(ResultStatus.Success, "[OK]")]
        public void StyleFor_EveryStatus_HasABracketedMarker(ResultStatus status, string expectedMarker)
        {
            var (marker, _) = ResultPresenter.StyleFor(status);

            Assert.Equal(expectedMarker, marker);
        }

        [Fact]
        public void ShortReason_MultiLineMessage_KeepsOnlyFirstLine()
        {
            var r = MakeResult(result: TweakOperationResult.Success("Freed 2.0 MB\r\nacross 3 file(s)"));

            Assert.Equal("Freed 2.0 MB", ResultPresenter.ShortReason(r));
        }

        [Fact]
        public void ShortReason_MessageLongerThan100Chars_TruncatesWithEllipsis()
        {
            var longMessage = new string('x', 150);
            var r = MakeResult(result: TweakOperationResult.Success(longMessage));

            var shortened = ResultPresenter.ShortReason(r);

            Assert.Equal(100, shortened.Length);
            Assert.EndsWith("...", shortened);
            Assert.Equal(new string('x', 97) + "...", shortened);
        }

        [Fact]
        public void ShortReason_MessageAtOrUnder100Chars_IsNotTruncated()
        {
            var message = new string('x', 100);
            var r = MakeResult(result: TweakOperationResult.Success(message));

            Assert.Equal(message, ResultPresenter.ShortReason(r));
        }

        [Fact]
        public void ShortReason_NoResultButHasException_FallsBackToExceptionMessage()
        {
            var r = MakeResult(exception: new System.InvalidOperationException("access denied"));

            Assert.Equal("access denied", ResultPresenter.ShortReason(r));
        }
    }
}
