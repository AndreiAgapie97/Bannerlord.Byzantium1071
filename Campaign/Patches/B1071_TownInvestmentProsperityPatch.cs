using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using HarmonyLib;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using Debug = TaleWorlds.Library.Debug;

namespace Byzantium1071.Campaign.Patches
{
    /// <summary>
    /// Civic Patronage → Town prosperity growth bonus.
    ///
    /// Postfix on DefaultSettlementProsperityModel.CalculateProsperityChange(Town, bool).
    ///
    /// When one or more lords (player or AI) have an active civic patronage investment
    /// in a town, this patch adds a positive prosperity modifier proportional to the
    /// combined investment bonuses. The amounts are configured per-tier in MCM.
    ///
    /// Only active when EnableTownInvestment is true and the behavior instance is
    /// available (i.e., mid-campaign load-safety guaranteed).
    ///
    /// This is a SEPARATE Postfix from the existing prosperity patches
    /// (B1071_DevastationProsperityPatch, B1071_GovernanceProsperityPatch,
    /// B1071_SlaveProsperityPatch). Multiple Harmony Postfixes on
    /// CalculateProsperityChange are executed in order — each one appends its own
    /// ExplainedNumber line.
    /// </summary>
    [HarmonyPatch(typeof(DefaultSettlementProsperityModel), "CalculateProsperityChange")]
    public static class B1071_TownInvestmentProsperityPatch
    {
        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        private static readonly TextObject _label = new TextObject("{=b1071_ti_civic}Civic Patronage");

        [HarmonyPostfix]
        public static void Postfix(Town fortification, ref ExplainedNumber __result)
        {
            try
            {
                if (!Settings.EnableTownInvestment) return;

                var behavior = B1071_TownInvestmentBehavior.Instance;
                if (behavior == null || fortification == null) return;

                float bonus = behavior.GetActiveProsperityBonus(fortification);
                if (bonus <= 0f) return;

                __result.Add(bonus, _label);
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][TownInvestmentProsperity] Error: {ex.Message}");
            }
        }
    }
}
