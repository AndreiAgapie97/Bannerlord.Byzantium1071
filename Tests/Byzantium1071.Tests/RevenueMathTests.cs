using Byzantium1071.Campaign;
using Xunit;

namespace Byzantium1071.Tests
{
    /// <summary>
    /// The revenue taper is the only thing standing between a mature settlement and an income line
    /// that keeps pace with the settlement's growth, so its shape is pinned here rather than left
    /// to inspection.
    /// </summary>
    public sealed class RevenueMathTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TariffPayoutNeverFallsAsTheBasisGrowsAcrossAllowedSettings(bool village)
        {
            int maxKnee = village ? 10000 : 30000;
            foreach (int strength in new[] { 5, 70, 99, 100 })
            for (float curve = 1f; curve <= 10f; curve += 0.25f)
            for (int knee = 0; knee <= maxKnee; knee += 500)
            {
                double previous = 0;
                for (int basis = 1; basis <= 150000; basis += 137)
                {
                    float scale = village
                        ? B1071_RevenueMath.VillageTariffScale(basis, strength, curve, knee)
                        : TownTariff(basis, strength, curve, knee);
                    double payout = basis * (double)scale;
                    Assert.True(payout >= previous - 0.02, $"Payout fell: village={village}, strength={strength}, curve={curve}, knee={knee}, basis={basis}");
                    previous = payout;
                }
            }
        }

        [Fact]
        public void VillageGrowthAboveTheMaximumKneeStillIncreasesIncome()
        {
            Assert.Equal(7000f, 10000 * B1071_RevenueMath.VillageTariffScale(10000, 70, 1f, 10000), 2);
            Assert.Equal(8166.67f, 15000 * B1071_RevenueMath.VillageTariffScale(15000, 70, 1f, 10000), 2);
        }

        [Fact]
        public void TaxPayoutNeverFallsAboveTheFixedExemptionAcrossAllowedSettings()
        {
            foreach (int strength in new[] { 5, 70, 99, 100 })
            for (float curve = 1f; curve <= 10f; curve += 0.25f)
            for (int knee = 0; knee <= 4000; knee += 200)
            {
                double previous = 0;
                for (int prosperity = 41; prosperity <= 30000; prosperity += 31)
                {
                    double payout = prosperity * 0.35 * B1071_RevenueMath.TownTaxScale(prosperity, strength, curve, knee);
                    Assert.True(payout >= previous - 0.002, $"Tax fell: strength={strength}, curve={curve}, knee={knee}, prosperity={prosperity}");
                    previous = payout;
                }
            }
        }

        /// <summary>
        /// The definition of the curve, written out independently of the implementation so the two
        /// cannot drift together. The degenerate curve=0 case is excluded here -- the definition
        /// divides by the curve -- and covered by
        /// <see cref="ADegenerateCurveLeavesTheStrengthAsTheOnlyLimit"/> instead.
        /// </summary>
        private static double Expected(double ratio, double curve) =>
            1d / System.Math.Pow(1d + ratio / curve, 1d / curve);

        private static float TownTariff(int basis, int strengthPercent, float curve, int knee = 0) =>
            B1071_RevenueMath.TownTariffScale(basis, strengthPercent, curve, knee);

        [Theory]
        [InlineData(1f)]
        [InlineData(2f)]
        [InlineData(4f)]
        [InlineData(10f)]
        public void FullStrengthLeavesEveryBasisExactlyAsVanilla(float curve)
        {
            Assert.Equal(1f, B1071_RevenueMath.TownTariffScale(1_000_000, 100, curve));
            Assert.Equal(1f, B1071_RevenueMath.VillageTariffScale(1_000_000, 100, curve));
            Assert.Equal(1f, B1071_RevenueMath.TownTaxScale(1_000_000f, 100, curve));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        public void AnEmptyTariffBasisIsUntouched(int basis)
        {
            Assert.Equal(1f, TownTariff(basis, 60, 2f));
            Assert.Equal(1f, B1071_RevenueMath.VillageTariffScale(basis, 60, 2f));
        }

        /// <summary>
        /// A village with a few hundred hearths must not feel a setting aimed at runaway towns.
        /// </summary>
        [Theory]
        [InlineData(0f)]
        [InlineData(39.9f)]
        [InlineData(40f)]
        public void TaxLeavesSmallSettlementsAlone(float prosperity) =>
            Assert.Equal(1f, B1071_RevenueMath.TownTaxScale(prosperity, 5, 2f));

        [Fact]
        public void TheCurveNeverCutsDeeperThanTheSettingAskedFor()
        {
            for (int strength = 5; strength <= 100; strength += 5)
            {
                for (float curve = 1f; curve <= 10f; curve += 0.5f)
                {
                    float share = B1071_RevenueMath.StrengthPercentToShare(strength);

                    // From basis 1: a settlement with nothing in the pool returns exactly 1 by
                    // design, since there is no revenue to taper, so it is excluded from the bound.
                    for (int basis = 1; basis <= 200_000; basis += 997)
                    {
                        Assert.InRange(TownTariff(basis, strength, curve), 0f, share);
                        Assert.InRange(B1071_RevenueMath.VillageTariffScale(basis, strength, curve), 0f, share);
                    }

                    // Likewise for prosperity at or below the knee, which is skipped outright.
                    for (float prosperity = 41f; prosperity <= 30_000f; prosperity += 137f)
                    {
                        Assert.InRange(B1071_RevenueMath.TownTaxScale(prosperity, strength, curve), 0f, share);
                    }
                }
            }
        }

        /// <summary>
        /// The property the whole feature rests on: past the reference, more revenue must never buy
        /// proportionally more income.
        /// </summary>
        [Fact]
        public void PayoutIsMonotonicAndSubLinearAboveTheReference()
        {
            const int strength = 50;
            const float curve = 2f;

            int previousPayout = -1;
            for (int basis = 0; basis <= 40_000; basis += 25)
            {
                int payout = (int)(basis * TownTariff(basis, strength, curve));
                Assert.True(payout >= previousPayout, $"payout fell at basis {basis}");
                previousPayout = payout;
            }

            // Doubling an already-large basis must less than double the payout.
            int smaller = (int)(12_000 * TownTariff(12_000, strength, curve));
            int larger = (int)(24_000 * TownTariff(24_000, strength, curve));
            Assert.True(larger < smaller * 2, $"sub-linear growth broken: {smaller} -> {larger}");
        }

        [Fact]
        public void StrengthOutsideTheSliderRangeIsClampedRatherThanTrusted()
        {
            // A hand-edited settings file asking for 0% is honoured as 0. One asking for more than
            // 100% is read as "leave it alone", never as a licence to exceed vanilla.
            Assert.Equal(0f, B1071_RevenueMath.StrengthPercentToShare(0));
            Assert.Equal(0f, B1071_RevenueMath.StrengthPercentToShare(-50));
            Assert.Equal(0.6f, B1071_RevenueMath.StrengthPercentToShare(60));
            Assert.Equal(1f, B1071_RevenueMath.StrengthPercentToShare(100));
            Assert.Equal(1f, B1071_RevenueMath.StrengthPercentToShare(500));
        }

        [Theory]
        [InlineData(1f)]
        [InlineData(2f)]
        [InlineData(4f)]
        [InlineData(10f)]
        public void TheTownTariffCurveMatchesItsDefinition(float curve)
        {
            foreach (int basis in new[] { 1_000, 3_000, 12_000, 48_000, 500_000 })
            {
                float expected = (float)(0.6d * Expected(
                    basis / (double)B1071_RevenueMath.TownTariffReference, curve));
                Assert.Equal(expected, TownTariff(basis, 60, curve), 5);
            }
        }

        [Theory]
        [InlineData(1f)]
        [InlineData(2f)]
        [InlineData(4f)]
        [InlineData(10f)]
        public void TheVillageTariffCurveMatchesItsDefinition(float curve)
        {
            foreach (int basis in new[] { 600, 2_500, 10_000, 40_000 })
            {
                float expected = (float)(0.6d * Expected(
                    basis / (double)B1071_RevenueMath.VillageTariffReference, curve));
                Assert.Equal(expected, B1071_RevenueMath.VillageTariffScale(basis, 60, curve), 5);
            }
        }

        [Theory]
        [InlineData(1f)]
        [InlineData(2f)]
        [InlineData(4f)]
        [InlineData(10f)]
        public void TheTaxCurveMatchesItsDefinition(float curve)
        {
            const float knee = B1071_RevenueMath.TaxKneeProsperity;
            float span = B1071_RevenueMath.TaxReferenceProsperity - knee;

            foreach (float prosperity in new[] { 1_000f, 2_000f, 4_000f, 10_000f, 30_000f })
            {
                float expected = (float)(0.6d * Expected((prosperity - knee) / span, curve));
                Assert.Equal(expected, B1071_RevenueMath.TownTaxScale(prosperity, 60, curve), 5);
            }
        }

        /// <summary>
        /// The figures quoted in the MCM hint text. If the curve or a reference constant moves,
        /// the hints move with it or this fails.
        /// </summary>
        [Fact]
        public void TheDocumentedHintFiguresStillHold()
        {
            Assert.Equal(0.57f, TownTariff(3_000, 60, 2f), 2);
            Assert.Equal(0.49f, TownTariff(12_000, 60, 2f), 2);
            Assert.Equal(0.35f, TownTariff(48_000, 60, 2f), 2);
            Assert.Equal(0.30f, TownTariff(12_000, 60, 1f), 2);
            Assert.Equal(0.57f, TownTariff(12_000, 60, 4f), 2);

            Assert.Equal(0.57f, B1071_RevenueMath.VillageTariffScale(600, 60, 2f), 2);
            Assert.Equal(0.49f, B1071_RevenueMath.VillageTariffScale(2_500, 60, 2f), 2);
            Assert.Equal(0.20f, B1071_RevenueMath.VillageTariffScale(40_000, 60, 2f), 2);

            Assert.Equal(0.54f, B1071_RevenueMath.TownTaxScale(2_000f, 60, 2f), 2);
            Assert.Equal(0.45f, B1071_RevenueMath.TownTaxScale(6_000f, 60, 2f), 2);
            Assert.Equal(0.32f, B1071_RevenueMath.TownTaxScale(20_000f, 60, 2f), 2);
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.MaxValue)]
        public void HostileProsperityNeverProducesANaNOrAnAmplifiedScale(float prosperity)
        {
            float scale = B1071_RevenueMath.TownTaxScale(prosperity, 50, 2f);
            Assert.False(float.IsNaN(scale), "scale was NaN");
            Assert.InRange(scale, 0f, 0.5f);
        }

        /// <summary>
        /// A NaN or infinite curve must read as "leave revenue alone". The alternative is a NaN
        /// scale, and a NaN reaching ExplainedNumber would show the player a broken income line.
        /// </summary>
        [Theory]
        [InlineData(0f)]
        [InlineData(-1f)]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        public void ADegenerateCurveLeavesTheStrengthAsTheOnlyLimit(float curve)
        {
            // The curve contributes nothing, so the taper falls back to the strength alone. With a
            // full-strength setting that is exactly vanilla, which is the case that matters.
            Assert.Equal(1f, TownTariff(12_000, 100, curve));
            Assert.Equal(1f, B1071_RevenueMath.VillageTariffScale(2_500, 100, curve));
            Assert.Equal(1f, B1071_RevenueMath.TownTaxScale(10_000f, 100, curve));

            Assert.Equal(0.6f, TownTariff(12_000, 60, curve));
            Assert.Equal(0.6f, B1071_RevenueMath.VillageTariffScale(2_500, 60, curve));
            Assert.Equal(0.6f, B1071_RevenueMath.TownTaxScale(10_000f, 60, curve));
        }

        [Fact]
        public void HostileStrengthValuesAreClampedRatherThanTrusted()
        {
            Assert.Equal(1f, TownTariff(12_000, int.MaxValue, 2f));
            Assert.Equal(0f, TownTariff(12_000, int.MinValue, 2f));
            Assert.Equal(0f, B1071_RevenueMath.TownTaxScale(10_000f, 0, 2f));
        }

        /// <summary>
        /// A zero knee has to reproduce the no-knee taper exactly, because every knee setting
        /// defaults to zero and that default is what keeps an unconfigured install on the
        /// behaviour that was already reviewed.
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(3_000)]
        [InlineData(12_000)]
        [InlineData(48_000)]
        public void AZeroKneeIsTheSameAsPassingNoKneeAtAll(int basis)
        {
            Assert.Equal(TownTariff(basis, 60, 2f), TownTariff(basis, 60, 2f, 0));
            Assert.Equal(
                B1071_RevenueMath.VillageTariffScale(basis, 60, 2f),
                B1071_RevenueMath.VillageTariffScale(basis, 60, 2f, 0));
            Assert.Equal(
                B1071_RevenueMath.TownTaxScale(basis, 60, 2f),
                B1071_RevenueMath.TownTaxScale(basis, 60, 2f, 0));
        }

        /// <summary>
        /// The knee does not exempt anyone: at or below it a settlement keeps exactly the strength
        /// share, the same plateau as every other settlement. The knee's job is to make fiefs
        /// ABOVE it pay more than that. This pins the semantics against a silent flip into an
        /// interpolated, exemption-style form. Basis 0 is excluded here -- an empty pool is the
        /// one hard exemption (<see cref="AnEmptyTariffBasisIsUntouched"/>).
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(3_000)]
        [InlineData(6_000)]
        public void ATariffAtOrBelowItsKneeKeepsExactlyTheStrengthShare(int basis)
        {
            Assert.Equal(0.05f, TownTariff(basis, 5, 2f, 6_000));
            Assert.Equal(0.05f, B1071_RevenueMath.VillageTariffScale(basis, 5, 2f, 6_000));
        }

        /// <summary>
        /// Only the fixed 40-prosperity floor is a hard exemption; between the floor and the
        /// slider knee a town keeps exactly the strength share. Prosperity at or below 40 is
        /// covered by <see cref="TaxLeavesSmallSettlementsAlone"/>.
        /// </summary>
        [Theory]
        [InlineData(41f)]
        [InlineData(2_000f)]
        [InlineData(2_040f)]
        public void ATaxKeepsItsShareBetweenTheFixedFloorAndTheKnee(float prosperity)
        {
            Assert.Equal(0.05f, B1071_RevenueMath.TownTaxScale(prosperity, 5, 2f, 2_000));
        }

        /// <summary>
        /// A knee past the reference is capped at the reference: every slider value from 3,960 up
        /// produces the identical scale, and a town below the exemption keeps exactly the
        /// strength share -- the cap cannot manufacture an exemption that the formula does not
        /// give.
        /// </summary>
        [Fact]
        public void ATaxKneePastTheReferenceIsCappedAtIt()
        {
            // Only 40 of the 4,000 taxable prosperity points get the extra taper.
            float atReference = B1071_RevenueMath.TownTaxScale(4_040f, 60, 2f, 3_960);
            Assert.Equal(0.599985f, atReference, 6);

            foreach (int knee in new[] { 3_960, 5_000, 12_000, int.MaxValue })
            {
                Assert.Equal(atReference, B1071_RevenueMath.TownTaxScale(4_040f, 60, 2f, knee), 5);
                Assert.Equal(0.6f, B1071_RevenueMath.TownTaxScale(2_000f, 60, 2f, knee));
            }
        }

        /// <summary>
        /// Past the knee the EXTRA reduction fades in rather than stepping: the scale starts at
        /// the strength share exactly at the knee and declines smoothly from there. There is no
        /// cliff in the extra cut -- though note the plateau itself is the strength share, not
        /// vanilla: the knee does not exempt anyone.
        /// </summary>
        [Fact]
        public void TheKneeIsASmoothRampRatherThanACliff()
        {
            const int knee = 6_000;
            float first = TownTariff(6_001, 70, 2f, knee);
            Assert.True(first > 0.69f && first <= 0.70f, $"step at the knee was {first}");
            Assert.True(TownTariff(6_100, 70, 2f, knee) < 0.70f);

            float previous = TownTariff(knee, 70, 2f, knee);
            Assert.Equal(0.70f, previous, 5);
            for (int basis = knee + 1; basis <= 40_000; basis += 137)
            {
                float scale = TownTariff(basis, 70, 2f, knee);
                Assert.True(scale <= previous + 1e-5f, $"scale rose at basis {basis}");
                Assert.True(previous - scale < 0.05f, $"scale dropped in a step at basis {basis}");
                previous = scale;
            }
        }

        /// <summary>
        /// A knee still cannot buy a settlement more than vanilla, and it cannot invert the taper:
        /// a larger basis always pays less than or equal to a smaller one.
        /// </summary>
        [Fact]
        public void AKneeNeverExceedsTheStrengthCeiling()
        {
            for (int strength = 5; strength <= 100; strength += 5)
            {
                float share = B1071_RevenueMath.StrengthPercentToShare(strength);

                for (int knee = 0; knee <= 20_000; knee += 2_500)
                {
                    for (int basis = 1; basis <= 150_000; basis += 1_013)
                    {
                        Assert.InRange(TownTariff(basis, strength, 2f, knee), 0f, share);
                        Assert.InRange(
                            B1071_RevenueMath.VillageTariffScale(basis, strength, 2f, knee), 0f, share);
                    }

                    for (float prosperity = 41f; prosperity <= 30_000f; prosperity += 149f)
                    {
                        Assert.InRange(
                            B1071_RevenueMath.TownTaxScale(prosperity, strength, 2f, knee), 0f, share);
                    }
                }
            }
        }

        /// <summary>
        /// Multiplicative semantics: strength is what a fief keeps at the plateau, and the curve
        /// keeps taking more past the reference without bound. Pins the ABSENCE of a cap so the
        /// semantics cannot silently flip into an interpolated, strength-capped form.
        /// </summary>
        [Fact]
        public void TheCutIsNotCappedByTheStrengthSetting()
        {
            Assert.True(TownTariff(5_000_000, 70, 2f) < 0.35f);
            Assert.True(B1071_RevenueMath.VillageTariffScale(5_000_000, 70, 2f) < 0.35f);
            Assert.True(B1071_RevenueMath.TownTaxScale(1_000_000f, 70, 2f) < 0.35f);
        }

        /// <summary>
        /// A negative knee would make the taper bite harder than leaving the slider at zero, which
        /// is the opposite of what a threshold is for, so it reads as zero.
        /// </summary>
        [Fact]
        public void ANegativeKneeIsReadAsZero()
        {
            Assert.Equal(TownTariff(12_000, 60, 2f), TownTariff(12_000, 60, 2f, -5_000));
            Assert.Equal(
                B1071_RevenueMath.VillageTariffScale(2_500, 60, 2f),
                B1071_RevenueMath.VillageTariffScale(2_500, 60, 2f, int.MinValue));
            Assert.Equal(
                B1071_RevenueMath.TownTaxScale(10_000f, 60, 2f),
                B1071_RevenueMath.TownTaxScale(10_000f, 60, 2f, int.MinValue));
        }

        /// <summary>
        /// The worked example quoted in the town tariff knee hint. If the curve or a reference
        /// moves, the hint moves with it or this fails.
        /// </summary>
        [Fact]
        public void TheDocumentedKneeExampleStillHolds()
        {
            Assert.Equal(0.70f, TownTariff(6_000, 70, 2f, 6_000), 2);
            Assert.Equal(0.49f, TownTariff(40_000, 70, 2f, 6_000), 2);
            Assert.Equal(0.70f, TownTariff(3_000, 70, 2f, 6_000), 2);
        }

        /// <summary>
        /// The slider floor is 1.00, but a hand-edited settings file could present far less, and
        /// <c>float.Epsilon</c> makes the exponent enormous. The result must stay a usable number
        /// rather than tipping into NaN or infinity on its way to ExplainedNumber.
        /// </summary>
        [Theory]
        [InlineData(0.001f)]
        [InlineData(float.Epsilon)]
        public void ATinyCurveStillProducesAUsableNumber(float curve)
        {
            float scale = TownTariff(12_000, 60, curve);
            Assert.False(float.IsNaN(scale), "scale was NaN");
            Assert.False(float.IsInfinity(scale), "scale was infinite");
            Assert.InRange(scale, 0f, 0.6f);
        }
    }
}
