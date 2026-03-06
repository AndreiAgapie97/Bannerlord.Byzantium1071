using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Byzantium1071.Campaign.Patches;
using Byzantium1071.Campaign.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
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
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
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

            // ── Startup scan: rescue homeless rebel clans ────────────────
            //
            // Vanilla's RebellionsCampaignBehavior.DailyTickClan kills heroes of
            // rebel clans that have IsRebelClan=true and Settlements.Count==0.
            // If the mod is installed mid-campaign, or the session is loaded from
            // a save where a rebel clan already lost its settlement, our
            // OnSettlementOwnerChanged never fires. By the time the first daily
            // tick runs, vanilla has already killed the heroes.
            //
            // This scan runs once at session start and normalizes (IsRebelClan→false,
            // IsMinorFaction→true) all homeless rebel clans with living heroes,
            // preventing vanilla's DailyTickClan from killing them.
            try
            {
                if (Settings.EnableClanSurvival)
                {
                    ScanAndRescueHomelessRebelClans();
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival] Startup scan error: {ex.Message}");
            }
        }

        /// <summary>
        /// Scans all clans for homeless rebel clans with living heroes and rescues them.
        /// Called once at session start to cover mid-campaign install and save/load gaps.
        /// </summary>
        private void ScanAndRescueHomelessRebelClans()
        {
            int rescued = 0;
            foreach (Clan clan in Clan.All.ToList())
            {
                if (clan == null || clan.IsEliminated) continue;
                if (clan == Clan.PlayerClan) continue;
                if (clan.IsBanditFaction) continue;
                if (!IsRebelClanOrigin(clan)) continue;

                // Already tracked from a previous session's save data
                if (IsTracked(clan)) continue;

                // Only care about homeless rebels (lost all settlements)
                if (clan.Settlements != null && clan.Settlements.Any()) continue;

                // If still in a kingdom they are an unlanded vassal — not in danger.
                // A rebel-origin clan whose rebellion succeeded may own fiefs and later
                // lose them; their StringId still contains "rebel_clan" but they must
                // not be normalized here.
                if (clan.Kingdom != null) continue;

                // Must have living adult heroes
                var livingAdults = clan.Heroes
                    .Where(h => h.IsAlive && !h.IsChild && (h.IsLord || h.IsMinorFactionHero))
                    .ToList();
                if (livingAdults.Count == 0) continue;

                string clanName = clan.Name?.ToString() ?? clan.StringId;
                Debug.Print($"[Byzantium1071][ClanSurvival][StartupScan] " +
                    $"Found homeless rebel clan '{clanName}' with {livingAdults.Count} " +
                    $"living heroes. Normalizing and rescuing...");
                B1071_SessionFileLog.WriteTagged("ClanSurvival",
                    $"[StartupScan] Homeless rebel '{clanName}' ({livingAdults.Count} heroes). Rescuing.");

                // Promote heir if leader is dead
                if (clan.Leader == null || !clan.Leader.IsAlive)
                {
                    try
                    {
                        ChangeClanLeaderAction.ApplyWithoutSelectedNewLeader(clan);
                    }
                    catch (Exception ex)
                    {
                        Debug.Print($"[Byzantium1071][ClanSurvival][StartupScan] " +
                            $"Heir promotion failed for {clanName}: {ex.Message}");
                        continue;
                    }
                    if (clan.Leader == null || !clan.Leader.IsAlive) continue;
                }

                NormalizeRebelClan(clan, "StartupScan");
                RegisterRescuedClan(clan);
                B1071_ClanSurvivalPatch._alreadyRescued.Add(clan.StringId);
                rescued++;

                B1071_VerboseLog.Log("ClanSurvival",
                    $"Startup rescue: {clanName} ({livingAdults.Count} heroes, " +
                    $"leader: {clan.Leader?.Name}). Normalized as independent minor faction.");
            }

            if (rescued > 0)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival][StartupScan] " +
                    $"Rescued {rescued} homeless rebel clan(s) at session start.");
                B1071_SessionFileLog.WriteTagged("ClanSurvival",
                    $"[StartupScan] Rescued {rescued} homeless rebel clan(s).");
            }
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
                B1071_SessionFileLog.WriteTagged("ClanSurvival",
                    $"Detected {clanName} leaving dying {kingdomName} (heroes: {heroCount})");

                // Eligibility: must have living adult lord/hero
                var livingAdults = clan.Heroes
                    .Where(h => h.IsAlive && !h.IsChild && (h.IsLord || h.IsMinorFactionHero))
                    .ToList();

                if (livingAdults.Count == 0)
                {
                    Debug.Print($"[Byzantium1071][ClanSurvival][Event] SKIP {clanName}: " +
                        $"no living adult lords/heroes.");
                    B1071_SessionFileLog.WriteTagged("ClanSurvival",
                        $"SKIP {clanName}: no living adult lords/heroes");
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
                        B1071_SessionFileLog.WriteTagged("ClanSurvival",
                            $"SKIP {clanName}: leader dead, no heir apparents");
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
                        B1071_SessionFileLog.WriteTagged("ClanSurvival",
                            $"ERROR SKIP {clanName}: heir promotion threw: {ex.Message}");
                        return;
                    }
                    if (clan.Leader == null || !clan.Leader.IsAlive)
                    {
                        Debug.Print($"[Byzantium1071][ClanSurvival][Event] SKIP {clanName}: " +
                            $"heir promotion failed.");
                        return;
                    }
                    Debug.Print($"[Byzantium1071][ClanSurvival][Event] {clanName} heir promoted to {clan.Leader.Name}.");
                    B1071_SessionFileLog.WriteTagged("ClanSurvival",
                        $"{clanName} heir promoted to {clan.Leader.Name}");
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
                B1071_SessionFileLog.WriteTagged("ClanSurvival",
                    $"FATAL ERROR in OnClanChangedKingdom: {ex.Message}");
            }
        }

        // ── REBEL RESCUE: Settlement ownership change event ─────────────
        //
        //  When a settlement changes hands (siege, barter, rebellion resolution),
        //  we check if the PREVIOUS owner's clan is a rebel clan that just lost
        //  its last settlement. If so, we rescue the clan rather than letting
        //  vanilla destroy it.
        //
        //  Why this event: Rebel clans don't go through the normal kingdom
        //  destruction pipeline. They have Kingdom == null and are not covered
        //  by Path A (OnClanChangedKingdom) or the kingdom-check in HandleDestroyClan.
        //  By listening here, we proactively rescue rebel clans before vanilla's
        //  daily-tick RebellionsCampaignBehavior calls DestroyClanAction on them.
        //
        //  SAFETY: No MakePeaceAction or other Campaign actions are called inline.
        //  We only register the clan for tracking. The daily tick clears wars.
        //  This avoids the TimeLord/BetterTime re-entrancy bug (see v0.2.6.1).

        private void OnSettlementOwnerChanged(
            Settlement settlement,
            bool openToClaim,
            Hero newOwner,
            Hero oldOwner,
            Hero capturerHero,
            ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            try
            {
                if (!Settings.EnableClanSurvival) return;
                if (oldOwner?.Clan == null) return;

                Clan clan = oldOwner.Clan;

                // Only interested in rebel-origin clans
                if (!IsRebelClanOrigin(clan)) return;

                // Already rescued?
                if (B1071_ClanSurvivalPatch._alreadyRescued.Contains(clan.StringId)) return;
                if (IsTracked(clan)) return;

                // Still has settlements? Not yet homeless.
                if (clan.Settlements != null && clan.Settlements.Count() > 0) return;

                // If the clan is still a member of a kingdom it is merely an unlanded vassal —
                // vanilla handles unlanded lords naturally (kingdom grants new fiefs over time).
                // Rebel-origin clans whose rebellion SUCCEEDED receive a fief and may later lose
                // it in a siege; their StringId still contains "rebel_clan" but they are full
                // vassals and must not be normalized or tracked by us.
                if (clan.Kingdom != null) return;

                string clanName = clan.Name?.ToString() ?? clan.StringId;
                string settlementName = settlement?.Name?.ToString() ?? "unknown";

                Debug.Print($"[Byzantium1071][ClanSurvival][RebelRescue] " +
                    $"Rebel clan {clanName} lost last settlement ({settlementName}). " +
                    $"Checking rescue eligibility...");
                B1071_SessionFileLog.WriteTagged("ClanSurvival",
                    $"Rebel clan {clanName} lost last settlement ({settlementName}). " +
                    $"Checking rescue eligibility...");

                // Eligibility: must have living adult lord/hero
                if (!B1071_ClanSurvivalPatch.IsClanEligibleForRescue(clan, null, "RebelRescue"))
                {
                    Debug.Print($"[Byzantium1071][ClanSurvival][RebelRescue] " +
                        $"SKIP {clanName}: failed eligibility checks.");
                    B1071_SessionFileLog.WriteTagged("ClanSurvival",
                        $"Rebel rescue SKIP {clanName}: failed eligibility checks.");
                    return;
                }

                // ── Normalize rebel state ──
                NormalizeRebelClan(clan, "RebelRescue");

                // ── Register for tracking (daily tick will clear any wars) ──
                //
                // IMPORTANT: Do NOT call MakePeaceAction.Apply() here. This event fires
                // from within ChangeOwnerOfSettlementAction, and calling another Campaign
                // action inline is unsafe (TimeLord/BetterTime re-entrancy — see v0.2.6.1).
                RegisterRescuedClan(clan);
                B1071_ClanSurvivalPatch._alreadyRescued.Add(clan.StringId);

                int heroCount = clan.Heroes.Count(h => h.IsAlive);
                Debug.Print($"[Byzantium1071][ClanSurvival][RebelRescue] RESCUED {clanName}: " +
                    $"leader: {clan.Leader?.Name}, heroes: {heroCount}. " +
                    $"Wars will be cleared on next daily tick.");

                B1071_VerboseLog.Log("ClanSurvival",
                    $"Rescued rebel clan {clanName} ({heroCount} heroes, leader: {clan.Leader?.Name}) " +
                    $"after losing last settlement ({settlementName}). " +
                    $"IsMinorFaction set to true for frontier revenue. " +
                    $"Tracking until kingdom join (wars cleared by daily tick).");
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival][RebelRescue] Fatal error: {ex}");
            }
        }

        // ── Rebel clan helpers ───────────────────────────────────────────

        /// <summary>
        /// Returns true if the clan is a rebel-origin clan, either freshly spawned
        /// (IsRebelClan == true) or a normalized former-rebel whose StringId still
        /// contains the rebel marker.
        /// </summary>
        internal static bool IsRebelClanOrigin(Clan clan)
        {
            if (clan == null) return false;
            if (clan.IsRebelClan) return true;
            // Vanilla rebel clans have StringId like "town_A5_rebel_clan"
            if (clan.StringId != null && clan.StringId.Contains("rebel_clan")) return true;
            return false;
        }

        /// <summary>
        /// Normalizes a rebel clan for independent survival:
        ///   1. Sets IsRebelClan = false (public setter)
        ///   2. Sets IsMinorFaction = true (private setter, via reflection) — enables
        ///      frontier revenue eligibility
        ///   3. Removes from vanilla's _rebelClansAndDaysPassedAfterCreation tracking
        ///      (via reflection on RebellionsCampaignBehavior)
        /// </summary>
        internal static void NormalizeRebelClan(Clan clan, string context)
        {
            string clanName = clan.Name?.ToString() ?? clan.StringId;

            // 1. Clear rebel flag
            try
            {
                bool wasRebel = clan.IsRebelClan;
                clan.IsRebelClan = false;
                Debug.Print($"[Byzantium1071][ClanSurvival][{context}] " +
                    $"{clanName}: IsRebelClan {wasRebel} → false");
                B1071_SessionFileLog.WriteTagged("ClanSurvival",
                    $"{clanName}: IsRebelClan {wasRebel} → false");
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival][{context}] " +
                    $"Failed to clear IsRebelClan for {clanName}: {ex.Message}");
            }

            // 2. Set IsMinorFaction = true (private setter — requires reflection)
            //    This enables frontier revenue via B1071_MinorFactionIncomePatch
            try
            {
                bool wasMF = clan.IsMinorFaction;
                PropertyInfo? prop = typeof(Clan).GetProperty(
                    nameof(Clan.IsMinorFaction),
                    BindingFlags.Instance | BindingFlags.Public);

                if (prop != null)
                {
                    MethodInfo? setter = prop.GetSetMethod(nonPublic: true);
                    if (setter != null)
                    {
                        setter.Invoke(clan, new object[] { true });
                        Debug.Print($"[Byzantium1071][ClanSurvival][{context}] " +
                            $"{clanName}: IsMinorFaction {wasMF} → true (reflection)");
                        B1071_SessionFileLog.WriteTagged("ClanSurvival",
                            $"{clanName}: IsMinorFaction {wasMF} → true (frontier revenue enabled)");
                    }
                    else
                    {
                        Debug.Print($"[Byzantium1071][ClanSurvival][{context}] " +
                            $"WARNING: IsMinorFaction setter not found for {clanName}");
                    }
                }
                else
                {
                    Debug.Print($"[Byzantium1071][ClanSurvival][{context}] " +
                        $"WARNING: IsMinorFaction property not found on Clan type");
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival][{context}] " +
                    $"Failed to set IsMinorFaction for {clanName}: {ex.Message}");
            }

            // 3. Remove from vanilla's RebellionsCampaignBehavior tracking
            //    Field: Dictionary<Clan, float> _rebelClansAndDaysPassedAfterCreation
            //    If not removed, vanilla's daily tick may still try to manage/destroy this clan.
            try
            {
                var rebellionBehavior = TaleWorlds.CampaignSystem.Campaign.Current?
                    .GetCampaignBehavior<TaleWorlds.CampaignSystem.CampaignBehaviors.RebellionsCampaignBehavior>();

                if (rebellionBehavior != null)
                {
                    FieldInfo? field = typeof(TaleWorlds.CampaignSystem.CampaignBehaviors.RebellionsCampaignBehavior)
                        .GetField("_rebelClansAndDaysPassedAfterCreation",
                            BindingFlags.Instance | BindingFlags.NonPublic);

                    if (field != null)
                    {
                        var dict = field.GetValue(rebellionBehavior) as System.Collections.IDictionary;
                        if (dict != null && dict.Contains(clan))
                        {
                            dict.Remove(clan);
                            Debug.Print($"[Byzantium1071][ClanSurvival][{context}] " +
                                $"Removed {clanName} from RebellionsCampaignBehavior tracking");
                            B1071_SessionFileLog.WriteTagged("ClanSurvival",
                                $"Removed {clanName} from vanilla rebel tracking dictionary");
                        }
                        else
                        {
                            Debug.Print($"[Byzantium1071][ClanSurvival][{context}] " +
                                $"{clanName} not found in RebellionsCampaignBehavior tracking (OK)");
                        }
                    }
                    else
                    {
                        Debug.Print($"[Byzantium1071][ClanSurvival][{context}] " +
                            $"WARNING: _rebelClansAndDaysPassedAfterCreation field not found " +
                            $"(API change in this game version?)");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival][{context}] " +
                    $"Failed to remove {clanName} from rebellion tracking: {ex.Message}");
            }

            // 4. Rename clan from settlement-based rebel name to leader-derived warband name
            //    Vanilla names rebel clans after the settlement (e.g. "Pen Cannoc rebels").
            //    When the same settlement rebels twice, we get duplicate clan names.
            //    Rename to "{LeaderName}'s Warband" for uniqueness and clarity.
            try
            {
                Hero? leader = clan.Leader;
                if (leader != null)
                {
                    string oldName = clan.Name?.ToString() ?? clan.StringId;
                    var newName = new TaleWorlds.Localization.TextObject(
                        "{=b1071_rescued_clan_name}{LEADER_NAME}'s Warband");
                    newName.SetTextVariable("LEADER_NAME", leader.Name);

                    var newInformalName = new TaleWorlds.Localization.TextObject(
                        "{=b1071_rescued_clan_informal}{LEADER_NAME}'s Warband");
                    newInformalName.SetTextVariable("LEADER_NAME", leader.Name);

                    clan.ChangeClanName(newName, newInformalName);

                    Debug.Print($"[Byzantium1071][ClanSurvival][{context}] " +
                        $"Renamed '{oldName}' → '{newName}' (leader: {leader.Name})");
                    B1071_SessionFileLog.WriteTagged("ClanSurvival",
                        $"Renamed '{oldName}' → '{newName}' (leader-derived warband name)");
                }
                else
                {
                    Debug.Print($"[Byzantium1071][ClanSurvival][{context}] " +
                        $"Skipped rename for {clanName}: no living leader");
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][ClanSurvival][{context}] " +
                    $"Failed to rename {clanName}: {ex.Message}");
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
                            B1071_SessionFileLog.WriteTagged("ClanSurvival",
                                $"Cleared {warsCleared} war(s) for {clan.Name} (day {(int)elapsedDays})");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.Print($"[Byzantium1071][ClanSurvival] War clearing error " +
                            $"for {clan.Name}: {ex.Message}");
                        B1071_SessionFileLog.WriteTagged("ClanSurvival",
                            $"ERROR war clearing for {clan.Name}: {ex.Message}");
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
                B1071_SessionFileLog.WriteTagged("ClanSurvival",
                    $"FATAL ERROR in DailyTick: {ex.Message}");
            }
        }
    }
}
