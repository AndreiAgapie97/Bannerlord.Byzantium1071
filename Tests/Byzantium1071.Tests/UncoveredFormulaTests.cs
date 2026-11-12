using System;
using System.Globalization;
using Byzantium1071.Campaign;
using FsCheck.Xunit;
using Xunit;

namespace Byzantium1071.Tests
{
    /// <summary>
    /// Covers the formulas that had no direct test of their own. Each is small, but every
    /// one of them feeds a number the player sees or a decision the AI makes.
    /// </summary>
    public sealed class UncoveredFormulaTests
    {
        // Courier and march speeds ────────────────────────────────────────────

        [Theory]
        [InlineData(0, 1f)]
        [InlineData(-50, 1f)]
        [InlineData(1, 1f)]
        [InlineData(7, 7f)]
        public void TravelSpeedsNeverFallBelowOneSoTheyCannotDivideByZero(int configured, float expected)
        {
            FakeSettings settings = new()
            {
                DemobilizationCourierSpeed = configured,
                DemobilizationMarchSpeed = configured
            };

            Assert.Equal(expected, B1071_ServiceMath.CourierSpeedPerDay(settings));
            Assert.Equal(expected, B1071_ServiceMath.MarchSpeedPerDay(settings));
        }

        [Fact]
        public void RecallEstimatesStayFiniteEvenWithSpeedsConfiguredToZero()
        {
            FakeSettings settings = new()
            {
                DemobilizationCourierSpeed = 0,
                DemobilizationMarchSpeed = 0
            };

            Assert.Equal(1, B1071_ServiceMath.EstimateRecallDays(0f, settings));
            Assert.Equal(200, B1071_ServiceMath.EstimateRecallDays(100f, settings));
            Assert.Equal(0, B1071_ServiceMath.EstimateArrivalDays(0f, 0f, settings));
            Assert.Equal(5, B1071_ServiceMath.EstimateArrivalDays(2f, 3f, settings));
        }

        [Fact]
        public void ArrivalIgnoresCourierTimeOnceTheMessengerHasArrived()
        {
            FakeSettings settings = new()
            {
                DemobilizationCourierSpeed = 10,
                DemobilizationMarchSpeed = 5
            };

            Assert.Equal(4, B1071_ServiceMath.EstimateArrivalDays(0f, 20f, settings));
            Assert.Equal(4, B1071_ServiceMath.EstimateArrivalDays(-100f, 20f, settings));
            Assert.Equal(6, B1071_ServiceMath.EstimateArrivalDays(20f, 20f, settings));
        }

        [Property(MaxTest = 1000)]
        public bool RecallIsNeverEstimatedAtLessThanOneDay(float distance, int courierSpeed, int marchSpeed)
        {
            if (float.IsNaN(distance) || float.IsInfinity(distance) || distance < 0f || distance > 1_000_000f)
            {
                return true;
            }

            FakeSettings settings = new()
            {
                DemobilizationCourierSpeed = courierSpeed,
                DemobilizationMarchSpeed = marchSpeed
            };

            return B1071_ServiceMath.EstimateRecallDays(distance, settings) >= 1;
        }

        // ClampInt ────────────────────────────────────────────────────────────

        [Theory]
        [InlineData(5, 0, 10, 5)]
        [InlineData(-1, 0, 10, 0)]
        [InlineData(11, 0, 10, 10)]
        [InlineData(0, 0, 0, 0)]
        [InlineData(int.MaxValue, 0, 100, 100)]
        [InlineData(int.MinValue, -5, 5, -5)]
        [InlineData(7, 10, 2, 10)]
        [InlineData(15, 10, 2, 2)]
        [InlineData(2, 10, 2, 10)]
        public void ClampIntHonoursItsBoundsIncludingAnInvertedRange(int value, int min, int max, int expected)
        {
            Assert.Equal(expected, B1071_ServiceMath.ClampInt(value, min, max));
        }

        [Property(MaxTest = 1000)]
        public bool ClampedValuesAlwaysLandInsideTheRequestedRange(int value, int min, int max)
        {
            if (min > max)
            {
                // An inverted range cannot be satisfied, so the low bound wins below it
                // and the high bound wins everywhere else. Neither branch throws.
                int collapsed = B1071_ServiceMath.ClampInt(value, min, max);
                return collapsed == (value < min ? min : max);
            }

            int clamped = B1071_ServiceMath.ClampInt(value, min, max);
            return clamped >= min && clamped <= max;
        }

        // Castle bucket weights ───────────────────────────────────────────────

        [Fact]
        public void BucketWeightsAreOnlyUsableWhenTheirTotalIsPositive()
        {
            Assert.False(B1071_CastlePoolMath.HasPositiveTotalWeight(Array.Empty<int>()));
            Assert.False(B1071_CastlePoolMath.HasPositiveTotalWeight(new[] { 0, 0, 0, 0 }));
            Assert.False(B1071_CastlePoolMath.HasPositiveTotalWeight(new[] { -5, -5 }));
            Assert.False(B1071_CastlePoolMath.HasPositiveTotalWeight(new[] { 5, -5 }));
            Assert.True(B1071_CastlePoolMath.HasPositiveTotalWeight(new[] { 0, 0, 0, 1 }));
            Assert.True(B1071_CastlePoolMath.HasPositiveTotalWeight(new[] { 45, 35, 15, 5 }));
        }

        [Fact]
        public void WeightUsabilityAgreesWithWhetherABucketCanActuallyBePicked()
        {
            int[][] cases =
            {
                new[] { 0, 0, 0, 0 },
                new[] { 5, -5 },
                new[] { 0, 0, 0, 1 },
                new[] { 45, 35, 15, 5 }
            };

            foreach (int[] weights in cases)
            {
                bool usable = B1071_CastlePoolMath.HasPositiveTotalWeight(weights);
                int picked = B1071_CastlePoolMath.ChooseWeightedBucketIndex(weights, new FakeRandom(new[] { 0 }));

                Assert.Equal(usable, picked >= 0);
            }
        }

        // Foreign recruitment premium ─────────────────────────────────────────

        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(4)]
        [InlineData(99)]
        [InlineData(int.MaxValue)]
        [InlineData(int.MinValue)]
        public void ForeignHirePremiumIsDisabledOutsideTheConfiguredPresets(int preset)
        {
            Assert.Equal(0f, B1071_EconomyMath.ForeignHireFactor(preset));
        }

        [Fact]
        public void ForeignHirePremiumGrowsWithEachActivePreset()
        {
            float light = B1071_EconomyMath.ForeignHireFactor(1);
            float medium = B1071_EconomyMath.ForeignHireFactor(2);
            float heavy = B1071_EconomyMath.ForeignHireFactor(3);

            Assert.True(light > 0f);
            Assert.True(medium > light);
            Assert.True(heavy > medium);
        }

        // Diplomacy band bias ─────────────────────────────────────────────────

        [Fact]
        public void CrisisCarriesHalfAgainTheRisingBandPeaceBias()
        {
            FakeSettings settings = new() { PeaceBiasBandLow = 0.2f, PeaceBiasBandHigh = 0.8f };

            Assert.Equal(0.2f, B1071_ExhaustionMath.BandPeaceBias(DiplomacyPressureBand.Low, settings));
            Assert.Equal(0.8f, B1071_ExhaustionMath.BandPeaceBias(DiplomacyPressureBand.Rising, settings));
            Assert.Equal(1.2f, B1071_ExhaustionMath.BandPeaceBias(DiplomacyPressureBand.Crisis, settings), 5);
            Assert.Equal(0.2f, B1071_ExhaustionMath.BandPeaceBias((DiplomacyPressureBand)42, settings));
        }

        [Property(MaxTest = 1000)]
        public bool PeaceBiasRisesWithTheBandWheneverBothSettingsArePositive(float low, float high)
        {
            if (float.IsNaN(low) || float.IsNaN(high) || float.IsInfinity(low) || float.IsInfinity(high))
            {
                return true;
            }

            if (low < 0f || high < low || high > 1_000f)
            {
                return true;
            }

            FakeSettings settings = new() { PeaceBiasBandLow = low, PeaceBiasBandHigh = high };
            float lowBias = B1071_ExhaustionMath.BandPeaceBias(DiplomacyPressureBand.Low, settings);
            float risingBias = B1071_ExhaustionMath.BandPeaceBias(DiplomacyPressureBand.Rising, settings);
            float crisisBias = B1071_ExhaustionMath.BandPeaceBias(DiplomacyPressureBand.Crisis, settings);

            return risingBias >= lowBias && crisisBias >= risingBias;
        }

        // Major war pressure ──────────────────────────────────────────────────

        [Theory]
        [InlineData(0, 0f)]
        [InlineData(1, 0.1f)]
        [InlineData(2, 0.2f)]
        [InlineData(5, 0.5f)]
        public void EachWarPastTheStartCountAddsOneStepOfPeaceBias(int wars, float expected)
        {
            FakeSettings settings = new()
            {
                DiplomacyMajorWarPressureStartCount = 1,
                DiplomacyExtraPeaceBiasPerMajorWar = 0.1f
            };

            Assert.Equal(expected, B1071_ExhaustionMath.MajorWarPressureBias(wars, settings), 5);
        }

        [Fact]
        public void MajorWarPressureIgnoresNegativeWarCountsAndNegativeStepSizes()
        {
            FakeSettings settings = new()
            {
                DiplomacyMajorWarPressureStartCount = 0,
                DiplomacyExtraPeaceBiasPerMajorWar = -1f
            };

            Assert.Equal(0f, B1071_ExhaustionMath.MajorWarPressureBias(-10, settings));
            Assert.Equal(0f, B1071_ExhaustionMath.MajorWarPressureBias(10, settings));
        }

        [Property(MaxTest = 1000)]
        public bool MajorWarPressureIsNeverNegative(int wars, int startCount, float step)
        {
            if (float.IsNaN(step) || float.IsInfinity(step))
            {
                return true;
            }

            FakeSettings settings = new()
            {
                DiplomacyMajorWarPressureStartCount = startCount,
                DiplomacyExtraPeaceBiasPerMajorWar = step
            };

            float bias = B1071_ExhaustionMath.MajorWarPressureBias(wars, settings);
            return bias >= 0f && !float.IsNaN(bias);
        }

        // Legacy (pre-band) diplomacy support ─────────────────────────────────

        [Fact]
        public void LegacySupportAdjustmentsScaleLinearlyWithExhaustion()
        {
            FakeSettings settings = new()
            {
                DiplomacyWarSupportPenaltyPerPoint = 0.5f,
                DiplomacyPeaceSupportBonusPerPoint = 0.25f
            };

            Assert.Equal(0f, B1071_ExhaustionMath.LegacyWarSupportPenalty(0f, 0f, settings));
            Assert.Equal(20f, B1071_ExhaustionMath.LegacyWarSupportPenalty(40f, 0f, settings), 5);
            Assert.Equal(23f, B1071_ExhaustionMath.LegacyWarSupportPenalty(40f, 3f, settings), 5);
            Assert.Equal(10f, B1071_ExhaustionMath.LegacyPeaceSupportBonus(40f, 0f, settings), 5);
            Assert.Equal(13f, B1071_ExhaustionMath.LegacyPeaceSupportBonus(40f, 3f, settings), 5);
        }

        [Fact]
        public void BandedSupportIsCappedWhereTheLegacyFormulaIsNot()
        {
            FakeSettings settings = new()
            {
                PeaceBiasBandLow = 0.2f,
                PeaceBiasBandHigh = 0.8f,
                PeaceSupportBonusCap = 30f,
                DiplomacyPeaceSupportBonusPerPoint = 0.8f
            };

            float banded = B1071_ExhaustionMath.PeaceSupportBonus(
                DiplomacyPressureBand.Crisis, exhaustion: 100f, majorWarBias: 0f, settings);
            float legacy = B1071_ExhaustionMath.LegacyPeaceSupportBonus(100f, 0f, settings);

            Assert.Equal(30f, banded);
            Assert.True(legacy > banded, "The legacy formula is deliberately uncapped.");
        }

        // Slave raid divisor ──────────────────────────────────────────────────

        [Theory]
        [InlineData(0, 1)]
        [InlineData(-20, 1)]
        [InlineData(1, 1)]
        [InlineData(50, 50)]
        public void RaidHearthDivisorNeverReachesZero(int configured, int expected)
        {
            FakeSettings settings = new() { SlaveHearthDivisor = configured };

            Assert.Equal(expected, B1071_SlaveMath.RaidHearthDivisor(settings));
        }

        [Fact]
        public void RaidSlaveCountTruncatesTowardsZeroAndSurvivesAZeroDivisor()
        {
            FakeSettings settings = new() { SlaveHearthDivisor = 0 };
            Assert.Equal(199, B1071_SlaveMath.RaidSlaveCount(199.9f, settings));

            settings.SlaveHearthDivisor = 100;
            Assert.Equal(0, B1071_SlaveMath.RaidSlaveCount(99f, settings));
            Assert.Equal(1, B1071_SlaveMath.RaidSlaveCount(100f, settings));
            Assert.Equal(0, B1071_SlaveMath.RaidSlaveCount(0f, settings));
        }

        [Property(MaxTest = 1000)]
        public bool RaidSlavesNeverExceedHearthsWhenTheDivisorIsAtLeastOne(float hearths, int divisor)
        {
            if (float.IsNaN(hearths) || float.IsInfinity(hearths) || hearths < 0f || hearths > 100_000f)
            {
                return true;
            }

            FakeSettings settings = new() { SlaveHearthDivisor = divisor };
            int slaves = B1071_SlaveMath.RaidSlaveCount(hearths, settings);

            return slaves >= 0 && slaves <= hearths;
        }

        // Manpower display ────────────────────────────────────────────────────

        [Theory]
        [InlineData(0, 0, "0/0")]
        [InlineData(5, 12, "5/12")]
        [InlineData(1000, 25000, "1,000/25,000")]
        [InlineData(-3, 10, "-3/10")]
        public void ManpowerIsShownAsGroupedCurrentOverMaximum(int current, int maximum, string expected)
        {
            using CultureSwap _ = new(CultureInfo.InvariantCulture);

            Assert.Equal(expected, B1071_DisplayMath.FormatManpower(current, maximum));
        }

        [Fact]
        public void ManpowerDisplayHandlesTheExtremesOfTheIntegerRange()
        {
            using CultureSwap _ = new(CultureInfo.InvariantCulture);

            Assert.Equal("2,147,483,647/2,147,483,647", B1071_DisplayMath.FormatManpower(int.MaxValue, int.MaxValue));
            Assert.Equal("-2,147,483,648/0", B1071_DisplayMath.FormatManpower(int.MinValue, 0));
        }

        [Property(MaxTest = 1000)]
        public bool ManpowerDisplayAlwaysContainsExactlyOneSeparator(int current, int maximum)
        {
            using CultureSwap _ = new(CultureInfo.InvariantCulture);

            string formatted = B1071_DisplayMath.FormatManpower(current, maximum);
            int separators = 0;
            foreach (char character in formatted)
            {
                if (character == '/')
                {
                    separators++;
                }
            }

            return separators == 1;
        }

        /// <summary>
        /// Pins the thread culture for the duration of a test so grouped number formatting
        /// does not depend on the machine the suite runs on.
        /// </summary>
        private sealed class CultureSwap : IDisposable
        {
            private readonly CultureInfo _previous;

            internal CultureSwap(CultureInfo culture)
            {
                _previous = CultureInfo.CurrentCulture;
                CultureInfo.CurrentCulture = culture;
            }

            public void Dispose() => CultureInfo.CurrentCulture = _previous;
        }
    }
}
