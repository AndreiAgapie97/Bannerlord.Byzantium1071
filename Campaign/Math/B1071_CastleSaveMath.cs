using System;
using System.Collections.Generic;

namespace Byzantium1071.Campaign
{
    internal readonly struct CastleIntSaveData
    {
        internal CastleIntSaveData(List<string> castleIds, List<string> troopIds, List<int> values)
        {
            CastleIds = castleIds;
            TroopIds = troopIds;
            Values = values;
        }

        internal List<string> CastleIds { get; }
        internal List<string> TroopIds { get; }
        internal List<int> Values { get; }
    }

    internal readonly struct CastleDepositorSaveData
    {
        internal CastleDepositorSaveData(List<string> castleIds, List<string> troopIds, List<string> heroIds, List<int> counts)
        {
            CastleIds = castleIds;
            TroopIds = troopIds;
            HeroIds = heroIds;
            Counts = counts;
        }

        internal List<string> CastleIds { get; }
        internal List<string> TroopIds { get; }
        internal List<string> HeroIds { get; }
        internal List<int> Counts { get; }
    }

    internal readonly struct CastleXpSaveData
    {
        internal CastleXpSaveData(List<string> castleIds, List<string> troopIds, List<string> heroIds, List<string> partyIds, List<int> counts)
        {
            CastleIds = castleIds;
            TroopIds = troopIds;
            HeroIds = heroIds;
            PartyIds = partyIds;
            Counts = counts;
        }

        internal List<string> CastleIds { get; }
        internal List<string> TroopIds { get; }
        internal List<string> HeroIds { get; }
        internal List<string> PartyIds { get; }
        internal List<int> Counts { get; }
    }

    internal static class B1071_CastleSaveMath
    {
        internal static CastleIntSaveData FlattenIntMap(IReadOnlyDictionary<string, Dictionary<string, int>> values)
        {
            var castleIds = new List<string>();
            var troopIds = new List<string>();
            var savedValues = new List<int>();

            foreach (var castleKvp in values)
            {
                foreach (var troopKvp in castleKvp.Value)
                {
                    castleIds.Add(castleKvp.Key);
                    troopIds.Add(troopKvp.Key);
                    savedValues.Add(troopKvp.Value);
                }
            }

            return new CastleIntSaveData(castleIds, troopIds, savedValues);
        }

        internal static Dictionary<string, Dictionary<string, int>> ReadIntMap(
            IReadOnlyList<string>? castleIds,
            IReadOnlyList<string>? troopIds,
            IReadOnlyList<int>? values)
        {
            castleIds ??= Array.Empty<string>();
            troopIds ??= Array.Empty<string>();
            values ??= Array.Empty<int>();

            var result = new Dictionary<string, Dictionary<string, int>>();
            int rowCount = Math.Min(castleIds.Count, Math.Min(troopIds.Count, values.Count));
            for (int index = 0; index < rowCount; index++)
            {
                string castleId = castleIds[index];
                string troopId = troopIds[index];
                if (string.IsNullOrEmpty(castleId) || string.IsNullOrEmpty(troopId))
                {
                    continue;
                }

                if (!result.TryGetValue(castleId, out var troopValues))
                {
                    troopValues = new Dictionary<string, int>();
                    result[castleId] = troopValues;
                }

                troopValues[troopId] = values[index];
            }

            return result;
        }

        internal static CastleDepositorSaveData FlattenDepositors(
            IReadOnlyDictionary<string, Dictionary<string, List<(string HeroId, int Count)>>> values)
        {
            var castleIds = new List<string>();
            var troopIds = new List<string>();
            var heroIds = new List<string>();
            var counts = new List<int>();

            foreach (var castleKvp in values)
            {
                foreach (var troopKvp in castleKvp.Value)
                {
                    foreach (var entry in troopKvp.Value)
                    {
                        if (entry.Count <= 0)
                        {
                            continue;
                        }

                        castleIds.Add(castleKvp.Key);
                        troopIds.Add(troopKvp.Key);
                        heroIds.Add(entry.HeroId);
                        counts.Add(entry.Count);
                    }
                }
            }

            return new CastleDepositorSaveData(castleIds, troopIds, heroIds, counts);
        }

        internal static Dictionary<string, Dictionary<string, List<(string HeroId, int Count)>>> ReadDepositors(
            IReadOnlyList<string>? castleIds,
            IReadOnlyList<string>? troopIds,
            IReadOnlyList<string>? heroIds,
            IReadOnlyList<int>? counts)
        {
            castleIds ??= Array.Empty<string>();
            troopIds ??= Array.Empty<string>();
            heroIds ??= Array.Empty<string>();
            counts ??= Array.Empty<int>();

            var result = new Dictionary<string, Dictionary<string, List<(string HeroId, int Count)>>>();
            int rowCount = Math.Min(castleIds.Count,
                Math.Min(troopIds.Count, Math.Min(heroIds.Count, counts.Count)));
            for (int index = 0; index < rowCount; index++)
            {
                string castleId = castleIds[index];
                string troopId = troopIds[index];
                string heroId = heroIds[index];
                int count = counts[index];
                if (string.IsNullOrEmpty(castleId) || string.IsNullOrEmpty(troopId)
                    || string.IsNullOrEmpty(heroId) || count <= 0)
                {
                    continue;
                }

                if (!result.TryGetValue(castleId, out var troopEntries))
                {
                    troopEntries = new Dictionary<string, List<(string HeroId, int Count)>>();
                    result[castleId] = troopEntries;
                }

                if (!troopEntries.TryGetValue(troopId, out var heroEntries))
                {
                    heroEntries = new List<(string HeroId, int Count)>();
                    troopEntries[troopId] = heroEntries;
                }

                heroEntries.Add((heroId, count));
            }

            return result;
        }

        internal static CastleXpSaveData FlattenXp(
            IReadOnlyDictionary<string, Dictionary<string, List<(string HeroId, string PartyId, int Count)>>> values)
        {
            var castleIds = new List<string>();
            var troopIds = new List<string>();
            var heroIds = new List<string>();
            var partyIds = new List<string>();
            var counts = new List<int>();

            foreach (var castleKvp in values)
            {
                foreach (var troopKvp in castleKvp.Value)
                {
                    foreach (var entry in troopKvp.Value)
                    {
                        if (entry.Count <= 0 || string.IsNullOrEmpty(entry.HeroId))
                        {
                            continue;
                        }

                        castleIds.Add(castleKvp.Key);
                        troopIds.Add(troopKvp.Key);
                        heroIds.Add(entry.HeroId);
                        partyIds.Add(entry.PartyId ?? string.Empty);
                        counts.Add(entry.Count);
                    }
                }
            }

            return new CastleXpSaveData(castleIds, troopIds, heroIds, partyIds, counts);
        }

        internal static Dictionary<string, Dictionary<string, List<(string HeroId, string PartyId, int Count)>>> ReadXp(
            IReadOnlyList<string>? castleIds,
            IReadOnlyList<string>? troopIds,
            IReadOnlyList<string>? heroIds,
            IReadOnlyList<string>? partyIds,
            IReadOnlyList<int>? counts)
        {
            castleIds ??= Array.Empty<string>();
            troopIds ??= Array.Empty<string>();
            heroIds ??= Array.Empty<string>();
            partyIds ??= Array.Empty<string>();
            counts ??= Array.Empty<int>();

            var result = new Dictionary<string, Dictionary<string, List<(string HeroId, string PartyId, int Count)>>>();
            int rowCount = Math.Min(castleIds.Count,
                Math.Min(troopIds.Count, Math.Min(heroIds.Count, Math.Min(partyIds.Count, counts.Count))));
            for (int index = 0; index < rowCount; index++)
            {
                string castleId = castleIds[index];
                string troopId = troopIds[index];
                string heroId = heroIds[index];
                string partyId = partyIds[index] ?? string.Empty;
                int count = counts[index];
                if (string.IsNullOrEmpty(castleId) || string.IsNullOrEmpty(troopId)
                    || string.IsNullOrEmpty(heroId) || count <= 0)
                {
                    continue;
                }

                if (!result.TryGetValue(castleId, out var troopEntries))
                {
                    troopEntries = new Dictionary<string, List<(string HeroId, string PartyId, int Count)>>();
                    result[castleId] = troopEntries;
                }

                if (!troopEntries.TryGetValue(troopId, out var heroEntries))
                {
                    heroEntries = new List<(string HeroId, string PartyId, int Count)>();
                    troopEntries[troopId] = heroEntries;
                }

                heroEntries.Add((heroId, partyId, count));
            }

            return result;
        }
    }
}
