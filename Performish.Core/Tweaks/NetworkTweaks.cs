using System.Collections.Generic;
using Microsoft.Win32;
using Performish.Core.Backends;
using Performish.Core.Models;

namespace Performish.Core.Tweaks
{
    /// <summary>Network library - all Moderate risk and opt-in per the task brief ("Network tweaks
    /// (Moderate, opt-in)"), none included in any preset by default. Nagle's algorithm disable is
    /// per-network-adapter (there is no single global key), so it is hand-written rather than a
    /// TweakFactory shape; it snapshots every adapter interface it touches so Undo restores each one.</summary>
    public static class NetworkTweaks
    {
        public static IEnumerable<TweakDefinition> All()
        {
            yield return TweakFactory.RegistryDword(
                "net.throttling_disable",
                "Disable network throttling during gaming",
                "Removes the ~10-packets-per-ms cap Windows applies to non-multimedia network traffic " +
                "during multimedia playback/gaming, which can otherwise add jitter to game traffic " +
                "sharing the connection with other apps.",
                TweakCategory.Network, RiskLevel.Moderate, TweakScope.Machine, false,
                "HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile\\NetworkThrottlingIndex = 0xFFFFFFFF",
                RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", -1);

            yield return TweakFactory.RegistryDword(
                "net.system_responsiveness",
                "Prioritize game threads over background tasks",
                "Lowers SystemResponsiveness from the 20% default to 0%, giving multimedia/game-class " +
                "threads a larger share of CPU time versus background work under load.",
                TweakCategory.Network, RiskLevel.Moderate, TweakScope.Machine, false,
                "HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile\\SystemResponsiveness",
                RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", 0);

            yield return TweakFactory.ProcessCommandPair(
                "net.tcp_timestamps_disable",
                "Disable TCP timestamps",
                "Minor per-packet overhead reduction; some competitive-gaming guides recommend it, " +
                "effect is generally too small to measure outside synthetic benchmarks.",
                TweakCategory.Network, RiskLevel.Moderate, TweakScope.Machine, false,
                "netsh int tcp set global timestamps=disabled / =enabled",
                "netsh.exe", "int tcp set global timestamps=disabled",
                "netsh.exe", "int tcp set global timestamps=enabled");

            yield return NagleDisableTweak();
        }

        private static TweakDefinition NagleDisableTweak()
        {
            const string id = "net.nagle_disable";

            TweakOperationResult Apply(TweakExecutionContext ctx)
            {
                const string descr = "Disable Nagle's algorithm (TcpAckFrequency=1, TcpNoDelay=1) on every network adapter";
                if (ctx.DryRun) return TweakOperationResult.Preview(descr);

                if (!ctx.Registry.KeyExists(RegistryHive.LocalMachine, interfacesPath))
                    return TweakOperationResult.Skipped("No network interface keys found under Tcpip\\Parameters\\Interfaces.");

                var interfaceIds = ListInterfaceIds(ctx);
                if (interfaceIds.Count == 0)
                    return TweakOperationResult.Skipped("No network interfaces found to configure.");

                var snapshots = new List<RegistryValueSnapshot>();
                foreach (var ifaceId in interfaceIds)
                {
                    var subKey = interfacesPath + "\\" + ifaceId;
                    snapshots.Add(ctx.Registry.Read(RegistryHive.LocalMachine, subKey, "TcpAckFrequency"));
                    snapshots.Add(ctx.Registry.Read(RegistryHive.LocalMachine, subKey, "TcpNoDelay"));
                    ctx.Registry.Write(RegistryHive.LocalMachine, subKey, "TcpAckFrequency", 1, RegistryValueKind.DWord);
                    ctx.Registry.Write(RegistryHive.LocalMachine, subKey, "TcpNoDelay", 1, RegistryValueKind.DWord);
                }
                ctx.UndoStore.Save(id, snapshots);

                return TweakOperationResult.Success(descr + $" ({interfaceIds.Count} adapter(s))");
            }

            TweakOperationResult Undo(TweakExecutionContext ctx)
            {
                const string descr = "Restore each adapter's previous TcpAckFrequency/TcpNoDelay values";
                if (ctx.DryRun) return TweakOperationResult.Preview(descr);

                if (!ctx.UndoStore.Has(id))
                    return TweakOperationResult.Skipped("No prior snapshot recorded - was this tweak ever applied by Performish?");

                var snapshots = ctx.UndoStore.Load<List<RegistryValueSnapshot>>(id);
                foreach (var snap in snapshots) ctx.Registry.Restore(snap);
                return TweakOperationResult.Success(descr);
            }

            return new TweakDefinition(id, "Disable Nagle's algorithm for lower latency",
                "Nagle's algorithm batches small outgoing packets to reduce overhead, at the cost of " +
                "up to ~200ms added latency per batched send - noticeable in some multiplayer games. " +
                "Disabling it trades a small amount of network efficiency for lower latency.",
                TweakCategory.Network, RiskLevel.Moderate, TweakScope.Machine, false,
                "Per-adapter HKLM\\...\\Tcpip\\Parameters\\Interfaces\\{id}\\TcpAckFrequency=1, TcpNoDelay=1 " +
                "(long-standing documented low-latency network tweak, not a hidden/undocumented key)",
                _ => TweakState.Unknown, Apply, Undo);
        }

        private static List<string> ListInterfaceIds(TweakExecutionContext ctx)
        {
            // IRegistryBackend doesn't expose subkey enumeration (every other tweak only needs single
            // values) - callers that need it go through IProcessRunner instead, keeping the registry
            // seam narrow. Real backend: query via reg.exe; fake backend/tests seed known adapter ids
            // directly through FakeProcessRunner's scripted response.
            var result = ctx.Process.Run("reg.exe", $"query \"HKLM\\{interfacesPath}\"");
            var ids = new List<string>();
            foreach (var line in result.StandardOutput.Split('\n'))
            {
                var trimmed = line.Trim().Trim('\r');
                var marker = "\\Interfaces\\";
                var idx = trimmed.IndexOf(marker, System.StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                    ids.Add(trimmed.Substring(idx + marker.Length));
            }
            return ids;
        }

        private const string interfacesPath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
    }
}
