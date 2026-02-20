using Byzantium1071.Campaign.Behaviors;
using HarmonyLib;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace Byzantium1071.Campaign.Patches
{
    [HarmonyPatch(typeof(RecruitmentCampaignBehavior), "ApplyInternal")]
    public static class B1071_AiRecruitmentManpowerGatePatch
    {
        private static bool Prefix(
            MobileParty side1Party,
            Settlement settlement,
            Hero individual,
            CharacterObject troop,
            int number,
            int bitCode,
            RecruitmentCampaignBehavior.RecruitingDetail detail)
        {
            if (side1Party == null || troop == null || number <= 0)
                return true;

            B1071_ManpowerBehavior? behavior = B1071_ManpowerBehavior.Instance;
            if (behavior == null)
                return true;

            Settlement? recruitmentSettlement = settlement
                                              ?? individual?.CurrentSettlement
                                              ?? side1Party.CurrentSettlement;
            if (recruitmentSettlement == null)
                return true;

            if (behavior.CanRecruitCountForPlayer(
                    recruitmentSettlement,
                    side1Party,
                    troop,
                    amount: number,
                    out int available,
                    out int costPer,
                    out Settlement? pool))
            {
                return true;
            }

            bool isPlayer = side1Party == MobileParty.MainParty;
            string poolName = pool?.Name?.ToString() ?? "pool";
            string troopName = troop.Name?.ToString() ?? "troop";
            int required = Math.Max(1, costPer) * Math.Max(1, number);

            if (isPlayer)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"Manpower: cannot recruit {troopName} — {poolName} needs {required}, only {available} left.",
                    Colors.Yellow));
            }
            else
            {
                Debug.Print(
                    $"[Byzantium1071][AIManpowerGate] Blocked {detail} for {troopName} x{number} at {poolName}. Need {required}, available {available}.");
            }

            return false;
        }
    }
}
