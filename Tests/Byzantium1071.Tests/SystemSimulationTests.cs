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
    }
}
