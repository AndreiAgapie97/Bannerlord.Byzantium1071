using System;
using System.Collections.Generic;
using Byzantium1071.Campaign.Settings;

namespace Byzantium1071.Campaign
{
    internal readonly struct InvestmentTierValues
    {
        internal InvestmentTierValues(int cost, int duration, float bonus, int relation, float influence, int power)
        {
            Cost = cost;
            Duration = duration;
            Bonus = bonus;
            Relation = relation;
            Influence = influence;
            Power = power;
        }

        internal int Cost { get; }
        internal int Duration { get; }
        internal float Bonus { get; }
        internal int Relation { get; }
        internal float Influence { get; }
        internal int Power { get; }
    }

    internal static class B1071_InvestmentMath
    {
        internal static InvestmentTierValues TownTier(int tier, IB1071Settings settings)
        {
            return tier switch
            {
                1 => new InvestmentTierValues(
                    settings.TownInvestCostModest,
                    settings.TownInvestDurationModest,
                    settings.TownInvestProsperityModest,
                    settings.TownInvestRelationModest,
                    settings.TownInvestInfluenceModest,
                    settings.TownInvestPowerModest),
                2 => new InvestmentTierValues(
                    settings.TownInvestCostGenerous,
                    settings.TownInvestDurationGenerous,
                    settings.TownInvestProsperityGenerous,
                    settings.TownInvestRelationGenerous,
                    settings.TownInvestInfluenceGenerous,
                    settings.TownInvestPowerGenerous),
                3 => new InvestmentTierValues(
                    settings.TownInvestCostGrand,
                    settings.TownInvestDurationGrand,
                    settings.TownInvestProsperityGrand,
                    settings.TownInvestRelationGrand,
                    settings.TownInvestInfluenceGrand,
                    settings.TownInvestPowerGrand),
                _ => new InvestmentTierValues(0, 0, 0f, 0, 0f, 0)
            };
        }

        internal static InvestmentTierValues VillageTier(int tier, IB1071Settings settings)
        {
            return tier switch
            {
                1 => new InvestmentTierValues(
                    settings.VillageInvestCostModest,
                    settings.VillageInvestDurationModest,
                    settings.VillageInvestHearthModest,
                    settings.VillageInvestRelationModest,
                    settings.VillageInvestInfluenceModest,
                    settings.VillageInvestPowerModest),
                2 => new InvestmentTierValues(
                    settings.VillageInvestCostGenerous,
                    settings.VillageInvestDurationGenerous,
                    settings.VillageInvestHearthGenerous,
                    settings.VillageInvestRelationGenerous,
                    settings.VillageInvestInfluenceGenerous,
                    settings.VillageInvestPowerGenerous),
                3 => new InvestmentTierValues(
                    settings.VillageInvestCostGrand,
                    settings.VillageInvestDurationGrand,
                    settings.VillageInvestHearthGrand,
                    settings.VillageInvestRelationGrand,
                    settings.VillageInvestInfluenceGrand,
                    settings.VillageInvestPowerGrand),
                _ => new InvestmentTierValues(0, 0, 0f, 0, 0f, 0)
            };
        }

        internal static float ActiveBonus(
            string settlementId,
            IReadOnlyDictionary<string, float> bonuses,
            IReadOnlyDictionary<string, float> daysRemaining)
        {
            string prefix = settlementId + "_";
            float total = 0f;
            foreach (KeyValuePair<string, float> bonus in bonuses)
            {
                if (bonus.Key.StartsWith(prefix, StringComparison.Ordinal)
                    && daysRemaining.TryGetValue(bonus.Key, out float days)
                    && days > 0f)
                {
                    total += bonus.Value;
                }
            }

            return total;
        }

        internal static bool IsHeroCooldownReady(float now, float lastActionDay, int cooldownDays) =>
            cooldownDays <= 0 || now - lastActionDay >= cooldownDays;

        internal static List<int> AffordableTiers(int gold, int multiplier, InvestmentTierValues modest,
            InvestmentTierValues generous, InvestmentTierValues grand)
        {
            var affordable = new List<int>(3);
            if (gold > modest.Cost * multiplier) affordable.Add(1);
            if (gold > generous.Cost * multiplier) affordable.Add(2);
            if (gold > grand.Cost * multiplier) affordable.Add(3);
            return affordable;
        }
    }
}
