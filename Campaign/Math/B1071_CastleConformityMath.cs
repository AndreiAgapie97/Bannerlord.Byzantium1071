using System;

namespace Byzantium1071.Campaign
{
    internal static class B1071_CastleConformityMath
    {
        // 24 * (10 + 0.05 * Leadership), in fifths of a point so daily
        // fractions survive save/load without floating-point drift.
        internal static int DailyBudget(int leadership, ref int remainder)
        {
            long fifths = 1200L + 6L * Math.Max(0, leadership) + Math.Max(0, Math.Min(4, remainder));
            remainder = (int)(fifths % 5);
            return (int)Math.Min(int.MaxValue, fifths / 5);
        }

        internal static int ReadyCount(int count, int points, int cost) =>
            Math.Min(Math.Max(0, count), Math.Max(0, points) / Math.Max(1, cost));

        internal static int Capacity(int count, int cost) =>
            (int)Math.Min(int.MaxValue, (long)Math.Max(0, count) * Math.Max(1, cost));

        internal static int LegacyProgress(int count, int cost, int heldDays, int requiredDays)
        {
            if (count <= 0 || heldDays <= 0) return 0;
            if (heldDays >= requiredDays) return Capacity(count, cost);
            // Old saves know only one timer per type. Keep its partial progress
            // toward ONE recruit; only already-ready stacks retain full eligibility.
            return (int)((long)Math.Max(1, cost) * heldDays / Math.Max(1, requiredDays));
        }

        // needs is in stable troop-id order. Completed types leave the rotation;
        // unused budget is discarded when everyone is ready, never banked for arrivals.
        internal static int[] Allocate(int[] needs, int budget, ref int cursor)
        {
            var grants = new int[needs.Length];
            if (needs.Length == 0) { cursor = 0; return grants; }
            cursor = Math.Max(0, cursor) % needs.Length;
            while (budget > 0)
            {
                int active = 0;
                int smallestNeed = int.MaxValue;
                for (int i = 0; i < needs.Length; i++)
                    if (needs[i] > grants[i])
                    {
                        active++;
                        smallestNeed = Math.Min(smallestNeed, needs[i] - grants[i]);
                    }
                if (active == 0) break;
                int rounds = Math.Min(smallestNeed, budget / active);
                if (rounds > 0)
                {
                    int last = cursor;
                    for (int offset = 0; offset < needs.Length; offset++)
                    {
                        int i = (cursor + offset) % needs.Length;
                        if (needs[i] <= grants[i]) continue;
                        grants[i] += rounds;
                        last = i;
                    }
                    budget -= rounds * active;
                    cursor = (last + 1) % needs.Length;
                    continue;
                }
                int scanned = 0;
                while (scanned < needs.Length && needs[cursor] <= grants[cursor])
                {
                    cursor = (cursor + 1) % needs.Length;
                    scanned++;
                }
                if (scanned == needs.Length) break;
                grants[cursor]++;
                budget--;
                cursor = (cursor + 1) % needs.Length;
            }
            return grants;
        }
    }
}
