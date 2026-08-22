using Byzantium1071.Campaign;
using FsCheck.Xunit;
using Xunit;

namespace Byzantium1071.Tests
{
    public sealed class EconomyMathTests
    {
        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(6)]
        [InlineData(7)]
        public void TierLookupsClampToTheTableRange(int tier)
        {
            int expectedTier = tier < 1 ? 1 : tier > 6 ? 6 : tier;
            Assert.Equal(B1071_EconomyMath.HireFactor(2, expectedTier), B1071_EconomyMath.HireFactor(2, tier));
            Assert.Equal(B1071_EconomyMath.WageFactor(2, expectedTier), B1071_EconomyMath.WageFactor(2, tier));
            Assert.Equal(B1071_EconomyMath.ArmorFactor(2, expectedTier), B1071_EconomyMath.ArmorFactor(2, tier));
            Assert.Equal(B1071_EconomyMath.SurvivalBonus(2, expectedTier), B1071_EconomyMath.SurvivalBonus(2, tier));
        }

        [Fact]
        public void HighestTiersAndInvalidPresetsFollowTheirExistingPolicies()
        {
            Assert.Equal(B1071_EconomyMath.HireFactor(2, 6), B1071_EconomyMath.HireFactor(2, 7));
            Assert.Equal(B1071_EconomyMath.WageFactor(2, 6), B1071_EconomyMath.WageFactor(2, 7));
            Assert.Equal(B1071_EconomyMath.ArmorFactor(2, 6), B1071_EconomyMath.ArmorFactor(2, 7));
            Assert.Equal(B1071_EconomyMath.SurvivalBonus(2, 6), B1071_EconomyMath.SurvivalBonus(2, 7));
            Assert.Equal(0f, B1071_EconomyMath.HireFactor(4, 6));
            Assert.Equal(0f, B1071_EconomyMath.WageFactor(-1, 6));
            Assert.Equal(B1071_EconomyMath.ArmorFactor(3, 6), B1071_EconomyMath.ArmorFactor(99, 6));
            Assert.Equal(B1071_EconomyMath.SurvivalBonus(0, 6), B1071_EconomyMath.SurvivalBonus(-1, 6));
        }

        [Fact]
        public void TierFactorsAreMonotonicForEveryPreset()
        {
            for (int preset = 0; preset <= 3; preset++)
            {
                for (int tier = 2; tier <= 6; tier++)
                {
                    Assert.True(B1071_EconomyMath.HireFactor(preset, tier) >= B1071_EconomyMath.HireFactor(preset, tier - 1));
                    Assert.True(B1071_EconomyMath.WageFactor(preset, tier) >= B1071_EconomyMath.WageFactor(preset, tier - 1));
                    Assert.True(B1071_EconomyMath.ArmorFactor(preset, tier) <= B1071_EconomyMath.ArmorFactor(preset, tier - 1));
                    Assert.True(B1071_EconomyMath.SurvivalBonus(preset, tier) >= B1071_EconomyMath.SurvivalBonus(preset, tier - 1));
                }
            }
        }

        [Fact]
        public void WageAndGarrisonAdjustmentsUseExistingRoundingAndPercentSemantics()
        {
            Assert.Equal(4, B1071_EconomyMath.AdjustedWage(3, 1, 3));
            Assert.Equal(1, B1071_EconomyMath.AdjustedWage(0, 3, 6));
            Assert.Equal(-0.2f, B1071_EconomyMath.GarrisonWageAddFactor(80), 5);
            Assert.Equal(0f, B1071_EconomyMath.GarrisonWageAddFactor(100));
        }

        [Fact]
        public void SlavePriceUsesTransferStockAndVanillaBounds()
        {
            Assert.Equal(1f, B1071_EconomyMath.SlavePriceFactor(0f, false, 0, 0.9f));
            Assert.Equal(0.9f, B1071_EconomyMath.SlavePriceFactor(0f, true, 300, 0.9f));
            Assert.Equal(0.1f, B1071_EconomyMath.SlavePriceFactor(300000f, false, 0, 0.9f));
            Assert.Equal(10f, B1071_EconomyMath.SlavePriceFactor(300f, false, 0, 12f));
        }

        [Property(MaxTest = 1000)]
        public bool PriceAndPresetLookupsRemainFinite(int preset, int tier, float inStoreValue, int transferValue)
        {
            if (float.IsNaN(inStoreValue) || float.IsInfinity(inStoreValue))
            {
                return true;
            }

            float price = B1071_EconomyMath.SlavePriceFactor(inStoreValue, true, transferValue, 0.9f);
            float hire = B1071_EconomyMath.HireFactor(preset, tier);
            float wage = B1071_EconomyMath.WageFactor(preset, tier);
            float armor = B1071_EconomyMath.ArmorFactor(preset, tier);
            float survival = B1071_EconomyMath.SurvivalBonus(preset, tier);

            return !float.IsNaN(price) && !float.IsInfinity(price)
                && !float.IsNaN(hire) && !float.IsInfinity(hire)
                && !float.IsNaN(wage) && !float.IsInfinity(wage)
                && !float.IsNaN(armor) && !float.IsInfinity(armor)
                && !float.IsNaN(survival) && !float.IsInfinity(survival);
        }
    }
}
