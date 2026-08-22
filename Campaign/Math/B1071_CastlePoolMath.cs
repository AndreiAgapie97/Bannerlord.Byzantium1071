using System;
using System.Collections.Generic;
using Byzantium1071.Campaign.Settings;

namespace Byzantium1071.Campaign
{
    internal readonly struct CastleFeeSplit
    {
        internal CastleFeeSplit(int recruiterCost, int ownerPayment, int depositorPayment)
        {
            RecruiterCost = recruiterCost;
            OwnerPayment = ownerPayment;
            DepositorPayment = depositorPayment;
        }

        internal int RecruiterCost { get; }
        internal int OwnerPayment { get; }
        internal int DepositorPayment { get; }
    }

    internal static class B1071_CastlePoolMath
    {
        internal static int PoolCapacity(float prosperity, int wallLevel, IB1071Settings settings)
        {
            if (!settings.EnableDynamicPoolCapacity)
            {
                return settings.CastleElitePoolMax;
            }

            int capacity = settings.CastleElitePoolBaseCapDynamic
                + (int)Math.Floor(prosperity * settings.CastleElitePoolProsperityScaling)
                + wallLevel * settings.CastleElitePoolWallBonus;
            return Math.Max(1, capacity);
        }

        internal static int DailyRegenCount(float prosperity, int poolCapacity, int currentCount, IB1071Settings settings)
        {
            float prosperityRatio = Math.Min(1f, prosperity / settings.ProsperityNormalizer);
            int regenMin = settings.CastleEliteRegenMin;
            int regenMax = settings.CastleEliteRegenMax;
            int regen = Math.Max(regenMin, (int)Math.Round(regenMin + (regenMax - regenMin) * prosperityRatio));
            return Math.Min(regen, poolCapacity - currentCount);
        }

        internal static int ChooseWeightedBucketIndex(IReadOnlyList<int> weights, IB1071Random random)
        {
            int totalWeight = TotalWeight(weights);
            if (totalWeight <= 0)
            {
                return -1;
            }

            int roll = random.Next(0, totalWeight);
            int cumulative = 0;
            for (int index = 0; index < weights.Count; index++)
            {
                cumulative += weights[index];
                if (roll < cumulative)
                {
                    return index;
                }
            }

            return weights.Count - 1;
        }

        internal static bool HasPositiveTotalWeight(IReadOnlyList<int> weights) => TotalWeight(weights) > 0;

        internal static int AiBufferedAffordableCount(int gold, int costPerUnit, int maxUnits, IB1071Settings settings)
        {
            if (maxUnits <= 0) return 0;
            if (costPerUnit <= 0) return maxUnits;

            long buffered = (long)costPerUnit * Math.Max(1, settings.CastleAiGoldBufferMultiplier);
            long affordable = (gold - 1) / buffered;
            return (int)Math.Max(0L, Math.Min(maxUnits, affordable));
        }

        internal static int RequiredPrisonerDays(int tier, IB1071Settings settings)
        {
            if (tier <= settings.CastlePrisonerAutoEnslaveTierMax) return 0;
            if (tier == 4) return settings.CastleRecruitT4Days;
            if (tier == 5) return settings.CastleRecruitT5Days;
            return settings.CastleRecruitT6Days;
        }

        internal static int GoldCostForTier(int tier, IB1071Settings settings)
        {
            if (tier <= 2) return settings.CastleRecruitGoldT2;
            if (tier == 3) return settings.CastleRecruitGoldT3;
            if (tier == 4) return settings.CastleRecruitGoldT4;
            if (tier == 5) return settings.CastleRecruitGoldT5;
            return settings.CastleRecruitGoldT6;
        }

        internal static CastleFeeSplit SplitHoldingFee(int totalGold, int holdingFeePercent)
        {
            float feePercent = holdingFeePercent / 100f;
            int ownerPayment = (int)(totalGold * feePercent);
            int depositorPayment = totalGold - ownerPayment;
            return new CastleFeeSplit(totalGold, ownerPayment, depositorPayment);
        }

        internal static CastleFeeSplit RecruitmentFeeSplit(
            int goldCostPerTroop,
            int count,
            int holdingFeePercent,
            bool hasSeparateDepositor,
            bool recruiterIsSameClanAsOwner,
            bool recruiterIsSameClanAsDepositor)
        {
            int totalGold = goldCostPerTroop * count;
            if (!hasSeparateDepositor)
            {
                return recruiterIsSameClanAsOwner
                    ? new CastleFeeSplit(0, 0, 0)
                    : new CastleFeeSplit(totalGold, totalGold, 0);
            }

            CastleFeeSplit holdingFee = SplitHoldingFee(totalGold, holdingFeePercent);
            int ownerPayment = recruiterIsSameClanAsOwner ? 0 : holdingFee.OwnerPayment;
            int depositorPayment = recruiterIsSameClanAsDepositor ? 0 : holdingFee.DepositorPayment;
            return new CastleFeeSplit(ownerPayment + depositorPayment, ownerPayment, depositorPayment);
        }

        private static int TotalWeight(IReadOnlyList<int> weights)
        {
            int totalWeight = 0;
            foreach (int weight in weights)
            {
                totalWeight += weight;
            }

            return totalWeight;
        }
    }
}
