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
