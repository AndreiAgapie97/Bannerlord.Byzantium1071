using System;

namespace Byzantium1071.Campaign
{
    /// <summary>
    /// Progressive taper for settlement revenue. Strength is the maximum share retained;
    /// 100% bypasses the taper entirely. At or below the knee, the strength share applies
    /// uniformly. Above it, only the excess receives an additional curved reduction:
    ///
    ///   excess = max(0, basis - knee)
    ///   payout = strength * (min(basis, knee) + excess * taperCurve(excess / reference))
    ///   taperCurve(r) = (1 + r / curve)^(-1 / curve)
    ///
    /// This keeps total payouts nondecreasing for every allowed curve (1..10), even when
    /// the knee exceeds the reference. Curve 1 approaches a finite payout; larger curves
    /// keep growing sublinearly. With knee zero, the original tariff curve is unchanged.
    /// </summary>
    internal static class B1071_RevenueMath
    {
        /// <summary>
        /// Trade basis, in denars per day, at which a town's scale is
        /// strength * (1 + 1/curve)^(-1/curve) -- about 81.7% of its strength at
        /// curve 2. The basis handed in is already divided by RevenueSmoothenFraction, so
        /// this reads as "a town whose trade earns about 12,000 a day".
        /// </summary>
        internal const float TownTariffReference = 12000f;

        /// <summary>
        /// As above for villages. Villages see far less traffic than the town they are bound to,
        /// which is why their reference is roughly a fifth of a town's.
        /// </summary>
        internal const float VillageTariffReference = 2500f;

        /// <summary>
        /// Prosperity, above <see cref="TaxKneeProsperity"/>, at which a town's scale is
        /// strength * (1 + 1/curve)^(-1/curve) -- about 81.7% of its strength at
        /// curve 2. Below <see cref="TaxKneeProsperity"/> the taper is switched off entirely
        /// rather than merely small. Villages use the separate tariff calculation.
        /// </summary>
        internal const float TaxReferenceProsperity = 4000f;

        /// <summary>
        /// Settlement size, in prosperity, below which tax is left completely alone. This is the
        /// floor the MCM knee slider adds to; it is not player-facing on its own because no town
        /// is ever that small, and a tax taper that reached down to one is not a setting anyone
        /// asked for.
        /// </summary>
        internal const float TaxKneeProsperity = 40f;

        /// <summary>
        /// Share of the vanilla amount a town or village still pays, given the trade basis it is
        /// earning. 1 means untouched.
        /// </summary>
        internal static float TownTariffScale(int basis, int strengthPercent, float curve, int knee = 0) =>
            BasisScale(basis, TownTariffReference, strengthPercent, curve, knee);

        /// <summary>
        /// As <see cref="TownTariffScale"/>, for a village's smaller pool.
        /// </summary>
        internal static float VillageTariffScale(int basis, int strengthPercent, float curve, int knee = 0) =>
            BasisScale(basis, VillageTariffReference, strengthPercent, curve, knee);

        /// <summary>
        /// Share of the vanilla amount a town still pays, given the prosperity its tax is
        /// calculated from. The fixed floor is removed from the basis before applying the knee.
        /// Preserve the existing effective threshold cap of 4,000 prosperity (40 + 3,960).
        /// </summary>
        internal static float TownTaxScale(float prosperity, int strengthPercent, float curve, int knee = 0)
        {
            if (prosperity <= TaxKneeProsperity)
            {
                return 1f;
            }

            float ceiling = Math.Max(0f, TaxReferenceProsperity - TaxKneeProsperity);
            float protectedBasis = Math.Min(Math.Max(0, knee), ceiling);
            return ScaleAboveKnee(prosperity - TaxKneeProsperity, Math.Max(1f, ceiling),
                StrengthPercentToShare(strengthPercent), curve, protectedBasis);
        }

        /// <summary>
        /// Turns the MCM percentage into the share the curve multiplies by. Clamped to [0, 1], so a
        /// hand-edited settings file asking for more than 100% is read as "leave it alone" rather
        /// than as a licence to pay a settlement more than vanilla.
        ///
        /// The share is a ceiling: the extra taper above the knee can only reduce it.
        /// </summary>
        internal static float StrengthPercentToShare(int strengthPercent)
        {
            if (strengthPercent >= 100) return 1f;
            if (strengthPercent <= 0) return 0f;
            return strengthPercent / 100f;
        }

        private static float BasisScale(int basis, float reference, int strengthPercent, float curve, int knee)
        {
            if (basis <= 0 || reference <= 0f)
            {
                return 1f;
            }

            return ScaleAboveKnee(basis, reference, StrengthPercentToShare(strengthPercent),
                curve, Math.Max(0, knee));
        }

        private static float ScaleAboveKnee(float basis, float reference, float strengthShare, float curve, float knee)
        {
            if (strengthShare >= 1f) return 1f;
            if (!(basis > 0f)) return strengthShare;

            // Reserve the portion below the knee at the strength share. Tapering the whole
            // amount by a ratio measured only above the knee can reverse total income growth.
            float protectedShare = Math.Min(1f, knee / basis);
            float excess = Math.Max(0f, basis - knee);
            return strengthShare * (protectedShare + (1f - protectedShare) * TaperCurve(excess / reference, curve));
        }

        /// <summary>
        /// The taper itself. Returns exactly 1 for a non-positive ratio or curve, which keeps an
        /// untouched settlement -- or a slider left at its floor -- on the vanilla path instead of
        /// dividing by it.
        /// </summary>
        private static float TaperCurve(float ratio, float curve)
        {
            // The curve test is written as a negated comparison on purpose. "curve <= 0f" is false
            // for NaN, so a corrupted settings file would sail past it and produce a NaN scale,
            // which would reach ExplainedNumber as a broken income line. "!(curve > 0f)" rejects
            // NaN and non-positive values. Positive infinity gives the neutral curve.
            if (ratio <= 0f || float.IsNaN(ratio) || !(curve > 0f))
            {
                return 1f;
            }

            double denominator = Math.Pow(1d + ratio / curve, 1d / curve);
            if (double.IsNaN(denominator) || double.IsInfinity(denominator) || denominator <= 0d)
            {
                return 1f;
            }

            return (float)(1d / denominator);
        }
    }
}
