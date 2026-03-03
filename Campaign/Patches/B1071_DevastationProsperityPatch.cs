using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using HarmonyLib;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.Patches
{
    /// <summary>
    /// Devastation → Town/Castle prosperity penalty.
    ///
    /// Postfix on DefaultSettlementProsperityModel.CalculateProsperityChange(Town, bool).
    ///
    /// Averages the devastation across all bound villages and applies a proportional
    /// prosperity penalty to the town/castle, representing disrupted supply chains
    /// and reduced agricultural output feeding the settlement.
    ///
    /// At avg devastation 50:  -1.0 prosperity/day
    /// At avg devastation 100: -2.0 prosperity/day (configurable via DevastationMaxProsperityPenalty)
    ///
    /// This is a SEPARATE Postfix from B1071_GovernanceProsperityPatch and
    /// B1071_SlaveProsperityPatch. Multiple Harmony Postfixes on CalculateProsperityChange
    /// are executed in order — each one appends its own ExplainedNumber line.
    /// </summary>
    [HarmonyPatch(typeof(DefaultSettlementProsperityModel), "CalculateProsperityChange")]
    public static class B1071_DevastationProsperityPatch
    {
        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        private static readonly TextObject _label = new TextObject("{=b1071_dev_hinterlands}Devastated Hinterlands");

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Town fortification, ref ExplainedNumber __result)
        {
            try
            {
                if (!Settings.EnableFrontierDevastation) return;

                var behavior = B1071_DevastationBehavior.Instance;
                if (behavior == null || fortification == null) return;

                float avgDev = behavior.GetAverageBoundVillageDevastation(fortification);
                if (avgDev <= 0f) return;

                float ratio = avgDev / 100f;
                float penalty = -(ratio * Settings.DevastationMaxProsperityPenalty);

                // Apply combined B1071 prosperity penalty cap (G-1).
                penalty = B1071_ProsperityPenaltyCapPatch.ClampPenalty(penalty);

                if (penalty < 0f)
                    __result.Add(penalty, _label);
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][DevastationProsperity] Error: {ex.Message}");
            }
        }
    }
}
