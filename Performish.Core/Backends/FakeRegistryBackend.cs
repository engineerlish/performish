using System.Collections.Generic;
using Microsoft.Win32;

namespace Performish.Core.Backends
{
    /// <summary>In-memory registry simulation - the only registry backend this dev/test session ever
    /// constructs. Behaves like the real one (missing key/value reads as "doesn't exist", Restore
    /// re-deletes a value that didn't exist before) so Apply/Undo logic exercised against it is a
    /// faithful test of the real behavior.</summary>
    public sealed class FakeRegistryBackend : IRegistryBackend
    {
        private sealed class Entry
        {
            public object Value;
            public RegistryValueKind Kind;
        }

        private readonly Dictionary<string, Entry> _values = new Dictionary<string, Entry>();
        private readonly HashSet<string> _keys = new HashSet<string>();

        private static string ValueKey(RegistryHive hive, string subKeyPath, string valueName) =>
            $"{hive}\\{subKeyPath}\\{valueName}";

        private static string KeyKey(RegistryHive hive, string subKeyPath) => $"{hive}\\{subKeyPath}";

        /// <summary>Test helper: seed a value as if it already existed before any tweak ran.</summary>
        public void Seed(RegistryHive hive, string subKeyPath, string valueName, object value, RegistryValueKind kind)
        {
            _keys.Add(KeyKey(hive, subKeyPath));
            _values[ValueKey(hive, subKeyPath, valueName)] = new Entry { Value = value, Kind = kind };
        }

        public RegistryValueSnapshot Read(RegistryHive hive, string subKeyPath, string valueName)
        {
            if (_values.TryGetValue(ValueKey(hive, subKeyPath, valueName), out var entry))
            {
                return new RegistryValueSnapshot
                {
                    Hive = hive,
                    SubKeyPath = subKeyPath,
                    ValueName = valueName,
                    Existed = true,
                    Kind = entry.Kind,
                    Value = entry.Value
                };
            }

            return new RegistryValueSnapshot { Hive = hive, SubKeyPath = subKeyPath, ValueName = valueName, Existed = false };
        }

        public void Write(RegistryHive hive, string subKeyPath, string valueName, object value, RegistryValueKind kind)
        {
            _keys.Add(KeyKey(hive, subKeyPath));
            _values[ValueKey(hive, subKeyPath, valueName)] = new Entry { Value = value, Kind = kind };
        }

        public void Delete(RegistryHive hive, string subKeyPath, string valueName)
        {
            _values.Remove(ValueKey(hive, subKeyPath, valueName));
        }

        public bool KeyExists(RegistryHive hive, string subKeyPath) => _keys.Contains(KeyKey(hive, subKeyPath));

        public void EnsureKey(RegistryHive hive, string subKeyPath) => _keys.Add(KeyKey(hive, subKeyPath));

        public void Restore(RegistryValueSnapshot snapshot)
        {
            if (snapshot.Existed)
                Write(snapshot.Hive, snapshot.SubKeyPath, snapshot.ValueName, snapshot.Value, snapshot.Kind);
            else
                Delete(snapshot.Hive, snapshot.SubKeyPath, snapshot.ValueName);
        }
    }
}
