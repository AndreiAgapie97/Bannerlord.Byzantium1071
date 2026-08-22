using System.Collections.Generic;
using Byzantium1071.Campaign;
using Xunit;

namespace Byzantium1071.Tests
{
    public sealed class ManpowerSaveMathTests
    {
        [Fact]
        public void StringMapsRoundTripAcrossValueTypes()
        {
            var savedInts = B1071_ManpowerSaveMath.FlattenStringMap(
                new Dictionary<string, int> { ["pool_a"] = 12, ["pool_b"] = 7 });
            var savedFloats = B1071_ManpowerSaveMath.FlattenStringMap(
                new Dictionary<string, float> { ["kingdom_a"] = 1.25f });
            var ints = new Dictionary<string, int>();
            var floats = new Dictionary<string, float>();

            B1071_ManpowerSaveMath.ReplaceStringMap(ints, savedInts.Keys, savedInts.Values);
            B1071_ManpowerSaveMath.ReplaceStringMap(floats, savedFloats.Keys, savedFloats.Values);

            Assert.Equal(12, ints["pool_a"]);
            Assert.Equal(7, ints["pool_b"]);
            Assert.Equal(1.25f, floats["kingdom_a"]);
        }

        [Fact]
        public void StringMapsDiscardBlankAndTruncatedRowsWhileKeepingTheLastDuplicate()
        {
            var destination = new Dictionary<string, int> { ["stale"] = 99 };

            B1071_ManpowerSaveMath.ReplaceStringMap(
                destination,
                new[] { "pool_a", "", "pool_a", "pool_c" },
                new[] { 3, 4, 9 });

            KeyValuePair<string, int> entry = Assert.Single(destination);
            Assert.Equal("pool_a", entry.Key);
            Assert.Equal(9, entry.Value);
        }

        [Fact]
        public void RecoveryPenaltiesRoundTripWithMissingDaysDefaultedAtSaveTime()
        {
            var bases = new Dictionary<string, float> { ["pool_a"] = 3.5f };
            var starts = new Dictionary<string, float>();
            var expiries = new Dictionary<string, float> { ["pool_a"] = 17f };
            RecoveryPenaltySaveData saved = B1071_ManpowerSaveMath.FlattenRecoveryPenalties(
                bases, starts, expiries, defaultDay: 11f);
            var loadedBases = new Dictionary<string, float>();
            var loadedStarts = new Dictionary<string, float>();
            var loadedExpiries = new Dictionary<string, float>();

            B1071_ManpowerSaveMath.ReplaceRecoveryPenalties(
                loadedBases, loadedStarts, loadedExpiries,
                saved.PoolIds, saved.BaseValues, saved.StartDays, saved.ExpiryDays);

            Assert.Equal(3.5f, loadedBases["pool_a"]);
            Assert.Equal(11f, loadedStarts["pool_a"]);
            Assert.Equal(17f, loadedExpiries["pool_a"]);
        }

        [Fact]
        public void RecoveryPenaltiesIgnoreIncompleteRowsAndClampNegativeBases()
        {
            var bases = new Dictionary<string, float>();
            var starts = new Dictionary<string, float>();
            var expiries = new Dictionary<string, float>();

            B1071_ManpowerSaveMath.ReplaceRecoveryPenalties(
                bases, starts, expiries,
                new[] { "pool_a", "", "pool_b" },
                new[] { -2f, 5f, 7f },
                new[] { 1f, 2f },
                new[] { 3f, 4f, 5f });

            KeyValuePair<string, float> entry = Assert.Single(bases);
            Assert.Equal("pool_a", entry.Key);
            Assert.Equal(0f, entry.Value);
            Assert.Equal(1f, starts["pool_a"]);
            Assert.Equal(3f, expiries["pool_a"]);
        }

        [Fact]
        public void CasualtiesRoundTripAndIgnoreInvalidRowsOnLoad()
        {
            var source = new Dictionary<string, (int killsA, int killsB)>
            {
                ["kingdom_a|kingdom_b"] = (3, 8),
                [""] = (5, 5)
            };
            CasualtySaveData saved = B1071_ManpowerSaveMath.FlattenCasualties(source);
            var loaded = new Dictionary<string, (int killsA, int killsB)>();

            B1071_ManpowerSaveMath.ReplaceCasualties(loaded, saved.Keys, saved.KillsA, saved.KillsB);

            KeyValuePair<string, (int killsA, int killsB)> entry = Assert.Single(loaded);
            Assert.Equal("kingdom_a|kingdom_b", entry.Key);
            Assert.Equal((3, 8), entry.Value);
        }

        [Fact]
        public void CasualtiesDiscardTruncatedRowsAndReplaceDuplicates()
        {
            var loaded = new Dictionary<string, (int killsA, int killsB)>();

            B1071_ManpowerSaveMath.ReplaceCasualties(
                loaded,
                new[] { "pair_a", "", "pair_a" },
                new[] { 1, 2, 9 },
                new[] { 3, 4, 8 });

            KeyValuePair<string, (int killsA, int killsB)> entry = Assert.Single(loaded);
            Assert.Equal("pair_a", entry.Key);
            Assert.Equal((9, 8), entry.Value);
        }

        [Fact]
        public void TenThousandStringMapEntriesRoundTripWithoutLosingAlignment()
        {
            var source = new Dictionary<string, int>();
            for (int index = 0; index < 10_000; index++)
            {
                source["pool_" + index] = index - 5_000;
            }

            StringMapSaveData<int> saved = B1071_ManpowerSaveMath.FlattenStringMap(source);
            var loaded = new Dictionary<string, int>();
            B1071_ManpowerSaveMath.ReplaceStringMap(loaded, saved.Keys, saved.Values);

            Assert.Equal(10_000, loaded.Count);
            foreach (KeyValuePair<string, int> entry in source)
            {
                Assert.Equal(entry.Value, loaded[entry.Key]);
            }
        }
    }
}
