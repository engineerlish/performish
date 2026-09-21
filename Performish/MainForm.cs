using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Performish.Core;
using Performish.Core.Benchmark;
using Performish.Core.Models;
using Performish.Core.Scanner;
using Performish.Core.Reporting;
using Performish.Core.Tweaks;
using Performish.Dialogs;

namespace Performish
{
    /// <summary>Home screen: banner + system summary + status, and the primary action buttons.
    /// Everything that used to be a keybind-driven sub-screen (browse tweaks, presets, history,
    /// confirm, running, revert-confirm) is now a separate modal dialog under Dialogs/ - see
    /// DECISIONS.md "keybinds to buttons" for the full old-key -> new-button mapping.</summary>
    public sealed class MainForm : Form
    {
        // Neither is constructed here anymore (see OnFirstShownAsync): AppSettings.Load() and
        // AppServices.BuildReal() both do real file I/O (settings.json read, %LOCALAPPDATA% folder
        // creation, legacy-install migration, building the 55-tweak registry) - doing that in the
        // constructor blocked the very first paint. Phase 5 "startup: defer non-essential
        // initialization until after the first screen renders" moved both off the constructor path;
        // see PERFORMANCE_REPORT.md for the measured effect.
        private AppSettings _settings;
        private AppServices _services;

        private RichTextBox _console;
        private FlowLayoutPanel _buttonPanel;
        private CheckBox _dryRunCheckBox;
        private Button _revertButton;

        private SystemSnapshot _lastScan;
        // Feeds "Export report" - the last apply/revert batch's results and (if a real, non-dry-run
        // batch) its before/after benchmark. Null until a batch has actually run in this session.
        private System.Collections.Generic.List<TweakRunResult> _lastBatchResults;
        private bool _lastBatchWasDryRun;
        private BenchmarkComparison _lastBenchmark;

        public MainForm()
        {
            Text = "Performish";
            Width = 1040;
            Height = 680;
            BackColor = UiStyle.Background;
            Font = UiStyle.Mono;
            StartPosition = FormStartPosition.CenterScreen;
            Icon = LoadEmbeddedIcon();

            BuildLayout();
            Shown += async (s, e) => await OnFirstShownAsync();
            Resize += (s, e) => { if (_services != null) RenderHome(); };
        }

        private void BuildLayout()
        {
            _console = UiStyle.MakeConsole();

            _buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                BackColor = UiStyle.Background,
                Padding = new Padding(10),
                WrapContents = true
            };

            var scanButton = UiStyle.MakeButton("Scan");
            scanButton.Click += async (s, e) => await RunScanAsync();

            var browseButton = UiStyle.MakeButton("Browse tweaks...");
            browseButton.Click += (s, e) => OpenBrowser(null);

            var presetsButton = UiStyle.MakeButton("Presets...");
            presetsButton.Click += (s, e) => OpenPresetPicker();

            var historyButton = UiStyle.MakeButton("History...");
            historyButton.Click += (s, e) => OpenHistory();

            _revertButton = UiStyle.MakeButton("Revert everything...", UiStyle.Error);
            _revertButton.Enabled = false; // re-enabled once UpdateRevertButtonState() confirms a backup exists
            _revertButton.Click += async (s, e) => await RevertEverythingAsync();

            var benchmarkButton = UiStyle.MakeButton("Import frame-time CSV...");
            benchmarkButton.Click += (s, e) => ImportFrameTimeCsv();

            var driftButton = UiStyle.MakeButton("Check for drift...");
            driftButton.Click += async (s, e) => await CheckForDriftAsync();

            var reportButton = UiStyle.MakeButton("Export report...");
            reportButton.Click += (s, e) => ExportReport();

            // Default true (matches AppSettings' own default) until the real, persisted value loads
            // asynchronously - see OnFirstShownAsync(). Never mutates _settings before it exists.
            _dryRunCheckBox = UiStyle.MakeCheckBox("Dry run (nothing actually changes)");
            _dryRunCheckBox.Checked = true;
            _dryRunCheckBox.CheckedChanged += (s, e) =>
            {
                if (_settings == null) return; // still loading - RestoreLoadedState() applies the real value
                _settings.DryRunByDefault = _dryRunCheckBox.Checked;
                _settings.Save();
                RenderHome();
            };

            // Disabled until AppServices finishes loading in the background - every one of these
            // needs _services (a scan, the tweak registry, the change log, ...).
            foreach (var b in new[] { scanButton, browseButton, presetsButton, historyButton, benchmarkButton, driftButton, reportButton })
                b.Enabled = false;

            _buttonPanel.Controls.Add(scanButton);
            _buttonPanel.Controls.Add(browseButton);
            _buttonPanel.Controls.Add(presetsButton);
            _buttonPanel.Controls.Add(historyButton);
            _buttonPanel.Controls.Add(driftButton);
            _buttonPanel.Controls.Add(_revertButton);
            _buttonPanel.Controls.Add(benchmarkButton);
            _buttonPanel.Controls.Add(reportButton);
            _buttonPanel.Controls.Add(_dryRunCheckBox);

            var host = new Panel { Dock = DockStyle.Fill, BackColor = UiStyle.Background, Padding = new Padding(12) };
            host.Controls.Add(_console);

            Controls.Add(host);
            Controls.Add(_buttonPanel);
        }

        private bool DryRun => _dryRunCheckBox.Checked;

        // ---- First paint: render immediately, defer all backend/file I/O until after --------------

        private async Task OnFirstShownAsync()
        {
            RenderHome(); // banner + "initializing" line visible immediately, zero I/O done yet

            var (settings, services) = await Task.Run(() => (AppSettings.Load(), AppServices.BuildReal()));
            _settings = settings;
            _services = services;
            _dryRunCheckBox.Checked = _settings.DryRunByDefault;

            foreach (Control c in _buttonPanel.Controls) c.Enabled = true;
            _revertButton.Enabled = false; // still needs UpdateRevertButtonState() below

            RenderHome();
            UpdateRevertButtonState();
        }

        private void UpdateRevertButtonState()
        {
            // "Revert everything is disabled when no backup exists" - checked async off the UI
            // thread since it reads the change log file.
            Task.Run(() => _services.Runner.CurrentlyAppliedTweakIds().Count)
                .ContinueWith(t =>
                {
                    if (IsDisposed) return;
                    BeginInvoke((Action)(() => _revertButton.Enabled = !t.IsFaulted && t.Result > 0));
                }, TaskScheduler.Default);
        }

        // ---- Home screen -------------------------------------------------------------------------

        private void RenderHome()
        {
            _console.Clear();
            RenderBanner();

            AppendLine("", UiStyle.Foreground);
            AppendLine("  by Elijah English - precise, reversible Windows 11 debloat and performance tuning.", UiStyle.Dim);
            AppendLine("", UiStyle.Foreground);

            if (_services == null)
            {
                AppendLine("  Initializing...", UiStyle.Dim);
                return;
            }

            AppendLine("  elevation:   ", UiStyle.Dim, false);
            var elevated = Elevation.IsElevated();
            AppendLine(elevated ? "Administrator" : "NOT elevated - most tweaks will fail until Performish is run as admin",
                elevated ? UiStyle.Accent : UiStyle.Error);

            if (_services.Migration.AnythingMigrated)
            {
                AppendLine("  migrated:    ", UiStyle.Dim, false);
                AppendLine($"Found data from the old \"Ish\" install and carried it forward " +
                    $"({(_services.Migration.MigratedSettings ? "settings, " : "")}{_services.Migration.FilesMigrated} backup/log file(s)).",
                    UiStyle.BrightAccent);
            }

            AppendLine("", UiStyle.Foreground);

            if (_lastScan == null)
                AppendLine("  No scan yet - click Scan to read this machine (read-only, nothing changes).", UiStyle.Dim);
            else
                PrintScanSummary(_lastScan);
        }

        private void RenderBanner()
        {
            using var g = _console.CreateGraphics();
            var availableWidth = Math.Max(200, _console.ClientSize.Width - 8);
            var banner = AsciiArt.ChooseBanner(g, UiStyle.Mono, availableWidth);
            foreach (var line in banner)
                AppendLine(line, UiStyle.Accent);
        }

        private void PrintScanSummary(SystemSnapshot s)
        {
            var health = HealthScore.Compute(s);
            AppendLine("  health score:", UiStyle.Dim, false);
            AppendLine($"{health.Score}/{HealthScore.TotalMax}  (" +
                string.Join(", ", health.Factors.Select(f => $"{f.Name}: {f.Points}/{f.MaxPoints}")) + ")",
                health.Score >= 75 ? UiStyle.Accent : (health.Score >= 50 ? UiStyle.GradientMid : UiStyle.Error));

            AppendLine("  system:      ", UiStyle.Dim, false);
            AppendLine($"{s.WindowsProductName} ({s.WindowsEdition}), build {s.BuildNumber}.{s.UpdateBuildRevision}" +
                (s.IsWindows11 ? "" : "  [NOT Windows 11 - some tweaks may not apply]"),
                s.IsWindows11 ? UiStyle.Foreground : UiStyle.Error);

            AppendLine("  cpu:         ", UiStyle.Dim, false);
            AppendLine($"{s.CpuName}  ({s.CpuCoreCount} cores / {s.CpuLogicalProcessorCount} threads)", UiStyle.Foreground);

            AppendLine("  gpu:         ", UiStyle.Dim, false);
            AppendLine(s.Gpus.Count == 0 ? "(none detected)" : string.Join(", ", s.Gpus.Select(g => $"{g.Name} [{g.Vendor}] driver {g.DriverVersion}")), UiStyle.Foreground);

            AppendLine("  memory:      ", UiStyle.Dim, false);
            AppendLine($"{FormatBytes(s.UsedRamBytes)} used / {FormatBytes(s.TotalRamBytes)} total", UiStyle.Foreground);

            AppendLine("  power plan:  ", UiStyle.Dim, false);
            AppendLine(s.ActivePowerPlanName + (s.IsLaptop ? "  (laptop - battery-sensitive tweaks are flagged)" : ""), UiStyle.Foreground);

            AppendLine("  processes:   ", UiStyle.Dim, false);
            AppendLine($"{s.ProcessCount} running, {s.Services.Count(sv => sv.Running)} services running, {s.StartupItems.Count} startup item(s)", UiStyle.Foreground);

            AppendLine("  disk (C:):   ", UiStyle.Dim, false);
            var freePct = s.TotalDiskSpaceBytes > 0 ? (double)s.FreeDiskSpaceBytes / s.TotalDiskSpaceBytes * 100.0 : 0;
            AppendLine($"{FormatBytes(s.FreeDiskSpaceBytes)} free of {FormatBytes(s.TotalDiskSpaceBytes)} ({freePct:0.0}% free)",
                freePct < 10 ? UiStyle.Error : UiStyle.Foreground);

            AppendLine("  uptime:      ", UiStyle.Dim, false);
            AppendLine($"{s.UptimeHours:0.0} hour(s)", UiStyle.Foreground);

            if (s.IsDomainJoined || s.IsMdmManaged)
            {
                AppendLine("  managed:     ", UiStyle.Dim, false);
                AppendLine("This machine is domain-joined and/or MDM-managed - policy-scope tweaks may be " +
                    "overridden or reverted by Group Policy/Intune on the next refresh.", UiStyle.Error);
            }

            if (s.DetectedManagementAgents.Count > 0)
            {
                AppendLine("  detected:    ", UiStyle.Dim, false);
                AppendLine($"VPN/RMM/endpoint-security software running: {string.Join(", ", s.DetectedManagementAgents)}. " +
                    "Some tweaks touch services or background-app behavior this software may depend on - review " +
                    "before applying Moderate/Advanced tweaks.", UiStyle.GradientMid);
            }
        }

        private async Task RunScanAsync()
        {
            SetBusy(true);
            AppendLine("", UiStyle.Foreground);
            AppendLine("  Scanning (read-only)...", UiStyle.Dim);

            try
            {
                _lastScan = await Task.Run(() => _services.Scanner.Scan());
            }
            catch (Exception ex)
            {
                AppendLine($"  Scan failed: {ex.Message}", UiStyle.Error);
            }
            finally
            {
                SetBusy(false);
                RenderHome();
            }
        }

        private void SetBusy(bool busy)
        {
            foreach (Control c in _buttonPanel.Controls) c.Enabled = !busy;
            // The blanket enable above would incorrectly re-enable "Revert everything" even when
            // nothing is applied - re-derive its real state immediately after.
            if (!busy) UpdateRevertButtonState();
        }

        // ---- Tweak browser / presets --------------------------------------------------------------

        private void OpenBrowser(TweakCategory? category)
        {
            using var dialog = new TweakBrowserForm(_services.TweakRegistry.All, _services, Enumerable.Empty<string>(), category);
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.ConfirmedSelection.Count > 0)
                _ = ReviewAndApplyAsync(dialog.ConfirmedSelection);
        }

        private void OpenPresetPicker()
        {
            using var dialog = new PresetPickerForm(_services.TweakRegistry);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Chosen == null) return;

            var presetIds = _services.TweakRegistry.ByPreset(dialog.Chosen.Value).Select(t => t.Id);
            using var browser = new TweakBrowserForm(_services.TweakRegistry.All, _services, presetIds, null);
            if (browser.ShowDialog(this) == DialogResult.OK && browser.ConfirmedSelection.Count > 0)
                _ = ReviewAndApplyAsync(browser.ConfirmedSelection);
        }

        private async Task ReviewAndApplyAsync(System.Collections.Generic.List<TweakDefinition> selection)
        {
            var lines = new System.Collections.Generic.List<(string, Color)>
            {
                ($"Mode: {(DryRun ? "DRY RUN (nothing will actually change)" : "REAL - these changes will be applied")}",
                    DryRun ? UiStyle.BrightAccent : UiStyle.Error),
                ("", UiStyle.Foreground)
            };
            foreach (var t in selection.OrderBy(t => t.Risk))
            {
                lines.Add(($"[{UiStyle.RiskTag(t.Risk)}] {t.Title}", UiStyle.ColorForRisk(t.Risk)));
                lines.Add(($"    {t.Description}", UiStyle.Dim));
                if (t.RebootRequired) lines.Add(("    Requires a reboot to fully take effect.", UiStyle.GradientMid));
            }

            // Compatibility guardrail: warn (never block) if VPN/RMM/endpoint-security software was
            // seen running on the last scan and this batch includes anything beyond Safe risk - such
            // software often depends on the exact service/background-app behavior those tweaks touch.
            if (_lastScan != null && _lastScan.DetectedManagementAgents.Count > 0 && selection.Any(t => t.Risk != RiskLevel.Safe))
            {
                lines.Add(("", UiStyle.Foreground));
                lines.Add(($"Detected running: {string.Join(", ", _lastScan.DetectedManagementAgents)}. " +
                    "Review the Moderate/Advanced tweaks above before continuing - this software may depend " +
                    "on the service/background-app behavior they change.", UiStyle.GradientMid));
            }

            if (!DryRun)
            {
                lines.Add(("", UiStyle.Foreground));
                lines.Add(("A System Restore point will be created first (if System Protection allows it).", UiStyle.Dim));
            }

            var confirmed = ConfirmDialogForm.Show(this, "Review & apply", lines, "Apply", "Cancel");
            if (!confirmed) return;

            var wantsRestorePoint = _settings.CreateRestorePointByDefault && selection.Any(t => t.Risk != RiskLevel.Safe);
            var dryRun = DryRun;

            BenchmarkSnapshot before = null;
            if (!dryRun)
                before = BenchmarkSnapshot.FromSystemSnapshot(await Task.Run(() => _services.Scanner.Scan()));

            var batchResult = await RunningForm.RunAsync(this, "Applying", (token, onLog) => Task.Run(() =>
            {
                var ctx = _services.CreateContext(dryRun, onLog);
                return _services.Runner.ApplyBatch(selection, ctx, wantsRestorePoint, token);
            }));

            _lastBatchResults = batchResult.Results;
            _lastBatchWasDryRun = dryRun;
            _lastBenchmark = null;

            if (!dryRun && before != null)
            {
                var after = BenchmarkSnapshot.FromSystemSnapshot(await Task.Run(() => _services.Scanner.Scan()));
                _lastBenchmark = new BenchmarkComparison { Before = before, After = after };
                ShowBenchmarkComparison(_lastBenchmark);
            }

            UpdateRevertButtonState();
            RenderHome();
        }

        // ---- Revert everything ----------------------------------------------------------------

        private async Task RevertEverythingAsync()
        {
            var appliedIds = await Task.Run(() => _services.Runner.CurrentlyAppliedTweakIds());
            var toRevert = _services.TweakRegistry.All.Where(t => appliedIds.Contains(t.Id)).ToList();

            if (toRevert.Count == 0)
            {
                MessageDialog.Show(this, "Nothing to revert",
                    "No tweak is currently recorded as applied by Performish.");
                return;
            }

            var lines = new System.Collections.Generic.List<(string, Color)>
            {
                ($"{toRevert.Count} tweak(s) will be undone:", UiStyle.Error)
            };
            foreach (var t in toRevert) lines.Add(($"  {t.Title}", UiStyle.Foreground));
            lines.Add(("", UiStyle.Foreground));
            lines.Add(($"Mode: {(DryRun ? "DRY RUN (nothing will actually change)" : "REAL - these changes will be reverted")}",
                DryRun ? UiStyle.BrightAccent : UiStyle.Error));

            var confirmed = ConfirmDialogForm.Show(this, "Revert everything", lines, "Revert", "Cancel");
            if (!confirmed) return;

            var dryRun = DryRun;
            var batchResult = await RunningForm.RunAsync(this, "Reverting", (token, onLog) => Task.Run(() =>
            {
                var ctx = _services.CreateContext(dryRun, onLog);
                return _services.Runner.UndoBatch(toRevert, ctx, token);
            }));

            _lastBatchResults = batchResult.Results;
            _lastBatchWasDryRun = dryRun;
            _lastBenchmark = null;

            UpdateRevertButtonState();
            RenderHome();
        }

        // ---- Drift detection --------------------------------------------------------------------

        private async Task CheckForDriftAsync()
        {
            SetBusy(true);
            AppendLine("", UiStyle.Foreground);
            AppendLine("  Checking for drift (comparing recorded-applied tweaks against live state)...", UiStyle.Dim);

            List<TweakDefinition> drifted;
            try
            {
                drifted = await Task.Run(() =>
                {
                    var ctx = _services.CreateContext(dryRun: true); // read-only - Check() never writes
                    return _services.Runner.DetectDrift(_services.TweakRegistry, ctx);
                });
            }
            finally
            {
                SetBusy(false);
            }

            if (drifted.Count == 0)
            {
                AppendLine("  No drift detected - everything Performish recorded as applied still checks out.", UiStyle.Accent);
                return;
            }

            var lines = new System.Collections.Generic.List<(string, Color)>
            {
                ($"{drifted.Count} tweak(s) were applied by Performish but no longer check out as applied - " +
                    "something (a Windows Update, a Group Policy refresh, or manual change) reverted them:", UiStyle.GradientMid)
            };
            foreach (var t in drifted) lines.Add(($"  {t.Title}", UiStyle.Foreground));
            lines.Add(("", UiStyle.Foreground));
            lines.Add(($"Mode: {(DryRun ? "DRY RUN (nothing will actually change)" : "REAL - these will be reapplied")}",
                DryRun ? UiStyle.BrightAccent : UiStyle.Error));

            var confirmed = ConfirmDialogForm.Show(this, "Drifted tweaks detected", lines, "Reapply", "Not now");
            if (confirmed) await ReviewAndApplyAsync(drifted);
        }

        // ---- Report export ----------------------------------------------------------------------

        private void ExportReport()
        {
            if (_lastScan == null)
            {
                MessageDialog.Show(this, "No scan yet", "Click Scan first - a report needs at least a system scan to report on.");
                return;
            }

            using var dialog = new SaveFileDialog
            {
                Title = "Export report",
                Filter = "HTML files (*.html)|*.html",
                FileName = $"performish-report-{DateTime.Now:yyyyMMdd-HHmmss}.html"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                var report = new ReportData
                {
                    MachineName = Environment.MachineName,
                    Scan = _lastScan,
                    HealthBefore = HealthScore.Compute(_lastScan),
                    BatchResults = _lastBatchResults,
                    WasDryRun = _lastBatchWasDryRun,
                    Benchmark = _lastBenchmark
                };
                System.IO.File.WriteAllText(dialog.FileName, HtmlReportWriter.Render(report));

                AppendLine("", UiStyle.Foreground);
                AppendLine($"  Exported report to {dialog.FileName}", UiStyle.Accent);
            }
            catch (Exception ex)
            {
                AppendLine("", UiStyle.Foreground);
                AppendLine($"  Report export failed: {ex.Message}", UiStyle.Error);
            }
        }

        // ---- History --------------------------------------------------------------------------

        private void OpenHistory()
        {
            using var dialog = new HistoryForm(_services, () => DryRun);
            dialog.ShowDialog(this);
            UpdateRevertButtonState(); // an Undo-this-tweak click inside History can change this
        }

        // ---- Benchmark: frame-time CSV import ---------------------------------------------------

        private void ImportFrameTimeCsv()
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Import a PresentMon or CapFrameX frame-time CSV",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                var report = FrameTimeReader.Parse(dialog.FileName);
                AppendLine("", UiStyle.Foreground);
                if (report.SampleCount == 0)
                {
                    AppendLine("  Could not find a recognizable frame-time column in that file.", UiStyle.Error);
                    return;
                }

                AppendLine($"  {report.SampleCount} frame samples from {System.IO.Path.GetFileName(dialog.FileName)}:", UiStyle.Accent);
                AppendLine($"    average:    {report.AverageFps:0.0} fps ({report.AverageFrameTimeMs:0.00} ms)", UiStyle.Foreground);
                AppendLine($"    1% low:     {report.P1LowFps:0.0} fps", UiStyle.Foreground);
                AppendLine($"    0.1% low:   {report.P0Point1LowFps:0.0} fps", UiStyle.Foreground);
                AppendLine("  These are measured results only - Performish makes no performance claim beyond what this file reports.", UiStyle.Dim);
            }
            catch (Exception ex)
            {
                AppendLine("", UiStyle.Foreground);
                AppendLine($"  Could not read that file: {ex.Message}", UiStyle.Error);
            }
        }

        private void ShowBenchmarkComparison(BenchmarkComparison c)
        {
            AppendLine("", UiStyle.Foreground);
            AppendLine("  Before/after (measured, this machine, this batch only):", UiStyle.Dim);
            AppendLine($"    RAM used:        {(c.UsedRamDeltaBytes <= 0 ? "" : "+")}{FormatBytes(c.UsedRamDeltaBytes)}",
                c.UsedRamDeltaBytes < 0 ? UiStyle.Accent : (c.UsedRamDeltaBytes > 0 ? UiStyle.Error : UiStyle.Dim));
            AppendLine($"    process count:   {(c.ProcessCountDelta <= 0 ? "" : "+")}{c.ProcessCountDelta}",
                c.ProcessCountDelta < 0 ? UiStyle.Accent : (c.ProcessCountDelta > 0 ? UiStyle.Error : UiStyle.Dim));
            AppendLine($"    services running:{(c.RunningServiceCountDelta <= 0 ? "" : "+")}{c.RunningServiceCountDelta}",
                c.RunningServiceCountDelta < 0 ? UiStyle.Accent : (c.RunningServiceCountDelta > 0 ? UiStyle.Error : UiStyle.Dim));
            AppendLine($"    boot-impact items:{(c.BootImpactItemCountDelta <= 0 ? "" : "+")}{c.BootImpactItemCountDelta}  " +
                "(startup entries + enabled scheduled tasks - a proxy, not a measured boot time)",
                c.BootImpactItemCountDelta < 0 ? UiStyle.Accent : (c.BootImpactItemCountDelta > 0 ? UiStyle.Error : UiStyle.Dim));
        }

        // ---- Formatting helpers -----------------------------------------------------------------

        private static string FormatBytes(long bytes) => $"{bytes / 1024.0 / 1024.0 / 1024.0:0.0} GB";

        private void AppendLine(string text, Color color, bool newLine = true)
        {
            void Do()
            {
                _console.SelectionStart = _console.TextLength;
                _console.SelectionLength = 0;
                _console.SelectionColor = color;
                _console.AppendText(newLine ? text + Environment.NewLine : text);
                _console.SelectionStart = _console.TextLength;
                _console.ScrollToCaret();
            }

            if (InvokeRequired) BeginInvoke((Action)Do);
            else Do();
        }

        public static Icon LoadEmbeddedIcon()
        {
            var assembly = typeof(MainForm).Assembly;
            var resourceName = Array.Find(assembly.GetManifestResourceNames(),
                n => n.EndsWith(".ico", StringComparison.OrdinalIgnoreCase));
            if (resourceName == null) return null;

            using var stream = assembly.GetManifestResourceStream(resourceName);
            return stream == null ? null : new Icon(stream);
        }
    }
}
