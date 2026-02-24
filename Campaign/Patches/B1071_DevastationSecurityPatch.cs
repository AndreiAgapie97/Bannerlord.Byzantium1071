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
    /// Devastation → Town/Castle security penalty.
    ///
    /// Postfix on DefaultSettlementSecurityModel.CalculateSecurityChange(Town, bool).
    ///
    /// Averages the devastation across all bound villages and applies a proportional
    /// security penalty to the town/castle, representing bandits, refugees from
    /// destroyed villages, and general frontier lawlessness.
    ///
    /// At avg devastation 50:  -0.75 security/day
    /// At avg devastation 100: -1.5 security/day (configurable via DevastationMaxSecurityPenalty)
    ///
    /// This is a SEPARATE Postfix from B1071_GovernanceSecurityPatch. Multiple
    /// Harmony Postfixes on CalculateSecurityChange execute in order.
    /// </summary>
    [HarmonyPatch(typeof(DefaultSettlementSecurityModel), "CalculateSecurityChange")]
    public static class B1071_DevastationSecurityPatch
    {
        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        private static readonly TextObject _label = new TextObject("{=b1071_dev_frontier}Frontier Devastation");

        [HarmonyPostfix]
        public static void Postfix(Town town, ref ExplainedNumber __result)
        {
            try
            {
                if (!Settings.EnableFrontierDevastation) return;

                var behavior = B1071_DevastationBehavior.Instance;
                if (behavior == null || town == null) return;

                float avgDev = behavior.GetAverageBoundVillageDevastation(town);
                if (avgDev <= 0f) return;

                float ratio = avgDev / 100f;
                float penalty = -(ratio * Settings.DevastationMaxSecurityPenalty);

                __result.Add(penalty, _label);
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][DevastationSecurity] Error: {ex.Message}");
            }
        }
    }
}
