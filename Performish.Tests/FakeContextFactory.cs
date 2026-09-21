using System.Collections.Generic;
using Performish.Core.Backends;
using Performish.Core.Backup;
using Performish.Core.Models;

namespace Performish.Tests
{
    /// <summary>Builds a TweakExecutionContext wired entirely to Fake* backends - the only kind of
    /// context this test project ever constructs, per the task's hard safety rule (never touch the
    /// real machine during development/testing).</summary>
    public static class FakeContextFactory
    {
        public sealed class Bundle
        {
            public FakeRegistryBackend Registry;
            public FakeServiceBackend Services;
            public FakeScheduledTaskBackend Tasks;
            public FakeAppxBackend Appx;
            public FakeProcessRunner Process;
            public FakeFileSystemBackend FileSystem;
            public FakePowerBackend Power;
            public InMemoryUndoStore UndoStore;
            public List<string> LogLines;
            public TweakExecutionContext Context;
        }

        public static Bundle Create(bool dryRun = false)
        {
            var bundle = new Bundle
            {
                Registry = new FakeRegistryBackend(),
                Services = new FakeServiceBackend(),
                Tasks = new FakeScheduledTaskBackend(),
                Appx = new FakeAppxBackend(),
                Process = new FakeProcessRunner(),
                FileSystem = new FakeFileSystemBackend(),
                Power = new FakePowerBackend(),
                UndoStore = new InMemoryUndoStore(),
                LogLines = new List<string>()
            };

            bundle.Context = new TweakExecutionContext(
                dryRun, bundle.Registry, bundle.Services, bundle.Tasks, bundle.Appx,
                bundle.Process, bundle.FileSystem, bundle.Power, bundle.UndoStore,
                bundle.LogLines.Add);

            return bundle;
        }
    }
}
