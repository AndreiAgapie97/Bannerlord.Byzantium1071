using System;
using System.Collections.Generic;
using Byzantium1071.Campaign.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace Byzantium1071.Campaign.Behaviors
{
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
        private List<string>? _exhaustionKeysScratch;
        private List<string> _savedExhaustionIds = new();
        private List<float> _savedExhaustionValues = new();
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
            }
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            Instance = this;
            UI.B1071_OverlayController.Reset();

            SeedAllPoolsIfNeeded();

            if (Hero.MainHero != null)
                InformationManager.DisplayMessage(new InformationMessage("[Byzantium1071] Manpower active."));
        }

        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            if (!Settings.ShowPlayerDebugMessages) return;
            if (hero != Hero.MainHero) return;
            if (settlement == null || settlement.IsHideout) return;

            GetManpowerPool(settlement, out int cur, out int max, out Settlement pool);

            string where = settlement == pool
                ? $"{settlement.Name}"
                : $"{settlement.Name} (pool: {pool.Name})";

            InformationManager.DisplayMessage(new InformationMessage($"[Manpower] {where}: {cur}/{max}"));
        }

        // Guards against double-deduction: OnUnitRecruitedEvent fires for ALL recruitments
        // (not just player), but provides no recruiter context. We track whether the
        // last OnTroopRecruited call was AI so the fallback can skip it.
        private bool _lastRecruitWasAI;
        private CharacterObject? _lastAIRecruitTroop;
        private int _lastAIRecruitAmount;
        private string? _lastAIRecruitSettlementId;

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
                && _lastAIRecruitSettlementId == currentSettlementId)
            {
                _lastRecruitWasAI = false;
                _lastAIRecruitTroop = null;
                _lastAIRecruitAmount = 0;
                _lastAIRecruitSettlementId = null;
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
            int max = GetMaxManpowerCached(pool);

            int cur = _manpowerByPoolId[poolId];
            int regen = GetDailyRegen(pool, max);

            int newCur = Math.Min(max, cur + regen);
            _manpowerByPoolId[poolId] = newCur;

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
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"\u26A0 Manpower critical at {pool.Name} ({pct}% - {newCur}/{max})",
                        Colors.Red));
                }

                _lastAlertBand[poolId] = band;
            }
        }

        private void OnDailyTick()
        {
            // Clear per-day caches so GetMaxManpower is recomputed with fresh data.
            _maxManpowerCache.Clear();

            // Notify the overlay to rebuild its cached settlement data.
            UI.B1071_OverlayController.MarkCacheStale();

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
                        float val = _warExhaustion[key] - decay;
                        if (val <= 0f)
                            _warExhaustion.Remove(key);
                        else
                            _warExhaustion[key] = val;
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

            float maxScore = Math.Max(1f, Settings.ExhaustionMaxScore);
            float cur = _warExhaustion.TryGetValue(kingdomId!, out float v) ? v : 0f;
            _warExhaustion[kingdomId!] = Math.Min(maxScore, cur + amount);
        }

        /// <summary>
        /// Returns the war exhaustion score for a kingdom (0 = fresh, max = crisis).
        /// </summary>
        public float GetWarExhaustion(string? kingdomId)
        {
            if (string.IsNullOrEmpty(kingdomId)) return 0f;
            return _warExhaustion.TryGetValue(kingdomId!, out float v) ? v : 0f;
        }

        private void TryApplyForcedPeaceAtCrisis()
        {
            if (!Settings.EnableExhaustionDiplomacyPressure || !Settings.EnableForcedPeaceAtCrisis)
                return;

            float threshold = Math.Max(1f, Settings.DiplomacyForcedPeaceThreshold);
            int maxActiveWars = Math.Max(0, Settings.DiplomacyForcedPeaceMaxActiveWars);
            int cooldownDays = Math.Max(1, Settings.DiplomacyForcedPeaceCooldownDays);
            float nowDays = (float)CampaignTime.Now.ToDays;

            foreach (Kingdom kingdom in Kingdom.All)
            {
                if (kingdom == null || kingdom.IsEliminated)
                    continue;

                if (Clan.PlayerClan?.Kingdom == kingdom)
                    continue;

                float exhaustion = GetWarExhaustion(kingdom.StringId);
                if (exhaustion < threshold)
                    continue;

                if (_lastForcedPeaceDayByKingdom.TryGetValue(kingdom.StringId, out float lastDay))
                {
                    if (nowDays - lastDay < cooldownDays)
                        continue;
                }

                IFaction? bestFactionToPeace = null;
                float bestPeaceScore = float.MinValue;
                int activeWarCount = 0;

                for (int i = 0; i < kingdom.FactionsAtWarWith.Count; i++)
                {
                    IFaction enemy = kingdom.FactionsAtWarWith[i];
                    if (enemy == null || enemy.IsEliminated)
                        continue;

                    if (!kingdom.IsAtWarWith(enemy))
                        continue;

                    activeWarCount++;

                    float score = TaleWorlds.CampaignSystem.Campaign.Current.Models.DiplomacyModel.GetScoreOfDeclaringPeace(kingdom, enemy);
                    if (score > bestPeaceScore)
                    {
                        bestPeaceScore = score;
                        bestFactionToPeace = enemy;
                    }
                }

                if (activeWarCount <= maxActiveWars || bestFactionToPeace == null)
                    continue;

                MakePeaceAction.ApplyByKingdomDecision(kingdom, bestFactionToPeace, 0, 0);
                _lastForcedPeaceDayByKingdom[kingdom.StringId] = nowDays;

                Debug.Print($"[Byzantium1071][Diplomacy] Forced peace: {kingdom.Name} ended war with {bestFactionToPeace.Name} at exhaustion {exhaustion:0.0}.");
            }
        }

        private void OnTroopRecruited(Hero recruiterHero, Settlement recruitmentSettlement, Hero recruitmentSource, CharacterObject troop, int amount)
        {
            if (recruiterHero == null || recruitmentSettlement == null || troop == null) return;
            if (amount <= 0) return;
            if (recruitmentSettlement.IsHideout) return;

            bool isPlayer = recruiterHero == Hero.MainHero;

            // Player recruitment is enforced via OnUnitRecruitedEvent (more granular per click).
            if (isPlayer) return;

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
                    if (Settings.LogAiManpowerConsumption)
                        Debug.Print($"[Byzantium1071][AIManpower] Partyless recruit {troop.Name} x{amount} from {recruitmentSettlement.Name}, pool {available}->{available - consumed}");
                }
                // Flag so fallback skips it.
                _lastRecruitWasAI = true;
                _lastAIRecruitTroop = troop;
                _lastAIRecruitAmount = amount;
                _lastAIRecruitSettlementId = recruitmentSettlement?.StringId;
                return;
            }

            // Flag this AI recruitment so OnUnitRecruitedFallback can skip it.
            _lastRecruitWasAI = true;
            _lastAIRecruitTroop = troop;
            _lastAIRecruitAmount = amount;
            _lastAIRecruitSettlementId = recruitmentSettlement?.StringId;

            ConsumeManpower(recruitmentSettlement, party, troop, amount, isPlayer: false, context: (party == recruitmentSettlement.Town?.GarrisonParty) ? "TroopRecruited(Garrison)" : "TroopRecruited(AI)");
        }

        // Centralized manpower consumption logic.
        private void ConsumeManpower(Settlement recruitmentSettlement, MobileParty party, CharacterObject troop, int amount, bool isPlayer, string context)
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
            int max = GetMaxManpowerCached(pool);

            int available = _manpowerByPoolId[poolId];
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
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"Not enough manpower in {pool.Name}. Allowed {allowed}/{amount}."
                    ));
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

                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Manpower:{context}] {troop.Name} x{amount} (tier {troop.Tier}) @ {where} | " +
                    $"costPer={costPer} allowed={allowed} removed={toRemove} | pool {before}->{after}/{max}"
                ));
            }

            // AI: log to file, throttled
            if (!isPlayer && Settings.LogAiManpowerConsumption)
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
                if (bound != null)
                    return bound;
            }

            return s;
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

            // Both towns and castles use prosperity for base regen rate.
            // Prosperity represents population/economy → how fast new recruits appear.
            float pct;
            float prosperityNorm = Math.Max(1f, settings.ProsperityNormalizer);

            if (pool.IsTown)
            {
                float prosperity = (pool.Town != null) ? pool.Town.Prosperity : 0f;
                float pN = Clamp01(prosperity / prosperityNorm);
                float minPct = Math.Max(0f, settings.TownRegenMinPercent) / 100f;
                float maxPct = Math.Max(minPct, settings.TownRegenMaxPercent / 100f);
                pct = minPct + ((maxPct - minPct) * pN);
            }
            else if (pool.IsCastle)
            {
                float prosperity = (pool.Town != null) ? pool.Town.Prosperity : 0f;
                float pN = Clamp01(prosperity / prosperityNorm);
                float minPct = Math.Max(0f, settings.CastleRegenMinPercent) / 100f;
                float maxPct = Math.Max(minPct, settings.CastleRegenMaxPercent / 100f);
                pct = minPct + ((maxPct - minPct) * pN);
            }
            else
            {
                pct = Math.Max(0f, settings.OtherRegenPercent) / 100f;
            }

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

            // Security modifier on regen (both towns and castles).
            // Safe settlements attract more volunteers.
            if (pool.Town != null)
            {
                float security = pool.Town.Security; // 0..100
                float sN = Clamp01(security / 100f);
                float secMin = Math.Max(0f, settings.SecurityRegenMinScale) / 100f;
                float secMax = Math.Max(secMin, settings.SecurityRegenMaxScale / 100f);
                pct *= secMin + ((secMax - secMin) * sN);
            }

            // Food modifier on regen: starving settlements regen much slower.
            if (pool.Town != null)
            {
                float foodStocks = pool.Town.FoodStocks;
                float foodNorm = Math.Max(1f, settings.FoodStocksNormalizer);
                float fN = Clamp01(foodStocks / foodNorm);
                float foodMin = Math.Max(0f, settings.FoodRegenMinScale) / 100f;
                float foodMax = Math.Max(foodMin, settings.FoodRegenMaxScale / 100f);
                pct *= foodMin + ((foodMax - foodMin) * fN);
            }

            // Loyalty modifier on regen: disloyal populations won't volunteer.
            if (pool.Town != null)
            {
                float loyalty = pool.Town.Loyalty; // 0..100
                float lN = Clamp01(loyalty / 100f);
                float loyMin = Math.Max(0f, settings.LoyaltyRegenMinScale) / 100f;
                float loyMax = Math.Max(loyMin, settings.LoyaltyRegenMaxScale / 100f);
                pct *= loyMin + ((loyMax - loyMin) * lN);
            }

            // Siege penalty.
            if (pool.IsUnderSiege)
                pct *= Math.Max(0f, settings.SiegeRegenMultiplierPercent) / 100f;

            // Seasonal modifier: spring/summer = bonus, winter = penalty.
            if (settings.EnableSeasonalRegen)
            {
                var season = CampaignTime.Now.GetSeasonOfYear;
                if (season == CampaignTime.Seasons.Spring || season == CampaignTime.Seasons.Summer)
                    pct *= Math.Max(0f, settings.SpringSummerRegenMultiplier) / 100f;
                else if (season == CampaignTime.Seasons.Winter)
                    pct *= Math.Max(0f, settings.WinterRegenMultiplier) / 100f;
                // Autumn = 1.0× (no change)
            }

            // Peace dividend: pools regen faster when kingdom is at peace.
            if (settings.EnablePeaceDividend && pool.OwnerClan?.Kingdom is Kingdom kingdom)
            {
                if (kingdom.FactionsAtWarWith?.Count == 0)
                    pct *= Math.Max(1f, settings.PeaceDividendMultiplier) / 100f;
            }

            // Governor bonus: Steward skill boosts regen.
            if (settings.EnableGovernorBonus && pool.Town?.Governor is Hero governor)
            {
                int steward = governor.GetSkillValue(DefaultSkills.Steward);
                float govDivisor = Math.Max(1f, settings.GovernorStewardRegenDivisor);
                pct += steward / govDivisor;
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
                        pct *= penalty;
                    }
                }
            }

            int regen = (int)(max * pct);

            // Hard cap: never exceed RegenCapPercent of pool per day.
            int cap = (int)(max * Math.Max(0.001f, settings.RegenCapPercent) / 100f);
            if (regen > cap)
                regen = cap;

            int minDailyRegen = Math.Max(0, settings.MinimumDailyRegen);
            // Cap always wins: ensure minimum never exceeds the hard cap.
            return Math.Min(cap, Math.Max(minDailyRegen, regen));
        }

        private static int GetManpowerCostPerTroop(CharacterObject troop)
        {
            var settings = Settings;

            int baseCost = Math.Max(1, settings.BaseManpowerCostPerTroop);
            int tiersPerStep = Math.Max(1, settings.TiersPerExtraCost);
            int tier = Math.Max(1, troop.Tier);
            int baseFormula = baseCost + ((tier - 1) / tiersPerStep);

            double scaled = baseFormula * (Math.Max(1, settings.CostMultiplierPercent) / 100.0);
            int cost = (int)Math.Round(scaled, MidpointRounding.AwayFromZero);
            return Math.Max(1, cost);
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

            Settlement? village = raidComponent?.MapEventSettlement;
            if (village == null || !village.IsVillage) return;

            Settlement? pool = GetPoolSettlement(village);
            if (pool == null) return;
            EnsureEntry(pool);

            string poolId = pool.StringId;
            if (string.IsNullOrEmpty(poolId)) return;

            int max = GetMaxManpowerCached(pool);
            float drainPct = Math.Max(0f, Settings.RaidManpowerDrainPercent) / 100f;
            int drain = (int)(max * drainPct);

            if (drain <= 0) return;

            int cur = _manpowerByPoolId.TryGetValue(poolId, out int v) ? v : max;
            int newVal = Math.Max(0, cur - drain);
            _manpowerByPoolId[poolId] = newVal;

            if (Settings.ShowPlayerDebugMessages)
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[B1071] Raid on {village.Name}: {pool.Name} pool -{drain} ({cur}→{newVal})",
                    Colors.Red));
            // War exhaustion: raid costs the defending kingdom.
            AddWarExhaustion(pool.OwnerClan?.Kingdom?.StringId, Settings.RaidExhaustionGain);        }

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
            _manpowerByPoolId[poolId] = Math.Min(cur, newVal); // only reduce, never increase

            if (Settings.ShowPlayerDebugMessages)
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[B1071] Siege aftermath ({aftermathType}) at {settlement.Name}: pool set to {newVal} ({retainPct:P0} of max)",
                    Colors.Red));

            // War exhaustion: siege costs both sides.
            AddWarExhaustion(previousOwnerClan?.Kingdom?.StringId, Settings.SiegeExhaustionDefender);
            AddWarExhaustion(attackerParty?.LeaderHero?.Clan?.Kingdom?.StringId, Settings.SiegeExhaustionAttacker);
        }

        private void OnMapEventEnded(MapEvent mapEvent)
        {
            if (!Settings.EnableWarEffects) return;
            if (mapEvent == null) return;
            if (!mapEvent.IsFieldBattle && !mapEvent.IsSiegeOutside) return;

            float multiplier = Math.Max(0f, Settings.BattleCasualtyDrainMultiplier);
            if (multiplier <= 0f) return;

            DrainPoolFromSide(mapEvent.AttackerSide, multiplier);
            DrainPoolFromSide(mapEvent.DefenderSide, multiplier);

            // War exhaustion from battle casualties.
            AccumulateBattleExhaustion(mapEvent.AttackerSide);
            AccumulateBattleExhaustion(mapEvent.DefenderSide);
        }

        private void AccumulateBattleExhaustion(MapEventSide side)
        {
            if (side?.Parties == null || !Settings.EnableWarExhaustion) return;

            float perCasualty = Math.Max(0f, Settings.BattleExhaustionPerCasualty);
            if (perCasualty <= 0f) return;

            foreach (MapEventParty mep in side.Parties)
            {
                MobileParty? mp = mep.Party?.MobileParty;
                if (mp == null || mp.IsBandit || mp.IsCaravan || mp.IsVillager) continue;

                int casualties = mep.DiedInBattle.TotalManCount + mep.WoundedInBattle.TotalManCount;
                if (casualties <= 0) continue;

                string? kingdomId = mp.LeaderHero?.Clan?.Kingdom?.StringId;
                AddWarExhaustion(kingdomId, casualties * perCasualty);
            }
        }

        private void DrainPoolFromSide(MapEventSide side, float multiplier)
        {
            if (side?.Parties == null) return;

            foreach (MapEventParty mep in side.Parties)
            {
                MobileParty? mp = mep.Party?.MobileParty;
                if (mp == null || mp.IsBandit || mp.IsCaravan || mp.IsVillager) continue;

                int casualties = mep.DiedInBattle.TotalManCount + mep.WoundedInBattle.TotalManCount;
                if (casualties <= 0) continue;

                Settlement? home = mp.HomeSettlement;
                if (home == null) continue;

                Settlement? pool = GetPoolSettlement(home);
                if (pool == null) continue;
                EnsureEntry(pool);

                string poolId = pool.StringId;
                if (string.IsNullOrEmpty(poolId)) continue;

                int drain = Math.Max(1, (int)(casualties * multiplier));
                int max = GetMaxManpowerCached(pool);
                int cur = _manpowerByPoolId.TryGetValue(poolId, out int v) ? v : max;
                int newVal = Math.Max(0, cur - drain);
                _manpowerByPoolId[poolId] = newVal;
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

            Settlement? pool = GetPoolSettlement(settlement);
            if (pool == null) return;
            EnsureEntry(pool);

            string poolId = pool.StringId;
            if (string.IsNullOrEmpty(poolId)) return;

            int max = GetMaxManpowerCached(pool);
            float retainPct = Math.Max(0f, Settings.ConquestPoolRetainPercent) / 100f;
            int cur = _manpowerByPoolId.TryGetValue(poolId, out int v) ? v : max;
            int newVal = Math.Max(0, (int)(cur * retainPct));
            _manpowerByPoolId[poolId] = newVal;

            if (Settings.ShowPlayerDebugMessages)
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[B1071] Ownership changed at {settlement.Name}: pool {cur}→{newVal} ({retainPct:P0} retained)",
                    Colors.Yellow));
            // War exhaustion: losing a settlement costs the old owner.
            AddWarExhaustion(oldOwner?.Clan?.Kingdom?.StringId, Settings.ConquestExhaustionGain);        }
    }
}
