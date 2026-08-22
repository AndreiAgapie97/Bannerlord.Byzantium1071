using Byzantium1071.Campaign;
using FsCheck.Xunit;
using Xunit;

namespace Byzantium1071.Tests
{
    public sealed class GovernanceMathTests
    {
        [Fact]
        public void StrainClampsOnAddAndReturnsToZeroOnReductionOrDecay()
        {
            FakeSettings settings = GovernanceSettings();
            settings.GovernanceStrainCap = 100f;

            Assert.Equal(100f, B1071_GovernanceMath.AddStrain(95f, 10f, settings));
            Assert.Equal(0f, B1071_GovernanceMath.ReduceStrain(5f, 8f));
            Assert.Equal(0f, B1071_GovernanceMath.DailyStrain(1f, 0.5f, 0.75f));
        }

        [Fact]
        public void GovernancePenaltiesScaleWithStrainAndConfiguredCap()
        {
            FakeSettings settings = GovernanceSettings();
            settings.GovernanceStrainCap = 100f;

            Assert.Equal(-1.5f, B1071_GovernanceMath.GovernancePenalty(50f, 3f, settings));
            settings.GovernanceStrainCap = 0f;
            Assert.Equal(-3f, B1071_GovernanceMath.GovernancePenalty(1f, 3f, settings));
        }

        [Fact]
        public void StabilizationTiersAndAiChoiceUseTheirConfiguredThresholds()
        {
            FakeSettings settings = GovernanceSettings();
            StabilizationTierValues tier = B1071_GovernanceMath.StabilizationTier(2, settings);

            Assert.Equal(5000, tier.Cost);
            Assert.Equal(20, tier.Duration);
            Assert.Equal(25f, tier.StrainReduction);
            Assert.Equal(3, B1071_GovernanceMath.AiStabilizationTier(10001, settings));
            Assert.Equal(2, B1071_GovernanceMath.AiStabilizationTier(5001, settings));
            Assert.Equal(0, B1071_GovernanceMath.AiStabilizationTier(1000, settings));
            Assert.Equal(0, B1071_GovernanceMath.StabilizationTier(4, settings).Cost);
        }

        [Fact]
        public void DevastationUsesCappedAccumulationAndProportionalPenalties()
        {
            FakeSettings settings = GovernanceSettings();
            settings.DevastationPerRaid = 25f;
            settings.DevastationDecayPerDay = 0.5f;
            settings.DevastationMaxFoodPenaltyPerVillage = 10f;

            Assert.Equal(100f, B1071_GovernanceMath.AddDevastation(90f, settings));
            Assert.Equal(0f, B1071_GovernanceMath.DailyDevastation(0.25f, settings));
            Assert.Equal(-1.5f, B1071_GovernanceMath.DevastationPenalty(75f, 2f));
            Assert.Equal(7.5f, B1071_GovernanceMath.DevastationFoodPenalty(75f, settings));
        }

        [Property(MaxTest = 1000)]
        public bool CappedStateNeverEscapesItsBounds(float current, float amount)
        {
            if (float.IsNaN(current) || float.IsInfinity(current)
                || float.IsNaN(amount) || float.IsInfinity(amount))
            {
                return true;
            }

            FakeSettings settings = GovernanceSettings();
            settings.GovernanceStrainCap = 100f;
            settings.DevastationPerRaid = amount;
            float strain = B1071_GovernanceMath.AddStrain(current, amount, settings);
            float devastation = B1071_GovernanceMath.AddDevastation(current, settings);
            return strain <= 100f && devastation <= 100f;
        }

        private static FakeSettings GovernanceSettings() =>
            new()
            {
                GovernanceStrainCap = 100f,
                GovernanceStabilizationAiGoldMultiplier = 1,
                GovernanceStabilizationCostDonative = 1000,
                GovernanceStabilizationDurationDonative = 10,
                GovernanceStabilizationStrainDonative = 10f,
                GovernanceStabilizationLoyaltyDonative = 1f,
                GovernanceStabilizationSecurityDonative = 1f,
                GovernanceStabilizationDecayDonative = 0.1f,
                GovernanceStabilizationCostElites = 5000,
                GovernanceStabilizationDurationElites = 20,
                GovernanceStabilizationStrainElites = 25f,
                GovernanceStabilizationLoyaltyElites = 2f,
                GovernanceStabilizationSecurityElites = 2f,
                GovernanceStabilizationDecayElites = 0.2f,
                GovernanceStabilizationCostAmnesty = 10000,
                GovernanceStabilizationDurationAmnesty = 30,
                GovernanceStabilizationStrainAmnesty = 50f,
                GovernanceStabilizationLoyaltyAmnesty = 3f,
                GovernanceStabilizationSecurityAmnesty = 3f,
                GovernanceStabilizationDecayAmnesty = 0.3f
            };
    }
}
