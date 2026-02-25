using Byzantium1071.Campaign.Settings;
using Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;

namespace Byzantium1071.Campaign.Behaviors
{
    /// <summary>
    /// Castle Recruitment System — v2 (elite pool + prisoner conversion)
    ///
    ///  THREE RECRUITMENT SOURCES AT EACH CASTLE:
    ///
    ///   1. ELITE TROOP POOL (culture-based)
    ///      Castles generate T4/T5/T6 troops matching the settlement culture.
    ///      The pool regenerates daily from the castle's manpower, capped by
    ///      CastleElitePoolMax. Anyone visiting an allied/neutral castle can recruit.
    ///
    ///   2. CONVERTED PRISONERS (ready)
    ///      T4+ prisoners held long enough become recruitable. Same waiting-period
    ///      system as before.
    ///
    ///   3. PENDING PRISONERS (not yet ready)
    ///      T4+ prisoners still serving their waiting period.
    ///
    ///  LOW-TIER PRISONER AUTO-ENSLAVEMENT
    ///   T1-T3 prisoners are auto-enslaved to the nearest town market (daily tick).
    ///
    ///  AI RECRUITMENT
    ///   AI lord parties currently at a castle auto-recruit from the elite pool,
    ///   adding troops to their party (up to a budget/need limit per day).
    ///
    ///  ACCESS RULES
    ///   Player can recruit from any castle that is NOT hostile (own, allied, or neutral).
    ///   Player can deposit prisoners at neutral castles via a custom dungeon menu option
    ///   (vanilla's "Donate prisoners" only covers same-faction; our menu extends to neutral).
    ///   AI lords recruit from any non-hostile castle (own faction or neutral).
    ///   AI lords deposit prisoners at any non-hostile castle they enter.
    ///
    ///  PERSISTENCE
    ///   _prisonerDaysHeld: per-castle per-troop day counters (prisoner conversion)
    ///   _elitePool: per-castle per-troop stock counts (culture elite pool)
    ///   Both saved via SyncData, null-safe on old save load.
    /// </summary>
    public sealed class B1071_CastleRecruitmentBehavior : CampaignBehaviorBase
    {
        public static B1071_CastleRecruitmentBehavior? Instance { get; internal set; }

        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        private ItemObject? _slaveItem;

        // ── Prisoner conversion tracking ──────────────────────────────────────────

        /// <summary>
        /// Outer key = castle StringId, inner key = CharacterObject.StringId, value = days held.
        /// </summary>
        private Dictionary<string, Dictionary<string, int>> _prisonerDaysHeld
            = new Dictionary<string, Dictionary<string, int>>();

        // ── Elite troop pool ──────────────────────────────────────────────────────

        /// <summary>
        /// Outer key = castle StringId, inner key = CharacterObject.StringId, value = available count.
        /// Regenerated daily from manpower, capped per castle.
        /// </summary>
        private Dictionary<string, Dictionary<string, int>> _elitePool
            = new Dictionary<string, Dictionary<string, int>>();

        /// <summary>
        /// Cache: culture StringId → list of T4-T6 CharacterObjects for that culture.
        /// Built once per session, cleared on unload.
        /// </summary>
        private readonly Dictionary<string, List<CharacterObject>> _cultureTroopCache
            = new Dictionary<string, List<CharacterObject>>();

        // ── Save/load backing lists (flat format for IDataStore) ──────────────────

        private List<string>? _savedPrisonerCastleIds;
        private List<string>? _savedPrisonerTroopIds;
        private List<int>? _savedPrisonerDays;

        private List<string>? _savedEliteCastleIds;
        private List<string>? _savedEliteTroopIds;
        private List<int>? _savedEliteCounts;

        // ── Depositor tracking (consignment model) ────────────────────────────────

        /// <summary>
        /// Tracks WHO deposited each prisoner at each castle, enabling fair income splits.
        ///
        /// Structure: castleId → troopId → list of (heroStringId, count) entries (FIFO order).
        /// When prisoners are consumed (enslaved, recruited, garrison-absorbed), we pop
        /// counts from the front of the list. If the list is exhausted but prisoners
        /// remain in the roster (siege conquest or pre-tracking), those have no depositor
        /// and the castle owner gets 100%.
        /// </summary>
        private Dictionary<string, Dictionary<string, List<(string HeroId, int Count)>>> _depositorTracking
            = new Dictionary<string, Dictionary<string, List<(string, int)>>>();

        private List<string>? _savedDepositorCastleIds;
        private List<string>? _savedDepositorTroopIds;
        private List<string>? _savedDepositorHeroIds;
        private List<int>? _savedDepositorCounts;

        // ── CampaignBehaviorBase ──────────────────────────────────────────────────

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailyTickSettlement);
            CampaignEvents.OnPrisonerDonatedToSettlementEvent.AddNonSerializedListener(this, OnPrisonerDonatedToSettlement);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // ── Prisoner days: flatten Dict<castle, Dict<troop, days>> → 3 parallel lists ──
            _savedPrisonerCastleIds ??= new List<string>();
            _savedPrisonerTroopIds ??= new List<string>();
            _savedPrisonerDays ??= new List<int>();

            if (!dataStore.IsLoading)
            {
                _savedPrisonerCastleIds.Clear();
                _savedPrisonerTroopIds.Clear();
                _savedPrisonerDays.Clear();

                foreach (var castleKvp in _prisonerDaysHeld)
                    foreach (var troopKvp in castleKvp.Value)
                    {
                        _savedPrisonerCastleIds.Add(castleKvp.Key);
                        _savedPrisonerTroopIds.Add(troopKvp.Key);
                        _savedPrisonerDays.Add(troopKvp.Value);
                    }
            }

            dataStore.SyncData("b1071_cr_prisonerCastles", ref _savedPrisonerCastleIds);
            dataStore.SyncData("b1071_cr_prisonerTroops", ref _savedPrisonerTroopIds);
            dataStore.SyncData("b1071_cr_prisonerDays", ref _savedPrisonerDays);

            _savedPrisonerCastleIds ??= new List<string>();
            _savedPrisonerTroopIds ??= new List<string>();
            _savedPrisonerDays ??= new List<int>();

            if (dataStore.IsLoading)
            {
                _prisonerDaysHeld.Clear();
                int n = Math.Min(_savedPrisonerCastleIds.Count,
                        Math.Min(_savedPrisonerTroopIds.Count, _savedPrisonerDays.Count));
                for (int i = 0; i < n; i++)
                {
                    string cId = _savedPrisonerCastleIds[i];
                    string tId = _savedPrisonerTroopIds[i];
                    if (string.IsNullOrEmpty(cId) || string.IsNullOrEmpty(tId)) continue;

                    if (!_prisonerDaysHeld.TryGetValue(cId, out var dict))
                    {
                        dict = new Dictionary<string, int>();
                        _prisonerDaysHeld[cId] = dict;
                    }
                    dict[tId] = _savedPrisonerDays[i];
                }
            }

            // ── Elite pool: flatten Dict<castle, Dict<troop, count>> → 3 parallel lists ──
            _savedEliteCastleIds ??= new List<string>();
            _savedEliteTroopIds ??= new List<string>();
            _savedEliteCounts ??= new List<int>();

            if (!dataStore.IsLoading)
            {
                _savedEliteCastleIds.Clear();
                _savedEliteTroopIds.Clear();
                _savedEliteCounts.Clear();

                foreach (var castleKvp in _elitePool)
                    foreach (var troopKvp in castleKvp.Value)
                    {
                        _savedEliteCastleIds.Add(castleKvp.Key);
                        _savedEliteTroopIds.Add(troopKvp.Key);
                        _savedEliteCounts.Add(troopKvp.Value);
                    }
            }

            dataStore.SyncData("b1071_cr_eliteCastles", ref _savedEliteCastleIds);
            dataStore.SyncData("b1071_cr_eliteTroops", ref _savedEliteTroopIds);
            dataStore.SyncData("b1071_cr_eliteCounts", ref _savedEliteCounts);

            _savedEliteCastleIds ??= new List<string>();
            _savedEliteTroopIds ??= new List<string>();
            _savedEliteCounts ??= new List<int>();

            if (dataStore.IsLoading)
            {
                _elitePool.Clear();
                int ne = Math.Min(_savedEliteCastleIds.Count,
                         Math.Min(_savedEliteTroopIds.Count, _savedEliteCounts.Count));
                for (int i = 0; i < ne; i++)
                {
                    string cId = _savedEliteCastleIds[i];
                    string tId = _savedEliteTroopIds[i];
                    if (string.IsNullOrEmpty(cId) || string.IsNullOrEmpty(tId)) continue;

                    if (!_elitePool.TryGetValue(cId, out var dict))
                    {
                        dict = new Dictionary<string, int>();
                        _elitePool[cId] = dict;
                    }
                    dict[tId] = _savedEliteCounts[i];
                }
            }

            // ── Depositor tracking: flatten Dict<castle, Dict<troop, List<(hero, count)>>> → 4 parallel lists ──
            _savedDepositorCastleIds ??= new List<string>();
            _savedDepositorTroopIds ??= new List<string>();
            _savedDepositorHeroIds ??= new List<string>();
            _savedDepositorCounts ??= new List<int>();

            if (!dataStore.IsLoading)
            {
                _savedDepositorCastleIds.Clear();
                _savedDepositorTroopIds.Clear();
                _savedDepositorHeroIds.Clear();
                _savedDepositorCounts.Clear();

                foreach (var castleKvp in _depositorTracking)
                    foreach (var troopKvp in castleKvp.Value)
                        foreach (var entry in troopKvp.Value)
                        {
                            if (entry.Count <= 0) continue;
                            _savedDepositorCastleIds.Add(castleKvp.Key);
                            _savedDepositorTroopIds.Add(troopKvp.Key);
                            _savedDepositorHeroIds.Add(entry.HeroId);
                            _savedDepositorCounts.Add(entry.Count);
                        }
            }

            dataStore.SyncData("b1071_cr_depCastles", ref _savedDepositorCastleIds);
            dataStore.SyncData("b1071_cr_depTroops", ref _savedDepositorTroopIds);
            dataStore.SyncData("b1071_cr_depHeroes", ref _savedDepositorHeroIds);
            dataStore.SyncData("b1071_cr_depCounts", ref _savedDepositorCounts);

            _savedDepositorCastleIds ??= new List<string>();
            _savedDepositorTroopIds ??= new List<string>();
            _savedDepositorHeroIds ??= new List<string>();
            _savedDepositorCounts ??= new List<int>();

            if (dataStore.IsLoading)
            {
                _depositorTracking.Clear();
                int nd = Math.Min(_savedDepositorCastleIds.Count,
                         Math.Min(_savedDepositorTroopIds.Count,
                         Math.Min(_savedDepositorHeroIds.Count, _savedDepositorCounts.Count)));
                for (int i = 0; i < nd; i++)
                {
                    string cId = _savedDepositorCastleIds[i];
                    string tId = _savedDepositorTroopIds[i];
                    string hId = _savedDepositorHeroIds[i];
                    int cnt = _savedDepositorCounts[i];
                    if (string.IsNullOrEmpty(cId) || string.IsNullOrEmpty(tId)
                        || string.IsNullOrEmpty(hId) || cnt <= 0) continue;

                    if (!_depositorTracking.TryGetValue(cId, out var troopDict))
                    {
                        troopDict = new Dictionary<string, List<(string, int)>>();
                        _depositorTracking[cId] = troopDict;
                    }
                    if (!troopDict.TryGetValue(tId, out var heroList))
                    {
                        heroList = new List<(string, int)>();
                        troopDict[tId] = heroList;
                    }
                    heroList.Add((hId, cnt));
                }
            }
        }

        // ── Session launch ────────────────────────────────────────────────────────

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            Instance = this;
            _slaveItem = MBObjectManager.Instance.GetObject<ItemObject>("b1071_slave");
            _cultureTroopCache.Clear();

            RegisterMenus(starter);
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  DAILY TICK
        // ══════════════════════════════════════════════════════════════════════════

        private void OnDailyTickSettlement(Settlement settlement)
        {
            if (!Settings.EnableCastleRecruitment) return;
            if (settlement == null || !settlement.IsCastle) return;

            // 1. Auto-enslave low-tier prisoners
            AutoEnslaveLowTierPrisoners(settlement);

            // 2. Track / increment days for high-tier prisoners
            TrackHighTierPrisonerDays(settlement);

            // 3. Regenerate elite troop pool from manpower
            RegenerateElitePool(settlement);

            // 4. AI auto-recruitment from elite pool + converted prisoners
            AiAutoRecruit(settlement);

            // 5. Garrison absorbs ready prisoners at the auto-recruit rate
            GarrisonAbsorbPrisoners(settlement);
        }

        // ── 1. Auto-enslave ───────────────────────────────────────────────────────

        private void AutoEnslaveLowTierPrisoners(Settlement settlement)
        {
            if (!Settings.EnableSlaveEconomy || _slaveItem == null) return;

            TroopRoster? prisonRoster = settlement.Party?.PrisonRoster;
            if (prisonRoster == null || prisonRoster.TotalManCount == 0) return;

            int enslaveTierMax = Settings.CastlePrisonerAutoEnslaveTierMax;

            var toEnslave = prisonRoster.GetTroopRoster()
                .Where(e => e.Character != null
                         && !e.Character.IsHero
                         && e.Number > 0
                         && e.Character.Tier <= enslaveTierMax)
                .ToList();

            if (toEnslave.Count == 0) return;

            Settlement? nearestTown = FindNearestTown(settlement);
            if (nearestTown == null) return;

            Town? town = nearestTown.Town;
            if (town == null) return;

            int slavePrice = town.GetItemPrice(_slaveItem);
            if (slavePrice <= 0) return;

            string castleId = settlement.StringId;

            // The nearest town BUYS the slaves from the castle. Process one unit
            // at a time — only enslave what the town can afford. When the town's
            // gold runs out, remaining prisoners stay in the castle dungeon.
            foreach (var element in toEnslave)
            {
                for (int unit = 0; unit < element.Number; unit++)
                {
                    // Affordability gate: town must have gold to buy this slave.
                    int townGold = town.Gold;
                    if (townGold < slavePrice) return; // Town broke — stop all enslavement.

                    prisonRoster.RemoveTroop(element.Character, 1);
                    nearestTown.ItemRoster.AddToCounts(_slaveItem, 1);

                    // Town pays for the slave. Income distributed to depositor/owner
                    // via GiveGoldAction.ApplyForSettlementToCharacter (deducts from
                    // town gold, properly clamped, fires campaign events).
                    var depositorEntries = ConsumeDepositorEntries(castleId, element.Character.StringId, 1);
                    foreach (var (heroId, consumed) in depositorEntries)
                    {
                        int income = slavePrice * consumed;
                        DistributeIncome(settlement, heroId, income, nearestTown);
                    }
                }
            }
        }

        // ── 2. Track high-tier prisoner days ──────────────────────────────────────

        private void TrackHighTierPrisonerDays(Settlement settlement)
        {
            TroopRoster? prisonRoster = settlement.Party?.PrisonRoster;
            if (prisonRoster == null || prisonRoster.TotalManCount == 0) return;

            string castleId = settlement.StringId;
            int enslaveTierMax = Settings.CastlePrisonerAutoEnslaveTierMax;

            var highTier = prisonRoster.GetTroopRoster()
                .Where(e => e.Character != null
                         && !e.Character.IsHero
                         && e.Number > 0
                         && e.Character.Tier > enslaveTierMax)
                .ToList();

            if (highTier.Count > 0)
            {
                if (!_prisonerDaysHeld.TryGetValue(castleId, out var castleDict))
                {
                    castleDict = new Dictionary<string, int>();
                    _prisonerDaysHeld[castleId] = castleDict;
                }

                foreach (var element in highTier)
                {
                    string troopId = element.Character.StringId;
                    if (castleDict.TryGetValue(troopId, out int days))
                        castleDict[troopId] = days + 1;
                    else
                        castleDict[troopId] = 1;
                }
            }

            CleanupStalePrisonerEntries(castleId, prisonRoster);
        }

        // ── 3. Elite pool regeneration ────────────────────────────────────────────

        private void RegenerateElitePool(Settlement settlement)
        {
            CultureObject? culture = settlement.Culture;
            if (culture == null) return;

            var cultureTroops = GetCultureEliteTroops(culture);
            if (cultureTroops.Count == 0) return;

            string castleId = settlement.StringId;
            int poolMax = Settings.CastleElitePoolMax;

            if (!_elitePool.TryGetValue(castleId, out var poolDict))
            {
                poolDict = new Dictionary<string, int>();
                _elitePool[castleId] = poolDict;
            }

            // Current total across all troop types at this castle.
            int currentTotal = poolDict.Values.Sum();
            if (currentTotal >= poolMax) return;

            // Determine how many troops to add today (based on castle prosperity).
            float prosperity = settlement.Town?.Prosperity ?? 0f;
            float prosperityRatio = Math.Min(1f, prosperity / Settings.ProsperityNormalizer);
            int regenMin = Settings.CastleEliteRegenMin;
            int regenMax = Settings.CastleEliteRegenMax;
            int toAdd = Math.Max(regenMin, (int)Math.Round(regenMin + (regenMax - regenMin) * prosperityRatio));
            toAdd = Math.Min(toAdd, poolMax - currentTotal);

            if (toAdd <= 0) return;

            // Drain manpower for the elite regen.
            if (Settings.CastleRecruitDrainsManpower)
            {
                B1071_ManpowerBehavior? manpowerBehavior = B1071_ManpowerBehavior.Instance;
                if (manpowerBehavior != null)
                {
                    manpowerBehavior.GetManpowerPool(settlement, out int curManpower, out _, out _);
                    int costPer = Settings.CastleEliteManpowerCost;
                    int affordableFromManpower = costPer > 0 ? curManpower / costPer : toAdd;
                    toAdd = Math.Min(toAdd, affordableFromManpower);
                    if (toAdd <= 0) return;

                    // Consume manpower using the flat elite cost, aligned with affordability check.
                    manpowerBehavior.ConsumeManpowerFlat(settlement, toAdd * costPer);
                }
            }

            // Distribute toAdd troops randomly among the culture troop types.
            for (int i = 0; i < toAdd; i++)
            {
                var troop = cultureTroops[MBRandom.RandomInt(0, cultureTroops.Count)];
                string troopId = troop.StringId;
                if (poolDict.TryGetValue(troopId, out int cnt))
                    poolDict[troopId] = cnt + 1;
                else
                    poolDict[troopId] = 1;
            }
        }

        // ── 4. AI auto-recruitment ────────────────────────────────────────────────

        /// <summary>
        /// Recruits from BOTH the elite pool and converted prisoners into AI lord parties
        /// currently at this castle. Same-clan lords recruit for free; cross-clan lords
        /// pay gold per troop, which is credited to the castle owner.
        /// Non-hostile (same-faction or neutral) lords can recruit; hostile lords cannot.
        /// Prisoner recruitment costs zero manpower; elite recruitment costs manpower
        /// only when <see cref="B1071_McmSettings.CastleRecruitDrainsManpower"/> is on.
        ///
        /// FLICKERING FIX: MemberRoster.AddToCounts fires OnPartySizeChanged and bumps
        /// MobileParty.VersionNo, invalidating cached AI decisions. On the next tick,
        /// CheckExitingSettlementParallel checks ShortTermTargetSettlement ==
        /// CurrentSettlement — if the version bump causes re-evaluation, the party
        /// would exit and immediately return ("flickering"). After modifying the roster,
        /// we re-anchor the party via SetMoveGoToSettlement + RecalculateShortTermBehavior.
        /// </summary>
        private void AiAutoRecruit(Settlement settlement)
        {
            if (!Settings.CastleEliteAiRecruits) return;
            if (settlement.OwnerClan == null) return;

            var faction = settlement.OwnerClan.MapFaction;
            if (faction == null) return;

            string castleId = settlement.StringId;

            // Snapshot the party list to avoid collection-modification issues.
            var partiesSnapshot = settlement.Parties.ToList();

            foreach (MobileParty party in partiesSnapshot)
            {
                if (party == null || party == MobileParty.MainParty) continue;
                if (party.LeaderHero == null) continue;
                if (!party.IsLordParty) continue;
                if (party.MapFaction == null || FactionManager.IsAtWarAgainstFaction(party.MapFaction, faction)) continue;

                int partyLimit = party.Party.PartySizeLimit;
                int partySize = party.MemberRoster.TotalManCount;
                if (partySize >= partyLimit) continue;

                int room = partyLimit - partySize;
                int gold = party.LeaderHero.Gold;
                int totalRecruited = 0;
                bool isSameClan = (party.LeaderHero.Clan == settlement.OwnerClan);

                // ── Recruit from elite pool ──
                if (_elitePool.TryGetValue(castleId, out var poolDict) && poolDict.Count > 0)
                {
                    var entries = poolDict.ToList();
                    foreach (var entry in entries)
                    {
                        if (room <= 0 || (!isSameClan && gold <= 0)) break;

                        string troopId = entry.Key;
                        int available = entry.Value;
                        if (available <= 0) continue;

                        CharacterObject? troop = MBObjectManager.Instance.GetObject<CharacterObject>(troopId);
                        if (troop == null) continue;

                        int costPer = GetGoldCostForTier(troop.Tier);
                        int affordableByGold = isSameClan
                            ? available
                            : (costPer > 0 ? gold / costPer : available);
                        int take = Math.Min(available, Math.Min(room, affordableByGold));
                        if (take <= 0) continue;

                        // Manpower check for elite recruitment.
                        if (Settings.CastleRecruitDrainsManpower)
                        {
                            B1071_ManpowerBehavior? mp = B1071_ManpowerBehavior.Instance;
                            if (mp != null)
                            {
                                mp.GetManpowerPool(settlement, out int curMp, out _, out _);
                                int mpCostPer = Math.Max(1, Settings.BaseManpowerCostPerTroop);
                                int affordableByMp = mpCostPer > 0 ? curMp / mpCostPer : take;
                                take = Math.Min(take, affordableByMp);
                                if (take <= 0) continue;
                                mp.ConsumeManpowerFlat(settlement, take * mpCostPer);
                            }
                        }

                        party.MemberRoster.AddToCounts(troop, take);

                        // Gold: same-clan = free, cross-clan = pay castle owner.
                        if (!isSameClan)
                        {
                            int totalCost = costPer * take;
                            Hero? owner = settlement.Owner;
                            if (owner != null)
                                GiveGoldAction.ApplyBetweenCharacters(party.LeaderHero, owner, totalCost, disableNotification: true);
                            // If owner is null, skip — don't silently destroy gold.
                            gold -= totalCost;
                        }

                        room -= take;
                        poolDict[troopId] = available - take;
                        if (poolDict[troopId] <= 0) poolDict.Remove(troopId);
                        totalRecruited += take;
                    }
                }

                // ── Recruit from converted prisoners (ready for recruitment) ──
                var readyPrisoners = GetRecruitablePrisoners(settlement);
                if (readyPrisoners.Count > 0)
                {
                    TroopRoster? prisonRoster = settlement.Party?.PrisonRoster;
                    if (prisonRoster != null)
                    {
                        foreach (var (troop, count, _, prisonerGoldCost) in readyPrisoners)
                        {
                            if (room <= 0) break;

                            // Process one unit at a time — different depositors may have
                            // different effective costs (mixed FIFO entries), so bulk
                            // affordability checks based on PeekDepositor would be wrong.
                            int recruited = 0;
                            for (int unit = 0; unit < count && room > 0; unit++)
                            {
                                string? depositorId = PeekDepositor(castleId, troop.StringId);
                                int effectiveCost = GetEffectiveGoldCost(settlement, party.LeaderHero, depositorId, prisonerGoldCost);

                                if (effectiveCost > 0 && gold < effectiveCost) break;

                                // Prisoner recruitment costs zero manpower (by design).

                                party.MemberRoster.AddToCounts(troop, 1);
                                prisonRoster.RemoveTroop(troop, 1);

                                var depositorEntries = ConsumeDepositorEntries(castleId, troop.StringId, 1);
                                foreach (var (heroId, consumed) in depositorEntries)
                                    HandleRecruitmentGold(settlement, party.LeaderHero, heroId, prisonerGoldCost, consumed);

                                gold -= effectiveCost;
                                room--;
                                recruited++;
                            }

                            totalRecruited += recruited;

                            // Clean up prisoner day tracking for fully recruited entries.
                            int remaining = prisonRoster.GetTroopRoster()
                                .Where(e => e.Character == troop).Select(e => e.Number).FirstOrDefault();
                            if (remaining <= 0 && _prisonerDaysHeld.TryGetValue(castleId, out var castleDict))
                                castleDict.Remove(troop.StringId);
                        }
                    }
                }

                // Re-anchor to prevent flickering from version-bump.
                if (totalRecruited > 0 && party.CurrentSettlement == settlement)
                {
                    party.SetMoveGoToSettlement(settlement,
                        party.DesiredAiNavigationType,
                        party.IsTargetingPort);
                    party.RecalculateShortTermBehavior();
                }
            }
        }

        // ── 5. Garrison absorbs ready prisoners at auto-recruit rate ──────────────

        /// <summary>
        /// Transfers converted (ready) prisoners into the castle's garrison at the
        /// vanilla auto-recruit rate (1/day). Only fires if garrison auto-recruit is
        /// enabled, food is positive, and there's room in the garrison.
        /// Prisoner absorption costs zero manpower (prisoners, not fresh recruits).
        ///
        /// For cross-clan deposited prisoners, the castle owner pays the depositor
        /// their share from own gold. If the owner can't afford it, that prisoner
        /// is SKIPPED — the garrison must not receive free troops at the depositor's
        /// expense. Untracked or same-clan deposited prisoners are absorbed for free.
        /// </summary>
        private void GarrisonAbsorbPrisoners(Settlement settlement)
        {
            Town? town = settlement.Town;
            if (town == null) return;

            // Respect vanilla guard: auto-recruit must be enabled and food positive.
            if (!town.GarrisonAutoRecruitmentIsEnabled) return;
            if (town.FoodChange <= 0f) return;
            if (settlement.Party?.MapEvent != null || settlement.Party?.SiegeEvent != null) return;

            MobileParty? garrisonParty = town.GarrisonParty;

            var readyPrisoners = GetRecruitablePrisoners(settlement);
            if (readyPrisoners.Count == 0) return;

            // Prisoner absorption rate: 1 per day (same as vanilla auto-recruit base rate).
            // We intentionally bypass the SettlementGarrisonModel call because our
            // B1071_GarrisonAutoRecruitManpowerPatch postfix caps that result by current
            // manpower — but prisoner absorption costs zero manpower. The vanilla
            // DefaultSettlementGarrisonModel.GetMaximumDailyAutoRecruitmentCount always
            // returns 1 anyway, so hardcoding avoids the manpower gate entirely.
            int dailyCap = 1;
            if (dailyCap <= 0) return;

            // Respect garrison size limit.
            int garrisonSize = garrisonParty?.Party.NumberOfAllMembers ?? 0;
            int garrisonLimit = garrisonParty?.Party.PartySizeLimit
                ?? (int)TaleWorlds.CampaignSystem.Campaign.Current.Models.PartySizeLimitModel
                    .CalculateGarrisonPartySizeLimit(settlement).ResultNumber;
            int room = garrisonLimit - garrisonSize;
            if (room <= 0) return;

            int toAbsorb = Math.Min(dailyCap, room);
            int absorbed = 0;

            TroopRoster? prisonRoster = settlement.Party?.PrisonRoster;
            if (prisonRoster == null) return;

            string castleId = settlement.StringId;
            Hero? owner = settlement.Owner;

            foreach (var (troop, count, _, prisonerGoldCost) in readyPrisoners)
            {
                if (absorbed >= toAbsorb) break;

                // Process one unit at a time — each prisoner may have a different
                // depositor with different affordability for the castle owner.
                for (int unit = 0; unit < count && absorbed < toAbsorb; unit++)
                {
                    // Pre-check: can the castle owner afford this prisoner's depositor share?
                    string? depositorId = PeekDepositor(castleId, troop.StringId);
                    int ownerCost = GetGarrisonAbsorptionCost(settlement, depositorId, prisonerGoldCost);
                    if (ownerCost > 0 && (owner == null || owner.Gold < ownerCost))
                        break; // Owner can't afford — skip remaining prisoners of this type.

                    // Create garrison party if needed.
                    if (garrisonParty == null)
                    {
                        settlement.AddGarrisonParty();
                        garrisonParty = town.GarrisonParty;
                        if (garrisonParty == null) return;
                    }

                    garrisonParty.MemberRoster.AddToCounts(troop, 1);
                    prisonRoster.RemoveTroop(troop, 1);
                    absorbed++;

                    // Compensate cross-clan depositors: castle owner pays their share.
                    if (owner != null && prisonerGoldCost > 0)
                    {
                        var depositorEntries = ConsumeDepositorEntries(castleId, troop.StringId, 1);
                        foreach (var (heroId, consumed) in depositorEntries)
                        {
                            if (string.IsNullOrEmpty(heroId)) continue; // Untracked → no payment.

                            Hero? depositor = FindAliveHero(heroId);
                            if (depositor == null || depositor.Clan == settlement.OwnerClan) continue; // Same-clan → free.

                            float feePercent = Settings.CastleHoldingFeePercent / 100f;
                            int depositorShare = (int)(prisonerGoldCost * (1f - feePercent)) * consumed;
                            if (depositorShare > 0)
                            {
                                GiveGoldAction.ApplyBetweenCharacters(owner, depositor, depositorShare, disableNotification: true);
                                if (depositor == Hero.MainHero)
                                    InformationManager.DisplayMessage(new InformationMessage(
                                        $"\u2694\ufe0f Consignment: received {depositorShare}g from {settlement.Name} (garrison absorbed your prisoner, {100 - Settings.CastleHoldingFeePercent}% share).",
                                        new Color(0.3f, 0.7f, 0.9f)));
                            }
                        }
                    }
                }

                // Clean up prisoner day tracking for fully recruited entries.
                int remaining = prisonRoster.GetTroopRoster()
                    .Where(e => e.Character == troop).Select(e => e.Number).FirstOrDefault();
                if (remaining <= 0 && _prisonerDaysHeld.TryGetValue(castleId, out var castleDict))
                    castleDict.Remove(troop.StringId);
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  CULTURE TROOP HELPER
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns all T4-T6 troops from a culture's basic + elite troop trees.
        /// Cached per session for performance.
        /// </summary>
        public List<CharacterObject> GetCultureEliteTroops(CultureObject culture)
        {
            if (culture == null) return new List<CharacterObject>();

            string cultureId = culture.StringId;
            if (_cultureTroopCache.TryGetValue(cultureId, out var cached))
                return cached;

            var result = new HashSet<CharacterObject>();

            // Walk the basic (infantry/common) troop tree.
            if (culture.BasicTroop != null)
            {
                foreach (var troop in CharacterHelper.GetTroopTree(culture.BasicTroop, 4f))
                {
                    if (troop != null && troop.Tier >= 4 && troop.Tier <= 6 && !troop.IsHero)
                        result.Add(troop);
                }
            }

            // Walk the elite (noble) troop tree.
            if (culture.EliteBasicTroop != null)
            {
                foreach (var troop in CharacterHelper.GetTroopTree(culture.EliteBasicTroop, 4f))
                {
                    if (troop != null && troop.Tier >= 4 && troop.Tier <= 6 && !troop.IsHero)
                        result.Add(troop);
                }
            }

            var list = result.ToList();
            _cultureTroopCache[cultureId] = list;
            return list;
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  PUBLIC API — PRISONER CONVERSION
        // ══════════════════════════════════════════════════════════════════════════

        public int GetRequiredDaysForTier(int tier)
        {
            if (tier <= Settings.CastlePrisonerAutoEnslaveTierMax) return 0;
            if (tier == 4) return Settings.CastleRecruitT4Days;
            if (tier == 5) return Settings.CastleRecruitT5Days;
            return Settings.CastleRecruitT6Days;
        }

        public int GetGoldCostForTier(int tier)
        {
            if (tier == 4) return Settings.CastleRecruitGoldT4;
            if (tier == 5) return Settings.CastleRecruitGoldT5;
            return Settings.CastleRecruitGoldT6;
        }

        public int GetDaysHeld(string castleStringId, string troopStringId)
        {
            if (_prisonerDaysHeld.TryGetValue(castleStringId, out var castleDict))
                if (castleDict.TryGetValue(troopStringId, out int days))
                    return days;
            return 0;
        }

        public bool IsReadyForRecruitment(string castleStringId, CharacterObject troop)
        {
            if (troop == null) return false;
            int tier = troop.Tier;
            if (tier <= Settings.CastlePrisonerAutoEnslaveTierMax) return false;
            int required = GetRequiredDaysForTier(tier);
            int held = GetDaysHeld(castleStringId, troop.StringId);
            return held >= required;
        }

        /// <summary>Recruitable prisoners (conversion complete).</summary>
        public List<(CharacterObject Troop, int Count, int DaysHeld, int GoldCost)> GetRecruitablePrisoners(Settlement castle)
        {
            var result = new List<(CharacterObject, int, int, int)>();
            if (castle == null || !castle.IsCastle) return result;

            TroopRoster? prisonRoster = castle.Party?.PrisonRoster;
            if (prisonRoster == null) return result;

            string castleId = castle.StringId;
            foreach (var element in prisonRoster.GetTroopRoster())
            {
                if (element.Character == null || element.Character.IsHero || element.Number <= 0) continue;
                if (element.Character.Tier <= Settings.CastlePrisonerAutoEnslaveTierMax) continue;
                if (!IsReadyForRecruitment(castleId, element.Character)) continue;

                result.Add((element.Character, element.Number,
                    GetDaysHeld(castleId, element.Character.StringId),
                    GetGoldCostForTier(element.Character.Tier)));
            }
            return result;
        }

        /// <summary>Pending prisoners (still serving waiting period).</summary>
        public List<(CharacterObject Troop, int Count, int DaysHeld, int DaysRequired)> GetPendingPrisoners(Settlement castle)
        {
            var result = new List<(CharacterObject, int, int, int)>();
            if (castle == null || !castle.IsCastle) return result;

            TroopRoster? prisonRoster = castle.Party?.PrisonRoster;
            if (prisonRoster == null) return result;

            string castleId = castle.StringId;
            foreach (var element in prisonRoster.GetTroopRoster())
            {
                if (element.Character == null || element.Character.IsHero || element.Number <= 0) continue;
                if (element.Character.Tier <= Settings.CastlePrisonerAutoEnslaveTierMax) continue;
                if (IsReadyForRecruitment(castleId, element.Character)) continue;

                result.Add((element.Character, element.Number,
                    GetDaysHeld(castleId, element.Character.StringId),
                    GetRequiredDaysForTier(element.Character.Tier)));
            }
            return result;
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  PUBLIC API — ELITE POOL
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns all elite troops currently available in the castle's culture pool.
        /// Each entry: (CharacterObject, count, goldCost).
        /// </summary>
        public List<(CharacterObject Troop, int Count, int GoldCost)> GetElitePoolTroops(Settlement castle)
        {
            var result = new List<(CharacterObject, int, int)>();
            if (castle == null || !castle.IsCastle) return result;

            string castleId = castle.StringId;
            if (!_elitePool.TryGetValue(castleId, out var poolDict) || poolDict.Count == 0) return result;

            foreach (var entry in poolDict)
            {
                string troopId = entry.Key;
                int count = entry.Value;
                if (count <= 0) continue;
                CharacterObject? troop = MBObjectManager.Instance.GetObject<CharacterObject>(troopId);
                if (troop == null) continue;
                int goldCost = GetGoldCostForTier(troop.Tier);
                result.Add((troop, count, goldCost));
            }
            return result;
        }

        /// <summary>Total elite troops available at this castle.</summary>
        public int GetElitePoolTotal(Settlement castle)
        {
            if (castle == null) return 0;
            if (!_elitePool.TryGetValue(castle.StringId, out var poolDict)) return 0;
            return poolDict.Values.Sum();
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  PLAYER RECRUITMENT ACTIONS
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Recruit one prisoner from the castle. Removes from prison, adds to player party.
        /// Same-clan recruitment is free; outsiders pay gold to the castle owner.
        /// Prisoner recruitment costs zero manpower.
        /// </summary>
        public bool TryRecruitPrisoner(Settlement castle, CharacterObject troop)
        {
            if (castle == null || troop == null) return false;

            TroopRoster? prisonRoster = castle.Party?.PrisonRoster;
            if (prisonRoster == null) return false;

            int inPrison = prisonRoster.GetTroopRoster()
                .Where(e => e.Character == troop).Select(e => e.Number).FirstOrDefault();
            if (inPrison <= 0) return false;
            if (!IsReadyForRecruitment(castle.StringId, troop)) return false;

            string castleId = castle.StringId;
            int baseCost = GetGoldCostForTier(troop.Tier);

            // 3-party clan-waiver affordability check.
            string? depositorId = PeekDepositor(castleId, troop.StringId);
            int effectiveCost = GetEffectiveGoldCost(castle, Hero.MainHero, depositorId, baseCost);
            if (effectiveCost > 0 && Hero.MainHero.Gold < effectiveCost) return false;

            // Prisoner recruitment costs zero manpower (by design).

            // Consume depositor entry and distribute gold.
            var depositorEntries = ConsumeDepositorEntries(castleId, troop.StringId, 1);
            foreach (var (heroId, consumed) in depositorEntries)
                HandleRecruitmentGold(castle, Hero.MainHero, heroId, baseCost, consumed);

            prisonRoster.RemoveTroop(troop, 1);
            MobileParty.MainParty.MemberRoster.AddToCounts(troop, 1);

            if (inPrison <= 1 && _prisonerDaysHeld.TryGetValue(castleId, out var castleDict))
                castleDict.Remove(troop.StringId);

            ShowRecruitMessage(troop, castle, effectiveCost, "Prisoner");
            return true;
        }

        /// <summary>
        /// Recruit one elite troop from the castle's culture pool.
        /// Same-clan recruitment is free; outsiders pay gold to the castle owner.
        /// Drains manpower if configured.
        /// </summary>
        public bool TryRecruitElite(Settlement castle, CharacterObject troop)
        {
            if (castle == null || troop == null) return false;

            string castleId = castle.StringId;
            if (!_elitePool.TryGetValue(castleId, out var poolDict)) return false;

            string troopId = troop.StringId;
            if (!poolDict.TryGetValue(troopId, out int available) || available <= 0) return false;

            int goldCost = GetGoldCostForTier(troop.Tier);
            bool isSameClan = (Clan.PlayerClan == castle.OwnerClan);

            // Same-clan lords recruit for free; outsiders must afford the cost.
            if (!isSameClan && Hero.MainHero.Gold < goldCost) return false;

            if (Settings.CastleRecruitDrainsManpower && !CheckManpower(castle, troop)) return false;

            // Execute — gold goes to the castle owner via direct transfer.
            if (!isSameClan)
            {
                Hero? owner = castle.Owner;
                if (owner != null)
                    GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, owner, goldCost, disableNotification: true);
                // If owner is null (shouldn't happen), skip — don't silently destroy gold.
            }
            poolDict[troopId] = available - 1;
            if (poolDict[troopId] <= 0) poolDict.Remove(troopId);
            MobileParty.MainParty.MemberRoster.AddToCounts(troop, 1);

            if (Settings.CastleRecruitDrainsManpower)
                B1071_ManpowerBehavior.Instance?.ConsumeManpowerPublic(castle, troop, 1);

            ShowRecruitMessage(troop, castle, isSameClan ? 0 : goldCost, "Elite");
            return true;
        }

        private bool CheckManpower(Settlement castle, CharacterObject troop)
        {
            B1071_ManpowerBehavior? manpowerBehavior = B1071_ManpowerBehavior.Instance;
            if (manpowerBehavior == null) return true;

            if (!manpowerBehavior.CanRecruitCountForPlayer(
                    castle, MobileParty.MainParty, troop, 1,
                    out int available, out int costPer, out Settlement? pool))
            {
                string poolName = pool?.Name?.ToString() ?? "castle";
                InformationManager.DisplayMessage(new InformationMessage(
                    $"Manpower: cannot recruit {troop.Name} \u2014 {poolName} needs {costPer}, only {available} left.",
                    Colors.Yellow));
                return false;
            }
            return true;
        }

        private void ShowRecruitMessage(CharacterObject troop, Settlement castle, int goldCost, string source)
        {
            if (Settings.ShowPlayerDebugMessages)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"\ud83c\udff0 [{source}] Recruited {troop.Name} at {castle.Name} for {goldCost}g",
                    new Color(0.2f, 0.8f, 0.4f)));
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  ACCESS CHECK
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Whether the player can recruit from this castle.
        /// Must not be at war with the castle's owner.
        /// </summary>
        public static bool CanPlayerAccessCastle(Settlement castle)
        {
            if (castle == null || !castle.IsCastle) return false;

            // Player's own castle — always allowed.
            if (castle.OwnerClan == Clan.PlayerClan) return true;

            IFaction? playerFaction = Clan.PlayerClan?.MapFaction;
            IFaction? castleFaction = castle.OwnerClan?.MapFaction;
            if (playerFaction == null || castleFaction == null) return false;

            // Hostile — not allowed.
            if (FactionManager.IsAtWarAgainstFaction(playerFaction, castleFaction))
                return false;

            // Neutral or allied — allowed.
            return true;
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  CASTLE GAME MENU
        // ══════════════════════════════════════════════════════════════════════════

        private void RegisterMenus(CampaignGameStarter starter)
        {
            starter.AddGameMenuOption(
                "castle",
                "b1071_castle_recruit",
                "{B1071_CASTLE_RECRUIT_TEXT}",
                CastleRecruitCondition,
                CastleRecruitConsequence,
                isLeave: false,
                index: 3);

            // Player deposit at neutral castles — vanilla's "Donate prisoners" only
            // appears at same-faction castles. This extends coverage to neutral ones.
            starter.AddGameMenuOption(
                "castle_dungeon",
                "b1071_castle_deposit_prisoners",
                "{B1071_CASTLE_DEPOSIT_TEXT}",
                CastleDepositPrisonersCondition,
                CastleDepositPrisonersConsequence,
                isLeave: false,
                index: 2);
        }

        private bool CastleRecruitCondition(MenuCallbackArgs args)
        {
            if (!Settings.EnableCastleRecruitment) return false;

            Settlement? s = Settlement.CurrentSettlement;
            if (s == null || !s.IsCastle) return false;

            // Hostile — hide completely.
            if (!CanPlayerAccessCastle(s)) return false;

            // Count available troops.
            int eliteCount = GetElitePoolTotal(s);
            var recruitable = GetRecruitablePrisoners(s);
            var pending = GetPendingPrisoners(s);

            int readyPrisoners = recruitable.Sum(r => r.Count);
            int pendingPrisoners = pending.Sum(p => p.Count);
            int totalReady = eliteCount + readyPrisoners;

            args.optionLeaveType = GameMenuOption.LeaveType.Recruit;
            args.IsEnabled = true;

            string label;
            if (totalReady > 0 && pendingPrisoners > 0)
                label = $"\ud83c\udff0 Recruit troops ({totalReady} available, {pendingPrisoners} pending)";
            else if (totalReady > 0)
                label = $"\ud83c\udff0 Recruit troops ({totalReady} available)";
            else if (pendingPrisoners > 0)
                label = $"\ud83c\udff0 Recruit troops ({pendingPrisoners} pending)";
            else
                label = "\ud83c\udff0 Recruit troops (none available)";

            TaleWorlds.Localization.MBTextManager.SetTextVariable("B1071_CASTLE_RECRUIT_TEXT", label);

            // Always show at non-hostile castles, but disable if nothing available.
            if (totalReady == 0 && pendingPrisoners == 0)
            {
                args.IsEnabled = false;
                args.Tooltip = new TaleWorlds.Localization.TextObject("No troops or prisoners available yet. Elite troops regenerate daily from the castle's manpower pool.");
            }

            return true;
        }

        private void CastleRecruitConsequence(MenuCallbackArgs args)
        {
            Settlement? castle = Settlement.CurrentSettlement;
            if (castle == null) return;
            Byzantium1071.Campaign.UI.B1071_CastleRecruitmentScreen.OpenScreen(castle);
        }

        // ── Player deposit at neutral castles ─────────────────────────────────────

        /// <summary>
        /// Shows "Deposit prisoners" at neutral castles where vanilla's "Donate prisoners"
        /// option does not appear (vanilla requires same-faction). Uses the same vanilla
        /// party screen; our <see cref="OnPrisonerDonatedToSettlement"/> hook records
        /// depositor tracking automatically.
        /// </summary>
        private bool CastleDepositPrisonersCondition(MenuCallbackArgs args)
        {
            if (!Settings.EnableCastleRecruitment) return false;

            Settlement? s = Settlement.CurrentSettlement;
            if (s == null || !s.IsCastle) return false;

            // Own castle — vanilla's "Manage prisoners" handles this.
            if (s.OwnerClan == Clan.PlayerClan) return false;

            IFaction? playerFaction = Clan.PlayerClan?.MapFaction;
            IFaction? castleFaction = s.OwnerClan?.MapFaction;
            if (playerFaction == null || castleFaction == null) return false;

            // Same faction — vanilla's "Donate prisoners" already handles this.
            if (playerFaction == castleFaction) return false;

            // Hostile — not allowed.
            if (FactionManager.IsAtWarAgainstFaction(playerFaction, castleFaction)) return false;

            // Neutral castle — show our option.
            int prisonerCount = MobileParty.MainParty.PrisonRoster?.TotalRegulars ?? 0;
            if (prisonerCount <= 0)
            {
                args.IsEnabled = false;
                args.Tooltip = new TaleWorlds.Localization.TextObject("You have no prisoners to deposit.");
            }
            else
            {
                args.IsEnabled = true;
            }

            // Check prison capacity.
            int prisonCap = s.Party?.PrisonerSizeLimit ?? 0;
            int prisonOccupied = s.Party?.NumberOfPrisoners ?? 0;
            if (prisonCap > 0 && prisonOccupied >= prisonCap)
            {
                args.IsEnabled = false;
                args.Tooltip = new TaleWorlds.Localization.TextObject("The castle's prison is full.");
            }

            args.optionLeaveType = GameMenuOption.LeaveType.Submenu;

            int feePercent = Settings.CastleHoldingFeePercent;
            string label = $"\u2694\ufe0f Deposit prisoners ({prisonerCount} available, {feePercent}% holding fee)";
            TaleWorlds.Localization.MBTextManager.SetTextVariable("B1071_CASTLE_DEPOSIT_TEXT", label);

            return true;
        }

        private void CastleDepositPrisonersConsequence(MenuCallbackArgs args)
        {
            // Vanilla API — opens the party screen for prisoner donation.
            // The done handler fires OnPrisonerDonatedToSettlement, which our hook records.
            PartyScreenHelper.OpenScreenAsDonatePrisoners();
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Fires when any party (typically the player) donates prisoners via vanilla's
        /// party screen ("Donate prisoners" in the dungeon menu).
        /// Registers depositor tracking so the consignment model applies to player deposits.
        /// </summary>
        private void OnPrisonerDonatedToSettlement(
            MobileParty donatingParty, FlattenedTroopRoster donatedPrisoners, Settlement settlement)
        {
            if (!Settings.EnableCastleRecruitment) return;
            if (settlement == null || !settlement.IsCastle) return;
            if (donatedPrisoners == null) return;

            // Skip own-clan castles — no economic difference for same-clan deposits.
            Hero? depositorHero = donatingParty?.LeaderHero;
            if (depositorHero == null) return;
            if (depositorHero.Clan == settlement.OwnerClan) return;

            string castleId = settlement.StringId;
            string heroId = depositorHero.StringId;

            // Group the flattened roster (one entry per soldier) by troop type.
            var grouped = new Dictionary<string, (CharacterObject Troop, int Count)>();
            foreach (FlattenedTroopRosterElement element in donatedPrisoners)
            {
                if (element.Troop == null || element.Troop.IsHero) continue;
                string troopId = element.Troop.StringId;
                if (grouped.TryGetValue(troopId, out var existing))
                    grouped[troopId] = (existing.Troop, existing.Count + 1);
                else
                    grouped[troopId] = (element.Troop, 1);
            }

            int totalDeposited = 0;
            foreach (var kvp in grouped)
            {
                RecordDeposit(castleId, heroId, kvp.Key, kvp.Value.Count);
                totalDeposited += kvp.Value.Count;
            }

            // Show consignment notification to the player.
            if (totalDeposited > 0 && donatingParty != null && donatingParty.IsMainParty)
            {
                int feePercent = Settings.CastleHoldingFeePercent;
                int depositorPercent = 100 - feePercent;
                InformationManager.DisplayMessage(new InformationMessage(
                    $"\u2694\ufe0f Deposited {totalDeposited} prisoner{(totalDeposited > 1 ? "s" : "")} at {settlement.Name}. " +
                    $"You receive {depositorPercent}% of processing income (holding fee: {feePercent}%).",
                    new Color(0.3f, 0.7f, 0.9f)));
            }
        }

        private static Settlement? FindNearestTown(Settlement origin)
        {
            Settlement? nearest = null;
            float bestDist = float.MaxValue;
            foreach (Town town in Town.AllTowns)
            {
                if (town.Settlement == null) continue;
                float dist = (origin.GetPosition2D - town.Settlement.GetPosition2D).LengthSquared;
                if (dist < bestDist) { bestDist = dist; nearest = town.Settlement; }
            }
            return nearest;
        }

        private void CleanupStalePrisonerEntries(string castleId, TroopRoster prisonRoster)
        {
            if (!_prisonerDaysHeld.TryGetValue(castleId, out var castleDict)) return;

            var currentTroopIds = new HashSet<string>();
            foreach (var element in prisonRoster.GetTroopRoster())
                if (element.Character != null && element.Number > 0)
                    currentTroopIds.Add(element.Character.StringId);

            var staleKeys = castleDict.Keys.Where(k => !currentTroopIds.Contains(k)).ToList();
            foreach (var key in staleKeys) castleDict.Remove(key);
            if (castleDict.Count == 0) _prisonerDaysHeld.Remove(castleId);

            // Also clean depositor tracking for troops no longer in the roster.
            if (_depositorTracking.TryGetValue(castleId, out var depDict))
            {
                var staleDepKeys = depDict.Keys.Where(k => !currentTroopIds.Contains(k)).ToList();
                foreach (var key in staleDepKeys) depDict.Remove(key);
                if (depDict.Count == 0) _depositorTracking.Remove(castleId);
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  DEPOSITOR TRACKING & INCOME SPLIT (CONSIGNMENT MODEL)
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Records that a hero deposited prisoners of a specific troop type at a castle.
        /// Called from <see cref="Patches.B1071_CastlePrisonerDepositPatch"/> and (future)
        /// player deposit game menu.
        /// </summary>
        public void RecordDeposit(string castleId, string heroStringId, string troopStringId, int count)
        {
            if (string.IsNullOrEmpty(castleId) || string.IsNullOrEmpty(heroStringId)
                || string.IsNullOrEmpty(troopStringId) || count <= 0) return;

            if (!_depositorTracking.TryGetValue(castleId, out var troopDict))
            {
                troopDict = new Dictionary<string, List<(string, int)>>();
                _depositorTracking[castleId] = troopDict;
            }
            if (!troopDict.TryGetValue(troopStringId, out var heroList))
            {
                heroList = new List<(string, int)>();
                troopDict[troopStringId] = heroList;
            }

            // Always append — never consolidate — to preserve strict FIFO ordering
            // when the same hero deposits the same troop type at the same castle
            // interleaved with deposits from other heroes.
            heroList.Add((heroStringId, count));
        }

        /// <summary>
        /// Consumes 'count' prisoners from depositor tracking for a specific castle+troop.
        /// Returns a list of (heroStringId, consumed) pairs in FIFO order.
        /// If tracked entries are exhausted but count remains (siege-conquest prisoners),
        /// the remainder has no depositor → castle owner gets 100%.
        /// </summary>
        private List<(string? HeroId, int Consumed)> ConsumeDepositorEntries(
            string castleId, string troopStringId, int count)
        {
            var result = new List<(string?, int)>();
            if (count <= 0) return result;

            if (!_depositorTracking.TryGetValue(castleId, out var troopDict)
                || !troopDict.TryGetValue(troopStringId, out var heroList)
                || heroList.Count == 0)
            {
                // No depositor info — all untracked (siege conquest or pre-tracking).
                result.Add((null, count));
                return result;
            }

            int remaining = count;
            while (remaining > 0 && heroList.Count > 0)
            {
                var (heroId, available) = heroList[0];
                int take = Math.Min(remaining, available);
                result.Add((heroId, take));
                remaining -= take;

                if (take >= available)
                    heroList.RemoveAt(0);
                else
                    heroList[0] = (heroId, available - take);
            }

            if (remaining > 0)
                result.Add((null, remaining)); // Untracked remainder.

            // Clean up empty lists.
            if (heroList.Count == 0) troopDict.Remove(troopStringId);
            if (troopDict.Count == 0) _depositorTracking.Remove(castleId);

            return result;
        }

        /// <summary>
        /// Distributes income from prisoner processing (enslavement) between
        /// the depositor and the castle owner based on the holding fee.
        ///
        /// - If depositor is same-clan as castle owner: owner gets 100% (family).
        /// - If depositor is null (untracked): owner gets 100%.
        /// - If depositor is dead/unavailable: owner gets 100%.
        /// - Otherwise: depositor gets (100% - fee), owner gets fee%.
        ///
        /// When <paramref name="payingTown"/> is set, the gold is transferred from
        /// the town's treasury via <c>GiveGoldAction.ApplyForSettlementToCharacter</c>
        /// (properly deducts from <c>Town.Gold</c>, clamped, fires campaign events).
        /// </summary>
        private void DistributeIncome(Settlement castle, string? depositorHeroId, int totalIncome,
            Settlement? payingTown = null)
        {
            if (totalIncome <= 0 || castle == null) return;

            Hero? owner = castle.Owner;
            if (owner == null) return;

            // Untracked prisoner → all to owner.
            if (string.IsNullOrEmpty(depositorHeroId))
            {
                PayHero(payingTown, owner, totalIncome);
                return;
            }

            Hero? depositor = FindAliveHero(depositorHeroId);
            if (depositor == null)
            {
                // Depositor dead/gone → fallback to owner.
                PayHero(payingTown, owner, totalIncome);
                return;
            }

            // Same-clan → owner gets 100% (family).
            if (depositor.Clan == castle.OwnerClan)
            {
                PayHero(payingTown, owner, totalIncome);
                return;
            }

            // Cross-clan split.
            float feePercent = Settings.CastleHoldingFeePercent / 100f;
            int ownerShare = (int)(totalIncome * feePercent);
            int depositorShare = totalIncome - ownerShare;

            if (depositorShare > 0)
            {
                PayHero(payingTown, depositor, depositorShare);
                if (depositor == Hero.MainHero)
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"\u2694\ufe0f Consignment: received {depositorShare}g from {castle.Name} (enslavement income, {100 - Settings.CastleHoldingFeePercent}% share).",
                        new Color(0.3f, 0.7f, 0.9f)));
            }
            if (ownerShare > 0)
                PayHero(payingTown, owner, ownerShare);
        }

        /// <summary>
        /// Pays a hero from either a town's treasury or from nothing (gold creation).
        /// When <paramref name="payingTown"/> is set, uses the vanilla
        /// <c>GiveGoldAction.ApplyForSettlementToCharacter</c> API which deducts from
        /// the town's <c>SettlementComponent.Gold</c> with proper clamping and events.
        /// When null, gold is created (used only as fallback — all current callers
        /// should provide a payingTown).
        /// </summary>
        private static void PayHero(Settlement? payingTown, Hero recipient, int amount)
        {
            if (amount <= 0 || recipient == null) return;

            if (payingTown != null)
                GiveGoldAction.ApplyForSettlementToCharacter(payingTown, recipient, amount, disableNotification: true);
            else
                GiveGoldAction.ApplyBetweenCharacters(null, recipient, amount, disableNotification: true);
        }

        /// <summary>
        /// Calculates and distributes gold when a prisoner is recruited (by player, AI, or garrison).
        /// Handles the 3-party clan-waiver model:
        /// - Recruiter same-clan as depositor → depositor's share waived
        /// - Recruiter same-clan as castle owner → owner's share waived
        /// - Both → free
        /// - Untracked prisoners → current behavior (recruiter-owner relationship only)
        /// </summary>
        private void HandleRecruitmentGold(
            Settlement castle, Hero? recruiterHero,
            string? depositorHeroId, int goldCostPerTroop, int count)
        {
            if (goldCostPerTroop <= 0 || count <= 0 || castle == null) return;

            Hero? owner = castle.Owner;
            Hero? depositor = FindAliveHero(depositorHeroId);

            // Determine clan relationships.
            Clan? recruiterClan = recruiterHero?.Clan;
            bool recruiterIsSameClanAsOwner = (recruiterClan != null && recruiterClan == castle.OwnerClan);
            bool recruiterIsSameClanAsDepositor = (depositor != null && recruiterClan != null
                && recruiterClan == depositor.Clan);

            // For untracked prisoners, depositor = owner effectively.
            if (depositor == null || depositor.Clan == castle.OwnerClan)
            {
                // Simple 2-party: recruiter vs. owner.
                if (recruiterIsSameClanAsOwner)
                    return; // Free — family.

                int totalCost = goldCostPerTroop * count;
                if (recruiterHero != null && owner != null)
                    GiveGoldAction.ApplyBetweenCharacters(recruiterHero, owner, totalCost, disableNotification: true);
                // If owner is null, skip — don't silently destroy gold.
                return;
            }

            // 3-party split: depositor, owner, recruiter all potentially different clans.
            float feePercent = Settings.CastleHoldingFeePercent / 100f;
            int totalGold = goldCostPerTroop * count;
            int ownerShareAmount = (int)(totalGold * feePercent);
            int depositorShareAmount = totalGold - ownerShareAmount;

            // Transfer gold via direct hero-to-hero transfers (GiveGoldAction
            // clamps to available gold — safe, no inflation, proper campaign events).

            // Pay depositor their share (waived if recruiter is family of depositor).
            if (!recruiterIsSameClanAsDepositor && depositorShareAmount > 0
                && recruiterHero != null)
            {
                GiveGoldAction.ApplyBetweenCharacters(recruiterHero, depositor, depositorShareAmount, disableNotification: true);
                if (depositor == Hero.MainHero)
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"\u2694\ufe0f Consignment: received {depositorShareAmount}g from {castle.Name} (recruitment income, {100 - Settings.CastleHoldingFeePercent}% share).",
                        new Color(0.3f, 0.7f, 0.9f)));
            }

            // Pay owner their share (waived if recruiter is family of owner).
            if (!recruiterIsSameClanAsOwner && ownerShareAmount > 0
                && recruiterHero != null && owner != null)
                GiveGoldAction.ApplyBetweenCharacters(recruiterHero, owner, ownerShareAmount, disableNotification: true);
        }

        /// <summary>
        /// Calculates effective gold cost for a recruiter considering clan waivers.
        /// Used for affordability checks before actual recruitment.
        /// </summary>
        private int GetEffectiveGoldCost(
            Settlement castle, Hero? recruiterHero,
            string? depositorHeroId, int goldCostPerTroop)
        {
            if (goldCostPerTroop <= 0) return 0;

            Hero? owner = castle?.Owner;
            Hero? depositor = FindAliveHero(depositorHeroId);

            Clan? recruiterClan = recruiterHero?.Clan;
            bool recruiterIsSameClanAsOwner = (recruiterClan != null && recruiterClan == castle?.OwnerClan);
            bool recruiterIsSameClanAsDepositor = (depositor != null && recruiterClan != null
                && recruiterClan == depositor.Clan);

            // Untracked or same-clan-as-owner depositor → 2-party rule.
            if (depositor == null || depositor.Clan == castle?.OwnerClan)
                return recruiterIsSameClanAsOwner ? 0 : goldCostPerTroop;

            // 3-party.
            float feePercent = Settings.CastleHoldingFeePercent / 100f;
            int ownerShare = (int)(goldCostPerTroop * feePercent);
            int depositorShare = goldCostPerTroop - ownerShare;

            int cost = 0;
            if (!recruiterIsSameClanAsDepositor) cost += depositorShare;
            if (!recruiterIsSameClanAsOwner) cost += ownerShare;
            return cost;
        }

        /// <summary>
        /// Calculates how much gold the castle owner must pay when absorbing a prisoner
        /// into the garrison. Returns 0 if the prisoner is untracked, depositor is dead,
        /// or depositor is same-clan as the castle owner (family — free).
        /// </summary>
        private int GetGarrisonAbsorptionCost(Settlement castle, string? depositorHeroId, int goldCostPerTroop)
        {
            if (goldCostPerTroop <= 0 || castle == null) return 0;
            if (string.IsNullOrEmpty(depositorHeroId)) return 0; // Untracked → free.

            Hero? depositor = FindAliveHero(depositorHeroId);
            if (depositor == null) return 0; // Dead depositor → free.
            if (depositor.Clan == castle.OwnerClan) return 0; // Family → free.

            float feePercent = Settings.CastleHoldingFeePercent / 100f;
            return (int)(goldCostPerTroop * (1f - feePercent));
        }

        /// <summary>
        /// Returns the effective gold cost the player would pay to recruit one unit of
        /// the given prisoner troop from this castle, accounting for clan waivers.
        /// Used by the castle recruitment UI to correctly enable/disable the recruit button.
        /// </summary>
        public int GetPlayerEffectivePrisonerCost(Settlement castle, CharacterObject troop)
        {
            if (castle == null || troop == null) return 0;
            int baseCost = GetGoldCostForTier(troop.Tier);
            string? depositorId = PeekDepositor(castle.StringId, troop.StringId);
            return GetEffectiveGoldCost(castle, Hero.MainHero, depositorId, baseCost);
        }

        /// <summary>
        /// Gets the depositor hero ID for the first tracked entry of a troop at a castle.
        /// Returns null if untracked (siege conquest, pre-tracking save).
        /// Does NOT consume entries — use <see cref="ConsumeDepositorEntries"/> for that.
        /// </summary>
        private string? PeekDepositor(string castleId, string troopStringId)
        {
            if (_depositorTracking.TryGetValue(castleId, out var troopDict)
                && troopDict.TryGetValue(troopStringId, out var heroList)
                && heroList.Count > 0)
            {
                return heroList[0].HeroId;
            }
            return null;
        }

        /// <summary>
        /// Finds an alive hero by their StringId. Returns null if dead or not found.
        /// </summary>
        private static Hero? FindAliveHero(string? heroStringId)
        {
            if (string.IsNullOrEmpty(heroStringId)) return null;
            foreach (Hero h in Hero.AllAliveHeroes)
                if (h.StringId == heroStringId) return h;
            return null;
        }
    }
}
