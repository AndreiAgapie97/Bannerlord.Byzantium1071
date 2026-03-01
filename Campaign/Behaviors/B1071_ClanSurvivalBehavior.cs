using System;
using System.Collections.Generic;
using System.Linq;
using Byzantium1071.Campaign.Patches;
using Byzantium1071.Campaign.Settings;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.Library;

namespace Byzantium1071.Campaign.Behaviors
{
    /// <summary>
    /// Manages the lifecycle of clans rescued from kingdom destruction.
    ///
    /// PRIMARY RESCUE POINT: Listens to <c>CampaignEvents.OnClanChangedKingdomEvent</c>.
    /// When vanilla removes clans from a dying kingdom (Path A — settlement loss),
    /// it calls <c>ChangeKingdomAction.ApplyInternal</c> with detail
    /// <c>LeaveByKingdomDestruction</c> for each clan BEFORE calling
    /// <c>Kingdom.DeactivateKingdom()</c>. Our event handler fires after each clan
    /// leaves, immediately clears inherited wars and registers for grace period.
    ///
    /// Lifecycle:
    ///   1. Kingdom loses last settlement → vanilla removes clans one by one
    ///      via <c>ChangeKingdomAction(detail: LeaveByKingdomDestruction)</c>.
    ///   2. Our <c>OnClanChangedKingdom</c> event handler intercepts each departure,
    ///      clears inherited wars, and registers the clan for grace period tracking.
    ///   3. During the grace period (default 30 days), the clan remains independent.
    ///      The Harmony prefix on <c>ChangeKingdomAction.ApplyInternal</c> blocks
    ///      any join attempts during this period.
    ///   4. After grace period expires, this behavior evaluates all surviving
    ///      kingdoms and places the clan as a mercenary with culture preference.
    ///   5. Once under mercenary service, vanilla barter / defection systems
    ///      naturally handle potential vassal promotion.
    ///
    /// Safety nets:
    ///   - <c>DestroyClanAction</c> prefixes catch Path B (leader death) destruction.
    ///   - <c>DeactivateKingdom</c> postfix logs any clans missed by the event handler.
    ///
    /// Persistence:
    ///   Dictionary&lt;string, float&gt; keyed by Clan.StringId, value = rescue day.
    ///   Survives save/load via SyncData.
    ///
    /// Mod removal safety:
    ///   Rescued clans are never destroyed. Without this behavior, they remain as
    ///   independent clans forever — vanilla handles them on leader death.
    /// </summary>
    public sealed class B1071_ClanSurvivalBehavior : CampaignBehaviorBase
    {
        public static B1071_ClanSurvivalBehavior? Instance { get; internal set; }

        private static B1071_McmSettings Settings =>
            B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        // Key = Clan.StringId, Value = rescue day (CampaignTime.CurrentTime at time of rescue).
        private Dictionary<string, float> _rescuedClans = new();

        // When true, our own placement code is in progress — skip any grace period guards.
        internal bool _bypassGraceGuard;

        // ── Public API (called by patch) ─────────────────────────────────

        /// <summary>
        /// Registers a clan as rescued. Called from <see cref="B1071_ClanSurvivalPatch"/>.
        /// </summary>
        public void RegisterRescuedClan(Clan clan)
        {
            if (clan == null) return;
            _rescuedClans[clan.StringId] = (float)CampaignTime.Now.ToDays;
        }

        /// <summary>
        /// Returns true if the specified clan is in its post-rescue grace period
        /// and should not join any kingdom yet.
        /// </summary>
        public bool IsInGracePeriod(Clan clan)
        {
            if (clan == null) return false;
            if (!_rescuedClans.TryGetValue(clan.StringId, out float rescueDay)) return false;
            float elapsed = (float)CampaignTime.Now.ToDays - rescueDay;
            return elapsed < Settings.ClanSurvivalGracePeriodDays;
        }

        /// <summary>
        /// Returns the number of clans currently tracked (rescued and awaiting placement).
        /// </summary>
        public int TrackedClanCount => _rescuedClans.Count;

        // ── CampaignBehaviorBase ─────────────────────────────────────────

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, OnClanChangedKingdom);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("b1071_rescuedClans", ref _rescuedClans);
            _rescuedClans ??= new Dictionary<string, float>();
        }

        // ── Lifecycle ────────────────────────────────────────────────────

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            Instance = this;
        }

        // ── PRIMARY RESCUE: ClanChangedKingdom event ────────────────────
        //
        //  This fires AFTER vanilla removes each clan from a dying kingdom
        //  (ChangeKingdomAction.ApplyInternal with LeaveByKingdomDestruction).
        //  At this point:
        //    - clan.Kingdom is already null (clan has left)
        //    - clan inherited the dying kingdom's wars
        //    - Kingdom.DeactivateKingdom() has NOT been called yet
        //    - Other clans of the same kingdom may not have left yet
        //
        //  We immediately clear inherited wars and register for grace period.

        private void OnClanChangedKingdom(
            Clan clan,
            Kingdom oldKingdom,
            Kingdom newKingdom,
            ChangeKingdomAction.ChangeKingdomActionDetail detail,
            bool showNotification)
        {
            try
            {
                if (!Settings.EnableClanSurvival) return;

                // Only handle "kingdom lost all settlements" departures
                if (detail != ChangeKingdomAction.ChangeKingdomActionDetail.LeaveByKingdomDestruction)
                    return;

                if (clan == null || clan.IsEliminated) return;
                if (clan == Clan.PlayerClan) return;
                if (clan.IsBanditFaction) return;

                string clanName = clan.Name?.ToString() ?? clan.StringId;
                string kingdomName = oldKingdom?.Name?.ToString() ?? "unknown";
                int heroCount = clan.Heroes.Count(h => h.IsAlive);

                Debug.Print($"[Byzantium1071][ClanSurvival][Event] " +
                    $"Detected {clanName} leaving dying {kingdomName} " +
                    $"(detail: LeaveByKingdomDestruction, heroes: {heroCount}).");

                // Eligibility: must have living adult lord/hero
                var livingAdults = clan.Heroes
                    .Where(h => h.IsAlive && !h.IsChild && (h.IsLord || h.IsMinorFactionHero))
                    .ToList();

                if (livingAdults.Count == 0)
                {
                    Debug.Print($"[Byzantium1071][ClanSurvival][Event] SKIP {clanName}: " +
                        $"no living adult lords/heroes.");
                    return;
                }

                // If leader is dead, try heir promotion
                if (clan.Leader == null || !clan.Leader.IsAlive)
                {
                    var heirs = clan.GetHeirApparents();
                    if (heirs.Count == 0)
                    {
                        Debug.Print($"[Byzantium1071][ClanSurvival][Event] SKIP {clanName}: " +
                            $"leader dead, no heir apparents.");
                        return;
                    }
                    try
                    {
                        ChangeClanLeaderAction.ApplyWithoutSelectedNewLeader(clan);
                    }
                    catch (Exception ex)
                    {
                        Debug.Print($"[Byzantium1071][ClanSurvival][Event] SKIP {clanName}: " +
                            $"heir promotion threw: {ex.Message}");
                        return;
                    }
                    if (clan.Leader == null || !clan.Leader.IsAlive)
                    {
                        Debug.Print($"[Byzantium1071][ClanSurvival][Event] SKIP {clanName}: " +
                            $"heir promotion failed.");
                        return;
                    }
                    Debug.Print($"[Byzantium1071][ClanSurvival][Event] {clanName} heir promoted to {clan.Leader.Name}.");
                }

                // ── Clear inherited wars ──
                int warsCleared = 0;
                try
                {
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
                            catch { /* best effort — some wars may resist */ }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.Print($"[Byzantium1071][ClanSurvival][Event] War clearing error for {clanName}: {ex.Message}");
                }

                // ── Register for grace period ──
                RegisterRescuedClan(clan);

                // Mark as rescued in shared state (prevents double-processing by safety nets)
                B1071_ClanSurvivalPatch._alreadyRescued.Add(clan.StringId);

                Debug.Print($"[Byzantium1071][ClanSurvival][Event] RESCUED {clanName}: " +
                    $"cleared {warsCleared} inherited war(s), " +
                    $"grace period {Settings.ClanSurvivalGracePeriodDays} days, " +
                    $"leader: {clan.Leader?.Name}, heroes: {livingAdults.Count}.");

                B1071_VerboseLog.Log("ClanSurvival",
                    $"Rescued {clanName} ({livingAdults.Count} heroes, leader: {clan.Leader?.Name}) " +
                    $"from destruction of {kingdomName}. Cleared {warsCleared} war(s). " +
                    $"Grace period: {Settings.ClanSurvivalGracePeriodDays} days.");
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival][Event] Fatal error: {ex}");
            }
        }

        // ── Daily Tick: process grace periods and placement ──────────────

        private void OnDailyTick()
        {
            try
            {
                if (!Settings.EnableClanSurvival) return;
                if (_rescuedClans.Count == 0) return;

                float gracePeriodDays = Settings.ClanSurvivalGracePeriodDays;
                float currentDay = (float)CampaignTime.Now.ToDays;

                // Iterate a snapshot to allow modification during iteration
                foreach (var kvp in _rescuedClans.ToList())
                {
                    string clanId = kvp.Key;
                    float rescueDay = kvp.Value;
                    float elapsedDays = currentDay - rescueDay;

                    Clan? clan = Clan.FindFirst(c => c.StringId == clanId);
                    if (clan == null)
                    {
                        // Clan no longer exists in campaign — remove tracking
                        _rescuedClans.Remove(clanId);
                        B1071_VerboseLog.Log("ClanSurvival",
                            $"Removed tracking for {clanId}: clan no longer exists in campaign.");
                        continue;
                    }

                    // If clan was eliminated by another path (e.g. leader died of old age), stop tracking
                    if (clan.IsEliminated)
                    {
                        _rescuedClans.Remove(clanId);
                        B1071_VerboseLog.Log("ClanSurvival",
                            $"Stopped tracking {clan.Name}: clan was eliminated via another path.");
                        continue;
                    }

                    // If clan already joined a kingdom during the grace period,
                    // force it back to independent. Vanilla AI can recruit clans
                    // very quickly — we need to enforce the grace period.
                    if (clan.Kingdom != null)
                    {
                        if (elapsedDays < gracePeriodDays)
                        {
                            // Still in grace period — eject from kingdom
                            Kingdom joinedKingdom = clan.Kingdom;
                            try
                            {
                                clan.Kingdom = null;

                                // Clear any wars inherited from the brief kingdom membership
                                foreach (IFaction enemy in clan.FactionsAtWarWith.ToList())
                                {
                                    if (clan != enemy &&
                                        !TaleWorlds.CampaignSystem.Campaign.Current.Models.DiplomacyModel
                                            .IsAtConstantWar(clan, enemy))
                                    {
                                        try { MakePeaceAction.Apply(clan, enemy); }
                                        catch { /* best effort */ }
                                    }
                                }

                                Debug.Print($"[Byzantium1071][ClanSurvival] Grace period enforced: " +
                                    $"ejected {clan.Name} from {joinedKingdom.Name} " +
                                    $"(day {(int)elapsedDays}/{(int)gracePeriodDays}).");
                            }
                            catch (Exception ex)
                            {
                                Debug.Print($"[Byzantium1071][ClanSurvival] Grace period enforcement " +
                                    $"error for {clan.Name}: {ex.Message}");
                            }
                            continue;
                        }
                        else
                        {
                            // Grace period expired and clan already found a kingdom — done
                            _rescuedClans.Remove(clanId);
                            B1071_VerboseLog.Log("ClanSurvival",
                                $"Stopped tracking {clan.Name}: already joined {clan.Kingdom.Name}" +
                                $" ({(clan.IsUnderMercenaryService ? "mercenary" : "vassal")}).");
                            continue;
                        }
                    }

                    // Still in grace period?
                    if (elapsedDays < gracePeriodDays)
                    {
                        // Log only on day 1, midpoint, and last day before placement
                        if (elapsedDays < 1.5f ||
                            Math.Abs(elapsedDays - gracePeriodDays / 2f) < 1f ||
                            gracePeriodDays - elapsedDays < 1.5f)
                        {
                            B1071_VerboseLog.Log("ClanSurvival",
                                $"{clan.Name} independence day {(int)elapsedDays}/{(int)gracePeriodDays}. " +
                                $"Leader: {clan.Leader?.Name}, Heroes alive: {clan.Heroes.Count(h => h.IsAlive)}.");
                        }
                        continue;
                    }

                    // ── Grace period expired — attempt placement ──
                    _bypassGraceGuard = true;
                    try
                    {
                        TryPlaceClanAsMercenary(clan);
                    }
                    finally
                    {
                        _bypassGraceGuard = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival] DailyTick error: {ex}");
            }
        }

        // ── Mercenary Placement ──────────────────────────────────────────

        /// <summary>
        /// Evaluates all surviving kingdoms and places the clan as a mercenary
        /// in the best-scoring one. 
        ///
        /// Scoring factors (mirrors vanilla <c>GetScoreOfKingdomToGetClan</c>
        /// with an amplified culture match bonus):
        ///   - Relation between ruling clan and the rescued clan
        ///   - Culture match (weighted by ClanSurvivalCultureWeight MCM setting)
        ///   - Military strength of the clan (commander limit + troop strength)
        ///   - Power ratio to enemies (kingdoms under pressure want help more)
        ///   - Leader reliability
        /// </summary>
        private void TryPlaceClanAsMercenary(Clan clan)
        {
            try
            {
                if (clan.Leader == null || !clan.Leader.IsAlive)
                {
                    B1071_VerboseLog.Log("ClanSurvival",
                        $"{clan.Name}: leader dead at placement time. Removing from tracking.");
                    _rescuedClans.Remove(clan.StringId);
                    return;
                }

                float cultureWeight = Settings.ClanSurvivalCultureWeight;
                Kingdom? bestKingdom = null;
                float bestScore = float.MinValue;

                foreach (Kingdom kingdom in Kingdom.All)
                {
                    if (kingdom.IsEliminated) continue;
                    if (kingdom.Clans.Count == 0) continue;
                    if (kingdom.RulingClan == null) continue;

                    // Don't join a kingdom that's at war with the clan
                    if (FactionManager.IsAtWarAgainstFaction(clan, kingdom)) continue;

                    // Score: base vanilla formula with amplified culture weight
                    float score = ScoreKingdomForClan(kingdom, clan, cultureWeight);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestKingdom = kingdom;
                    }
                }

                if (bestKingdom == null)
                {
                    // No eligible kingdom found — keep trying next day
                    B1071_VerboseLog.Log("ClanSurvival",
                        $"{clan.Name}: no eligible kingdom for mercenary service. Will retry tomorrow.");
                    return;
                }

                // Determine mercenary award multiplier.
                // Vanilla uses MinorFactionsModel.GetMercenaryAwardFactorToJoinKingdom for minor
                // factions. For major clans becoming mercenaries, we use a reasonable default (50).
                int awardMultiplier = 50;
                try
                {
                    awardMultiplier = TaleWorlds.CampaignSystem.Campaign.Current.Models.MinorFactionsModel
                        .GetMercenaryAwardFactorToJoinKingdom(clan, bestKingdom);
                    awardMultiplier = Math.Max(20, Math.Min(100, awardMultiplier));
                }
                catch
                {
                    awardMultiplier = 50; // fallback
                }

                // Mercenary contracts typically last ~30 days
                CampaignTime stayUntil = CampaignTime.DaysFromNow(30f);

                // Join as mercenary
                ChangeKingdomAction.ApplyByJoinFactionAsMercenary(
                    clan, bestKingdom, stayUntil, awardMultiplier, showNotification: true);

                _rescuedClans.Remove(clan.StringId);

                string cultureParity = clan.Culture == bestKingdom.Culture
                    ? "same culture" : "different culture";

                B1071_VerboseLog.Log("ClanSurvival",
                    $"{clan.Name} joined {bestKingdom.Name} as mercenary " +
                    $"({cultureParity}, score: {bestScore:F0}, award: {awardMultiplier}x). " +
                    $"Contract valid for ~30 days.");

                Debug.Print($"[Byzantium1071][ClanSurvival] Placement complete: {clan.Name} → " +
                    $"{bestKingdom.Name} (mercenary, {cultureParity}).");
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival] Placement error for {clan.Name}: {ex}");
            }
        }

        /// <summary>
        /// Scores how attractive a kingdom is for a rescued clan to join as mercenary.
        /// Modeled after vanilla <c>DefaultDiplomacyModel.GetScoreOfKingdomToGetClan</c>
        /// but with a configurable culture match weight.
        /// </summary>
        private static float ScoreKingdomForClan(Kingdom kingdom, Clan clan, float cultureWeight)
        {
            // Relation factor (0.33 to 2.0)
            int relation = FactionManager.GetRelationBetweenClans(kingdom.RulingClan, clan);
            float relationFactor = MathF.Min(2f, MathF.Max(0.33f, 1f + 0.02f * relation));

            // Culture factor: vanilla uses 1.0 (mismatch) or 2.0 (match).
            // We amplify the match bonus with the MCM cultureWeight setting.
            float cultureFactor = kingdom.Culture == clan.Culture
                ? 1f + cultureWeight  // e.g. 1 + 2.0 = 3.0 for same culture
                : 1f;                 // no bonus for mismatch

            // Military value of the clan
            int commanderLimit = clan.CommanderLimit;
            float militaryValue = (clan.CurrentTotalStrength + 150f * commanderLimit) * 20f;

            // Power ratio adjustment (kingdoms under pressure value clans more)
            float powerRatio = FactionHelper.GetPowerRatioToEnemies(kingdom);
            float pressureFactor = 1f / MathF.Max(0.4f, MathF.Min(2.5f, MathF.Sqrt(powerRatio)));
            militaryValue *= pressureFactor;

            // Leader reliability factor
            float reliability = HeroHelper.CalculateReliabilityConstant(clan.Leader);

            return militaryValue * relationFactor * cultureFactor * reliability;
        }
    }
}
