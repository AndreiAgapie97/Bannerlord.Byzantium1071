using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using Helpers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace Byzantium1071.Campaign.Patches
{
    /// <summary>
    /// Prevents major clans from being destroyed when their kingdom falls.
    /// Instead of the vanilla kill-all-heroes chain, eligible clans are rescued:
    /// their fiefs are transferred, wars cleared, and they become independent factions
    /// with a configurable grace period before seeking mercenary service.
    ///
    /// Patch points:
    ///   • <see cref="DestroyClanAction.Apply(Clan)"/> — non-leader-death kingdom destruction path.
    ///   • <see cref="DestroyClanAction.ApplyByClanLeaderDeath(Clan)"/> — leader-death kingdom destruction path.
    ///
    /// Neither <c>ApplyByFailedRebellion</c> nor genuine single-clan leader-death
    /// (where no heirs exist) are intercepted — those destructions are legitimate.
    /// </summary>
    [HarmonyPatch]
    internal static class B1071_ClanSurvivalPatch
    {
        private static B1071_McmSettings Settings =>
            B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        // ── Cached reflection handles ──
        private static readonly FieldInfo? _kingdomField =
            AccessTools.Field(typeof(Clan), "_kingdom");

        // ─────────────────────────────────────────────
        //  Patch 1: DestroyClanAction.Apply (Default path)
        //  Called by DestroyKingdomAction when isKingdomLeaderDeath = false.
        // ─────────────────────────────────────────────
        [HarmonyPrefix]
        [HarmonyPatch(typeof(DestroyClanAction), nameof(DestroyClanAction.Apply))]
        static bool ApplyPrefix(Clan destroyedClan)
        {
            return !TryRescueClan(destroyedClan, "Apply");
        }

        // ─────────────────────────────────────────────
        //  Patch 2: DestroyClanAction.ApplyByClanLeaderDeath
        //  Called by DestroyKingdomAction when isKingdomLeaderDeath = true.
        //  Also called by KillCharacterAction for genuine leader-death cases.
        // ─────────────────────────────────────────────
        [HarmonyPrefix]
        [HarmonyPatch(typeof(DestroyClanAction), nameof(DestroyClanAction.ApplyByClanLeaderDeath))]
        static bool ApplyByClanLeaderDeathPrefix(Clan destroyedClan)
        {
            return !TryRescueClan(destroyedClan, "ApplyByClanLeaderDeath");
        }

        /// <summary>
        /// Attempts to rescue a clan from destruction. Returns true if the clan was
        /// rescued (caller should skip vanilla destruction), false if vanilla should proceed.
        /// </summary>
        private static bool TryRescueClan(Clan clan, string callPath)
        {
            try
            {
                if (!Settings.EnableClanSurvival) return false;
                if (clan == null || clan.IsEliminated) return false;
                if (clan == Clan.PlayerClan) return false;
                if (clan.IsBanditFaction) return false;

                // Only rescue clans that are part of a dying/dead kingdom.
                // A clan being destroyed outside of a kingdom context (e.g., leader
                // death with no heirs, console command) should not be rescued.
                Kingdom? kingdom = clan.Kingdom;
                if (kingdom == null) return false;

                // The kingdom must be eliminated or about to be eliminated.
                // DestroyKingdomAction.ApplyInternal calls DeactivateKingdom() 
                // before iterating clans, so isEliminated is true by this point.
                if (!kingdom.IsEliminated) return false;

                // Must have living adult heroes who can carry on the clan
                var livingAdults = clan.Heroes
                    .Where(h => h.IsAlive && !h.IsChild && h.IsLord)
                    .ToList();
                if (livingAdults.Count == 0) return false;

                // If the leader is dead, try to promote an heir
                if (clan.Leader == null || !clan.Leader.IsAlive)
                {
                    // GetHeirApparents returns empty if no valid heirs exist
                    var heirs = clan.GetHeirApparents();
                    if (heirs.Count == 0) return false;

                    ChangeClanLeaderAction.ApplyWithoutSelectedNewLeader(clan);

                    // If promotion failed, let vanilla destroy
                    if (clan.Leader == null || !clan.Leader.IsAlive) return false;
                }

                // ── RESCUE ──
                PerformRescue(clan, kingdom, callPath);
                return true;
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival][ERROR] Exception in TryRescueClan for {clan?.Name}: {ex}");
                return false; // On error, let vanilla proceed (safe fallback)
            }
        }

        /// <summary>
        /// Rescues a clan from destruction by transferring fiefs, clearing wars,
        /// detaching from the dying kingdom, and registering for mercenary placement.
        /// </summary>
        private static void PerformRescue(Clan clan, Kingdom dyingKingdom, string callPath)
        {
            string clanName = clan.Name?.ToString() ?? clan.StringId;
            string kingdomName = dyingKingdom.Name?.ToString() ?? dyingKingdom.StringId;
            int heroCount = clan.Heroes.Count(h => h.IsAlive);

            // 1. Transfer fiefs to an heir clan (same logic vanilla uses in DestroyClanAction)
            if (clan.Settlements.Any())
            {
                try
                {
                    Clan heirClan = FactionHelper.ChooseHeirClanForFiefs(clan);
                    foreach (Settlement settlement in clan.Settlements.ToList())
                    {
                        if (settlement.IsTown || settlement.IsCastle)
                        {
                            Hero? newOwner = heirClan.Heroes
                                .Where(x => x.IsAlive && !x.IsChild && x.IsLord)
                                .FirstOrDefault();
                            if (newOwner != null)
                            {
                                ChangeOwnerOfSettlementAction.ApplyByDestroyClan(settlement, newOwner);
                                B1071_VerboseLog.Log("ClanSurvival",
                                    $"Transferred {settlement.Name} to {newOwner.Name} ({heirClan.Name}).");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.Print($"[Byzantium1071][ClanSurvival] Fief transfer error for {clanName}: {ex.Message}");
                }
            }

            // 2. Clear all wars — the clan is becoming independent, not inheriting enemies
            try
            {
                // Use MakePeaceAction for each active war (RemoveFactionsFromCampaignWars is internal)
                foreach (IFaction enemy in clan.FactionsAtWarWith.ToList())
                {
                    if (clan != enemy)
                    {
                        try
                        {
                            MakePeaceAction.Apply(clan, enemy);
                        }
                        catch { /* best effort */ }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival] War clearing error for {clanName}: {ex.Message}");
            }

            // 3. Detach from the dying kingdom.
            // We can't use ChangeKingdomAction because the kingdom is in a destruction
            // state and the action would trigger cascading events. Instead, directly
            // null the private _kingdom field and handle the departure cleanly.
            try
            {
                if (_kingdomField != null)
                {
                    _kingdomField.SetValue(clan, null);
                }
                else
                {
                    // Fallback: this shouldn't happen but log it
                    Debug.Print("[Byzantium1071][ClanSurvival] WARNING: _kingdom field reflection failed. Clan may have stale kingdom reference.");
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival] Kingdom detach error for {clanName}: {ex.Message}");
            }

            // 4. Disband any armies this clan is leading (armies require a kingdom)
            try
            {
                foreach (WarPartyComponent wpc in clan.WarPartyComponents.ToList())
                {
                    if (wpc.MobileParty?.Army != null)
                    {
                        if (wpc.MobileParty.Army.LeaderParty == wpc.MobileParty)
                            DisbandArmyAction.ApplyByNotEnoughParty(wpc.MobileParty.Army);
                        else
                            wpc.MobileParty.Army = null;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival] Army disband error for {clanName}: {ex.Message}");
            }

            // 5. Register the rescued clan in the behavior for grace period tracking
            B1071_ClanSurvivalBehavior.Instance?.RegisterRescuedClan(clan);

            // 6. Log the rescue
            B1071_VerboseLog.Log("ClanSurvival",
                $"Rescued {clanName} ({heroCount} heroes, leader: {clan.Leader?.Name}) " +
                $"from destruction of {kingdomName} (path: {callPath}). " +
                $"Grace period: {Settings.ClanSurvivalGracePeriodDays} days.");

            // Always log rescues to rgl_log regardless of verbose setting
            Debug.Print($"[Byzantium1071][ClanSurvival] Clan rescued: {clanName} " +
                $"({heroCount} heroes) from {kingdomName}. Will seek mercenary service in " +
                $"{Settings.ClanSurvivalGracePeriodDays} days.");
        }
    }
}
