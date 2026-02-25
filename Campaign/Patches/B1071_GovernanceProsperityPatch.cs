using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using HarmonyLib;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.Patches
{
    /// <summary>
    /// Applies governance strain as a daily prosperity penalty.
    /// At strain 50/100: -0.5/day. At strain 100/100: -1.0/day.
    /// Appears as "Governance Strain" in the prosperity tooltip.
    ///
    /// Verified: DefaultSettlementProsperityModel.CalculateProsperityChange(Town, bool)
    /// returns ExplainedNumber — Postfix modifies via ref __result. v1.3.15.
    ///
    /// NOTE: This is a SEPARATE Postfix from B1071_SlaveProsperityPatch.
    /// Multiple Harmony Postfixes on the same method are executed in order
    /// and each sees the accumulated ExplainedNumber. No conflict.
    /// </summary>
    [HarmonyPatch(typeof(DefaultSettlementProsperityModel), nameof(DefaultSettlementProsperityModel.CalculateProsperityChange))]
    public static class B1071_GovernanceProsperityPatch
    {
        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        private static readonly TextObject _label = new TextObject("{=b1071_gov_strain}Governance Strain");

        public static void Postfix(Town fortification, ref ExplainedNumber __result)
        {
            try
            {
                if (!Settings.EnableGovernanceStrain) return;
                if (fortification == null) return;

                var behavior = B1071_GovernanceBehavior.Instance;
                if (behavior == null) return;

                float strain = behavior.GetStrainForTown(fortification);
                if (strain <= 0f) return;

                float cap = Math.Max(1f, Settings.GovernanceStrainCap);
                float penalty = -(strain / cap) * Settings.GovernanceMaxProsperityPenalty;

                // Apply combined B1071 prosperity penalty cap (G-1).
                penalty = B1071_ProsperityPenaltyCapPatch.ClampPenalty(penalty);

                if (penalty < 0f)
                    __result.Add(penalty, _label);
            }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print($"[Byzantium1071] GovernanceProsperityPatch error: {ex}");
            }
        }
    }
}
