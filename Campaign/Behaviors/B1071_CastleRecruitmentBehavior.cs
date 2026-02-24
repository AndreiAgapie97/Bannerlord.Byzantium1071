using Byzantium1071.Campaign.Settings;
using Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
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
    ///   AI lords recruit from their own faction's castles only.
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

        // ── CampaignBehaviorBase ──────────────────────────────────────────────────

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailyTickSettlement);
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

            // 4. AI auto-recruitment from elite pool
            AiAutoRecruit(settlement);
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

            int totalEnslaved = 0;
            foreach (var element in toEnslave)
            {
                int count = element.Number;
                prisonRoster.RemoveTroop(element.Character, count);
                nearestTown.ItemRoster.AddToCounts(_slaveItem, count);
                totalEnslaved += count;
            }

            // No per-tick log — enslaved count is visible in the recruitment screen.
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
        /// Recruits from the elite pool into AI lord parties currently at this castle.
        ///
        /// IMPORTANT: MemberRoster.AddToCounts fires CampaignEventDispatcher.OnPartySizeChanged
        /// and bumps MobileParty.VersionNo, which invalidates cached AI decisions. On the very
        /// next tick, CheckExitingSettlementParallel checks ShortTermTargetSettlement ==
        /// CurrentSettlement — if the version bump caused the AI to re-evaluate and pick a new
        /// target, the party would exit the castle and immediately turn back (visual "flickering").
        ///
        /// Fix: after modifying the roster, re-anchor the party to this settlement via
        /// SetMoveGoToSettlement + RecalculateShortTermBehavior so that ShortTermTargetSettlement
        /// remains equal to CurrentSettlement through the next tick.
        /// </summary>
        private void AiAutoRecruit(Settlement settlement)
        {
            if (!Settings.CastleEliteAiRecruits) return;
            if (settlement.OwnerClan == null) return;

            var faction = settlement.OwnerClan.MapFaction;
            if (faction == null) return;

            foreach (MobileParty party in settlement.Parties)
            {
                if (party == null || party == MobileParty.MainParty) continue;
                if (party.LeaderHero == null) continue;
                if (!party.IsLordParty) continue;
                if (party.MapFaction != faction) continue;

                int partyLimit = party.Party.PartySizeLimit;
                int partySize = party.MemberRoster.TotalManCount;
                if (partySize >= (int)(partyLimit * 0.9f)) continue;

                int budget = Math.Min(Settings.CastleEliteAiMaxPerDay, partyLimit - partySize);
                int recruited = 0;

                string castleId = settlement.StringId;
                if (!_elitePool.TryGetValue(castleId, out var poolDict) || poolDict.Count == 0) continue;

                var entries = poolDict.ToList();
                foreach (var entry in entries)
                {
                    string troopId = entry.Key;
                    int count = entry.Value;
                    if (recruited >= budget) break;
                    if (count <= 0) continue;

                    CharacterObject? troop = MBObjectManager.Instance.GetObject<CharacterObject>(troopId);
                    if (troop == null) continue;

                    int take = Math.Min(count, budget - recruited);
                    party.MemberRoster.AddToCounts(troop, take);
                    poolDict[troopId] = count - take;
                    if (poolDict[troopId] <= 0) poolDict.Remove(troopId);
                    recruited += take;
                }

                // Re-anchor the party to this castle so the AI version-bump doesn't
                // cause CheckExitingSettlementParallel to eject the party next tick.
                if (recruited > 0 && party.CurrentSettlement == settlement)
                {
                    party.SetMoveGoToSettlement(settlement,
                        party.DesiredAiNavigationType,
                        party.IsTargetingPort);
                    party.RecalculateShortTermBehavior();
                }
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
        /// Recruit one prisoner from the castle. Removes from prison, adds to player party,
        /// deducts gold. Prisoner recruitment costs zero manpower.
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

            int goldCost = GetGoldCostForTier(troop.Tier);
            if (Hero.MainHero.Gold < goldCost) return false;

            // Prisoner recruitment costs zero manpower (by design).

            // Execute
            Hero.MainHero.ChangeHeroGold(-goldCost);
            prisonRoster.RemoveTroop(troop, 1);
            MobileParty.MainParty.MemberRoster.AddToCounts(troop, 1);

            if (inPrison <= 1 && _prisonerDaysHeld.TryGetValue(castle.StringId, out var castleDict))
                castleDict.Remove(troop.StringId);

            ShowRecruitMessage(troop, castle, goldCost, "Prisoner");
            return true;
        }

        /// <summary>
        /// Recruit one elite troop from the castle's culture pool.
        /// Removes from pool, adds to player party, deducts gold, drains manpower if configured.
        /// </summary>
        public bool TryRecruitElite(Settlement castle, CharacterObject troop)
        {
            if (castle == null || troop == null) return false;

            string castleId = castle.StringId;
            if (!_elitePool.TryGetValue(castleId, out var poolDict)) return false;

            string troopId = troop.StringId;
            if (!poolDict.TryGetValue(troopId, out int available) || available <= 0) return false;

            int goldCost = GetGoldCostForTier(troop.Tier);
            if (Hero.MainHero.Gold < goldCost) return false;

            if (Settings.CastleRecruitDrainsManpower && !CheckManpower(castle, troop)) return false;

            // Execute
            Hero.MainHero.ChangeHeroGold(-goldCost);
            poolDict[troopId] = available - 1;
            if (poolDict[troopId] <= 0) poolDict.Remove(troopId);
            MobileParty.MainParty.MemberRoster.AddToCounts(troop, 1);

            if (Settings.CastleRecruitDrainsManpower)
                B1071_ManpowerBehavior.Instance?.ConsumeManpowerPublic(castle, troop, 1);

            ShowRecruitMessage(troop, castle, goldCost, "Elite");
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

        // ══════════════════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════════════════

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
        }
    }
}
