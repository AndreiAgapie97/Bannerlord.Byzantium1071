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
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace Byzantium1071.Campaign.Patches
{
    /// <summary>
    /// Clan Survival system — rescues clans when their kingdom is destroyed.
    ///
    /// Vanilla has TWO distinct kingdom destruction paths:
    ///
    ///   Path A — "Lost all settlements": Vanilla calls <c>Kingdom.DeactivateKingdom()</c>
    ///     which sets <c>IsEliminated = true</c>. Clans remain alive as independent factions
    ///     but inherit the kingdom's wars. Vanilla does NOT call <c>DestroyClanAction</c>
    ///     or <c>DestroyKingdomAction</c> in this path.
    ///
    ///   Path B — "Leader death": <c>DestroyKingdomAction.ApplyInternal</c> calls
    ///     <c>DeactivateKingdom()</c>, then iterates clans and calls
    ///     <c>DestroyClanAction.Apply/ApplyByClanLeaderDeath</c> to destroy each clan
    ///     (killing heroes, removing from campaign, etc.).
    ///
    /// Patch architecture:
    ///   1. <c>Kingdom.DeactivateKingdom</c> postfix (PRIMARY) — fires on BOTH paths.
    ///      Immediately rescues all eligible clans: clears inherited wars, detaches
    ///      from kingdom, registers for grace period tracking.
    ///
    ///   2. <c>DestroyClanAction.Apply/ApplyByClanLeaderDeath</c> prefixes (SAFETY NET) —
    ///      only fires in Path B. If the clan was already rescued by the postfix,
    ///      skips vanilla destruction. If not yet rescued, rescues it now.
    ///
    ///   3. <c>ChangeKingdomAction.ApplyInternal</c> prefix (GRACE GUARD) — blocks
    ///      rescued clans from joining kingdoms during the configurable grace period.
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
                return false;
            }

            // If the leader is dead, try to promote an heir.
            if (clan.Leader == null || !clan.Leader.IsAlive)
            {
                var heirs = clan.GetHeirApparents();
                if (heirs.Count == 0)
                {
                    Debug.Print($"[Byzantium1071][ClanSurvival][{context}] SKIP '{clanId}': leader dead, no heir apparents");
                    return false;
                }

                try
                {
                    ChangeClanLeaderAction.ApplyWithoutSelectedNewLeader(clan);
                }
                catch (Exception ex)
                {
                    Debug.Print($"[Byzantium1071][ClanSurvival][{context}] SKIP '{clanId}': heir promotion threw: {ex.Message}");
                    return false;
                }

                if (clan.Leader == null || !clan.Leader.IsAlive)
                {
                    Debug.Print($"[Byzantium1071][ClanSurvival][{context}] SKIP '{clanId}': heir promotion failed");
                    return false;
                }
                Debug.Print($"[Byzantium1071][ClanSurvival][{context}] '{clanId}' heir promoted to {clan.Leader.Name}");
            }

            Debug.Print($"[Byzantium1071][ClanSurvival][{context}] '{clanId}' ELIGIBLE " +
                $"({livingAdults.Count} heroes, leader: {clan.Leader?.Name})");
            return true;
        }

        // ── Shared rescue logic ─────────────────────────────────────

        /// <summary>
        /// Rescues a clan from a dying kingdom.
        ///
        /// Operation order:
        ///   1. Detach from kingdom (clan.Kingdom = null)
        ///   2. Clear inherited wars
        ///   3. Register for grace period
        ///
        /// Fief transfer is NOT done here — when a kingdom loses all settlements
        /// (Path A), there are no fiefs left to transfer. In Path B, vanilla's
        /// DestroyClanAction would have handled it, but we skip vanilla entirely.
        /// If the clan somehow still has fiefs (edge case), they become independent
        /// clan settlements, which is acceptable.
        /// </summary>
        internal static void PerformRescue(Clan clan, Kingdom dyingKingdom, string callPath)
        {
            string clanName = clan.Name?.ToString() ?? clan.StringId;
            string kingdomName = dyingKingdom.Name?.ToString() ?? dyingKingdom.StringId;
            int heroCount = clan.Heroes.Count(h => h.IsAlive);

            Debug.Print($"[Byzantium1071][ClanSurvival] Rescuing {clanName} " +
                $"({heroCount} heroes, leader: {clan.Leader?.Name}) " +
                $"from {kingdomName} (path: {callPath}).");

            // ── Step 1: Detach from the dying kingdom ──
            try
            {
                if (clan.Kingdom != null)
                    clan.Kingdom = null;
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival] Kingdom detach error for {clanName}: {ex.Message}");
            }

            // ── Step 2: Clear inherited wars ──
            try
            {
                int warsCleared = 0;
                foreach (IFaction enemy in clan.FactionsAtWarWith.ToList())
                {
                    if (clan != enemy &&
                        !TaleWorlds.CampaignSystem.Campaign.Current.Models.DiplomacyModel
                            .IsAtConstantWar(clan, enemy))
                    {
                        try
                        {
                            MakePeaceAction.Apply(clan, enemy);
                            warsCleared++;
                        }
                        catch { /* best effort */ }
                    }
                }
                Debug.Print($"[Byzantium1071][ClanSurvival] Cleared {warsCleared} inherited war(s) for {clanName}.");
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival] War clearing error for {clanName}: {ex.Message}");
            }

            // ── Step 3: Register for grace period ──
            try
            {
                B1071_ClanSurvivalBehavior.Instance?.RegisterRescuedClan(clan);
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival] Grace period registration error for {clanName}: {ex.Message}");
            }

            // Mark as rescued
            _alreadyRescued.Add(clan.StringId);

            Debug.Print($"[Byzantium1071][ClanSurvival] Rescue complete: {clanName} " +
                $"({heroCount} heroes) from {kingdomName} (path: {callPath}). " +
                $"Grace period: {Settings.ClanSurvivalGracePeriodDays} days.");

            B1071_VerboseLog.Log("ClanSurvival",
                $"Rescued {clanName} ({heroCount} heroes, leader: {clan.Leader?.Name}) " +
                $"from destruction of {kingdomName} (path: {callPath}). " +
                $"Grace period: {Settings.ClanSurvivalGracePeriodDays} days.");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  PRIMARY PATCH: Kingdom.DeactivateKingdom postfix
    //  Fires on ALL kingdom destruction paths (settlement loss AND
    //  leader death). This is where the actual rescue happens.
    // ═══════════════════════════════════════════════════════════════
    [HarmonyPatch(typeof(Kingdom), "DeactivateKingdom")]
    internal static class B1071_ClanSurvivalDeactivatePatch
    {
        [HarmonyPostfix]
        static void Postfix(Kingdom __instance)
        {
            try
            {
                if (!B1071_ClanSurvivalPatch.Settings.EnableClanSurvival) return;
                if (__instance == null || !__instance.IsEliminated) return;

                string kingdomName = __instance.Name?.ToString() ?? __instance.StringId;
                var clans = __instance.Clans?.ToList();
                if (clans == null || clans.Count == 0)
                {
                    Debug.Print($"[Byzantium1071][ClanSurvival][DeactivateKingdom] " +
                        $"{kingdomName} eliminated but has no clans to rescue.");
                    return;
                }

                Debug.Print($"[Byzantium1071][ClanSurvival][DeactivateKingdom] " +
                    $"{kingdomName} eliminated. Processing {clans.Count} clan(s)...");

                B1071_ClanSurvivalPatch._alreadyRescued.Clear();
                int rescued = 0;
                int skipped = 0;

                foreach (Clan clan in clans)
                {
                    try
                    {
                        if (B1071_ClanSurvivalPatch.IsClanEligibleForRescue(
                                clan, __instance, "DeactivateKingdom"))
                        {
                            B1071_ClanSurvivalPatch.PerformRescue(
                                clan, __instance, "DeactivateKingdom");
                            rescued++;
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.Print($"[Byzantium1071][ClanSurvival][DeactivateKingdom] " +
                            $"Error processing {clan?.Name}: {ex.Message}");
                        skipped++;
                    }
                }

                Debug.Print($"[Byzantium1071][ClanSurvival][DeactivateKingdom] " +
                    $"{kingdomName}: {rescued} rescued, {skipped} skipped.");
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival][DeactivateKingdom] " +
                    $"Fatal error: {ex}");
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
        static bool ApplyPrefix(Clan destroyedClan)
        {
            Debug.Print($"[Byzantium1071][ClanSurvival][PREFIX] ApplyPrefix FIRED " +
                $"for '{destroyedClan?.Name}' (StringId: {destroyedClan?.StringId})");
            return !HandleDestroyClan(destroyedClan, "DestroyClanAction.Apply");
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(DestroyClanAction), nameof(DestroyClanAction.ApplyByClanLeaderDeath))]
        static bool ApplyByClanLeaderDeathPrefix(Clan destroyedClan)
        {
            Debug.Print($"[Byzantium1071][ClanSurvival][PREFIX] ApplyByClanLeaderDeathPrefix FIRED " +
                $"for '{destroyedClan?.Name}' (StringId: {destroyedClan?.StringId})");
            return !HandleDestroyClan(destroyedClan, "DestroyClanAction.ApplyByClanLeaderDeath");
        }

        /// <summary>
        /// Returns true if the clan was rescued (skip vanilla), false to let vanilla proceed.
        /// </summary>
        private static bool HandleDestroyClan(Clan clan, string callPath)
        {
            try
            {
                if (clan == null) return false;

                // Already rescued by DeactivateKingdom postfix?
                if (B1071_ClanSurvivalPatch._alreadyRescued.Contains(clan.StringId))
                {
                    Debug.Print($"[Byzantium1071][ClanSurvival][PREFIX] '{clan.Name}' already rescued " +
                        $"by DeactivateKingdom postfix — skipping vanilla destruction.");
                    return true; // Skip vanilla
                }

                // Already rescued and in grace period (e.g. from a previous event)?
                var behavior = B1071_ClanSurvivalBehavior.Instance;
                if (behavior != null && behavior.IsInGracePeriod(clan))
                {
                    Debug.Print($"[Byzantium1071][ClanSurvival][PREFIX] '{clan.Name}' is in grace period " +
                        $"— skipping vanilla destruction.");
                    return true; // Skip vanilla
                }

                // Not yet rescued — check if from a dying kingdom
                Kingdom? kingdom = clan.Kingdom;
                if (kingdom == null || !kingdom.IsEliminated) return false;

                // Try to rescue now
                if (B1071_ClanSurvivalPatch.IsClanEligibleForRescue(clan, kingdom, callPath))
                {
                    B1071_ClanSurvivalPatch.PerformRescue(clan, kingdom, callPath);
                    return true; // Skip vanilla
                }

                return false; // Let vanilla proceed
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival][PREFIX] Error for {clan?.Name}: {ex}");
                return false; // Safe fallback
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  GRACE PERIOD GUARD: ChangeKingdomAction prefix
    //  Blocks rescued clans from joining kingdoms during the grace
    //  period. Our own mercenary placement uses a bypass flag.
    // ═══════════════════════════════════════════════════════════════
    [HarmonyPatch]
    internal static class B1071_ClanSurvivalGracePeriodPatch
    {
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods()
        {
            var type = typeof(ChangeKingdomAction);

            // Try ApplyInternal first — single chokepoint for all kingdom changes
            var applyInternal = AccessTools.Method(type, "ApplyInternal");
            if (applyInternal != null)
            {
                Debug.Print("[Byzantium1071][ClanSurvival] Grace guard: patching ChangeKingdomAction.ApplyInternal");
                yield return applyInternal;
                yield break;
            }

            // Fallback: patch known public methods individually
            Debug.Print("[Byzantium1071][ClanSurvival] Grace guard: ApplyInternal not found, trying public methods");
            int found = 0;
            foreach (string methodName in new[]
            {
                "ApplyByJoinToKingdom",
                "ApplyByJoinFactionAsMercenary",
                "ApplyByJoinAsVassal"
            })
            {
                var method = AccessTools.Method(type, methodName);
                if (method != null)
                {
                    Debug.Print($"[Byzantium1071][ClanSurvival] Grace guard: patching {methodName}");
                    found++;
                    yield return method;
                }
            }

            if (found == 0)
                Debug.Print("[Byzantium1071][ClanSurvival] Grace guard: WARNING — no ChangeKingdomAction methods found!");
        }

        [HarmonyPrefix]
        static bool Prefix(MethodBase __originalMethod, object[] __args)
        {
            Clan? clan = null;
            Kingdom? newKingdom = null;

            if (__args != null)
            {
                foreach (object arg in __args)
                {
                    if (clan == null && arg is Clan c) clan = c;
                    if (newKingdom == null && arg is Kingdom k) newKingdom = k;
                }
            }

            if (clan == null) return true;
            if (newKingdom == null) return true;     // Leave action — allow

            var behavior = B1071_ClanSurvivalBehavior.Instance;
            if (behavior == null) return true;
            if (behavior._bypassGraceGuard) return true;

            if (!behavior.IsInGracePeriod(clan)) return true;

            Debug.Print($"[Byzantium1071][ClanSurvival] Grace period guard: BLOCKED {clan.Name} " +
                $"from joining {newKingdom.Name} (via {__originalMethod.Name}).");
            return false;
        }
    }
}
