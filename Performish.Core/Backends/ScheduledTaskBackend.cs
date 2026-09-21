using System;
using System.Collections.Generic;

namespace Performish.Core.Backends
{
    public sealed class ScheduledTaskSnapshot
    {
        public string TaskPath { get; set; }
        public bool Existed { get; set; }
        public bool WasEnabled { get; set; }
    }

    /// <summary>Scheduled task enable/disable, routed through schtasks.exe (via IProcessRunner) for
    /// the real backend so there is no extra native dependency.</summary>
    public interface IScheduledTaskBackend
    {
        bool Exists(string taskPath);
        ScheduledTaskSnapshot Read(string taskPath);
        void SetEnabled(string taskPath, bool enabled);
        void Restore(ScheduledTaskSnapshot snapshot);
    }

    public sealed class RealScheduledTaskBackend : IScheduledTaskBackend
    {
        private readonly IProcessRunner _process;

        public RealScheduledTaskBackend(IProcessRunner process)
        {
            _process = process ?? throw new ArgumentNullException(nameof(process));
        }

        public bool Exists(string taskPath)
        {
            var result = _process.Run("schtasks.exe", $"/Query /TN \"{taskPath}\"");
            return result.ExitCode == 0;
        }

        public ScheduledTaskSnapshot Read(string taskPath)
        {
            var result = _process.Run("schtasks.exe", $"/Query /TN \"{taskPath}\" /FO LIST /V");
            if (result.ExitCode != 0)
                return new ScheduledTaskSnapshot { TaskPath = taskPath, Existed = false };

            var enabled = result.StandardOutput.IndexOf("Ready", StringComparison.OrdinalIgnoreCase) >= 0
                || result.StandardOutput.IndexOf("Running", StringComparison.OrdinalIgnoreCase) >= 0;
            var disabled = result.StandardOutput.IndexOf("Disabled", StringComparison.OrdinalIgnoreCase) >= 0;

            return new ScheduledTaskSnapshot { TaskPath = taskPath, Existed = true, WasEnabled = enabled && !disabled };
        }

        public void SetEnabled(string taskPath, bool enabled) =>
            _process.Run("schtasks.exe", $"/Change /TN \"{taskPath}\" /{(enabled ? "Enable" : "Disable")}");

        public void Restore(ScheduledTaskSnapshot snapshot)
        {
            if (!snapshot.Existed) return;
            SetEnabled(snapshot.TaskPath, snapshot.WasEnabled);
        }
    }

    public sealed class FakeScheduledTaskBackend : IScheduledTaskBackend
    {
        private readonly Dictionary<string, bool> _tasks = new Dictionary<string, bool>();

        public void Seed(string taskPath, bool enabled) => _tasks[taskPath] = enabled;

        public bool Exists(string taskPath) => _tasks.ContainsKey(taskPath);

        public ScheduledTaskSnapshot Read(string taskPath)
        {
            if (_tasks.TryGetValue(taskPath, out var enabled))
                return new ScheduledTaskSnapshot { TaskPath = taskPath, Existed = true, WasEnabled = enabled };
            return new ScheduledTaskSnapshot { TaskPath = taskPath, Existed = false };
        }

        public void SetEnabled(string taskPath, bool enabled) => _tasks[taskPath] = enabled;

        public void Restore(ScheduledTaskSnapshot snapshot)
        {
            if (!snapshot.Existed) return;
            SetEnabled(snapshot.TaskPath, snapshot.WasEnabled);
        }
    }
}
