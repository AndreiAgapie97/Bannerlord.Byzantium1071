using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace Byzantium1071.Campaign.Patches
{
    /// <summary>
    /// Redirects regular (non-hero) prisoners from visiting AI lord parties into the
    /// castle's prison roster instead of letting vanilla sell/vaporize them.
    ///
    /// VANILLA BEHAVIOR:
    /// <see cref="PartiesSellPrisonerCampaignBehavior.OnSettlementEntered"/> fires when
    /// any non-player, friendly party enters a fortification. It collects ALL prisoners
    /// (regular + heroes at war) and calls <c>SellPrisonersAction.ApplyForSelectedPrisoners</c>.
    /// For regular (non-hero) prisoners, that action ONLY removes them from the party's
    /// prison roster and pays gold to the lord — it never adds them to the settlement's
    /// prison roster. The prisoners simply vanish.
    ///
    /// WHY THIS MATTERS:
    /// Our castle recruitment system (<see cref="Behaviors.B1071_CastleRecruitmentBehavior"/>)
    /// reads from <c>settlement.Party.PrisonRoster</c> to track T4+ prisoner conversion
    /// and populate the Pending/Ready lists. If prisoners never enter that roster, the system
    /// has no raw material to work with.
    ///
    /// THIS FIX:
    /// A Harmony Prefix on the private <c>OnSettlementEntered</c> method. For castles with
    /// castle recruitment enabled, we move ALL non-hero regular prisoners from the party's
    /// prison roster directly into the castle's prison roster (free deposit — no gold paid).
    /// After our prefix, the party's roster contains only heroes; vanilla's handler still
    /// runs, finds no regulars, and handles hero prisoners normally.
    ///
    /// T1-T3 prisoners deposited will be auto-enslaved on the next daily tick by
    /// <see cref="Behaviors.B1071_CastleRecruitmentBehavior.AutoEnslaveLowTierPrisoners"/>.
    /// T4+ prisoners begin their conversion tracking immediately.
    ///
    /// NOTE: "OnSettlementEntered" is a private method. Verified against v1.3.15
    /// TaleWorlds.CampaignSystem.dll. Re-verify after game updates.
    /// </summary>
    [HarmonyPatch(typeof(PartiesSellPrisonerCampaignBehavior), "OnSettlementEntered")]
    public static class B1071_CastlePrisonerDepositPatch
    {
        static void Prefix(MobileParty mobileParty, Settlement settlement)
        {
            try
            {
                // Only intercept at castles with castle recruitment enabled.
                if (settlement == null || !settlement.IsCastle) return;
                if (mobileParty == null || mobileParty.IsMainParty) return;

                var settings = Settings.B1071_McmSettings.Instance ?? Settings.B1071_McmSettings.Defaults;
                if (!settings.EnableCastleRecruitment) return;

                // Skip hostile/disbanding parties (vanilla skips them too).
                if (mobileParty.MapFaction == null || mobileParty.IsDisbanding) return;
                if (mobileParty.MapFaction.IsAtWarWith(settlement.MapFaction)) return;

                TroopRoster? partyPrison = mobileParty.PrisonRoster;
                TroopRoster? castlePrison = settlement.Party?.PrisonRoster;
                if (partyPrison == null || castlePrison == null) return;
                if (partyPrison.TotalRegulars <= 0) return;

                // Snapshot the roster to avoid collection-modification during iteration.
                var toDeposit = new List<(CharacterObject Troop, int Count, int Wounded)>();
                foreach (TroopRosterElement element in partyPrison.GetTroopRoster())
                {
                    if (element.Character == null || element.Character.IsHero || element.Number <= 0)
                        continue;
                    toDeposit.Add((element.Character, element.Number, element.WoundedNumber));
                }

                if (toDeposit.Count == 0) return;

                // Move all regular prisoners: party → castle prison.
                // No gold paid — lords deliver prisoners to their faction's castles as duty.
                foreach (var (troop, count, wounded) in toDeposit)
                {
                    partyPrison.AddToCounts(troop, -count, insertAtFront: false, -wounded);
                    castlePrison.AddToCounts(troop, count, insertAtFront: false, wounded);
                }

                // After this prefix, the party's prison roster contains only heroes.
                // Vanilla's OnSettlementEntered will still run, iterate the roster,
                // find no regulars, and handle hero prisoners normally (transfer/ransom).
            }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print(
                    $"[Byzantium1071] CastlePrisonerDepositPatch error: {ex}");
                // Fail-open: if we crash, vanilla still runs and sells prisoners as before.
            }
        }
    }
}
