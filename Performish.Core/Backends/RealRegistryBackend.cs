using System;
using Microsoft.Win32;

namespace Performish.Core.Backends
{
    /// <summary>Real Win32 registry access. Only ever wired up in the shipped app's composition
    /// root - never constructed by this project's test suite or by this dev session.</summary>
    public sealed class RealRegistryBackend : IRegistryBackend
    {
        private static RegistryKey OpenBase(RegistryHive hive) =>
            RegistryKey.OpenBaseKey(hive, RegistryView.Default);

        public RegistryValueSnapshot Read(RegistryHive hive, string subKeyPath, string valueName)
        {
            using var baseKey = OpenBase(hive);
            using var key = baseKey.OpenSubKey(subKeyPath, writable: false);
            if (key == null)
                return new RegistryValueSnapshot { Hive = hive, SubKeyPath = subKeyPath, ValueName = valueName, Existed = false };

            var value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value == null)
                return new RegistryValueSnapshot { Hive = hive, SubKeyPath = subKeyPath, ValueName = valueName, Existed = false };

            return new RegistryValueSnapshot
            {
                Hive = hive,
                SubKeyPath = subKeyPath,
                ValueName = valueName,
                Existed = true,
                Kind = key.GetValueKind(valueName),
                Value = value
            };
        }

        public void Write(RegistryHive hive, string subKeyPath, string valueName, object value, RegistryValueKind kind)
        {
            using var baseKey = OpenBase(hive);
            using var key = baseKey.CreateSubKey(subKeyPath, writable: true)
                ?? throw new InvalidOperationException($"Could not open or create key {subKeyPath}.");
            key.SetValue(valueName, value, kind);
        }

        public void Delete(RegistryHive hive, string subKeyPath, string valueName)
        {
            using var baseKey = OpenBase(hive);
            using var key = baseKey.OpenSubKey(subKeyPath, writable: true);
            key?.DeleteValue(valueName, throwOnMissingValue: false);
        }

        public bool KeyExists(RegistryHive hive, string subKeyPath)
        {
            using var baseKey = OpenBase(hive);
            using var key = baseKey.OpenSubKey(subKeyPath, writable: false);
            return key != null;
        }

        public void EnsureKey(RegistryHive hive, string subKeyPath)
        {
            using var baseKey = OpenBase(hive);
            using var key = baseKey.CreateSubKey(subKeyPath, writable: true);
        }

        public void Restore(RegistryValueSnapshot snapshot)
        {
            if (snapshot.Existed)
                Write(snapshot.Hive, snapshot.SubKeyPath, snapshot.ValueName, snapshot.Value, snapshot.Kind);
            else
                Delete(snapshot.Hive, snapshot.SubKeyPath, snapshot.ValueName);
        }
    }
}
