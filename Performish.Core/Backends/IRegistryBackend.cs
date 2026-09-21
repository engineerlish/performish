using System;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Performish.Core.Backends
{
    /// <summary>Snapshot of one registry value, or its absence - enough to restore it exactly
    /// (including "the value didn't exist" -> undo deletes it again).
    ///
    /// Value is deliberately NOT the serialized field: System.Text.Json deserializes an `object`-typed
    /// property into a boxed JsonElement rather than the original CLR type, which would silently break
    /// every "is int" / "as string" check the moment a snapshot round-trips through IUndoStore (disk
    /// or in-memory, both go through JsonSerializer). Instead each CLR shape gets its own serializable
    /// field, and Value is a computed, JSON-ignored convenience that reconstructs the right boxed type
    /// from Kind - so callers still just read/write .Value like before.</summary>
    public sealed class RegistryValueSnapshot
    {
        public RegistryHive Hive { get; set; }
        public string SubKeyPath { get; set; }
        public string ValueName { get; set; }
        public bool Existed { get; set; }
        public RegistryValueKind Kind { get; set; }

        public int? IntValue { get; set; }
        public long? LongValue { get; set; }
        public string StringValue { get; set; }
        public string[] StringArrayValue { get; set; }
        public string BinaryValueBase64 { get; set; }

        [JsonIgnore]
        public object Value
        {
            get
            {
                switch (Kind)
                {
                    case RegistryValueKind.DWord: return IntValue;
                    case RegistryValueKind.QWord: return LongValue;
                    case RegistryValueKind.String:
                    case RegistryValueKind.ExpandString: return StringValue;
                    case RegistryValueKind.MultiString: return StringArrayValue;
                    case RegistryValueKind.Binary: return BinaryValueBase64 != null ? Convert.FromBase64String(BinaryValueBase64) : null;
                    default: return StringValue;
                }
            }
            set
            {
                switch (value)
                {
                    case null: break;
                    case int i: IntValue = i; break;
                    case long l: LongValue = l; break;
                    case string s: StringValue = s; break;
                    case string[] sa: StringArrayValue = sa; break;
                    case byte[] b: BinaryValueBase64 = Convert.ToBase64String(b); break;
                    default: StringValue = value.ToString(); break;
                }
            }
        }
    }

    /// <summary>Thin wrapper over the small slice of Win32 registry access every tweak needs.
    /// RealRegistryBackend hits Microsoft.Win32.Registry for real; FakeRegistryBackend simulates an
    /// in-memory tree so tests and (in dry-run's read-only Check calls only, never writes) the
    /// packaged app can run without ever touching this dev machine's registry.</summary>
    public interface IRegistryBackend
    {
        RegistryValueSnapshot Read(RegistryHive hive, string subKeyPath, string valueName);
        void Write(RegistryHive hive, string subKeyPath, string valueName, object value, RegistryValueKind kind);
        void Delete(RegistryHive hive, string subKeyPath, string valueName);
        bool KeyExists(RegistryHive hive, string subKeyPath);
        void EnsureKey(RegistryHive hive, string subKeyPath);

        /// <summary>Restores a value (or its absence) from a prior snapshot - the core of every
        /// registry-tweak's Undo.</summary>
        void Restore(RegistryValueSnapshot snapshot);
    }
}
