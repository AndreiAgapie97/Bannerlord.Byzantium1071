using System;
using System.Collections.Generic;
using System.Linq;
using Byzantium1071.Campaign.Patches;
using Byzantium1071.Campaign.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
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
    /// leaves and registers the clan for tracking. Inherited wars are NOT cleared
    /// here (unsafe re-entrancy); the daily tick handles war clearing.
    ///
    /// Lifecycle:
    ///   1. Kingdom loses last settlement → vanilla removes clans one by one
    ///      via <c>ChangeKingdomAction(detail: LeaveByKingdomDestruction)</c>.
    ///   2. Our <c>OnClanChangedKingdom</c> event handler intercepts each departure
    ///      and registers the clan for tracking. War clearing is deferred.
    ///   3. While the clan is independent (Kingdom == null), the daily tick
    ///      clears inherited wars and any new wars that accumulate. Kingdomless
    ///      clans should be at peace with everyone.
    ///   4. Vanilla handles recruitment naturally — clans join kingdoms as vassals
    ///      or mercenaries via vanilla barter / AI defection systems.
    ///   5. Once the clan joins a kingdom, tracking stops.
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
        /// Returns true if the specified clan is currently tracked as rescued.
        /// </summary>
        public bool IsTracked(Clan clan)
        {
            if (clan == null) return false;
            return _rescuedClans.ContainsKey(clan.StringId);
        }

        /// <summary>
        /// Returns the number of clans currently tracked (rescued, awaiting kingdom join).
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
            B1071_ClanSurvivalPatch._alreadyRescued.Clear();
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
        //  We register the clan for tracking; the daily tick clears inherited wars.

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

                // ── Register for tracking (daily tick will clear inherited wars) ──
                //
                // IMPORTANT: Do NOT call MakePeaceAction.Apply() here. This event fires from
                // within ChangeKingdomAction.ApplyInternal, and calling another Campaign action
                // (MakePeaceAction) from inside a Campaign action's event callback is unsafe.
                // It can leave Bannerlord's diplomatic state machine inconsistent, which manifests
                // as save corruption when fast-forwarding with BetterTime + TimeLord.
                // The daily tick (OnDailyTick) already clears inherited wars every campaign day,
                // so the clan will be at peace within 1 campaign day.
                //
                // WHY THE 1-DAY GAP IS SAFE (wars cleared on next daily tick, not instantly):
                //
                //   Concern: a kingdom could recruit this clan before wars are cleared, causing
                //   the new kingdom's leader to lose relations for "accepting a clan at war."
                //
                //   This does NOT happen, verified by decompiling vanilla (v1.3.15):
                //
                //   1. DiplomaticBartersBehavior.DailyTickClan guards against it:
                //      AI will NOT recruit a clan that has wars misaligned with the kingdom
                //      (unless the kingdom is 10× stronger than the war enemy).
                //
                //   2. ChangeKingdomAction.ApplyInternal calls
                //      FactionHelper.AdjustFactionStancesForClanJoiningKingdom() BEFORE
                //      setting clan.Kingdom. This iterates ALL clan stances and calls
                //      MakePeaceAction.Apply() for any war the new kingdom doesn't share.
                //      Wars are cleared before the clan becomes a member.
                //
                //   3. MakePeaceAction.ApplyInternal does NOT cause relation changes —
                //      it only calls FactionManager.SetNeutral + fires OnMakePeace event.
                //
                //   Net result: even if recruitment happens before our daily tick,
                //   vanilla's own join logic clears misaligned wars with zero relation cost.
                RegisterRescuedClan(clan);

                // Mark as rescued in shared state (prevents double-processing by safety nets)
                B1071_ClanSurvivalPatch._alreadyRescued.Add(clan.StringId);

                Debug.Print($"[Byzantium1071][ClanSurvival][Event] RESCUED {clanName}: " +
                    $"leader: {clan.Leader?.Name}, heroes: {livingAdults.Count}. " +
                    $"Inherited wars will be cleared on next daily tick.");

                B1071_VerboseLog.Log("ClanSurvival",
                    $"Rescued {clanName} ({livingAdults.Count} heroes, leader: {clan.Leader?.Name}) " +
                    $"from destruction of {kingdomName}. " +
                    $"Tracking until kingdom join (wars cleared by daily tick).");
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival][Event] Fatal error: {ex}");
            }
        }

        // ── Daily Tick: clear wars for independent rescued clans ─────────

        private void OnDailyTick()
        {
            try
            {
                if (!Settings.EnableClanSurvival) return;
                if (_rescuedClans.Count == 0) return;

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
                        _rescuedClans.Remove(clanId);
                        B1071_VerboseLog.Log("ClanSurvival",
                            $"Removed tracking for {clanId}: clan no longer exists.");
                        continue;
                    }

                    if (clan.IsEliminated)
                    {
                        _rescuedClans.Remove(clanId);
                        B1071_VerboseLog.Log("ClanSurvival",
                            $"Stopped tracking {clan.Name}: eliminated.");
                        continue;
                    }

                    // Clan joined a kingdom — stop tracking, they're vanilla's problem now
                    if (clan.Kingdom != null)
                    {
                        _rescuedClans.Remove(clanId);
                        string role = clan.IsUnderMercenaryService ? "mercenary" : "vassal";
                        Debug.Print($"[Byzantium1071][ClanSurvival] {clan.Name} joined " +
                            $"{clan.Kingdom.Name} as {role} after {(int)elapsedDays} day(s). Done.");
                        B1071_VerboseLog.Log("ClanSurvival",
                            $"{clan.Name} joined {clan.Kingdom.Name} as {role} " +
                            $"after {(int)elapsedDays} day(s) of independence.");
                        continue;
                    }

                    // ── Still independent: clear any wars that accumulated ──
                    // Kingdomless clans should be at peace with everyone.
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

                        if (warsCleared > 0)
                        {
                            Debug.Print($"[Byzantium1071][ClanSurvival] Cleared {warsCleared} " +
                                $"stale war(s) for independent {clan.Name} (day {(int)elapsedDays}).");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.Print($"[Byzantium1071][ClanSurvival] War clearing error " +
                            $"for {clan.Name}: {ex.Message}");
                    }

                    // Periodic status log (day 1 and every 10 days)
                    if (elapsedDays < 1.5f || (int)elapsedDays % 10 == 0)
                    {
                        B1071_VerboseLog.Log("ClanSurvival",
                            $"{clan.Name} independent day {(int)elapsedDays}. " +
                            $"Leader: {clan.Leader?.Name}, Heroes: {clan.Heroes.Count(h => h.IsAlive)}.");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival] DailyTick error: {ex}");
            }
        }
    }
}
