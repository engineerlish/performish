using System;
using System.Collections.Generic;
using System.ServiceProcess;

namespace Performish.Core.Backends
{
    public sealed class ServiceSnapshot
    {
        public string ServiceName { get; set; }
        public bool Existed { get; set; }
        public ServiceStartMode StartMode { get; set; }
        public bool WasRunning { get; set; }
    }

    /// <summary>Windows service start-type + running-state access. Every service tweak reads a
    /// snapshot before changing start mode, so Undo can restore both the start mode and whether the
    /// service was running.</summary>
    public interface IServiceBackend
    {
        bool Exists(string serviceName);
        ServiceSnapshot Read(string serviceName);
        void SetStartMode(string serviceName, ServiceStartMode mode);
        void Stop(string serviceName);
        void Start(string serviceName);
        void Restore(ServiceSnapshot snapshot);
    }

    /// <summary>Real service control via System.ServiceProcess + the registry (ServiceController has
    /// no start-type setter; the start type lives at
    /// HKLM\SYSTEM\CurrentControlSet\Services\{name}\Start and is normally changed with sc.exe /
    /// the Win32_Service WMI class - here routed through IProcessRunner's "sc" calls so it goes
    /// through the same real/fake seam as everything else).</summary>
    public sealed class RealServiceBackend : IServiceBackend
    {
        private readonly IProcessRunner _process;

        public RealServiceBackend(IProcessRunner process)
        {
            _process = process ?? throw new ArgumentNullException(nameof(process));
        }

        public bool Exists(string serviceName)
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                var _ = sc.Status; // throws if the service doesn't exist
                return true;
            }
            catch
            {
                return false;
            }
        }

        public ServiceSnapshot Read(string serviceName)
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                return new ServiceSnapshot
                {
                    ServiceName = serviceName,
                    Existed = true,
                    StartMode = sc.StartType,
                    WasRunning = sc.Status == ServiceControllerStatus.Running
                };
            }
            catch
            {
                return new ServiceSnapshot { ServiceName = serviceName, Existed = false };
            }
        }

        public void SetStartMode(string serviceName, ServiceStartMode mode)
        {
            var arg = mode switch
            {
                ServiceStartMode.Disabled => "disabled",
                ServiceStartMode.Manual => "demand",
                ServiceStartMode.Automatic => "auto",
                _ => "demand"
            };
            _process.Run("sc.exe", $"config \"{serviceName}\" start= {arg}");
        }

        public void Stop(string serviceName)
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                if (sc.Status != ServiceControllerStatus.Stopped)
                    sc.Stop();
            }
            catch { /* best-effort - a service that can't be stopped shouldn't abort the batch */ }
        }

        public void Start(string serviceName)
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                if (sc.Status == ServiceControllerStatus.Stopped)
                    sc.Start();
            }
            catch { /* best-effort */ }
        }

        public void Restore(ServiceSnapshot snapshot)
        {
            if (!snapshot.Existed) return;
            SetStartMode(snapshot.ServiceName, snapshot.StartMode);
            if (snapshot.WasRunning) Start(snapshot.ServiceName);
        }
    }

    /// <summary>In-memory simulation for tests.</summary>
    public sealed class FakeServiceBackend : IServiceBackend
    {
        private sealed class State
        {
            public ServiceStartMode StartMode;
            public bool Running;
        }

        private readonly Dictionary<string, State> _services = new Dictionary<string, State>();

        public void Seed(string serviceName, ServiceStartMode mode, bool running) =>
            _services[serviceName] = new State { StartMode = mode, Running = running };

        public bool Exists(string serviceName) => _services.ContainsKey(serviceName);

        public ServiceSnapshot Read(string serviceName)
        {
            if (_services.TryGetValue(serviceName, out var state))
                return new ServiceSnapshot { ServiceName = serviceName, Existed = true, StartMode = state.StartMode, WasRunning = state.Running };
            return new ServiceSnapshot { ServiceName = serviceName, Existed = false };
        }

        public void SetStartMode(string serviceName, ServiceStartMode mode)
        {
            if (!_services.TryGetValue(serviceName, out var state))
            {
                state = new State();
                _services[serviceName] = state;
            }
            state.StartMode = mode;
        }

        public void Stop(string serviceName)
        {
            if (_services.TryGetValue(serviceName, out var state)) state.Running = false;
        }

        public void Start(string serviceName)
        {
            if (_services.TryGetValue(serviceName, out var state)) state.Running = true;
        }

        public void Restore(ServiceSnapshot snapshot)
        {
            if (!snapshot.Existed) return;
            SetStartMode(snapshot.ServiceName, snapshot.StartMode);
            if (snapshot.WasRunning) Start(snapshot.ServiceName);
            else Stop(snapshot.ServiceName);
        }
    }
}
