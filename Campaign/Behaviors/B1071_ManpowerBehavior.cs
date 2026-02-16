using System;
using System.Collections.Generic;
using Byzantium1071.Campaign.Settings;
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

        // MCM settings live source. If MCM is unavailable for any reason, fall back to defaults.
        private static readonly B1071_McmSettings FallbackSettings = new();
        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? FallbackSettings;

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
            if (!Settings.ShowPlayerDebugMessages) return;
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
            if (!Settings.UseOnUnitRecruitedFallbackForPlayer) return;
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

            //1. Normal AI party recruitment: recruiterHero is party leader, recruitmentSettlement is where they recruit from.
            MobileParty? party = recruiterHero.PartyBelongedTo;
            if (party == null) return;

            //2. Garrison recruitment: recruiterHero is town's governor, recruitmentSettlement is the town, party is the garrison. 
            if (party == null)
                party = recruitmentSettlement.Town?.GarrisonParty; // GarrisonParty e pe fief/town. :contentReference[oaicite:1]{index=1}

            if (party == null) return;

            ConsumeManpower(recruitmentSettlement, party, troop, amount, isPlayer: false, context: (party == recruitmentSettlement.Town?.GarrisonParty) ? "TroopRecruited(Garrison)" : "TroopRecruited(AI)");
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
            var settings = Settings;

            int value =
                pool.IsTown ? settings.TownPoolMax :
                pool.IsCastle ? settings.CastlePoolMax :
                settings.OtherPoolMax;

            value = Math.Max(1, value);

            if (settings.UseTinyPoolsForTesting)
            {
                int divisor = Math.Max(1, settings.TinyPoolDivisor);
                int minimumScaledPool = Math.Max(1, settings.TinyPoolMinimum);
                value = Math.Max(minimumScaledPool, value / divisor);
            }

            return value;
        }

        private static int GetDailyRegen(Settlement pool, int max)
        {
            var settings = Settings;

            // Economic-based regen:
            // - Town: Prosperity
            // - Castle: Security
            // - Both: plus contribution from bound villages' hearths
            float pct;

            if (pool.IsTown)
            {
                float prosperity = (pool.Town != null) ? pool.Town.Prosperity : 0f; // typical values: a few thousand+
                float pN = Clamp01(prosperity / 8000f);
                float minPct = Math.Max(0f, settings.TownRegenMinPercent) / 100f;
                float maxPct = Math.Max(minPct, settings.TownRegenMaxPercent / 100f);
                pct = minPct + ((maxPct - minPct) * pN);
            }
            else if (pool.IsCastle)
            {
                float security = (pool.Town != null) ? pool.Town.Security : 50f; // 0..100
                float sN = Clamp01(security / 100f);
                float minPct = Math.Max(0f, settings.CastleRegenMinPercent) / 100f;
                float maxPct = Math.Max(minPct, settings.CastleRegenMaxPercent / 100f);
                pct = minPct + ((maxPct - minPct) * sN);
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

            if (pool.IsUnderSiege)
                pct *= Math.Max(0f, settings.SiegeRegenMultiplierPercent) / 100f;

            int regen = (int)(max * pct);
            int minDailyRegen = Math.Max(0, settings.MinimumDailyRegen);
            return Math.Max(minDailyRegen, regen);
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
