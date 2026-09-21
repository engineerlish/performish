using System.Text.Json;
using Microsoft.Win32;
using Performish.Core.Backends;
using Performish.Core.Backup;
using Xunit;

namespace Performish.Tests
{
    public class BackupTests
    {
        [Fact]
        public void RegistryValueSnapshot_JsonRoundTrip_PreservesIntValue()
        {
            var snap = new RegistryValueSnapshot
            {
                Hive = RegistryHive.CurrentUser,
                SubKeyPath = @"Software\Test",
                ValueName = "V",
                Existed = true,
                Kind = RegistryValueKind.DWord,
                Value = 42
            };

            var json = JsonSerializer.Serialize(snap);
            var roundTripped = JsonSerializer.Deserialize<RegistryValueSnapshot>(json);

            // Regression test: an `object`-typed property deserializes to a boxed JsonElement, not the
            // original CLR int, unless the snapshot routes through the typed Int/String/... fields -
            // see IRegistryBackend.cs's comment on RegistryValueSnapshot for why this matters.
            Assert.IsType<int>(roundTripped.Value);
            Assert.Equal(42, roundTripped.Value);
        }

        [Fact]
        public void RegistryValueSnapshot_JsonRoundTrip_PreservesStringValue()
        {
            var snap = new RegistryValueSnapshot
            {
                Hive = RegistryHive.CurrentUser,
                SubKeyPath = @"Control Panel\Desktop",
                ValueName = "MenuShowDelay",
                Existed = true,
                Kind = RegistryValueKind.String,
                Value = "400"
            };

            var json = JsonSerializer.Serialize(snap);
            var roundTripped = JsonSerializer.Deserialize<RegistryValueSnapshot>(json);

            Assert.IsType<string>(roundTripped.Value);
            Assert.Equal("400", roundTripped.Value);
        }

        [Fact]
        public void InMemoryUndoStore_SaveThenLoad_RoundTripsThroughJson()
        {
            var store = new InMemoryUndoStore();
            var snap = new RegistryValueSnapshot { Existed = true, Kind = RegistryValueKind.DWord, Value = 7 };

            store.Save("id1", snap);
            var loaded = store.Load<RegistryValueSnapshot>("id1");

            Assert.Equal(7, loaded.Value);
            Assert.True(store.Has("id1"));
        }

        [Fact]
        public void InMemoryUndoStore_MissingId_ReturnsDefault()
        {
            var store = new InMemoryUndoStore();
            Assert.False(store.Has("missing"));
            Assert.Null(store.Load<RegistryValueSnapshot>("missing"));
        }

        [Fact]
        public void InMemoryChangeLogStore_ReadAll_IsOrderedByTimestamp()
        {
            var store = new InMemoryChangeLogStore();
            var t0 = System.DateTime.UtcNow;

            store.Append(new ChangeLogEntry { TimestampUtc = t0.AddMinutes(2), TweakId = "b", Action = ChangeLogAction.Apply, Outcome = Performish.Core.Models.OperationOutcome.Success });
            store.Append(new ChangeLogEntry { TimestampUtc = t0, TweakId = "a", Action = ChangeLogAction.Apply, Outcome = Performish.Core.Models.OperationOutcome.Success });

            var all = store.ReadAll();
            Assert.Equal("a", all[0].TweakId);
            Assert.Equal("b", all[1].TweakId);
        }

        [Fact]
        public void FakeRestorePointBackend_TryCreate_RecordsCallAndDescription()
        {
            var backend = new FakeRestorePointBackend();
            var result = backend.TryCreate("before applying tweaks");

            Assert.True(result);
            Assert.Equal(1, backend.CreateCallCount);
            Assert.Equal("before applying tweaks", backend.LastDescription);
        }
    }
}
