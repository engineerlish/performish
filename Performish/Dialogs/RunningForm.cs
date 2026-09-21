using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Performish.Core.Models;
using Performish.Core.Tweaks;

namespace Performish.Dialogs
{
    /// <summary>Modal progress/result dialog for an apply/undo/revert batch. Never blocks the UI
    /// thread - the batch runs on a background task while this dialog's own message loop keeps
    /// pumping, so the live log keeps appending and Cancel stays clickable throughout (Phase 5 "UI
    /// responsiveness ... cancel support where safe"). Close is disabled until the batch finishes so
    /// the dialog can't be dismissed mid-run out from under a live TweakExecutionContext.</summary>
    public sealed class RunningForm : Form
    {
        private readonly RichTextBox _log;
        private readonly Button _cancelButton;
        private readonly Button _closeButton;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public BatchRunResult Result { get; private set; }

        public RunningForm(string title)
        {
            Text = title;
            Width = 760;
            Height = 540;
            BackColor = UiStyle.Background;
            Font = UiStyle.Mono;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ControlBox = false; // closing via the X mid-run would abandon a live context; use Close button

            _log = UiStyle.MakeConsole();

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                BackColor = UiStyle.Background,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(10)
            };

            _closeButton = UiStyle.MakeButton("Close");
            _closeButton.Enabled = false;
            _closeButton.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };

            _cancelButton = UiStyle.MakeButton("Cancel", UiStyle.Error);
            _cancelButton.Click += (s, e) =>
            {
                _cts.Cancel();
                _cancelButton.Enabled = false;
                _cancelButton.Text = "Cancelling...";
            };

            buttonPanel.Controls.Add(_closeButton);
            buttonPanel.Controls.Add(_cancelButton);

            var host = new Panel { Dock = DockStyle.Fill, BackColor = UiStyle.Background, Padding = new Padding(12) };
            host.Controls.Add(_log);

            Controls.Add(host);
            Controls.Add(buttonPanel);

            CancelButton = null; // Esc must not silently dismiss a running/just-finished batch dialog
        }

        private void AppendLine(string text, Color color)
        {
            void Do()
            {
                _log.SelectionStart = _log.TextLength;
                _log.SelectionLength = 0;
                _log.SelectionColor = color;
                _log.AppendText(text + Environment.NewLine);
                _log.SelectionStart = _log.TextLength;
                _log.ScrollToCaret();
            }

            if (_log.InvokeRequired) _log.BeginInvoke((Action)Do);
            else Do();
        }

        /// <summary>Runs work(ctx) on a background thread, streaming ctx.Log() calls into the dialog
        /// live, then shows the dialog modally. work is expected to construct its own
        /// TweakExecutionContext using the CancellationToken this method hands it.</summary>
        public static async Task<BatchRunResult> RunAsync(IWin32Window owner, string title,
            Func<CancellationToken, Action<string>, Task<BatchRunResult>> work)
        {
            using var dialog = new RunningForm(title);

            // Force the dialog's Win32 window handle to exist, synchronously, on the UI thread -
            // BEFORE any background work starts. Root cause of the "Failed to set Win32 parent
            // window of the Control" crash (see DECISIONS.md "RunningForm handle-creation race"):
            // Control.InvokeRequired silently returns false when nothing in the control's parent
            // chain has a handle yet, so if the background task below calls ctx.Log() -> AppendLine()
            // before ShowDialog() (further down) has created the handle, that first log line runs
            // directly on the background thread instead of being marshaled via BeginInvoke -
            // corrupting the control's thread affinity right as the UI thread tries to create the
            // real Win32 window a moment later. A dry-run batch applies near-instantly (no real
            // registry/service/PowerShell I/O happens - see TweakOperationResult.Preview), so its
            // very first ctx.Log() call can easily win this race; a real (non-dry-run) batch is
            // usually slow enough on a real machine that ShowDialog wins by default, which is why
            // this went unnoticed until a fast dry-run batch (Balanced, 36 tweaks) made it reliably
            // reproducible. Accessing .Handle here forces WinForms to create the window on this (UI)
            // thread now; ShowDialog(owner) below still re-parents it to `owner` correctly even
            // though the handle already exists (Form.ShowDialog explicitly re-applies GWL_HWNDPARENT
            // in that case) - this is a normal, supported WinForms pattern, not a workaround.
            _ = dialog.Handle;

            var runTask = Task.Run(() => work(dialog._cts.Token, line => dialog.AppendLine("  " + line, UiStyle.Dim)));

            _ = runTask.ContinueWith(t =>
            {
                dialog.BeginInvoke((Action)(() =>
                {
                    if (t.IsFaulted)
                    {
                        dialog.AppendLine("", UiStyle.Foreground);
                        dialog.AppendLine($"  Unexpected error: {t.Exception?.GetBaseException().Message}", UiStyle.Error);
                        dialog.Result = new BatchRunResult();
                    }
                    else
                    {
                        dialog.Result = t.Result;
                        var succeeded = 0;
                        var failed = 0;
                        foreach (var r in dialog.Result.Results) { if (r.Failed) failed++; else succeeded++; }

                        dialog.AppendLine("", UiStyle.Foreground);
                        if (dialog.Result.Cancelled)
                            dialog.AppendLine($"  Cancelled. {succeeded} succeeded, {failed} failed before stopping.", UiStyle.Dim);
                        else
                            dialog.AppendLine($"  Done. {succeeded} succeeded, {failed} failed.", failed > 0 ? UiStyle.Error : UiStyle.Accent);
                    }

                    dialog._cancelButton.Enabled = false;
                    dialog._closeButton.Enabled = true;
                    dialog._closeButton.Focus();
                }));
            }, TaskScheduler.Default);

            dialog.ShowDialog(owner);
            return dialog.Result ?? new BatchRunResult();
        }
    }
}
