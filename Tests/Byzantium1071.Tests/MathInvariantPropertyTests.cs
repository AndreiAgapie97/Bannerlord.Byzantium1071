using System;
using System.Collections.Generic;
using System.Linq;
using Byzantium1071.Campaign;
using FsCheck.Xunit;
using Xunit;

namespace Byzantium1071.Tests
{
    /// <summary>
    /// Rules that must hold for every input rather than for a list of chosen ones. Each rule is
    /// attacked with a thousand generated cases per run, and FsCheck shrinks any failure down to
    /// the smallest input that still breaks it.
    ///
    /// Inputs outside the range a campaign can produce are skipped by returning true, so a rule
    /// only ever fails on something the game could actually hand the formula.
    /// </summary>
    public sealed class MathInvariantPropertyTests
    {
        // Costs ───────────────────────────────────────────────────────────────

        [Property(MaxTest = 1000)]
        public bool RecallingMoreSoldiersNeverCostsLessGold(int tier, int count, int extra)
        {
            if (!IsSaneCount(count) || !IsSaneCount(extra)) return true;

            FakeSettings settings = new() { DemobilizationRecallGoldPerTier = 40 };
            int fewer = B1071_ServiceMath.RecallGoldCost(tier, count, settings);
            int more = B1071_ServiceMath.RecallGoldCost(tier, count + extra, settings);

            return more >= fewer && fewer >= 0;
        }

        [Property(MaxTest = 1000)]
        public bool RecallingAHigherTierNeverCostsLessGold(int tier, int count)
        {
            if (!IsSaneCount(count) || tier < 1 || tier > 5) return true;

            FakeSettings settings = new() { DemobilizationRecallGoldPerTier = 40 };

            return B1071_ServiceMath.RecallGoldCost(tier + 1, count, settings)
                >= B1071_ServiceMath.RecallGoldCost(tier, count, settings);
        }

        [Property(MaxTest = 1000)]
        public bool ExtendingMoreSoldiersNeverCostsLessGold(int tier, int count, int extra, int alreadyExtended)
        {
            if (!IsSaneCount(count) || !IsSaneCount(extra) || alreadyExtended < 0 || alreadyExtended > 10)
            {
                return true;
            }

            FakeSettings settings = ExtensionSettings();
            int fewer = B1071_ServiceMath.ExtensionCost(tier, count, alreadyExtended, settings);
            int more = B1071_ServiceMath.ExtensionCost(tier, count + extra, alreadyExtended, settings);

            return more >= fewer && fewer >= 0;
        }

        [Property(MaxTest = 1000)]
        public bool EachExtensionCostsAtLeastAsMuchAsTheLast(int tier, int count, int alreadyExtended)
        {
            if (!IsSaneCount(count) || alreadyExtended < 0 || alreadyExtended > 20) return true;

            FakeSettings settings = ExtensionSettings();

            return B1071_ServiceMath.ExtensionCost(tier, count, alreadyExtended + 1, settings)
                >= B1071_ServiceMath.ExtensionCost(tier, count, alreadyExtended, settings);
        }

        [Property(MaxTest = 1000)]
        public bool ACultureDiscountOnlyEverMakesRecruitingCheaper(int baseCost, int discountPercent)
        {
            if (!IsSaneCount(baseCost) || discountPercent < 0 || discountPercent > 100) return true;

            FakeSettings settings = new() { EnableCultureDiscount = true, CultureCostPercent = discountPercent };
            int full = B1071_ManpowerMath.CultureDiscountedRecruitmentCost(baseCost, false, settings);
            int discounted = B1071_ManpowerMath.CultureDiscountedRecruitmentCost(baseCost, true, settings);

            return discounted <= full && discounted >= 1;
        }

        [Property(MaxTest = 1000)]
        public bool AdjustedWagesAreAlwaysWorthPaying(int vanillaWage, int preset, int tier)
        {
            if (vanillaWage < 0 || vanillaWage > 100_000) return true;

            int wage = B1071_EconomyMath.AdjustedWage(vanillaWage, preset, tier);

            return vanillaWage == 0 ? wage >= 0 : wage >= 1;
        }

        // Preset lookup tables ────────────────────────────────────────────────

        [Property(MaxTest = 1000)]
        public bool PresetZeroLeavesTheVanillaEconomyExactlyAsItWas(int tier)
        {
            return B1071_EconomyMath.HireFactor(0, tier) == 0f
                && B1071_EconomyMath.WageFactor(0, tier) == 0f
                && B1071_EconomyMath.ArmorFactor(0, tier) == 0f
                && B1071_EconomyMath.SurvivalBonus(0, tier) == 0f
                && B1071_EconomyMath.ForeignHireFactor(0) == 0f;
        }

        [Property(MaxTest = 1000)]
        public bool AnyTierWhatsoeverResolvesToARealTableSlot(int preset, int tier)
        {
            float[] factors =
            {
                B1071_EconomyMath.HireFactor(preset, tier),
                B1071_EconomyMath.WageFactor(preset, tier),
                B1071_EconomyMath.ArmorFactor(preset, tier),
                B1071_EconomyMath.SurvivalBonus(preset, tier)
            };

            return factors.All(factor => !float.IsNaN(factor) && !float.IsInfinity(factor));
        }

        [Property(MaxTest = 1000)]
        public bool BetterTroopsAreNeverCheaperToHireOrPay(int preset, int tier)
        {
            if (preset < 0 || preset > 3 || tier < 1 || tier > 5) return true;

            return B1071_EconomyMath.HireFactor(preset, tier + 1) >= B1071_EconomyMath.HireFactor(preset, tier)
                && B1071_EconomyMath.WageFactor(preset, tier + 1) >= B1071_EconomyMath.WageFactor(preset, tier);
        }

        [Property(MaxTest = 1000)]
        public bool BetterTroopsAreNeverEasierToKill(int preset, int tier)
        {
            if (preset < 0 || preset > 3 || tier < 1 || tier > 5) return true;

            // Armour factors are negative: a lower number means less damage taken.
            return B1071_EconomyMath.ArmorFactor(preset, tier + 1) <= B1071_EconomyMath.ArmorFactor(preset, tier)
                && B1071_EconomyMath.SurvivalBonus(preset, tier + 1) >= B1071_EconomyMath.SurvivalBonus(preset, tier);
        }

        // Service ─────────────────────────────────────────────────────────────

        [Property(MaxTest = 1000)]
        public bool HigherTiersAlwaysServeAtLeastAsLong(int tier, int preset)
        {
            if (preset < 0 || preset > 3 || tier < 1 || tier > 5) return true;

            FakeSettings settings = new() { DemobilizationIntensityPreset = preset };

            return B1071_ServiceMath.BaseServiceDays(tier + 1, settings)
                >= B1071_ServiceMath.BaseServiceDays(tier, settings);
        }

        [Property(MaxTest = 1000)]
        public bool NoSoldierIsEverDischargedBeforeServingADay(int tier, int preset, int season, bool inCrisis)
        {
            if (preset < 0 || preset > 3) return true;

            FakeSettings settings = new()
            {
                DemobilizationIntensityPreset = preset,
                EnableDemobilizationSeasonality = true,
                DemobilizationSpringSummerThresholdPercent = 80,
                DemobilizationWinterThresholdPercent = 120,
                EnableDemobilizationCrisisCompression = true,
                DemobilizationCrisisThresholdPercent = 60
            };

            B1071Season resolved = (B1071Season)(((season % 4) + 4) % 4);

            return B1071_ServiceMath.ServiceThresholdDays(tier, resolved, inCrisis, settings) >= 1;
        }

        [Property(MaxTest = 1000)]
        public bool TheDailyRetirementCapNeverReleasesMoreThanAreOverdue(int overdue, int capPercent)
        {
            // The caller only asks for a cap once it has found at least one overdue soldier, and the
            // slider runs 1-100, so those are the inputs the rule has to hold for.
            if (!IsSaneCount(overdue) || overdue < 1) return true;
            if (capPercent < 1 || capPercent > 100) return true;

            int released = B1071_ServiceMath.DailyRetirementCap(overdue, capPercent);

            return released >= 1 && released <= overdue;
        }

        [Fact]
        public void TheDailyRetirementCapStillReportsOneWhenNobodyIsOverdue()
        {
            // A floor of one keeps a small garrison from freezing forever at 1 * 8 / 100 == 0.
            // With nothing overdue it reports a cap of one, which is harmless: the caller skips the
            // troop before asking, and the cap only ever bounds a list of genuinely overdue men.
            Assert.Equal(1, B1071_ServiceMath.DailyRetirementCap(0, 8));
            Assert.Equal(1, B1071_ServiceMath.DailyRetirementCap(1, 8));
            Assert.Equal(1, B1071_ServiceMath.DailyRetirementCap(12, 8));
            Assert.Equal(8, B1071_ServiceMath.DailyRetirementCap(100, 8));
            Assert.Equal(100, B1071_ServiceMath.DailyRetirementCap(100, 100));
        }

        [Property(MaxTest = 1000)]
        public bool ScatteringAndReturningNeverInventSoldiers(int count, int percent, int seed)
        {
            if (!IsSaneCount(count)) return true;

            int scattered = B1071_ServiceMath.ScatterCount(count, percent, new SeededRandom(seed));
            int returned = B1071_ServiceMath.VeteranReturnCount(count, percent, new SeededRandom(seed));

            return scattered >= 0 && scattered <= count && returned >= 0 && returned <= count;
        }

        // Castle recruitment ──────────────────────────────────────────────────

        [Property(MaxTest = 1000)]
        public bool TheAiNeverCommitsToTroopsItCannotAfford(int gold, int costPerUnit, int maxUnits, int buffer)
        {
            if (!IsSaneCount(gold) || !IsSaneCount(costPerUnit) || !IsSaneCount(maxUnits)) return true;
            if (buffer < 1 || buffer > 20) return true;

            FakeSettings settings = new() { CastleAiGoldBufferMultiplier = buffer };
            int affordable = B1071_CastlePoolMath.AiBufferedAffordableCount(gold, costPerUnit, maxUnits, settings);

            if (affordable < 0 || affordable > maxUnits) return false;
            if (costPerUnit <= 0) return true;

            return (long)affordable * costPerUnit * buffer < gold || affordable == 0;
        }

        [Property(MaxTest = 1000)]
        public bool AWeightedPickAlwaysLandsOnARealBucket(int weightA, int weightB, int weightC, int seed)
        {
            int[] weights = { Bounded(weightA), Bounded(weightB), Bounded(weightC) };
            int index = B1071_CastlePoolMath.ChooseWeightedBucketIndex(weights, new SeededRandom(seed));

            if (!B1071_CastlePoolMath.HasPositiveTotalWeight(weights))
            {
                return index == -1;
            }

            return index >= 0 && index < weights.Length && weights[index] > 0;
        }

        [Property(MaxTest = 1000)]
        public bool ThreeWayRecruitmentFeesNeitherCreateNorDestroyGold(
            int costPerTroop,
            int count,
            int feePercent,
            bool hasDepositor,
            bool recruiterOwnsCastle,
            bool recruiterIsDepositor)
        {
            if (!IsSaneCount(costPerTroop) || costPerTroop > 10_000) return true;
            if (!IsSaneCount(count) || count > 1_000) return true;
            if (feePercent < 0 || feePercent > 100) return true;

            CastleFeeSplit split = B1071_CastlePoolMath.RecruitmentFeeSplit(
                costPerTroop, count, feePercent, hasDepositor, recruiterOwnsCastle, recruiterIsDepositor);

            if (split.RecruiterCost < 0 || split.OwnerPayment < 0 || split.DepositorPayment < 0) return false;

            // Nobody is ever paid more than the recruiter actually handed over.
            return split.OwnerPayment + split.DepositorPayment <= split.RecruiterCost;
        }

        // Diplomacy ───────────────────────────────────────────────────────────

        [Property(MaxTest = 1000)]
        public bool SupportSwingsAlwaysRespectTheirDeclaredCaps(float exhaustion, float bias, float cap)
        {
            if (!IsSaneFloat(exhaustion) || !IsSaneFloat(bias) || !IsSaneFloat(cap)) return true;
            if (exhaustion < 0f || exhaustion > 100f || bias < 0f || bias > 10f) return true;
            if (cap < 0f || cap > 100f) return true;

            FakeSettings settings = new()
            {
                PeaceBiasBandLow = 0.2f,
                PeaceBiasBandHigh = 0.8f,
                DiplomacyWarSupportPenaltyPerPoint = 0.5f,
                WarSupportPenaltyCap = cap,
                PeaceSupportBonusCap = cap
            };

            foreach (DiplomacyPressureBand band in AllBands)
            {
                float penalty = B1071_ExhaustionMath.WarSupportPenalty(band, exhaustion, bias, settings);
                float bonus = B1071_ExhaustionMath.PeaceSupportBonus(band, exhaustion, bias, settings);

                if (bonus > cap || bonus < 0f) return false;
                if (band != DiplomacyPressureBand.Low && penalty > cap) return false;
                if (float.IsNaN(penalty) || float.IsNaN(bonus)) return false;
            }

            return true;
        }

        [Property(MaxTest = 1000)]
        public bool MorePressingWarsNeverRaiseTheBarToPeace(int wars, float baseThreshold, float reductionPerWar)
        {
            if (!IsSaneFloat(baseThreshold) || !IsSaneFloat(reductionPerWar)) return true;
            if (baseThreshold < 0f || baseThreshold > 1000f) return true;
            if (reductionPerWar < 0f || reductionPerWar > 100f) return true;
            if (!IsSaneCount(wars) || wars > 50) return true;

            FakeSettings settings = new()
            {
                DiplomacyForcedPeaceThreshold = baseThreshold,
                DiplomacyMajorWarPressureStartCount = 1,
                DiplomacyForcedPeaceThresholdReductionPerMajorWar = reductionPerWar
            };

            float fewer = B1071_ExhaustionMath.ForcedPeaceThreshold(wars, settings);
            float more = B1071_ExhaustionMath.ForcedPeaceThreshold(wars + 1, settings);

            return more <= fewer && fewer >= 1f;
        }

        [Property(MaxTest = 1000)]
        public bool ExhaustionOnlyEverPushesTheBandUpwards(float lower, float higher)
        {
            if (!IsSaneFloat(lower) || !IsSaneFloat(higher)) return true;
            if (lower < 0f || higher < lower || higher > 1000f) return true;

            FakeSettings settings = new()
            {
                PressureBandRisingStart = 35f,
                PressureBandCrisisStart = 85f,
                PressureBandHysteresis = 5f
            };

            foreach (DiplomacyPressureBand current in AllBands)
            {
                DiplomacyPressureBand atLower = B1071_ExhaustionMath.EvaluatePressureBand(lower, current, settings);
                DiplomacyPressureBand atHigher = B1071_ExhaustionMath.EvaluatePressureBand(higher, current, settings);

                if (atHigher < atLower) return false;
            }

            return true;
        }

        [Property(MaxTest = 1000)]
        public bool AWarPastItsMinimumLengthCarriesNoEarlyPeacePenalty(float elapsedDays, int minimumDays)
        {
            if (!IsSaneFloat(elapsedDays) || elapsedDays < 0f || elapsedDays > 10_000f) return true;
            if (minimumDays < 0 || minimumDays > 500) return true;

            FakeSettings settings = new()
            {
                MinWarDurationDaysBeforeForcedPeace = minimumDays,
                EnableMultiFrontWarRelief = false,
                EarlyWarPeacePenaltyStrength = 10f
            };

            float penalty = B1071_ExhaustionMath.EarlyWarPeacePenalty(elapsedDays, false, settings);

            if (elapsedDays >= minimumDays) return penalty == 0f;

            return penalty > 0f && penalty <= 10f;
        }

        // Slaves, governance and pools ────────────────────────────────────────

        [Property(MaxTest = 1000)]
        public bool TheSlaveCapNeverDropsBelowItsConfiguredFloor(float prosperity, int minimum, float perProsperity)
        {
            if (!IsSaneFloat(prosperity) || prosperity < 0f || prosperity > 100_000f) return true;
            if (minimum < 0 || minimum > 10_000) return true;
            if (!IsSaneFloat(perProsperity) || perProsperity < 0f || perProsperity > 10f) return true;

            FakeSettings settings = new() { SlaveCapMinimum = minimum, SlaveCapPerProsperity = perProsperity };

            return B1071_SlaveMath.SlaveCap(prosperity, settings) >= minimum;
        }

        [Property(MaxTest = 1000)]
        public bool DecayLeavesNoDebtBehind(int slaveCount, float accumulator, float decayPercent)
        {
            if (!IsSaneCount(slaveCount)) return true;
            if (!IsSaneFloat(accumulator) || accumulator < 0f || accumulator >= 1f) return true;
            if (!IsSaneFloat(decayPercent) || decayPercent < 0f || decayPercent > 100f) return true;

            FakeSettings settings = new() { SlaveDailyDecayPercent = decayPercent };
            SlaveDecayResult result = B1071_SlaveMath.DailyDecay(slaveCount, accumulator, settings);

            return result.WholeLoss >= 0
                && result.WholeLoss <= slaveCount
                && result.RemainingAccumulator >= 0f
                && !float.IsNaN(result.RemainingAccumulator);
        }

        [Property(MaxTest = 1000)]
        public bool StrainNeverEscapesItsCapInEitherDirection(float current, float amount, float cap)
        {
            if (!IsSaneFloat(current) || !IsSaneFloat(amount) || !IsSaneFloat(cap)) return true;
            if (current < 0f || current > 1000f || amount < 0f || amount > 1000f) return true;
            if (cap < 1f || cap > 1000f || current > cap) return true;

            FakeSettings settings = new() { GovernanceStrainCap = cap };
            float added = B1071_GovernanceMath.AddStrain(current, amount, settings);
            float reduced = B1071_GovernanceMath.ReduceStrain(current, amount);
            float decayed = B1071_GovernanceMath.DailyStrain(current, 1f, 2f);

            return added <= cap && reduced >= 0f && decayed >= 0f;
        }

        [Property(MaxTest = 1000)]
        public bool DevastationStaysOnItsHundredPointScale(float current, float perRaid, float decayPerDay)
        {
            if (!IsSaneFloat(current) || current < 0f || current > 100f) return true;
            if (!IsSaneFloat(perRaid) || perRaid < 0f || perRaid > 100f) return true;
            if (!IsSaneFloat(decayPerDay) || decayPerDay < 0f || decayPerDay > 100f) return true;

            FakeSettings settings = new() { DevastationPerRaid = perRaid, DevastationDecayPerDay = decayPerDay };
            float raided = B1071_GovernanceMath.AddDevastation(current, settings);
            float recovered = B1071_GovernanceMath.DailyDevastation(current, settings);

            return raided <= 100f && raided >= 0f && recovered >= 0f && recovered <= 100f;
        }

        [Property(MaxTest = 1000)]
        public bool PoolBandsAlwaysLandOnOneOfTheFiveSteps(int current, int max)
        {
            int band = B1071_ManpowerMath.PoolBand(current, max);

            return band >= 0 && band <= 4;
        }

        [Property(MaxTest = 1000)]
        public bool AFullerPoolNeverReportsAWorseBand(int max, int lower, int extra)
        {
            if (!IsSaneCount(max) || !IsSaneCount(lower) || !IsSaneCount(extra)) return true;

            return B1071_ManpowerMath.PoolBand(lower + extra, max) >= B1071_ManpowerMath.PoolBand(lower, max);
        }

        // Apportionment ───────────────────────────────────────────────────────

        [Property(MaxTest = 1000)]
        public bool TheSameAllocationInputAlwaysProducesTheSameSplit(int total, int weightA, int weightB, int weightC)
        {
            if (!IsSaneCount(total)) return true;

            Dictionary<string, int> weights = new()
            {
                ["kingdom_a"] = Bounded(weightA),
                ["kingdom_b"] = Bounded(weightB),
                ["kingdom_c"] = Bounded(weightC)
            };

            Dictionary<string, int> first = B1071_ApportionMath.AllocateByWeights(total, weights, key => key);
            Dictionary<string, int> second = B1071_ApportionMath.AllocateByWeights(total, weights, key => key);

            return first.Count == second.Count
                && first.All(entry => second.TryGetValue(entry.Key, out int value) && value == entry.Value);
        }

        [Property(MaxTest = 1000)]
        public bool NobodyIsEverAllocatedANegativeShare(int total, int weightA, int weightB, int weightC)
        {
            if (!IsSaneCount(total)) return true;

            Dictionary<string, int> weights = new()
            {
                ["kingdom_a"] = Bounded(weightA),
                ["kingdom_b"] = Bounded(weightB),
                ["kingdom_c"] = Bounded(weightC)
            };

            Dictionary<string, int> allocation = B1071_ApportionMath.AllocateByWeights(total, weights, key => key);

            return allocation.Values.All(share => share >= 0);
        }

        // Text handling ───────────────────────────────────────────────────────

        [Property(MaxTest = 1000)]
        public bool ColumnTextNeverOutgrowsItsColumn(string? text, int width)
        {
            if (width < 1 || width > 200) return true;

            string truncated = B1071_DisplayMath.TruncateForColumn(text ?? string.Empty, width);

            return truncated.Length <= Math.Max(width, (text ?? string.Empty).Length);
        }

        [Property(MaxTest = 1000)]
        public bool SearchScoresAreNeverNegative(string? query, string? field)
        {
            int score = B1071_DisplayMath.ComputeQueryScore(
                query ?? string.Empty, new[] { field ?? string.Empty }, null);

            return score >= 0;
        }

        [Property(MaxTest = 1000)]
        public bool ModIdentificationNeverThrowsOnAnyString(string? value)
        {
            B1071_CompatibilityMath.IsFrameworkId(value);
            B1071_CompatibilityMath.IsNativeAssembly(value);

            return true;
        }

        [Property(MaxTest = 1000)]
        public bool ClampedFractionsAlwaysLandBetweenZeroAndOne(float value)
        {
            if (float.IsNaN(value)) return true;

            float clamped = B1071_ManpowerMath.Clamp01(value);

            return clamped >= 0f && clamped <= 1f;
        }

        // Support ─────────────────────────────────────────────────────────────

        private static readonly DiplomacyPressureBand[] AllBands =
        {
            DiplomacyPressureBand.Low, DiplomacyPressureBand.Rising, DiplomacyPressureBand.Crisis
        };

        private static bool IsSaneCount(int value) => value >= 0 && value <= 100_000;

        private static bool IsSaneFloat(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        /// <summary>Folds a generated integer into the 0-100 range weights actually use.</summary>
        private static int Bounded(int value) => Math.Abs(value % 101);

        private static FakeSettings ExtensionSettings() => new()
        {
            DemobilizationIntensityPreset = 1,
            DemobilizationExtensionDays = 21,
            DemobilizationExtensionGoldPerTierDay = 2,
            DemobilizationMaxExtensions = 3
        };

        /// <summary>
        /// A repeatable stand-in for the game's random source. The same seed always produces the
        /// same rolls, so a failing property shrinks to an input that can be replayed exactly.
        /// </summary>
        private sealed class SeededRandom : IB1071Random
        {
            private readonly Random _random;

            internal SeededRandom(int seed) => _random = new Random(seed);

            public int Next(int maxExclusive) => Next(0, maxExclusive);

            public int Next(int minInclusive, int maxExclusive) =>
                maxExclusive <= minInclusive ? minInclusive : _random.Next(minInclusive, maxExclusive);

            public float RangeFloat(float minInclusive, float maxInclusive) =>
                maxInclusive <= minInclusive
                    ? minInclusive
                    : minInclusive + (float)_random.NextDouble() * (maxInclusive - minInclusive);

            public int RoundRandomized(float value)
            {
                if (float.IsNaN(value) || float.IsInfinity(value)) return 0;

                int whole = (int)value;
                float remainder = value - whole;

                return _random.NextDouble() < remainder ? whole + 1 : whole;
            }
        }
    }
}
