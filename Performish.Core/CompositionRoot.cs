using System;
using Performish.Core.Backends;
using Performish.Core.Backup;
using Performish.Core.Benchmark;
using Performish.Core.Models;
using Performish.Core.Scanner;
using Performish.Core.Tweaks;

namespace Performish.Core
{
    /// <summary>The one place real backends are ever constructed and wired together. Every backend
    /// here is a Real* implementation - this class is only ever called from the shipped WinForms
    /// app's Program.cs, never from this dev session and never from Performish.Tests (see DECISIONS.md /
    /// PROGRESS.md "Dev machine is never touched"). Bundles everything a MainForm needs to run a
    /// real scan and real tweak batches.</summary>
    public sealed class AppServices
    {
        public IProcessRunner Process { get; }
        public IRegistryBackend Registry { get; }
        public IServiceBackend Services { get; }
        public IScheduledTaskBackend Tasks { get; }
        public IAppxBackend Appx { get; }
        public IFileSystemBackend FileSystem { get; }
        public IPowerBackend Power { get; }
        public IRestorePointBackend RestorePoint { get; }
        public IUndoStore UndoStore { get; }
        public IChangeLogStore ChangeLog { get; }
        public ISystemInfoBackend SystemInfo { get; }
        public SystemScanner Scanner { get; }
        public TweakRegistry TweakRegistry { get; }
        public TweakRunner Runner { get; }
        public MigrationResult Migration { get; }
        public BenchmarkSuiteRunner Benchmarks { get; }
        public IBenchmarkRunStore BenchmarkHistory { get; }

        private AppServices(
            IProcessRunner process, IRegistryBackend registry, IServiceBackend services, IScheduledTaskBackend tasks,
            IAppxBackend appx, IFileSystemBackend fileSystem, IPowerBackend power, IRestorePointBackend restorePoint,
            IUndoStore undoStore, IChangeLogStore changeLog, ISystemInfoBackend systemInfo, SystemScanner scanner,
            TweakRegistry tweakRegistry, TweakRunner runner, MigrationResult migration,
            BenchmarkSuiteRunner benchmarks, IBenchmarkRunStore benchmarkHistory)
        {
            Process = process;
            Registry = registry;
            Services = services;
            Tasks = tasks;
            Appx = appx;
            FileSystem = fileSystem;
            Power = power;
            RestorePoint = restorePoint;
            UndoStore = undoStore;
            ChangeLog = changeLog;
            SystemInfo = systemInfo;
            Scanner = scanner;
            TweakRegistry = tweakRegistry;
            Runner = runner;
            Migration = migration;
            Benchmarks = benchmarks;
            BenchmarkHistory = benchmarkHistory;
        }

        public static AppServices BuildReal()
        {
            // Migrate any pre-rename "Ish" data before anything else touches DataPaths, so a user's
            // undo snapshots/change log/settings from the old product name carry forward - see
            // DataPaths.MigrateFromLegacyInstall() and DECISIONS.md.
            var migration = DataPaths.MigrateFromLegacyInstall();

            var process = new RealProcessRunner();
            var registry = new RealRegistryBackend();
            var services = new RealServiceBackend(process);
            var tasks = new RealScheduledTaskBackend(process);
            var appx = new RealAppxBackend(process);
            var fileSystem = new RealFileSystemBackend();
            var power = new RealPowerBackend(process);
            var restorePoint = new RealRestorePointBackend(process);
            var undoStore = new FileUndoStore(DataPaths.BackupDirectory);
            var changeLog = new FileChangeLogStore(DataPaths.ChangeLogDirectory);
            var systemInfo = new RealSystemInfoBackend(process);
            var scanner = new SystemScanner(systemInfo, power);
            var tweakRegistry = TweakRegistry.BuildDefault();
            var runner = new TweakRunner(changeLog, restorePoint);
            var benchmarks = new BenchmarkSuiteRunner(systemInfo);
            var benchmarkHistory = new FileBenchmarkRunStore(DataPaths.BenchmarksDirectory);

            return new AppServices(process, registry, services, tasks, appx, fileSystem, power, restorePoint,
                undoStore, changeLog, systemInfo, scanner, tweakRegistry, runner, migration, benchmarks, benchmarkHistory);
        }

        public TweakExecutionContext CreateContext(bool dryRun, Action<string> onLog = null) =>
            new TweakExecutionContext(dryRun, Registry, Services, Tasks, Appx, Process, FileSystem, Power, UndoStore, onLog);

        /// <summary>Same shape as BuildReal(), wired entirely to Fake* backends and in-memory
        /// stores - never touches disk or the real machine. Exists so UI code (dialogs that take a
        /// concrete AppServices, not an interface) can be exercised by Performish.Tests with
        /// simulated input events, per the Phase 4 requirement to test button/state handling without
        /// depending on RealRegistryBackend etc.</summary>
        public static AppServices BuildFake()
        {
            var process = new FakeProcessRunner();
            var registry = new FakeRegistryBackend();
            var services = new FakeServiceBackend();
            var tasks = new FakeScheduledTaskBackend();
            var appx = new FakeAppxBackend();
            var fileSystem = new FakeFileSystemBackend();
            var power = new FakePowerBackend();
            var restorePoint = new FakeRestorePointBackend();
            var undoStore = new InMemoryUndoStore();
            var changeLog = new InMemoryChangeLogStore();
            var systemInfo = new FakeSystemInfoBackend();
            var scanner = new SystemScanner(systemInfo, power);
            var tweakRegistry = TweakRegistry.BuildDefault();
            var runner = new TweakRunner(changeLog, restorePoint);
            var benchmarks = new BenchmarkSuiteRunner(systemInfo);
            var benchmarkHistory = new InMemoryBenchmarkRunStore();

            return new AppServices(process, registry, services, tasks, appx, fileSystem, power, restorePoint,
                undoStore, changeLog, systemInfo, scanner, tweakRegistry, runner, new MigrationResult(), benchmarks, benchmarkHistory);
        }
    }
}
