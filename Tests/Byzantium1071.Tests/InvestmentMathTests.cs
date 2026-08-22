using System.Collections.Generic;
using Byzantium1071.Campaign;
using FsCheck.Xunit;
using Xunit;

namespace Byzantium1071.Tests
{
    public sealed class InvestmentMathTests
    {
        [Fact]
        public void TownAndVillageTiersReturnTheirConfiguredValues()
        {
            FakeSettings settings = InvestmentSettings();
            InvestmentTierValues town = B1071_InvestmentMath.TownTier(2, settings);
            InvestmentTierValues village = B1071_InvestmentMath.VillageTier(3, settings);

            Assert.Equal(5000, town.Cost);
            Assert.Equal(20, town.Duration);
            Assert.Equal(2f, town.Bonus);
            Assert.Equal(15000, village.Cost);
            Assert.Equal(30, village.Duration);
            Assert.Equal(3f, village.Bonus);
            Assert.Equal(0, B1071_InvestmentMath.TownTier(4, settings).Cost);
        }

        [Fact]
        public void ActiveBonusUsesTheDelimitedSettlementPrefixAndActiveDaysOnly()
        {
            var bonuses = new Dictionary<string, float>
            {
                ["town_A1_lord_1"] = 1f,
                ["town_A11_lord_2"] = 100f,
                ["town_A1_lord_3"] = 2f
            };
            var days = new Dictionary<string, float>
            {
                ["town_A1_lord_1"] = 3f,
                ["town_A11_lord_2"] = 3f,
                ["town_A1_lord_3"] = 0f
            };

            Assert.Equal(1f, B1071_InvestmentMath.ActiveBonus("town_A1", bonuses, days));
        }

        [Fact]
        public void CooldownAndAffordableTiersUseStrictConfiguredThresholds()
        {
            FakeSettings settings = InvestmentSettings();
            InvestmentTierValues modest = B1071_InvestmentMath.TownTier(1, settings);
            InvestmentTierValues generous = B1071_InvestmentMath.TownTier(2, settings);
            InvestmentTierValues grand = B1071_InvestmentMath.TownTier(3, settings);

            Assert.False(B1071_InvestmentMath.IsHeroCooldownReady(14f, 10f, 5));
            Assert.True(B1071_InvestmentMath.IsHeroCooldownReady(15f, 10f, 5));
            Assert.Equal(new[] { 1, 2 }, B1071_InvestmentMath.AffordableTiers(10001, 1, modest, generous, grand));
        }

        [Property(MaxTest = 1000)]
        public bool TierLookupsAlwaysReturnDefinedValues(int tier)
        {
            FakeSettings settings = InvestmentSettings();
            InvestmentTierValues town = B1071_InvestmentMath.TownTier(tier, settings);
            InvestmentTierValues village = B1071_InvestmentMath.VillageTier(tier, settings);
            return town.Cost >= 0 && town.Duration >= 0 && village.Cost >= 0 && village.Duration >= 0;
        }

        private static FakeSettings InvestmentSettings() =>
            new()
            {
                TownInvestCostModest = 1000,
                TownInvestDurationModest = 10,
                TownInvestProsperityModest = 1f,
                TownInvestRelationModest = 2,
                TownInvestInfluenceModest = 3f,
                TownInvestPowerModest = 4,
                TownInvestCostGenerous = 5000,
                TownInvestDurationGenerous = 20,
                TownInvestProsperityGenerous = 2f,
                TownInvestRelationGenerous = 4,
                TownInvestInfluenceGenerous = 6f,
                TownInvestPowerGenerous = 8,
                TownInvestCostGrand = 15000,
                TownInvestDurationGrand = 30,
                TownInvestProsperityGrand = 3f,
                TownInvestRelationGrand = 6,
                TownInvestInfluenceGrand = 9f,
                TownInvestPowerGrand = 12,
                VillageInvestCostModest = 1000,
                VillageInvestDurationModest = 10,
                VillageInvestHearthModest = 1f,
                VillageInvestRelationModest = 2,
                VillageInvestInfluenceModest = 3f,
                VillageInvestPowerModest = 4,
                VillageInvestCostGenerous = 5000,
                VillageInvestDurationGenerous = 20,
                VillageInvestHearthGenerous = 2f,
                VillageInvestRelationGenerous = 4,
                VillageInvestInfluenceGenerous = 6f,
                VillageInvestPowerGenerous = 8,
                VillageInvestCostGrand = 15000,
                VillageInvestDurationGrand = 30,
                VillageInvestHearthGrand = 3f,
                VillageInvestRelationGrand = 6,
                VillageInvestInfluenceGrand = 9f,
                VillageInvestPowerGrand = 12
            };
    }
}
