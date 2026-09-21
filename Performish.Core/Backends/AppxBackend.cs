using System;
using System.Collections.Generic;
using System.Linq;

namespace Performish.Core.Backends
{
    public sealed class AppxSnapshot
    {
        public string PackageFamilyName { get; set; }
        public bool InstalledForCurrentUser { get; set; }
        public bool Provisioned { get; set; }
    }

    /// <summary>Appx/UWP package removal - per-user uninstall and provisioned-package deprovision
    /// (so it doesn't reinstall for new user profiles) are tracked separately, matching how
    /// Remove-AppxPackage vs Remove-AppxProvisionedPackage actually work.</summary>
    public interface IAppxBackend
    {
        AppxSnapshot Read(string packageFamilyPrefix);
        void RemoveForCurrentUser(string packageFamilyPrefix);
        void Deprovision(string packageFamilyPrefix);
        void ReinstallForCurrentUser(string packageFamilyPrefix);
        void Reprovision(string packageFamilyPrefix);
    }

    /// <summary>Real backend shells out to PowerShell's Appx cmdlets (there is no first-class .NET
    /// Appx API) via IProcessRunner, so it stays behind the same fakeable seam as everything else.
    /// Matching is by name prefix (e.g. "Microsoft.XboxGamingOverlay") since package family names
    /// carry a publisher hash suffix that varies by build.</summary>
    public sealed class RealAppxBackend : IAppxBackend
    {
        private readonly IProcessRunner _process;

        // The whole point of caching here: the old version launched 2 powershell.exe processes per
        // Read() call (Get-AppxPackage + Get-AppxProvisionedPackage), so opening the tweak browser
        // with 15 Appx tweaks meant up to 30 process spawns. A PowerShell cold start is genuinely
        // slow (real-world hundreds of ms, not measurable from this dev session's Fake-backed
        // benchmarks - see BASELINE.md). Caching both full listings ONCE per backend instance and
        // filtering in memory drops that to 2 spawns total for the whole session, matching Phase 5's
        // "batch or replace repeated PowerShell launches with a single persistent session". The cache
        // is invalidated after any write, since a Remove/Deprovision/Reinstall genuinely changes what
        // a later Read() should report.
        private List<string> _installedNamesCache;
        private List<string> _provisionedNamesCache;

        public RealAppxBackend(IProcessRunner process)
        {
            _process = process ?? throw new ArgumentNullException(nameof(process));
        }

        private ProcessRunResult RunPs(string script) =>
            _process.Run("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"");

        private void EnsureCache()
        {
            if (_installedNamesCache != null && _provisionedNamesCache != null) return;

            var installed = RunPs("(Get-AppxPackage -AllUsers).Name -join \"`n\"");
            var provisioned = RunPs("(Get-AppxProvisionedPackage -Online).DisplayName -join \"`n\"");

            _installedNamesCache = SplitLines(installed.StandardOutput);
            _provisionedNamesCache = SplitLines(provisioned.StandardOutput);
        }

        private static List<string> SplitLines(string output) =>
            output.Split('\n').Select(s => s.Trim().Trim('\r')).Where(s => s.Length > 0).ToList();

        private void InvalidateCache()
        {
            _installedNamesCache = null;
            _provisionedNamesCache = null;
        }

        public AppxSnapshot Read(string packageFamilyPrefix)
        {
            EnsureCache();
            return new AppxSnapshot
            {
                PackageFamilyName = packageFamilyPrefix,
                InstalledForCurrentUser = _installedNamesCache.Any(n => n.StartsWith(packageFamilyPrefix, StringComparison.OrdinalIgnoreCase)),
                Provisioned = _provisionedNamesCache.Any(n => n.StartsWith(packageFamilyPrefix, StringComparison.OrdinalIgnoreCase))
            };
        }

        public void RemoveForCurrentUser(string packageFamilyPrefix)
        {
            RunPs($"Get-AppxPackage -AllUsers -Name '{packageFamilyPrefix}*' | Remove-AppxPackage -ErrorAction SilentlyContinue");
            InvalidateCache();
        }

        public void Deprovision(string packageFamilyPrefix)
        {
            RunPs($"Get-AppxProvisionedPackage -Online | Where-Object DisplayName -like '{packageFamilyPrefix}*' | " +
                  "ForEach-Object { Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName -ErrorAction SilentlyContinue }");
            InvalidateCache();
        }

        public void ReinstallForCurrentUser(string packageFamilyPrefix)
        {
            RunPs($"Get-AppxPackage -AllUsers -Name '{packageFamilyPrefix}*' | " +
                  "ForEach-Object { Add-AppxPackage -DisableDevelopmentMode -Register \"$($_.InstallLocation)\\AppXManifest.xml\" -ErrorAction SilentlyContinue }");
            InvalidateCache();
        }

        public void Reprovision(string packageFamilyPrefix)
        {
            // Provisioning a removed package back in for new profiles needs the original .appx/.msix,
            // which Windows no longer has once deprovisioned - undo can only restore the per-user
            // install (ReinstallForCurrentUser). This is surfaced to the user as a documented
            // limitation rather than silently no-op'd; see the Deprovision-tweak Undo() bodies.
        }
    }

    public sealed class FakeAppxBackend : IAppxBackend
    {
        private readonly HashSet<string> _installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _provisioned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public void Seed(string packageFamilyPrefix, bool installed, bool provisioned)
        {
            if (installed) _installed.Add(packageFamilyPrefix);
            if (provisioned) _provisioned.Add(packageFamilyPrefix);
        }

        public AppxSnapshot Read(string packageFamilyPrefix) => new AppxSnapshot
        {
            PackageFamilyName = packageFamilyPrefix,
            InstalledForCurrentUser = _installed.Contains(packageFamilyPrefix),
            Provisioned = _provisioned.Contains(packageFamilyPrefix)
        };

        public void RemoveForCurrentUser(string packageFamilyPrefix) => _installed.Remove(packageFamilyPrefix);
        public void Deprovision(string packageFamilyPrefix) => _provisioned.Remove(packageFamilyPrefix);
        public void ReinstallForCurrentUser(string packageFamilyPrefix) => _installed.Add(packageFamilyPrefix);
        public void Reprovision(string packageFamilyPrefix) => _provisioned.Add(packageFamilyPrefix);
    }
}
