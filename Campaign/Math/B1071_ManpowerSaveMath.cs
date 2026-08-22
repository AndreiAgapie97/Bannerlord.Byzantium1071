using System;
using System.Collections.Generic;

namespace Byzantium1071.Campaign
{
    internal readonly struct StringMapSaveData<T>
    {
        internal StringMapSaveData(List<string> keys, List<T> values)
        {
            Keys = keys;
            Values = values;
        }

        internal List<string> Keys { get; }
        internal List<T> Values { get; }
    }

    internal readonly struct RecoveryPenaltySaveData
    {
        internal RecoveryPenaltySaveData(List<string> poolIds, List<float> baseValues, List<float> startDays, List<float> expiryDays)
        {
            PoolIds = poolIds;
            BaseValues = baseValues;
            StartDays = startDays;
            ExpiryDays = expiryDays;
        }

        internal List<string> PoolIds { get; }
        internal List<float> BaseValues { get; }
        internal List<float> StartDays { get; }
        internal List<float> ExpiryDays { get; }
    }

    internal readonly struct CasualtySaveData
    {
        internal CasualtySaveData(List<string> keys, List<int> killsA, List<int> killsB)
        {
            Keys = keys;
            KillsA = killsA;
            KillsB = killsB;
        }

        internal List<string> Keys { get; }
        internal List<int> KillsA { get; }
        internal List<int> KillsB { get; }
    }

    internal static class B1071_ManpowerSaveMath
    {
        internal static StringMapSaveData<T> FlattenStringMap<T>(IReadOnlyDictionary<string, T> values)
        {
            var keys = new List<string>();
            var savedValues = new List<T>();
            foreach (var kvp in values)
            {
                keys.Add(kvp.Key);
                savedValues.Add(kvp.Value);
            }

            return new StringMapSaveData<T>(keys, savedValues);
        }

        internal static void ReplaceStringMap<T>(
            IDictionary<string, T> destination,
            IReadOnlyList<string>? keys,
            IReadOnlyList<T>? values)
        {
            keys ??= Array.Empty<string>();
            values ??= Array.Empty<T>();

            destination.Clear();
            int rowCount = Math.Min(keys.Count, values.Count);
            for (int index = 0; index < rowCount; index++)
            {
                string key = keys[index];
                if (!string.IsNullOrEmpty(key))
                {
                    destination[key] = values[index];
                }
            }
        }

        internal static RecoveryPenaltySaveData FlattenRecoveryPenalties(
            IReadOnlyDictionary<string, float> baseValues,
            IReadOnlyDictionary<string, float> startDays,
            IReadOnlyDictionary<string, float> expiryDays,
            float defaultDay)
        {
            var poolIds = new List<string>();
            var savedBaseValues = new List<float>();
            var savedStartDays = new List<float>();
            var savedExpiryDays = new List<float>();

            foreach (var kvp in baseValues)
            {
                string poolId = kvp.Key;
                if (string.IsNullOrEmpty(poolId))
                {
                    continue;
                }

                poolIds.Add(poolId);
                savedBaseValues.Add(kvp.Value);
                savedStartDays.Add(startDays.TryGetValue(poolId, out float startDay) ? startDay : defaultDay);
                savedExpiryDays.Add(expiryDays.TryGetValue(poolId, out float expiryDay) ? expiryDay : defaultDay);
            }

            return new RecoveryPenaltySaveData(poolIds, savedBaseValues, savedStartDays, savedExpiryDays);
        }

        internal static void ReplaceRecoveryPenalties(
            IDictionary<string, float> destinationBaseValues,
            IDictionary<string, float> destinationStartDays,
            IDictionary<string, float> destinationExpiryDays,
            IReadOnlyList<string>? poolIds,
            IReadOnlyList<float>? baseValues,
            IReadOnlyList<float>? startDays,
            IReadOnlyList<float>? expiryDays)
        {
            poolIds ??= Array.Empty<string>();
            baseValues ??= Array.Empty<float>();
            startDays ??= Array.Empty<float>();
            expiryDays ??= Array.Empty<float>();

            destinationBaseValues.Clear();
            destinationStartDays.Clear();
            destinationExpiryDays.Clear();

            int rowCount = Math.Min(
                Math.Min(poolIds.Count, baseValues.Count),
                Math.Min(startDays.Count, expiryDays.Count));
            for (int index = 0; index < rowCount; index++)
            {
                string poolId = poolIds[index];
                if (string.IsNullOrEmpty(poolId))
                {
                    continue;
                }

                destinationBaseValues[poolId] = Math.Max(0f, baseValues[index]);
                destinationStartDays[poolId] = startDays[index];
                destinationExpiryDays[poolId] = expiryDays[index];
            }
        }

        internal static CasualtySaveData FlattenCasualties(
            IReadOnlyDictionary<string, (int killsA, int killsB)> values)
        {
            var keys = new List<string>();
            var killsA = new List<int>();
            var killsB = new List<int>();
            foreach (var kvp in values)
            {
                keys.Add(kvp.Key);
                killsA.Add(kvp.Value.killsA);
                killsB.Add(kvp.Value.killsB);
            }

            return new CasualtySaveData(keys, killsA, killsB);
        }

        internal static void ReplaceCasualties(
            IDictionary<string, (int killsA, int killsB)> destination,
            IReadOnlyList<string>? keys,
            IReadOnlyList<int>? killsA,
            IReadOnlyList<int>? killsB)
        {
            keys ??= Array.Empty<string>();
            killsA ??= Array.Empty<int>();
            killsB ??= Array.Empty<int>();

            destination.Clear();
            int rowCount = Math.Min(keys.Count, Math.Min(killsA.Count, killsB.Count));
            for (int index = 0; index < rowCount; index++)
            {
                string key = keys[index];
                if (!string.IsNullOrEmpty(key))
                {
                    destination[key] = (killsA[index], killsB[index]);
                }
            }
        }
    }
}
