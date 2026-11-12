using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Byzantium1071.Campaign;
using Byzantium1071.Campaign.Settings;
using Xunit;

namespace Byzantium1071.Tests
{
    /// <summary>
    /// Drives every extracted formula with deliberately hostile settings — all zero, all negative,
    /// every knob at the extreme of its type — and checks the two things a player actually cares
    /// about: the game does not crash, and no number comes back as "NaN" or a negative pool.
    ///
    /// MCM ranges normally prevent these values, but a corrupt settings file, a profile migration
    /// bug or a future range change can all deliver them, and the formulas run every campaign day.
    /// </summary>
    public sealed class MathHostileInputTests
    {
        // The settings variants ───────────────────────────────────────────────

        /// <summary>
        /// Every numeric property of <see cref="IB1071Settings"/> set to the same hostile value,
        /// with the booleans forced one way. Built by reflection so a new setting is swept
        /// automatically rather than being quietly missed.
        /// </summary>
        private static FakeSettings Uniform(double numericValue, bool booleanValue)
        {
            FakeSettings settings = new();
            foreach (PropertyInfo property in typeof(FakeSettings).GetProperties())
            {
                if (!property.CanWrite) continue;

                if (property.PropertyType == typeof(bool))
                {
                    property.SetValue(settings, booleanValue);
                }
                else if (property.PropertyType == typeof(int))
                {
                    property.SetValue(settings, ToInt(numericValue));
                }
                else if (property.PropertyType == typeof(float))
                {
                    property.SetValue(settings, (float)numericValue);
                }
            }

            return settings;
        }

        private static int ToInt(double value)
        {
            if (double.IsNaN(value)) return 0;
            if (value >= int.MaxValue) return int.MaxValue;
            if (value <= int.MinValue) return int.MinValue;
            return (int)value;
        }

        /// <summary>
        /// Absurd values no settings file should ever hold. Only the "does not crash" contract
        /// applies here — a formula is free to return nonsense, but never to take the campaign down.
        /// </summary>
        public static IEnumerable<object[]> AbsurdSettings()
        {
            double[] values =
            {
                0d, 1d, -1d, -100d, int.MaxValue, int.MinValue, 1e30d, -1e30d,
                double.NaN, double.PositiveInfinity, double.NegativeInfinity
            };

            foreach (double value in values)
            {
                yield return new object[] { value, true };
                yield return new object[] { value, false };
            }
        }

        /// <summary>
        /// Every knob pushed to one end of its own MCM slider at once. Unlike the absurd values
        /// above, a player can produce every one of these in the settings menu, so the results have
        /// to be sane and not merely non-fatal.
        /// </summary>
        public static IEnumerable<object[]> ReachableSettings()
        {
            foreach (string bound in new[] { "Minimum", "Maximum", "Default" })
            {
                yield return new object[] { bound, true };
                yield return new object[] { bound, false };
            }
        }

        /// <summary>
        /// Builds the settings a player would have after dragging every slider to one end.
        /// Settings with no slider keep the value the mod ships.
        /// </summary>
        private static FakeSettings AtDeclaredBound(string bound, bool booleanValue)
        {
            FakeSettings settings = new();
            Dictionary<string, PropertyInfo> properties = typeof(FakeSettings)
                .GetProperties()
                .Where(property => property.CanWrite)
                .ToDictionary(property => property.Name, StringComparer.Ordinal);

            foreach (PropertyInfo property in properties.Values)
            {
                if (property.PropertyType == typeof(bool))
                {
                    property.SetValue(settings, booleanValue);
                }
            }

            foreach (DeclaredSettingRange range in DeclaredSettingRanges.All)
            {
                if (!properties.TryGetValue(range.Name, out PropertyInfo? property)) continue;

                float value = bound switch
                {
                    "Minimum" => range.Minimum,
                    "Maximum" => range.Maximum,
                    _ => range.DefaultValue
                };

                if (property.PropertyType == typeof(int))
                {
                    property.SetValue(settings, (int)value);
                }
                else if (property.PropertyType == typeof(float))
                {
                    property.SetValue(settings, value);
                }
            }

            return settings;
        }

        // The call battery ────────────────────────────────────────────────────

        /// <summary>
        /// Every number a run of the battery produced, tagged with the formula that produced it,
        /// so a failure names the culprit instead of just saying "something returned NaN".
        /// </summary>
        private sealed class Results
        {
            private readonly List<(string Name, double Value)> _values = new();

            internal void Add(string name, double value) => _values.Add((name, value));

            internal void AssertAllFinite()
            {
                (string Name, double Value)[] bad = _values
                    .Where(entry => double.IsNaN(entry.Value) || double.IsInfinity(entry.Value))
                    .ToArray();

                Assert.True(
                    bad.Length == 0,
                    "Finite settings produced non-finite results: "
                    + string.Join(", ", bad.Select(entry => $"{entry.Name}={entry.Value}")));
            }

            internal void AssertNotNegative(params string[] names)
            {
                foreach (string name in names)
                {
                    foreach ((string Name, double Value) entry in _values.Where(e => e.Name == name))
                    {
                        Assert.True(entry.Value >= 0d, $"{name} returned {entry.Value}, which is below zero.");
                    }
                }
            }

            internal double Single(string name) => _values.Single(entry => entry.Name == name).Value;
        }

        /// <summary>
        /// Calls one representative case of every formula that takes settings. Inputs are ordinary
        /// campaign values; the hostility comes entirely from the settings under test.
        /// </summary>
        private static Results RunBattery(IB1071Settings settings)
        {
            Results results = new();
            PermissiveRandom random = new();

            PoolFacts town = new(
                isTown: true, isCastle: false, hasTown: true,
                prosperity: 4000f, security: 50f,
                villageHearths: new[] { 300f, 450f, 900f },
                governorLeadership: 120, foodStocks: 200f, loyalty: 60f,
                isUnderSiege: false, ownerAtPeace: true, governorSteward: 80,
                season: B1071Season.Autumn, exhaustion: 30f, recoveryPenalty: 0.2f,
                currentPool: 250);

            PoolFacts castle = new(
                isTown: false, isCastle: true, hasTown: false,
                prosperity: 0f, security: 0f,
                villageHearths: Array.Empty<float>(),
                currentPool: 0);

            // Manpower
            int townMax = B1071_ManpowerMath.MaxPool(town, settings);
            int castleMax = B1071_ManpowerMath.MaxPool(castle, settings);
            results.Add(nameof(B1071_ManpowerMath.MaxPool), townMax);
            results.Add("MaxPool.Castle", castleMax);

            DailyRegenResult townRegen = B1071_ManpowerMath.DailyRegen(town, townMax, settings, random);
            DailyRegenResult castleRegen = B1071_ManpowerMath.DailyRegen(castle, castleMax, settings, random);
            results.Add(nameof(B1071_ManpowerMath.DailyRegen), townRegen.Amount);
            results.Add("DailyRegen.Castle", castleRegen.Amount);

            results.Add(nameof(B1071_ManpowerMath.RecoveryPenaltyFraction),
                B1071_ManpowerMath.RecoveryPenaltyFraction(0.5f, 10f, 40f, 25f, 100, 500, settings));
            results.Add(nameof(B1071_ManpowerMath.RaidDrainAmount),
                B1071_ManpowerMath.RaidDrainAmount(400, 800, 15f, 30, 0));
            results.Add(nameof(B1071_ManpowerMath.SiegeRetention),
                B1071_ManpowerMath.SiegeRetention(400, 800, 40f).AppliedPool);
            results.Add(nameof(B1071_ManpowerMath.ConquestRetention),
                B1071_ManpowerMath.ConquestRetention(400, 800, settings).AppliedPool);
            results.Add(nameof(B1071_ManpowerMath.CultureDiscountedRecruitmentCost),
                B1071_ManpowerMath.CultureDiscountedRecruitmentCost(50, true, settings));
            results.Add(nameof(B1071_ManpowerMath.RecruitmentCostPerTroop),
                B1071_ManpowerMath.RecruitmentCostPerTroop(settings));
            results.Add(nameof(B1071_ManpowerMath.PoolBand), B1071_ManpowerMath.PoolBand(250, townMax));
            results.Add(nameof(B1071_ManpowerMath.TierWeightedDrain),
                B1071_ManpowerMath.TierWeightedDrain(20, 4, 1.5f));

            // Service and demobilisation
            results.Add(nameof(B1071_ServiceMath.BaseServiceDays), B1071_ServiceMath.BaseServiceDays(3, settings));
            results.Add(nameof(B1071_ServiceMath.ServiceThresholdDays),
                B1071_ServiceMath.ServiceThresholdDays(3, B1071Season.Winter, true, settings));
            results.Add(nameof(B1071_ServiceMath.ExtensionCost),
                B1071_ServiceMath.ExtensionCost(6, 500, 2, settings));
            results.Add(nameof(B1071_ServiceMath.MaxExtensions), B1071_ServiceMath.MaxExtensions(settings));
            results.Add(nameof(B1071_ServiceMath.RecallGoldCost), B1071_ServiceMath.RecallGoldCost(4, 30, settings));
            results.Add(nameof(B1071_ServiceMath.CourierSpeedPerDay), B1071_ServiceMath.CourierSpeedPerDay(settings));
            results.Add(nameof(B1071_ServiceMath.MarchSpeedPerDay), B1071_ServiceMath.MarchSpeedPerDay(settings));
            results.Add(nameof(B1071_ServiceMath.EstimateRecallDays),
                B1071_ServiceMath.EstimateRecallDays(120f, settings));
            results.Add(nameof(B1071_ServiceMath.EstimateArrivalDays),
                B1071_ServiceMath.EstimateArrivalDays(40f, 80f, settings));
            results.Add(nameof(B1071_ServiceMath.DailyRetirementCap),
                B1071_ServiceMath.DailyRetirementCap(200, 10));

            // Castle recruitment
            results.Add(nameof(B1071_CastlePoolMath.PoolCapacity),
                B1071_CastlePoolMath.PoolCapacity(3000f, 3, settings));
            results.Add(nameof(B1071_CastlePoolMath.DailyRegenCount),
                B1071_CastlePoolMath.DailyRegenCount(3000f, 200, 50, settings));
            results.Add(nameof(B1071_CastlePoolMath.AiBufferedAffordableCount),
                B1071_CastlePoolMath.AiBufferedAffordableCount(50000, 120, 40, settings));
            results.Add(nameof(B1071_CastlePoolMath.RequiredPrisonerDays),
                B1071_CastlePoolMath.RequiredPrisonerDays(4, settings));
            results.Add(nameof(B1071_CastlePoolMath.GoldCostForTier),
                B1071_CastlePoolMath.GoldCostForTier(4, settings));

            // Exhaustion and diplomacy
            DiplomacyPressureBand band = B1071_ExhaustionMath.EvaluatePressureBand(
                60f, DiplomacyPressureBand.Rising, settings);
            results.Add("EvaluatePressureBand", (int)band);
            results.Add(nameof(B1071_ExhaustionMath.BandPeaceBias), B1071_ExhaustionMath.BandPeaceBias(band, settings));
            results.Add(nameof(B1071_ExhaustionMath.ForcedPeaceThreshold),
                B1071_ExhaustionMath.ForcedPeaceThreshold(3, settings));
            results.Add(nameof(B1071_ExhaustionMath.MajorWarPressureBias),
                B1071_ExhaustionMath.MajorWarPressureBias(3, settings));
            results.Add(nameof(B1071_ExhaustionMath.ManpowerDiplomacyPeaceBias),
                B1071_ExhaustionMath.ManpowerDiplomacyPeaceBias(0.25f, settings));
            results.Add(nameof(B1071_ExhaustionMath.EarlyWarPeacePenalty),
                B1071_ExhaustionMath.EarlyWarPeacePenalty(10f, true, settings));
            results.Add(nameof(B1071_ExhaustionMath.WarSupportPenalty),
                B1071_ExhaustionMath.WarSupportPenalty(band, 60f, 0.3f, settings));
            results.Add(nameof(B1071_ExhaustionMath.PeaceSupportBonus),
                B1071_ExhaustionMath.PeaceSupportBonus(band, 60f, 0.3f, settings));

            // Slave economy
            results.Add(nameof(B1071_SlaveMath.SlaveCap), B1071_SlaveMath.SlaveCap(3000f, settings));
            results.Add(nameof(B1071_SlaveMath.RaidHearthDivisor), B1071_SlaveMath.RaidHearthDivisor(settings));
            results.Add(nameof(B1071_SlaveMath.RaidSlaveCount), B1071_SlaveMath.RaidSlaveCount(600f, settings));
            SlaveDecayResult decay = B1071_SlaveMath.DailyDecay(500, 0.4f, settings);
            results.Add("DailyDecay.WholeLoss", decay.WholeLoss);
            results.Add("DailyDecay.RemainingAccumulator", decay.RemainingAccumulator);
            results.Add(nameof(B1071_SlaveMath.FoodConsumption), B1071_SlaveMath.FoodConsumption(500, settings));
            results.Add(nameof(B1071_SlaveMath.ConstructionBonus), B1071_SlaveMath.ConstructionBonus(500, settings));
            results.Add(nameof(B1071_SlaveMath.ProsperityBonus), B1071_SlaveMath.ProsperityBonus(500, settings));

            // Governance and devastation
            results.Add(nameof(B1071_GovernanceMath.AddStrain), B1071_GovernanceMath.AddStrain(20f, 5f, settings));
            results.Add(nameof(B1071_GovernanceMath.ReduceStrain), B1071_GovernanceMath.ReduceStrain(20f, 5f));
            results.Add(nameof(B1071_GovernanceMath.DailyStrain), B1071_GovernanceMath.DailyStrain(20f, 1f, 2f));
            results.Add(nameof(B1071_GovernanceMath.GovernancePenalty),
                B1071_GovernanceMath.GovernancePenalty(20f, 10f, settings));
            results.Add(nameof(B1071_GovernanceMath.AiStabilizationTier),
                B1071_GovernanceMath.AiStabilizationTier(50000, settings));
            results.Add(nameof(B1071_GovernanceMath.AddDevastation), B1071_GovernanceMath.AddDevastation(30f, settings));
            results.Add(nameof(B1071_GovernanceMath.DailyDevastation),
                B1071_GovernanceMath.DailyDevastation(30f, settings));
            results.Add(nameof(B1071_GovernanceMath.DevastationFoodPenalty),
                B1071_GovernanceMath.DevastationFoodPenalty(30f, settings));

            // Investment
            InvestmentTierValues townTier = B1071_InvestmentMath.TownTier(2, settings);
            results.Add("TownTier.Cost", townTier.Cost);
            results.Add("TownTier.Duration", townTier.Duration);
            results.Add("TownTier.Bonus", townTier.Bonus);
            InvestmentTierValues villageTier = B1071_InvestmentMath.VillageTier(2, settings);
            results.Add("VillageTier.Cost", villageTier.Cost);
            results.Add("VillageTier.Bonus", villageTier.Bonus);

            return results;
        }

        // The assertions ──────────────────────────────────────────────────────

        [Fact]
        public void TheDeclaredRangeReaderActuallyFindsTheSettings()
        {
            int withSliders = DeclaredSettingRanges.All.Count(range => range.Minimum < range.Maximum);
            string[] unknown = DeclaredSettingRanges.All
                .Select(range => range.Name)
                .Where(name => typeof(FakeSettings).GetProperty(name) is null)
                .ToArray();

            Assert.True(
                withSliders >= 260,
                $"Only {withSliders} settings came back with a declared slider range; the reader is likely broken.");
            Assert.True(
                unknown.Length == 0,
                $"The settings source declares properties the fake does not have: {string.Join(", ", unknown)}.");
        }

        [Fact]
        public void TheThreeSliderPositionsReallyProduceDifferentCampaigns()
        {
            double atMinimum = RunBattery(AtDeclaredBound("Minimum", true)).Single(nameof(B1071_ManpowerMath.MaxPool));
            double atDefault = RunBattery(AtDeclaredBound("Default", true)).Single(nameof(B1071_ManpowerMath.MaxPool));
            double atMaximum = RunBattery(AtDeclaredBound("Maximum", true)).Single(nameof(B1071_ManpowerMath.MaxPool));

            Assert.True(
                atMinimum < atDefault && atDefault < atMaximum,
                $"The sweep is not varying the settings: pools came out {atMinimum} / {atDefault} / {atMaximum}.");
        }

        [Theory]
        [MemberData(nameof(AbsurdSettings))]
        public void NoFormulaThrowsWhateverTheSettingsSay(double numericValue, bool booleanValue)
        {
            IB1071Settings settings = Uniform(numericValue, booleanValue);

            Exception? failure = Record.Exception(() => RunBattery(settings));

            Assert.True(
                failure is null,
                $"Settings uniformly set to {numericValue} (booleans {booleanValue}) threw {failure?.GetType().Name}: "
                + failure?.Message);
        }

        [Theory]
        [MemberData(nameof(ReachableSettings))]
        public void NoFormulaThrowsAtEitherEndOfEverySlider(string bound, bool booleanValue)
        {
            Exception? failure = Record.Exception(() => RunBattery(AtDeclaredBound(bound, booleanValue)));

            Assert.True(
                failure is null,
                $"Every slider at its {bound} (booleans {booleanValue}) threw {failure?.GetType().Name}: "
                + failure?.Message);
        }

        [Theory]
        [MemberData(nameof(ReachableSettings))]
        public void ReachableSettingsNeverProduceNaNOrInfinity(string bound, bool booleanValue)
        {
            RunBattery(AtDeclaredBound(bound, booleanValue)).AssertAllFinite();
        }

        [Theory]
        [MemberData(nameof(ReachableSettings))]
        public void QuantitiesThatCannotBeNegativeNeverAre(string bound, bool booleanValue)
        {
            Results results = RunBattery(AtDeclaredBound(bound, booleanValue));

            results.AssertNotNegative(
                nameof(B1071_ManpowerMath.MaxPool),
                "MaxPool.Castle",
                nameof(B1071_ManpowerMath.DailyRegen),
                "DailyRegen.Castle",
                nameof(B1071_ManpowerMath.RaidDrainAmount),
                nameof(B1071_ManpowerMath.SiegeRetention),
                nameof(B1071_ManpowerMath.ConquestRetention),
                nameof(B1071_ManpowerMath.CultureDiscountedRecruitmentCost),
                nameof(B1071_ManpowerMath.RecruitmentCostPerTroop),
                nameof(B1071_ServiceMath.ExtensionCost),
                nameof(B1071_ServiceMath.RecallGoldCost),
                nameof(B1071_ServiceMath.DailyRetirementCap),
                nameof(B1071_CastlePoolMath.AiBufferedAffordableCount),
                nameof(B1071_SlaveMath.RaidSlaveCount),
                nameof(B1071_SlaveMath.SlaveCap),
                nameof(B1071_SlaveMath.FoodConsumption),
                "DailyDecay.WholeLoss",
                "TownTier.Cost",
                "VillageTier.Cost",
                nameof(B1071_ExhaustionMath.MajorWarPressureBias),
                nameof(B1071_GovernanceMath.ReduceStrain),
                nameof(B1071_GovernanceMath.DailyStrain),
                nameof(B1071_GovernanceMath.DailyDevastation));
        }

        [Theory]
        [MemberData(nameof(ReachableSettings))]
        public void AnyPoolAlwaysHasRoomForAtLeastOneSoldier(string bound, bool booleanValue)
        {
            Results results = RunBattery(AtDeclaredBound(bound, booleanValue));

            Assert.True(results.Single(nameof(B1071_ManpowerMath.MaxPool)) >= 1d);
            Assert.True(results.Single("MaxPool.Castle") >= 1d);
        }

        [Theory]
        [MemberData(nameof(ReachableSettings))]
        public void DailyRegenNeverExceedsThePoolItFills(string bound, bool booleanValue)
        {
            Results results = RunBattery(AtDeclaredBound(bound, booleanValue));

            Assert.True(
                results.Single(nameof(B1071_ManpowerMath.DailyRegen))
                    <= results.Single(nameof(B1071_ManpowerMath.MaxPool)),
                "A single day of regeneration filled more than the whole town pool.");
            Assert.True(
                results.Single("DailyRegen.Castle") <= results.Single("MaxPool.Castle"),
                "A single day of regeneration filled more than the whole castle pool.");
        }

        [Theory]
        [MemberData(nameof(ReachableSettings))]
        public void ServiceLengthsAndTravelTimesStayAtLeastOneDay(string bound, bool booleanValue)
        {
            Results results = RunBattery(AtDeclaredBound(bound, booleanValue));

            Assert.True(results.Single(nameof(B1071_ServiceMath.BaseServiceDays)) >= 1d);
            Assert.True(results.Single(nameof(B1071_ServiceMath.ServiceThresholdDays)) >= 1d);
            Assert.True(results.Single(nameof(B1071_ServiceMath.CourierSpeedPerDay)) >= 1d);
            Assert.True(results.Single(nameof(B1071_ServiceMath.MarchSpeedPerDay)) >= 1d);
            Assert.True(results.Single(nameof(B1071_ServiceMath.EstimateRecallDays)) >= 1d);
        }

        [Theory]
        [MemberData(nameof(ReachableSettings))]
        public void RetainedManpowerNeverExceedsWhatWasThereBefore(string bound, bool booleanValue)
        {
            Results results = RunBattery(AtDeclaredBound(bound, booleanValue));

            Assert.True(results.Single(nameof(B1071_ManpowerMath.SiegeRetention)) <= 400d);
            Assert.True(results.Single(nameof(B1071_ManpowerMath.ConquestRetention)) <= 400d);
            Assert.True(results.Single(nameof(B1071_ManpowerMath.RaidDrainAmount)) <= 400d);
        }

        [Theory]
        [MemberData(nameof(AbsurdSettings))]
        public void PressureBandIsAlwaysOneOfTheThreeDefinedLevels(double numericValue, bool booleanValue)
        {
            IB1071Settings settings = Uniform(numericValue, booleanValue);

            foreach (DiplomacyPressureBand current in new[]
                     {
                         DiplomacyPressureBand.Low, DiplomacyPressureBand.Rising, DiplomacyPressureBand.Crisis
                     })
            {
                foreach (float exhaustion in new[] { 0f, 1f, 50f, 100f, -100f, float.MaxValue })
                {
                    DiplomacyPressureBand band =
                        B1071_ExhaustionMath.EvaluatePressureBand(exhaustion, current, settings);

                    Assert.True(
                        band is DiplomacyPressureBand.Low or DiplomacyPressureBand.Rising
                            or DiplomacyPressureBand.Crisis,
                        $"Band resolved to the undefined value {(int)band}.");
                }
            }
        }

        // Hostile inputs rather than hostile settings ──────────────────────────

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        [InlineData(float.MaxValue)]
        [InlineData(float.MinValue)]
        [InlineData(-1f)]
        [InlineData(0f)]
        public void PoolSizingSurvivesAProsperityValueTheGameShouldNeverProduce(float prosperity)
        {
            FakeSettings settings = RealisticSettings();
            PoolFacts facts = new(
                isTown: true, isCastle: false, hasTown: true,
                prosperity: prosperity, security: 50f,
                villageHearths: new[] { prosperity, 300f });

            int max = B1071_ManpowerMath.MaxPool(facts, settings);

            Assert.True(max >= 1, $"Prosperity {prosperity} produced a pool of {max}.");
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        public void DisplayFormattersFallBackRatherThanPrintingNonsense(float value)
        {
            Assert.Equal("?", B1071_DisplayMath.FormatFoodTrendCompact(value));
            Assert.Equal(FoodTrendDisplayKind.Unknown, B1071_DisplayMath.FormatFoodTrend(value).Kind);
            Assert.Equal(
                PeacePressureDisplayDirection.Neutral,
                B1071_DisplayMath.GetPeacePressureBand(value, pressureBands: true).Direction);
            Assert.Equal(
                ExhaustionDisplayTag.Fresh,
                B1071_DisplayMath.ExhaustionTag(value, pressureBands: false, DiplomacyPressureBand.Low));
            Assert.Equal(int.MaxValue, B1071_DisplayMath.EstimateTimeToRebelDays(80f, value, false));
        }

        [Fact]
        public void ScoringHelpersStayInsideTheirPercentageRange()
        {
            float[] hostileFloats = { float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.MaxValue, -1e30f };
            int[] hostileInts = { 0, -1, int.MaxValue, int.MinValue };

            foreach (float hostileFloat in hostileFloats)
            {
                foreach (int hostileInt in hostileInts)
                {
                    int instability = B1071_DisplayMath.ComputeInstabilityScore(
                        true, hostileInt, hostileInt, hostileInt, hostileInt);
                    int rebellion = B1071_DisplayMath.ComputeRebellionRiskScore(
                        hostileFloat, hostileFloat, hostileFloat, true, false);

                    Assert.InRange(instability, 0, 100);
                    Assert.InRange(rebellion, 0, 100);
                }
            }
        }

        [Fact]
        public void ColumnTruncationHandlesEveryWidthWithoutThrowing()
        {
            string[] texts = { string.Empty, "a", "ab", "abcdefghij", "ᾨᾩᾪᾫ" };

            foreach (string text in texts)
            {
                for (int width = 0; width <= text.Length + 2; width++)
                {
                    string truncated = B1071_DisplayMath.TruncateForColumn(text, width);

                    Assert.NotNull(truncated);
                    Assert.True(
                        truncated.Length <= Math.Max(width, text.Length),
                        $"Truncating \"{text}\" to {width} produced \"{truncated}\".");
                }
            }
        }

        [Fact]
        public void QueryScoringToleratesEmptyAndMissingFields()
        {
            Assert.Equal(0, B1071_DisplayMath.ComputeQueryScore(string.Empty, new[] { "Vlandia" }, null));
            Assert.Equal(0, B1071_DisplayMath.ComputeQueryScore("   ", new[] { "Vlandia" }, null));
            Assert.Equal(0, B1071_DisplayMath.ComputeQueryScore("vlandia", Array.Empty<string>(), null));
            Assert.Equal(0, B1071_DisplayMath.ComputeQueryScore("vlandia", new[] { string.Empty }, null));
            Assert.True(B1071_DisplayMath.ComputeQueryScore("vlandia", new[] { "Vlandia" }, null) > 0);
            Assert.True(B1071_DisplayMath.ComputeQueryScore("vlandia", new[] { "Vlandia" }, new[] { 1f }) > 0);
            Assert.True(B1071_DisplayMath.ComputeQueryScore("vlandia", new[] { "Vlandia", "Battania" }, new[] { 1f }) > 0);
        }

        // Support ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Ordinary settings, used where the hostility belongs in the inputs rather than the config.
        /// </summary>
        private static FakeSettings RealisticSettings() => new()
        {
            TownPoolMax = 800,
            CastlePoolMax = 400,
            OtherPoolMax = 150,
            ProsperityNormalizer = 5000f,
            MaxPoolProsperityMinScale = 0.5f,
            MaxPoolProsperityMaxScale = 1.5f,
            SecurityBonusMinScale = 0.9f,
            SecurityBonusMaxScale = 1.1f,
            MaxPoolHearthMultiplier = 0.1f,
            HearthNormalizer = 1000,
            HearthBonusMaxPercent = 50f
        };

        /// <summary>
        /// Answers every roll with the low end of the requested range, so the battery is
        /// deterministic and never runs out of values however many times a formula rolls.
        /// </summary>
        private sealed class PermissiveRandom : IB1071Random
        {
            public int Next(int maxExclusive) => 0;

            public int Next(int minInclusive, int maxExclusive) => minInclusive;

            public float RangeFloat(float minInclusive, float maxInclusive) => minInclusive;

            public int RoundRandomized(float value) =>
                float.IsNaN(value) || float.IsInfinity(value) ? 0 : (int)value;
        }
    }
}
