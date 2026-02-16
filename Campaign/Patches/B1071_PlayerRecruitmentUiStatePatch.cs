using Byzantium1071.Campaign.Behaviors;
using HarmonyLib;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Recruitment;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.Patches
{
    // NOTE: "RefreshScreen" was private in v1.3.14 but became public in v1.3.15; Harmony patches both fine.
    // "RefreshPartyProperties" is still private in v1.3.15.
    // Verified against v1.3.15 TaleWorlds.CampaignSystem.ViewModelCollection.dll. Re-verify after game updates.
    [HarmonyPatch(typeof(RecruitmentVM), "RefreshScreen")]
    public static class B1071_PlayerRecruitmentUiRefreshScreenPatch
    {
        private static void Postfix(RecruitmentVM __instance)
        {
            try { B1071_PlayerRecruitmentUiStateHelper.RefreshUiState(__instance); }
            catch (Exception ex) { TaleWorlds.Library.Debug.Print($"[Byzantium1071] UiRefreshScreenPatch error: {ex}"); }
        }
    }

    [HarmonyPatch(typeof(RecruitmentVM), "RefreshPartyProperties")]
    public static class B1071_PlayerRecruitmentUiRefreshPartyPatch
    {
        private static void Postfix(RecruitmentVM __instance)
        {
            try { B1071_PlayerRecruitmentUiStateHelper.RefreshUiState(__instance); }
            catch (Exception ex) { TaleWorlds.Library.Debug.Print($"[Byzantium1071] UiRefreshPartyPatch error: {ex}"); }
        }
    }

    internal static class B1071_PlayerRecruitmentUiStateHelper
    {
        internal static void RefreshUiState(RecruitmentVM vm)
        {
            if (vm == null) return;

            B1071_ManpowerBehavior? behavior = B1071_ManpowerBehavior.Instance;
            Settlement? settlement = Settlement.CurrentSettlement;
            MobileParty? party = MobileParty.MainParty;
            if (behavior == null || settlement == null || party == null)
                return;

            behavior.GetManpowerPool(settlement, out int currentPool, out _, out _);
            int remainingAfterCart = currentPool;

            var troopsInCart = new List<CharacterObject>();
            if (vm.TroopsInCart != null)
            {
                foreach (RecruitVolunteerTroopVM cartTroop in vm.TroopsInCart)
                {
                    if (cartTroop?.Character == null) continue;
                    troopsInCart.Add(cartTroop.Character);
                    int cartCost = behavior.GetRecruitCostForParty(settlement, party, cartTroop.Character);
                    remainingAfterCart -= cartCost;
                }
            }

            if (remainingAfterCart < 0)
                remainingAfterCart = 0;

            bool allRecruitAllCandidatesAffordable = true;
            int recruitAllSequenceRemaining = currentPool;
            int individualAvailableCount = 0;

            if (vm.VolunteerList != null)
            foreach (RecruitVolunteerVM volunteer in vm.VolunteerList)
            {
                if (volunteer == null) continue;

                if (volunteer.Troops == null) continue;
                foreach (RecruitVolunteerTroopVM troop in volunteer.Troops)
                {
                    if (troop == null || troop.Character == null || troop.IsTroopEmpty)
                        continue;

                    if (troop.IsInCart)
                        continue;

                    if (!troop.PlayerHasEnoughRelation)
                    {
                        troop.CanBeRecruited = false;
                        continue;
                    }

                    int costPer = behavior.GetRecruitCostForParty(settlement, party, troop.Character);
                    bool individuallyAffordable = remainingAfterCart >= costPer;
                    // AND with vanilla's existing decision so manpower only further restricts,
                    // never re-enables troops that vanilla disabled (e.g. no gold, full party).
                    troop.CanBeRecruited = troop.CanBeRecruited && individuallyAffordable;

                    if (individuallyAffordable)
                        individualAvailableCount++;

                    if (recruitAllSequenceRemaining >= costPer)
                        recruitAllSequenceRemaining -= costPer;
                    else
                        allRecruitAllCandidatesAffordable = false;
                }
            }

            // AND with vanilla's existing value: manpower can only further restrict Recruit All,
            // never re-enable it when vanilla disabled it for other reasons (party full, no gold, etc.).
            vm.CanRecruitAll = vm.CanRecruitAll && individualAvailableCount > 0 && allRecruitAllCandidatesAffordable;

            if (!behavior.CanRecruitSequenceAllOrNothing(
                    settlement,
                    party,
                    troopsInCart,
                    out CharacterObject? blockedTroop,
                    out int neededCost,
                    out int availableBefore,
                    out Settlement? pool))
            {
                vm.IsDoneEnabled = false;
                string poolName = pool?.Name?.ToString() ?? "pool";
                string troopName = blockedTroop?.Name?.ToString() ?? "troop";
                if (vm.DoneHint != null)
                {
                    vm.DoneHint.HintText = new TextObject("{=!}" +
                        $"Manpower: {poolName} needs {neededCost} for {troopName}, only {availableBefore} left.");
                }
            }
        }
    }
}
