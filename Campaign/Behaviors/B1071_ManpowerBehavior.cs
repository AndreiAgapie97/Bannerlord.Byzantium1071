using System;
using System.Collections.Generic;
using System.Reflection;
using Byzantium1071.Campaign.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.Behaviors
{
    /// <summary>
    /// WP5: Graded diplomacy pressure levels that replace hard exhaustion threshold cliffs.
    /// </summary>
    public enum DiplomacyPressureBand
    {
        Low,     // Normal operations
        Rising,  // Elevated peace bias, softer war penalties
        Crisis   // War declarations blocked, strong peace pressure, forced peace eligible
    }

    /// <summary>
    /// Regional manpower pool:
    /// - Villages do NOT have their own pool; they draw from their bound Town/Castle pool.
    /// - Seeded to Max on campaign start
    /// - Regens daily per pool-settlement (Town/Castle)
    /// - Recruitment consumes manpower; if insufficient, removes the extra troops immediately
    /// </summary>
    public sealed class B1071_ManpowerBehavior : CampaignBehaviorBase
    {
        public static B1071_ManpowerBehavior? Instance { get; internal set; }

        // NOTE: Key is POOL settlement StringId (Town/Castle). Villages map to their bound settlement pool.
        private readonly Dictionary<string, int> _manpowerByPoolId = new();

        // Save-friendly backing lists.
        private List<string> _savedIds = new();
        private List<int> _savedValues = new();

        // MCM settings live source. If MCM is unavailable for any reason, fall back to shared defaults.
        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        private bool _seeded;
        // Alert tracking: only fire crisis alerts on downward band transitions.
        private readonly Dictionary<string, int> _lastAlertBand = new();
        // Throttle AI logs: we only log when a pool drops to a lower “band” (75/50/25/0) or when manpower blocks recruitment.
        private readonly Dictionary<string, int> _aiPoolBandByPoolId = new();
        // War exhaustion per kingdom (key = kingdom StringId, value = 0..MaxExhaustionScore).
        private readonly Dictionary<string, float> _warExhaustion = new();
        // Last day a forced peace was applied per kingdom (key = kingdom StringId).
        private readonly Dictionary<string, float> _lastForcedPeaceDayByKingdom = new();
        // Per-pair truce expiry day after peace (key = normalized kingdomA|kingdomB).
        private readonly Dictionary<string, float> _truceExpiryByPair = new();
        // Raid drain dedupe (key = village StringId, value = campaign day) to avoid duplicate callbacks draining twice.
        private readonly Dictionary<string, int> _lastRaidDrainDayByVillageId = new();
        // Raid drain spent this day per pool (key = "poolId|day", value = spent manpower).
        private readonly Dictionary<string, int> _raidDrainSpentByPoolDay = new();
        // Delayed recovery (WP3): linear-decaying regen penalty per pool.
        private readonly Dictionary<string, float> _recoveryPenaltyBaseByPoolId = new();
        private readonly Dictionary<string, float> _recoveryPenaltyStartDayByPoolId = new();
        private readonly Dictionary<string, float> _recoveryPenaltyExpiryDayByPoolId = new();
        // WP5: per-kingdom pressure band with hysteresis (runtime-only, recalculated from exhaustion).
        private readonly Dictionary<string, DiplomacyPressureBand> _pressureBandByKingdom = new();
        // Runtime cache: average manpower ratio per kingdom, refreshed once per in-game day.
        // Eliminates repeated settlement iteration when exhaustion events fire in tight loops
        // (AccumulateBattleExhaustion per party, DetermineSupport per clan per vote).
        private readonly Dictionary<string, float> _manpowerRatioCacheById = new();
        private float _manpowerRatioCacheExpiryDay = -1f;
        // WP1 telemetry (runtime-only, non-persistent)
        private int _telemetryRaidDrainToday;
        private int _telemetrySiegeDrainToday;
        private int _telemetryBattleDrainToday;
        private float _telemetryExhaustionGainToday;
        private float _telemetryExhaustionDecayToday;
        private string _telemetryLastForcedPeace = "None";
        private string _telemetryLastTruce = "None";
        private string _telemetryLastDiplomacyDecision = "None";
        private readonly HashSet<string> _telemetryLoggedDiplomacy = new HashSet<string>();
        private string _telemetryLastRegenBreakdown = "n/a";
        private string _telemetryLastRegenPoolId = string.Empty;
        private List<string>? _exhaustionKeysScratch;
        private List<string>? _cleanupKeysScratch;
        private List<string> _savedExhaustionIds = new();
        private List<float> _savedExhaustionValues = new();
        private List<string> _savedForcedPeaceCooldownIds = new();
        private List<float> _savedForcedPeaceCooldownDays = new();
        private List<string> _savedTruceKeys = new();
        private List<float> _savedTruceExpiryDays = new();
        private List<string> _savedRaidDrainVillageIds = new();
        private List<int> _savedRaidDrainDays = new();
        private List<string> _savedRaidPoolDayKeys = new();
        private List<int> _savedRaidPoolDaySpent = new();
        private List<string> _savedRecoveryPenaltyPoolIds = new();
        private List<float> _savedRecoveryPenaltyBaseValues = new();
        private List<float> _savedRecoveryPenaltyStartDays = new();
        private List<float> _savedRecoveryPenaltyExpiryDays = new();
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailyTickSettlement);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);

            CampaignEvents.OnTroopRecruitedEvent.AddNonSerializedListener(this, OnTroopRecruited);
            CampaignEvents.OnUnitRecruitedEvent.AddNonSerializedListener(this, OnUnitRecruitedFallback);

            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);

            // War consequences
            CampaignEvents.RaidCompletedEvent.AddNonSerializedListener(this, OnRaidCompleted);
            CampaignEvents.OnSiegeAftermathAppliedEvent.AddNonSerializedListener(this, OnSiegeAftermath);
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);

            // Noble capture → enemy war exhaustion spike.
            CampaignEvents.HeroPrisonerTaken.AddNonSerializedListener(this, OnHeroPrisonerTaken);

            // Truce registration via game event — belt-and-suspenders alongside the Harmony Postfix patches.
            // This ensures truces are registered even if a third-party mod (e.g. AIInfluence) adds a
            // Prefix to MakePeaceAction and the Postfix hooks are skipped in a rare scenario.
            CampaignEvents.MakePeace.AddNonSerializedListener(this, OnMakePeaceEvent);
        }

        public override void SyncData(IDataStore dataStore)
        {
            _savedIds ??= new List<string>();
            _savedValues ??= new List<int>();

            if (!dataStore.IsLoading)
            {
                _savedIds.Clear();
                _savedValues.Clear();

                foreach (var kvp in _manpowerByPoolId)
                {
                    _savedIds.Add(kvp.Key);
                    _savedValues.Add(kvp.Value);
                }
            }

            dataStore.SyncData("B1071_Manpower_Ids", ref _savedIds);
            dataStore.SyncData("B1071_Manpower_Values", ref _savedValues);

            // C-1: Persist the seeded flag so we don't re-seed pools after load.
            dataStore.SyncData("B1071_Seeded", ref _seeded);

            _savedIds ??= new List<string>();
            _savedValues ??= new List<int>();

            if (dataStore.IsLoading)
            {
                _manpowerByPoolId.Clear();

                int n = Math.Min(_savedIds.Count, _savedValues.Count);
                for (int i = 0; i < n; i++)
                {
                    var id = _savedIds[i];
                    if (!string.IsNullOrEmpty(id))
                        _manpowerByPoolId[id] = _savedValues[i];
                }
            }

            // War Exhaustion save/load.
            _savedExhaustionIds ??= new List<string>();
            _savedExhaustionValues ??= new List<float>();

            if (!dataStore.IsLoading)
            {
                _savedExhaustionIds.Clear();
                _savedExhaustionValues.Clear();

                foreach (var kvp in _warExhaustion)
                {
                    _savedExhaustionIds.Add(kvp.Key);
                    _savedExhaustionValues.Add(kvp.Value);
                }
            }

            dataStore.SyncData("B1071_WarExhaustion_Ids", ref _savedExhaustionIds);
            dataStore.SyncData("B1071_WarExhaustion_Values", ref _savedExhaustionValues);

            _savedExhaustionIds ??= new List<string>();
            _savedExhaustionValues ??= new List<float>();

            if (dataStore.IsLoading)
            {
                _warExhaustion.Clear();

                int ne = Math.Min(_savedExhaustionIds.Count, _savedExhaustionValues.Count);
                for (int i = 0; i < ne; i++)
                {
                    var id = _savedExhaustionIds[i];
                    if (!string.IsNullOrEmpty(id))
                        _warExhaustion[id] = _savedExhaustionValues[i];
                }

                // Re-evaluate pressure bands immediately so they're correct from tick 0,
                // before any diplomacy decisions are evaluated post-load.
                _pressureBandByKingdom.Clear();
                foreach (var kvp in _warExhaustion)
                {
                    if (kvp.Value > 0f)
                        EvaluatePressureBand(kvp.Key, kvp.Value);
                }
            }

            // Forced-peace cooldown save/load.
            _savedForcedPeaceCooldownIds ??= new List<string>();
            _savedForcedPeaceCooldownDays ??= new List<float>();

            if (!dataStore.IsLoading)
            {
                _savedForcedPeaceCooldownIds.Clear();
                _savedForcedPeaceCooldownDays.Clear();
                foreach (var kvp in _lastForcedPeaceDayByKingdom)
                {
                    _savedForcedPeaceCooldownIds.Add(kvp.Key);
                    _savedForcedPeaceCooldownDays.Add(kvp.Value);
                }
            }

            dataStore.SyncData("B1071_ForcedPeaceCooldown_Ids", ref _savedForcedPeaceCooldownIds);
            dataStore.SyncData("B1071_ForcedPeaceCooldown_Days", ref _savedForcedPeaceCooldownDays);

            _savedForcedPeaceCooldownIds ??= new List<string>();
            _savedForcedPeaceCooldownDays ??= new List<float>();

            if (dataStore.IsLoading)
            {
                _lastForcedPeaceDayByKingdom.Clear();
                int nf = Math.Min(_savedForcedPeaceCooldownIds.Count, _savedForcedPeaceCooldownDays.Count);
                for (int i = 0; i < nf; i++)
                {
                    string id = _savedForcedPeaceCooldownIds[i];
                    if (!string.IsNullOrEmpty(id))
                        _lastForcedPeaceDayByKingdom[id] = _savedForcedPeaceCooldownDays[i];
                }
            }

            // Kingdom pair truce save/load.
            _savedTruceKeys ??= new List<string>();
            _savedTruceExpiryDays ??= new List<float>();

            if (!dataStore.IsLoading)
            {
                _savedTruceKeys.Clear();
                _savedTruceExpiryDays.Clear();
                foreach (var kvp in _truceExpiryByPair)
                {
                    _savedTruceKeys.Add(kvp.Key);
                    _savedTruceExpiryDays.Add(kvp.Value);
                }
            }

            dataStore.SyncData("B1071_TrucePair_Keys", ref _savedTruceKeys);
            dataStore.SyncData("B1071_TrucePair_ExpiryDays", ref _savedTruceExpiryDays);

            _savedTruceKeys ??= new List<string>();
            _savedTruceExpiryDays ??= new List<float>();

            if (dataStore.IsLoading)
            {
                _truceExpiryByPair.Clear();
                int nt = Math.Min(_savedTruceKeys.Count, _savedTruceExpiryDays.Count);
                for (int i = 0; i < nt; i++)
                {
                    string key = _savedTruceKeys[i];
                    if (!string.IsNullOrEmpty(key))
                        _truceExpiryByPair[key] = _savedTruceExpiryDays[i];
                }
            }

            // Raid drain dedupe save/load.
            _savedRaidDrainVillageIds ??= new List<string>();
            _savedRaidDrainDays ??= new List<int>();

            if (!dataStore.IsLoading)
            {
                _savedRaidDrainVillageIds.Clear();
                _savedRaidDrainDays.Clear();
                foreach (var kvp in _lastRaidDrainDayByVillageId)
                {
                    _savedRaidDrainVillageIds.Add(kvp.Key);
                    _savedRaidDrainDays.Add(kvp.Value);
                }
            }

            dataStore.SyncData("B1071_RaidDrainDedupe_VillageIds", ref _savedRaidDrainVillageIds);
            dataStore.SyncData("B1071_RaidDrainDedupe_Days", ref _savedRaidDrainDays);

            _savedRaidDrainVillageIds ??= new List<string>();
            _savedRaidDrainDays ??= new List<int>();

            if (dataStore.IsLoading)
            {
                _lastRaidDrainDayByVillageId.Clear();
                int nrv = Math.Min(_savedRaidDrainVillageIds.Count, _savedRaidDrainDays.Count);
                for (int i = 0; i < nrv; i++)
                {
                    string villageId = _savedRaidDrainVillageIds[i];
                    if (!string.IsNullOrEmpty(villageId))
                        _lastRaidDrainDayByVillageId[villageId] = _savedRaidDrainDays[i];
                }
            }

            // Raid pool-day cap spend save/load.
            _savedRaidPoolDayKeys ??= new List<string>();
            _savedRaidPoolDaySpent ??= new List<int>();

            if (!dataStore.IsLoading)
            {
                _savedRaidPoolDayKeys.Clear();
                _savedRaidPoolDaySpent.Clear();
                foreach (var kvp in _raidDrainSpentByPoolDay)
                {
                    _savedRaidPoolDayKeys.Add(kvp.Key);
                    _savedRaidPoolDaySpent.Add(kvp.Value);
                }
            }

            dataStore.SyncData("B1071_RaidDrainCap_PoolDayKeys", ref _savedRaidPoolDayKeys);
            dataStore.SyncData("B1071_RaidDrainCap_Spent", ref _savedRaidPoolDaySpent);

            _savedRaidPoolDayKeys ??= new List<string>();
            _savedRaidPoolDaySpent ??= new List<int>();

            if (dataStore.IsLoading)
            {
                _raidDrainSpentByPoolDay.Clear();
                int nrp = Math.Min(_savedRaidPoolDayKeys.Count, _savedRaidPoolDaySpent.Count);
                for (int i = 0; i < nrp; i++)
                {
                    string poolDayKey = _savedRaidPoolDayKeys[i];
                    if (!string.IsNullOrEmpty(poolDayKey))
                        _raidDrainSpentByPoolDay[poolDayKey] = _savedRaidPoolDaySpent[i];
                }
            }

            // Delayed recovery save/load.
            _savedRecoveryPenaltyPoolIds ??= new List<string>();
            _savedRecoveryPenaltyBaseValues ??= new List<float>();
            _savedRecoveryPenaltyStartDays ??= new List<float>();
            _savedRecoveryPenaltyExpiryDays ??= new List<float>();

            if (!dataStore.IsLoading)
            {
                _savedRecoveryPenaltyPoolIds.Clear();
                _savedRecoveryPenaltyBaseValues.Clear();
                _savedRecoveryPenaltyStartDays.Clear();
                _savedRecoveryPenaltyExpiryDays.Clear();
                foreach (var kvp in _recoveryPenaltyBaseByPoolId)
                {
                    string poolId = kvp.Key;
                    if (string.IsNullOrEmpty(poolId))
                        continue;

                    _savedRecoveryPenaltyPoolIds.Add(poolId);
                    _savedRecoveryPenaltyBaseValues.Add(kvp.Value);
                    _savedRecoveryPenaltyStartDays.Add(_recoveryPenaltyStartDayByPoolId.TryGetValue(poolId, out float startDay) ? startDay : (float)CampaignTime.Now.ToDays);
                    _savedRecoveryPenaltyExpiryDays.Add(_recoveryPenaltyExpiryDayByPoolId.TryGetValue(poolId, out float expiryDay) ? expiryDay : (float)CampaignTime.Now.ToDays);
                }
            }

            dataStore.SyncData("B1071_RecoveryPenalty_PoolIds", ref _savedRecoveryPenaltyPoolIds);
            dataStore.SyncData("B1071_RecoveryPenalty_Base", ref _savedRecoveryPenaltyBaseValues);
            dataStore.SyncData("B1071_RecoveryPenalty_StartDays", ref _savedRecoveryPenaltyStartDays);
            dataStore.SyncData("B1071_RecoveryPenalty_ExpiryDays", ref _savedRecoveryPenaltyExpiryDays);

            _savedRecoveryPenaltyPoolIds ??= new List<string>();
            _savedRecoveryPenaltyBaseValues ??= new List<float>();
            _savedRecoveryPenaltyStartDays ??= new List<float>();
            _savedRecoveryPenaltyExpiryDays ??= new List<float>();

            if (dataStore.IsLoading)
            {
                _recoveryPenaltyBaseByPoolId.Clear();
                _recoveryPenaltyStartDayByPoolId.Clear();
                _recoveryPenaltyExpiryDayByPoolId.Clear();

                int nr = Math.Min(
                    Math.Min(_savedRecoveryPenaltyPoolIds.Count, _savedRecoveryPenaltyBaseValues.Count),
                    Math.Min(_savedRecoveryPenaltyStartDays.Count, _savedRecoveryPenaltyExpiryDays.Count));

                for (int i = 0; i < nr; i++)
                {
                    string poolId = _savedRecoveryPenaltyPoolIds[i];
                    if (string.IsNullOrEmpty(poolId))
                        continue;

                    _recoveryPenaltyBaseByPoolId[poolId] = Math.Max(0f, _savedRecoveryPenaltyBaseValues[i]);
                    _recoveryPenaltyStartDayByPoolId[poolId] = _savedRecoveryPenaltyStartDays[i];
                    _recoveryPenaltyExpiryDayByPoolId[poolId] = _savedRecoveryPenaltyExpiryDays[i];
                }
            }
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            Instance = this;
            UI.B1071_OverlayController.Reset();

            ResetDailyTelemetry();
            _telemetryLastForcedPeace = "None";
            _telemetryLastTruce = "None";
            _telemetryLastDiplomacyDecision = "None";
            _telemetryLastRegenBreakdown = "n/a";
            _telemetryLastRegenPoolId = string.Empty;

            SeedAllPoolsIfNeeded();

            B1071_VerboseLog.Log("Session", $"ManpowerBehavior launched. Pools seeded={_seeded}, tracked={_manpowerByPoolId.Count}, exhaustion entries={_warExhaustion.Count}.");

            if (Hero.MainHero != null)
                InformationManager.DisplayMessage(new InformationMessage(new TaleWorlds.Localization.TextObject("{=b1071_mp_active}[Byzantium1071] Manpower active.").ToString()));
        }

        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            if (!Settings.ShowPlayerDebugMessages) return;
            if (hero != Hero.MainHero) return;
            if (settlement == null || settlement.IsHideout) return;

            GetManpowerPool(settlement, out int cur, out int max, out Settlement pool);
            if (pool == null) return;

            string where = settlement == pool
                ? $"{settlement.Name}"
                : $"{settlement.Name} (pool: {pool.Name})";

            InformationManager.DisplayMessage(new InformationMessage(
                new TaleWorlds.Localization.TextObject("{=b1071_mp_debug_pool}[Manpower] {WHERE}: {CUR}/{MAX}")
                    .SetTextVariable("WHERE", where)
                    .SetTextVariable("CUR", cur)
                    .SetTextVariable("MAX", max)
                    .ToString()));
        }

        // Guards against double-deduction: OnUnitRecruitedEvent fires for ALL recruitments
        // (not just player), but provides no recruiter context. We track whether the
        // last OnTroopRecruited call was AI so the fallback can skip it.
        private bool _lastRecruitWasAI;
        private CharacterObject? _lastAIRecruitTroop;
        private int _lastAIRecruitAmount;
        private string? _lastAIRecruitSettlementId;
        private string? _lastAIRecruitPartyId;

        private void OnUnitRecruitedFallback(CharacterObject troop, int amount)
        {
            if (!Settings.UseOnUnitRecruitedFallbackForPlayer) return;
            if (troop == null || amount <= 0) return;

            // Skip if this is the AI recruitment we already handled in OnTroopRecruited.
            // Use settlement context in addition to troop+amount to avoid false dedupe
            // when two different settlements recruit the same troop type and count.
            Settlement? currentSettlement = Hero.MainHero?.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement;
            string? currentSettlementId = currentSettlement?.StringId;
            if (_lastRecruitWasAI && _lastAIRecruitTroop == troop && _lastAIRecruitAmount == amount
                && _lastAIRecruitSettlementId == currentSettlementId
                && _lastAIRecruitPartyId != MobileParty.MainParty?.StringId)
            {
                _lastRecruitWasAI = false;
                _lastAIRecruitTroop = null;
                _lastAIRecruitAmount = 0;
                _lastAIRecruitSettlementId = null;
                _lastAIRecruitPartyId = null;
                return;
            }

            Settlement? playerSettlement = Hero.MainHero?.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement;
            if (playerSettlement == null || playerSettlement.IsHideout) return;

            MobileParty? main = MobileParty.MainParty;
            if (main != null)
            {
                ConsumeManpower(playerSettlement, main, troop, amount, isPlayer: true, context: "UnitRecruited");
            }
        }

        private void SeedAllPoolsIfNeeded()
        {
            if (_seeded) return;
            _seeded = true;

            B1071_VerboseLog.Log("Manpower", "Seeding all manpower pools for the first time.");

            foreach (var settlement in Settlement.All)
            {
                if (settlement == null || settlement.IsHideout) continue;

                // Ensures pool entry exists (villages map to their bound pool).
                EnsureEntry(settlement);
            }
        }

        private void OnDailyTickSettlement(Settlement settlement)
        {
            if (settlement == null || settlement.IsHideout) return;

            // Regen ONLY on pool settlements (Town/Castle), not per village.
            Settlement? pool = GetPoolSettlement(settlement);
            if (pool != settlement) return;

            EnsureEntry(pool);

            string poolId = pool.StringId;
            if (string.IsNullOrEmpty(poolId)) return;

            int max = GetMaxManpowerCached(pool);

            if (!_manpowerByPoolId.TryGetValue(poolId, out int cur))
                cur = max;

            // A-2: Skip regen computation if already at or above max.
            if (cur >= max) return;

            int regen = GetDailyRegen(pool, max);

            // ── Castle supply chain ──────────────────────────────────────
            // Castles did not generate soldiers from births — garrisons were
            // rotated from towns by the strategos. Only a tiny trickle (peasant 
            // levy from bound villages) is created organically. Everything above
            // that trickle is *transferred* from the nearest same-faction town,
            // draining its pool rather than materialising manpower from nothing.
            //
            // If no same-faction town exists or the supply town is depleted,
            // the castle receives only the local trickle.
            int actualRegen;
            if (pool.IsCastle && Settings.EnableCastleSupplyChain)
            {
                int localTrickle = Math.Min(regen, Settings.CastleMinimumDailyRegen);
                int supplyRequest = regen - localTrickle;

                int supplyTransfer = 0;
                string supplySource = "none";

                if (supplyRequest > 0)
                {
                    Settlement? supplyTown = FindNearestSameFactionTown(pool);
                    if (supplyTown != null)
                    {
                        string supplyId = supplyTown.StringId;
                        EnsureEntry(supplyTown);
                        int supplyMax = GetMaxManpowerCached(supplyTown);
                        int supplyCur = _manpowerByPoolId.TryGetValue(supplyId, out int sv) ? sv : supplyMax;

                        supplyTransfer = Math.Min(supplyRequest, supplyCur);
                        if (supplyTransfer > 0)
                        {
                            _manpowerByPoolId[supplyId] = supplyCur - supplyTransfer;
                        }
                        supplySource = $"{supplyTown.Name} ({supplyCur}->{supplyCur - supplyTransfer})";
                    }
                }

                actualRegen = localTrickle + supplyTransfer;
                B1071_VerboseLog.Log("Manpower",
                    $"CastleSupply {pool.Name}: trickle={localTrickle} request={supplyRequest} " +
                    $"transfer={supplyTransfer} supply={supplySource} total=+{actualRegen}");
            }
            else
            {
                actualRegen = regen;
            }

            int newCur = Math.Min(max, cur + actualRegen);
            _manpowerByPoolId[poolId] = newCur;

            B1071_VerboseLog.Log("Manpower", $"Regen {pool.Name}: +{actualRegen} ({cur}->{newCur}/{max}).");

            // Crisis alerts for player settlements.
            if (Settings.EnableManpowerAlerts && IsPlayerSettlement(pool))
            {
                int pct = max <= 0 ? 100 : (int)((100f * newCur) / max);
                int threshold = Math.Max(1, Settings.AlertThresholdPercent);
                int band = GetPoolBand(newCur, max);

                if (!_lastAlertBand.TryGetValue(poolId, out int prevBand))
                    prevBand = band;

                if (band < prevBand && pct <= threshold)
                {
                    TextObject msg = new TextObject("{=b1071_mp_critical_alert}Manpower critical at {POOL} ({PCT}% - {CUR}/{MAX})")
                        .SetTextVariable("POOL", pool.Name)
                        .SetTextVariable("PCT", pct)
                        .SetTextVariable("CUR", newCur)
                        .SetTextVariable("MAX", max);

                    InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Red));
                }

                _lastAlertBand[poolId] = band;
            }
        }

        private void OnDailyTick()
        {
            ResetDailyTelemetry();

            // Clear per-day caches so GetMaxManpower is recomputed with fresh data.
            _maxManpowerCache.Clear();

            // Notify the overlay to rebuild its cached settlement data.
            UI.B1071_OverlayController.MarkCacheStale();

            // A-4: Do NOT clear raid dedup maps daily — the embedded day-value
            // comparison already prevents stale matches. Clearing mid-day could
            // cause a same-day raid to bypass dedup if it fires after OnDailyTick.
            // Instead, periodically prune entries older than 2 days to keep the maps small.
            int today = (int)CampaignTime.Now.ToDays;
            PruneStaleRaidDedupEntries(today);

            CleanupExpiredDelayedRecovery();

            CleanupExpiredTruces();

            if (Settings.EnableWarExhaustion)
            {
                float decay = Math.Max(0f, Settings.ExhaustionDailyDecay);
                if (decay > 0f)
                {
                    // Reuse a scratch list to avoid allocating a new List<string> each day.
                    if (_exhaustionKeysScratch == null)
                        _exhaustionKeysScratch = new List<string>(_warExhaustion.Count);
                    _exhaustionKeysScratch.Clear();
                    _exhaustionKeysScratch.AddRange(_warExhaustion.Keys);
                    foreach (string key in _exhaustionKeysScratch)
                    {
                        // Clean up exhaustion entries for kingdoms that no longer exist.
                        Kingdom? kd = Kingdom.All?.Find(x => x != null && x.StringId == key);
                        if (kd == null || kd.IsEliminated)
                        {
                            _warExhaustion.Remove(key);
                            _pressureBandByKingdom.Remove(key);
                            _lastForcedPeaceDayByKingdom.Remove(key);
                            B1071_VerboseLog.Log("Exhaustion", $"Removed {key}: kingdom eliminated.");
                            continue;
                        }

                        float before = _warExhaustion[key];
                        float val = before - decay;
                        if (val <= 0f)
                            _warExhaustion.Remove(key);
                        else
                            _warExhaustion[key] = val;

                        _telemetryExhaustionDecayToday += Math.Min(decay, before);
                        B1071_VerboseLog.Log("Exhaustion", $"Decay {key}: {before:0.0}->{Math.Max(0f, val):0.0} (-{decay:0.0}).");
                    }
                }

                // WP5: Evaluate pressure bands (with hysteresis) for all kingdoms with exhaustion.
                if (Settings.EnableDiplomacyPressureBands)
                {
                    foreach (Kingdom kingdom in Kingdom.All)
                    {
                        if (kingdom == null || kingdom.IsEliminated) continue;
                        string kid = kingdom.StringId;
                        if (string.IsNullOrEmpty(kid)) continue;
                        float exh = GetWarExhaustion(kid);
                        EvaluatePressureBand(kid, exh);
                    }
                }

                TryApplyForcedPeaceAtCrisis();
            }
        }

        /// <summary>
        /// Adds war exhaustion to a kingdom by StringId.
        /// </summary>
        private void AddWarExhaustion(string? kingdomId, float amount)
        {
            if (!Settings.EnableWarExhaustion) return;
            if (string.IsNullOrEmpty(kingdomId) || amount <= 0f) return;

            // Don't accumulate exhaustion for kingdoms that have been destroyed.
            Kingdom? k = Kingdom.All?.Find(x => x != null && x.StringId == kingdomId);
            if (k == null || k.IsEliminated) return;

            // Manpower-depletion amplifier: when a kingdom's pools are depleted, losses hit harder.
            if (Settings.EnableManpowerDepletionAmplifier && Settings.ManpowerDepletionAmplifier > 0f)
            {
                if (k != null)
                {
                    float avgRatio = GetKingdomAverageManpowerRatio(k);
                    float amplifier = 1f + (1f - avgRatio) * Settings.ManpowerDepletionAmplifier;
                    amount *= amplifier;
                }
            }

            float maxScore = Math.Max(1f, Settings.ExhaustionMaxScore);
            float cur = _warExhaustion.TryGetValue(kingdomId!, out float v) ? v : 0f;
            float newExh = Math.Min(maxScore, cur + amount);
            _warExhaustion[kingdomId!] = newExh;
            _telemetryExhaustionGainToday += amount;
            B1071_VerboseLog.Log("Exhaustion", $"Gain {kingdomId}: +{amount:0.0} ({cur:0.0}->{newExh:0.0}).");
        }

        /// <summary>
        /// Returns the average manpower fill ratio (0–1) across all town and castle pools owned by this kingdom.
        /// Returns 1 (fully stocked) when the kingdom has no tracked settlements.
        /// </summary>
        internal float GetKingdomAverageManpowerRatio(Kingdom kingdom)
        {
            if (kingdom == null) return 1f;
            string id = kingdom.StringId ?? string.Empty;

            // One-day cache: avoids repeated settlement iteration in tight loops.
            float currentDay = (float)CampaignTime.Now.ToDays;
            if (currentDay < _manpowerRatioCacheExpiryDay && !string.IsNullOrEmpty(id)
                && _manpowerRatioCacheById.TryGetValue(id, out float cached))
                return cached;

            if (currentDay >= _manpowerRatioCacheExpiryDay)
            {
                _manpowerRatioCacheById.Clear();
                _manpowerRatioCacheExpiryDay = (float)(Math.Floor(currentDay) + 1.0);
            }

            if (kingdom.Settlements == null) return 1f;
            float totalRatio = 0f;
            int count = 0;
            foreach (Settlement s in kingdom.Settlements)
            {
                if (!s.IsTown && !s.IsCastle) continue;
                EnsureEntry(s);
                int max = GetMaxManpowerCached(s);
                if (max <= 0) continue;
                int cur = _manpowerByPoolId.TryGetValue(s.StringId, out int v) ? v : max;
                totalRatio += (float)cur / max;
                count++;
            }
            float ratio = count > 0 ? totalRatio / count : 1f;
            if (!string.IsNullOrEmpty(id))
                _manpowerRatioCacheById[id] = ratio;
            return ratio;
        }

        internal bool ShouldShowTelemetryInOverlay => Settings.ShowTelemetryInOverlay;
        internal string GetTelemetryCurrentRowLabel() => "Dbg";
        internal string GetTelemetryCurrentRowC2() =>
            $"R/S/B:{_telemetryRaidDrainToday}/{_telemetrySiegeDrainToday}/{_telemetryBattleDrainToday} ({_telemetryLastRegenPoolId})";
        internal string GetTelemetryCurrentRowC3() =>
            $"Exh +{_telemetryExhaustionGainToday:0.0} -{_telemetryExhaustionDecayToday:0.0}";
        internal string GetTelemetryCurrentRowC4() =>
            TruncateTelemetry(_telemetryLastForcedPeace == "None" ? _telemetryLastTruce : _telemetryLastForcedPeace, 28);
        internal string GetTelemetryRegenBreakdown() => _telemetryLastRegenBreakdown;

        internal void RecordDiplomacyTelemetry(string reason)
        {
            _telemetryLastDiplomacyDecision = string.IsNullOrEmpty(reason) ? "None" : reason;
            if ((Settings.TelemetryDebugLogs || B1071_VerboseLog.Enabled)
                && _telemetryLoggedDiplomacy.Add(_telemetryLastDiplomacyDecision))
                Debug.Print("[Byzantium1071][Telemetry][Diplomacy] " + _telemetryLastDiplomacyDecision);
        }

        private void ResetDailyTelemetry()
        {
            _telemetryRaidDrainToday = 0;
            _telemetrySiegeDrainToday = 0;
            _telemetryBattleDrainToday = 0;
            _telemetryExhaustionGainToday = 0f;
            _telemetryExhaustionDecayToday = 0f;
            _telemetryLastForcedPeace = "None";
            _telemetryLastDiplomacyDecision = "None";
            _telemetryLoggedDiplomacy.Clear();
        }

        private static string TruncateTelemetry(string value, int maxLen)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLen)
                return value;
            if (maxLen <= 0)
                return string.Empty;
            if (maxLen <= 1)
                return value.Substring(0, maxLen);
            return value.Substring(0, maxLen - 1) + "…";
        }

        private static readonly Dictionary<string, MemberInfo?> _campaignTimeMemberCache = new();

        private static object? TryReadCampaignTimeMember(CampaignTime time, string memberName)
        {
            if (!_campaignTimeMemberCache.TryGetValue(memberName, out MemberInfo? cached))
            {
                Type type = typeof(CampaignTime);
                cached = (MemberInfo?)type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public)
                    ?? (MemberInfo?)type.GetMethod(memberName, BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null)
                    ?? type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public);
                _campaignTimeMemberCache[memberName] = cached;
            }

            if (cached is PropertyInfo prop)
                return prop.GetValue(time);
            if (cached is MethodInfo meth)
                return meth.Invoke(time, null);
            if (cached is FieldInfo field)
                return field.GetValue(time);

            return null;
        }

        private static int TryReadCampaignTimeInt(CampaignTime time, string memberName)
        {
            object? value = TryReadCampaignTimeMember(time, memberName);
            if (value == null)
                return -1;

            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                return -1;
            }
        }

        private static string FormatCampaignDateTime(float absoluteDay)
        {
            CampaignTime when = CampaignTime.Days(absoluteDay);

            string season = TryReadCampaignTimeMember(when, "GetSeasonOfYear")?.ToString() ?? string.Empty;
            int dayOfSeason = TryReadCampaignTimeInt(when, "GetDayOfSeason");
            int year = TryReadCampaignTimeInt(when, "GetYear");
            int hourOfDay = TryReadCampaignTimeInt(when, "GetHourOfDay");

            if (!string.IsNullOrEmpty(season) && dayOfSeason > 0 && year >= 0)
            {
                if (hourOfDay >= 0)
                    return $"{season} {dayOfSeason}, {year} {hourOfDay:00}:00";

                return $"{season} {dayOfSeason}, {year}";
            }

            int dayOfYear = TryReadCampaignTimeInt(when, "GetDayOfYear");
            if (year >= 0 && dayOfYear >= 0)
            {
                if (hourOfDay >= 0)
                    return $"Year {year}, day {dayOfYear}, {hourOfDay:00}:00";

                return $"Year {year}, day {dayOfYear}";
            }

            return $"day {absoluteDay:0.0}";
        }

        internal string FormatAbsoluteDay(float absoluteDay)
        {
            return FormatCampaignDateTime(absoluteDay);
        }

        internal string FormatDaysFromNow(float daysFromNow)
        {
            float absoluteDay = (float)CampaignTime.Now.ToDays + Math.Max(0f, daysFromNow);
            return FormatCampaignDateTime(absoluteDay);
        }

        private void CleanupExpiredDelayedRecovery()
        {
            if (_recoveryPenaltyExpiryDayByPoolId.Count == 0)
                return;

            float now = (float)CampaignTime.Now.ToDays;
            if (_cleanupKeysScratch == null) _cleanupKeysScratch = new List<string>();
            else _cleanupKeysScratch.Clear();

            foreach (var kvp in _recoveryPenaltyExpiryDayByPoolId)
            {
                if (kvp.Value <= now)
                    _cleanupKeysScratch.Add(kvp.Key);
            }

            for (int i = 0; i < _cleanupKeysScratch.Count; i++)
            {
                string poolId = _cleanupKeysScratch[i];
                _recoveryPenaltyExpiryDayByPoolId.Remove(poolId);
                _recoveryPenaltyStartDayByPoolId.Remove(poolId);
                _recoveryPenaltyBaseByPoolId.Remove(poolId);
            }
        }

        /// <summary>
        /// A-4: Removes stale entries from raid dedup maps (older than 2 days).
        /// Called daily instead of clearing the maps, to prevent mid-day dedup gaps.
        /// </summary>
        private void PruneStaleRaidDedupEntries(int today)
        {
            int cutoff = today - 2;

            // Prune _lastRaidDrainDayByVillageId: value is the day number.
            if (_lastRaidDrainDayByVillageId.Count > 0)
            {
                if (_cleanupKeysScratch == null) _cleanupKeysScratch = new List<string>();
                else _cleanupKeysScratch.Clear();

                foreach (var kvp in _lastRaidDrainDayByVillageId)
                    if (kvp.Value < cutoff) _cleanupKeysScratch.Add(kvp.Key);

                for (int i = 0; i < _cleanupKeysScratch.Count; i++)
                    _lastRaidDrainDayByVillageId.Remove(_cleanupKeysScratch[i]);
            }

            // Prune _raidDrainSpentByPoolDay: key is "poolId|day".
            if (_raidDrainSpentByPoolDay.Count > 0)
            {
                if (_cleanupKeysScratch == null) _cleanupKeysScratch = new List<string>();
                else _cleanupKeysScratch.Clear();

                foreach (var kvp in _raidDrainSpentByPoolDay)
                {
                    int sepIdx = kvp.Key.LastIndexOf('|');
                    if (sepIdx > 0 && int.TryParse(kvp.Key.Substring(sepIdx + 1), out int day) && day < cutoff)
                        _cleanupKeysScratch.Add(kvp.Key);
                }

                for (int i = 0; i < _cleanupKeysScratch.Count; i++)
                    _raidDrainSpentByPoolDay.Remove(_cleanupKeysScratch[i]);
            }
        }

        internal float GetRecoveryPenaltyFraction(string poolId)
        {
            if (string.IsNullOrEmpty(poolId))
                return 0f;

            if (!_recoveryPenaltyBaseByPoolId.TryGetValue(poolId, out float basePenalty) || basePenalty <= 0f)
                return 0f;

            if (!_recoveryPenaltyStartDayByPoolId.TryGetValue(poolId, out float startDay) ||
                !_recoveryPenaltyExpiryDayByPoolId.TryGetValue(poolId, out float expiryDay))
                return 0f;

            float now = (float)CampaignTime.Now.ToDays;
            if (now >= expiryDay)
            {
                _recoveryPenaltyBaseByPoolId.Remove(poolId);
                _recoveryPenaltyStartDayByPoolId.Remove(poolId);
                _recoveryPenaltyExpiryDayByPoolId.Remove(poolId);
                return 0f;
            }

            float duration = Math.Max(1f, expiryDay - startDay);
            float remaining = Math.Max(0f, expiryDay - now);
            float ratio = Clamp01(remaining / duration);
            float penalty = Math.Max(0f, basePenalty * ratio);

            // Reduce penalty effectiveness when pool is depleted (below threshold).
            // A ruined settlement has nothing left to penalize — halve the penalty.
            if (Settings.ReduceRecoveryPenaltyWhenDepleted && penalty > 0f)
            {
                float depletedThreshold = Clamp01(Math.Max(0f, Settings.RecoveryDepletedThresholdPercent) / 100f);
                if (depletedThreshold > 0f && !string.IsNullOrEmpty(poolId))
                {
                    // Prefer cache (populated during daily tick) to avoid Settlement.All iteration.
                    int max = _maxManpowerCache.TryGetValue(poolId, out int cached) ? cached : 0;
                    if (max <= 0)
                    {
                        // Cache miss — find settlement to compute max. Only happens
                        // if called before the first daily tick (e.g. conquest event).
                        foreach (var s in Settlement.All)
                        {
                            if (s != null && s.StringId == poolId && (s.IsTown || s.IsCastle))
                            {
                                max = GetMaxManpowerCached(s);
                                break;
                            }
                        }
                    }
                    if (max > 0)
                    {
                        int cur = _manpowerByPoolId.TryGetValue(poolId, out int cv) ? cv : max;
                        float fillRatio = Clamp01((float)cur / max);
                        if (fillRatio < depletedThreshold)
                        {
                            // Halve the penalty when depleted.
                            penalty *= 0.5f;
                        }
                    }
                }
            }

            return penalty;
        }

        /// <summary>
        /// Returns the number of in-game days remaining on the recovery penalty for the given pool.
        /// Returns 0 if no penalty is active.
        /// </summary>
        internal float GetRecoveryDaysRemaining(string poolId)
        {
            if (string.IsNullOrEmpty(poolId)) return 0f;
            if (!_recoveryPenaltyExpiryDayByPoolId.TryGetValue(poolId, out float expiryDay))
                return 0f;
            float now = (float)CampaignTime.Now.ToDays;
            return Math.Max(0f, expiryDay - now);
        }

        /// <summary>
        /// Returns all active truce pairs as (displayNameA, displayNameB, daysRemaining) for overlay display.
        /// </summary>
        internal List<(string kingdomAName, string kingdomBName, float daysRemaining)> GetActiveTruces()
        {
            var result = new List<(string, string, float)>();
            float now = (float)CampaignTime.Now.ToDays;
            foreach (var kvp in _truceExpiryByPair)
            {
                float remaining = kvp.Value - now;
                if (remaining <= 0f) continue;
                string[] parts = kvp.Key.Split('|');
                if (parts.Length == 2)
                {
                    string nameA = ResolveKingdomDisplayName(parts[0]);
                    string nameB = ResolveKingdomDisplayName(parts[1]);
                    result.Add((nameA, nameB, remaining));
                }
            }
            return result;
        }

        private static string ResolveKingdomDisplayName(string stringId)
        {
            if (string.IsNullOrEmpty(stringId)) return stringId;
            foreach (Kingdom k in Kingdom.All)
            {
                if (k != null && string.Equals(k.StringId, stringId, StringComparison.Ordinal))
                    return k.Name?.ToString() ?? stringId;
            }
            return stringId;
        }

        private void ApplyDelayedRecoveryPenalty(Settlement pool, int basePenaltyPercent, int durationDays, string reason)
        {
            if (!Settings.EnableDelayedRecovery) return;
            if (pool == null || string.IsNullOrEmpty(pool.StringId)) return;
            if (basePenaltyPercent <= 0 || durationDays <= 0) return;

            string poolId = pool.StringId;
            float now = (float)CampaignTime.Now.ToDays;
            float maxPenalty = Math.Max(0f, Settings.RecoveryPenaltyMaxPercent) / 100f;
            if (maxPenalty <= 0f) return;

            float currentPenalty = GetRecoveryPenaltyFraction(poolId);
            float addPenalty = Math.Max(0f, basePenaltyPercent) / 100f;
            float combined = Math.Min(maxPenalty, currentPenalty + addPenalty);
            float newExpiry = now + Math.Max(1, durationDays);

            if (_recoveryPenaltyExpiryDayByPoolId.TryGetValue(poolId, out float oldExpiry) && oldExpiry > newExpiry)
                newExpiry = oldExpiry;

            _recoveryPenaltyBaseByPoolId[poolId] = combined;
            _recoveryPenaltyStartDayByPoolId[poolId] = now;
            _recoveryPenaltyExpiryDayByPoolId[poolId] = newExpiry;

            B1071_VerboseLog.Log("Manpower", $"Recovery penalty at {pool.Name}: {(combined * 100f):0}% for {Math.Max(0f, newExpiry - now):0.0} days ({reason}).");

            if (Settings.ShowPlayerDebugMessages)
            {
                float remainingDays = Math.Max(0f, newExpiry - now);
                string expiryText = FormatCampaignDateTime(newExpiry);
                TextObject msg = new TextObject("{=b1071_mp_dbg_recovery_penalty}[B1071] Recovery penalty at {POOL}: {PENALTY}% until {EXPIRY} (~{DAYS} days, {REASON}).")
                    .SetTextVariable("POOL", pool.Name)
                    .SetTextVariable("PENALTY", (combined * 100f).ToString("0"))
                    .SetTextVariable("EXPIRY", expiryText)
                    .SetTextVariable("DAYS", remainingDays.ToString("0.0"))
                    .SetTextVariable("REASON", reason);

                InformationManager.DisplayMessage(new InformationMessage(
                    msg.ToString()));
            }
        }

        /// <summary>
        /// Returns the war exhaustion score for a kingdom (0 = fresh, max = crisis).
        /// </summary>
        public float GetWarExhaustion(string? kingdomId)
        {
            if (string.IsNullOrEmpty(kingdomId)) return 0f;
            return _warExhaustion.TryGetValue(kingdomId!, out float v) ? v : 0f;
        }

        // ─── WP5: Pressure band evaluation with hysteresis ───

        /// <summary>
        /// Returns the current diplomacy pressure band for a kingdom.
        /// If pressure bands are disabled, maps exhaustion to legacy thresholds.
        /// </summary>
        public DiplomacyPressureBand GetPressureBand(string? kingdomId)
        {
            if (string.IsNullOrEmpty(kingdomId)) return DiplomacyPressureBand.Low;
            if (!Settings.EnableDiplomacyPressureBands)
                return MapExhaustionToLegacyBand(GetWarExhaustion(kingdomId));

            return _pressureBandByKingdom.TryGetValue(kingdomId!, out var band) ? band : DiplomacyPressureBand.Low;
        }

        /// <summary>
        /// Evaluates and updates pressure band for a kingdom, applying hysteresis on downward transitions.
        /// Called once per daily tick per kingdom with active exhaustion.
        /// </summary>
        private DiplomacyPressureBand EvaluatePressureBand(string kingdomId, float exhaustion)
        {
            float risingStart = Math.Max(1f, Settings.PressureBandRisingStart);
            float crisisStart = Math.Max(risingStart + 1f, Settings.PressureBandCrisisStart);
            float hysteresis = Math.Max(0f, Settings.PressureBandHysteresis);

            DiplomacyPressureBand current = _pressureBandByKingdom.TryGetValue(kingdomId, out var prev)
                ? prev : DiplomacyPressureBand.Low;

            DiplomacyPressureBand newBand;

            // Upward transitions: use raw thresholds
            if (exhaustion >= crisisStart)
                newBand = DiplomacyPressureBand.Crisis;
            else if (exhaustion >= risingStart)
                newBand = DiplomacyPressureBand.Rising;
            else
                newBand = DiplomacyPressureBand.Low;

            // Hysteresis: resist downward transitions
            if (newBand < current)
            {
                switch (current)
                {
                    case DiplomacyPressureBand.Crisis:
                        // Stay in Crisis until exhaustion drops below crisisStart - hysteresis
                        if (exhaustion >= crisisStart - hysteresis)
                            newBand = DiplomacyPressureBand.Crisis;
                        break;
                    case DiplomacyPressureBand.Rising:
                        // Stay in Rising until exhaustion drops below risingStart - hysteresis
                        if (exhaustion >= risingStart - hysteresis)
                            newBand = DiplomacyPressureBand.Rising;
                        break;
                }
            }

            if (newBand != current)
                B1071_VerboseLog.Log("Diplomacy", $"Band transition {kingdomId}: {current}->{newBand} at exhaustion {exhaustion:0.0}.");
            _pressureBandByKingdom[kingdomId] = newBand;
            return newBand;
        }

        /// <summary>
        /// Maps exhaustion to a legacy-compatible band (no hysteresis) for when pressure bands are disabled.
        /// Uses the old hard thresholds: NoWarThreshold → Crisis, PeaceThreshold → Rising, else → Low.
        /// </summary>
        private static DiplomacyPressureBand MapExhaustionToLegacyBand(float exhaustion)
        {
            float noWarThreshold = (B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults).DiplomacyNoNewWarThreshold;
            float peaceThreshold = (B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults).DiplomacyPeacePressureThreshold;
            if (exhaustion >= noWarThreshold) return DiplomacyPressureBand.Crisis;
            if (exhaustion >= peaceThreshold) return DiplomacyPressureBand.Rising;
            return DiplomacyPressureBand.Low;
        }

        /// <summary>
        /// Returns per-point peace bias for the given band.
        /// </summary>
        internal float GetBandPeaceBias(DiplomacyPressureBand band)
        {
            return band switch
            {
                DiplomacyPressureBand.Low => Settings.PeaceBiasBandLow,
                DiplomacyPressureBand.Rising => Settings.PeaceBiasBandHigh,
                DiplomacyPressureBand.Crisis => Settings.PeaceBiasBandHigh * 1.5f, // Crisis escalates further
                _ => Settings.PeaceBiasBandLow,
            };
        }

        public void RegisterKingdomPairTruce(IFaction? faction1, IFaction? faction2)
        {
            if (faction1 is not Kingdom kingdom1 || faction2 is not Kingdom kingdom2)
                return;

            int truceDays = Math.Max(0, Settings.ForcedPeaceTruceDays);
            if (truceDays <= 0)
                return;

            string key = MakeKingdomPairKey(kingdom1, kingdom2);
            if (string.IsNullOrEmpty(key))
                return;

            float expiryDay = (float)CampaignTime.Now.ToDays + truceDays;
            _truceExpiryByPair[key] = expiryDay;
            B1071_VerboseLog.Log("Diplomacy", $"Truce registered: {kingdom1.Name} vs {kingdom2.Name}, {truceDays} days until {FormatCampaignDateTime(expiryDay)}.");
            _telemetryLastTruce = $"Truce {kingdom1.Name}-{kingdom2.Name} until {FormatCampaignDateTime(expiryDay)}";
        }

        public bool IsKingdomPairUnderTruce(Kingdom? kingdom, IFaction? otherFaction, out float daysRemaining)
        {
            daysRemaining = 0f;
            if (kingdom == null || otherFaction is not Kingdom otherKingdom)
                return false;

            string key = MakeKingdomPairKey(kingdom, otherKingdom);
            if (string.IsNullOrEmpty(key))
                return false;

            if (!_truceExpiryByPair.TryGetValue(key, out float expiryDay))
                return false;

            float now = (float)CampaignTime.Now.ToDays;
            daysRemaining = expiryDay - now;
            if (daysRemaining <= 0f)
            {
                _truceExpiryByPair.Remove(key);
                daysRemaining = 0f;
                return false;
            }

            return true;
        }

        private static string MakeKingdomPairKey(Kingdom kingdomA, Kingdom kingdomB)
        {
            string idA = kingdomA.StringId ?? string.Empty;
            string idB = kingdomB.StringId ?? string.Empty;
            if (string.IsNullOrEmpty(idA) || string.IsNullOrEmpty(idB))
                return string.Empty;

            return string.CompareOrdinal(idA, idB) <= 0 ? $"{idA}|{idB}" : $"{idB}|{idA}";
        }

        private void CleanupExpiredTruces()
        {
            if (_truceExpiryByPair.Count == 0)
                return;

            float now = (float)CampaignTime.Now.ToDays;
            if (_cleanupKeysScratch == null) _cleanupKeysScratch = new List<string>();
            else _cleanupKeysScratch.Clear();

            foreach (var kvp in _truceExpiryByPair)
            {
                if (kvp.Value <= now)
                    _cleanupKeysScratch.Add(kvp.Key);
            }

            for (int i = 0; i < _cleanupKeysScratch.Count; i++)
            {
                _truceExpiryByPair.Remove(_cleanupKeysScratch[i]);
                _telemetryLastTruce = "Expired " + _cleanupKeysScratch[i] + " at " + FormatCampaignDateTime(now);
            }
        }

        private void TryApplyForcedPeaceAtCrisis()
        {
            if (!Settings.EnableExhaustionDiplomacyPressure || !Settings.EnableForcedPeaceAtCrisis)
                return;

            float baseThreshold = Math.Max(1f, Settings.DiplomacyForcedPeaceThreshold);
            int pressureStartWars = Math.Max(1, Settings.DiplomacyMajorWarPressureStartCount);
            float thresholdReductionPerWar = Math.Max(0f, Settings.DiplomacyForcedPeaceThresholdReductionPerMajorWar);
            int maxActiveWars = Math.Max(0, Settings.DiplomacyForcedPeaceMaxActiveWars);
            int cooldownDays = Math.Max(1, Settings.DiplomacyForcedPeaceCooldownDays);
            int minWarDays = Math.Max(0, Settings.MinWarDurationDaysBeforeForcedPeace);
            bool ignoreIfBesiegingCore = Settings.IgnoreForcedPeaceIfEnemyBesiegingCoreSettlement;
            float nowDays = (float)CampaignTime.Now.ToDays;

            foreach (Kingdom kingdom in Kingdom.All)
            {
                if (kingdom == null || kingdom.IsEliminated)
                    continue;

                if (string.IsNullOrEmpty(kingdom.StringId))
                    continue;

                if (Clan.PlayerClan?.Kingdom == kingdom && !Settings.DiplomacyEnforcePlayerParity)
                {
                    DebugDiplomacy($"Skip forced peace for {kingdom.Name}: player kingdom context (parity OFF).");
                    _telemetryLastForcedPeace = $"Skip {kingdom.Name}: player kingdom (parity OFF)";
                    continue;
                }

                int activeMajorWars = CountActiveMajorWars(kingdom);
                int extraWarCount = Math.Max(0, activeMajorWars - pressureStartWars + 1);
                float threshold = Math.Max(1f, baseThreshold - (extraWarCount * thresholdReductionPerWar));

                float exhaustion = GetWarExhaustion(kingdom.StringId);

                // WP5: If bands are enabled, require Crisis band for forced peace.
                // Otherwise, fall back to the legacy raw threshold comparison.
                bool shouldForce;
                if (Settings.EnableDiplomacyPressureBands)
                {
                    DiplomacyPressureBand band = GetPressureBand(kingdom.StringId);
                    shouldForce = band == DiplomacyPressureBand.Crisis && exhaustion >= threshold;
                }
                else
                {
                    shouldForce = exhaustion >= threshold;
                }

                if (!shouldForce)
                {
                    DiplomacyPressureBand dbgBand = GetPressureBand(kingdom.StringId);
                    DebugDiplomacy($"Skip forced peace for {kingdom.Name}: exhaustion {exhaustion:0.0}, threshold {threshold:0.0}, band {dbgBand}.");
                    _telemetryLastForcedPeace = $"Skip {kingdom.Name}: {exhaustion:0.0}<{threshold:0.0} ({dbgBand})";
                    continue;
                }

                if (_lastForcedPeaceDayByKingdom.TryGetValue(kingdom.StringId, out float lastDay))
                {
                    if (nowDays - lastDay < cooldownDays)
                    {
                        float remain = cooldownDays - (nowDays - lastDay);
                        float resumeDay = nowDays + Math.Max(0f, remain);
                        string resumeText = FormatCampaignDateTime(resumeDay);
                        DebugDiplomacy($"Skip forced peace for {kingdom.Name}: cooldown active ({remain:0.0} days left, until {resumeText}).");
                        _telemetryLastForcedPeace = $"Skip {kingdom.Name}: cooldown until {resumeText}";
                        continue;
                    }
                }

                IFaction? bestFactionToPeace = null;
                float bestPeaceScore = float.MinValue;
                int activeWarCount = 0;

                if (kingdom.FactionsAtWarWith == null)
                    continue;

                for (int i = 0; i < kingdom.FactionsAtWarWith.Count; i++)
                {
                    IFaction enemy = kingdom.FactionsAtWarWith[i];
                    if (enemy == null || enemy.IsEliminated)
                        continue;

                    if (enemy is not Kingdom)
                        continue;

                    if (!kingdom.IsAtWarWith(enemy))
                        continue;

                    if (IsKingdomPairUnderTruce(kingdom, enemy, out _))
                    {
                        DebugDiplomacy($"Skip peace candidate {kingdom.Name} vs {enemy.Name}: truce active.");
                        continue;
                    }

                    StanceLink? stance = kingdom.GetStanceWith(enemy);
                    if (stance == null || !stance.IsAtWar)
                        continue;

                    if (minWarDays > 0 && stance.WarStartDate.ElapsedDaysUntilNow < minWarDays)
                    {
                        DebugDiplomacy($"Skip peace candidate {kingdom.Name} vs {enemy.Name}: war age {stance.WarStartDate.ElapsedDaysUntilNow:0.0} < {minWarDays} days.");
                        continue;
                    }

                    if (ignoreIfBesiegingCore && IsEnemyBesiegingCoreSettlement(kingdom, enemy))
                    {
                        DebugDiplomacy($"Skip peace candidate {kingdom.Name} vs {enemy.Name}: enemy besieging core settlement.");
                        continue;
                    }

                    // Skip peace with a faction we are actively besieging — don't
                    // waste siege progress by suing for peace mid-operation.
                    if (IsKingdomBesiegingFaction(kingdom, enemy))
                    {
                        DebugDiplomacy($"Skip peace candidate {kingdom.Name} vs {enemy.Name}: we are besieging their settlement.");
                        continue;
                    }

                    activeWarCount++;

                    var diplomacyModel = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.DiplomacyModel;
                    if (diplomacyModel == null) continue;
                    float score = diplomacyModel.GetScoreOfDeclaringPeace(kingdom, enemy);
                    if (score > bestPeaceScore)
                    {
                        bestPeaceScore = score;
                        bestFactionToPeace = enemy;
                    }
                }

                if (activeWarCount <= maxActiveWars || bestFactionToPeace == null)
                {
                    DebugDiplomacy($"Skip forced peace for {kingdom.Name}: eligible wars {activeWarCount} <= configured minimum {maxActiveWars}.");
                    _telemetryLastForcedPeace = $"Skip {kingdom.Name}: wars {activeWarCount}";
                    continue;
                }

                try
                {
                    MakePeaceAction.ApplyByKingdomDecision(kingdom, bestFactionToPeace, 0, 0);
                }
                catch (Exception ex)
                {
                    Debug.Print($"[Byzantium1071][Diplomacy] Forced peace failed for {kingdom.Name}: {ex.Message}");
                    continue;
                }
                _lastForcedPeaceDayByKingdom[kingdom.StringId] = nowDays;
                _telemetryLastForcedPeace = $"Peace {kingdom.Name}-{bestFactionToPeace.Name} at {FormatCampaignDateTime(nowDays)} (ex {exhaustion:0.0})";

                Debug.Print($"[Byzantium1071][Diplomacy] Forced peace: {kingdom.Name} ended war with {bestFactionToPeace.Name} at exhaustion {exhaustion:0.0}, wars={activeWarCount}.");
            }
        }

        private static void DebugDiplomacy(string message)
        {
            if (!(B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults).DiplomacyDebugLogs
                && !B1071_VerboseLog.Enabled)
                return;

            Debug.Print($"[Byzantium1071][Diplomacy][Debug] {message}");
        }

        private static int CountActiveMajorWars(Kingdom kingdom)
        {
            if (kingdom.FactionsAtWarWith == null) return 0;
            int count = 0;
            for (int i = 0; i < kingdom.FactionsAtWarWith.Count; i++)
            {
                IFaction enemy = kingdom.FactionsAtWarWith[i];
                if (enemy is Kingdom && kingdom.IsAtWarWith(enemy))
                    count++;
            }

            return count;
        }

        private static bool IsEnemyBesiegingCoreSettlement(Kingdom defender, IFaction attacker)
        {
            // G-7: Use kingdom.Settlements instead of Settlement.All for better performance.
            foreach (Settlement settlement in defender.Settlements)
            {
                if (settlement == null || (!settlement.IsTown && !settlement.IsCastle))
                    continue;

                if (!settlement.IsUnderSiege || settlement.SiegeEvent == null)
                    continue;

                foreach (PartyBase attackerParty in settlement.SiegeEvent.GetSiegeEventSide(BattleSideEnum.Attacker).GetInvolvedPartiesForEventType())
                {
                    if (attackerParty?.MapFaction == attacker)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns true when <paramref name="attacker"/> has any war-party
        /// currently besieging a settlement belonging to <paramref name="defender"/>.
        /// Mirror of <see cref="B1071_ExhaustionDiplomacyHelpers.IsKingdomBesiegingFaction"/>
        /// but accessible from the behavior for the forced-peace path.
        /// </summary>
        private static bool IsKingdomBesiegingFaction(Kingdom attacker, IFaction defender)
        {
            if (attacker == null || defender == null) return false;

            foreach (WarPartyComponent wpc in attacker.WarPartyComponents)
            {
                MobileParty? party = wpc.MobileParty;
                if (party == null) continue;

                Settlement? besieged = party.BesiegedSettlement;
                if (besieged != null && besieged.MapFaction == defender)
                    return true;
            }

            return false;
        }

        private void OnTroopRecruited(Hero recruiterHero, Settlement recruitmentSettlement, Hero recruitmentSource, CharacterObject troop, int amount)
        {
            if (recruiterHero == null || recruitmentSettlement == null || troop == null) return;
            if (amount <= 0) return;
            if (recruitmentSettlement.IsHideout) return;

            bool isPlayer = recruiterHero == Hero.MainHero;

            // Player recruitment is enforced via OnUnitRecruitedEvent (more granular per click).
            if (isPlayer)
            {
                // Clear AI dedupe flags to prevent false positive in OnUnitRecruitedFallback.
                _lastRecruitWasAI = false;
                _lastAIRecruitTroop = null;
                _lastAIRecruitAmount = 0;
                _lastAIRecruitSettlementId = null;
                _lastAIRecruitPartyId = null;
                return;
            }

            //1. Normal AI party recruitment: recruiterHero is party leader, recruitmentSettlement is where they recruit from.
            MobileParty? party = recruiterHero.PartyBelongedTo;

            //2. Garrison recruitment: recruiterHero is town's governor, recruitmentSettlement is the town, party is the garrison. 
            if (party == null)
                party = recruitmentSettlement.Town?.GarrisonParty;

            if (party == null)
            {
                // Still consume manpower even without a party (e.g., notable/non-party hero recruitment).
                // We can't remove excess troops but we can at least drain the pool.
                Settlement? pool = GetPoolSettlement(recruitmentSettlement);
                if (pool != null)
                {
                    EnsureEntry(pool);
                    int costPer = GetManpowerCostPerTroop(troop);
                    string poolId = pool.StringId;
                    int available = _manpowerByPoolId.TryGetValue(poolId, out int v) ? v : 0;
                    int consumed = Math.Min(available, amount * costPer);
                    _manpowerByPoolId[poolId] = Math.Max(0, available - consumed);
                    if (Settings.LogAiManpowerConsumption || B1071_VerboseLog.Enabled)
                        Debug.Print($"[Byzantium1071][AIManpower] Partyless recruit {troop.Name} x{amount} from {recruitmentSettlement.Name}, pool {available}->{available - consumed}");
                }
                // Flag so fallback skips it.
                _lastRecruitWasAI = true;
                _lastAIRecruitTroop = troop;
                _lastAIRecruitAmount = amount;
                _lastAIRecruitSettlementId = recruitmentSettlement?.StringId;
                _lastAIRecruitPartyId = null; // No party for partyless recruitment.
                return;
            }

            // Flag this AI recruitment so OnUnitRecruitedFallback can skip it.
            _lastRecruitWasAI = true;
            _lastAIRecruitTroop = troop;
            _lastAIRecruitAmount = amount;
            _lastAIRecruitSettlementId = recruitmentSettlement?.StringId;
            _lastAIRecruitPartyId = party?.StringId;

            string recruitContext = (recruitmentSettlement != null && party == recruitmentSettlement.Town?.GarrisonParty)
                ? "TroopRecruited(Garrison)"
                : "TroopRecruited(AI)";
            ConsumeManpower(recruitmentSettlement, party, troop, amount, isPlayer: false, context: recruitContext);
        }

        // Centralized manpower consumption logic.
        private void ConsumeManpower(Settlement? recruitmentSettlement, MobileParty? party, CharacterObject troop, int amount, bool isPlayer, string context)
        {
            if (recruitmentSettlement == null || party == null || troop == null) return;
            if (amount <= 0) return;

            var settings = Settings;
            Settlement? pool = GetPoolSettlement(recruitmentSettlement);
            if (pool == null) return;
            EnsureEntry(pool);

            int costPer = GetRecruitCostForParty(recruitmentSettlement, party, troop);

            if (costPer <= 0) return;

            string poolId = pool.StringId;
            if (string.IsNullOrEmpty(poolId)) return;

            int max = GetMaxManpowerCached(pool);

            if (!_manpowerByPoolId.TryGetValue(poolId, out int available))
                available = max;
            int before = available;

            int allowed = Math.Min(amount, available / costPer);
            int toRemove = amount - allowed;

            if (toRemove > 0)
            {
                int have = party.MemberRoster.GetTroopCount(troop);
                int removeNow = Math.Min(toRemove, have);

                if (removeNow > 0)
                {
                    party.MemberRoster.AddToCounts(troop, -removeNow, insertAtFront: false, woundedCount: 0, xpChange: 0, removeDepleted: true, index: -1);
                }

                if (isPlayer)
                {
                    TextObject msg = new TextObject("{=b1071_mp_not_enough_allowed}Not enough manpower in {POOL}. Allowed {ALLOWED}/{REQUESTED}.")
                        .SetTextVariable("POOL", pool.Name)
                        .SetTextVariable("ALLOWED", allowed)
                        .SetTextVariable("REQUESTED", amount);

                    InformationManager.DisplayMessage(new InformationMessage(msg.ToString()));
                }
            }

            int consumed = allowed * costPer;
            int after = Math.Max(0, available - consumed);
            _manpowerByPoolId[poolId] = after;

            if (Settings.ShowPlayerDebugMessages && isPlayer)
            {
                string where = recruitmentSettlement == pool
                    ? $"{recruitmentSettlement.Name}"
                    : $"{recruitmentSettlement.Name} (pool: {pool.Name})";

                TextObject msg = new TextObject("{=b1071_mp_dbg_consume}[Manpower:{CONTEXT}] {TROOP} x{AMOUNT} (tier {TIER}) @ {WHERE} | costPer={COST_PER} allowed={ALLOWED} removed={REMOVED} | pool {BEFORE}->{AFTER}/{MAX}")
                    .SetTextVariable("CONTEXT", context)
                    .SetTextVariable("TROOP", troop.Name)
                    .SetTextVariable("AMOUNT", amount)
                    .SetTextVariable("TIER", troop.Tier)
                    .SetTextVariable("WHERE", where)
                    .SetTextVariable("COST_PER", costPer)
                    .SetTextVariable("ALLOWED", allowed)
                    .SetTextVariable("REMOVED", toRemove)
                    .SetTextVariable("BEFORE", before)
                    .SetTextVariable("AFTER", after)
                    .SetTextVariable("MAX", max);

                InformationManager.DisplayMessage(new InformationMessage(
                    msg.ToString()
                ));
            }

            // AI: log to file, throttled
            if (!isPlayer && (Settings.LogAiManpowerConsumption || B1071_VerboseLog.Enabled))
            {
                bool shouldLog = false;

                int band = GetPoolBand(after, max);
                if (!_aiPoolBandByPoolId.TryGetValue(poolId, out int prevBand))
                    prevBand = band;

                if (band < prevBand) shouldLog = true; // crossed into a worse band (e.g., 75% -> 50%)
                if (toRemove > 0) shouldLog = true;    // manpower actually blocked recruits
                if (after == 0 && before > 0) shouldLog = true;

                if (shouldLog)
                {
                    Debug.Print(
                        $"[Byzantium1071][AIManpower] Pool {pool.Name} {before}->{after}/{max} | " +
                        $"recruit {troop.Name} x{amount} (tier {troop.Tier}) costPer={costPer} allowed={allowed} removed={toRemove} | from {recruitmentSettlement.Name}"
                    );
                }

                _aiPoolBandByPoolId[poolId] = band;
            }

            B1071_VerboseLog.Log("Manpower", $"Consume [{context}] {troop.Name} x{amount} (tier {troop.Tier}) @ {recruitmentSettlement.Name}: pool {before}->{after}/{max}, costPer={costPer}, allowed={allowed}, removed={toRemove}.");
        }

        private void EnsureEntry(Settlement? anySettlement)
        {
            Settlement? pool = GetPoolSettlement(anySettlement);
            if (pool == null) return;

            string poolId = pool.StringId;
            if (string.IsNullOrEmpty(poolId)) return;

            if (_manpowerByPoolId.TryGetValue(poolId, out int cur))
            {
                if (cur < 0)
                    _manpowerByPoolId[poolId] = 0;
                return; // Entry exists — skip GetMaxManpower entirely
            }

            // First time: seed to max
            _manpowerByPoolId[poolId] = GetMaxManpowerCached(pool);
        }

        // Per-day cache for GetMaxManpower to avoid recomputing the same heavy
        // formula thousands of times per daily-tick cycle (volunteer model, militia
        // model, garrison patch, overlay rebuild all call GetManpowerPool).
        private readonly Dictionary<string, int> _maxManpowerCache = new();

        /// <summary>
        /// Returns GetMaxManpower result, caching per pool per daily-tick cycle.
        /// Cleared in OnDailyTick via ClearDailyCache().
        /// </summary>
        private int GetMaxManpowerCached(Settlement pool)
        {
            string poolId = pool.StringId;
            if (string.IsNullOrEmpty(poolId))
                return GetMaxManpower(pool);

            if (_maxManpowerCache.TryGetValue(poolId, out int cached))
                return cached;

            int max = GetMaxManpower(pool);
            _maxManpowerCache[poolId] = max;
            return max;
        }

        public void GetManpowerPool(Settlement? settlement, out int cur, out int max, out Settlement pool)
        {
            Settlement? resolved = GetPoolSettlement(settlement);
            if (resolved == null) { cur = 0; max = 1; pool = null!; return; }

            pool = resolved;
            EnsureEntry(pool);

            string poolId = pool.StringId;
            max = GetMaxManpowerCached(pool);
            cur = _manpowerByPoolId.TryGetValue(poolId, out int v) ? v : max;
        }

        public string GetManpowerUiLine(Settlement settlement)
        {
            GetManpowerPool(settlement, out int cur, out int max, out Settlement pool);
            return $"Manpower: {cur}/{max}";
        }

        public float GetManpowerRatio(Settlement settlement)
        {
            GetManpowerPool(settlement, out int cur, out int max, out _);
            if (max <= 0) return 1f;

            float ratio = (float)cur / (float)max;
            return Clamp01(ratio);
        }

        private static Settlement? GetPoolSettlement(Settlement? s)
        {
            if (s == null) return null;
            if (s.IsVillage)
            {
                Settlement? bound = s.Village?.Bound;
                // If the village has a bound town/castle, use that as the pool.
                // If not (orphan village from modded maps), return null — no valid pool.
                return bound;
            }

            return s;
        }

        /// <summary>
        /// Finds the nearest town belonging to the same faction as <paramref name="castle"/>.
        /// Used by the castle supply chain: castles draw manpower from their nearest
        /// same-faction town rather than generating it from nothing.
        /// Returns null if no same-faction town exists (e.g. last faction holdout is a castle).
        /// </summary>
        private static Settlement? FindNearestSameFactionTown(Settlement castle)
        {
            var faction = castle.OwnerClan?.MapFaction;
            if (faction == null) return null;

            Settlement? nearest = null;
            float bestDist = float.MaxValue;
            foreach (Town town in Town.AllTowns)
            {
                if (town.Settlement == null) continue;
                if (town.Settlement.OwnerClan?.MapFaction != faction) continue;
                float dist = (castle.GetPosition2D - town.Settlement.GetPosition2D).LengthSquared;
                if (dist < bestDist) { bestDist = dist; nearest = town.Settlement; }
            }
            return nearest;
        }

        /// <summary>
        /// Adds manpower to the pool that owns <paramref name="settlement"/>.
        /// Called by the slave economy behavior after a captive sale.
        /// Clamped to [current, max]; never overflows the pool.
        /// </summary>
        internal void AddManpowerToSettlement(Settlement settlement, int amount)
        {
            if (amount <= 0) return;
            Settlement? pool = GetPoolSettlement(settlement);
            if (pool == null) return;
            EnsureEntry(pool);
            string poolId = pool.StringId;
            if (string.IsNullOrEmpty(poolId)) return;
            int max = GetMaxManpowerCached(pool);
            int cur = _manpowerByPoolId.TryGetValue(poolId, out int v) ? v : max;
            _manpowerByPoolId[poolId] = Math.Min(max, cur + amount);
        }

        private static bool IsPlayerSettlement(Settlement s)
        {
            try
            {
                Clan? playerClan = Hero.MainHero?.Clan;
                if (playerClan == null) return false;

                // Owned by player clan.
                if (s.OwnerClan == playerClan) return true;

                // Owned by a clan in the player's kingdom.
                Kingdom? kingdom = playerClan.Kingdom;
                if (kingdom != null && s.OwnerClan?.Kingdom == kingdom) return true;

                return false;
            }
            catch { return false; }
        }

        internal static int GetMaxManpower(Settlement pool)
        {
            var settings = Settings;

            // Base pool from MCM.
            int baseMax =
                pool.IsTown ? settings.TownPoolMax :
                pool.IsCastle ? settings.CastlePoolMax :
                settings.OtherPoolMax;

            baseMax = Math.Max(1, baseMax);

            // --- Economic scaling ---
            // Both towns and castles use prosperity as the primary driver (population/economy)
            // and security as a secondary multiplier (safe settlements attract more volunteers).

            float prosperityScale = 1.0f;
            float securityBonus = 1.0f;

            if (pool.Town != null)
            {
                // Prosperity → primary pool scaling (both towns and castles).
                float prosperity = pool.Town.Prosperity;
                float pN = Clamp01(prosperity / Math.Max(1f, settings.ProsperityNormalizer));
                float prosMin = Math.Max(0.01f, settings.MaxPoolProsperityMinScale / 100f);
                float prosMax = Math.Max(prosMin, settings.MaxPoolProsperityMaxScale / 100f);
                prosperityScale = prosMin + ((prosMax - prosMin) * pN);

                // Security → secondary multiplier (both towns and castles).
                float security = pool.Town.Security; // 0..100
                float sN = Clamp01(security / 100f);
                float secMin = Math.Max(0.01f, settings.SecurityBonusMinScale / 100f);
                float secMax = Math.Max(secMin, settings.SecurityBonusMaxScale / 100f);
                securityBonus = secMin + ((secMax - secMin) * sN);
            }

            // Hearth bonus: each village adds hearth × multiplier as flat manpower.
            int hearthBonus = 0;
            if (pool.Town?.Villages != null)
            {
                float mult = Math.Max(0f, settings.MaxPoolHearthMultiplier);
                foreach (var v in pool.Town.Villages)
                {
                    if (v != null)
                        hearthBonus += (int)(v.Hearth * mult);
                }
            }

            int value = (int)(baseMax * prosperityScale * securityBonus) + hearthBonus;

            // Governor bonus: Leadership skill boosts max pool.
            if (settings.EnableGovernorBonus && pool.Town?.Governor is Hero governor)
            {
                int leadership = governor.GetSkillValue(DefaultSkills.Leadership);
                float govDivisor = Math.Max(1f, settings.GovernorLeadershipPoolDivisor);
                float leadershipBonus = Math.Min(1.0f, leadership / govDivisor); // cap at +100%
                value += (int)(value * leadershipBonus);
            }

            value = Math.Max(1, value);

            // Tiny pool testing override.
            if (settings.UseTinyPoolsForTesting)
            {
                int divisor = Math.Max(1, settings.TinyPoolDivisor);
                int minimumScaledPool = Math.Max(1, settings.TinyPoolMinimum);
                value = Math.Max(minimumScaledPool, value / divisor);
            }

            return value;
        }

        internal static int GetDailyRegen(Settlement pool, int max)
        {
            var settings = Settings;

            // Stage 1: base rate from settlement type + prosperity.
            float basePct;
            float prosperityNorm = Math.Max(1f, settings.ProsperityNormalizer);

            if (pool.IsTown)
            {
                float prosperity = (pool.Town != null) ? pool.Town.Prosperity : 0f;
                float pN = Clamp01(prosperity / prosperityNorm);
                float minPct = Math.Max(0f, settings.TownRegenMinPercent) / 100f;
                float maxPct = Math.Max(minPct, settings.TownRegenMaxPercent / 100f);
                basePct = minPct + ((maxPct - minPct) * pN);
            }
            else if (pool.IsCastle)
            {
                float prosperity = (pool.Town != null) ? pool.Town.Prosperity : 0f;
                float pN = Clamp01(prosperity / prosperityNorm);
                float minPct = Math.Max(0f, settings.CastleRegenMinPercent) / 100f;
                float maxPct = Math.Max(minPct, settings.CastleRegenMaxPercent / 100f);
                basePct = minPct + ((maxPct - minPct) * pN);
            }
            else
            {
                basePct = Math.Max(0f, settings.OtherRegenPercent) / 100f;
            }

            float pct = basePct;

            // Stage 2: structural modifiers.
            // Hearth contribution (bound villages)
            float hearthSum = 0f;
            if (pool.Town != null && pool.Town.Villages != null)
            {
                foreach (var v in pool.Town.Villages)
                {
                    if (v != null)
                        hearthSum += v.Hearth;
                }
            }

            float hearthNormalizer = Math.Max(1f, settings.HearthNormalizer);
            float hN = Clamp01(hearthSum / hearthNormalizer);
            float hearthBonus = Math.Max(0f, settings.HearthBonusMaxPercent) / 100f;
            pct += hearthBonus * hN;

            float securityMul = 1f;
            float foodMul = 1f;
            float loyaltyMul = 1f;
            float siegeMul = 1f;
            float seasonalMul = 1f;
            float peaceMul = 1f;
            float governorAdd = 0f;
            float exhaustionMul = 1f;
            float recoveryMul = 1f;
            float softCapMul = 1f;

            // Security modifier on regen (both towns and castles).
            // Safe settlements attract more volunteers.
            if (pool.Town != null)
            {
                float security = pool.Town.Security; // 0..100
                float sN = Clamp01(security / 100f);
                float secMin = Math.Max(0f, settings.SecurityRegenMinScale) / 100f;
                float secMax = Math.Max(secMin, settings.SecurityRegenMaxScale / 100f);
                securityMul = secMin + ((secMax - secMin) * sN);
                pct *= securityMul;
            }

            // Stage 3: stress modifiers.
            // Food modifier on regen: starving settlements regen much slower.
            if (pool.Town != null)
            {
                float foodStocks = pool.Town.FoodStocks;
                float foodNorm = Math.Max(1f, settings.FoodStocksNormalizer);
                float fN = Clamp01(foodStocks / foodNorm);
                float foodMin = Math.Max(0f, settings.FoodRegenMinScale) / 100f;
                float foodMax = Math.Max(foodMin, settings.FoodRegenMaxScale / 100f);
                foodMul = foodMin + ((foodMax - foodMin) * fN);
                pct *= foodMul;
            }

            // Loyalty modifier on regen: disloyal populations won't volunteer.
            if (pool.Town != null)
            {
                float loyalty = pool.Town.Loyalty; // 0..100
                float lN = Clamp01(loyalty / 100f);
                float loyMin = Math.Max(0f, settings.LoyaltyRegenMinScale) / 100f;
                float loyMax = Math.Max(loyMin, settings.LoyaltyRegenMaxScale / 100f);
                loyaltyMul = loyMin + ((loyMax - loyMin) * lN);
                pct *= loyaltyMul;
            }

            // Siege penalty.
            if (pool.IsUnderSiege)
            {
                siegeMul = Math.Max(0f, settings.SiegeRegenMultiplierPercent) / 100f;
                pct *= siegeMul;
            }

            // Stage 4: seasonal effect.
            // Seasonal modifier: spring/summer = bonus, winter = penalty.
            if (settings.EnableSeasonalRegen)
            {
                var season = CampaignTime.Now.GetSeasonOfYear;
                if (season == CampaignTime.Seasons.Spring || season == CampaignTime.Seasons.Summer)
                {
                    seasonalMul = Math.Max(0f, settings.SpringSummerRegenMultiplier) / 100f;
                    pct *= seasonalMul;
                }
                else if (season == CampaignTime.Seasons.Winter)
                {
                    seasonalMul = Math.Max(0f, settings.WinterRegenMultiplier) / 100f;
                    pct *= seasonalMul;
                }
                // Autumn = 1.0× (no change)
            }

            // Peace dividend: pools regen faster when kingdom is at peace.
            if (settings.EnablePeaceDividend && pool.OwnerClan?.Kingdom is Kingdom kingdom)
            {
                if (kingdom.FactionsAtWarWith?.Count == 0)
                {
                    peaceMul = Math.Max(1f, settings.PeaceDividendMultiplier) / 100f;
                    pct *= peaceMul;
                }
            }

            // Governor bonus: Steward skill boosts regen.
            if (settings.EnableGovernorBonus && pool.Town?.Governor is Hero governor)
            {
                int steward = governor.GetSkillValue(DefaultSkills.Steward);
                float govDivisor = Math.Max(1f, settings.GovernorStewardRegenDivisor);
                governorAdd = steward / govDivisor;
                pct += governorAdd;
            }

            // War exhaustion penalty: strained kingdoms regen slower.
            if (settings.EnableWarExhaustion)
            {
                string? kingdomId = pool.OwnerClan?.Kingdom?.StringId;
                if (!string.IsNullOrEmpty(kingdomId))
                {
                    float exhaustion = Instance?.GetWarExhaustion(kingdomId) ?? 0f;
                    if (exhaustion > 0f)
                    {
                        float divisor = Math.Max(1f, settings.ExhaustionRegenDivisor);
                        float penalty = 1f - (exhaustion / divisor);
                        if (penalty < 0.1f) penalty = 0.1f; // never reduce below 10%
                        exhaustionMul = penalty;
                        pct *= exhaustionMul;
                    }
                }
            }

            // Delayed recovery penalty: post-war shock decays over time.
            if (settings.EnableDelayedRecovery)
            {
                float penalty = Instance?.GetRecoveryPenaltyFraction(pool.StringId) ?? 0f;
                if (penalty > 0f)
                {
                    recoveryMul = Math.Max(0.1f, 1f - penalty);
                    pct *= recoveryMul;
                }
            }

            // Stage 5: soft-cap near full pool.
            if (settings.EnableRegenSoftCap && max > 0)
            {
                float startRatio = Clamp01(settings.RegenSoftCapStartRatio);
                float strength = Math.Max(0f, settings.RegenSoftCapStrength);

                if (startRatio < 0.999f && strength > 0f)
                {
                    // A-3: Default to 0 when Instance is null so the soft-cap is effectively
                    // disabled rather than assuming full pool and crushing regen.
                    int current = 0;
                    if (Instance != null && !string.IsNullOrEmpty(pool.StringId) && Instance._manpowerByPoolId.TryGetValue(pool.StringId, out int cur))
                        current = cur;

                    float fillRatio = Clamp01((float)current / max);
                    if (fillRatio > startRatio)
                    {
                        float t = Clamp01((fillRatio - startRatio) / Math.Max(0.001f, 1f - startRatio));
                        float slowdown = 1f - (strength * t * t);
                        softCapMul = Math.Max(0.1f, slowdown);
                        pct *= softCapMul;
                    }
                }
            }

            // WP4: bounded stochastic variance on daily regen output.
            float varianceMul = 1f;
            if (settings.EnableRecruitmentVariance && settings.RecoveryVariancePercent > 0)
            {
                float spread = Math.Min(settings.RecoveryVariancePercent, 100f) / 100f;
                varianceMul = MBRandom.RandomFloatRanged(1f - spread, 1f + spread);
                pct *= varianceMul;
            }

            // Stress floor: final safety net — avoid permanent stall under stacked penalties.
            // Applied after ALL multipliers so nothing can push pct below this threshold.
            float stressFloor = Math.Max(0f, settings.RegenStressFloorPercent) / 100f;
            if (pct < stressFloor)
                pct = stressFloor;

            int regen = (int)(max * pct);

            // Hard cap: never exceed RegenCapPercent of pool per day.
            int cap = (int)(max * Math.Max(0.001f, settings.RegenCapPercent) / 100f);
            if (regen > cap)
                regen = cap;

            // Castle-specific minimum daily regen (garrison rotation / Crown resettlement).
            int minDailyRegen = pool.IsCastle
                ? Math.Max(0, settings.CastleMinimumDailyRegen)
                : Math.Max(0, settings.MinimumDailyRegen);
            // Cap always wins: ensure minimum never exceeds the hard cap.
            int result = Math.Min(cap, Math.Max(minDailyRegen, regen));

            // Depleted Emergency Regen: when pool is below threshold, add a flat bonus
            // that scales inversely with fill ratio (emptier = faster recovery).
            // Models Crown frontier investment and refugee influx to devastated settlements.
            int depletedBonus = 0;
            if (settings.EnableDepletedEmergencyRegen && max > 0)
            {
                float depletedThreshold = Clamp01(Math.Max(0f, settings.DepletedRegenThresholdPercent) / 100f);
                int maxBonus = Math.Max(0, settings.DepletedRegenBonusAtZero);
                if (depletedThreshold > 0f && maxBonus > 0)
                {
                    int current = 0;
                    if (Instance != null && !string.IsNullOrEmpty(pool.StringId) && Instance._manpowerByPoolId.TryGetValue(pool.StringId, out int depCur))
                        current = depCur;
                    float fillRatio = Clamp01((float)current / max);
                    if (fillRatio < depletedThreshold)
                    {
                        // Linear interpolation: at 0% fill → full bonus, at threshold → 0 bonus.
                        float t = 1f - (fillRatio / depletedThreshold);
                        depletedBonus = Math.Max(0, (int)(maxBonus * t));
                        result += depletedBonus;
                        // Emergency bonus intentionally bypasses the normal hard cap to
                        // ensure critically depleted settlements can recover.
                    }
                }
            }

            if (Instance != null)
            {
                Instance._telemetryLastRegenPoolId = pool.StringId ?? string.Empty;
                Instance._telemetryLastRegenBreakdown =
                    $"Base:{(basePct * 100f):0.###}% Final:{(pct * 100f):0.###}% Sec:{securityMul:0.##} Food:{foodMul:0.##} Loy:{loyaltyMul:0.##} Siege:{siegeMul:0.##} Season:{seasonalMul:0.##} Peace:{peaceMul:0.##} Gov:+{governorAdd:0.###} Exh:{exhaustionMul:0.##} Rec:{recoveryMul:0.##} Soft:{softCapMul:0.##} Var:{varianceMul:0.##} Dep:+{depletedBonus} => +{result}";
            }

            return result;
        }

        /// <summary>
        /// Returns the flat manpower cost per troop for standard recruitment.
        /// All tiers now cost the same: BaseManpowerCostPerTroop (default 1).
        /// </summary>
        private static int GetManpowerCostPerTroop(CharacterObject troop)
        {
            return Math.Max(1, Settings.BaseManpowerCostPerTroop);
        }

        private static int ApplyCultureDiscountIfAny(int baseCost, Settlement recruitmentSettlement, MobileParty party)
        {
            int costPer = Math.Max(1, baseCost);
            var settings = Settings;

            if (settings.EnableCultureDiscount && party.LeaderHero != null)
            {
                var settlementCulture = recruitmentSettlement.Culture;
                var heroCulture = party.LeaderHero.Culture;
                if (settlementCulture != null && heroCulture != null && settlementCulture == heroCulture)
                {
                    float costPct = Math.Max(0.01f, settings.CultureCostPercent) / 100f;
                    costPer = Math.Max(1, (int)(costPer * costPct));
                }
            }

            return costPer;
        }

        internal int GetRecruitCostForParty(Settlement recruitmentSettlement, MobileParty party, CharacterObject troop)
        {
            if (recruitmentSettlement == null || party == null || troop == null) return 1;
            int baseCost = GetManpowerCostPerTroop(troop);
            return ApplyCultureDiscountIfAny(baseCost, recruitmentSettlement, party);
        }

        internal bool CanRecruitCountForPlayer(
            Settlement recruitmentSettlement,
            MobileParty party,
            CharacterObject troop,
            int amount,
            out int available,
            out int costPer,
            out Settlement? pool)
        {
            available = 0;
            costPer = 1;
            pool = null;

            if (recruitmentSettlement == null || party == null || troop == null || amount <= 0)
                return false;

            pool = GetPoolSettlement(recruitmentSettlement);
            if (pool == null)
                return false;

            EnsureEntry(pool);
            string poolId = pool.StringId;
            if (string.IsNullOrEmpty(poolId))
                return false;

            int max = GetMaxManpowerCached(pool);
            available = _manpowerByPoolId.TryGetValue(poolId, out int v) ? v : max;
            costPer = GetRecruitCostForParty(recruitmentSettlement, party, troop);

            long required = (long)costPer * amount;
            return available >= required;
        }

        internal bool CanRecruitSequenceAllOrNothing(
            Settlement recruitmentSettlement,
            MobileParty party,
            IEnumerable<CharacterObject> troops,
            out CharacterObject? blockedTroop,
            out int neededCost,
            out int availableBefore,
            out Settlement? pool)
        {
            blockedTroop = null;
            neededCost = 0;
            availableBefore = 0;
            pool = null;

            if (recruitmentSettlement == null || party == null || troops == null)
                return false;

            pool = GetPoolSettlement(recruitmentSettlement);
            if (pool == null)
                return false;

            EnsureEntry(pool);
            string poolId = pool.StringId;
            if (string.IsNullOrEmpty(poolId))
                return false;

            int max = GetMaxManpowerCached(pool);
            int remaining = _manpowerByPoolId.TryGetValue(poolId, out int v) ? v : max;
            availableBefore = remaining;

            foreach (CharacterObject troop in troops)
            {
                if (troop == null)
                    continue;

                int costPer = GetRecruitCostForParty(recruitmentSettlement, party, troop);
                if (remaining < costPer)
                {
                    blockedTroop = troop;
                    neededCost = costPer;
                    return false;
                }

                remaining -= costPer;
            }

            return true;
        }

        /// <summary>
        /// Public entry point for external systems (e.g., castle recruitment) to consume
        /// manpower from a settlement's pool. Deducts cost based on troop tier.
        /// Does NOT remove troops from any roster — caller handles that.
        /// </summary>
        internal void ConsumeManpowerPublic(Settlement recruitmentSettlement, CharacterObject troop, int amount)
        {
            if (recruitmentSettlement == null || troop == null || amount <= 0) return;

            Settlement? pool = GetPoolSettlement(recruitmentSettlement);
            if (pool == null) return;
            EnsureEntry(pool);

            string poolId = pool.StringId;
            if (string.IsNullOrEmpty(poolId)) return;

            int costPer = GetManpowerCostPerTroop(troop);
            if (costPer <= 0) return;

            int max = GetMaxManpowerCached(pool);
            int available = _manpowerByPoolId.TryGetValue(poolId, out int v) ? v : max;
            int consumed = Math.Min(available, costPer * amount);
            _manpowerByPoolId[poolId] = Math.Max(0, available - consumed);
        }

        /// <summary>
        /// Consumes a flat amount of manpower from a settlement's pool, ignoring troop tier.
        /// Used for elite pool regeneration where the cost is a fixed MCM setting (CastleEliteManpowerCost).
        /// </summary>
        internal void ConsumeManpowerFlat(Settlement recruitmentSettlement, int totalCost)
        {
            if (recruitmentSettlement == null || totalCost <= 0) return;

            Settlement? pool = GetPoolSettlement(recruitmentSettlement);
            if (pool == null) return;
            EnsureEntry(pool);

            string poolId = pool.StringId;
            if (string.IsNullOrEmpty(poolId)) return;

            int max = GetMaxManpowerCached(pool);
            int available = _manpowerByPoolId.TryGetValue(poolId, out int v) ? v : max;
            int consumed = Math.Min(available, totalCost);
            _manpowerByPoolId[poolId] = Math.Max(0, available - consumed);
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private static int GetPoolBand(int cur, int max)
        {
            if (max <= 0) return 0;
            float r = (float)cur / (float)max;

            if (cur <= 0) return 0;
            if (r < 0.25f) return 1;
            if (r < 0.50f) return 2;
            if (r < 0.75f) return 3;
            return 4;
        }

        // ───────────────────────  WAR CONSEQUENCES  ───────────────────────

        private void OnRaidCompleted(BattleSideEnum winnerSide, RaidEventComponent raidComponent)
        {
            if (!Settings.EnableWarEffects) return;
            if (winnerSide != BattleSideEnum.Attacker) return;

            Settlement? village = raidComponent?.MapEventSettlement;
            if (village == null || !village.IsVillage) return;

            bool raidCompletionConfirmed = IsVillageRaidCompletionConfirmed(village);
            if (!raidCompletionConfirmed)
            {
                if (Settings.ShowPlayerDebugMessages)
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TaleWorlds.Localization.TextObject("{=b1071_mp_raid_not_completed}[B1071] Raid not completed at {VILLAGE}: no manpower drain.")
                            .SetTextVariable("VILLAGE", village.Name?.ToString() ?? "village")
                            .ToString()));
                return;
            }

            string villageId = village.StringId;
            if (string.IsNullOrEmpty(villageId)) return;

            int today = (int)CampaignTime.Now.ToDays;
            if (_lastRaidDrainDayByVillageId.TryGetValue(villageId, out int lastDrainDay) && lastDrainDay == today)
                return;

            _lastRaidDrainDayByVillageId[villageId] = today;

            Settlement? pool = GetPoolSettlement(village);
            if (pool == null) return;
            EnsureEntry(pool);

            string poolId = pool.StringId;
            if (string.IsNullOrEmpty(poolId)) return;

            int max = GetMaxManpowerCached(pool);
            float drainPct = Math.Max(0f, Settings.RaidManpowerDrainPercent) / 100f;
            int cur = _manpowerByPoolId.TryGetValue(poolId, out int v) ? v : max;
            int drainRequested = (int)(cur * drainPct);
            if (drainRequested <= 0 && drainPct > 0f && cur > 0)
                drainRequested = 1;

            if (drainRequested <= 0) return;

            int capPercent = Math.Max(0, Settings.RaidDailyPoolDrainCapPercent);
            int capAbsolute = (int)(max * (capPercent / 100f));
            string poolDayKey = $"{poolId}|{today}";
            int spentToday = _raidDrainSpentByPoolDay.TryGetValue(poolDayKey, out int spent) ? spent : 0;
            int remainingBudget = capAbsolute > 0 ? Math.Max(0, capAbsolute - spentToday) : drainRequested;
            int drain = capAbsolute > 0 ? Math.Min(drainRequested, remainingBudget) : drainRequested;

            if (drain <= 0)
            {
                if (Settings.ShowPlayerDebugMessages)
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TaleWorlds.Localization.TextObject("{=b1071_mp_raid_cap}[B1071] Raid at {VILLAGE}: daily raid cap reached, no manpower drain.")
                            .SetTextVariable("VILLAGE", village.Name?.ToString() ?? "village")
                            .ToString()));
                return;
            }

            int newVal = Math.Max(0, cur - drain);
            _manpowerByPoolId[poolId] = newVal;
            _raidDrainSpentByPoolDay[poolDayKey] = spentToday + drain;
            _telemetryRaidDrainToday += drain;

            B1071_VerboseLog.Log("WarEffects", $"Raid drain {village.Name} (pool {pool.Name}): -{drain} ({cur}->{newVal}/{max}).");

            ApplyDelayedRecoveryPenalty(
                pool,
                Settings.RecoveryPenaltyRaidPercent,
                Settings.RaidRecoveryDays,
                "raid");

            if (Settings.ShowPlayerDebugMessages)
            {
                TextObject msg = new TextObject("{=b1071_mp_dbg_raid_drain}[B1071] Raid on {VILLAGE}: {POOL} pool -{DRAIN} ({CUR}->{NEW}).")
                    .SetTextVariable("VILLAGE", village.Name)
                    .SetTextVariable("POOL", pool.Name)
                    .SetTextVariable("DRAIN", drain)
                    .SetTextVariable("CUR", cur)
                    .SetTextVariable("NEW", newVal);

                InformationManager.DisplayMessage(new InformationMessage(
                    msg.ToString(),
                    Colors.Red));
            }
            // War exhaustion: raid costs the defending kingdom.
            AddWarExhaustion(pool.OwnerClan?.Kingdom?.StringId, Settings.RaidExhaustionGain);        }

        private static bool IsVillageRaidCompletionConfirmed(Settlement village)
        {
            if (village == null || !village.IsVillage)
                return false;

            Village? villageComponent = village.Village;
            if (villageComponent == null)
                return false;

            Village.VillageStates state = villageComponent.VillageState;
            if (state == Village.VillageStates.BeingRaided)
                return false;

            return state == Village.VillageStates.Looted;
        }

        private void OnSiegeAftermath(
            MobileParty attackerParty,
            Settlement settlement,
            SiegeAftermathAction.SiegeAftermath aftermathType,
            Clan previousOwnerClan,
            Dictionary<MobileParty, float> contributionShares)
        {
            if (!Settings.EnableWarEffects) return;
            if (settlement == null || (!settlement.IsTown && !settlement.IsCastle)) return;

            Settlement? pool = GetPoolSettlement(settlement);
            if (pool == null) return;
            EnsureEntry(pool);

            string poolId = pool.StringId;
            if (string.IsNullOrEmpty(poolId)) return;

            int max = GetMaxManpowerCached(pool);
            float retainPct;
            switch (aftermathType)
            {
                case SiegeAftermathAction.SiegeAftermath.Devastate:
                    retainPct = Math.Max(0f, Settings.SiegeDevastateRetainPercent) / 100f;
                    break;
                case SiegeAftermathAction.SiegeAftermath.Pillage:
                    retainPct = Math.Max(0f, Settings.SiegePillageRetainPercent) / 100f;
                    break;
                default: // ShowMercy
                    retainPct = Math.Max(0f, Settings.SiegeMercyRetainPercent) / 100f;
                    break;
            }

            int newVal = Math.Max(0, (int)(max * retainPct));
            int cur = _manpowerByPoolId.TryGetValue(poolId, out int v) ? v : max;
            int appliedVal = Math.Min(cur, newVal);
            _manpowerByPoolId[poolId] = appliedVal; // only reduce, never increase
            _telemetrySiegeDrainToday += Math.Max(0, cur - appliedVal);

            B1071_VerboseLog.Log("WarEffects", $"Siege aftermath ({aftermathType}) at {settlement.Name}: pool {cur}->{appliedVal}/{max} ({retainPct:P0} retain).");

            ApplyDelayedRecoveryPenalty(
                pool,
                Settings.RecoveryPenaltySiegePercent,
                Settings.SiegeRecoveryDays,
                "siege");

            if (Settings.ShowPlayerDebugMessages)
            {
                TextObject msg = new TextObject("{=b1071_mp_dbg_siege_aftermath}[B1071] Siege aftermath ({TYPE}) at {SETTLEMENT}: pool set to {APPLIED} ({RETAIN} retain, clamped from {NEWVAL}).")
                    .SetTextVariable("TYPE", aftermathType.ToString())
                    .SetTextVariable("SETTLEMENT", settlement.Name)
                    .SetTextVariable("APPLIED", appliedVal)
                    .SetTextVariable("RETAIN", retainPct.ToString("P0"))
                    .SetTextVariable("NEWVAL", newVal);

                InformationManager.DisplayMessage(new InformationMessage(
                    msg.ToString(),
                    Colors.Red));
            }

            // War exhaustion: siege costs both sides.
            AddWarExhaustion(previousOwnerClan?.Kingdom?.StringId, Settings.SiegeExhaustionDefender);
            AddWarExhaustion(attackerParty?.LeaderHero?.Clan?.Kingdom?.StringId, Settings.SiegeExhaustionAttacker);
        }

        private void OnHeroPrisonerTaken(PartyBase capturer, Hero prisoner)
        {
            if (!Settings.EnableNobleCaptureExhaustion) return;
            if (prisoner?.MapFaction is not Kingdom kingdom) return;
            if (!prisoner.IsLord) return;  // Only nobles/heroes with a clan, not wanderers

            // Only add WE when captured by an enemy kingdom at war — not bandits,
            // caravans, or other non-kingdom factions.
            if (capturer?.MapFaction is not Kingdom capturerKingdom) return;
            if (!capturerKingdom.IsAtWarWith(kingdom)) return;

            float gain = Math.Max(0f, Settings.NobleCaptureExhaustionGain);
            if (gain <= 0f) return;

            AddWarExhaustion(kingdom.StringId, gain);

            if (Settings.TelemetryDebugLogs || B1071_VerboseLog.Enabled)
                Debug.Print($"[Byzantium1071][Exhaustion] Noble captured: {prisoner.Name} ({kingdom.Name}) by {capturerKingdom.Name} +{gain:0.0} exhaustion.");
        }

        private void OnMapEventEnded(MapEvent mapEvent)
        {
            if (!Settings.EnableWarEffects) return;
            if (mapEvent == null) return;
            if (IsVillageRaidRelatedMapEvent(mapEvent)) return;
            if (!mapEvent.IsFieldBattle && !mapEvent.IsSiegeOutside) return;

            float multiplier = Math.Max(0f, Settings.BattleCasualtyDrainMultiplier);
            if (multiplier <= 0f) return;

            DrainPoolFromSide(mapEvent.AttackerSide, multiplier);
            DrainPoolFromSide(mapEvent.DefenderSide, multiplier);

            // War exhaustion from battle casualties (kingdom-vs-kingdom only).
            AccumulateBattleExhaustion(mapEvent.AttackerSide, mapEvent.DefenderSide);
            AccumulateBattleExhaustion(mapEvent.DefenderSide, mapEvent.AttackerSide);
        }

        private static bool IsVillageRaidRelatedMapEvent(MapEvent mapEvent)
        {
            if (IsRaidLikeMapEvent(mapEvent))
                return true;

            if (HasVillageRelatedParty(mapEvent.AttackerSide) || HasVillageRelatedParty(mapEvent.DefenderSide))
                return true;

            return false;
        }

        private static PropertyInfo? _mapEventIsRaidProp;
        private static PropertyInfo? _mapEventSettlementProp;
        private static PropertyInfo? _mapEventEventTypeProp;
        private static bool _mapEventReflectionCached;

        private static bool IsRaidLikeMapEvent(MapEvent mapEvent)
        {
            var mapEventType = mapEvent.GetType();

            if (!_mapEventReflectionCached)
            {
                _mapEventIsRaidProp = mapEventType.GetProperty("IsRaid");
                _mapEventSettlementProp = mapEventType.GetProperty("MapEventSettlement");
                _mapEventEventTypeProp = mapEventType.GetProperty("EventType");
                _mapEventReflectionCached = true;
            }

            var isRaidProp = _mapEventIsRaidProp;
            if (isRaidProp != null && isRaidProp.PropertyType == typeof(bool) && isRaidProp.GetValue(mapEvent) is bool isRaid && isRaid)
                return true;

            if (_mapEventSettlementProp?.GetValue(mapEvent) is Settlement settlement && settlement.IsVillage)
                return true;

            string? eventTypeText = _mapEventEventTypeProp?.GetValue(mapEvent)?.ToString();
            if (eventTypeText is string eventType && eventType.IndexOf("Raid", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        private static bool HasVillageRelatedParty(MapEventSide? side)
        {
            var parties = side?.Parties;
            if (parties == null)
                return false;

            foreach (MapEventParty mep in parties)
            {
                if (mep == null)
                    continue;

                MobileParty? mobileParty = mep.Party?.MobileParty;
                if (mobileParty == null)
                    continue;

                if (mobileParty.IsVillager)
                    return true;

                if (mobileParty.HomeSettlement?.IsVillage == true)
                    return true;

                if (mobileParty.CurrentSettlement?.IsVillage == true)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Returns true if the given side contains at least one party belonging to a kingdom.
        /// Used to filter WE accumulation to kingdom-vs-kingdom combat only.
        /// </summary>
        private static bool HasKingdomParty(MapEventSide? side)
        {
            if (side?.Parties == null) return false;
            foreach (MapEventParty mep in side.Parties)
            {
                if (mep == null) continue;
                MobileParty? mp = mep.Party?.MobileParty;
                if (mp == null) continue;
                if (mp.LeaderHero?.Clan?.Kingdom != null) return true;
            }
            return false;
        }

        private void AccumulateBattleExhaustion(MapEventSide side, MapEventSide? opposingSide)
        {
            if (side?.Parties == null || !Settings.EnableWarExhaustion) return;

            // Only accumulate WE from kingdom-vs-kingdom combat.
            // Skip bandit ambushes, caravan attacks, villager defense, etc.
            if (!HasKingdomParty(opposingSide)) return;

            float perCasualty = Math.Max(0f, Settings.BattleExhaustionPerCasualty);
            if (perCasualty <= 0f) return;

            foreach (MapEventParty mep in side.Parties)
            {
                if (mep == null) continue;
                MobileParty? mp = mep.Party?.MobileParty;
                if (mp == null || mp.IsBandit || mp.IsCaravan || mp.IsVillager) continue;

                int died = mep.DiedInBattle?.TotalManCount ?? 0;
                int wounded = mep.WoundedInBattle?.TotalManCount ?? 0;
                int casualties = died + wounded;
                if (casualties <= 0) continue;

                string? kingdomId = mp.LeaderHero?.Clan?.Kingdom?.StringId;
                AddWarExhaustion(kingdomId, casualties * perCasualty);
            }
        }

        // Tier drain weights: T1=1.0×, T2=1.1×, T3=1.25×, T4=1.5×, T5=1.75×, T6=2.0×
        private static readonly float[] _tierDrainWeights = { 1.0f, 1.1f, 1.25f, 1.5f, 1.75f, 2.0f };

        /// <summary>
        /// Computes the effective casualty drain from a roster, optionally applying tier-based weight multipliers.
        /// Returns the sum of (count × baseMultiplier × tierWeight) for each entry.
        /// </summary>
        private static float CalcTierWeightedDrain(TroopRoster? roster, float baseMultiplier, bool applyTierWeighting)
        {
            if (roster == null || roster.TotalManCount <= 0) return 0f;
            if (!applyTierWeighting)
                return roster.TotalManCount * baseMultiplier;

            float total = 0f;
            var elements = roster.GetTroopRoster();
            for (int i = 0; i < elements.Count; i++)
            {
                var elem = elements[i];
                if (elem.Character == null) continue;
                int count = elem.Number;
                if (count <= 0) continue;
                int tier = Math.Max(1, Math.Min(6, elem.Character.Tier));
                total += count * baseMultiplier * _tierDrainWeights[tier - 1];
            }
            return total;
        }

        private void DrainPoolFromSide(MapEventSide side, float multiplier)
        {
            if (side?.Parties == null) return;

            foreach (MapEventParty mep in side.Parties)
            {
                if (mep == null) continue;
                MobileParty? mp = mep.Party?.MobileParty;
                if (mp == null || mp.IsBandit || mp.IsCaravan || mp.IsVillager) continue;

                bool tierWeighting = Settings.EnableTierWeightedCasualties;
                float drainDied = CalcTierWeightedDrain(mep.DiedInBattle, multiplier, tierWeighting);
                float drainWounded = CalcTierWeightedDrain(mep.WoundedInBattle, multiplier, tierWeighting);
                if (drainDied + drainWounded <= 0f) continue;

                int drain = Math.Max(1, (int)(drainDied + drainWounded));

                Settlement? home = mp.HomeSettlement;
                if (home == null) continue;

                Settlement? pool = GetPoolSettlement(home);
                if (pool == null) continue;
                EnsureEntry(pool);

                string poolId = pool.StringId;
                if (string.IsNullOrEmpty(poolId)) continue;

                int max = GetMaxManpowerCached(pool);
                int cur = _manpowerByPoolId.TryGetValue(poolId, out int v) ? v : max;
                int newVal = Math.Max(0, cur - drain);
                _manpowerByPoolId[poolId] = newVal;
                _telemetryBattleDrainToday += Math.Max(0, cur - newVal);
                B1071_VerboseLog.Log("WarEffects", $"Battle drain {mp.Name} -> pool {pool.Name}: -{drain} ({cur}->{newVal}/{max}).");
            }
        }

        private void OnSettlementOwnerChanged(
            Settlement settlement,
            bool openToClaim,
            Hero newOwner,
            Hero oldOwner,
            Hero capturerHero,
            ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (!Settings.EnableWarEffects) return;
            if (settlement == null || (!settlement.IsTown && !settlement.IsCastle)) return;

            // Only apply conquest effects when the settlement truly changes kingdoms.
            // Internal fief grants (ByKingDecision, ByGift, etc.) within the same
            // kingdom should NOT drain manpower or apply recovery penalties.
            Kingdom? oldKingdom = oldOwner?.Clan?.Kingdom;
            Kingdom? newKingdom = newOwner?.Clan?.Kingdom;

            bool isCrossKingdomConquest = oldKingdom != null && newKingdom != null && oldKingdom != newKingdom;

            if (!isCrossKingdomConquest)
            {
                if (Settings.ShowPlayerDebugMessages)
                {
                    string oldName = oldOwner?.Name?.ToString() ?? "None";
                    string newName = newOwner?.Name?.ToString() ?? "None";
                    string oldKName = oldKingdom?.Name?.ToString() ?? "None";
                    string newKName = newKingdom?.Name?.ToString() ?? "None";
                    TextObject msg = new TextObject("{=b1071_mp_dbg_internal_owner_change}[B1071] Internal ownership change at {SETTLEMENT} ({DETAIL}): {OLD_OWNER}->{NEW_OWNER} (kingdom: {OLD_KINGDOM}->{NEW_KINGDOM}). No conquest effects.")
                        .SetTextVariable("SETTLEMENT", settlement.Name)
                        .SetTextVariable("DETAIL", detail.ToString())
                        .SetTextVariable("OLD_OWNER", oldName)
                        .SetTextVariable("NEW_OWNER", newName)
                        .SetTextVariable("OLD_KINGDOM", oldKName)
                        .SetTextVariable("NEW_KINGDOM", newKName);

                    InformationManager.DisplayMessage(new InformationMessage(
                        msg.ToString()));
                }
                return;
            }

            Settlement? pool = GetPoolSettlement(settlement);
            if (pool == null) return;
            EnsureEntry(pool);

            string poolId = pool.StringId;
            if (string.IsNullOrEmpty(poolId)) return;

            int max = GetMaxManpowerCached(pool);
            float baseRetainPct = Math.Max(0f, Settings.ConquestPoolRetainPercent) / 100f;
            int cur = _manpowerByPoolId.TryGetValue(poolId, out int v) ? v : max;

            // Dynamic conquest protection: depleted pools retain a higher percentage.
            // Prevents ping-pong border castles from being permanently zeroed.
            float retainPct = baseRetainPct;
            if (Settings.EnableDynamicConquestProtection && max > 0)
            {
                float depletedThreshold = Clamp01(Math.Max(0f, Settings.ConquestDepletedThresholdPercent) / 100f);
                float depletedRetain = Math.Max(baseRetainPct, Math.Max(0f, Settings.ConquestDepletedRetainPercent) / 100f);
                float fillRatio = Clamp01((float)cur / max);
                if (fillRatio < depletedThreshold && depletedThreshold > 0f)
                {
                    // Linear interpolation: at 0% fill → depletedRetain, at threshold → baseRetainPct.
                    float t = fillRatio / depletedThreshold;
                    retainPct = depletedRetain + (baseRetainPct - depletedRetain) * t;
                }
            }

            int newVal = Math.Max(0, (int)(cur * retainPct));
            _manpowerByPoolId[poolId] = newVal;

            B1071_VerboseLog.Log("WarEffects", $"Conquest at {settlement.Name}: {oldKingdom?.Name}->{newKingdom?.Name}, pool {cur}->{newVal}/{max} ({retainPct:P0} retain{(retainPct > baseRetainPct ? $", boosted from {baseRetainPct:P0}" : "")}).");

            ApplyDelayedRecoveryPenalty(
                pool,
                Settings.RecoveryPenaltyConquestPercent,
                Settings.ConquestRecoveryDays,
                "conquest");

            if (Settings.ShowPlayerDebugMessages)
            {
                string oldOwnerName = oldOwner?.Name?.ToString() ?? "None";
                string newOwnerName = newOwner?.Name?.ToString() ?? "None";
                string oldKingdomName = oldKingdom?.Name?.ToString() ?? "?";
                string newKingdomName = newKingdom?.Name?.ToString() ?? "?";
                TextObject msg = new TextObject("{=b1071_mp_dbg_conquest}[B1071] Conquest at {SETTLEMENT} ({DETAIL}): {OLD_OWNER} ({OLD_KINGDOM})->{NEW_OWNER} ({NEW_KINGDOM}), pool {CUR}->{NEW} ({RETAIN} retained).")
                    .SetTextVariable("SETTLEMENT", settlement.Name)
                    .SetTextVariable("DETAIL", detail.ToString())
                    .SetTextVariable("OLD_OWNER", oldOwnerName)
                    .SetTextVariable("OLD_KINGDOM", oldKingdomName)
                    .SetTextVariable("NEW_OWNER", newOwnerName)
                    .SetTextVariable("NEW_KINGDOM", newKingdomName)
                    .SetTextVariable("CUR", cur)
                    .SetTextVariable("NEW", newVal)
                    .SetTextVariable("RETAIN", retainPct.ToString("P0"));

                InformationManager.DisplayMessage(new InformationMessage(
                    msg.ToString(),
                    Colors.Yellow));
            }

            // War exhaustion: losing a settlement costs the old owner.
            AddWarExhaustion(oldOwner?.Clan?.Kingdom?.StringId, Settings.ConquestExhaustionGain);
        }

        /// <summary>
        /// Belt-and-suspenders truce registration via the native CampaignEvent.
        /// Fires after any peace deal is committed, independently of the Harmony chain.
        /// This ensures truces are recorded even if a third-party mod's Prefix on
        /// MakePeaceAction causes our Harmony Postfix hooks to be skipped.
        /// </summary>
        private void OnMakePeaceEvent(IFaction faction1, IFaction faction2, MakePeaceAction.MakePeaceDetail detail)
        {
            if (!(Settings.EnableTruceEnforcement)) return;
            B1071_VerboseLog.Log("Diplomacy", $"Peace event ({detail}): {faction1?.Name} and {faction2?.Name}.");
            RegisterKingdomPairTruce(faction1, faction2);
        }
    }
}
