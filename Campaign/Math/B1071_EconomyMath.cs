using System;

namespace Byzantium1071.Campaign
{
    internal static class B1071_EconomyMath
    {
        internal const int SlaveBaseValue = 300;

        private static readonly float[][] HireFactors =
        {
            new[] { 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f },
            new[] { 0.00f, 0.15f, 0.35f, 0.65f, 1.00f, 1.50f },
            new[] { 0.10f, 0.30f, 0.75f, 1.50f, 2.50f, 4.00f },
            new[] { 0.25f, 0.75f, 1.75f, 3.50f, 6.00f, 10.0f }
        };

        private static readonly float[] ForeignHireFactors = { 0.00f, 0.50f, 1.00f, 2.00f };

        private static readonly float[][] WageFactors =
        {
            new[] { 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f },
            new[] { 0.00f, 0.00f, 0.20f, 0.40f, 0.70f, 1.00f },
            new[] { 0.00f, 0.10f, 0.50f, 1.00f, 1.60f, 2.50f },
            new[] { 0.10f, 0.25f, 0.70f, 1.50f, 2.50f, 4.00f }
        };

        private static readonly float[][] ArmorFactors =
        {
            new[] { 0f, 0f, 0f, 0f, 0f, 0f },
            new[] { 0f, 0f, -0.03f, -0.06f, -0.09f, -0.12f },
            new[] { 0f, 0f, -0.05f, -0.10f, -0.15f, -0.20f },
            new[] { 0f, 0f, -0.06f, -0.12f, -0.18f, -0.24f }
        };

        private static readonly float[][] SurvivalBonuses =
        {
            new[] { 0f, 0f, 0f, 0f, 0f, 0f },
            new[] { 0f, 0f, 0.02f, 0.04f, 0.06f, 0.08f },
            new[] { 0f, 0f, 0.03f, 0.06f, 0.09f, 0.12f },
            new[] { 0f, 0f, 0.05f, 0.10f, 0.15f, 0.20f }
        };

        internal static float HireFactor(int preset, int tier) => LookupDisabledWhenInvalid(HireFactors, preset, tier);

        internal static float ForeignHireFactor(int preset)
        {
            return preset > 0 && preset < ForeignHireFactors.Length
                ? ForeignHireFactors[preset]
                : 0f;
        }

        internal static float WageFactor(int preset, int tier) => LookupDisabledWhenInvalid(WageFactors, preset, tier);

        internal static int AdjustedWage(int vanillaWage, int preset, int tier)
        {
            float factor = WageFactor(preset, tier);
            return factor == 0f
                ? vanillaWage
                : Math.Max(1, (int)Math.Round(vanillaWage * (1f + factor)));
        }

        internal static float GarrisonWageAddFactor(int wagePercent) => wagePercent / 100f - 1f;

        internal static float ArmorFactor(int preset, int tier) => LookupClampedPreset(ArmorFactors, preset, tier);

        internal static float SurvivalBonus(int preset, int tier) => LookupClampedPreset(SurvivalBonuses, preset, tier);

        /// <summary>
        /// Tier whose power is held at exactly vanilla. Every other tier moves relative to it,
        /// which keeps a typical party's TOTAL power near its vanilla value instead of inflating
        /// it. That matters because several vanilla AI gates compare power against hard-coded
        /// absolute constants calibrated to the vanilla scale -- CanLordCreateArmy needs a summed
        /// GetCustomStrength of 1000, and DefaultDiplomacyModel refuses war below a
        /// CurrentTotalStrength of 500. Inflating every tier would silently shift how often AI
        /// kingdoms raise armies and declare war. Tier 3 is the usual mid-point of a lord's roster.
        /// </summary>
        private const int PowerReferenceTier = 3;

        /// <summary>
        /// Share of the survivability gain that becomes visible power. Tougher troops live longer
        /// but do not hit harder, so durability is worth roughly half of a troop's combat value.
        /// It only sets how wide the tier spread is; what protects the absolute gates described
        /// above is the normalisation in PowerFactor, not this constant.
        /// </summary>
        private const float PowerDamping = 0.5f;

        /// <summary>
        /// AI-visible power multiplier for a troop of this tier, derived from the SAME preset
        /// curves that make the troop harder to kill (see ArmorFactors and SurvivalBonuses), so
        /// the two can never drift apart. Preset 0 returns exactly 1f for every tier.
        ///
        /// Vanilla prices a troop purely by tier -- DefaultMilitaryPowerModel.GetDefaultTroopPower
        /// is (2 + tier) * (10 + tier) * 0.02f -- and Campaign++ never changes a troop's tier, only
        /// how much punishment that tier absorbs. Without this the AI keeps valuing elite stacks at
        /// vanilla worth while they fight far above it.
        /// </summary>
        internal static float PowerFactor(int preset, int tier)
        {
            float reference = RawPowerMultiplier(preset, PowerReferenceTier);
            if (reference <= 0f)
            {
                return 1f;
            }

            return RawPowerMultiplier(preset, tier) / reference;
        }

        /// <summary>
        /// Undamped, un-normalised survivability multiplier for a tier. A troop enters the fatal
        /// gate less often when its damage taken drops (ArmorFactor, negative) and walks away from
        /// more of the gates it does enter (SurvivalBonus), so the two compound into a death rate
        /// of (1 + armor) * (1 - survival). The reciprocal is how much longer the troop lasts.
        /// </summary>
        private static float RawPowerMultiplier(int preset, int tier)
        {
            float deathRate = (1f + ArmorFactor(preset, tier)) * (1f - SurvivalBonus(preset, tier));
            if (deathRate <= 0f)
            {
                return 1f;
            }

            return 1f + PowerDamping * (1f / deathRate - 1f);
        }

        internal static float SlavePriceFactor(
            float inStoreValue,
            bool isSelling,
            int transferValue,
            float decayRate)
        {
            float effectiveValue = inStoreValue;
            if (isSelling)
            {
                effectiveValue += transferValue;
            }

            int stock = (int)(effectiveValue / SlaveBaseValue);
            float factor = (float)Math.Pow(decayRate, stock);
            return Math.Max(0.1f, Math.Min(10f, factor));
        }

        private static float LookupDisabledWhenInvalid(float[][] table, int preset, int tier)
        {
            if (preset <= 0 || preset >= table.Length)
            {
                return 0f;
            }

            return table[preset][TierIndex(tier)];
        }

        private static float LookupClampedPreset(float[][] table, int preset, int tier)
        {
            int clampedPreset = Math.Max(0, Math.Min(table.Length - 1, preset));
            return table[clampedPreset][TierIndex(tier)];
        }

        private static int TierIndex(int tier) => Math.Max(0, Math.Min(5, tier - 1));
    }
}
