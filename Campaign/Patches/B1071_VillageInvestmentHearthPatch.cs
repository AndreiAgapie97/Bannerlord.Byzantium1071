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
    /// Patronage → Hearth growth bonus.
    ///
    /// Postfix on DefaultSettlementProsperityModel.CalculateHearthChange(Village, bool).
    ///
    /// When one or more lords (player or AI) have an active patronage investment in a
    /// village, this patch adds a positive hearth-growth modifier proportional to the
    /// combined investment bonuses. The amounts are configured per-tier in MCM.
    ///
    /// Only active when EnableVillageInvestment is true and the behavior instance is
    /// available (i.e., mid-campaign load-safety guaranteed).
    /// </summary>
    [HarmonyPatch(typeof(DefaultSettlementProsperityModel), "CalculateHearthChange")]
    public static class B1071_VillageInvestmentHearthPatch
    {
        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        private static readonly TextObject _label = new TextObject("{=b1071_vi_patronage}Patronage");

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Village village, ref ExplainedNumber __result)
        {
            try
            {
                if (!Settings.EnableVillageInvestment) return;

                var behavior = B1071_VillageInvestmentBehavior.Instance;
                if (behavior == null || village == null) return;

                float bonus = behavior.GetActiveHearthBonus(village);
                if (bonus <= 0f) return;

                __result.Add(bonus, _label);
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][VillageInvestmentHearth] Error: {ex.Message}");
            }
        }
    }
}
