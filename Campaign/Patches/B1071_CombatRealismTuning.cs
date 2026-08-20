using System;
using Byzantium1071.Campaign.Settings;

namespace Byzantium1071.Campaign.Patches
{
    /// <summary>
    /// Single source of truth for the elite-survivability tuning curve.
    ///
    /// Two separate systems reward troop tier in autoresolve, and they compound:
    ///   • B1071_TierArmorSimulationPatch reduces simulated hit damage, which
    ///     lowers how OFTEN the fatal-hit gate fires.
    ///   • B1071_FatalityPatch raises survival chance, which decides kill-vs-wound
    ///     WHEN the gate does fire.
    /// Tuned independently they double-dip: before v1.0.2.5 a T6 troop took ~24%
    /// fewer fatal-hit checks AND survived far more of the checks it got, landing
    /// elite death rates somewhere near a third of vanilla. Players reported elite
    /// troops as effectively unkillable.
    ///
    /// Both curves now come from one preset so they can only move together.
    ///   0 = Vanilla   1 = Light (default)   2 = Moderate   3 = Strong
    /// Preset 3 reproduces the pre-v1.0.2.5 values for players who want them back.
    ///
    /// Applies to AI and player equally. T1-T2 never receive a bonus at any preset.
    /// </summary>
    internal static class B1071_CombatRealismTuning
    {
        /// <summary>Highest preset index; also the count bound for the tables below.</summary>
        private const int MaxPreset = 3;

        /// <summary>Tier index bound — tables are T1..T6, anything above T6 clamps to T6.</summary>
        private const int MaxTierIndex = 5;

        // [preset][tier-1] — negative multiplicative factor fed to ExplainedNumber.AddFactor.
        private static readonly float[][] _armorFactors =
        {
            new[] { 0f, 0f,  0f,     0f,     0f,     0f     }, // 0 Vanilla
            new[] { 0f, 0f, -0.03f, -0.06f, -0.09f, -0.12f }, // 1 Light
            new[] { 0f, 0f, -0.05f, -0.10f, -0.15f, -0.20f }, // 2 Moderate
            new[] { 0f, 0f, -0.06f, -0.12f, -0.18f, -0.24f }, // 3 Strong (pre-v1.0.2.5)
        };

        // [preset][tier-1] — flat addition to GetSurvivalChance (0.05f = +5 percentage points).
        private static readonly float[][] _survivalBonuses =
        {
            new[] { 0f, 0f, 0f,    0f,    0f,    0f    }, // 0 Vanilla
            new[] { 0f, 0f, 0.02f, 0.04f, 0.06f, 0.08f }, // 1 Light
            new[] { 0f, 0f, 0.03f, 0.06f, 0.09f, 0.12f }, // 2 Moderate
            new[] { 0f, 0f, 0.05f, 0.10f, 0.15f, 0.20f }, // 3 Strong (pre-v1.0.2.5)
        };

        /// <summary>Reads the preset, clamped so a hand-edited config can never index out of range.</summary>
        private static int Preset
        {
            get
            {
                int p = (B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults).EliteSurvivabilityPreset;
                return Math.Max(0, Math.Min(MaxPreset, p));
            }
        }

        /// <summary>Negative damage factor for a struck troop of this tier. 0f means "leave vanilla alone".</summary>
        internal static float GetArmorFactor(int tier) => Lookup(_armorFactors, tier);

        /// <summary>Flat survival-chance bonus for a troop of this tier. 0f means "leave vanilla alone".</summary>
        internal static float GetSurvivalBonus(int tier) => Lookup(_survivalBonuses, tier);

        private static float Lookup(float[][] table, int tier)
        {
            int tierIdx = Math.Max(0, Math.Min(MaxTierIndex, tier - 1));
            return table[Preset][tierIdx];
        }
    }
}
