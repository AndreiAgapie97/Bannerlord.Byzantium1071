using System;
using System.Linq;
using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.Patches
{
    // Guard both the displayed arrows/Transfer All and individual transfer commands.
    // No payment or FIFO mutation occurs inside the reversible native party screen.
    [HarmonyPatch(typeof(PartyScreenLogic), nameof(PartyScreenLogic.IsTroopTransferable))]
    public static class B1071_CastlePrisonerWithdrawalPatch
    {
        static void Postfix(PartyScreenLogic __instance, PartyScreenLogic.TroopType troopType,
            CharacterObject character, int side, ref bool __result)
        {
            if (__result) __result = CanWithdraw(__instance, troopType, character, side, 1);
        }

        internal static bool CanWithdraw(PartyScreenLogic logic, PartyScreenLogic.TroopType type,
            CharacterObject troop, int side, int count)
        {
            try
            {
                var settings = B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;
                if (!settings.EnableCastleRecruitment || type != PartyScreenLogic.TroopType.Prisoner
                    || side != (int)PartyScreenLogic.PartyRosterSide.Left || troop == null || troop.IsHero
                    || troop.Tier <= settings.CastlePrisonerAutoEnslaveTierMax) return true;
                var castle = Settlement.CurrentSettlement;
                var behavior = B1071_CastleRecruitmentBehavior.Instance;
                if (behavior == null || castle?.IsCastle != true || logic.RightOwnerParty != PartyBase.MainParty
                    || !ReferenceEquals(logic.PrisonerRosters[0], castle.Party.PrisonRoster)) return true;

                int net = logic.CurrentData.TransferredPrisonersHistory
                    .FirstOrDefault(e => e.Item1 == troop)?.Item2 ?? 0;
                int originalCount = logic.PrisonerRosters[0].GetTroopCount(troop) + net;
                return count <= behavior.GetDirectPrisonerWithdrawalLimit(castle, troop, originalCount, net);
            }
            catch (Exception ex)
            {
                B1071_VerboseLog.Log("Prisoners", $"Castle prisoner withdrawal guard: {ex}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(PartyScreenLogic), nameof(PartyScreenLogic.ValidateCommand))]
    public static class B1071_CastlePrisonerWithdrawalCommandPatch
    {
        static void Postfix(PartyScreenLogic __instance, PartyScreenLogic.PartyCommand command, ref bool __result)
        {
            try
            {
                if (!__result || command.Code != PartyScreenLogic.PartyCommandCode.TransferTroop) return;
                if (B1071_CastlePrisonerWithdrawalPatch.CanWithdraw(__instance, command.Type,
                    command.Character, (int)command.RosterSide, command.TotalNumber)) return;
                __result = false;
                InformationManager.DisplayMessage(new InformationMessage(new TextObject(
                    "{=b1071_cr_withdraw_consigned}This troop type includes prisoners with recruitment fees still owed. Use Castle Recruitment to recruit them and pay the fee.").ToString()));
            }
            catch (Exception ex)
            {
                B1071_VerboseLog.Log("Prisoners", $"Castle prisoner withdrawal notification: {ex}");
            }
        }
    }
}
