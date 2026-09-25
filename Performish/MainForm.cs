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
using Performish.Views;

namespace Performish
{
    /// <summary>The main window: a left navigation rail (Home / Tweaks / Results) and one content area
    /// that swaps between in-window views under Views/. Browsing and choosing tweaks, the apply
    /// confirmation, and the apply/undo results all happen in this window (no popups); the smaller
    /// utility dialogs (health score, startup items, history, benchmarks, settings) remain modal
    /// dialogs under Dialogs/.</summary>
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

        private enum ViewKind { Home, Tweaks, Results }

        private HomeView _home;
        private TweaksView _tweaks;
        private ResultsView _results;
        private ConfirmOverlay _overlay;
        private Panel _content, _nav;
        private readonly Dictionary<ViewKind, Button> _navButtons = new Dictionary<ViewKind, Button>();
        private ViewKind _currentView = ViewKind.Home;
        private RichTextBox _console;
        private FlowLayoutPanel _buttonPanel;
        private CheckBox _dryRunCheckBox;
        private Button _revertButton;
        private Button _benchmarkModeButton;

        // The buttons actually need enabling/disabling as a group (loading-gate, busy-gate) - tracked
        // separately from _buttonPanel's own Controls now that buttons live nested inside per-section
        // sub-panels (see UiStyle.MakeButtonSection) rather than as _buttonPanel's direct children.
        private readonly List<Control> _actionControls = new List<Control>();

        private SystemSnapshot _lastScan;
        // Feeds "Export report" - the last apply/revert batch's results and (if a real, non-dry-run
        // batch) its before/after benchmark. Null until a batch has actually run in this session.
        private System.Collections.Generic.List<TweakRunResult> _lastBatchResults;
        private bool _lastBatchWasDryRun;
        private BenchmarkComparison _lastBenchmark;
        private BenchmarkComparisonReport _lastRealBenchmark;

        // Set only by the (services, settings) constructor below, so tests can run the whole window
        // against fake backends and never touch AppServices.BuildReal().
        private readonly (AppSettings Settings, AppServices Services)? _injected;

        /// <summary>Builds the window around already-constructed services/settings (used by tests with
        /// AppServices.BuildFake()). The parameterless constructor loads the real ones after first paint.</summary>
        public MainForm(AppServices services, AppSettings settings) : this()
        {
            _injected = (settings, services);
        }

        /// <summary>Test hook: runs the same post-first-paint initialization the Shown event runs.</summary>
        public Task InitializeForTestingAsync() => OnFirstShownAsync();

        public MainForm()
        {
            Text = "Performish";
            Width = 1320;
            Height = 820;
            MinimumSize = new Size(1040, 680);
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
            _home = new HomeView { Dock = DockStyle.Fill };
            _console = _home.Console;

            _buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                BackColor = UiStyle.Background,
                Padding = new Padding(0),
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false
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

            var startupButton = UiStyle.MakeButton("Startup items...");
            startupButton.Click += (s, e) => OpenStartupItems();

            var healthButton = UiStyle.MakeButton("Health score...");
            healthButton.Click += (s, e) => OpenHealthScore();

            var runBenchmarkButton = UiStyle.MakeButton("Run benchmark now");
            runBenchmarkButton.Click += async (s, e) => await RunStandaloneBenchmarkAsync();

            var benchmarkHistoryButton = UiStyle.MakeButton("Benchmark history...");
            benchmarkHistoryButton.Click += (s, e) => OpenBenchmarkHistory();

            // Cycles Off -> Quick -> Full -> Off - a toggle rather than a dropdown/picker dialog, so
            // "show an estimated time... let the user skip or choose a quick subset" doesn't need its
            // own screen. Label always shows the current mode plus its time estimate.
            _benchmarkModeButton = UiStyle.MakeButton("Benchmark: Quick");
            _benchmarkModeButton.Click += (s, e) => CycleBenchmarkMode();

            var reportButton = UiStyle.MakeButton("Export report...");
            reportButton.Click += (s, e) => ExportReport();

            var settingsButton = UiStyle.MakeButton("Settings...");
            settingsButton.Click += (s, e) => OpenSettings();

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
            // needs _services (a scan, the tweak registry, the change log, ...). Settings only needs
            // _settings (loads at the same time) so it's gated the same way for a consistent "nothing
            // is clickable until initialization finishes" feel.
            _actionControls.AddRange(new Control[]
            {
                scanButton, browseButton, presetsButton, _revertButton, healthButton, startupButton,
                driftButton, runBenchmarkButton, benchmarkHistoryButton, _benchmarkModeButton,
                benchmarkButton, historyButton, reportButton, settingsButton
            });
            foreach (var c in _actionControls) c.Enabled = false;

            // Grouped into labeled sections instead of one flat wrapped row of 14 controls - see
            // UiStyle.MakeButtonSection and NEXT_DIRECTIONS.md "Direction C: day-to-day usability".
            _buttonPanel.Controls.Add(UiStyle.MakeButtonSection("Tweaks",
                scanButton, browseButton, presetsButton, _revertButton));
            _buttonPanel.Controls.Add(UiStyle.MakeButtonSection("Diagnostics",
                healthButton, startupButton, driftButton));
            _buttonPanel.Controls.Add(UiStyle.MakeButtonSection("Benchmarking",
                runBenchmarkButton, benchmarkHistoryButton, _benchmarkModeButton, benchmarkButton));
            _buttonPanel.Controls.Add(UiStyle.MakeButtonSection("History & reports",
                historyButton, reportButton));
            _buttonPanel.Controls.Add(UiStyle.MakeButtonSection("Options", settingsButton));
            _home.AddBottom(_buttonPanel);

            _results = new ResultsView { Dock = DockStyle.Fill, Visible = false, Padding = new Padding(24, 16, 24, 0) };
            _results.BackRequested += () => ShowView(_tweaks != null ? ViewKind.Tweaks : ViewKind.Home);
            _results.ExportRequested += ExportReport;
            _overlay = new ConfirmOverlay();

            _content = new Panel { Dock = DockStyle.Fill, BackColor = UiStyle.Background };
            _content.Controls.Add(_home);
            _content.Controls.Add(_results);
            _content.Controls.Add(_overlay);

            BuildNavRail();
            Controls.Add(_content);
            Controls.Add(_nav);
            ShowView(ViewKind.Home);
        }

        private void BuildNavRail()
        {
            _nav = new Panel { Dock = DockStyle.Left, Width = 184, BackColor = UiStyle.Panel };
            _nav.Paint += (s, e) => { using var p = new Pen(UiStyle.Line); e.Graphics.DrawLine(p, _nav.Width - 1, 0, _nav.Width - 1, _nav.Height); };

            var mark = new Label { Text = "performish", Font = UiStyle.MonoBold, ForeColor = UiStyle.Accent, BackColor = UiStyle.Panel, AutoSize = true, Location = new Point(18, 20), AccessibleName = "Performish" };
            var cursor = new Panel { BackColor = UiStyle.Accent, Size = new Size(8, UiStyle.Mono.Height - 2), Location = new Point(18 + 10 * UiStyle.CharWidth + 8, 22), TabStop = false };
            _nav.Controls.Add(mark);
            _nav.Controls.Add(cursor);

            int y = 76;
            foreach (var (kind, label, key) in new[] { (ViewKind.Home, "Home", "F1"), (ViewKind.Tweaks, "Tweaks", "F2"), (ViewKind.Results, "Results", "F3") })
            {
                var k = kind;
                var button = new Button
                {
                    Text = label,
                    FlatStyle = FlatStyle.Flat,
                    Font = UiStyle.Mono,
                    ForeColor = UiStyle.Dim,
                    BackColor = UiStyle.Panel,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(16, 0, 0, 0),
                    Size = new Size(_nav.Width - 1, 42),
                    Location = new Point(0, y),
                    UseMnemonic = false,
                    UseVisualStyleBackColor = false,
                    Cursor = Cursors.Hand,
                    AccessibleName = label,
                    AccessibleDescription = $"Show the {label} screen ({key})"
                };
                button.FlatAppearance.BorderSize = 0;
                button.FlatAppearance.MouseOverBackColor = UiStyle.Panel2;
                button.FlatAppearance.MouseDownBackColor = UiStyle.ButtonPressed;
                button.Click += (s, e) => ShowView(k);
                var hint = new Label { Text = key, Font = UiStyle.MonoSmall, ForeColor = UiStyle.Faint, BackColor = Color.Transparent, AutoSize = true, Enabled = false };
                button.Controls.Add(hint);
                hint.Location = new Point(button.Width - 40, 13);
                _navButtons[kind] = button;
                _nav.Controls.Add(button);
                y += 46;
            }

            var dryBox = new Panel { Dock = DockStyle.Bottom, Height = 128, BackColor = UiStyle.Panel, Padding = new Padding(14, 10, 10, 10) };
            var note = new Label { Text = "With dry run on, nothing on this machine changes.", ForeColor = UiStyle.Faint, Font = UiStyle.MonoSmall, BackColor = UiStyle.Panel, Dock = DockStyle.Fill };
            _dryRunCheckBox.BackColor = UiStyle.Panel;
            _dryRunCheckBox.AutoSize = false;
            _dryRunCheckBox.Dock = DockStyle.Top;
            _dryRunCheckBox.Height = 44;
            _dryRunCheckBox.Text = "Dry run";
            dryBox.Controls.Add(note);
            dryBox.Controls.Add(_dryRunCheckBox);
            _nav.Controls.Add(dryBox);
        }

        private void ShowView(ViewKind view)
        {
            if (view == ViewKind.Tweaks && _tweaks == null) view = ViewKind.Home; // not loaded yet
            _currentView = view;
            _home.Visible = view == ViewKind.Home;
            if (_tweaks != null) _tweaks.Visible = view == ViewKind.Tweaks;
            _results.Visible = view == ViewKind.Results;
            foreach (var (kind, button) in _navButtons)
            {
                bool active = kind == view;
                button.BackColor = active ? UiStyle.Panel2 : UiStyle.Panel;
                button.ForeColor = active ? UiStyle.Foreground : UiStyle.Dim;
                button.Font = active ? UiStyle.MonoBold : UiStyle.Mono;
                button.AccessibleName = active ? $"{button.Text}, current screen" : button.Text;
            }
            _overlay.BringToFront();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!_overlay.IsOpen)
            {
                switch (keyData)
                {
                    case Keys.F1: ShowView(ViewKind.Home); return true;
                    case Keys.F2: ShowView(ViewKind.Tweaks); return true;
                    case Keys.F3: ShowView(ViewKind.Results); return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private bool DryRun => _dryRunCheckBox.Checked;

        // ---- First paint: render immediately, defer all backend/file I/O until after --------------

        private async Task OnFirstShownAsync()
        {
            RenderHome(); // banner + "initializing" line visible immediately, zero I/O done yet

            var (settings, services) = _injected ?? await Task.Run(() => (AppSettings.Load(), AppServices.BuildReal()));
            _settings = settings;
            _services = services;
            _dryRunCheckBox.Checked = _settings.DryRunByDefault;
            UpdateBenchmarkModeButtonText();

            foreach (var c in _actionControls) c.Enabled = true;
            _revertButton.Enabled = false; // still needs UpdateRevertButtonState() below

            _tweaks = new TweaksView(_services.TweakRegistry.All, _services, Enumerable.Empty<string>(), null)
            {
                Dock = DockStyle.Fill,
                Visible = false,
                Padding = new Padding(24, 12, 24, 0)
            };
            _tweaks.ReviewRequested += selection => _ = ReviewAndApplyAsync(selection);
            _content.Controls.Add(_tweaks);
            ShowView(_currentView);

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
            {
                _home.ShowNoScan();
                AppendLine("  No scan yet - click Scan to read this machine (read-only, nothing changes).", UiStyle.Dim);
            }
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

        /// <summary>The numeric summary now lives in the home screen's system/health panel; the console
        /// keeps only what needs a sentence of explanation.</summary>
        private void PrintScanSummary(SystemSnapshot s)
        {
            _home.ShowScan(s);
            AppendLine("  Scan complete. The system and health panel on the right has the details.", UiStyle.Accent);

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
            foreach (var c in _actionControls) c.Enabled = !busy;
            _tweaks?.SetBusy(busy);
            // The blanket enable above would incorrectly re-enable "Revert everything" even when
            // nothing is applied - re-derive its real state immediately after.
            if (!busy) UpdateRevertButtonState();
        }

        // ---- Tweak browser / presets --------------------------------------------------------------

        private void OpenBrowser(TweakCategory? category)
        {
            if (_tweaks == null) return;
            _tweaks.ShowCategory(category);
            ShowView(ViewKind.Tweaks);
        }

        private void OpenStartupItems()
        {
            if (_lastScan == null)
            {
                MessageDialog.Show(this, "No scan yet", "Click Scan first - startup items come from a system scan.");
                return;
            }

            using var dialog = new StartupItemsForm(_lastScan.StartupItems);
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.ConfirmedSelection.Count > 0)
                _ = ReviewAndApplyAsync(dialog.ConfirmedSelection);
        }

        private void OpenHealthScore()
        {
            if (_lastScan == null)
            {
                MessageDialog.Show(this, "No scan yet", "Click Scan first - the health score needs a system scan.");
                return;
            }

            using var dialog = new HealthScoreForm(HealthScore.Compute(_lastScan));
            dialog.ShowDialog(this);
        }

        private void OpenSettings()
        {
            using var dialog = new SettingsForm(_settings);
            dialog.ShowDialog(this);
            UpdateBenchmarkModeButtonText(); // BenchmarkIncludeNetwork feeds the mode button's time estimate
        }

        // ---- Benchmarking -------------------------------------------------------------------------

        private void CycleBenchmarkMode()
        {
            _settings.BenchmarkModeDefault = _settings.BenchmarkModeDefault switch
            {
                BenchmarkMode.Off => BenchmarkMode.Quick,
                BenchmarkMode.Quick => BenchmarkMode.Full,
                _ => BenchmarkMode.Off
            };
            _settings.Save();
            UpdateBenchmarkModeButtonText();
        }

        private void UpdateBenchmarkModeButtonText()
        {
            var options = BuildBenchmarkOptions();
            _benchmarkModeButton.Text = _settings.BenchmarkModeDefault == BenchmarkMode.Off
                ? "Benchmark: Off"
                : $"Benchmark: {_settings.BenchmarkModeDefault} (~{BenchmarkSuiteRunner.EstimateSeconds(options):0}s)";
        }

        private BenchmarkOptions BuildBenchmarkOptions()
        {
            var options = _settings.BenchmarkModeDefault == BenchmarkMode.Full ? BenchmarkOptions.Full() : BenchmarkOptions.Quick();
            options.IncludeNetwork = _settings.BenchmarkIncludeNetwork;
            return options;
        }

        /// <summary>Captures a real benchmark run, tied to the tweaks about to be applied. Returns
        /// null when benchmarking is turned Off, so callers don't need to check the setting twice.</summary>
        private async Task<BenchmarkRun> CaptureBenchmarkAsync(BenchmarkRunKind kind, string label,
            IEnumerable<string> tweakIds, bool isDryRunPreview, int? healthScore, string pairId)
        {
            if (_settings.BenchmarkModeDefault == BenchmarkMode.Off) return null;

            var options = BuildBenchmarkOptions();
            var run = await Task.Run(() => _services.Benchmarks.Run(options, kind, label, tweakIds, isDryRunPreview, healthScore, pairId));
            _services.BenchmarkHistory.Save(run);
            return run;
        }

        private void ShowBenchmarkComparisonDialog(BenchmarkRun before, BenchmarkRun after)
        {
            var options = BuildBenchmarkOptions();
            var higherIsBetter = _services.Benchmarks.HigherIsBetterByMetric(options);
            var report = BenchmarkComparer.Compare(before, after, higherIsBetter);
            _lastRealBenchmark = report; // feeds "Export report" - see ExportReport()
            using var dialog = new BenchmarkComparisonForm(report);
            dialog.ShowDialog(this);
        }

        private async Task RunStandaloneBenchmarkAsync()
        {
            SetBusy(true);
            AppendLine("", UiStyle.Foreground);
            AppendLine("  Running benchmark checkpoint...", UiStyle.Dim);
            BenchmarkRun run;
            try
            {
                var options = BuildBenchmarkOptions();
                if (_settings.BenchmarkModeDefault == BenchmarkMode.Off) options = BenchmarkOptions.Quick(); // "run now" always measures something, even if auto-benchmarking is off
                var healthScore = _lastScan != null ? HealthScore.Compute(_lastScan).Score : (int?)null;
                run = await Task.Run(() => _services.Benchmarks.Run(options, BenchmarkRunKind.Standalone, "Manual checkpoint", healthScore: healthScore));
                _services.BenchmarkHistory.Save(run);
            }
            finally
            {
                SetBusy(false);
            }

            AppendLine("  Benchmark checkpoint saved.", UiStyle.Accent);

            var previous = _services.BenchmarkHistory.ReadRecent(2).FirstOrDefault(r => r.Id != run.Id);
            if (previous != null)
            {
                ShowBenchmarkComparisonDialog(previous, run);
            }
            else
            {
                MessageDialog.Show(this, "Benchmark saved",
                    "First checkpoint saved - run it again later (or after applying tweaks) to see a comparison.");
            }
        }

        private void OpenBenchmarkHistory()
        {
            using var dialog = new BenchmarkHistoryForm(_services.BenchmarkHistory.ReadRecent(200));
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedPair != null)
                ShowBenchmarkComparisonDialog(dialog.SelectedPair.Value.Older, dialog.SelectedPair.Value.Newer);
        }

        private void OpenPresetPicker()
        {
            if (_tweaks == null) return;
            ShowView(ViewKind.Tweaks);
            _tweaks.FocusPresets();
        }

        /// <summary>The in-window confirmation card (see ConfirmOverlay). Danger styling when the action
        /// is real (not a dry run).</summary>
        private Task<bool> ConfirmAsync(string title, IEnumerable<(string Text, Color Color)> lines, string confirmLabel, string cancelLabel, bool danger)
        {
            return _overlay.ShowAsync(_content, title, lines, confirmLabel, cancelLabel, danger);
        }

        /// <summary>Runs a batch in the in-window Results view.</summary>
        private Task<BatchRunResult> RunBatchAsync(string title, IReadOnlyList<TweakDefinition> tweaks,
            Performish.Core.Backup.ChangeLogAction action, bool dryRun, bool wantsRestorePoint = false)
        {
            ShowView(ViewKind.Results);
            return _results.RunAsync(_services, title, tweaks, action, dryRun, wantsRestorePoint);
        }

        private async Task ReviewAndApplyAsync(System.Collections.Generic.List<TweakDefinition> selection)
        {
            var lines = new System.Collections.Generic.List<(string, Color)>
            {
                ($"Mode: {(DryRun ? "DRY RUN (nothing will actually change)" : "REAL - these changes will be applied")}",
                    DryRun ? UiStyle.BrightAccent : UiStyle.Error),
                ("", UiStyle.Foreground)
            };
            lines.Insert(1, ($"{selection.Count} tweak(s): {selection.Count(t => t.Risk == RiskLevel.Safe)} safe, " +
                $"{selection.Count(t => t.Risk == RiskLevel.Moderate)} moderate, {selection.Count(t => t.Risk == RiskLevel.Advanced)} advanced", UiStyle.Foreground));
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

            if (_settings.BenchmarkModeDefault != BenchmarkMode.Off)
            {
                lines.Add(("", UiStyle.Foreground));
                lines.Add(($"Benchmark: {_settings.BenchmarkModeDefault} (~{BenchmarkSuiteRunner.EstimateSeconds(BuildBenchmarkOptions()):0}s before and after) - " +
                    "change with the \"Benchmark:\" button.", UiStyle.Dim));
            }

            var confirmed = await ConfirmAsync(DryRun ? $"Run a dry run of {selection.Count} tweaks?" : $"Apply {selection.Count} tweaks for real?",
                lines, DryRun ? "Run dry run" : "Apply now", "Cancel", danger: !DryRun);
            if (!confirmed) return;

            SetBusy(true);
            try
            {
                var wantsRestorePoint = _settings.CreateRestorePointByDefault && selection.Any(t => t.Risk != RiskLevel.Safe);
                var dryRun = DryRun;
                var tweakIds = selection.Select(t => t.Id).ToList();
                var pairId = Guid.NewGuid().ToString("N");

                BenchmarkSnapshot before = null;
                if (!dryRun)
                    before = BenchmarkSnapshot.FromSystemSnapshot(await Task.Run(() => _services.Scanner.Scan()));

                var beforeHealthScore = _lastScan != null ? HealthScore.Compute(_lastScan).Score : (int?)null;
                var benchmarkBaseline = await CaptureBenchmarkAsync(BenchmarkRunKind.Baseline,
                    $"Before: {DescribeSelection(selection)}", tweakIds, isDryRunPreview: dryRun, beforeHealthScore, pairId);

                var batchResult = await RunBatchAsync($"Applying {selection.Count} tweak(s)...", selection,
                    Performish.Core.Backup.ChangeLogAction.Apply, dryRun, wantsRestorePoint);

                _lastBatchResults = batchResult.Results;
                _lastBatchWasDryRun = dryRun;
                _lastBenchmark = null;
                _lastRealBenchmark = null;

                if (!dryRun && before != null)
                {
                    var afterScan = await Task.Run(() => _services.Scanner.Scan());
                    var after = BenchmarkSnapshot.FromSystemSnapshot(afterScan);
                    _lastBenchmark = new BenchmarkComparison { Before = before, After = after };
                    ShowBenchmarkComparison(_lastBenchmark);

                    if (benchmarkBaseline != null)
                    {
                        var afterHealthScore = HealthScore.Compute(afterScan).Score;
                        var benchmarkAfter = await CaptureBenchmarkAsync(BenchmarkRunKind.PostApply,
                            $"After: {DescribeSelection(selection)}", tweakIds, isDryRunPreview: false, afterHealthScore, pairId);
                        ShowBenchmarkComparisonDialog(benchmarkBaseline, benchmarkAfter);
                    }
                }
                else if (benchmarkBaseline != null)
                {
                    // Dry run: nothing real changed, so there's nothing real to measure "after" - the
                    // comparison dialog shows this plainly rather than fabricating a result (see
                    // BenchmarkSuiteRunner.Run/BenchmarkComparer's dry-run handling).
                    var dryRunAfter = await CaptureBenchmarkAsync(BenchmarkRunKind.PostApply,
                        $"After (dry run): {DescribeSelection(selection)}", tweakIds, isDryRunPreview: true, null, pairId);
                    ShowBenchmarkComparisonDialog(benchmarkBaseline, dryRunAfter);
                }

                _tweaks?.InvalidateStates();
                UpdateRevertButtonState();
                RenderHome();
            }
            finally
            {
                SetBusy(false);
            }
        }

        private static string DescribeSelection(System.Collections.Generic.List<TweakDefinition> selection) =>
            selection.Count == 1 ? selection[0].Title : $"{selection.Count} tweaks";

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

            var confirmed = await ConfirmAsync("Revert everything?", lines, DryRun ? "Run dry run" : "Revert", "Cancel", danger: !DryRun);
            if (!confirmed) return;

            SetBusy(true);
            try
            {
                var dryRun = DryRun;
                var tweakIds = toRevert.Select(t => t.Id).ToList();
                var pairId = Guid.NewGuid().ToString("N");
                var beforeHealthScore = _lastScan != null ? HealthScore.Compute(_lastScan).Score : (int?)null;
                var benchmarkBaseline = await CaptureBenchmarkAsync(BenchmarkRunKind.Baseline,
                    "Before: revert everything", tweakIds, isDryRunPreview: dryRun, beforeHealthScore, pairId);

                var batchResult = await RunBatchAsync($"Reverting {toRevert.Count} tweak(s)...", toRevert,
                    Performish.Core.Backup.ChangeLogAction.Undo, dryRun);

                _lastBatchResults = batchResult.Results;
                _lastBatchWasDryRun = dryRun;
                _lastBenchmark = null;
                _lastRealBenchmark = null;

                if (benchmarkBaseline != null)
                {
                    int? afterHealthScore = null;
                    if (!dryRun) afterHealthScore = HealthScore.Compute(await Task.Run(() => _services.Scanner.Scan())).Score;
                    var benchmarkAfter = await CaptureBenchmarkAsync(BenchmarkRunKind.PostApply,
                        "After: revert everything", tweakIds, isDryRunPreview: dryRun, afterHealthScore, pairId);
                    ShowBenchmarkComparisonDialog(benchmarkBaseline, benchmarkAfter);
                }

                _tweaks?.InvalidateStates();
                UpdateRevertButtonState();
                RenderHome();
            }
            finally
            {
                SetBusy(false);
            }
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

            var confirmed = await ConfirmAsync("Drifted tweaks detected", lines, "Reapply", "Not now", danger: false);
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
                    Benchmark = _lastBenchmark,
                    RealBenchmark = _lastRealBenchmark
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
