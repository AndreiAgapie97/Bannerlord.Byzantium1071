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
        /// <summary>Negative damage factor for a struck troop of this tier. 0f means "leave vanilla alone".</summary>
        internal static float GetArmorFactor(int tier)
        {
            IB1071Settings settings = B1071_TestHooks.Settings
                ?? B1071_McmSettings.Instance
                ?? B1071_McmSettings.Defaults;
            return B1071_EconomyMath.ArmorFactor(settings.EliteSurvivabilityPreset, tier);
        }

        /// <summary>Flat survival-chance bonus for a troop of this tier. 0f means "leave vanilla alone".</summary>
        internal static float GetSurvivalBonus(int tier)
        {
            IB1071Settings settings = B1071_TestHooks.Settings
                ?? B1071_McmSettings.Instance
                ?? B1071_McmSettings.Defaults;
            return B1071_EconomyMath.SurvivalBonus(settings.EliteSurvivabilityPreset, tier);
        }
    }
}
