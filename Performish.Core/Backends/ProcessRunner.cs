using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Performish.Core.Backends
{
    public sealed class ProcessRunResult
    {
        public int ExitCode { get; set; }
        public string StandardOutput { get; set; } = string.Empty;
        public string StandardError { get; set; } = string.Empty;
    }

    /// <summary>Every external command (schtasks, sc, powercfg, netsh, DISM, PowerShell for Appx)
    /// goes through this one seam so it can be faked in tests without a single Process.Start ever
    /// happening off the real backend.</summary>
    public interface IProcessRunner
    {
        ProcessRunResult Run(string fileName, string arguments, int timeoutMs = 30000);
    }

    public sealed class RealProcessRunner : IProcessRunner
    {
        public ProcessRunResult Run(string fileName, string arguments, int timeoutMs = 30000)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            proc.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            proc.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            if (!proc.WaitForExit(timeoutMs))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                return new ProcessRunResult { ExitCode = -1, StandardError = "Timed out." };
            }

            return new ProcessRunResult { ExitCode = proc.ExitCode, StandardOutput = stdout.ToString(), StandardError = stderr.ToString() };
        }
    }

    /// <summary>Scripted responses keyed by a substring match against "fileName arguments" - tests
    /// register exactly the commands a tweak is expected to run and assert against the call log.</summary>
    public sealed class FakeProcessRunner : IProcessRunner
    {
        public List<(string FileName, string Arguments)> Calls { get; } = new List<(string, string)>();

        private readonly List<(string Match, ProcessRunResult Result)> _scripts = new List<(string, ProcessRunResult)>();
        private readonly ProcessRunResult _default = new ProcessRunResult { ExitCode = 0 };

        public void Script(string commandLineSubstring, ProcessRunResult result) => _scripts.Add((commandLineSubstring, result));

        public ProcessRunResult Run(string fileName, string arguments, int timeoutMs = 30000)
        {
            Calls.Add((fileName, arguments));
            var full = $"{fileName} {arguments}";
            foreach (var (match, result) in _scripts)
            {
                if (full.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0)
                    return result;
            }
            return _default;
        }
    }
}
