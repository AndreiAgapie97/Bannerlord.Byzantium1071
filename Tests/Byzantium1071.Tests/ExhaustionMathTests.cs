using Byzantium1071.Campaign;
using FsCheck.Xunit;
using Xunit;

namespace Byzantium1071.Tests
{
    public sealed class ExhaustionMathTests
    {
        [Fact]
        public void PressureBandHysteresisPreventsImmediateDownwardOscillation()
        {
            FakeSettings settings = DiplomacySettings();
            settings.PressureBandRisingStart = 35f;
            settings.PressureBandCrisisStart = 85f;
            settings.PressureBandHysteresis = 5f;

            Assert.Equal(DiplomacyPressureBand.Crisis,
                B1071_ExhaustionMath.EvaluatePressureBand(85f, DiplomacyPressureBand.Rising, settings));
            Assert.Equal(DiplomacyPressureBand.Crisis,
                B1071_ExhaustionMath.EvaluatePressureBand(80f, DiplomacyPressureBand.Crisis, settings));
            Assert.Equal(DiplomacyPressureBand.Rising,
                B1071_ExhaustionMath.EvaluatePressureBand(79.9f, DiplomacyPressureBand.Crisis, settings));
        }

        [Fact]
        public void LegacyBandMappingAndForcedPeaceThresholdUseTheirConfiguredWarCounts()
        {
            FakeSettings settings = DiplomacySettings();
            settings.DiplomacyNoNewWarThreshold = 65f;
            settings.DiplomacyPeacePressureThreshold = 45f;
            settings.DiplomacyForcedPeaceThreshold = 80f;
            settings.DiplomacyMajorWarPressureStartCount = 2;
            settings.DiplomacyForcedPeaceThresholdReductionPerMajorWar = 5f;

            Assert.Equal(DiplomacyPressureBand.Rising,
                B1071_ExhaustionMath.MapExhaustionToLegacyBand(45f, settings));
            Assert.Equal(DiplomacyPressureBand.Crisis,
                B1071_ExhaustionMath.MapExhaustionToLegacyBand(65f, settings));
            Assert.Equal(65f, B1071_ExhaustionMath.ForcedPeaceThreshold(4, settings));
        }

        [Fact]
        public void SupportAdjustmentsUseBandScalingAndTheirCaps()
        {
            FakeSettings settings = DiplomacySettings();
            settings.PeaceBiasBandLow = 1.5f;
            settings.PeaceBiasBandHigh = 3f;
            settings.DiplomacyWarSupportPenaltyPerPoint = 4f;
            settings.WarSupportPenaltyCap = -400f;
            settings.PeaceSupportBonusCap = 300f;

            Assert.Equal(400f, B1071_ExhaustionMath.WarSupportPenalty(
                DiplomacyPressureBand.Crisis, 200f, 0f, settings));
            Assert.Equal(300f, B1071_ExhaustionMath.PeaceSupportBonus(
                DiplomacyPressureBand.Crisis, 200f, 0f, settings));
        }

        [Fact]
        public void ManpowerAndEarlyWarBiasesScaleToTheirConfiguredEndpoints()
        {
            FakeSettings settings = DiplomacySettings();
            settings.EnableManpowerDiplomacyPressure = true;
            settings.ManpowerDiplomacyThresholdPercent = 40;
            settings.ManpowerDiplomacyPressureStrength = 100f;
            settings.MinWarDurationDaysBeforeForcedPeace = 40;
            settings.EarlyWarPeacePenaltyStrength = 300f;

            Assert.Equal(50f, B1071_ExhaustionMath.ManpowerDiplomacyPeaceBias(0.2f, settings));
            Assert.Equal(150f, B1071_ExhaustionMath.EarlyWarPeacePenalty(20f, false, settings));
        }

        [Fact]
        public void DailyDecayAndBattleExhaustionRespectZeroFloors()
        {
            Assert.Equal(6f, B1071_ExhaustionMath.DailyDecay(10f, 4f));
            Assert.Equal(0f, B1071_ExhaustionMath.DailyDecay(3f, 4f));
            Assert.Equal(3f, B1071_ExhaustionMath.DailyDecay(3f, -4f));
            Assert.Equal(2.5f, B1071_ExhaustionMath.BattleExhaustion(3, 2, 0.5f));
            Assert.Equal(0f, B1071_ExhaustionMath.BattleExhaustion(0, 0, 1f));
            Assert.Equal(0f, B1071_ExhaustionMath.BattleExhaustion(3, 2, -1f));
        }

        [Property(MaxTest = 1000)]
        public bool PressureBandAlwaysProducesADefinedValue(float exhaustion)
        {
            DiplomacyPressureBand band = B1071_ExhaustionMath.EvaluatePressureBand(
                exhaustion,
                DiplomacyPressureBand.Low,
                DiplomacySettings());

            return band == DiplomacyPressureBand.Low
                || band == DiplomacyPressureBand.Rising
                || band == DiplomacyPressureBand.Crisis;
        }

        private static FakeSettings DiplomacySettings() =>
            new()
            {
                PressureBandRisingStart = 35f,
                PressureBandCrisisStart = 85f,
                PressureBandHysteresis = 5f,
                PeaceBiasBandLow = 1.5f,
                PeaceBiasBandHigh = 3f,
                DiplomacyForcedPeaceThreshold = 80f,
                DiplomacyMajorWarPressureStartCount = 2,
                DiplomacyForcedPeaceThresholdReductionPerMajorWar = 5f,
                DiplomacyExtraPeaceBiasPerMajorWar = 20f,
                DiplomacyWarSupportPenaltyPerPoint = 4f,
                DiplomacyPeaceSupportBonusPerPoint = 5f,
                WarSupportPenaltyCap = -400f,
                PeaceSupportBonusCap = 300f,
                ManpowerDiplomacyThresholdPercent = 35,
                ManpowerDiplomacyPressureStrength = 100f,
                MinWarDurationDaysBeforeForcedPeace = 40,
                EarlyWarPeacePenaltyStrength = 300f
            };
    }
}
