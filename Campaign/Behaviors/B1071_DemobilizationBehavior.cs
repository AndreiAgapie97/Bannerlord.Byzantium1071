using Byzantium1071.Campaign.Settings;
using Byzantium1071.Campaign.UI;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace Byzantium1071.Campaign.Behaviors
{
    /// <summary>
    /// Party-scoped service rotation for field troops.
    /// Tracks FIFO service entries per individual soldier, warns the player before
    /// main-party soldiers leave, and lets the player pay to extend selected entries.
    /// </summary>
    public sealed class B1071_DemobilizationBehavior : CampaignBehaviorBase
    {
        public static B1071_DemobilizationBehavior? Instance { get; internal set; }

        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        private const string LogTag = "Demobilization";

        private const int MinimumTransferReserveDays = 14;

        private sealed class CohortEntry
        {
            public int JoinDay;
            public int Count;
            public bool HasBeenExtended;
        }

        private sealed class TransferReserveEntry
        {
            public int JoinDay;
            public int StoredDay;
            public int Count;
            public bool HasBeenExtended;
        }

        private sealed class OverdueCandidate
        {
            public string TroopId = string.Empty;
            public CharacterObject Troop = null!;
            public CohortEntry Cohort = null!;
            public int JoinDay;
            public int ThresholdDays;
        }

        private sealed class AiExtensionCandidate
        {
            public string TroopId = string.Empty;
            public CharacterObject Troop = null!;
            public CohortEntry Cohort = null!;
            public int JoinDay;
            public int RemainingDays;
            public int Cost;
        }

        public sealed class CohortView
        {
            public string PartyId = string.Empty;
            public string TroopId = string.Empty;
            public int CohortIndex;
            public CharacterObject Troop = null!;
            public int Count;
            public int JoinDay;
            public int AgeDays;
            public int ThresholdDays;
            public int RemainingDays;
            public int ExtendCost;
            public bool IsWarning;
            public bool IsOverdue;
            public bool HasBeenExtended;
            public bool CanExtend;
        }

        private readonly Dictionary<string, Dictionary<string, List<CohortEntry>>> _serviceCohorts
            = new Dictionary<string, Dictionary<string, List<CohortEntry>>>();

        private readonly Dictionary<string, List<string>> _upgradePathCache
            = new Dictionary<string, List<string>>();

        private readonly Dictionary<string, List<TransferReserveEntry>> _transferReserve
            = new Dictionary<string, List<TransferReserveEntry>>();

        private List<string>? _savedPartyIds;
        private List<string>? _savedTroopIds;
        private List<int>? _savedJoinDays;
        private List<int>? _savedCounts;
        private List<bool>? _savedExtendedFlags;

        private List<string>? _savedReserveTroopIds;
        private List<int>? _savedReserveJoinDays;
        private List<int>? _savedReserveStoredDays;
        private List<int>? _savedReserveCounts;
        private List<bool>? _savedReserveExtendedFlags;

        private int _lastWarningDay = -1;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.OnTroopRecruitedEvent.AddNonSerializedListener(this, OnTroopRecruited);
            CampaignEvents.OnUnitRecruitedEvent.AddNonSerializedListener(this, OnUnitRecruited);
        }

        public override void SyncData(IDataStore dataStore)
        {
            _savedPartyIds ??= new List<string>();
            _savedTroopIds ??= new List<string>();
            _savedJoinDays ??= new List<int>();
            _savedCounts ??= new List<int>();
            _savedExtendedFlags ??= new List<bool>();
            _savedReserveTroopIds ??= new List<string>();
            _savedReserveJoinDays ??= new List<int>();
            _savedReserveStoredDays ??= new List<int>();
            _savedReserveCounts ??= new List<int>();
            _savedReserveExtendedFlags ??= new List<bool>();

            if (!dataStore.IsLoading)
            {
                _savedPartyIds.Clear();
                _savedTroopIds.Clear();
                _savedJoinDays.Clear();
                _savedCounts.Clear();
                _savedExtendedFlags.Clear();
                _savedReserveTroopIds.Clear();
                _savedReserveJoinDays.Clear();
                _savedReserveStoredDays.Clear();
                _savedReserveCounts.Clear();
                _savedReserveExtendedFlags.Clear();

                foreach (var partyKvp in _serviceCohorts)
                {
                    foreach (var troopKvp in partyKvp.Value)
                    {
                        foreach (CohortEntry cohort in troopKvp.Value)
                        {
                            int soldiers = Math.Max(0, cohort.Count);
                            for (int i = 0; i < soldiers; i++)
                            {
                                _savedPartyIds.Add(partyKvp.Key);
                                _savedTroopIds.Add(troopKvp.Key);
                                _savedJoinDays.Add(cohort.JoinDay);
                                _savedCounts.Add(1);
                                _savedExtendedFlags.Add(cohort.HasBeenExtended);
                            }
                        }
                    }
                }

                foreach (var reserveKvp in _transferReserve)
                {
                    foreach (TransferReserveEntry entry in reserveKvp.Value)
                    {
                        int soldiers = Math.Max(0, entry.Count);
                        for (int i = 0; i < soldiers; i++)
                        {
                            _savedReserveTroopIds.Add(reserveKvp.Key);
                            _savedReserveJoinDays.Add(entry.JoinDay);
                            _savedReserveStoredDays.Add(entry.StoredDay);
                            _savedReserveCounts.Add(1);
                            _savedReserveExtendedFlags.Add(entry.HasBeenExtended);
                        }
                    }
                }
            }

            dataStore.SyncData("b1071_demob_partyIds", ref _savedPartyIds);
            dataStore.SyncData("b1071_demob_troopIds", ref _savedTroopIds);
            dataStore.SyncData("b1071_demob_joinDays", ref _savedJoinDays);
            dataStore.SyncData("b1071_demob_counts", ref _savedCounts);
            dataStore.SyncData("b1071_demob_extendedFlags", ref _savedExtendedFlags);
            dataStore.SyncData("b1071_demob_reserveTroopIds", ref _savedReserveTroopIds);
            dataStore.SyncData("b1071_demob_reserveJoinDays", ref _savedReserveJoinDays);
            dataStore.SyncData("b1071_demob_reserveStoredDays", ref _savedReserveStoredDays);
            dataStore.SyncData("b1071_demob_reserveCounts", ref _savedReserveCounts);
            dataStore.SyncData("b1071_demob_reserveExtendedFlags", ref _savedReserveExtendedFlags);

            _savedPartyIds ??= new List<string>();
            _savedTroopIds ??= new List<string>();
            _savedJoinDays ??= new List<int>();
            _savedCounts ??= new List<int>();
            _savedExtendedFlags ??= new List<bool>();
            _savedReserveTroopIds ??= new List<string>();
            _savedReserveJoinDays ??= new List<int>();
            _savedReserveStoredDays ??= new List<int>();
            _savedReserveCounts ??= new List<int>();
            _savedReserveExtendedFlags ??= new List<bool>();

            if (dataStore.IsLoading)
            {
                _serviceCohorts.Clear();
                _transferReserve.Clear();
                int n = Math.Min(_savedPartyIds.Count,
                    Math.Min(_savedTroopIds.Count, Math.Min(_savedJoinDays.Count, _savedCounts.Count)));

                for (int i = 0; i < n; i++)
                {
                    string partyId = _savedPartyIds[i];
                    string troopId = _savedTroopIds[i];
                    int count = _savedCounts[i];
                    bool hasBeenExtended = i < _savedExtendedFlags.Count && _savedExtendedFlags[i];
                    if (string.IsNullOrEmpty(partyId) || string.IsNullOrEmpty(troopId) || count <= 0) continue;

                    if (!_serviceCohorts.TryGetValue(partyId, out var troopDict))
                    {
                        troopDict = new Dictionary<string, List<CohortEntry>>();
                        _serviceCohorts[partyId] = troopDict;
                    }

                    if (!troopDict.TryGetValue(troopId, out var cohorts))
                    {
                        cohorts = new List<CohortEntry>();
                        troopDict[troopId] = cohorts;
                    }

                    for (int soldier = 0; soldier < count; soldier++)
                    {
                        cohorts.Add(new CohortEntry
                        {
                            JoinDay = _savedJoinDays[i],
                            Count = 1,
                            HasBeenExtended = hasBeenExtended
                        });
                    }
                }

                int reserveCount = Math.Min(_savedReserveTroopIds.Count,
                    Math.Min(_savedReserveJoinDays.Count, Math.Min(_savedReserveStoredDays.Count, _savedReserveCounts.Count)));

                for (int i = 0; i < reserveCount; i++)
                {
                    string troopId = _savedReserveTroopIds[i];
                    int count = _savedReserveCounts[i];
                    bool hasBeenExtended = i < _savedReserveExtendedFlags.Count && _savedReserveExtendedFlags[i];
                    if (string.IsNullOrEmpty(troopId) || count <= 0) continue;

                    if (!_transferReserve.TryGetValue(troopId, out var entries))
                    {
                        entries = new List<TransferReserveEntry>();
                        _transferReserve[troopId] = entries;
                    }

                    for (int soldier = 0; soldier < count; soldier++)
                    {
                        entries.Add(new TransferReserveEntry
                        {
                            JoinDay = _savedReserveJoinDays[i],
                            StoredDay = _savedReserveStoredDays[i],
                            Count = 1,
                            HasBeenExtended = hasBeenExtended
                        });
                    }
                }

                B1071_VerboseLog.Log(LogTag, $"Loaded {CountTrackedSoldiers()} tracked soldier service entr{(CountTrackedSoldiers() == 1 ? "y" : "ies")} across {_serviceCohorts.Count} part{(_serviceCohorts.Count == 1 ? "y" : "ies")}; transferReserve={CountReservedSoldiers()}.");
            }
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            Instance = this;
            _lastWarningDay = -1;
            CleanupStalePartyData();
            CleanupTransferReserve(GetToday());
            RegisterMenus(starter);
            B1071_VerboseLog.Log(LogTag, $"Session launched. trackedSoldiers={CountTrackedSoldiers()}, trackedParties={_serviceCohorts.Count}, enabled={Settings.EnableDemobilizationSystem}.");
        }

        // ── Settlement menu entry point ───────────────────────────────────────────
        // Hotkey-independent way to open the troop-service screen, mirroring the
        // castle-recruitment menu option. Ensures the feature is reachable even when
        // the configured hotkey is consumed by the engine (e.g. F9 on game 1.4.5).
        private void RegisterMenus(CampaignGameStarter starter)
        {
            foreach (string menu in new[] { "town", "castle", "village" })
            {
                starter.AddGameMenuOption(
                    menu,
                    "b1071_demob_manage_" + menu,
                    "{=b1071_demob_menu}Manage troop service",
                    DemobMenuCondition,
                    DemobMenuConsequence,
                    isLeave: false,
                    index: 4);
            }
        }

        private bool DemobMenuCondition(MenuCallbackArgs args)
        {
            if (!Settings.EnableDemobilizationSystem || MobileParty.MainParty == null)
                return false;
            args.optionLeaveType = GameMenuOption.LeaveType.Manage;
            return true;
        }

        private void DemobMenuConsequence(MenuCallbackArgs args)
        {
            B1071_DemobilizationScreen.OpenScreen();
        }

        private void OnTroopRecruited(Hero recruiterHero, Settlement recruitmentSettlement, Hero recruitmentSource, CharacterObject troop, int amount)
        {
            try
            {
                if (!Settings.EnableDemobilizationSystem || amount <= 0 || troop == null || troop.IsHero) return;

                if (recruiterHero == null)
                {
                    B1071_VerboseLog.Log(LogTag, $"OnTroopRecruited skipped: recruiterHero=null, troop={troop.StringId}, soldiers={amount}.");
                    return;
                }

                MobileParty? party = recruiterHero.PartyBelongedTo;
                if (party == null || !IsEligibleFieldParty(party)) return;

                AddFreshCohort(party, troop, amount, GetToday(), "event");
            }
            catch (Exception ex)
            {
                B1071_VerboseLog.Log(LogTag, $"OnTroopRecruited skipped: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void OnUnitRecruited(CharacterObject troop, int amount)
        {
            try
            {
                if (!Settings.EnableDemobilizationSystem || amount <= 0 || troop == null || troop.IsHero) return;

                MobileParty? mainParty = MobileParty.MainParty;
                if (mainParty == null || !IsEligibleFieldParty(mainParty)) return;

                AddFreshCohort(mainParty, troop, amount, GetToday(), "unit_event");
            }
            catch (Exception ex)
            {
                B1071_VerboseLog.Log(LogTag, $"OnUnitRecruited skipped: {ex.GetType().Name}: {ex.Message}");
            }
        }

        public int RegisterDirectRecruitment(MobileParty party, CharacterObject troop, int amount, string source)
        {
            try
            {
                if (!Settings.EnableDemobilizationSystem || amount <= 0 || !IsTrackableTroop(troop)) return 0;
                if (party == null || party.MemberRoster == null || !IsEligibleFieldParty(party)) return 0;

                string partyId = GetPartyId(party);
                if (string.IsNullOrEmpty(partyId)) return 0;

                int currentCount = party.MemberRoster.GetTroopCount(troop);
                if (currentCount <= 0) return 0;

                int trackedCount = GetTrackedTroopCount(partyId, troop.StringId);
                int missing = Math.Min(amount, Math.Max(0, currentCount - trackedCount));
                if (missing <= 0)
                {
                    B1071_VerboseLog.Log(LogTag, $"Direct recruit service registration skipped: source={source}, party={PartyLogName(party)}, troop={troop.StringId}, requested={amount}, roster={currentCount}, tracked={trackedCount}.");
                    return 0;
                }

                AddFreshCohort(party, troop, missing, GetToday(), source);
                return missing;
            }
            catch (Exception ex)
            {
                B1071_VerboseLog.Log(LogTag, $"RegisterDirectRecruitment skipped: source={source}, {ex.GetType().Name}: {ex.Message}");
                return 0;
            }
        }

        private void OnDailyTick()
        {
            try
            {
                if (!Settings.EnableDemobilizationSystem) return;

                int today = GetToday();
                CleanupStalePartyData();
                CleanupTransferReserve(today);

                var eligibleParties = new List<MobileParty>();
                foreach (MobileParty party in MobileParty.All)
                {
                    if (!IsEligibleFieldParty(party)) continue;
                    eligibleParties.Add(party);
                }

                foreach (MobileParty party in eligibleParties)
                    ReconcileParty(party, today, addNewlyObserved: false);

                foreach (MobileParty party in eligibleParties)
                {
                    ReconcileParty(party, today, addNewlyObserved: true);
                    TryApplyAiExtensions(party, today);
                    RetireOverdueCohorts(party, today);
                }

                ShowMainPartyWarningIfNeeded(today);
                B1071_VerboseLog.Log(LogTag, $"Daily tick day={today}: processedParties={eligibleParties.Count}, trackedSoldiers={CountTrackedSoldiers()}, trackedParties={_serviceCohorts.Count}, transferReserve={CountReservedSoldiers()}.");
            }
            catch (Exception ex)
            {
                B1071_VerboseLog.Log(LogTag, $"Daily tick failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        public List<CohortView> GetMainPartyCohortsForUi()
        {
            var rows = new List<CohortView>();
            MobileParty? party = MobileParty.MainParty;
            if (party == null || party.MemberRoster == null) return rows;

            int today = GetToday();
            ReconcileParty(party, today);

            string partyId = GetPartyId(party);
            if (!_serviceCohorts.TryGetValue(partyId, out var troopDict)) return rows;

            foreach (var troopKvp in troopDict)
            {
                CharacterObject? troop = ResolveTroop(troopKvp.Key);
                if (troop == null) continue;

                for (int i = 0; i < troopKvp.Value.Count; i++)
                {
                    CohortEntry cohort = troopKvp.Value[i];
                    if (cohort.Count <= 0) continue;

                    int threshold = GetServiceThresholdDays(troop, party);
                    int age = Math.Max(0, today - cohort.JoinDay);
                    int remaining = threshold - age;
                    rows.Add(new CohortView
                    {
                        PartyId = partyId,
                        TroopId = troopKvp.Key,
                        CohortIndex = i,
                        Troop = troop,
                        Count = cohort.Count,
                        JoinDay = cohort.JoinDay,
                        AgeDays = age,
                        ThresholdDays = threshold,
                        RemainingDays = remaining,
                        ExtendCost = GetExtensionCost(troop, cohort.Count),
                        IsWarning = remaining <= Settings.DemobilizationWarningLeadDays,
                        IsOverdue = remaining <= 0,
                        HasBeenExtended = cohort.HasBeenExtended,
                        CanExtend = !cohort.HasBeenExtended && Hero.MainHero != null && Hero.MainHero.Gold >= GetExtensionCost(troop, cohort.Count)
                    });
                }
            }

            rows.Sort((a, b) =>
            {
                int compare = a.RemainingDays.CompareTo(b.RemainingDays);
                if (compare != 0) return compare;
                compare = a.Troop.Tier.CompareTo(b.Troop.Tier);
                if (compare != 0) return compare;
                return string.Compare(a.Troop.Name?.ToString(), b.Troop.Name?.ToString(), StringComparison.Ordinal);
            });

            return rows;
        }

        public bool TryExtendCohort(string partyId, string troopId, int cohortIndex)
        {
            try
            {
                if (!Settings.EnableDemobilizationSystem) return false;
                MobileParty? mainParty = MobileParty.MainParty;
                if (mainParty == null || !string.Equals(GetPartyId(mainParty), partyId, StringComparison.Ordinal)) return false;
                if (!_serviceCohorts.TryGetValue(partyId, out var troopDict)) return false;
                if (!troopDict.TryGetValue(troopId, out var cohorts)) return false;
                if (cohortIndex < 0 || cohortIndex >= cohorts.Count) return false;

                CharacterObject? troop = ResolveTroop(troopId);
                if (troop == null) return false;

                CohortEntry cohort = cohorts[cohortIndex];
                int cost = GetExtensionCost(troop, cohort.Count);
                if (cost < 0 || Hero.MainHero == null) return false;

                if (cohort.HasBeenExtended)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TextObject("{=b1071_demob_extend_used}This soldier has already used his one allowed service extension.")
                            .ToString(), Colors.Red));
                    return false;
                }

                if (Hero.MainHero.Gold < cost)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TextObject("{=b1071_demob_extend_no_gold}Not enough gold to extend service. Need {COST}g.")
                            .SetTextVariable("COST", cost)
                            .ToString(), Colors.Red));
                    return false;
                }

                if (cost > 0)
                    GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, cost, disableNotification: true);
                cohort.JoinDay += Math.Max(1, Settings.DemobilizationExtensionDays);
                cohort.HasBeenExtended = true;

                B1071_VerboseLog.Log(LogTag, $"Extended service: party={partyId}, troop={troopId}, entry={cohortIndex}, days={Math.Max(1, Settings.DemobilizationExtensionDays)}, cost={cost}, newJoinDay={cohort.JoinDay}.");

                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=b1071_demob_extend_done}Extended service for {COUNT} {TROOP} by {DAYS} days for {COST}g.")
                        .SetTextVariable("COUNT", cohort.Count)
                        .SetTextVariable("TROOP", troop.Name?.ToString() ?? new TextObject("{=b1071_ui_unknown}Unknown").ToString())
                        .SetTextVariable("DAYS", Math.Max(1, Settings.DemobilizationExtensionDays))
                        .SetTextVariable("COST", cost)
                        .ToString(), new Color(0.35f, 0.75f, 0.55f)));

                return true;
            }
            catch (Exception ex)
            {
                B1071_VerboseLog.Log(LogTag, $"TryExtendCohort failed: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private void ReconcileParty(MobileParty party, int today)
        {
            ReconcileParty(party, today, addNewlyObserved: true);
        }

        private void ReconcileParty(MobileParty party, int today, bool addNewlyObserved)
        {
            if (party.MemberRoster == null) return;

            string partyId = GetPartyId(party);
            if (!_serviceCohorts.TryGetValue(partyId, out var troopDict))
            {
                troopDict = new Dictionary<string, List<CohortEntry>>();
                _serviceCohorts[partyId] = troopDict;
            }

            Dictionary<string, int> currentCounts = GetRosterCounts(party.MemberRoster);
            NormalizeIndividualEntries(troopDict);
            CarryUpgradeServiceForward(party, troopDict, currentCounts);
            RemoveMissingTrackedTroops(party, troopDict, currentCounts, today);
            if (addNewlyObserved)
                AddNewlyObservedTroops(party, troopDict, currentCounts, today);
            RemoveEmptyEntries(partyId);
        }

        private void CarryUpgradeServiceForward(MobileParty party, Dictionary<string, List<CohortEntry>> troopDict, Dictionary<string, int> currentCounts)
        {
            bool movedAny;
            int guard = 0;
            do
            {
                movedAny = false;
                guard++;
                Dictionary<string, int> trackedTotals = BuildTrackedTotals(troopDict);
                var troopIds = new List<string>(troopDict.Keys);

                foreach (string sourceId in troopIds)
                {
                    int tracked = GetCount(trackedTotals, sourceId);
                    int current = GetCount(currentCounts, sourceId);
                    int missing = tracked - current;
                    if (missing <= 0) continue;

                    foreach (string targetId in GetUpgradePathIds(sourceId))
                    {
                        if (missing <= 0) break;
                        int targetGain = GetCount(currentCounts, targetId) - GetCount(trackedTotals, targetId);
                        if (targetGain <= 0) continue;

                        int promotionBonusDays = Math.Max(0, Settings.DemobilizationPromotionBonusDays);
                        int moved = MoveOldestCohorts(troopDict, sourceId, targetId, Math.Min(missing, targetGain), promotionBonusDays, GetToday());
                        if (moved <= 0) continue;

                        movedAny = true;
                        trackedTotals[sourceId] = GetCount(trackedTotals, sourceId) - moved;
                        trackedTotals[targetId] = GetCount(trackedTotals, targetId) + moved;
                        missing -= moved;
                        B1071_VerboseLog.Log(LogTag, $"Upgrade carryover: party={PartyLogName(party)}, {sourceId}->{targetId}, soldiers={moved}, promotionBonusDays={promotionBonusDays}.");
                    }
                }
            }
            while (movedAny && guard < 8);
        }

        private void RemoveMissingTrackedTroops(MobileParty party, Dictionary<string, List<CohortEntry>> troopDict, Dictionary<string, int> currentCounts, int today)
        {
            Dictionary<string, int> trackedTotals = BuildTrackedTotals(troopDict);
            var troopIds = new List<string>(troopDict.Keys);
            foreach (string troopId in troopIds)
            {
                int excess = GetCount(trackedTotals, troopId) - GetCount(currentCounts, troopId);
                if (excess > 0)
                {
                    int reserved = MoveOldestCohortsToTransferReserve(troopDict, troopId, excess, today);
                    B1071_VerboseLog.Log(LogTag, $"Roster reconciliation reserved missing service entries: party={PartyLogName(party)}, troop={troopId}, soldiers={reserved}, reserve={CountReservedSoldiers()}.");
                }
            }
        }

        private void AddNewlyObservedTroops(MobileParty party, Dictionary<string, List<CohortEntry>> troopDict, Dictionary<string, int> currentCounts, int today)
        {
            Dictionary<string, int> trackedTotals = BuildTrackedTotals(troopDict);
            foreach (var kvp in currentCounts)
            {
                int fresh = kvp.Value - GetCount(trackedTotals, kvp.Key);
                if (fresh <= 0) continue;

                if (!troopDict.TryGetValue(kvp.Key, out var cohorts))
                {
                    cohorts = new List<CohortEntry>();
                    troopDict[kvp.Key] = cohorts;
                }

                bool hadTrackedCohortsBeforeRestore = cohorts.Count > 0;
                int restored = RestoreTransferReserveEntries(kvp.Key, cohorts, fresh, today);
                int newSoldiers = fresh - restored;

                if (restored > 0)
                {
                    B1071_VerboseLog.Log(LogTag, $"Restored transferred service entries: party={PartyLogName(party)}, troop={kvp.Key}, soldiers={restored}, reserve={CountReservedSoldiers()}.");
                    if (!hadTrackedCohortsBeforeRestore)
                        B1071_VerboseLog.Log(LogTag, $"Reserve restore into previously untracked troop type: party={PartyLogName(party)}, troop={kvp.Key}, soldiers={restored}, requestedFresh={fresh}.");
                }

                if (newSoldiers > 0)
                {
                    AddIndividualEntries(cohorts, today, newSoldiers);
                    B1071_VerboseLog.Log(LogTag, $"Observed new service soldiers: party={PartyLogName(party)}, troop={kvp.Key}, soldiers={newSoldiers}, joinDay={today}.");
                }
            }
        }

        private void TryApplyAiExtensions(MobileParty party, int today)
        {
            if (!Settings.DemobilizationAiExtensionsEnabled) return;
            if (party == MobileParty.MainParty) return;

            Hero? leader = party.LeaderHero;
            if (leader == null || leader.Gold <= 0) return;

            string partyId = GetPartyId(party);
            if (!_serviceCohorts.TryGetValue(partyId, out var troopDict)) return;

            int warningLead = Math.Max(0, Settings.DemobilizationWarningLeadDays);
            var candidates = new List<AiExtensionCandidate>();

            foreach (var troopKvp in troopDict)
            {
                CharacterObject? troop = ResolveTroop(troopKvp.Key);
                if (troop == null) continue;

                int threshold = GetServiceThresholdDays(troop, party);
                foreach (CohortEntry cohort in troopKvp.Value)
                {
                    if (cohort.Count <= 0 || cohort.HasBeenExtended) continue;

                    int age = Math.Max(0, today - cohort.JoinDay);
                    int remaining = threshold - age;
                    if (remaining > warningLead) continue;

                    for (int unit = 0; unit < cohort.Count; unit++)
                    {
                        candidates.Add(new AiExtensionCandidate
                        {
                            TroopId = troopKvp.Key,
                            Troop = troop,
                            Cohort = cohort,
                            JoinDay = cohort.JoinDay,
                            RemainingDays = remaining,
                            Cost = GetExtensionCost(troop, 1)
                        });
                    }
                }
            }

            if (candidates.Count == 0) return;

            candidates.Sort((a, b) =>
            {
                int compare = a.RemainingDays.CompareTo(b.RemainingDays);
                if (compare != 0) return compare;
                compare = a.JoinDay.CompareTo(b.JoinDay);
                if (compare != 0) return compare;
                return string.Compare(a.TroopId, b.TroopId, StringComparison.Ordinal);
            });

            int cap = Math.Max(1, Settings.DemobilizationMaxDailyDepartures);
            int extended = 0;
            int spent = 0;
            string firstTroopName = string.Empty;
            int extensionDays = Math.Max(1, Settings.DemobilizationExtensionDays);

            foreach (AiExtensionCandidate candidate in candidates)
            {
                if (extended >= cap) break;
                if (candidate.Cohort.Count <= 0 || candidate.Cohort.HasBeenExtended) continue;
                if (!CanAiAffordExtension(leader, candidate.Cost)) continue;

                if (candidate.Cost > 0)
                    GiveGoldAction.ApplyBetweenCharacters(leader, null, candidate.Cost, disableNotification: true);

                candidate.Cohort.JoinDay += extensionDays;
                candidate.Cohort.HasBeenExtended = true;
                extended++;
                spent += candidate.Cost;

                if (string.IsNullOrEmpty(firstTroopName))
                    firstTroopName = candidate.Troop.Name?.ToString() ?? candidate.TroopId;
            }

            if (extended > 0)
            {
                B1071_VerboseLog.Log(LogTag, $"AI extended service: party={PartyLogName(party)}, hero={leader.Name?.ToString() ?? leader.StringId}, soldiers={extended}, spent={spent}, days={extensionDays}, buffer={Math.Max(1, Settings.DemobilizationAiExtensionGoldBufferMultiplier)}x, firstTroop={firstTroopName}.");
            }
        }

        private static bool CanAiAffordExtension(Hero hero, int cost)
        {
            if (cost <= 0) return true;
            int bufferMultiplier = Math.Max(1, Settings.DemobilizationAiExtensionGoldBufferMultiplier);
            long requiredGold = (long)cost * bufferMultiplier;
            return hero.Gold > requiredGold;
        }

        private void RetireOverdueCohorts(MobileParty party, int today)
        {
            string partyId = GetPartyId(party);
            if (!_serviceCohorts.TryGetValue(partyId, out var troopDict)) return;

            int partyCap = Math.Max(1, Settings.DemobilizationMaxDailyDepartures);
            int retiredTotal = 0;
            string firstTroopName = string.Empty;

            var overdueByTroop = new Dictionary<string, int>();
            var thresholdByTroop = new Dictionary<string, int>();
            var troopCapByTroop = new Dictionary<string, int>();
            var retiredByTroop = new Dictionary<string, int>();
            var candidates = new List<OverdueCandidate>();

            var troopIds = new List<string>(troopDict.Keys);
            foreach (string troopId in troopIds)
            {
                CharacterObject? troop = ResolveTroop(troopId);
                if (troop == null) continue;
                if (!troopDict.TryGetValue(troopId, out var cohorts)) continue;

                int threshold = GetServiceThresholdDays(troop, party);
                int overdueForTroop = CountOverdueSoldiers(cohorts, today, threshold);
                if (overdueForTroop <= 0) continue;

                int troopCap = Math.Max(1, overdueForTroop * Math.Max(1, Settings.DemobilizationDailyCapPercent) / 100);
                overdueByTroop[troopId] = overdueForTroop;
                thresholdByTroop[troopId] = threshold;
                troopCapByTroop[troopId] = troopCap;

                for (int i = 0; i < cohorts.Count; i++)
                {
                    CohortEntry cohort = cohorts[i];
                    if (cohort.Count <= 0) continue;
                    if (today - cohort.JoinDay < threshold) continue;

                    for (int unit = 0; unit < cohort.Count; unit++)
                    {
                        candidates.Add(new OverdueCandidate
                        {
                            TroopId = troopId,
                            Troop = troop,
                            Cohort = cohort,
                            JoinDay = cohort.JoinDay,
                            ThresholdDays = threshold
                        });
                    }
                }
            }

            candidates.Sort((a, b) =>
            {
                int compare = a.JoinDay.CompareTo(b.JoinDay);
                if (compare != 0) return compare;
                compare = string.Compare(a.TroopId, b.TroopId, StringComparison.Ordinal);
                if (compare != 0) return compare;
                return a.ThresholdDays.CompareTo(b.ThresholdDays);
            });

            foreach (OverdueCandidate candidate in candidates)
            {
                if (retiredTotal >= partyCap) break;
                if (candidate.Cohort.Count <= 0) continue;

                int troopRetired = GetCount(retiredByTroop, candidate.TroopId);
                if (troopRetired >= GetCount(troopCapByTroop, candidate.TroopId)) continue;

                int removed = RemoveTroopsFromRoster(party, candidate.Troop, 1);
                if (removed <= 0) continue;

                candidate.Cohort.Count -= removed;
                retiredTotal += removed;
                retiredByTroop[candidate.TroopId] = troopRetired + removed;
                if (string.IsNullOrEmpty(firstTroopName))
                    firstTroopName = candidate.Troop.Name?.ToString() ?? new TextObject("{=b1071_ui_unknown}Unknown").ToString();
            }

            foreach (var kvp in retiredByTroop)
            {
                if (kvp.Value <= 0) continue;
                B1071_VerboseLog.Log(LogTag, $"Retired service soldiers: party={PartyLogName(party)}, troop={kvp.Key}, soldiers={kvp.Value}, overdue={GetCount(overdueByTroop, kvp.Key)}, threshold={GetCount(thresholdByTroop, kvp.Key)}, troopCap={GetCount(troopCapByTroop, kvp.Key)}, partyCap={partyCap}.");
            }

            RemoveEmptyEntries(partyId);

            if (retiredTotal > 0)
            {
                if (party == MobileParty.MainParty)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TextObject("{=b1071_demob_retired_main}{COUNT} soldier{PLURAL} completed service and left your party. First group: {TROOP}.")
                            .SetTextVariable("COUNT", retiredTotal)
                            .SetTextVariable("PLURAL", retiredTotal == 1 ? string.Empty : "s")
                            .SetTextVariable("TROOP", string.IsNullOrEmpty(firstTroopName) ? new TextObject("{=b1071_ui_unknown}Unknown").ToString() : firstTroopName)
                            .ToString(), new Color(0.85f, 0.55f, 0.25f)));
                }

                if (party.CurrentSettlement != null)
                {
                    party.SetMoveGoToSettlement(party.CurrentSettlement, party.DesiredAiNavigationType, party.IsTargetingPort);
                    party.RecalculateShortTermBehavior();
                }
            }
        }

        private void ShowMainPartyWarningIfNeeded(int today)
        {
            if (_lastWarningDay == today) return;
            if (!Settings.DemobilizationNotifyPlayer && !Settings.DemobilizationWarningPopup) return;

            List<CohortView> rows = GetMainPartyCohortsForUi();
            CohortView? earliest = null;
            int warningCount = 0;
            int warningMen = 0;
            bool shouldShowPopup = false;
            int popupLead = Math.Max(0, Settings.DemobilizationWarningLeadDays);

            foreach (CohortView row in rows)
            {
                if (row.RemainingDays > Settings.DemobilizationWarningLeadDays) continue;
                warningCount++;
                warningMen += row.Count;
                if (earliest == null || row.RemainingDays < earliest.RemainingDays)
                    earliest = row;

                if (row.RemainingDays == popupLead || row.RemainingDays == 0)
                    shouldShowPopup = true;
            }

            if (earliest == null) return;
            _lastWarningDay = today;

            string troopName = earliest.Troop.Name?.ToString() ?? new TextObject("{=b1071_ui_unknown}Unknown").ToString();
            string warning = new TextObject("{=b1071_demob_warning_text}{MEN} service soldier{MPLURAL} will begin leaving within {LEAD} days. Earliest: {TROOP}, {DAYS} day{DPLURAL} remaining.")
                .SetTextVariable("COHORTS", warningCount)
                .SetTextVariable("CPLURAL", warningCount == 1 ? string.Empty : "s")
                .SetTextVariable("MEN", warningMen)
                .SetTextVariable("MPLURAL", warningMen == 1 ? string.Empty : "s")
                .SetTextVariable("LEAD", Settings.DemobilizationWarningLeadDays)
                .SetTextVariable("COUNT", earliest.Count)
                .SetTextVariable("TROOP", troopName)
                .SetTextVariable("DAYS", Math.Max(0, earliest.RemainingDays))
                .SetTextVariable("DPLURAL", Math.Abs(earliest.RemainingDays) == 1 ? string.Empty : "s")
                .ToString();

            bool sentMessage = false;
            bool sentPopup = false;

            if (Settings.DemobilizationNotifyPlayer)
            {
                InformationManager.DisplayMessage(new InformationMessage(warning, new Color(0.9f, 0.7f, 0.25f)));
                sentMessage = true;
            }

            if (Settings.DemobilizationWarningPopup && shouldShowPopup)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    titleText: new TextObject("{=b1071_demob_warning_title}Troops Near Demobilization").ToString(),
                    text: warning,
                    isAffirmativeOptionShown: true,
                    isNegativeOptionShown: true,
                    affirmativeText: new TextObject("{=b1071_demob_open_service}Open Service").ToString(),
                    negativeText: new TextObject("{=b1071_ui_ok}OK").ToString(),
                    affirmativeAction: B1071_DemobilizationScreen.OpenScreen,
                    negativeAction: null));
                sentPopup = true;
            }

            if (sentMessage || sentPopup)
                B1071_VerboseLog.Log(LogTag, $"Player warning issued: soldiers={warningMen}, nearestTroop={troopName}, remaining={earliest.RemainingDays}, message={sentMessage}, popup={sentPopup}.");
        }

        private Dictionary<string, int> GetRosterCounts(TroopRoster roster)
        {
            var result = new Dictionary<string, int>();
            var elements = roster.GetTroopRoster();
            for (int i = 0; i < elements.Count; i++)
            {
                CharacterObject? troop = elements[i].Character;
                int count = elements[i].Number;
                if (!IsTrackableTroop(troop) || count <= 0) continue;
                string troopId = troop!.StringId;
                result[troopId] = GetCount(result, troopId) + count;
            }

            return result;
        }

        private bool IsEligibleFieldParty(MobileParty? party)
        {
            if (party == null || party.MemberRoster == null || string.IsNullOrEmpty(party.StringId)) return false;
            if (party.IsDisbanding || party.IsGarrison || party.IsBandit || party.IsCaravan || party.IsVillager) return false;
            if (party == MobileParty.MainParty) return true;
            return party.IsLordParty && party.LeaderHero?.Clan != null;
        }

        private static bool IsTrackableTroop(CharacterObject? troop)
        {
            return troop != null && !troop.IsHero;
        }

        private void AddFreshCohort(MobileParty party, CharacterObject troop, int amount, int today, string source)
        {
            if (!IsTrackableTroop(troop) || amount <= 0) return;
            string partyId = GetPartyId(party);
            if (!_serviceCohorts.TryGetValue(partyId, out var troopDict))
            {
                troopDict = new Dictionary<string, List<CohortEntry>>();
                _serviceCohorts[partyId] = troopDict;
            }

            if (!troopDict.TryGetValue(troop.StringId, out var cohorts))
            {
                cohorts = new List<CohortEntry>();
                troopDict[troop.StringId] = cohorts;
            }

            AddIndividualEntries(cohorts, today, amount);
            B1071_VerboseLog.Log(LogTag, $"Fresh service soldiers registered: source={source}, party={PartyLogName(party)}, troop={troop.StringId}, soldiers={amount}, joinDay={today}.");
        }

        private int GetTrackedTroopCount(string partyId, string troopId)
        {
            if (!_serviceCohorts.TryGetValue(partyId, out var troopDict)) return 0;
            if (!troopDict.TryGetValue(troopId, out var cohorts)) return 0;

            int total = 0;
            foreach (CohortEntry cohort in cohorts)
                total += Math.Max(0, cohort.Count);
            return total;
        }

        private int RemoveTroopsFromRoster(MobileParty party, CharacterObject troop, int requested)
        {
            if (party.MemberRoster == null || requested <= 0) return 0;
            int have = party.MemberRoster.GetTroopCount(troop);
            int remove = Math.Min(have, requested);
            if (remove <= 0) return 0;

            party.MemberRoster.AddToCounts(troop, -remove, insertAtFront: false, woundedCount: 0, xpChange: 0, removeDepleted: true, index: -1);
            return remove;
        }

        private int GetServiceThresholdDays(CharacterObject troop, MobileParty party)
        {
            int tier = ClampInt(troop.Tier, 1, 6);
            int baseDays = GetBaseServiceDays(tier);
            int percent = 100;

            if (Settings.EnableDemobilizationSeasonality)
            {
                var season = CampaignTime.Now.GetSeasonOfYear;
                if (season == CampaignTime.Seasons.Spring || season == CampaignTime.Seasons.Summer)
                    percent = percent * Math.Max(25, Settings.DemobilizationSpringSummerThresholdPercent) / 100;
                else if (season == CampaignTime.Seasons.Winter)
                    percent = percent * Math.Max(25, Settings.DemobilizationWinterThresholdPercent) / 100;
            }

            if (Settings.EnableDemobilizationCrisisCompression)
            {
                string? kingdomId = party.LeaderHero?.Clan?.Kingdom?.StringId;
                if (!string.IsNullOrEmpty(kingdomId) && B1071_ManpowerBehavior.Instance?.GetPressureBand(kingdomId) == DiplomacyPressureBand.Crisis)
                    percent = percent * Math.Max(25, Settings.DemobilizationCrisisThresholdPercent) / 100;
            }

            return Math.Max(1, baseDays * percent / 100);
        }

        private int GetBaseServiceDays(int tier)
        {
            int preset = Settings.DemobilizationIntensityPreset;
            if (preset == 0)
            {
                switch (tier)
                {
                    case 1: return 34;
                    case 2: return 49;
                    case 3: return 63;
                    case 4: return 91;
                    case 5: return 126;
                    default: return 168;
                }
            }

            if (preset == 2)
            {
                switch (tier)
                {
                    case 1: return 14;
                    case 2: return 21;
                    case 3: return 35;
                    case 4: return 56;
                    case 5: return 84;
                    default: return 112;
                }
            }

            if (preset == 3)
            {
                switch (tier)
                {
                    case 1: return Settings.DemobilizationT1ServiceDays;
                    case 2: return Settings.DemobilizationT2ServiceDays;
                    case 3: return Settings.DemobilizationT3ServiceDays;
                    case 4: return Settings.DemobilizationT4ServiceDays;
                    case 5: return Settings.DemobilizationT5ServiceDays;
                    default: return Settings.DemobilizationT6ServiceDays;
                }
            }

            switch (tier)
            {
                case 1: return 21;
                case 2: return 32;
                case 3: return 45;
                case 4: return 63;
                case 5: return 84;
                default: return 112;
            }
        }

        private int GetExtensionCost(CharacterObject troop, int count)
        {
            int tier = ClampInt(troop.Tier, 1, 6);
            int days = Math.Max(1, Settings.DemobilizationExtensionDays);
            int costPerTierDay = Math.Max(0, Settings.DemobilizationExtensionGoldPerTierDay);
            return Math.Max(0, count * tier * days * costPerTierDay);
        }

        private List<string> GetUpgradePathIds(string sourceTroopId)
        {
            if (_upgradePathCache.TryGetValue(sourceTroopId, out List<string> cached))
                return cached;

            var result = new List<string>();
            var seen = new HashSet<string> { sourceTroopId };
            var queue = new Queue<CharacterObject>();
            CharacterObject? source = ResolveTroop(sourceTroopId);
            if (source?.UpgradeTargets != null)
            {
                foreach (CharacterObject target in source.UpgradeTargets)
                {
                    if (target == null || target.IsHero || !seen.Add(target.StringId)) continue;
                    queue.Enqueue(target);
                }
            }

            while (queue.Count > 0)
            {
                CharacterObject current = queue.Dequeue();
                result.Add(current.StringId);

                if (current.UpgradeTargets == null) continue;
                foreach (CharacterObject target in current.UpgradeTargets)
                {
                    if (target == null || target.IsHero || !seen.Add(target.StringId)) continue;
                    queue.Enqueue(target);
                }
            }

            _upgradePathCache[sourceTroopId] = result;
            return result;
        }

        private CharacterObject? ResolveTroop(string troopId)
        {
            if (string.IsNullOrEmpty(troopId)) return null;
            return MBObjectManager.Instance?.GetObject<CharacterObject>(troopId);
        }

        private static Dictionary<string, int> BuildTrackedTotals(Dictionary<string, List<CohortEntry>> troopDict)
        {
            var totals = new Dictionary<string, int>();
            foreach (var kvp in troopDict)
            {
                int total = 0;
                foreach (CohortEntry cohort in kvp.Value)
                    total += Math.Max(0, cohort.Count);
                totals[kvp.Key] = total;
            }

            return totals;
        }

        private static void NormalizeIndividualEntries(Dictionary<string, List<CohortEntry>> troopDict)
        {
            var troopIds = new List<string>(troopDict.Keys);
            foreach (string troopId in troopIds)
            {
                var original = troopDict[troopId];
                bool needsSplit = false;
                for (int i = 0; i < original.Count; i++)
                {
                    if (original[i].Count > 1)
                    {
                        needsSplit = true;
                        break;
                    }
                }

                if (!needsSplit) continue;

                var split = new List<CohortEntry>();
                foreach (CohortEntry entry in original)
                    AddIndividualEntries(split, entry.JoinDay, entry.Count, entry.HasBeenExtended);

                troopDict[troopId] = split;
            }
        }

        private static void AddIndividualEntries(List<CohortEntry> entries, int joinDay, int count, bool hasBeenExtended = false)
        {
            for (int i = 0; i < count; i++)
                entries.Add(new CohortEntry { JoinDay = joinDay, Count = 1, HasBeenExtended = hasBeenExtended });
        }

        private static int CountOverdueSoldiers(List<CohortEntry> cohorts, int today, int threshold)
        {
            int total = 0;
            foreach (CohortEntry cohort in cohorts)
            {
                if (cohort.Count <= 0) continue;
                if (today - cohort.JoinDay >= threshold)
                    total += cohort.Count;
            }

            return total;
        }

        private int CountTrackedSoldiers()
        {
            int total = 0;
            foreach (var partyKvp in _serviceCohorts)
            {
                foreach (var troopKvp in partyKvp.Value)
                {
                    foreach (CohortEntry cohort in troopKvp.Value)
                        total += Math.Max(0, cohort.Count);
                }
            }

            return total;
        }

        private int CountReservedSoldiers()
        {
            int total = 0;
            foreach (var kvp in _transferReserve)
            {
                foreach (TransferReserveEntry entry in kvp.Value)
                    total += Math.Max(0, entry.Count);
            }

            return total;
        }

        private int MoveOldestCohortsToTransferReserve(Dictionary<string, List<CohortEntry>> troopDict, string troopId, int count, int today)
        {
            if (count <= 0 || !troopDict.TryGetValue(troopId, out var cohorts)) return 0;
            if (!_transferReserve.TryGetValue(troopId, out var reserveEntries))
            {
                reserveEntries = new List<TransferReserveEntry>();
                _transferReserve[troopId] = reserveEntries;
            }

            int remaining = count;
            int moved = 0;
            for (int i = 0; i < cohorts.Count && remaining > 0; i++)
            {
                CohortEntry cohort = cohorts[i];
                if (cohort.Count <= 0) continue;

                int take = Math.Min(cohort.Count, remaining);
                cohort.Count -= take;
                remaining -= take;
                moved += take;

                reserveEntries.Add(new TransferReserveEntry
                {
                    JoinDay = cohort.JoinDay,
                    StoredDay = today,
                    Count = take,
                    HasBeenExtended = cohort.HasBeenExtended
                });
            }

            cohorts.RemoveAll(c => c.Count <= 0);
            SortTransferReserve(troopId);
            return moved;
        }

        private int RestoreTransferReserveEntries(string troopId, List<CohortEntry> cohorts, int count, int today)
        {
            if (count <= 0 || !_transferReserve.TryGetValue(troopId, out var reserveEntries)) return 0;

            CleanupTransferReserve(troopId, today);
            if (!_transferReserve.TryGetValue(troopId, out reserveEntries)) return 0;

            int remaining = count;
            int restored = 0;
            for (int i = 0; i < reserveEntries.Count && remaining > 0; i++)
            {
                TransferReserveEntry entry = reserveEntries[i];
                if (entry.Count <= 0) continue;

                int take = Math.Min(entry.Count, remaining);
                entry.Count -= take;
                remaining -= take;
                restored += take;
                AddIndividualEntries(cohorts, entry.JoinDay, take, entry.HasBeenExtended);
            }

            reserveEntries.RemoveAll(e => e.Count <= 0);
            if (reserveEntries.Count == 0)
                _transferReserve.Remove(troopId);

            return restored;
        }

        private void CleanupTransferReserve(int today)
        {
            var troopIds = new List<string>(_transferReserve.Keys);
            foreach (string troopId in troopIds)
                CleanupTransferReserve(troopId, today);
        }

        private void CleanupTransferReserve(string troopId, int today)
        {
            if (!_transferReserve.TryGetValue(troopId, out var reserveEntries)) return;

            int retentionDays = GetTransferReserveRetentionDays();
            reserveEntries.RemoveAll(e => e.Count <= 0 || today - e.StoredDay > retentionDays);
            if (reserveEntries.Count == 0)
            {
                _transferReserve.Remove(troopId);
                return;
            }

            SortTransferReserve(troopId);
        }

        private void SortTransferReserve(string troopId)
        {
            if (!_transferReserve.TryGetValue(troopId, out var reserveEntries)) return;

            reserveEntries.Sort((a, b) =>
            {
                int compare = a.JoinDay.CompareTo(b.JoinDay);
                if (compare != 0) return compare;
                return a.StoredDay.CompareTo(b.StoredDay);
            });
        }

        private static int GetTransferReserveRetentionDays()
        {
            return Math.Max(MinimumTransferReserveDays, Math.Max(Settings.DemobilizationWarningLeadDays, Settings.DemobilizationExtensionDays));
        }

        private static int MoveOldestCohorts(Dictionary<string, List<CohortEntry>> troopDict, string fromTroopId, string toTroopId, int count, int serviceDayBonus, int today)
        {
            if (count <= 0 || !troopDict.TryGetValue(fromTroopId, out var fromList)) return 0;
            if (!troopDict.TryGetValue(toTroopId, out var toList))
            {
                toList = new List<CohortEntry>();
                troopDict[toTroopId] = toList;
            }

            int moved = 0;
            for (int i = 0; i < fromList.Count && moved < count; i++)
            {
                CohortEntry source = fromList[i];
                if (source.Count <= 0) continue;
                int take = Math.Min(source.Count, count - moved);
                source.Count -= take;
                int adjustedJoinDay = source.JoinDay;
                if (serviceDayBonus > 0 && source.JoinDay < today)
                    adjustedJoinDay = Math.Min(today, source.JoinDay + serviceDayBonus);
                AddIndividualEntries(toList, adjustedJoinDay, take, source.HasBeenExtended);
                moved += take;
            }

            fromList.RemoveAll(c => c.Count <= 0);
            return moved;
        }

        private static void RemoveOldestCohorts(Dictionary<string, List<CohortEntry>> troopDict, string troopId, int count)
        {
            if (count <= 0 || !troopDict.TryGetValue(troopId, out var cohorts)) return;
            int remaining = count;
            for (int i = 0; i < cohorts.Count && remaining > 0; i++)
            {
                CohortEntry cohort = cohorts[i];
                int take = Math.Min(cohort.Count, remaining);
                cohort.Count -= take;
                remaining -= take;
            }

            cohorts.RemoveAll(c => c.Count <= 0);
        }

        private void RemoveEmptyEntries(string partyId)
        {
            if (!_serviceCohorts.TryGetValue(partyId, out var troopDict)) return;
            var removeTroops = new List<string>();
            foreach (var kvp in troopDict)
            {
                kvp.Value.RemoveAll(c => c.Count <= 0);
                if (kvp.Value.Count == 0)
                    removeTroops.Add(kvp.Key);
            }

            foreach (string troopId in removeTroops)
                troopDict.Remove(troopId);

            if (troopDict.Count == 0)
                _serviceCohorts.Remove(partyId);
        }

        private void CleanupStalePartyData()
        {
            var activeIds = new HashSet<string>();
            foreach (MobileParty party in MobileParty.All)
            {
                if (!string.IsNullOrEmpty(party.StringId))
                    activeIds.Add(party.StringId);
            }

            var remove = new List<string>();
            foreach (string partyId in _serviceCohorts.Keys)
            {
                if (!activeIds.Contains(partyId))
                    remove.Add(partyId);
            }

            foreach (string partyId in remove)
                _serviceCohorts.Remove(partyId);
        }

        private static string GetPartyId(MobileParty party)
        {
            return party.StringId ?? string.Empty;
        }

        private static int GetToday()
        {
            return (int)CampaignTime.Now.ToDays;
        }

        private static int GetCount(Dictionary<string, int> dict, string key)
        {
            return dict.TryGetValue(key, out int value) ? value : 0;
        }

        private static int ClampInt(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static string PartyLogName(MobileParty party)
        {
            string id = party.StringId ?? "<no-id>";
            string name = party.Name?.ToString() ?? id;
            return $"{name}({id})";
        }
    }
}