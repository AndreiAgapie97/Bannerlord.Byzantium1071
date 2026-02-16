using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
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
        public static B1071_ManpowerBehavior? Instance { get; private set; }

        // NOTE: Key is POOL settlement StringId (Town/Castle). Villages map to their bound settlement pool.
        private readonly Dictionary<string, int> _manpowerByPoolId = new();

        // Save-friendly backing lists.
        private List<string> _savedIds = new();
        private List<int> _savedValues = new();

        private static readonly bool bDebugPlayerMessages = true;

        // Setează TRUE doar ca să testezi rapid (pool-uri foarte mici).
        private static readonly bool bTestModeTinyPools = true;
        private const int TestModeDivisor = 50; // 6000 -> 120

        // Player enforcement uses OnUnitRecruitedEvent (in practice, more reliable per-click).
        private static readonly bool bUseOnUnitRecruitedFallbackForEnforcement = true;

        // AI logging (to rgl_log / launcher logs). Throttled by “bands”, not every recruit.
        private static readonly bool bLogAiManpowerConsumption = true;

        private bool _seeded;


        // Throttle AI logs: we only log when a pool drops to a lower “band” (75/50/25/0) or when manpower blocks recruitment.
        private readonly Dictionary<string, int> _aiPoolBandByPoolId = new();

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailyTickSettlement);

            CampaignEvents.OnTroopRecruitedEvent.AddNonSerializedListener(this, OnTroopRecruited);
            CampaignEvents.OnUnitRecruitedEvent.AddNonSerializedListener(this, OnUnitRecruitedFallback);

            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
            CampaignEvents.AfterSettlementEntered.AddNonSerializedListener(this, OnAfterSettlementEntered);
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
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            Instance = this;

            SeedAllPoolsIfNeeded();

            if (Hero.MainHero != null)
                InformationManager.DisplayMessage(new InformationMessage("[Byzantium1071] Manpower active."));
        }

        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            if (!bDebugPlayerMessages) return;
            if (hero != Hero.MainHero) return;
            if (settlement == null || settlement.IsHideout) return;

            GetManpowerPool(settlement, out int cur, out int max, out Settlement pool);

            string where = settlement == pool
                ? $"{settlement.Name}"
                : $"{settlement.Name} (pool: {pool.Name})";

            InformationManager.DisplayMessage(new InformationMessage($"[Manpower] {where}: {cur}/{max}"));
        }

        private void OnAfterSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            OnSettlementEntered(party, settlement, hero);
        }

        private void OnUnitRecruitedFallback(CharacterObject troop, int amount)
        {
            if (!bUseOnUnitRecruitedFallbackForEnforcement) return;
            if (troop == null || amount <= 0) return;

            Settlement? playerSettlement = Hero.MainHero?.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement;
            if (playerSettlement == null || playerSettlement.IsHideout) return;

            Settlement pool = GetPoolSettlement(playerSettlement);
            string poolId = pool.StringId;

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
                EnsureEntry(settlement, fillToMaxClamp: true);
            }
        }

        private void OnDailyTickSettlement(Settlement settlement)
        {
            if (settlement == null || settlement.IsHideout) return;

            // Regen ONLY on pool settlements (Town/Castle), not per village.
            Settlement pool = GetPoolSettlement(settlement);
            if (pool != settlement) return;

            EnsureEntry(pool, fillToMaxClamp: false);

            string poolId = pool.StringId;
            int max = GetMaxManpower(pool);

            int cur = _manpowerByPoolId[poolId];
            int regen = GetDailyRegen(pool, max);

            _manpowerByPoolId[poolId] = Math.Min(max, cur + regen);
        }

        private void OnTroopRecruited(Hero recruiterHero, Settlement recruitmentSettlement, Hero recruitmentSource, CharacterObject troop, int amount)
        {
            if (recruiterHero == null || recruitmentSettlement == null || troop == null) return;
            if (amount <= 0) return;
            if (recruitmentSettlement.IsHideout) return;

            bool isPlayer = recruiterHero == Hero.MainHero;

            // Player recruitment is enforced via OnUnitRecruitedEvent (more granular per click).
            if (isPlayer) return;

            MobileParty? party = recruiterHero.PartyBelongedTo;
            if (party == null) return;

            ConsumeManpower(recruitmentSettlement, party, troop, amount, isPlayer: false, context: "TroopRecruited(AI)");
        }

        // Centralized manpower consumption logic.
        private void ConsumeManpower(Settlement recruitmentSettlement, MobileParty party, CharacterObject troop, int amount, bool isPlayer, string context)
        {
            if (recruitmentSettlement == null || party == null || troop == null) return;
            if (amount <= 0) return;

            Settlement pool = GetPoolSettlement(recruitmentSettlement);
            EnsureEntry(pool, fillToMaxClamp: false);

            int costPer = GetManpowerCostPerTroop(troop);
            if (costPer <= 0) return;

            string poolId = pool.StringId;
            int max = GetMaxManpower(pool);

            int available = _manpowerByPoolId[poolId];
            int before = available;

            int allowed = Math.Min(amount, available / costPer);
            int toRemove = amount - allowed;

            if (toRemove > 0)
            {
                int have = party.MemberRoster.GetTroopCount(troop);
                int removeNow = Math.Min(toRemove, have);

                if (removeNow > 0)
                    party.MemberRoster.AddToCounts(troop, -removeNow, insertAtFront: false, woundedCount: 0, xpChange: 0, removeDepleted: true, index: -1);

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

            if (bDebugPlayerMessages && isPlayer)
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
            if (!isPlayer && bLogAiManpowerConsumption)
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

        private void EnsureEntry(Settlement anySettlement, bool fillToMaxClamp)
        {
            Settlement pool = GetPoolSettlement(anySettlement);

            string poolId = pool.StringId;
            if (string.IsNullOrEmpty(poolId)) return;

            int max = GetMaxManpower(pool);

            if (!_manpowerByPoolId.TryGetValue(poolId, out int cur) || cur < 0)
            {
                _manpowerByPoolId[poolId] = max;
                return;
            }

            if (fillToMaxClamp)
                _manpowerByPoolId[poolId] = Math.Min(cur, max); // clamp down if needed
        }

        public void GetManpowerPool(Settlement settlement, out int cur, out int max, out Settlement pool)
        {
            pool = GetPoolSettlement(settlement);
            EnsureEntry(pool, fillToMaxClamp: false);

            string poolId = pool.StringId;
            max = GetMaxManpower(pool);
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

        private static Settlement GetPoolSettlement(Settlement s)
        {
            if (s.IsVillage)
            {
                Settlement? bound = s.Village?.Bound;
                if (bound != null)
                    return bound;
            }

            return s;
        }

        private static int GetMaxManpower(Settlement pool)
        {
            int value =
                pool.IsTown ? 6000 :
                pool.IsCastle ? 3000 :
                1000;

            if (bTestModeTinyPools)
                value = Math.Max(10, value / TestModeDivisor);

            return value;
        }

        private static int GetDailyRegen(Settlement pool, int max)
        {
            // Economic-based regen:
            // - Town: Prosperity
            // - Castle: Security
            // - Both: plus contribution from bound villages' hearths
            float pct;

            if (pool.IsTown)
            {
                float prosperity = (pool.Town != null) ? pool.Town.Prosperity : 0f; // typical values: a few thousand+
                float pN = Clamp01(prosperity / 8000f);
                pct = 0.008f + (0.012f * pN); // 0.8% .. 2.0% / day
            }
            else if (pool.IsCastle)
            {
                float security = (pool.Town != null) ? pool.Town.Security : 50f; // 0..100
                float sN = Clamp01(security / 100f);
                pct = 0.006f + (0.008f * sN); // 0.6% .. 1.4% / day
            }
            else
            {
                pct = 0.010f;
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

            float hN = Clamp01(hearthSum / 3000f); // tune as you like
            pct += 0.002f * hN; // up to +0.2%

            if (pool.IsUnderSiege)
                pct *= 0.25f;

            int regen = (int)(max * pct);
            return Math.Max(1, regen);
        }

        private static int GetManpowerCostPerTroop(CharacterObject troop)
        {
            int tier = Math.Max(1, troop.Tier);
            return 1 + ((tier - 1) / 2);
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
    }
}
