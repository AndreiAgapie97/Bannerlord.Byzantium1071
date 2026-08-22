using System.Collections.Generic;
using Byzantium1071.Campaign;
using Xunit;

namespace Byzantium1071.Tests
{
    public sealed class CastleSaveMathTests
    {
        [Fact]
        public void IntegerMapsRoundTripAllValidEntries()
        {
            var source = new Dictionary<string, Dictionary<string, int>>
            {
                ["castle_a"] = new Dictionary<string, int> { ["troop_a"] = 7, ["troop_b"] = -2 },
                ["castle_b"] = new Dictionary<string, int> { ["troop_c"] = 13 }
            };

            CastleIntSaveData saved = B1071_CastleSaveMath.FlattenIntMap(source);
            var loaded = B1071_CastleSaveMath.ReadIntMap(saved.CastleIds, saved.TroopIds, saved.Values);

            Assert.Equal(3, saved.Values.Count);
            Assert.Equal(7, loaded["castle_a"]["troop_a"]);
            Assert.Equal(-2, loaded["castle_a"]["troop_b"]);
            Assert.Equal(13, loaded["castle_b"]["troop_c"]);
        }

        [Fact]
        public void IntegerMapsIgnoreIncompleteAndBlankRowsButKeepTheLastDuplicate()
        {
            var loaded = B1071_CastleSaveMath.ReadIntMap(
                new[] { "castle_a", "", "castle_a", "castle_c" },
                new[] { "troop_a", "troop_b", "troop_a" },
                new[] { 3, 4, 9, 12 });

            KeyValuePair<string, Dictionary<string, int>> castle = Assert.Single(loaded);
            Assert.Equal("castle_a", castle.Key);
            Assert.Equal(9, castle.Value["troop_a"]);
        }

        [Fact]
        public void DepositorEntriesRoundTripInFifoOrder()
        {
            var source = new Dictionary<string, Dictionary<string, List<(string HeroId, int Count)>>>
            {
                ["castle_a"] = new Dictionary<string, List<(string HeroId, int Count)>>
                {
                    ["troop_a"] = new List<(string HeroId, int Count)> { ("hero_a", 2), ("hero_b", 5), ("ignored", 0) }
                }
            };

            CastleDepositorSaveData saved = B1071_CastleSaveMath.FlattenDepositors(source);
            var loaded = B1071_CastleSaveMath.ReadDepositors(saved.CastleIds, saved.TroopIds, saved.HeroIds, saved.Counts);
            List<(string HeroId, int Count)> entries = loaded["castle_a"]["troop_a"];

            Assert.Equal(2, entries.Count);
            Assert.Equal(("hero_a", 2), entries[0]);
            Assert.Equal(("hero_b", 5), entries[1]);
        }

        [Fact]
        public void DepositorEntriesIgnoreInvalidAndTruncatedRows()
        {
            var loaded = B1071_CastleSaveMath.ReadDepositors(
                new[] { "castle_a", "", "castle_b", "castle_c" },
                new[] { "troop_a", "troop_b", "troop_c" },
                new[] { "hero_a", "hero_b", "", "hero_c" },
                new[] { 4, 4, 4, 4 });

            KeyValuePair<string, Dictionary<string, List<(string HeroId, int Count)>>> castle = Assert.Single(loaded);
            Assert.Equal("castle_a", castle.Key);
            List<(string HeroId, int Count)> entries = castle.Value["troop_a"];
            Assert.Equal(("hero_a", 4), Assert.Single(entries));
        }

        [Fact]
        public void XpEntriesRoundTripAndNormalizeEmptyPartyIds()
        {
            var source = new Dictionary<string, Dictionary<string, List<(string HeroId, string PartyId, int Count)>>>
            {
                ["castle_a"] = new Dictionary<string, List<(string HeroId, string PartyId, int Count)>>
                {
                    ["troop_a"] = new List<(string HeroId, string PartyId, int Count)> { ("hero_a", string.Empty, 6), (string.Empty, "party_b", 3) }
                }
            };

            CastleXpSaveData saved = B1071_CastleSaveMath.FlattenXp(source);
            var loaded = B1071_CastleSaveMath.ReadXp(saved.CastleIds, saved.TroopIds, saved.HeroIds, saved.PartyIds, saved.Counts);
            List<(string HeroId, string PartyId, int Count)> entries = loaded["castle_a"]["troop_a"];

            Assert.Equal(("hero_a", string.Empty, 6), Assert.Single(entries));
        }

        [Fact]
        public void XpEntriesIgnoreRowsMissingAnyRequiredField()
        {
            var loaded = B1071_CastleSaveMath.ReadXp(
                new[] { "castle_a", "", "castle_b", "castle_c" },
                new[] { "troop_a", "troop_b", "troop_c", "troop_d" },
                new[] { "hero_a", "hero_b", string.Empty, "hero_d" },
                new[] { "party_a", "party_b", "party_c" },
                new[] { 3, 3, 3, 3 });

            KeyValuePair<string, Dictionary<string, List<(string HeroId, string PartyId, int Count)>>> castle = Assert.Single(loaded);
            Assert.Equal("castle_a", castle.Key);
            Assert.Equal(("hero_a", "party_a", 3), Assert.Single(castle.Value["troop_a"]));
        }
    }
}
