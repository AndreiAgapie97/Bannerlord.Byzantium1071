using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using HarmonyLib;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace Byzantium1071.Campaign.Patches
{
    /// <summary>
    /// Manpower → volunteer production gating.
    ///
    /// Postfix on DefaultVolunteerModel.GetDailyVolunteerProductionProbability.
    ///
    /// Scales the probability that a notable produces a volunteer by the
    /// settlement's manpower ratio (current / max). Empty manpower → zero
    /// volunteers. Full manpower → unchanged.
    ///
    /// Compatible with any mod that replaces the VolunteerModel via AddModel,
    /// as long as the replacement inherits from DefaultVolunteerModel and does
    /// not override GetDailyVolunteerProductionProbability itself (in which
    /// case the Postfix still fires after the override).
    ///
    /// Replaces the old B1071_ManpowerVolunteerModel (AddModel approach) which
    /// caused last-loaded-wins conflicts with mods like EconomyOverhaul.
    /// </summary>
    [HarmonyPatch(typeof(DefaultVolunteerModel), nameof(DefaultVolunteerModel.GetDailyVolunteerProductionProbability))]
    public static class B1071_ManpowerVolunteerPatch
    {
        private static B1071_McmSettings Settings
            => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        [HarmonyPostfix]
        public static void Postfix(Hero hero, int index, Settlement settlement, ref float __result)
        {
            try
            {
                var mp = B1071_ManpowerBehavior.Instance;
                if (mp == null) return;

                float ratio = mp.GetManpowerRatio(settlement);
                // Low manpower → fewer volunteers appear. Zero manpower → none.
                __result *= ratio;

                // WP4: bounded stochastic variance on volunteer production.
                if (Settings.EnableRecruitmentVariance && Settings.VolunteerVariancePercent > 0)
                {
                    float spread = Math.Min(Settings.VolunteerVariancePercent, 100f) / 100f;
                    float factor = MBRandom.RandomFloatRanged(1f - spread, 1f + spread);
                    __result *= factor;
                }

                __result = Math.Max(0f, __result);
            }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print($"[Byzantium1071][ManpowerVolunteer] Error: {ex.Message}");
            }
        }
    }
}
