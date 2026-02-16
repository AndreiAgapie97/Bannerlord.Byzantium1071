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
    /// Devastation → Town food supply penalty.
    ///
    /// Postfix on DefaultSettlementFoodModel.CalculateTownFoodStocksChange(Town, bool, bool).
    ///
    /// Each bound village with devastation contributes less food to the town.
    /// The penalty is proportional: at devastation 100, the village's ENTIRE food
    /// contribution is effectively wiped out (configurable scale factor).
    ///
    /// Calculation per bound village with devastation > 0:
    ///   penalty = -(devastation / 100) × base_village_food_output × 0.50
    ///
    /// The base_village_food_output is estimated from vanilla Village.TradeBound
    /// food production. A scale factor of 0.5 means at dev 100, the village
    /// contributes 50% less food to the town.
    ///
    /// This models how ravaged farmland and displaced peasants reduce the
    /// agricultural output flowing into the bound settlement.
    /// </summary>
    [HarmonyPatch(typeof(DefaultSettlementFoodModel), "CalculateTownFoodStocksChange")]
    public static class B1071_DevastationFoodPatch
    {
        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        [HarmonyPostfix]
        public static void Postfix(Town town, ref ExplainedNumber __result)
        {
            try
            {
                if (!Settings.EnableFrontierDevastation) return;

                var behavior = B1071_DevastationBehavior.Instance;
                if (behavior == null || town?.Settlement == null) return;

                var villages = town.Settlement.BoundVillages;
                if (villages == null || villages.Count == 0) return;

                float totalPenalty = 0f;
                foreach (Village v in villages)
                {
                    float dev = behavior.GetDevastation(v);
                    if (dev <= 0f) continue;

                    float ratio = dev / 100f;
                    // Estimate per-village food contribution from hearths:
                    // Vanilla formula: each village contributes roughly Hearth * factor to town food.
                    // Rather than recalculate the exact vanilla value, we apply a flat per-village
                    // penalty that scales with devastation — simpler and more predictable.
                    totalPenalty += ratio * Settings.DevastationMaxFoodPenaltyPerVillage;
                }

                if (totalPenalty <= 0f) return;

                __result.Add(-totalPenalty, new TaleWorlds.Localization.TextObject("Devastated Villages"));
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][DevastationFood] Error: {ex.Message}");
            }
        }
    }
}
