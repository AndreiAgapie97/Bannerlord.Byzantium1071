using System;
using Byzantium1071.Campaign.Settings;

namespace Byzantium1071.Campaign
{
    internal readonly struct SlaveDecayResult
    {
        internal SlaveDecayResult(int wholeLoss, float remainingAccumulator)
        {
            WholeLoss = wholeLoss;
            RemainingAccumulator = remainingAccumulator;
        }

        internal int WholeLoss { get; }
        internal float RemainingAccumulator { get; }
    }

    internal static class B1071_SlaveMath
    {
        internal static float MaxReasonableSupply(int slaveCount, int slaveItemValue) =>
            slaveCount * slaveItemValue * 2f + 3000f;

        internal static int RaidHearthDivisor(IB1071Settings settings) => Math.Max(1, settings.SlaveHearthDivisor);

        internal static int RaidSlaveCount(float hearths, IB1071Settings settings) =>
            (int)(hearths / RaidHearthDivisor(settings));

        internal static SlaveDecayResult DailyDecay(
            int slaveCount,
            float priorAccumulator,
            IB1071Settings settings)
        {
            float rawLoss = slaveCount * (settings.SlaveDailyDecayPercent / 100f);
            float accumulated = priorAccumulator + rawLoss;
            int wholeLoss = Math.Min((int)accumulated, slaveCount);
            return new SlaveDecayResult(wholeLoss, accumulated - wholeLoss);
        }

        internal static int SlaveCap(float prosperity, IB1071Settings settings)
        {
            return Math.Max(
                settings.SlaveCapMinimum,
                (int)(prosperity * settings.SlaveCapPerProsperity));
        }

        internal static float ProsperityBonus(int slaveCount, IB1071Settings settings) =>
            slaveCount * settings.SlaveProsperityPerUnit * settings.SlaveRansomMultiplier;

        internal static float ConstructionBonus(int slaveCount, IB1071Settings settings)
        {
            return Math.Min(
                settings.SlaveConstructionBonusCap,
                slaveCount * settings.SlaveConstructionAcceleration * settings.SlaveRansomMultiplier);
        }

        internal static float FoodConsumption(int slaveCount, IB1071Settings settings) =>
            slaveCount * settings.SlaveFoodConsumptionPerUnit;

        internal static float RogueryXpFromTierSum(int tierSum) => tierSum * 2f;
    }
}
