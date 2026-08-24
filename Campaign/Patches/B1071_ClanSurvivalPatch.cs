using System;
using System.Collections.Generic;
using System.Linq;
using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace Byzantium1071.Campaign.Patches
{
    /// <summary>
    /// Clan Survival system — rescues clans when their kingdom is destroyed.
    ///
    /// Vanilla has TWO distinct kingdom destruction paths:
    ///
    ///   Path A — "Lost all settlements": Vanilla calls
    ///     <c>ChangeKingdomAction.ApplyInternal(clan, null, LeaveByKingdomDestruction)</c>
    ///     for EACH clan to remove them from the kingdom, THEN calls
    ///     <c>Kingdom.DeactivateKingdom()</c> on the now-empty kingdom.
    ///     Clans become independent factions and inherit all of the kingdom's wars.
    ///
    ///   Path B — "Leader death": <c>DestroyKingdomAction.ApplyInternal</c> calls
    ///     <c>DeactivateKingdom()</c>, then iterates clans and calls
    ///     <c>DestroyClanAction.Apply/ApplyByClanLeaderDeath</c> to destroy each clan.
    ///
    /// Patch architecture:
    ///   1. <c>CampaignEvents.OnClanChangedKingdomEvent</c> handler (PRIMARY) — in the
    ///      behavior class. Fires for each clan when vanilla removes it from a dying
    ///      kingdom. Registers the clan for tracking; the daily tick clears inherited
    ///      wars. Handles Path A completely.
    ///
    ///   2. <c>DestroyClanAction.Apply/ApplyByClanLeaderDeath</c> prefixes (SAFETY NET) —
    ///      handles Path B. A tracked independent clan is protected only while it has a
    ///      living leader. If vanilla reaches destruction with a dead or missing leader,
    ///      tracking is cleared and the current vanilla action is allowed to finish.
    ///      If the clan was not previously rescued, the normal eligibility flow applies.
    ///
    ///   3. <c>Kingdom.DeactivateKingdom</c> postfix (DIAGNOSTIC) — logs for debugging.
    ///      In Path A, clans are already gone by this point. In Path B, the ClanDestroy
    ///      prefix handles clans. This is purely observational.
    /// </summary>
    internal static class B1071_ClanSurvivalPatch
    {
        internal static B1071_McmSettings Settings =>
            B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        /// <summary>
        /// Set of Clan.StringId that have already been rescued in the current
        /// DeactivateKingdom call. Prevents double-processing when Path B's
        /// DestroyClanAction calls arrive after the postfix already rescued.
        /// </summary>
        internal static readonly HashSet<string> _alreadyRescued = new();

        // ── Shared eligibility check ────────────────────────────────

        /// <summary>
        /// Checks whether a clan is eligible for rescue. Does NOT perform the rescue.
        /// </summary>
        internal static bool IsClanEligibleForRescue(Clan clan, Kingdom? kingdom, string context)
        {
            string clanId = clan?.Name?.ToString() ?? clan?.StringId ?? "NULL";

            if (!Settings.EnableClanSurvival)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival][{context}] SKIP '{clanId}': EnableClanSurvival is false");
                return false;
            }

            if (clan == null || clan.IsEliminated)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival][{context}] SKIP '{clanId}': clan null={clan == null}, IsEliminated={clan?.IsEliminated}");
                return false;
            }

            if (clan == Clan.PlayerClan)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival][{context}] SKIP '{clanId}': is PlayerClan");
                return false;
            }

            if (clan.IsBanditFaction)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival][{context}] SKIP '{clanId}': IsBanditFaction=true");
                return false;
            }

            // Must have living adult heroes who can carry on the clan.
            // Include both lords (IsLord) and minor faction heroes (IsMinorFactionHero).
            var livingAdults = clan.Heroes
                .Where(h => h.IsAlive && !h.IsChild && (h.IsLord || h.IsMinorFactionHero))
                .ToList();
            if (livingAdults.Count == 0)
            {
                int totalHeroes = clan.Heroes.Count;
                int aliveHeroes = clan.Heroes.Count(h => h.IsAlive);
                Debug.Print($"[Byzantium1071][ClanSurvival][{context}] SKIP '{clanId}': no living adult lords/heroes " +
                    $"(total={totalHeroes}, alive={aliveHeroes}, IsMinorFaction={clan.IsMinorFaction})");
                B1071_SessionFileLog.WriteTagged("ClanSurvival",
                    $"[{context}] SKIP '{clanId}': no living adult lords/heroes " +
                    $"(total={totalHeroes}, alive={aliveHeroes})");
                return false;
            }

            // Vanilla resolves succession synchronously when a leader dies. Reaching a
            // rescue boundary with no living leader means vanilla found no successor, or
            // another mod/save has already left the clan inconsistent. Do not start a
            // nested ChangeClanLeaderAction from a campaign callback or Harmony prefix.
            if (clan.Leader == null || !clan.Leader.IsAlive || clan.Leader.Clan != clan)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival][{context}] SKIP '{clanId}': " +
                    "dead, missing, or detached leader after vanilla succession");
                B1071_SessionFileLog.WriteTagged("ClanSurvival",
                    $"[{context}] SKIP '{clanId}': dead, missing, or detached leader after vanilla succession");
                return false;
            }

            Debug.Print($"[Byzantium1071][ClanSurvival][{context}] '{clanId}' ELIGIBLE " +
                $"({livingAdults.Count} heroes, leader: {clan.Leader?.Name})");
            B1071_SessionFileLog.WriteTagged("ClanSurvival",
                $"[{context}] '{clanId}' ELIGIBLE ({livingAdults.Count} heroes, leader: {clan.Leader?.Name})");
            return true;
        }
        // ── Shared rescue logic ─────────────────────────────────────

        /// <summary>
        /// Rescues a clan from a dying kingdom.
        ///
        /// Operation order:
        ///   1. Detach from kingdom (clan.Kingdom = null)
        ///   2. Register for tracking (daily tick clears inherited wars)
        ///
        /// Fief transfer is NOT done here — when a kingdom loses all settlements
        /// (Path A), there are no fiefs left to transfer. In Path B, vanilla's
        /// DestroyClanAction would have handled it, but we skip vanilla entirely.
        /// If the clan somehow still has fiefs (edge case), they become independent
        /// clan settlements, which is acceptable.
        ///
        /// For rebel clans, <paramref name="dyingKingdom"/> may be null (rebels
        /// have no kingdom). In that case, kingdom detach is skipped.
        /// </summary>
        /// <returns>True only when tracking was registered and verified.</returns>
        internal static bool PerformRescue(Clan clan, Kingdom? dyingKingdom, string callPath)
        {
            string clanName = clan.Name?.ToString() ?? clan.StringId;
            string kingdomName = dyingKingdom?.Name?.ToString() ?? dyingKingdom?.StringId ?? "none (rebel)";
            int heroCount = clan.Heroes.Count(h => h.IsAlive);
            var behavior = B1071_ClanSurvivalBehavior.Instance;

            if (behavior == null)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival] Rescue aborted for {clanName}: " +
                    "tracking behavior is unavailable.");
                B1071_SessionFileLog.WriteTagged("ClanSurvival",
                    $"RESCUE_ABORTED {clanName}: tracking behavior unavailable ({callPath}).");
                return false;
            }

            Debug.Print($"[Byzantium1071][ClanSurvival] Rescuing {clanName} " +
                $"({heroCount} heroes, leader: {clan.Leader?.Name}) " +
                $"from {kingdomName} (path: {callPath}).");
            B1071_SessionFileLog.WriteTagged("ClanSurvival",
                $"Rescuing {clanName} ({heroCount} heroes, leader: {clan.Leader?.Name}) " +
                $"from {kingdomName} (path: {callPath}).");

            // ── Step 1: Detach from the dying kingdom (if any) ──
            try
            {
                if (clan.Kingdom != null)
                    clan.Kingdom = null;
                if (clan.Kingdom != null)
                    throw new InvalidOperationException("kingdom detach verification failed");
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival] Kingdom detach error for {clanName}: {ex.Message}");
                B1071_SessionFileLog.WriteTagged("ClanSurvival",
                    $"ERROR kingdom detach for {clanName}: {ex.Message}");
                return false;
            }

            // ── Step 2: Register for tracking (daily tick clears inherited wars) ──
            //
            // IMPORTANT: Do NOT call MakePeaceAction.Apply() here. This method is called
            // from a DestroyClanAction Harmony prefix, which runs while DestroyKingdomAction
            // is still active on the call stack. Calling another Campaign action here risks
            // diplomatic state corruption (same class of bug as the event callback case).
            // The daily tick (OnDailyTick) clears all inherited wars within 1 campaign day.
            try
            {
                behavior.RegisterRescuedClan(clan);
                if (!behavior.IsTracked(clan))
                    throw new InvalidOperationException("tracking verification failed");
            }
            catch (Exception ex)
            {
                behavior.UnregisterRescuedClan(clan);
                Debug.Print($"[Byzantium1071][ClanSurvival] Registration error for {clanName}: {ex.Message}");
                B1071_SessionFileLog.WriteTagged("ClanSurvival",
                    $"RESCUE_ABORTED {clanName}: registration failed: {ex.Message} ({callPath}).");
                return false;
            }

            // Mark as rescued
            _alreadyRescued.Add(clan.StringId);

            Debug.Print($"[Byzantium1071][ClanSurvival] Rescue complete: {clanName} " +
                $"({heroCount} heroes) from {kingdomName} (path: {callPath}).");
            B1071_SessionFileLog.WriteTagged("ClanSurvival",
                $"Rescue complete: {clanName} ({heroCount} heroes) " +
                $"from {kingdomName} (path: {callPath}).");

            B1071_VerboseLog.Log("ClanSurvival",
                $"Rescued {clanName} ({heroCount} heroes, leader: {clan.Leader?.Name}) " +
                $"from destruction of {kingdomName} (path: {callPath}).");
            return true;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  DIAGNOSTIC: Kingdom.DeactivateKingdom postfix
    //  Logs the state when a kingdom is eliminated. In Path A, clans
    //  have already been removed and rescued by the event handler.
    //  In Path B, DestroyKingdomAction handles clans next.
    //  This postfix is purely for logging — no rescue logic here.
    // ═══════════════════════════════════════════════════════════════
    [HarmonyPatch(typeof(Kingdom), "DeactivateKingdom")]
    internal static class B1071_ClanSurvivalDeactivatePatch
    {
        [HarmonyPostfix]
        static void Postfix(Kingdom __instance)
        {
            try
            {
                if (__instance == null) return;

                string kingdomName = __instance.Name?.ToString() ?? __instance.StringId;
                var clans = __instance.Clans?.ToList();
                int clanCount = clans?.Count ?? 0;
                int rescuedCount = B1071_ClanSurvivalPatch._alreadyRescued.Count;

                Debug.Print($"[Byzantium1071][ClanSurvival][DeactivateKingdom] " +
                    $"{kingdomName} eliminated. Remaining clans: {clanCount}, " +
                    $"already rescued by event handler: {rescuedCount}.");
                B1071_SessionFileLog.WriteTagged("ClanSurvival",
                    $"Kingdom {kingdomName} eliminated. Remaining: {clanCount}, rescued: {rescuedCount}");

                if (clanCount > 0)
                {
                    // This can happen in Path B — DestroyKingdomAction calls
                    // DeactivateKingdom first, then destroys clans. The prefix
                    // safety net on DestroyClanAction will handle them.
                    Debug.Print($"[Byzantium1071][ClanSurvival][DeactivateKingdom] " +
                        $"{kingdomName} still has {clanCount} clan(s): " +
                        $"{string.Join(", ", clans.Select(c => c.Name?.ToString() ?? c.StringId))}. " +
                        $"These will be handled by DestroyClanAction safety net (Path B).");
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival][DeactivateKingdom] Error: {ex.Message}");
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  SAFETY NET: DestroyClanAction prefixes
    //  Only fire in Path B (DestroyKingdomAction → DestroyClanAction).
    //  If the DeactivateKingdom postfix already rescued the clan,
    //  these skip vanilla destruction. Otherwise, rescue here.
    // ═══════════════════════════════════════════════════════════════
    [HarmonyPatch]
    internal static class B1071_ClanSurvivalDestroyClanPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(DestroyClanAction), nameof(DestroyClanAction.Apply))]
        static bool ApplyPrefix(Clan destroyedClan, out bool __state)
        {
            Debug.Print($"[Byzantium1071][ClanSurvival][PREFIX] ApplyPrefix FIRED " +
                $"for '{destroyedClan?.Name}' (StringId: {destroyedClan?.StringId})");
            B1071_SessionFileLog.WriteTagged("ClanSurvival",
                $"PREFIX DestroyClanAction.Apply FIRED for '{destroyedClan?.Name}' ({destroyedClan?.StringId})");
            __state = !HandleDestroyClan(destroyedClan!, "DestroyClanAction.Apply");
            return __state;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(DestroyClanAction), nameof(DestroyClanAction.Apply))]
        static void ApplyPostfix(Clan destroyedClan, bool __state, bool __runOriginal)
        {
            if (__state)
                LogDestructionPipelineReturned(
                    destroyedClan, "DestroyClanAction.Apply", __runOriginal);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(DestroyClanAction), nameof(DestroyClanAction.ApplyByClanLeaderDeath))]
        static bool ApplyByClanLeaderDeathPrefix(Clan destroyedClan, out bool __state)
        {
            Debug.Print($"[Byzantium1071][ClanSurvival][PREFIX] ApplyByClanLeaderDeathPrefix FIRED " +
                $"for '{destroyedClan?.Name}' (StringId: {destroyedClan?.StringId})");
            B1071_SessionFileLog.WriteTagged("ClanSurvival",
                $"PREFIX DestroyClanAction.ApplyByClanLeaderDeath FIRED for '{destroyedClan?.Name}' ({destroyedClan?.StringId})");
            __state = !HandleDestroyClan(
                destroyedClan!, "DestroyClanAction.ApplyByClanLeaderDeath");
            return __state;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(DestroyClanAction), nameof(DestroyClanAction.ApplyByClanLeaderDeath))]
        static void ApplyByClanLeaderDeathPostfix(
            Clan destroyedClan,
            bool __state,
            bool __runOriginal)
        {
            if (__state)
                LogDestructionPipelineReturned(
                    destroyedClan,
                    "DestroyClanAction.ApplyByClanLeaderDeath",
                    __runOriginal);
        }

        /// <summary>
        /// Returns true if the clan was rescued (skip vanilla), false to let vanilla proceed.
        /// </summary>
        private static bool HandleDestroyClan(Clan clan, string callPath)
        {
            try
            {
                if (clan == null) return false;

                // The leftover-company cleanup destroys these clans on purpose. Without this
                // check our own rescue catches our own DestroyClanAction call, saves the clan
                // again, and the cleanup reports a count it never actually removed. It is a
                // reachable combination: a player can run the cleanup with the rescue still
                // switched on, wanting the old behaviour but not the backlog.
                if (B1071_ClanSurvivalBehavior.IsPurgingRebelClans)
                {
                    B1071_ClanSurvivalPatch._alreadyRescued.Remove(clan.StringId);
                    return false; // Let vanilla destroy
                }

                var behavior = B1071_ClanSurvivalBehavior.Instance;

                if (B1071_ClanSurvivalPatch._alreadyRescued.Contains(clan.StringId) &&
                    (behavior == null || !behavior.IsTracked(clan)))
                {
                    // Stale marker from an earlier rescue lifecycle; do not treat it as authoritative.
                    B1071_ClanSurvivalPatch._alreadyRescued.Remove(clan.StringId);
                }

                if (behavior != null && behavior.IsTracked(clan))
                {
                    bool hasValidLivingLeader = clan.Leader != null &&
                        clan.Leader.IsAlive &&
                        clan.Leader.Clan == clan;
                    if (ShouldSuppressTrackedClanDestruction(
                            clan.IsEliminated,
                            clan.Kingdom != null,
                            hasValidLivingLeader))
                    {
                        Debug.Print($"[Byzantium1071][ClanSurvival][PREFIX] '{clan.Name}' is tracked " +
                            "with a living leader — skipping vanilla destruction.");
                        B1071_SessionFileLog.WriteTagged("ClanSurvival",
                            $"SUPPRESS_LIVING_LEADER '{clan.Name}' ({clan.StringId}); " +
                            $"leader={clan.Leader?.Name}, path={callPath}.");
                        return true;
                    }

                    int aliveHeroes = clan.Heroes.Count(h => h.IsAlive);
                    int aliveLords = clan.Heroes.Count(h => h.IsAlive && !h.IsChild && h.IsLord);
                    string releaseReason = clan.IsEliminated
                        ? "already eliminated"
                        : clan.Kingdom != null
                            ? "joined a kingdom"
                            : "dead, missing, or detached leader";

                    behavior.UnregisterRescuedClan(clan);
                    Debug.Print($"[Byzantium1071][ClanSurvival][PREFIX] Released tracked clan " +
                        $"'{clan.Name}' to the current vanilla destruction call: {releaseReason}.");
                    B1071_SessionFileLog.WriteTagged("ClanSurvival",
                        $"RELEASE_TRACKED_CLAN '{clan.Name}' ({clan.StringId}); reason={releaseReason}, " +
                        $"leader={clan.Leader?.Name}, validLivingLeader={hasValidLivingLeader}, " +
                        $"aliveHeroes={aliveHeroes}, aliveLords={aliveLords}, " +
                        $"isMinorFaction={clan.IsMinorFaction}, isRebelClan={clan.IsRebelClan}, " +
                        $"path={callPath}.");
                    return false;
                }

                // ── Case 1: Regular kingdom clan — kingdom must be eliminated ──
                Kingdom? kingdom = clan.Kingdom;
                if (kingdom != null && kingdom.IsEliminated)
                {
                    if (B1071_ClanSurvivalPatch.IsClanEligibleForRescue(clan, kingdom, callPath))
                    {
                        return B1071_ClanSurvivalPatch.PerformRescue(
                            clan, kingdom, callPath); // Skip vanilla only after tracking commits
                    }
                    return false;
                }

                // ── Case 2: Rebel clan — no kingdom, rebel-origin StringId ──
                //
                //  Rebel clans have Kingdom == null and are not covered by the
                //  kingdom-elimination check above. Vanilla destroys them via
                //  DestroyClanAction.Apply or ApplyByClanLeaderDeath when their
                //  settlement is reconquered or their leader dies.
                //
                //  This is the SAFETY NET for rebel clans. The PRIMARY rescue
                //  point is OnSettlementOwnerChanged in the behavior class.
                //  If that already fired, the clan is tracked and the check above
                //  would have returned true. If not (e.g., leader death without
                //  settlement loss), we rescue here.
                if (kingdom == null && B1071_ClanSurvivalBehavior.IsRebelClanOrigin(clan))
                {
                    string clanName = clan.Name?.ToString() ?? clan.StringId;

                    // The rebel rescue is opt-in as of v1.0.2.6. This is the third and last
                    // door into it - the behavior class guards the settlement-loss path and
                    // the startup scan, but a rebel clan whose LEADER DIES arrives here
                    // instead, and IsClanEligibleForRescue only tests the master toggle. Left
                    // ungated the setting looked switched off while companies kept appearing.
                    if (!B1071_ClanSurvivalPatch.Settings.RescueRebelClans)
                    {
                        Debug.Print($"[Byzantium1071][ClanSurvival][PREFIX] " +
                            $"Rebel clan {clanName} not rescued - RescueRebelClans is off. Vanilla will destroy.");
                        B1071_SessionFileLog.WriteTagged("ClanSurvival",
                            $"Rebel clan {clanName} not rescued - RescueRebelClans off ({callPath}).");
                        return false; // Let vanilla destroy, exactly as it would without the mod
                    }
                    Debug.Print($"[Byzantium1071][ClanSurvival][PREFIX] " +
                        $"Rebel clan {clanName} caught by safety net ({callPath}). " +
                        $"Checking rescue eligibility...");
                    B1071_SessionFileLog.WriteTagged("ClanSurvival",
                        $"Rebel clan {clanName} caught by PREFIX safety net ({callPath}). " +
                        $"Checking rescue eligibility...");

                    if (B1071_ClanSurvivalPatch.IsClanEligibleForRescue(clan, null, callPath))
                    {
                        // Normalize rebel state and rescue
                        if (!B1071_ClanSurvivalBehavior.NormalizeRebelClan(clan, callPath))
                            return false;
                        return B1071_ClanSurvivalPatch.PerformRescue(
                            clan, null, callPath); // Skip vanilla only after tracking commits
                    }

                    Debug.Print($"[Byzantium1071][ClanSurvival][PREFIX] " +
                        $"Rebel clan {clanName} not eligible — letting vanilla proceed.");
                    B1071_SessionFileLog.WriteTagged("ClanSurvival",
                        $"Rebel clan {clanName} not eligible for rescue — vanilla will destroy.");
                    return false;
                }

                return false; // Let vanilla proceed
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival][PREFIX] Error for {clan?.Name}: {ex}");
                B1071_SessionFileLog.WriteTagged("ClanSurvival",
                    $"FATAL ERROR in HandleDestroyClan for {clan?.Name}: {ex.Message}");
                return false; // Safe fallback
            }
        }

        internal static bool ShouldSuppressTrackedClanDestruction(
            bool isEliminated,
            bool hasKingdom,
            bool hasValidLivingLeader) =>
            !isEliminated && !hasKingdom && hasValidLivingLeader;

        private static void LogDestructionPipelineReturned(
            Clan clan,
            string callPath,
            bool originalRan)
        {
            int aliveHeroes = clan?.Heroes?.Count(h => h.IsAlive) ?? 0;
            string message = $"DESTRUCTION_PIPELINE_RETURNED '{clan?.Name}' ({clan?.StringId}); " +
                $"originalRan={originalRan}, isEliminated={clan?.IsEliminated}, " +
                $"aliveHeroes={aliveHeroes}, path={callPath}.";

            Debug.Print($"[Byzantium1071][ClanSurvival][POSTFIX] {message}");
            B1071_SessionFileLog.WriteTagged("ClanSurvival", message);
        }
    }

}
