using System;
using System.Collections.Generic;
using System.Linq;
using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using Helpers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace Byzantium1071.Campaign.Patches
{
    /// <summary>
    /// Prevents clans from being destroyed when their kingdom falls.
    /// Instead of the vanilla kill-all-heroes chain, eligible clans are rescued:
    /// their fiefs are transferred, they leave the dying kingdom cleanly, inherited
    /// wars are cleared, and they become independent factions with a configurable
    /// grace period before seeking mercenary service.
    ///
    /// Patch points:
    ///   • <see cref="DestroyClanAction.Apply(Clan)"/> — non-leader-death kingdom destruction path.
    ///   • <see cref="DestroyClanAction.ApplyByClanLeaderDeath(Clan)"/> — leader-death kingdom destruction path.
    ///
    /// Neither <c>ApplyByFailedRebellion</c> nor genuine single-clan leader-death
    /// (where the kingdom is NOT being destroyed) are intercepted.
    ///
    /// After a prefix returns false (skip), <c>DestroyKingdomAction.ApplyInternal</c>
    /// still calls <c>Kingdom.RemoveClanInternal(clan)</c>. This is benign because
    /// the public <c>Clan.Kingdom</c> setter already called <c>LeaveKingdomInternal()</c>
    /// which calls <c>RemoveClanInternal</c>. MBList.Remove() is idempotent.
    /// </summary>
    [HarmonyPatch]
    internal static class B1071_ClanSurvivalPatch
    {
        private static B1071_McmSettings Settings =>
            B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

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
        //  Also called by KillCharacterAction for genuine leader-death cases;
        //  the kingdom.IsEliminated guard ensures we only rescue in the
        //  kingdom-destruction context, not in genuine standalone leader death.
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
                // The kingdom.IsEliminated guard ensures we don't intercept:
                //   - Genuine leader-death with no heirs (kingdom is still alive)
                //   - Console-triggered single-clan destruction
                //   - Any other non-kingdom-destruction context
                Kingdom? kingdom = clan.Kingdom;
                if (kingdom == null) return false;

                // DestroyKingdomAction.ApplyInternal calls DeactivateKingdom()
                // BEFORE iterating clans, so IsEliminated is true by this point.
                if (!kingdom.IsEliminated) return false;

                // Must have living adult heroes who can carry on the clan.
                // IsLord filters out companions attached to the clan.
                var livingAdults = clan.Heroes
                    .Where(h => h.IsAlive && !h.IsChild && h.IsLord)
                    .ToList();
                if (livingAdults.Count == 0) return false;

                // If the leader is dead, try to promote an heir.
                // This happens in the kingdom-leader-death path for the ruling clan,
                // or if the clan leader died in the same battle that caused the kingdom fall.
                if (clan.Leader == null || !clan.Leader.IsAlive)
                {
                    var heirs = clan.GetHeirApparents();
                    if (heirs.Count == 0) return false;

                    ChangeClanLeaderAction.ApplyWithoutSelectedNewLeader(clan);

                    // If promotion failed (shouldn't happen, but defensive), let vanilla destroy
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
        /// Rescues a clan from destruction. 
        /// 
        /// Operation order is critical:
        ///   1. Transfer fiefs FIRST (needs clan.Kingdom != null for ChooseHeirClanForFiefs)
        ///   2. Detach from kingdom via public setter (handles influence, armies, banner, tracking)
        ///   3. Clear inherited wars AFTER detach (FactionsAtWarWith now returns clan's own stances)
        ///   4. Register in behavior for grace period tracking
        ///
        /// After this method returns, DestroyKingdomAction calls RemoveClanInternal(clan)
        /// which is a no-op because the setter's LeaveKingdomInternal already removed the clan.
        /// </summary>
        private static void PerformRescue(Clan clan, Kingdom dyingKingdom, string callPath)
        {
            string clanName = clan.Name?.ToString() ?? clan.StringId;
            string kingdomName = dyingKingdom.Name?.ToString() ?? dyingKingdom.StringId;
            int heroCount = clan.Heroes.Count(h => h.IsAlive);
            bool wasMercenary = clan.IsUnderMercenaryService;

            // ── Step 1: Transfer fiefs ──
            // Must happen BEFORE Kingdom=null because ChooseHeirClanForFiefs uses
            // clan.Kingdom to search among the kingdom's surviving clans.
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

            // ── Step 2: Detach from the dying kingdom ──
            // Using the public setter Clan.Kingdom, which calls SetKingdomInternal(null):
            //   - LeaveKingdomInternal(): zeroes influence, removes from kingdom clan list,
            //     removes heroes/fiefs/warparties from kingdom tracking, disbands armies
            //   - UpdateBannerColorsAccordingToKingdom(): updates banner for independence
            //   - Sets LastFactionChangeTime = CampaignTime.Now
            //
            // DestroyKingdomAction will also call RemoveClanInternal(clan) after us —
            // this is safe because MBList.Remove() is idempotent (returns false if absent).
            try
            {
                clan.Kingdom = null;
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival] Kingdom detach error for {clanName}: {ex.Message}");
            }

            // ── Step 3: Clear inherited wars ──
            // After Kingdom=null, the clan may have inherited war stances from the
            // dying kingdom. Clear them all so the clan starts fresh as a neutral faction.
            // Match vanilla's pattern: skip constant-war factions (bandits, etc.).
            try
            {
                foreach (IFaction enemy in clan.FactionsAtWarWith.ToList())
                {
                    if (clan != enemy &&
                        !TaleWorlds.CampaignSystem.Campaign.Current.Models.DiplomacyModel.IsAtConstantWar(clan, enemy))
                    {
                        try
                        {
                            MakePeaceAction.Apply(clan, enemy);
                        }
                        catch { /* best effort — some stances may not resolve cleanly */ }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival] War clearing error for {clanName}: {ex.Message}");
            }

            // ── Step 4: Register for grace period tracking ──
            B1071_ClanSurvivalBehavior.Instance?.RegisterRescuedClan(clan);

            // ── Step 5: Log ──
            string roleStr = wasMercenary ? "mercenary" : "vassal";
            B1071_VerboseLog.Log("ClanSurvival",
                $"Rescued {clanName} ({heroCount} heroes, leader: {clan.Leader?.Name}, " +
                $"was {roleStr}) from destruction of {kingdomName} (path: {callPath}). " +
                $"Grace period: {Settings.ClanSurvivalGracePeriodDays} days.");

            // Always log rescues to rgl_log regardless of verbose setting
            Debug.Print($"[Byzantium1071][ClanSurvival] Clan rescued: {clanName} " +
                $"({heroCount} heroes, {roleStr}) from {kingdomName}. " +
                $"Will seek mercenary service in {Settings.ClanSurvivalGracePeriodDays} days.");
        }
    }
}
