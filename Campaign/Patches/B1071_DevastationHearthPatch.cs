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
    /// Devastation → Hearth penalty.
    ///
    /// Postfix on DefaultSettlementProsperityModel.CalculateHearthChange(Village, bool).
    ///
    /// A devastated village loses hearth growth proportional to its devastation level,
    /// representing population flight, destroyed homes, and disrupted agriculture.
    ///
    /// At devastation 50:  -1.0 hearth/day
    /// At devastation 100: -2.0 hearth/day (configurable via DevastationMaxHearthPenalty)
    ///
    /// Only active when EnableFrontierDevastation is true and DevastationBehavior.Instance
    /// is available (i.e., mid-campaign load-safety guaranteed).
    /// </summary>
    [HarmonyPatch(typeof(DefaultSettlementProsperityModel), "CalculateHearthChange")]
    public static class B1071_DevastationHearthPatch
    {
        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        private static readonly TextObject _label = new TextObject("{=b1071_dev_frontier}Frontier Devastation");

        [HarmonyPostfix]
        public static void Postfix(Village village, ref ExplainedNumber __result)
        {
            try
            {
                if (!Settings.EnableFrontierDevastation) return;

                var behavior = B1071_DevastationBehavior.Instance;
                if (behavior == null || village == null) return;

                float dev = behavior.GetDevastation(village);
                if (dev <= 0f) return;

                float ratio = dev / 100f;
                float penalty = -(ratio * Settings.DevastationMaxHearthPenalty);

                __result.Add(penalty, _label);
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][DevastationHearth] Error: {ex.Message}");
            }
        }
    }
}
