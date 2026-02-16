using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace Byzantium1071.Campaign.Behaviors
{
    /// <summary>
    /// Simple manpower pool per settlement:
    /// - Seeded to Max on campaign start
    /// - Regens daily per settlement
    /// - Recruitment consumes manpower; if insufficient, removes the extra troops immediately
    /// </summary>
    public sealed class B1071_ManpowerBehavior : CampaignBehaviorBase
    {
        private readonly Dictionary<string, int> _manpowerBySettlementId = new();

        // Save-friendly backing lists (IDataStore serialization is safest this way).
        private List<string> _savedIds = new();
        private List<int> _savedValues = new();

        private static readonly bool bDebugPlayerMessages = true;

        // Setează TRUE doar ca să testezi rapid (pool-uri foarte mici).
        private static readonly bool bTestModeTinyPools = true;
        private const int TestModeDivisor = 50; // 6000 -> 120

        private bool _seeded;


        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailyTickSettlement);
            CampaignEvents.OnTroopRecruitedEvent.AddNonSerializedListener(this, OnTroopRecruited);
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
            CampaignEvents.AfterSettlementEntered.AddNonSerializedListener(this, OnAfterSettlementEntered);
            CampaignEvents.OnUnitRecruitedEvent.AddNonSerializedListener(this, OnUnitRecruitedFallback);

        }

        public override void SyncData(IDataStore dataStore)
        {
            _savedIds ??= new List<string>();
            _savedValues ??= new List<int>();

            if (!dataStore.IsLoading)
            {
                _savedIds.Clear();
                _savedValues.Clear();

                foreach (var kvp in _manpowerBySettlementId)
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
                _manpowerBySettlementId.Clear();

                int n = Math.Min(_savedIds.Count, _savedValues.Count);
                for (int i = 0; i < n; i++)
                {
                    var id = _savedIds[i];
                    if (!string.IsNullOrEmpty(id))
                        _manpowerBySettlementId[id] = _savedValues[i];
                }
            }
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            SeedAllSettlementsIfNeeded();

            // Tiny confirmation for the player (avoid spam).
            if (Hero.MainHero != null)
                InformationManager.DisplayMessage(new InformationMessage("[Byzantium1071] Manpower active."));
        }

        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {

            if (hero != Hero.MainHero) return;
            if (settlement == null || settlement.IsHideout) return;

            EnsureEntry(settlement, fillToMax: false);

            string id = settlement.StringId;
            int max = GetMaxManpower(settlement);
            int cur = _manpowerBySettlementId.TryGetValue(id, out var v) ? v : 0;

            InformationManager.DisplayMessage(new InformationMessage(
                $"[Manpower] {settlement.Name}: {cur}/{max}"
            ));
        }

        private void OnAfterSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            // Some flows fire AfterSettlementEntered more reliably than SettlementEntered.
            OnSettlementEntered(party, settlement, hero);
        }

        private void OnUnitRecruitedFallback(CharacterObject troop, int amount)
        {
            if (!bDebugPlayerMessages) return;
            if (troop == null || amount <= 0) return;

            // This event doesn't include recruiter/settlement; for debug we only show when the player is in a settlement.
            Settlement? playerSettlement = Hero.MainHero?.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement;
            if (playerSettlement == null || playerSettlement.IsHideout) return;

            InformationManager.DisplayMessage(new InformationMessage(
                $"[Manpower] OnUnitRecruitedEvent: {troop.Name} x{amount} (tier {troop.Tier}) @ {playerSettlement.Name}"
            ));


            // Enforce manpower for the player when recruiting inside a settlement.
            // OnUnitRecruitedEvent fires reliably per recruit in your setup. :contentReference[oaicite:2]{index=2}
            if (Hero.MainHero != null)
            {
                MobileParty? main = MobileParty.MainParty;
                if (main != null)
                {
                    ConsumeManpower(playerSettlement, main, troop, amount, isPlayer: true, context: "UnitRecruited");
                }
            }
        }

        private void SeedAllSettlementsIfNeeded()
        {
            if (_seeded) return;
            _seeded = true;

            // Settlement.All exists (static list). :contentReference[oaicite:2]{index=2}
            foreach (var settlement in Settlement.All)
            {
                if (settlement == null || settlement.IsHideout) continue;
                EnsureEntry(settlement, fillToMax: true);
            }
        }

        private void OnDailyTickSettlement(Settlement settlement)
        {
            if (settlement == null || settlement.IsHideout) return;

            EnsureEntry(settlement, fillToMax: false);

            string id = settlement.StringId;
            int max = GetMaxManpower(settlement);

            int cur = _manpowerBySettlementId[id];
            int regen = GetDailyRegen(settlement, max);

            _manpowerBySettlementId[id] = Math.Min(max, cur + regen);
        }

        // Centralized manpower consumption logic, used by OnTroopRecruited and optionally by OnUnitRecruited fallback.
        private void ConsumeManpower(Settlement settlement, MobileParty party, CharacterObject troop, int amount, bool isPlayer, string context)
        {
            if (settlement == null || party == null || troop == null) return;
            if (amount <= 0) return;

            EnsureEntry(settlement, fillToMax: false);

            int costPer = GetManpowerCostPerTroop(troop);
            if (costPer <= 0) return;

            string id = settlement.StringId;
            int available = _manpowerBySettlementId[id];
            int before = available;

            int allowed = Math.Min(amount, available / costPer);
            int toRemove = amount - allowed;

            if (toRemove > 0)
            {
                // Safe remove: don’t try to remove more than roster currently has.
                int have = party.MemberRoster.GetTroopCount(troop); // TroopRoster.GetTroopCount exists. :contentReference[oaicite:1]{index=1}
                int removeNow = Math.Min(toRemove, have);

                if (removeNow > 0)
                {
                    party.MemberRoster.AddToCounts(troop, -removeNow, insertAtFront: false, woundedCount: 0, xpChange: 0, removeDepleted: true, index: -1);
                }

                if (isPlayer)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"Not enough manpower in {settlement.Name}. Allowed {allowed}/{amount}."
                    ));
                }
            }

            int consumed = allowed * costPer;
            _manpowerBySettlementId[id] = Math.Max(0, available - consumed);

            if (bDebugPlayerMessages && isPlayer)
            {
                int after = _manpowerBySettlementId[id];
                int max = GetMaxManpower(settlement);
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Manpower:{context}] {troop.Name} x{amount} (tier {troop.Tier}) @ {settlement.Name} | " +
                    $"costPer={costPer} allowed={allowed} removed={toRemove} | pool {before}->{after}/{max}"
                ));
            }
        }



        private void OnTroopRecruited(Hero recruiterHero, Settlement recruitmentSettlement, Hero recruitmentSource, CharacterObject troop, int amount)
        {
            if (recruiterHero == null || recruitmentSettlement == null || troop == null) return;
            if (amount <= 0) return;
            if (recruitmentSettlement.IsHideout) return;

            bool isPlayer = recruiterHero == Hero.MainHero;

            // Player recruitment is handled via OnUnitRecruitedEvent to get correct per-troop counts.
            // This avoids undercounting (and avoids double consuming).
            if (isPlayer) return;

            MobileParty? party = recruiterHero.PartyBelongedTo;
            if (party == null) return;

            ConsumeManpower(recruitmentSettlement, party, troop, amount, isPlayer: false, context: "TroopRecruited(AI)");
        }

        private void EnsureEntry(Settlement settlement, bool fillToMax)
        {
            string id = settlement.StringId;
            if (string.IsNullOrEmpty(id)) return;

            int max = GetMaxManpower(settlement);

            if (!_manpowerBySettlementId.TryGetValue(id, out int cur) || cur < 0)
            {
                _manpowerBySettlementId[id] = max;
                return;
            }

            if (fillToMax)
                _manpowerBySettlementId[id] = Math.Min(cur, max); // clamp down if needed
        }

        public float GetManpowerRatio(Settlement settlement)
        {
            if (settlement == null) return 1f;

            EnsureEntry(settlement, fillToMax: false);

            string id = settlement.StringId;
            int max = GetMaxManpower(settlement);
            int cur = _manpowerBySettlementId.TryGetValue(id, out var v) ? v : max;

            if (max <= 0) return 1f;
            return MathF.Max(0f, MathF.Min(1f, (float)cur / max));
        }

        private static int GetMaxManpower(Settlement s)
        {
            int value =
                s.IsTown ? 6000 :
                s.IsCastle ? 3000 :
                s.IsVillage ? (1200 + (int)((s.Village?.Hearth ?? 600f) * 0.5f)) :
                1000;

            if (bTestModeTinyPools)
                value = Math.Max(10, value / TestModeDivisor);

            return value;
        }

        private static int GetDailyRegen(Settlement s, int max)
        {
            float pct = s.IsTown ? 0.015f : (s.IsCastle ? 0.012f : 0.010f);

            if (s.IsUnderSiege)
                pct *= 0.25f;

            int regen = (int)(max * pct);
            return Math.Max(1, regen);
        }

        private static int GetManpowerCostPerTroop(CharacterObject troop)
        {
            // CharacterObject.Tier exists. :contentReference[oaicite:4]{index=4}
            int tier = Math.Max(1, troop.Tier);

            // 1-2 => 1 manpower; 3-4 => 2; 5-6 => 3; etc.
            return 1 + ((tier - 1) / 2);
        }
    }
}
