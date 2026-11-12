using System;
using System.Collections.Generic;

namespace Byzantium1071.Campaign
{
    internal static class B1071_ApportionMath
    {
        internal static Dictionary<TKey, int> AllocateByWeights<TKey>(
            int total,
            IReadOnlyDictionary<TKey, int> weights,
            Func<TKey, string> idSelector)
            where TKey : notnull
        {
            var allocation = new Dictionary<TKey, int>();
            if (total <= 0 || weights.Count == 0)
            {
                return allocation;
            }

            int totalWeight = 0;
            foreach (int value in weights.Values)
            {
                if (value > 0)
                {
                    totalWeight += value;
                }
            }

            if (totalWeight <= 0)
            {
                int evenShare = total / weights.Count;
                int remainder = total % weights.Count;
                int index = 0;
                foreach (TKey key in weights.Keys)
                {
                    allocation[key] = evenShare + (index < remainder ? 1 : 0);
                    index++;
                }

                return allocation;
            }

            var remainders = new List<KeyValuePair<TKey, float>>();
            int assigned = 0;
            foreach (KeyValuePair<TKey, int> entry in weights)
            {
                int weight = Math.Max(0, entry.Value);
                float exact = (float)total * weight / totalWeight;
                int whole = (int)Math.Floor(exact);
                allocation[entry.Key] = whole;
                assigned += whole;
                remainders.Add(new KeyValuePair<TKey, float>(entry.Key, exact - whole));
            }

            remainders.Sort((left, right) =>
            {
                int compare = right.Value.CompareTo(left.Value);
                return compare != 0
                    ? compare
                    : string.CompareOrdinal(idSelector(left.Key), idSelector(right.Key));
            });

            int leftover = total - assigned;
            for (int index = 0; index < leftover && index < remainders.Count; index++)
            {
                TKey key = remainders[index].Key;
                allocation[key] = allocation.TryGetValue(key, out int current) ? current + 1 : 1;
            }

            return allocation;
        }
    }
}
