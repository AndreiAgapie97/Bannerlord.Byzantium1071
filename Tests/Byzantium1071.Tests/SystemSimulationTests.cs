using System;
using System.Collections.Generic;
using Byzantium1071.Campaign;
using Xunit;

namespace Byzantium1071.Tests
{
    public sealed class SystemSimulationTests
    {
        [Fact]
        public void PeacefulTownConvergesToItsPoolMaximumAndStaysThereOverTwoHundredDays()
        {
            FakeSettings settings = TownSettings();
            int maximum = B1071_ManpowerMath.MaxPool(TownFacts(0), settings);
            int pool = 0;

            for (int day = 0; day < 200; day++)
            {
                DailyRegenResult regen = B1071_ManpowerMath.DailyRegen(TownFacts(pool), maximum, settings, new FakeRandom());
                pool = Math.Min(maximum, pool + regen.Amount);
                Assert.InRange(pool, 0, maximum);
            }

            Assert.Equal(maximum, pool);
        }

        [Fact]
        public void StressedTownNeverEscapesPoolBoundsAcrossTwoHundredDays()
        {
            FakeSettings settings = TownSettings();
            settings.SiegeRegenMultiplierPercent = 25f;
            settings.EnableDepletedEmergencyRegen = true;
            settings.DepletedRegenThresholdPercent = 20;
            settings.DepletedRegenBonusAtZero = 5;
            int maximum = B1071_ManpowerMath.MaxPool(TownFacts(0), settings);
            int pool = maximum;

            for (int day = 0; day < 200; day++)
            {
                pool = Math.Max(0, pool - 17);
                DailyRegenResult regen = B1071_ManpowerMath.DailyRegen(
                    new PoolFacts(
                        isTown: true,
                        isCastle: false,
                        hasTown: true,
                        prosperity: 1_000f,
                        security: 100f,
                        foodStocks: 100f,
                        loyalty: 100f,
                        isUnderSiege: day < 100,
                        ownerAtPeace: true,
                        currentPool: pool),
                    maximum,
                    settings,
                    new FakeRandom());
                pool = Math.Min(maximum, pool + regen.Amount);
                Assert.InRange(pool, 0, maximum);
                Assert.True(regen.Amount >= 0);
            }
        }

        [Fact]
        public void ExtremeButFiniteSettingsStayFiniteAndBoundedForTwoHundredDays()
        {
            FakeSettings settings = TownSettings();
            settings.TownPoolMax = 10_000;
            settings.ProsperityNormalizer = 0f;
            settings.MaxPoolProsperityMinScale = 1f;
            settings.MaxPoolProsperityMaxScale = 500f;
            settings.SecurityBonusMinScale = 1f;
            settings.SecurityBonusMaxScale = 500f;
            settings.TownRegenMinPercent = 0f;
            settings.TownRegenMaxPercent = 500f;
            settings.SecurityRegenMinScale = 0f;
            settings.SecurityRegenMaxScale = 500f;
            settings.FoodRegenMinScale = 0f;
            settings.FoodRegenMaxScale = 500f;
            settings.LoyaltyRegenMinScale = 0f;
            settings.LoyaltyRegenMaxScale = 500f;
            settings.EnableSeasonalRegen = true;
            settings.SpringSummerRegenMultiplier = 500;
            settings.WinterRegenMultiplier = 0;
            settings.EnablePeaceDividend = true;
            settings.PeaceDividendMultiplier = 1;
            settings.EnableWarExhaustion = true;
            settings.ExhaustionRegenDivisor = 0f;
            settings.EnableDelayedRecovery = true;
            settings.EnableRegenSoftCap = true;
            settings.RegenSoftCapStartRatio = -1f;
            settings.RegenSoftCapStrength = 10f;
            settings.EnableRecruitmentVariance = true;
            settings.RecoveryVariancePercent = 100;
            settings.RegenStressFloorPercent = 1f;
            settings.RegenCapPercent = 100f;
            settings.EnableDepletedEmergencyRegen = true;
            settings.DepletedRegenThresholdPercent = 100;
            settings.DepletedRegenBonusAtZero = 25;
            settings.PressureBandRisingStart = 1f;
            settings.PressureBandCrisisStart = 2f;
            settings.PressureBandHysteresis = 1f;

            PoolFacts maxFacts = new(
                isTown: true,
                isCastle: false,
                hasTown: true,
                prosperity: 2_000f,
                security: 200f,
                foodStocks: 200f,
                loyalty: 200f,
                ownerAtPeace: true,
                currentPool: 0);
            int maximum = B1071_ManpowerMath.MaxPool(maxFacts, settings);
            int pool = 0;
            float exhaustion = 100f;
            DiplomacyPressureBand band = DiplomacyPressureBand.Low;

            for (int day = 0; day < 200; day++)
            {
                float recovery = B1071_ManpowerMath.RecoveryPenaltyFraction(
                    basePenalty: 2f,
                    startDay: 0f,
                    expiryDay: 100f,
                    currentDay: day,
                    currentPool: pool,
                    maximumPool: maximum,
                    settings);
                PoolFacts facts = new(
                    isTown: true,
                    isCastle: false,
                    hasTown: true,
                    prosperity: day % 2 == 0 ? 2_000f : -100f,
                    security: day % 3 == 0 ? 200f : -100f,
                    foodStocks: day % 5 == 0 ? 200f : -100f,
                    loyalty: day % 7 == 0 ? 200f : -100f,
                    isUnderSiege: day % 11 == 0,
                    ownerAtPeace: day % 13 != 0,
                    season: (B1071Season)(day % 4),
                    exhaustion: exhaustion,
                    recoveryPenalty: recovery,
                    currentPool: pool);
                DailyRegenResult regen = B1071_ManpowerMath.DailyRegen(
                    facts, maximum, settings, new FakeRandom(floats: new[] { day % 2 == 0 ? 0f : 2f }));
                pool = Math.Min(maximum, Math.Max(0, pool + regen.Amount));
                exhaustion = B1071_ExhaustionMath.DailyDecay(
                    exhaustion + B1071_ExhaustionMath.BattleExhaustion(10, 5, 0.5f),
                    5f);
                band = B1071_ExhaustionMath.EvaluatePressureBand(exhaustion, band, settings);

                Assert.InRange(pool, 0, maximum);
                Assert.InRange(exhaustion, 0f, 1_000f);
                Assert.NotEqual(DiplomacyPressureBand.Low, band);
                AssertFinite(recovery, regen.BasePercent, regen.FinalPercent, regen.SecurityMultiplier,
                    regen.FoodMultiplier, regen.LoyaltyMultiplier, regen.SiegeMultiplier,
                    regen.SeasonalMultiplier, regen.PeaceMultiplier, regen.GovernorAdd,
                    regen.ExhaustionMultiplier, regen.RecoveryMultiplier, regen.SoftCapMultiplier,
                    regen.VarianceMultiplier);
            }
        }

        [Fact]
        public void SiegeConquestAndRecoveryPreserveDepletedPoolsThenRestoreThem()
        {
            FakeSettings settings = TownSettings();
            settings.ConquestPoolRetainPercent = 50;
            settings.EnableDynamicConquestProtection = true;
            settings.ConquestDepletedThresholdPercent = 25;
            settings.ConquestDepletedRetainPercent = 80;
            int maximum = B1071_ManpowerMath.MaxPool(TownFacts(0), settings);
            int pool = 200;

            PoolRetentionResult siege = B1071_ManpowerMath.SiegeRetention(pool, maximum, 50f);
            pool = siege.AppliedPool;
            PoolRetentionResult conquest = B1071_ManpowerMath.ConquestRetention(pool, maximum, settings);
            pool = conquest.AppliedPool;

            Assert.Equal(200, siege.AppliedPool);
            Assert.Equal(0.56f, conquest.RetainFraction, 5);
            Assert.Equal(112, pool);

            for (int day = 0; day < 200; day++)
            {
                DailyRegenResult regen = B1071_ManpowerMath.DailyRegen(
                    TownFacts(pool), maximum, settings, new FakeRandom());
                pool = Math.Min(maximum, pool + regen.Amount);
                Assert.InRange(pool, 0, maximum);
            }

            Assert.Equal(maximum, pool);
        }

        [Fact]
        public void LongWarReachesCrisisAndCooldownKeepsForcedPeaceEventsApart()
        {
            FakeSettings settings = new()
            {
                PressureBandRisingStart = 35f,
                PressureBandCrisisStart = 85f,
                PressureBandHysteresis = 5f,
                DiplomacyForcedPeaceThreshold = 80f,
                DiplomacyMajorWarPressureStartCount = 2,
                DiplomacyForcedPeaceThresholdReductionPerMajorWar = 5f
            };
            const int cooldownDays = 30;
            const int activeMajorWars = 4;
            float exhaustion = 0f;
            DiplomacyPressureBand band = DiplomacyPressureBand.Low;
            float threshold = B1071_ExhaustionMath.ForcedPeaceThreshold(activeMajorWars, settings);
            var forcedPeaceDays = new List<int>();

            for (int day = 0; day < 300; day++)
            {
                exhaustion = day < 150 ? Math.Min(100f, exhaustion + 1f) : Math.Max(0f, exhaustion - 1f);
                band = B1071_ExhaustionMath.EvaluatePressureBand(exhaustion, band, settings);

                bool cooldownExpired = forcedPeaceDays.Count == 0
                    || day - forcedPeaceDays[forcedPeaceDays.Count - 1] >= cooldownDays;
                if (exhaustion >= threshold && cooldownExpired)
                {
                    forcedPeaceDays.Add(day);
                }
            }

            Assert.Equal(DiplomacyPressureBand.Low, band);
            Assert.NotEmpty(forcedPeaceDays);
            Assert.Equal((int)threshold - 1, forcedPeaceDays[0]);
            for (int index = 1; index < forcedPeaceDays.Count; index++)
            {
                Assert.True(forcedPeaceDays[index] - forcedPeaceDays[index - 1] >= cooldownDays);
            }
        }

        [Fact]
        public void ServiceLifecycleAppliesThreeExtensionsThenReturnsTheWholeCohort()
        {
            FakeSettings settings = new()
            {
                DemobilizationIntensityPreset = 3,
                DemobilizationT1ServiceDays = 10,
                DemobilizationExtensionDays = 5,
                DemobilizationExtensionGoldPerTierDay = 2,
                DemobilizationMaxExtensions = 3
            };
            const int soldiers = 12;
            int threshold = B1071_ServiceMath.ServiceThresholdDays(1, B1071Season.Autumn, false, settings);
            int day = threshold;
            int totalGold = 0;

            for (int extension = 0; extension < B1071_ServiceMath.MaxExtensions(settings); extension++)
            {
                totalGold += B1071_ServiceMath.ExtensionCost(1, soldiers, extension, settings);
                day += settings.DemobilizationExtensionDays;
            }

            int returned = B1071_ServiceMath.VeteranReturnCount(soldiers, 100, new FakeRandom());
            Assert.Equal(25, day);
            Assert.Equal(540, totalGold);
            Assert.Equal(soldiers, returned);
        }

        [Fact]
        public void SlaveEconomyStaysWithinCapacityAndKeepsFractionalDecayStableForAYear()
        {
            FakeSettings settings = new()
            {
                SlaveCapMinimum = 10,
                SlaveCapPerProsperity = 0.02f,
                SlaveDailyDecayPercent = 0.5f,
                SlaveFoodConsumptionPerUnit = 0.05f
            };
            int capacity = B1071_SlaveMath.SlaveCap(5_000f, settings);
            int slaves = capacity;
            float accumulator = 0f;
            float foodDraw = 0f;

            for (int day = 0; day < 365; day++)
            {
                if (day % 30 == 0)
                {
                    slaves = Math.Min(capacity, slaves + 25);
                }

                SlaveDecayResult decay = B1071_SlaveMath.DailyDecay(slaves, accumulator, settings);
                slaves -= decay.WholeLoss;
                accumulator = decay.RemainingAccumulator;
                foodDraw += B1071_SlaveMath.FoodConsumption(slaves, settings);

                Assert.InRange(slaves, 0, capacity);
                Assert.InRange(accumulator, 0f, 1f);
            }

            Assert.True(foodDraw > 0f);
        }

        [Fact]
        public void GovernanceAndDevastationRecoverToZeroAfterAProlongedCrisis()
        {
            FakeSettings settings = new()
            {
                GovernanceStrainCap = 100f,
                DevastationPerRaid = 20f,
                DevastationDecayPerDay = 0.5f
            };
            float strain = 0f;
            float devastation = 0f;

            for (int day = 0; day < 300; day++)
            {
                if (day < 50 && day % 5 == 0)
                {
                    strain = B1071_GovernanceMath.AddStrain(strain, 18f, settings);
                    devastation = B1071_GovernanceMath.AddDevastation(devastation, settings);
                }

                if (day == 50)
                {
                    strain = B1071_GovernanceMath.ReduceStrain(strain, 50f);
                }

                strain = B1071_GovernanceMath.DailyStrain(strain, 1f, 0.5f);
                devastation = B1071_GovernanceMath.DailyDevastation(devastation, settings);
                Assert.InRange(strain, 0f, settings.GovernanceStrainCap);
                Assert.InRange(devastation, 0f, 100f);
            }

            Assert.Equal(0f, strain);
            Assert.Equal(0f, devastation);
        }

        private static PoolFacts TownFacts(int currentPool) => new(
            isTown: true,
            isCastle: false,
            hasTown: true,
            prosperity: 1_000f,
            security: 100f,
            foodStocks: 100f,
            loyalty: 100f,
            ownerAtPeace: true,
            currentPool: currentPool);

        private static FakeSettings TownSettings() => new()
        {
            TownPoolMax = 1_000,
            CastlePoolMax = 400,
            OtherPoolMax = 100,
            ProsperityNormalizer = 1_000f,
            MaxPoolProsperityMinScale = 100f,
            MaxPoolProsperityMaxScale = 100f,
            SecurityBonusMinScale = 100f,
            SecurityBonusMaxScale = 100f,
            TownRegenMinPercent = 10f,
            TownRegenMaxPercent = 10f,
            CastleRegenMinPercent = 10f,
            CastleRegenMaxPercent = 10f,
            OtherRegenPercent = 10f,
            HearthNormalizer = 1,
            SecurityRegenMinScale = 100f,
            SecurityRegenMaxScale = 100f,
            FoodStocksNormalizer = 1f,
            FoodRegenMinScale = 100f,
            FoodRegenMaxScale = 100f,
            LoyaltyRegenMinScale = 100f,
            LoyaltyRegenMaxScale = 100f,
            SiegeRegenMultiplierPercent = 100f,
            RegenCapPercent = 100f
        };

        private static void AssertFinite(params float[] values)
        {
            foreach (float value in values)
            {
                Assert.False(float.IsNaN(value));
                Assert.False(float.IsInfinity(value));
            }
        }
    }
}
