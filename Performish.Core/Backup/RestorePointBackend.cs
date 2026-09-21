using System;
using Performish.Core.Backends;

namespace Performish.Core.Backup
{
    /// <summary>Windows System Restore point creation, one per batch, per the task's non-negotiable
    /// safety requirement. Routed through PowerShell's Checkpoint-Computer (there is no first-class
    /// managed API) via IProcessRunner, so it stays behind the same fakeable seam as everything
    /// else - never invoked for real by this dev/test session.</summary>
    public interface IRestorePointBackend
    {
        /// <summary>Returns true if a restore point was created (or one already exists within
        /// Windows's own throttling window, which System Restore enforces on its own - at most one
        /// automatic point per 24h by default). False means it could not be created (System
        /// Protection off, unsupported edition, etc.) and the caller must decide whether to proceed
        /// or stop, per "confirmation before Moderate/Advanced changes".</summary>
        bool TryCreate(string description);
    }

    public sealed class RealRestorePointBackend : IRestorePointBackend
    {
        private readonly IProcessRunner _process;

        public RealRestorePointBackend(IProcessRunner process)
        {
            _process = process ?? throw new ArgumentNullException(nameof(process));
        }

        public bool TryCreate(string description)
        {
            var script = $"Checkpoint-Computer -Description '{description.Replace("'", "''")}' -RestorePointType 'MODIFY_SETTINGS'";
            var result = _process.Run("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"");
            return result.ExitCode == 0;
        }
    }

    public sealed class FakeRestorePointBackend : IRestorePointBackend
    {
        public bool NextResult { get; set; } = true;
        public int CreateCallCount { get; private set; }
        public string LastDescription { get; private set; }

        public bool TryCreate(string description)
        {
            CreateCallCount++;
            LastDescription = description;
            return NextResult;
        }
    }
}
