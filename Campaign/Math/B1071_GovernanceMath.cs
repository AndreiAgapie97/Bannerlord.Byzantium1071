using System;
using Byzantium1071.Campaign.Settings;

namespace Byzantium1071.Campaign
{
    internal readonly struct StabilizationTierValues
    {
        internal StabilizationTierValues(
            int cost,
            int duration,
            float strainReduction,
            float loyalty,
            float security,
            float decay)
        {
            Cost = cost;
            Duration = duration;
            StrainReduction = strainReduction;
            Loyalty = loyalty;
            Security = security;
            Decay = decay;
        }

        internal int Cost { get; }
        internal int Duration { get; }
        internal float StrainReduction { get; }
        internal float Loyalty { get; }
        internal float Security { get; }
        internal float Decay { get; }
    }

    internal static class B1071_GovernanceMath
    {
        internal static float AddStrain(float current, float amount, IB1071Settings settings) =>
            Math.Min(settings.GovernanceStrainCap, current + amount);

        internal static float ReduceStrain(float current, float amount) => Math.Max(0f, current - amount);

        internal static float DailyStrain(float current, float baseDecay, float stabilizationDecay) =>
            Math.Max(0f, current - (baseDecay + stabilizationDecay));

        internal static float GovernancePenalty(float strain, float maximumPenalty, IB1071Settings settings)
        {
            float cap = Math.Max(1f, settings.GovernanceStrainCap);
            return -(strain / cap) * maximumPenalty;
        }

        internal static StabilizationTierValues StabilizationTier(int tier, IB1071Settings settings)
        {
            return tier switch
            {
                1 => new StabilizationTierValues(
                    settings.GovernanceStabilizationCostDonative,
                    settings.GovernanceStabilizationDurationDonative,
                    settings.GovernanceStabilizationStrainDonative,
                    settings.GovernanceStabilizationLoyaltyDonative,
                    settings.GovernanceStabilizationSecurityDonative,
                    settings.GovernanceStabilizationDecayDonative),
                2 => new StabilizationTierValues(
                    settings.GovernanceStabilizationCostElites,
                    settings.GovernanceStabilizationDurationElites,
                    settings.GovernanceStabilizationStrainElites,
                    settings.GovernanceStabilizationLoyaltyElites,
                    settings.GovernanceStabilizationSecurityElites,
                    settings.GovernanceStabilizationDecayElites),
                3 => new StabilizationTierValues(
                    settings.GovernanceStabilizationCostAmnesty,
                    settings.GovernanceStabilizationDurationAmnesty,
                    settings.GovernanceStabilizationStrainAmnesty,
                    settings.GovernanceStabilizationLoyaltyAmnesty,
                    settings.GovernanceStabilizationSecurityAmnesty,
                    settings.GovernanceStabilizationDecayAmnesty),
                _ => new StabilizationTierValues(0, 0, 0f, 0f, 0f, 0f)
            };
        }

        internal static int AiStabilizationTier(int gold, IB1071Settings settings)
        {
            int multiplier = Math.Max(1, settings.GovernanceStabilizationAiGoldMultiplier);
            if (gold > settings.GovernanceStabilizationCostAmnesty * multiplier) return 3;
            if (gold > settings.GovernanceStabilizationCostElites * multiplier) return 2;
            if (gold > settings.GovernanceStabilizationCostDonative * multiplier) return 1;
            return 0;
        }

        internal static float AddDevastation(float current, IB1071Settings settings) =>
            Math.Min(100f, current + settings.DevastationPerRaid);

        internal static float DailyDevastation(float current, IB1071Settings settings) =>
            Math.Max(0f, current - settings.DevastationDecayPerDay);

        internal static float DevastationPenalty(float devastation, float maximumPenalty) =>
            -(devastation / 100f) * maximumPenalty;

        internal static float DevastationFoodPenalty(float devastation, IB1071Settings settings) =>
            (devastation / 100f) * settings.DevastationMaxFoodPenaltyPerVillage;
    }
}
