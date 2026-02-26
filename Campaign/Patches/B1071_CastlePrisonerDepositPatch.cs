using System;
using System.Collections.Generic;
using System.Linq;
using Byzantium1071.Campaign.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace Byzantium1071.Campaign.Patches
{
    /// <summary>
    /// Intercepts vanilla's prisoner sell/vaporize logic for AI lord parties entering
    /// fortifications (castles AND towns) before vanilla can destroy the prisoners.
    ///
    /// VANILLA BEHAVIOR:
    /// <see cref="PartiesSellPrisonerCampaignBehavior.OnSettlementEntered"/> fires when
    /// any non-player, friendly party enters a fortification. It collects ALL prisoners
    /// (regular + heroes at war) and calls <c>SellPrisonersAction.ApplyForSelectedPrisoners</c>.
    /// For regular (non-hero) prisoners, that action ONLY removes them from the party's
    /// prison roster and pays gold to the lord — it never adds them to the settlement's
    /// prison roster. The prisoners simply vanish.
    ///
    /// THIS FIX (two branches):
    ///
    /// AT CASTLES (when castle recruitment is enabled):
    ///   All non-hero regular prisoners are moved from the party's prison roster into
    ///   the castle's prison roster (free deposit — no gold paid). Depositor tracking
    ///   is recorded for the consignment income model. T1-T3 will be auto-enslaved
    ///   on the next daily tick; T4+ begin conversion tracking. A prison capacity
    ///   check limits deposits to PrisonerSizeLimit — excess prisoners fall through
    ///   to vanilla sell behavior.
    ///
    /// AT TOWNS (when slave economy is enabled):
    ///   Non-hero prisoners at or below CastlePrisonerAutoEnslaveTierMax (default T3)
    ///   are converted to b1071_slave trade goods and added directly to the town's
    ///   market ItemRoster. The town pays the AI lord the current slave market price
    ///   for each enslaved prisoner via GiveGoldAction.ApplyForSettlementToCharacter
    ///   (deducts from Town.Gold, properly clamped, fires campaign events). If the
    ///   town runs out of gold mid-batch, remaining prisoners stay with the lord for
    ///   vanilla to sell/ransom. Higher-tier prisoners (T4+) are left in the party
    ///   roster for vanilla to sell/ransom normally. This runs BEFORE vanilla's handler,
    ///   solving the race condition where vanilla would vaporize all prisoners before
    ///   our SlaveEconomyBehavior.OnSettlementEntered could act.
    ///
    /// After this prefix, the party's prison roster contains only heroes (at castles)
    /// or heroes + T4+ regulars (at towns). Vanilla still runs and handles what remains.
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
                if (settlement == null || mobileParty == null || mobileParty.IsMainParty) return;
                if (mobileParty.MapFaction == null || mobileParty.IsDisbanding) return;
                if (FactionManager.IsAtWarAgainstFaction(mobileParty.MapFaction, settlement.MapFaction)) return;

                if (settlement.IsCastle)
                    HandleCastleDeposit(mobileParty, settlement);
                else if (settlement.IsTown)
                    HandleTownEnslavement(mobileParty, settlement);
            }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print(
                    $"[Byzantium1071] CastlePrisonerDepositPatch error: {ex}");
                // Fail-open: if we crash, vanilla still runs and sells prisoners as before.
            }
        }

        // ── Castle branch: deposit all regulars into castle prison ─────────────

        private static void HandleCastleDeposit(MobileParty mobileParty, Settlement settlement)
        {
            var settings = Settings.B1071_McmSettings.Instance ?? Settings.B1071_McmSettings.Defaults;
            if (!settings.EnableCastleRecruitment) return;

            TroopRoster? partyPrison = mobileParty.PrisonRoster;
            TroopRoster? castlePrison = settlement.Party?.PrisonRoster;
            if (partyPrison == null || castlePrison == null) return;
            if (partyPrison.TotalRegulars <= 0) return;

            // ── Prison capacity check ──
            // Enforce vanilla PrisonerSizeLimit. If the castle prison is full,
            // skip the deposit entirely — vanilla will sell/ransom the prisoners
            // at the next town the lord visits instead.
            int prisonCap = settlement.Party?.PrisonerSizeLimit ?? 0;
            int prisonOccupied = castlePrison.TotalManCount;
            int room = prisonCap > 0 ? prisonCap - prisonOccupied : int.MaxValue;
            if (room <= 0) return;

            // Snapshot the roster to avoid collection-modification during iteration.
            var toDeposit = new List<(CharacterObject Troop, int Count, int Wounded)>();
            int totalQueued = 0;

            // When slave economy is disabled, T1-T3 prisoners have no processing
            // pipeline at castles: AutoEnslaveLowTierPrisoners exits early (no slave
            // economy), TrackHighTierPrisonerDays skips them (below tier threshold),
            // and the retention patch blocks vanilla's 10% daily sell. They'd be
            // permanently stuck. Skip T1-T3 so they stay with the lord — vanilla
            // sells them for ransom gold at the next town.
            bool slaveEconEnabled = settings.EnableSlaveEconomy;
            int enslaveTierMax = settings.CastlePrisonerAutoEnslaveTierMax;

            foreach (TroopRosterElement element in partyPrison.GetTroopRoster())
            {
                if (element.Character == null || element.Character.IsHero || element.Number <= 0)
                    continue;

                // Skip T1-T3 when slave economy is OFF — no processing pipeline.
                if (!slaveEconEnabled && element.Character.Tier <= enslaveTierMax)
                    continue;

                // Clamp by remaining room.
                int take = Math.Min(element.Number, room - totalQueued);
                if (take <= 0) break;

                // Proportional wounded: if depositing a partial batch, take the same
                // fraction of wounded to avoid stranding more wounded than total count.
                int wounded = element.WoundedNumber;
                if (take < element.Number && wounded > 0)
                    wounded = Math.Min(wounded, (int)Math.Round((double)wounded * take / element.Number));

                toDeposit.Add((element.Character, take, wounded));
                totalQueued += take;
                if (totalQueued >= room) break;
            }

            if (toDeposit.Count == 0) return;

            // Move regular prisoners: party → castle prison.
            // No gold paid — lords deliver prisoners to their faction's castles as duty.
            foreach (var (troop, count, wounded) in toDeposit)
            {
                partyPrison.AddToCounts(troop, -count, insertAtFront: false, -wounded);
                castlePrison.AddToCounts(troop, count, insertAtFront: false, wounded);
            }

            // Record depositor for consignment income tracking.
            string? depositorHeroId = mobileParty.LeaderHero?.StringId;

            B1071_VerboseLog.Log("Prisoners", $"Castle deposit: {mobileParty.Name} deposited {totalQueued} prisoner(s) at {settlement.Name} (room={room}).");

            if (!string.IsNullOrEmpty(depositorHeroId))
            {
                var behavior = B1071_CastleRecruitmentBehavior.Instance;
                if (behavior != null)
                {
                    foreach (var (troop, count, _) in toDeposit)
                        behavior.RecordDeposit(settlement.StringId, depositorHeroId!, troop.StringId, count);
                }
            }
        }

        // ── Town branch: enslave T1-T3 prisoners into slave goods ──────────────

        private static void HandleTownEnslavement(MobileParty mobileParty, Settlement settlement)
        {
            var settings = Settings.B1071_McmSettings.Instance ?? Settings.B1071_McmSettings.Defaults;
            if (!settings.EnableSlaveEconomy) return;
            if (!mobileParty.IsLordParty) return;

            ItemObject? slaveItem = MBObjectManager.Instance.GetObject<ItemObject>("b1071_slave");
            if (slaveItem == null) return;

            TroopRoster? roster = mobileParty.PrisonRoster;
            if (roster == null || roster.TotalRegulars <= 0) return;

            Town? town = settlement.Town;
            if (town == null) return;

            int maxEnslaveTier = settings.CastlePrisonerAutoEnslaveTierMax;

            // Town buys slaves at current market price — mirrors castle auto-enslave model.
            int slavePrice = town.GetItemPrice(slaveItem);
            if (slavePrice <= 0) return;

            Hero? lordHero = mobileParty.LeaderHero;

            // Snapshot eligible prisoners.
            var toEnslave = roster.GetTroopRoster()
                .Where(e => e.Character != null && !e.Character.IsHero
                         && e.Number > 0 && e.Character.Tier <= maxEnslaveTier)
                .ToList();

            if (toEnslave.Count == 0) return;

            int totalEnslaved = 0;
            foreach (var element in toEnslave)
            {
                for (int unit = 0; unit < element.Number; unit++)
                {
                    // Affordability gate: town must have gold to buy this slave.
                    // When the town is broke, remaining prisoners stay with the lord
                    // and fall through to vanilla sell behavior (ransom gold).
                    if (town.Gold < slavePrice) goto done;

                    roster.RemoveTroop(element.Character, 1);
                    settlement.ItemRoster.AddToCounts(slaveItem, 1);

                    // Town pays the lord at market price — gold properly deducted from
                    // Town.Gold via GiveGoldAction (clamped, fires campaign events).
                    if (lordHero != null)
                    {
                        GiveGoldAction.ApplyForSettlementToCharacter(
                            settlement, lordHero, slavePrice, disableNotification: true);
                    }

                    totalEnslaved++;
                }
            }
            done:;

            if (totalEnslaved > 0)
                B1071_VerboseLog.Log("SlaveEconomy", $"Town enslavement: {mobileParty.Name} enslaved {totalEnslaved} prisoner(s) at {settlement.Name} @ {slavePrice}g each.");

            // After this, roster contains only heroes + T4+ regulars + any T1-T3
            // the town couldn't afford. Vanilla's handler will sell/ransom those normally.
        }
    }
}
