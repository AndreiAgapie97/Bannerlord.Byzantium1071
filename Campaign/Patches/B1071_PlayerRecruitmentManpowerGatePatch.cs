using Byzantium1071.Campaign.Behaviors;
using HarmonyLib;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Recruitment;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace Byzantium1071.Campaign.Patches
{
    [HarmonyPatch(typeof(RecruitVolunteerVM), nameof(RecruitVolunteerVM.ExecuteRecruit))]
    public static class B1071_PlayerRecruitmentSingleGatePatch
    {
        private static bool Prefix(RecruitVolunteerTroopVM troop)
        {
            if (troop == null || troop.Character == null)
                return true;

            B1071_ManpowerBehavior? behavior = B1071_ManpowerBehavior.Instance;
            Settlement? settlement = Settlement.CurrentSettlement;
            MobileParty? party = MobileParty.MainParty;
            if (behavior == null || settlement == null || party == null)
                return true;

            if (behavior.CanRecruitCountForPlayer(
                    settlement,
                    party,
                    troop.Character,
                    amount: 1,
                    out int available,
                    out int costPer,
                    out Settlement? pool))
            {
                return true;
            }

            string poolName = pool?.Name?.ToString() ?? "pool";
            InformationManager.DisplayMessage(new InformationMessage(
                $"Not enough manpower in {poolName}. Need {costPer}, available {available}."));
            return false;
        }
    }

    [HarmonyPatch(typeof(RecruitmentVM), nameof(RecruitmentVM.ExecuteRecruitAll))]
    public static class B1071_PlayerRecruitmentAllGatePatch
    {
        private static bool Prefix(RecruitmentVM __instance)
        {
            if (__instance == null)
                return true;

            B1071_ManpowerBehavior? behavior = B1071_ManpowerBehavior.Instance;
            Settlement? settlement = Settlement.CurrentSettlement;
            MobileParty? party = MobileParty.MainParty;
            if (behavior == null || settlement == null || party == null)
                return true;

            var requestedTroops = new List<CharacterObject>();
            foreach (RecruitVolunteerVM volunteer in __instance.VolunteerList)
            {
                if (volunteer == null) continue;
                foreach (RecruitVolunteerTroopVM troop in volunteer.Troops)
                {
                    if (troop == null || troop.Character == null) continue;
                    if (troop.IsInCart || troop.IsTroopEmpty) continue;
                    if (!troop.PlayerHasEnoughRelation) continue;
                    requestedTroops.Add(troop.Character);
                }
            }

            if (requestedTroops.Count == 0)
                return true;

            if (behavior.CanRecruitSequenceAllOrNothing(
                    settlement,
                    party,
                    requestedTroops,
                    out CharacterObject? blockedTroop,
                    out int neededCost,
                    out int availableBefore,
                    out Settlement? pool))
            {
                return true;
            }

            string poolName = pool?.Name?.ToString() ?? "pool";
            string troopName = blockedTroop?.Name?.ToString() ?? "troop";
            InformationManager.DisplayMessage(new InformationMessage(
                $"Recruit All blocked: not enough manpower in {poolName} for {troopName}. Need {neededCost}, available {availableBefore}."));
            return false;
        }
    }

    [HarmonyPatch(typeof(RecruitmentVM), "OnDone")]
    public static class B1071_PlayerRecruitmentOnDoneGatePatch
    {
        private static bool Prefix(RecruitmentVM __instance)
        {
            if (__instance == null)
                return true;

            B1071_ManpowerBehavior? behavior = B1071_ManpowerBehavior.Instance;
            Settlement? settlement = Settlement.CurrentSettlement;
            MobileParty? party = MobileParty.MainParty;
            if (behavior == null || settlement == null || party == null)
                return true;

            var troopsInCart = new List<CharacterObject>();
            foreach (RecruitVolunteerTroopVM troop in __instance.TroopsInCart)
            {
                if (troop?.Character != null)
                    troopsInCart.Add(troop.Character);
            }

            if (troopsInCart.Count == 0)
                return true;

            if (behavior.CanRecruitSequenceAllOrNothing(
                    settlement,
                    party,
                    troopsInCart,
                    out CharacterObject? blockedTroop,
                    out int neededCost,
                    out int availableBefore,
                    out Settlement? pool))
            {
                return true;
            }

            string poolName = pool?.Name?.ToString() ?? "pool";
            string troopName = blockedTroop?.Name?.ToString() ?? "troop";
            InformationManager.DisplayMessage(new InformationMessage(
                $"Cannot complete recruitment: not enough manpower in {poolName} for {troopName}. Need {neededCost}, available {availableBefore}."));
            return false;
        }
    }
}
