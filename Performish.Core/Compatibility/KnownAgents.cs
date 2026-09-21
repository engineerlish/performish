using System;
using System.Collections.Generic;
using System.Linq;

namespace Performish.Core.Compatibility
{
    /// <summary>A short, curated list of common VPN client / RMM (remote monitoring & management)
    /// agent / endpoint-security process names, matched by exact process name (no path, no
    /// extension - same shape .NET's Process.ProcessName returns). Detection is informational only:
    /// it never blocks a tweak, it only tells the user "this software is running, and it may care
    /// about the service/background-app/network behavior some tweaks change" so they can make an
    /// informed call before applying, per the task's "compatibility guardrails ... before applying"
    /// requirement. Intentionally short and named-vendor-specific rather than a heuristic, so a false
    /// positive is very unlikely and every entry is auditable.</summary>
    public static class KnownAgents
    {
        public sealed class AgentInfo
        {
            public string ProcessName { get; }
            public string DisplayName { get; }
            public string Category { get; } // VPN / RMM / Endpoint Security

            public AgentInfo(string processName, string displayName, string category)
            {
                ProcessName = processName;
                DisplayName = displayName;
                Category = category;
            }
        }

        public static readonly IReadOnlyList<AgentInfo> All = new[]
        {
            // VPN clients
            new AgentInfo("openvpn", "OpenVPN", "VPN"),
            new AgentInfo("openvpn-gui", "OpenVPN GUI", "VPN"),
            new AgentInfo("nordvpn-service", "NordVPN", "VPN"),
            new AgentInfo("expressvpn-service", "ExpressVPN", "VPN"),
            new AgentInfo("wireguard", "WireGuard", "VPN"),
            new AgentInfo("anyconnect", "Cisco AnyConnect", "VPN"),
            new AgentInfo("vpnui", "Cisco Secure Client", "VPN"),
            new AgentInfo("globalprotect", "Palo Alto GlobalProtect", "VPN"),
            new AgentInfo("fortitray", "Fortinet FortiClient", "VPN"),

            // RMM / remote-management agents
            new AgentInfo("screenconnect.clientservice", "ConnectWise ScreenConnect", "RMM"),
            new AgentInfo("connectwisecontrol.clientservice", "ConnectWise Control", "RMM"),
            new AgentInfo("ninjarmmagent", "NinjaRMM", "RMM"),
            new AgentInfo("atera_agent", "Atera Agent", "RMM"),
            new AgentInfo("kaseyaagentendpoint", "Kaseya Agent", "RMM"),
            new AgentInfo("teamviewer", "TeamViewer", "RMM"),
            new AgentInfo("teamviewer_service", "TeamViewer Service", "RMM"),
            new AgentInfo("anydesk", "AnyDesk", "RMM"),
            new AgentInfo("splashtop-streamer", "Splashtop Streamer", "RMM"),
            new AgentInfo("logmein", "LogMeIn", "RMM"),

            // Endpoint security / EDR
            new AgentInfo("csfalconservice", "CrowdStrike Falcon", "Endpoint Security"),
            new AgentInfo("sentinelagent", "SentinelOne", "Endpoint Security"),
            new AgentInfo("mfemms", "McAfee/Trellix Agent", "Endpoint Security"),
            new AgentInfo("mcshield", "McAfee/Trellix Shield", "Endpoint Security"),
            new AgentInfo("savservice", "Sophos Anti-Virus", "Endpoint Security"),
            new AgentInfo("sophosservice", "Sophos Endpoint", "Endpoint Security"),
            new AgentInfo("cyserver", "Cylance", "Endpoint Security"),
            new AgentInfo("ekrn", "ESET", "Endpoint Security"),
            new AgentInfo("mbamservice", "Malwarebytes", "Endpoint Security"),
        };

        /// <summary>Matches a distinct set of running process names against the known-agent list,
        /// case-insensitive exact match. Pure function, no I/O - the caller (SystemScanner) supplies
        /// the process name list, which is how this stays testable without touching a real
        /// Process.GetProcesses() call.</summary>
        public static List<AgentInfo> Detect(IEnumerable<string> runningProcessNames)
        {
            var names = new HashSet<string>(runningProcessNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            return All.Where(a => names.Contains(a.ProcessName)).ToList();
        }
    }
}
