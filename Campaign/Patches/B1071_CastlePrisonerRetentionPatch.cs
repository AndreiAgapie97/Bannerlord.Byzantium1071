using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Settlements;

namespace Byzantium1071.Campaign.Patches
{
    /// <summary>
    /// Prevents the vanilla <see cref="PartiesSellPrisonerCampaignBehavior"/> from
    /// auto-selling prisoners at castles where our castle recruitment system is active.
    ///
    /// Without this patch, vanilla sells ~10% of all prisoners every day at AI-owned castles,
    /// causing T4+ prisoners to vanish before they finish their training period.
    /// </summary>
    [HarmonyPatch(typeof(PartiesSellPrisonerCampaignBehavior), "DailyTickSettlement")]
    public static class B1071_CastlePrisonerRetentionPatch
    {
        static bool Prefix(Settlement settlement)
        {
            try
            {
                // Only intercept castles when our recruitment system is enabled.
                if (settlement == null || !settlement.IsCastle) return true;

                var settings = Settings.B1071_McmSettings.Instance ?? Settings.B1071_McmSettings.Defaults;
                if (!settings.EnableCastleRecruitment) return true;

                // Skip the vanilla prisoner selling entirely at this castle.
                // Our mod handles prisoners:
                //   T1-T3 → auto-enslaved by AutoEnslaveLowTierPrisoners
                //   T4+   → tracked for conversion by TrackHighTierPrisonerDays
                return false;
            }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print($"[Byzantium1071] CastlePrisonerRetentionPatch error: {ex}");
                return true; // fail-open: let vanilla handle it
            }
        }
    }
}
