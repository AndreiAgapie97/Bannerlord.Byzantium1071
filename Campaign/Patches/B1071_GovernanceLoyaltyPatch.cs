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
    /// Applies governance strain as a daily loyalty penalty.
    /// At strain 50/100: -1.5/day. At strain 100/100: -3.0/day.
    /// Appears as "Governance Strain" in the loyalty tooltip.
    ///
    /// Verified: DefaultSettlementLoyaltyModel.CalculateLoyaltyChange(Town, bool)
    /// returns ExplainedNumber — Postfix modifies via ref __result. v1.4.5 (unchanged since v1.3.15).
    /// </summary>
    [HarmonyPatch(typeof(DefaultSettlementLoyaltyModel), nameof(DefaultSettlementLoyaltyModel.CalculateLoyaltyChange))]
    public static class B1071_GovernanceLoyaltyPatch
    {
        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        private static readonly TextObject _label = new TextObject("{=b1071_gov_strain}Governance Strain");
        private static readonly TextObject _stabilizationLabel = new TextObject("{=b1071_gov_stabilization}Provincial Stabilization");

        public static void Postfix(Town town, ref ExplainedNumber __result)
        {
            try
            {
                if (!Settings.EnableGovernanceStrain) return;
                if (town == null) return;

                var behavior = B1071_GovernanceBehavior.Instance;
                if (behavior == null) return;

                float strain = behavior.GetStrainForTown(town);
                if (strain > 0f)
                {
                    float cap = Math.Max(1f, Settings.GovernanceStrainCap);
                    float penalty = -(strain / cap) * Settings.GovernanceMaxLoyaltyPenalty;

                    if (penalty < 0f)
                        __result.Add(penalty, _label);
                }

                float recovery = B1071_GovernanceStabilizationBehavior.Instance?.GetActiveLoyaltyBonus(town) ?? 0f;
                if (recovery > 0f)
                    __result.Add(recovery, _stabilizationLabel);
            }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print($"[Byzantium1071] GovernanceLoyaltyPatch error: {ex}");
            }
        }
    }
}
