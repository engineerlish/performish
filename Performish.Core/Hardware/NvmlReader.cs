using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Performish.Core.Hardware
{
    /// <summary>Reads NVIDIA GPU telemetry through NVML (nvml.dll), which the NVIDIA driver installs -
    /// Performish bundles nothing. Loaded dynamically; if the library is missing, every NVIDIA reading
    /// is simply unavailable.
    ///
    /// READ-ONLY BY CONSTRUCTION: the only NVML entry points this class can ever resolve are the ones
    /// named in <see cref="ImportedFunctions"/>, and every one of them is a query (init/shutdown aside).
    /// NVML also exports setters (power limits, clock offsets, application clocks); none of them are
    /// named here, so none can be called. HardwareNoWritePathTests enforces that list.</summary>
    public static class NvmlReader
    {
        public static readonly IReadOnlyList<string> ImportedFunctions = new[]
        {
            "nvmlInit_v2",
            "nvmlShutdown",
            "nvmlDeviceGetCount_v2",
            "nvmlDeviceGetHandleByIndex_v2",
            "nvmlDeviceGetName",
            "nvmlDeviceGetClockInfo",
            "nvmlDeviceGetMaxClockInfo",
            "nvmlDeviceGetPowerUsage",
            "nvmlDeviceGetEnforcedPowerLimit",
            "nvmlDeviceGetTemperature",
            "nvmlDeviceGetUtilizationRates",
            "nvmlDeviceGetMemoryInfo",
            "nvmlDeviceGetBAR1MemoryInfo",
        };

        private const int NvmlSuccess = 0;
        private const int ClockGraphics = 0;
        private const int ClockMemory = 2;
        private const int TemperatureGpu = 0;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NoArgs();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetUInt(out uint value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetHandle(uint index, out IntPtr device);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetName(IntPtr device, byte[] name, uint length);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetDeviceUInt(IntPtr device, out uint value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetDeviceTypedUInt(IntPtr device, int type, out uint value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetUtilization(IntPtr device, out Utilization value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetMemory(IntPtr device, out ThreeULong value);

        [StructLayout(LayoutKind.Sequential)] private struct Utilization { public uint Gpu; public uint Memory; }
        [StructLayout(LayoutKind.Sequential)] private struct ThreeULong { public ulong Total; public ulong Free; public ulong Used; }

        private static IEnumerable<string> CandidatePaths()
        {
            // Current drivers put nvml.dll in System32; older ones used the NVSMI folder.
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvml.dll");
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(programFiles))
                yield return Path.Combine(programFiles, "NVIDIA Corporation", "NVSMI", "nvml.dll");
        }

        /// <summary>Returns telemetry for every NVIDIA GPU NVML can see, and the resulting state. Never throws.</summary>
        public static (NvmlState State, List<NvidiaTelemetry> Devices) ReadAll()
        {
            var devices = new List<NvidiaTelemetry>();
            IntPtr lib = IntPtr.Zero;
            foreach (var path in CandidatePaths())
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out lib)) break;
            if (lib == IntPtr.Zero) return (NvmlState.LibraryNotFound, devices);

            try
            {
                T Fn<T>(string name) where T : Delegate
                {
                    // Belt and braces alongside the unit test: refuse any name not in the read-only list.
                    if (!((IList<string>)ImportedFunctions).Contains(name)) throw new InvalidOperationException($"{name} is not an allowed NVML query.");
                    return NativeLibrary.TryGetExport(lib, name, out var p) ? Marshal.GetDelegateForFunctionPointer<T>(p) : null;
                }

                var init = Fn<NoArgs>("nvmlInit_v2");
                var shutdown = Fn<NoArgs>("nvmlShutdown");
                if (init == null || init() != NvmlSuccess) return (NvmlState.InitFailed, devices);

                try
                {
                    var getCount = Fn<GetUInt>("nvmlDeviceGetCount_v2");
                    var getHandle = Fn<GetHandle>("nvmlDeviceGetHandleByIndex_v2");
                    var getName = Fn<GetName>("nvmlDeviceGetName");
                    var getClock = Fn<GetDeviceTypedUInt>("nvmlDeviceGetClockInfo");
                    var getMaxClock = Fn<GetDeviceTypedUInt>("nvmlDeviceGetMaxClockInfo");
                    var getPower = Fn<GetDeviceUInt>("nvmlDeviceGetPowerUsage");
                    var getLimit = Fn<GetDeviceUInt>("nvmlDeviceGetEnforcedPowerLimit");
                    var getTemp = Fn<GetDeviceTypedUInt>("nvmlDeviceGetTemperature");
                    var getUtil = Fn<GetUtilization>("nvmlDeviceGetUtilizationRates");
                    var getMem = Fn<GetMemory>("nvmlDeviceGetMemoryInfo");
                    var getBar1 = Fn<GetMemory>("nvmlDeviceGetBAR1MemoryInfo");

                    if (getCount == null || getHandle == null || getCount(out var count) != NvmlSuccess)
                        return (NvmlState.InitFailed, devices);

                    for (uint i = 0; i < count; i++)
                    {
                        if (getHandle(i, out var device) != NvmlSuccess) continue;
                        var t = new NvidiaTelemetry();

                        var nameBuffer = new byte[96];
                        if (getName != null && getName(device, nameBuffer, (uint)nameBuffer.Length) == NvmlSuccess)
                            t.Name = Encoding.ASCII.GetString(nameBuffer).TrimEnd('\0').Trim();

                        if (getClock != null && getClock(device, ClockGraphics, out var core) == NvmlSuccess) t.CoreClockMhz = (int)core;
                        if (getClock != null && getClock(device, ClockMemory, out var mem) == NvmlSuccess) t.MemoryClockMhz = (int)mem;
                        if (getMaxClock != null && getMaxClock(device, ClockGraphics, out var max) == NvmlSuccess) t.MaxCoreClockMhz = (int)max;
                        if (getPower != null && getPower(device, out var mw) == NvmlSuccess) t.PowerDrawWatts = mw / 1000.0;
                        if (getLimit != null && getLimit(device, out var limitMw) == NvmlSuccess) t.PowerLimitWatts = limitMw / 1000.0;
                        if (getTemp != null && getTemp(device, TemperatureGpu, out var temp) == NvmlSuccess) t.TemperatureC = (int)temp;
                        if (getUtil != null && getUtil(device, out var util) == NvmlSuccess) t.UtilizationPercent = (int)util.Gpu;
                        if (getMem != null && getMem(device, out var vram) == NvmlSuccess) t.VramTotalBytes = (long)vram.Total;
                        if (getBar1 != null && getBar1(device, out var bar1) == NvmlSuccess) t.Bar1TotalBytes = (long)bar1.Total;

                        devices.Add(t);
                    }
                    return (NvmlState.Ok, devices);
                }
                finally
                {
                    shutdown?.Invoke();
                }
            }
            catch
            {
                return (NvmlState.InitFailed, devices);
            }
            finally
            {
                NativeLibrary.Free(lib);
            }
        }
    }
}
