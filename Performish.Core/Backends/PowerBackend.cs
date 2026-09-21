using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Performish.Core.Backends
{
    public sealed class PowerPlanInfo
    {
        public string Guid { get; set; }
        public string Name { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>Power plan enumeration/switching, routed through powercfg.exe (via IProcessRunner)
    /// for the real backend - there is no managed API for this.</summary>
    public interface IPowerBackend
    {
        List<PowerPlanInfo> ListPlans();
        string GetActivePlanGuid();
        void SetActivePlan(string guid);
        /// <summary>Imports the well-known "Ultimate Performance" plan (hidden by default on most
        /// Windows 11 installs) via `powercfg -duplicatescheme`, returning its GUID.</summary>
        string EnsureUltimatePerformancePlanImported();
    }

    public sealed class RealPowerBackend : IPowerBackend
    {
        // Well-known GUID Microsoft ships for "Ultimate Performance" - duplicating this scheme id
        // is the documented way to reveal the hidden Ultimate Performance plan.
        private const string UltimatePerformanceSourceGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";

        private readonly IProcessRunner _process;

        // ListPlans() is called from several places (GetActivePlanGuid(), EnsureUltimatePerformance-
        // PlanImported(), and SystemScanner.Scan()) that all want "the current plan list" within the
        // same logical operation - without caching, each of those is its own powercfg.exe spawn.
        // Cached for the lifetime of this backend instance, invalidated by the only two calls that
        // can actually change the answer (SetActivePlan, EnsureUltimatePerformancePlanImported).
        private List<PowerPlanInfo> _plansCache;

        public RealPowerBackend(IProcessRunner process)
        {
            _process = process ?? throw new ArgumentNullException(nameof(process));
        }

        public List<PowerPlanInfo> ListPlans()
        {
            if (_plansCache != null) return _plansCache;

            var result = _process.Run("powercfg.exe", "/list");
            var plans = new List<PowerPlanInfo>();
            foreach (Match m in Regex.Matches(result.StandardOutput,
                @"Power Scheme GUID:\s*([0-9a-fA-F-]{36})\s*\(([^)]*)\)\s*(\*?)"))
            {
                plans.Add(new PowerPlanInfo { Guid = m.Groups[1].Value, Name = m.Groups[2].Value, IsActive = m.Groups[3].Value == "*" });
            }
            _plansCache = plans;
            return plans;
        }

        public string GetActivePlanGuid() => ListPlans().FirstOrDefault(p => p.IsActive)?.Guid;

        public void SetActivePlan(string guid)
        {
            _process.Run("powercfg.exe", $"/setactive {guid}");
            _plansCache = null;
        }

        public string EnsureUltimatePerformancePlanImported()
        {
            var existing = ListPlans().FirstOrDefault(p => p.Name.IndexOf("Ultimate Performance", StringComparison.OrdinalIgnoreCase) >= 0);
            if (existing != null) return existing.Guid;

            var result = _process.Run("powercfg.exe", $"-duplicatescheme {UltimatePerformanceSourceGuid}");
            _plansCache = null;
            var match = Regex.Match(result.StandardOutput, @"([0-9a-fA-F-]{36})");
            return match.Success ? match.Groups[1].Value : null;
        }
    }

    public sealed class FakePowerBackend : IPowerBackend
    {
        public List<PowerPlanInfo> Plans { get; } = new List<PowerPlanInfo>
        {
            new PowerPlanInfo { Guid = "381b4222-f694-41f0-9685-ff5bb260df2e", Name = "Balanced", IsActive = true },
            new PowerPlanInfo { Guid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", Name = "High performance", IsActive = false }
        };

        public bool UltimateImported { get; private set; }
        public const string UltimateGuid = "ee12f906-d277-404b-b6da-e5fa1a576df5";

        public List<PowerPlanInfo> ListPlans() => Plans;

        public string GetActivePlanGuid() => Plans.FirstOrDefault(p => p.IsActive)?.Guid;

        public void SetActivePlan(string guid)
        {
            foreach (var p in Plans) p.IsActive = p.Guid == guid;
        }

        public string EnsureUltimatePerformancePlanImported()
        {
            if (!UltimateImported)
            {
                UltimateImported = true;
                Plans.Add(new PowerPlanInfo { Guid = UltimateGuid, Name = "Ultimate Performance", IsActive = false });
            }
            return UltimateGuid;
        }
    }
}
