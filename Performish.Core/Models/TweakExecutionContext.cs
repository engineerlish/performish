using System;
using Performish.Core.Backends;
using Performish.Core.Backup;

namespace Performish.Core.Models
{
    /// <summary>Everything a tweak's Check/Apply/Undo needs to touch the system - always passed in,
    /// never looked up globally, so a tweak can be run against real backends or fully-simulated fake
    /// backends with no code difference. See DECISIONS.md for why this split exists: it is what lets
    /// this project ship a real dry-run mode and a real test suite from one code path without ever
    /// letting the dev/test session touch the real machine.</summary>
    public sealed class TweakExecutionContext
    {
        /// <summary>When true, Apply/Undo must not call any backend write method - they build and
        /// log a description of what WOULD happen and return a Preview result instead. Check() (read
        /// only) still runs normally in dry-run mode; read-only scanning is always allowed.</summary>
        public bool DryRun { get; }

        public IRegistryBackend Registry { get; }
        public IServiceBackend Services { get; }
        public IScheduledTaskBackend Tasks { get; }
        public IAppxBackend Appx { get; }
        public IProcessRunner Process { get; }
        public IFileSystemBackend FileSystem { get; }
        public IPowerBackend Power { get; }
        public IUndoStore UndoStore { get; }

        private readonly Action<string> _onLog;

        public TweakExecutionContext(
            bool dryRun,
            IRegistryBackend registry,
            IServiceBackend services,
            IScheduledTaskBackend tasks,
            IAppxBackend appx,
            IProcessRunner process,
            IFileSystemBackend fileSystem,
            IPowerBackend power,
            IUndoStore undoStore,
            Action<string> onLog = null)
        {
            DryRun = dryRun;
            Registry = registry;
            Services = services;
            Tasks = tasks;
            Appx = appx;
            Process = process;
            FileSystem = fileSystem;
            Power = power;
            UndoStore = undoStore;
            _onLog = onLog;
        }

        /// <summary>Fire-and-forget progress line for the live UI. Never throws - a logging failure
        /// must never abort a tweak batch.</summary>
        public void Log(string message)
        {
            try { _onLog?.Invoke(message); } catch { /* logging must never break a tweak run */ }
        }
    }
}
