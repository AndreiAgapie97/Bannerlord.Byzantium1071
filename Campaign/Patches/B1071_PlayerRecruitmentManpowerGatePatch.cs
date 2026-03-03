using Byzantium1071.Campaign.Behaviors;
using HarmonyLib;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Recruitment;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.Patches
{
    [HarmonyPatch(typeof(RecruitVolunteerVM), nameof(RecruitVolunteerVM.ExecuteRecruit))]
    public static class B1071_PlayerRecruitmentSingleGatePatch
    {
        private static bool Prefix(RecruitVolunteerTroopVM troop)
        {
            try
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
                TextObject msg = new TextObject("{=b1071_cr_manpower_block}Manpower: cannot recruit {TROOP} — {POOL} needs {COST}, only {LEFT} left.")
                    .SetTextVariable("TROOP", troop.Character.Name)
                    .SetTextVariable("POOL", poolName)
                    .SetTextVariable("COST", costPer)
                    .SetTextVariable("LEFT", available);

                InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Yellow));
                return false;
            }
            catch (Exception ex) { TaleWorlds.Library.Debug.Print($"[Byzantium1071] PlayerRecruitmentSingleGatePatch error: {ex}"); return true; }
        }
    }

    [HarmonyPatch(typeof(RecruitmentVM), nameof(RecruitmentVM.ExecuteRecruitAll))]
    public static class B1071_PlayerRecruitmentAllGatePatch
    {
        private static bool Prefix(RecruitmentVM __instance)
        {
            try
            {
                if (__instance == null)
                    return true;

                B1071_ManpowerBehavior? behavior = B1071_ManpowerBehavior.Instance;
                Settlement? settlement = Settlement.CurrentSettlement;
                MobileParty? party = MobileParty.MainParty;
                if (behavior == null || settlement == null || party == null)
                    return true;

                var requestedTroops = new List<CharacterObject>();
                if (__instance.VolunteerList != null)
                foreach (RecruitVolunteerVM volunteer in __instance.VolunteerList)
                {
                    if (volunteer == null) continue;
                    if (volunteer.Troops == null) continue;
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
                TextObject msg = new TextObject("{=b1071_ui_mp_recruit_all_block}Manpower: Recruit All blocked — {POOL} needs {NEEDED} for {TROOP}, only {LEFT} left.")
                    .SetTextVariable("POOL", poolName)
                    .SetTextVariable("NEEDED", neededCost)
                    .SetTextVariable("TROOP", troopName)
                    .SetTextVariable("LEFT", availableBefore);

                InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Yellow));
                return false;
            }
            catch (Exception ex) { TaleWorlds.Library.Debug.Print($"[Byzantium1071] PlayerRecruitmentAllGatePatch error: {ex}"); return true; }
        }
    }

    // NOTE: "OnDone" is a private method in RecruitmentVM; nameof() cannot be used.
    // Verified against v1.3.15 TaleWorlds.CampaignSystem.ViewModelCollection.dll. Re-verify after game updates.
    [HarmonyPatch(typeof(RecruitmentVM), "OnDone")]
    public static class B1071_PlayerRecruitmentOnDoneGatePatch
    {
        private static bool Prefix(RecruitmentVM __instance)
        {
            try
            {
                if (__instance == null)
                    return true;

                B1071_ManpowerBehavior? behavior = B1071_ManpowerBehavior.Instance;
                Settlement? settlement = Settlement.CurrentSettlement;
                MobileParty? party = MobileParty.MainParty;
                if (behavior == null || settlement == null || party == null)
                    return true;

                var troopsInCart = new List<CharacterObject>();
                if (__instance.TroopsInCart != null)
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
                TextObject msg = new TextObject("{=b1071_ui_mp_confirm_block}Manpower: cannot confirm — {POOL} needs {NEEDED} for {TROOP}, only {LEFT} left.")
                    .SetTextVariable("POOL", poolName)
                    .SetTextVariable("NEEDED", neededCost)
                    .SetTextVariable("TROOP", troopName)
                    .SetTextVariable("LEFT", availableBefore);

                InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Yellow));
                return false;
            }
            catch (Exception ex) { TaleWorlds.Library.Debug.Print($"[Byzantium1071] PlayerRecruitmentOnDoneGatePatch error: {ex}"); return true; }
        }
    }
}
