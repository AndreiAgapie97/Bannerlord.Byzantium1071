using System;
using Byzantium1071.Campaign;
using FsCheck.Xunit;
using Xunit;

namespace Byzantium1071.Tests
{
    public sealed class SlaveMathTests
    {
        [Fact]
        public void FractionalDecayAccumulatesWithoutLosingWholeSlaves()
        {
            FakeSettings settings = SlaveSettings();
            settings.SlaveDailyDecayPercent = 0.5f;
            int remaining = 100;
            float accumulator = 0f;

            for (int day = 0; day < 100; day++)
            {
                SlaveDecayResult decay = B1071_SlaveMath.DailyDecay(remaining, accumulator, settings);
                remaining -= decay.WholeLoss;
                accumulator = decay.RemainingAccumulator;
            }

            float expectedLoss = 100f * (1f - (float)Math.Pow(0.995f, 100));
            Assert.InRange(Math.Abs((100 - remaining) - expectedLoss), 0f, 1f);
            Assert.InRange(accumulator, 0f, 1f);
        }

        [Fact]
        public void DecayAndCapacityKeepSlaveCountsWithinTheirValidBounds()
        {
            FakeSettings settings = SlaveSettings();
            settings.SlaveDailyDecayPercent = 200f;
            settings.SlaveCapMinimum = 10;
            settings.SlaveCapPerProsperity = 0.01f;

            SlaveDecayResult decay = B1071_SlaveMath.DailyDecay(7, 0f, settings);
            Assert.Equal(7, decay.WholeLoss);
            Assert.Equal(10, B1071_SlaveMath.SlaveCap(500f, settings));
            Assert.Equal(12, B1071_SlaveMath.SlaveCap(1200f, settings));
        }

        [Fact]
        public void LaborOutputsApplyEffectivenessAndConstructionCap()
        {
            FakeSettings settings = SlaveSettings();
            settings.SlaveRansomMultiplier = 2f;
            settings.SlaveProsperityPerUnit = 0.2f;
            settings.SlaveConstructionAcceleration = 0.5f;
            settings.SlaveConstructionBonusCap = 8f;
            settings.SlaveFoodConsumptionPerUnit = 0.05f;

            Assert.Equal(8f, B1071_SlaveMath.ProsperityBonus(20, settings));
            Assert.Equal(8f, B1071_SlaveMath.ConstructionBonus(20, settings));
            Assert.Equal(1f, B1071_SlaveMath.FoodConsumption(20, settings));
        }

        [Fact]
        public void RaidYieldSupplyCorrectionAndRogueryXpUseTheirDocumentedRates()
        {
            FakeSettings settings = SlaveSettings();
            settings.SlaveHearthDivisor = 50;

            Assert.Equal(3, B1071_SlaveMath.RaidSlaveCount(199f, settings));
            Assert.Equal(3300f, B1071_SlaveMath.MaxReasonableSupply(1, 150));
            Assert.Equal(24f, B1071_SlaveMath.RogueryXpFromTierSum(12));
        }

        [Property(MaxTest = 1000)]
        public bool DecayNeverRemovesMoreSlavesThanExist(int slaveCount, float accumulator, float decayPercent)
        {
            if (slaveCount < 0 || float.IsNaN(accumulator) || float.IsInfinity(accumulator)
                || float.IsNaN(decayPercent) || float.IsInfinity(decayPercent))
            {
                return true;
            }

            FakeSettings settings = SlaveSettings();
            settings.SlaveDailyDecayPercent = decayPercent;
            SlaveDecayResult decay = B1071_SlaveMath.DailyDecay(slaveCount, accumulator, settings);
            return decay.WholeLoss <= slaveCount;
        }

        [Theory]
        [InlineData(100, 57, 0)]
        [InlineData(100, 57, 43)]
        [InlineData(100, 0, 100)]
        [InlineData(0, 0, 100)]
        public void EscortDeltaReproducesNativeCurveForCombinedCaptives(int men, int prisoners, int slaves)
        {
            float native = B1071_SlaveMath.EscortFactor(men, prisoners);
            float extra = B1071_SlaveMath.EscortFactor(men, prisoners + slaves) - native;
            double expected = 1d / Math.Pow((10d + men + prisoners + slaves) / (10d + men), 0.33d) - 1d;
            Assert.InRange(Math.Abs(native + extra - expected), 0d, 0.000001d);
            Assert.True(extra <= 0f);
        }

        [Theory]
        [InlineData(0, 100, 0f)]
        [InlineData(100, 100, 0f)]
        [InlineData(200, 100, -0.5f)]
        [InlineData(100, 0, -1f)]
        public void CapacityBurdenUsesOneCombinedRatio(int captives, int capacity, float expected) =>
            Assert.Equal(expected, B1071_SlaveMath.OverCapacityFactor(captives, capacity));

        private static FakeSettings SlaveSettings() =>
            new()
            {
                SlaveHearthDivisor = 50,
                SlaveCapMinimum = 10,
                SlaveCapPerProsperity = 0.01f,
                SlaveRansomMultiplier = 1f,
                SlaveProsperityPerUnit = 0.1f,
                SlaveConstructionAcceleration = 0.5f,
                SlaveConstructionBonusCap = 10f,
                SlaveFoodConsumptionPerUnit = 0.05f,
                SlaveDailyDecayPercent = 0.5f
            };
    }
}
