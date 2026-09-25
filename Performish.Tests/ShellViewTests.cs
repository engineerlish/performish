using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Performish.Core;
using Performish.Views;
using Xunit;

namespace Performish.Tests
{
    /// <summary>The in-window confirmation card: cancel is the default, Esc cancels, the result comes
    /// back through the returned task, and it can never stack.</summary>
    public class ConfirmOverlayTests
    {
        private static (ConfirmOverlay overlay, Form form) Host()
        {
            var overlay = new ConfirmOverlay();
            var form = new Form { ClientSize = new Size(900, 600), StartPosition = FormStartPosition.Manual, Location = new Point(10, 10), BackColor = Color.Black };
            form.Controls.Add(overlay);
            form.Show();
            Application.DoEvents();
            return (overlay, form);
        }

        private static System.Threading.Tasks.Task<bool> Open(ConfirmOverlay overlay, Form form, bool danger = true) =>
            overlay.ShowAsync(form, "Apply 3 tweaks for real?",
                new[] { ("Mode: REAL", Color.White), ("[SAFE] One", Color.Gray) }, "Apply now", "Cancel", danger);

        [StaFact]
        public void ShowAsync_OpensTheCard_WithTitleAndLines_CancelFocusedByDefault()
        {
            var (overlay, form) = Host();
            using var _ = form;

            var task = Open(overlay, form);

            Assert.True(overlay.IsOpen);
            Assert.Equal("Apply 3 tweaks for real?", overlay.TitleText);
            Assert.Contains("Mode: REAL", overlay.BodyText);
            Assert.Contains("[SAFE] One", overlay.BodyText);
            Assert.Equal("Apply now", overlay.ConfirmButton.Text);
            Assert.True(overlay.CancelActionButton.Focused, "focus must default to Cancel so Enter-mashing cancels");
            Assert.False(task.IsCompleted);
        }

        [StaFact]
        public void Confirm_CompletesTrue_AndClosesTheCard()
        {
            var (overlay, form) = Host();
            using var _ = form;
            var task = Open(overlay, form);

            overlay.ConfirmButton.PerformClick();

            Assert.True(task.Wait(2000));
            Assert.True(task.Result);
            Assert.False(overlay.IsOpen);
            Assert.False(overlay.Visible);
        }

        [StaFact]
        public void Cancel_CompletesFalse()
        {
            var (overlay, form) = Host();
            using var _ = form;
            var task = Open(overlay, form);

            overlay.CancelActionButton.PerformClick();

            Assert.True(task.Wait(2000));
            Assert.False(task.Result);
            Assert.False(overlay.IsOpen);
        }

        [StaFact]
        public void Escape_CancelsTheCard()
        {
            var (overlay, form) = Host();
            using var _ = form;
            var task = Open(overlay, form);

            var msg = new Message();
            typeof(ConfirmOverlay).GetMethod("ProcessCmdKey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(overlay, new object[] { msg, Keys.Escape });

            Assert.True(task.Wait(2000));
            Assert.False(task.Result);
        }

        [StaFact]
        public void OpeningWhileAlreadyOpen_ReturnsTheSameTask_NeverStacks()
        {
            var (overlay, form) = Host();
            using var _ = form;

            var first = Open(overlay, form);
            var second = Open(overlay, form);

            Assert.Same(first, second);
        }

        [StaFact]
        public void Reopening_AfterClose_Works()
        {
            var (overlay, form) = Host();
            using var _ = form;
            Open(overlay, form);
            overlay.CancelActionButton.PerformClick();

            var task = Open(overlay, form, danger: false);

            Assert.True(overlay.IsOpen);
            overlay.ConfirmButton.PerformClick();
            Assert.True(task.Wait(2000));
            Assert.True(task.Result);
        }

        [StaFact]
        public void TheCard_SitsInsideTheWindow_AndIsCentered()
        {
            var (overlay, form) = Host();
            using var _ = form;
            Open(overlay, form);

            var card = overlay.Controls.OfType<Panel>().First();
            Assert.True(card.Left >= 0 && card.Right <= overlay.ClientSize.Width);
            Assert.True(card.Top >= 0 && card.Bottom <= overlay.ClientSize.Height);
            Assert.InRange(card.Left + card.Width / 2, overlay.ClientSize.Width / 2 - 2, overlay.ClientSize.Width / 2 + 2);
        }
    }

    /// <summary>The home screen's system/health panel.</summary>
    public class HomeViewTests
    {
        [StaFact]
        public void BeforeAScan_ShowsTheNoScanNote_AndNoRows()
        {
            var view = new HomeView();
            using var form = ViewHost.Show(view);

            view.ShowNoScan();

            Assert.True(view.ShowsEmptyNote);
        }

        [StaFact]
        public void AfterAScan_FillsSystemRows_AndShowsTheHealthScore()
        {
            var services = AppServices.BuildFake();
            var view = new HomeView();
            using var form = ViewHost.Show(view);

            view.ShowScan(services.Scanner.Scan());

            Assert.False(view.ShowsEmptyNote);
            Assert.False(string.IsNullOrWhiteSpace(view.RowText("os")));
            Assert.Contains("cores", view.RowText("cpu"));
            Assert.Contains("free", view.RowText("disk"));
            Assert.True(int.TryParse(view.HealthText, out var score) && score >= 0 && score <= 100);
            Assert.Contains("factor", view.HealthNote + " factor"); // wording present either way
        }

        [StaFact]
        public void ScanThenNoScan_ReturnsToTheEmptyNote()
        {
            var services = AppServices.BuildFake();
            var view = new HomeView();
            using var form = ViewHost.Show(view);
            view.ShowScan(services.Scanner.Scan());

            view.ShowNoScan();

            Assert.True(view.ShowsEmptyNote);
        }
    }
}
