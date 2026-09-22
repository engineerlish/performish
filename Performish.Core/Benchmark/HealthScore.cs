using System;
using System.Collections.Generic;
using System.Linq;
using Performish.Core.Scanner;

namespace Performish.Core.Benchmark
{
    public sealed class HealthScoreFactor
    {
        public string Name { get; set; }
        public int Points { get; set; }
        public int MaxPoints { get; set; }
        public string Detail { get; set; }
    }

    public sealed class HealthScoreResult
    {
        public int Score { get; set; } // always 0-100, regardless of how many raw factor points exist
        public List<HealthScoreFactor> Factors { get; set; } = new List<HealthScoreFactor>();
    }

    /// <summary>A 0-100 "is this machine in good shape" score computed ONLY from numbers the scanner
    /// already measures - no estimate, no guess, nothing that isn't a real read of this machine right
    /// now (task requirement: "the health score may only use measurable inputs"). Every factor's
    /// weight and thresholds are named constants here, not hidden magic numbers, so the score is
    /// auditable and each factor is independently testable.
    ///
    /// Revision note (see DECISIONS.md "Health score accuracy audit"): the original "Process count"
    /// factor and the raw "all enabled scheduled tasks"/"all running services" counts were found to
    /// be scoring defects, not real signals - verified against a real, heavily-used machine's actual
    /// read-only scan. A stock Windows 11 install alone commonly has 150-250+ enabled scheduled tasks
    /// and 100-150+ running services before a single third-party app is installed (almost all under
    /// \Microsoft\Windows\ or C:\Windows\System32\), so those raw counts could never reflect real
    /// bloat - they mostly measured "does Windows exist on this machine". Process count is similarly
    /// dominated by how many browser tabs/multi-process apps happen to be open, not by anything a
    /// debloat tool can act on. Fixed by: removing process count entirely, and re-measuring the
    /// scheduled-tasks/services factors as counts of *third-party* items specifically (excluding the
    /// Windows-shipped baseline), which is what was actually intended to be measured.</summary>
    public static class HealthScore
    {
        private const int StartupItemsMax = 20;
        private const int DiskSpaceMax = 20;
        private const int UptimeMax = 15;
        private const int ThirdPartyScheduledTasksMax = 20;
        private const int ThirdPartyServicesRunningMax = 25;
        public const int TotalMax = StartupItemsMax + DiskSpaceMax + UptimeMax + ThirdPartyScheduledTasksMax + ThirdPartyServicesRunningMax;

        public static HealthScoreResult Compute(SystemSnapshot s)
        {
            var factors = new List<HealthScoreFactor>
            {
                ScoreStartupItems(s),
                ScoreDiskSpace(s),
                ScoreUptime(s),
                ScoreThirdPartyScheduledTasks(s),
                ScoreThirdPartyRunningServices(s)
            };

            var raw = factors.Sum(f => f.Points);
            // Scaled to 100 rather than assuming factor weights always sum to exactly TotalMax - lets
            // a factor be added/removed/reweighted (as this revision just did) without every other
            // factor's weight needing to change too just to keep the total at a round number.
            var scaled = TotalMax > 0 ? (int)Math.Round(raw * 100.0 / TotalMax) : 0;

            return new HealthScoreResult
            {
                Score = Math.Clamp(scaled, 0, 100),
                Factors = factors
            };
        }

        private static HealthScoreFactor ScoreStartupItems(SystemSnapshot s)
        {
            // 0 items = full marks, 20+ items = zero - linear in between. 20 was chosen as the point
            // past which a Run-key startup list is unambiguously bloated on a typical Windows 11
            // install (a handful of first-party entries is normal; dozens is not).
            var count = s.StartupItems.Count;
            var points = Math.Max(0, StartupItemsMax - (int)Math.Round(count * (StartupItemsMax / 20.0)));
            return new HealthScoreFactor { Name = "Startup items", Points = points, MaxPoints = StartupItemsMax, Detail = $"{count} startup item(s)" };
        }

        private static HealthScoreFactor ScoreDiskSpace(SystemSnapshot s)
        {
            if (s.TotalDiskSpaceBytes <= 0)
                return new HealthScoreFactor { Name = "Free disk space", Points = 0, MaxPoints = DiskSpaceMax, Detail = "unknown" };

            var freePct = (double)s.FreeDiskSpaceBytes / s.TotalDiskSpaceBytes * 100.0;
            // Below 5% free = 0, at or above 20% free = full marks - Windows itself starts warning
            // around the 5-10% mark, and update/temp headroom problems concentrate below 20%.
            var points = (int)Math.Round(Math.Clamp((freePct - 5.0) / 15.0, 0.0, 1.0) * DiskSpaceMax);
            return new HealthScoreFactor { Name = "Free disk space", Points = points, MaxPoints = DiskSpaceMax, Detail = $"{freePct:0.0}% free" };
        }

        private static HealthScoreFactor ScoreUptime(SystemSnapshot s)
        {
            // Full marks under 48h uptime, zero at 14+ days - long uptimes correlate with accumulated
            // memory/handle bloat and deferred updates, not a hard rule but a reasonable proxy. This
            // one is a genuine "needs a reboot" nudge (category B, user action), not a bloat measure -
            // kept as-is by the accuracy audit since it isn't miscalibrated, just a different kind of
            // signal than the others.
            var hours = s.UptimeHours;
            var points = (int)Math.Round(Math.Clamp(1.0 - (hours - 48.0) / (336.0 - 48.0), 0.0, 1.0) * UptimeMax);
            return new HealthScoreFactor { Name = "Uptime", Points = points, MaxPoints = UptimeMax, Detail = $"{hours:0.0} hour(s)" };
        }

        /// <summary>A scheduled task shipped and owned by Windows itself, never something a debloat
        /// tool should count against a machine - identified by its Task Scheduler folder path, which
        /// every native Windows task lives under regardless of Windows version/edition/language.
        /// Everything else (Office, OneDrive, GPU/NIC vendor updaters, third-party app installers,
        /// etc.) counts as installed software's own footprint - the actual signal this factor means to
        /// measure.</summary>
        public static bool IsWindowsCoreScheduledTaskPath(string path) =>
            !string.IsNullOrEmpty(path) && path.StartsWith(@"\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase);

        private static HealthScoreFactor ScoreThirdPartyScheduledTasks(SystemSnapshot s)
        {
            var thirdPartyCount = s.ScheduledTasks.Count(t => t.Enabled && !IsWindowsCoreScheduledTaskPath(t.Path));
            // Full marks at 0-10 third-party enabled tasks, zero at 60+ - recalibrated from a real
            // scan of a heavily-used machine (Office, OneDrive, NVIDIA, a NIC vendor suite, a couple
            // of consumer apps) that came out to ~34 third-party enabled tasks; the old thresholds
            // (40/100 against the RAW enabled count, which included ~230 Windows-owned tasks on that
            // same machine) could never be beaten by any real install. See DECISIONS.md.
            var points = (int)Math.Round(Math.Clamp(1.0 - (thirdPartyCount - 10.0) / (60.0 - 10.0), 0.0, 1.0) * ThirdPartyScheduledTasksMax);
            return new HealthScoreFactor
            {
                Name = "Third-party scheduled tasks",
                Points = points,
                MaxPoints = ThirdPartyScheduledTasksMax,
                Detail = $"{thirdPartyCount} enabled (Windows's own tasks are not counted)"
            };
        }

        /// <summary>A service whose executable lives in Windows's own system folders - the baseline
        /// every Windows 11 install runs regardless of what's installed on top, never something a
        /// debloat tool should count against a machine. Anything else (a driver's own management
        /// service, a hardware vendor's suite, security software, a background updater) counts as
        /// installed software's footprint - not all of it is reducible (drivers and security software
        /// must stay), but it is real signal for "how much non-Windows software runs services here".
        /// An empty/unreadable path is never counted as third-party - a missing measurement should
        /// never inflate a penalty.</summary>
        public static bool IsWindowsCoreServicePath(string binaryPath)
        {
            var exePath = ExtractExecutablePath(binaryPath);
            if (string.IsNullOrEmpty(exePath)) return true;
            return exePath.IndexOf(@"\Windows\System32\", StringComparison.OrdinalIgnoreCase) >= 0
                || exePath.IndexOf(@"\Windows\SysWOW64\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Win32_Service.PathName is a full command line (e.g. a quoted path, or an
        /// unquoted path followed by arguments like svchost's "-k netsvcs") - this pulls out just the
        /// executable path so it can be checked against the Windows system folders.</summary>
        public static string ExtractExecutablePath(string pathName)
        {
            if (string.IsNullOrWhiteSpace(pathName)) return "";
            var s = pathName.Trim();
            if (s.StartsWith("\"", StringComparison.Ordinal))
            {
                var closeQuote = s.IndexOf('"', 1);
                return closeQuote > 0 ? s.Substring(1, closeQuote - 1) : s.Trim('"');
            }

            var exeIdx = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return exeIdx > 0 ? s.Substring(0, exeIdx + 4) : s;
        }

        private static HealthScoreFactor ScoreThirdPartyRunningServices(SystemSnapshot s)
        {
            var thirdPartyCount = s.Services.Count(sv => sv.Running && !IsWindowsCoreServicePath(sv.BinaryPath));
            // Full marks at 0-10 third-party running services, zero at 60+ - recalibrated the same way
            // as scheduled tasks. A real scan of a laptop with a security suite and several hardware-
            // vendor service bundles (NIC, GPU, audio, Thunderbolt) still came out well short of 60,
            // while the old raw-count thresholds (90/180) were dominated by ~110 Windows-owned
            // services that were never going anywhere. See DECISIONS.md.
            var points = (int)Math.Round(Math.Clamp(1.0 - (thirdPartyCount - 10.0) / (60.0 - 10.0), 0.0, 1.0) * ThirdPartyServicesRunningMax);
            return new HealthScoreFactor
            {
                Name = "Third-party running services",
                Points = points,
                MaxPoints = ThirdPartyServicesRunningMax,
                Detail = $"{thirdPartyCount} running (Windows's own services are not counted; not all of these can safely be reduced)"
            };
        }
    }
}
