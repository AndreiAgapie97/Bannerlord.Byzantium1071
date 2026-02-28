using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;

namespace Byzantium1071.Campaign.Patches
{
    /// <summary>
    /// Prevents influence from being granted when prisoners are donated to a
    /// settlement that belongs to a DIFFERENT faction than the donating party.
    ///
    /// VANILLA BUG:
    /// <see cref="InfluenceGainCampaignBehavior.OnPrisonerDonatedToSettlement"/>
    /// grants influence for ALL prisoner donations regardless of faction match.
    /// The only skip condition is when both the donating party AND the settlement
    /// owner are the player's own clan (to avoid double-counting with garrison
    /// management). This means donating prisoners at a neutral or allied castle
    /// (e.g., as a mercenary) awards influence — influence that converts to
    /// mercenary gold, creating a gold-from-nothing exploit.
    ///
    /// FIX:
    /// Prefix blocks the method when the donating party's MapFaction does not
    /// match the settlement's MapFaction. Same-faction donations work as vanilla.
    /// This affects both player AND AI equally (AI rarely triggers this path, but
    /// if they did, the same rule applies).
    ///
    /// NOTE: The target method is private. Verified against v1.3.15
    /// TaleWorlds.CampaignSystem.dll. Re-verify after game updates.
    /// </summary>
    [HarmonyPatch(typeof(InfluenceGainCampaignBehavior), "OnPrisonerDonatedToSettlement")]
    public static class B1071_PrisonerDonationInfluencePatch
    {
        static bool Prefix(MobileParty donatingParty, FlattenedTroopRoster donatedPrisoners, Settlement donatedSettlement)
        {
            try
            {
                if (donatingParty == null || donatedSettlement == null)
                    return true; // Let vanilla handle null guards

                IFaction? partyFaction = donatingParty.MapFaction;
                IFaction? settlementFaction = donatedSettlement.MapFaction;

                if (partyFaction == null || settlementFaction == null)
                    return true; // Safety: let vanilla run if we can't determine faction

                // Block influence when factions don't match (neutral/allied castle deposit).
                if (partyFaction != settlementFaction)
                {
                    B1071_VerboseLog.Log("CastleRecruitment",
                        $"Blocked influence for prisoner donation: {donatingParty.Name} " +
                        $"(faction: {partyFaction.Name}) donated to {donatedSettlement.Name} " +
                        $"(faction: {settlementFaction.Name}) — cross-faction, no influence.");
                    return false; // Skip vanilla's influence grant
                }

                // Same faction — vanilla influence grant proceeds normally.
                return true;
            }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print(
                    $"[Byzantium1071] PrisonerDonationInfluencePatch error: {ex.Message}");
                return true; // Fail-open: let vanilla run on error
            }
        }
    }
}
