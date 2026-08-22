using Byzantium1071.Campaign.Settings;
using Byzantium1071.Campaign.UI;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.MapEvents;
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
            public int ExtensionCount;
            /// <summary>Settlement this soldier was raised in; empty when it could not be determined.</summary>
            public string HomeId = string.Empty;
        }

        private sealed class TransferReserveEntry
        {
            public int JoinDay;
            public int StoredDay;
            public int Count;
            public int ExtensionCount;
            public string HomeId = string.Empty;
        }

        /// <summary>
        /// One batch of soldiers who finished their term and went home to a settlement.
        /// Grouped by discharge day rather than stored per man, because discharges arrive
        /// in batches and this keeps the save small.
        /// </summary>
        private sealed class VeteranEntry
        {
            public int DischargeDay;
            public int Count;

            /// <summary>
            /// True when the player discharged these men himself. Your own veterans stay
            /// yours to collect wherever you left them, even on foreign ground where the
            /// access setting would otherwise close the register to you.
            /// </summary>
            public bool FromPlayer;
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
            public int ExtensionCount;
            public int MaxExtensions;
            public bool ExtensionsExhausted;
            public bool CanExtend;
            public string HomeId = string.Empty;
            public string HomeName = string.Empty;
            public bool ReturnsHome;
        }

        /// <summary>Read-only row describing veterans available for recall at one settlement.</summary>
        public sealed class VeteranView
        {
            public string SettlementId = string.Empty;
            public string TroopId = string.Empty;
            public CharacterObject Troop = null!;
            public int Count;
            public int Tier;
            public int GoldCostPerMan;
            public int ManpowerCostPerMan;
            public int DaysUntilGone;
            public bool CanRecallOne;
            public string BlockReason = string.Empty;
        }

        private readonly Dictionary<string, Dictionary<string, List<CohortEntry>>> _serviceCohorts
            = new Dictionary<string, Dictionary<string, List<CohortEntry>>>();

        private readonly Dictionary<string, List<string>> _upgradePathCache
            = new Dictionary<string, List<string>>();

        private readonly Dictionary<string, List<TransferReserveEntry>> _transferReserve
            = new Dictionary<string, List<TransferReserveEntry>>();

        // settlementId -> troopId -> discharge batches
        private readonly Dictionary<string, Dictionary<string, List<VeteranEntry>>> _veteranRegister
            = new Dictionary<string, Dictionary<string, List<VeteranEntry>>>();

        private List<string>? _savedPartyIds;
        private List<string>? _savedTroopIds;
        private List<int>? _savedJoinDays;
        private List<int>? _savedCounts;
        private List<bool>? _savedExtendedFlags;
        private List<int>? _savedExtensionCounts;
        private List<string>? _savedHomeIds;

        private List<string>? _savedReserveTroopIds;
        private List<int>? _savedReserveJoinDays;
        private List<int>? _savedReserveStoredDays;
        private List<int>? _savedReserveCounts;
        private List<bool>? _savedReserveExtendedFlags;
        private List<int>? _savedReserveExtensionCounts;
        private List<string>? _savedReserveHomeIds;

        private List<string>? _savedVeteranSettlementIds;
        private List<string>? _savedVeteranTroopIds;
        private List<int>? _savedVeteranDischargeDays;
        private List<int>? _savedVeteranCounts;
        private List<bool>? _savedVeteranFromPlayer;

        private int _lastWarningDay = -1;
        private int _lastWarningEvalDay = -1;
        private int _lastPopupDay = -1;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.OnTroopRecruitedEvent.AddNonSerializedListener(this, OnTroopRecruited);
            CampaignEvents.OnUnitRecruitedEvent.AddNonSerializedListener(this, OnUnitRecruited);
            CampaignEvents.RaidCompletedEvent.AddNonSerializedListener(this, OnRaidCompleted);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
        }

        public override void SyncData(IDataStore dataStore)
        {
            _savedPartyIds ??= new List<string>();
            _savedTroopIds ??= new List<string>();
            _savedJoinDays ??= new List<int>();
            _savedCounts ??= new List<int>();
            _savedExtendedFlags ??= new List<bool>();
            _savedExtensionCounts ??= new List<int>();
            _savedHomeIds ??= new List<string>();
            _savedReserveTroopIds ??= new List<string>();
            _savedReserveJoinDays ??= new List<int>();
            _savedReserveStoredDays ??= new List<int>();
            _savedReserveCounts ??= new List<int>();
            _savedReserveExtendedFlags ??= new List<bool>();
            _savedReserveExtensionCounts ??= new List<int>();
            _savedReserveHomeIds ??= new List<string>();
            _savedVeteranSettlementIds ??= new List<string>();
            _savedVeteranTroopIds ??= new List<string>();
            _savedVeteranDischargeDays ??= new List<int>();
            _savedVeteranCounts ??= new List<int>();
            _savedVeteranFromPlayer ??= new List<bool>();

            if (!dataStore.IsLoading)
            {
                _savedPartyIds.Clear();
                _savedTroopIds.Clear();
                _savedJoinDays.Clear();
                _savedCounts.Clear();
                _savedExtendedFlags.Clear();
                _savedExtensionCounts.Clear();
                _savedHomeIds.Clear();
                _savedReserveTroopIds.Clear();
                _savedReserveJoinDays.Clear();
                _savedReserveStoredDays.Clear();
                _savedReserveCounts.Clear();
                _savedReserveExtendedFlags.Clear();
                _savedReserveExtensionCounts.Clear();
                _savedReserveHomeIds.Clear();
                _savedVeteranSettlementIds.Clear();
                _savedVeteranTroopIds.Clear();
                _savedVeteranDischargeDays.Clear();
                _savedVeteranCounts.Clear();
                _savedVeteranFromPlayer.Clear();

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
                                // The bool list is still written so an older build reading this
                                // save keeps at least the was-extended-at-all fact.
                                _savedExtendedFlags.Add(cohort.ExtensionCount > 0);
                                _savedExtensionCounts.Add(cohort.ExtensionCount);
                                _savedHomeIds.Add(cohort.HomeId ?? string.Empty);
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
                            _savedReserveExtendedFlags.Add(entry.ExtensionCount > 0);
                            _savedReserveExtensionCounts.Add(entry.ExtensionCount);
                            _savedReserveHomeIds.Add(entry.HomeId ?? string.Empty);
                        }
                    }
                }

                foreach (var settlementKvp in _veteranRegister)
                {
                    foreach (var troopKvp in settlementKvp.Value)
                    {
                        foreach (VeteranEntry entry in troopKvp.Value)
                        {
                            if (entry.Count <= 0) continue;
                            _savedVeteranSettlementIds.Add(settlementKvp.Key);
                            _savedVeteranTroopIds.Add(troopKvp.Key);
                            _savedVeteranDischargeDays.Add(entry.DischargeDay);
                            _savedVeteranCounts.Add(entry.Count);
                            _savedVeteranFromPlayer.Add(entry.FromPlayer);
                        }
                    }
                }
            }

            dataStore.SyncData("b1071_demob_partyIds", ref _savedPartyIds);
            dataStore.SyncData("b1071_demob_troopIds", ref _savedTroopIds);
            dataStore.SyncData("b1071_demob_joinDays", ref _savedJoinDays);
            dataStore.SyncData("b1071_demob_counts", ref _savedCounts);
            dataStore.SyncData("b1071_demob_extendedFlags", ref _savedExtendedFlags);
            dataStore.SyncData("b1071_demob_extensionCounts", ref _savedExtensionCounts);
            dataStore.SyncData("b1071_demob_homeIds", ref _savedHomeIds);
            dataStore.SyncData("b1071_demob_reserveTroopIds", ref _savedReserveTroopIds);
            dataStore.SyncData("b1071_demob_reserveJoinDays", ref _savedReserveJoinDays);
            dataStore.SyncData("b1071_demob_reserveStoredDays", ref _savedReserveStoredDays);
            dataStore.SyncData("b1071_demob_reserveCounts", ref _savedReserveCounts);
            dataStore.SyncData("b1071_demob_reserveExtendedFlags", ref _savedReserveExtendedFlags);
            dataStore.SyncData("b1071_demob_reserveExtensionCounts", ref _savedReserveExtensionCounts);
            dataStore.SyncData("b1071_demob_reserveHomeIds", ref _savedReserveHomeIds);
            dataStore.SyncData("b1071_demob_vetSettlementIds", ref _savedVeteranSettlementIds);
            dataStore.SyncData("b1071_demob_vetTroopIds", ref _savedVeteranTroopIds);
            dataStore.SyncData("b1071_demob_vetDischargeDays", ref _savedVeteranDischargeDays);
            dataStore.SyncData("b1071_demob_vetCounts", ref _savedVeteranCounts);
            dataStore.SyncData("b1071_demob_vetFromPlayer", ref _savedVeteranFromPlayer);

            _savedPartyIds ??= new List<string>();
            _savedTroopIds ??= new List<string>();
            _savedJoinDays ??= new List<int>();
            _savedCounts ??= new List<int>();
            _savedExtendedFlags ??= new List<bool>();
            _savedExtensionCounts ??= new List<int>();
            _savedHomeIds ??= new List<string>();
            _savedReserveTroopIds ??= new List<string>();
            _savedReserveJoinDays ??= new List<int>();
            _savedReserveStoredDays ??= new List<int>();
            _savedReserveCounts ??= new List<int>();
            _savedReserveExtendedFlags ??= new List<bool>();
            _savedReserveExtensionCounts ??= new List<int>();
            _savedReserveHomeIds ??= new List<string>();
            _savedVeteranSettlementIds ??= new List<string>();
            _savedVeteranTroopIds ??= new List<string>();
            _savedVeteranDischargeDays ??= new List<int>();
            _savedVeteranCounts ??= new List<int>();
            _savedVeteranFromPlayer ??= new List<bool>();

            if (dataStore.IsLoading)
            {
                _serviceCohorts.Clear();
                _transferReserve.Clear();
                _veteranRegister.Clear();
                int n = Math.Min(_savedPartyIds.Count,
                    Math.Min(_savedTroopIds.Count, Math.Min(_savedJoinDays.Count, _savedCounts.Count)));

                for (int i = 0; i < n; i++)
                {
                    string partyId = _savedPartyIds[i];
                    string troopId = _savedTroopIds[i];
                    int count = _savedCounts[i];
                    // Saves written before v1.0.2.7 only carry the bool, which maps to one extension.
                    int extensionCount = i < _savedExtensionCounts.Count
                        ? Math.Max(0, _savedExtensionCounts[i])
                        : ((i < _savedExtendedFlags.Count && _savedExtendedFlags[i]) ? 1 : 0);
                    string homeId = i < _savedHomeIds.Count ? (_savedHomeIds[i] ?? string.Empty) : string.Empty;
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
                            ExtensionCount = extensionCount,
                            HomeId = homeId
                        });
                    }
                }

                int reserveCount = Math.Min(_savedReserveTroopIds.Count,
                    Math.Min(_savedReserveJoinDays.Count, Math.Min(_savedReserveStoredDays.Count, _savedReserveCounts.Count)));

                for (int i = 0; i < reserveCount; i++)
                {
                    string troopId = _savedReserveTroopIds[i];
                    int count = _savedReserveCounts[i];
                    int extensionCount = i < _savedReserveExtensionCounts.Count
                        ? Math.Max(0, _savedReserveExtensionCounts[i])
                        : ((i < _savedReserveExtendedFlags.Count && _savedReserveExtendedFlags[i]) ? 1 : 0);
                    string homeId = i < _savedReserveHomeIds.Count ? (_savedReserveHomeIds[i] ?? string.Empty) : string.Empty;
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
                            ExtensionCount = extensionCount,
                            HomeId = homeId
                        });
                    }
                }

                int veteranRows = Math.Min(_savedVeteranSettlementIds.Count,
                    Math.Min(_savedVeteranTroopIds.Count,
                        Math.Min(_savedVeteranDischargeDays.Count, _savedVeteranCounts.Count)));

                for (int i = 0; i < veteranRows; i++)
                {
                    string settlementId = _savedVeteranSettlementIds[i];
                    string troopId = _savedVeteranTroopIds[i];
                    int count = _savedVeteranCounts[i];
                    if (string.IsNullOrEmpty(settlementId) || string.IsNullOrEmpty(troopId) || count <= 0) continue;

                    if (!_veteranRegister.TryGetValue(settlementId, out var vetTroopDict))
                    {
                        vetTroopDict = new Dictionary<string, List<VeteranEntry>>();
                        _veteranRegister[settlementId] = vetTroopDict;
                    }

                    if (!vetTroopDict.TryGetValue(troopId, out var vetEntries))
                    {
                        vetEntries = new List<VeteranEntry>();
                        vetTroopDict[troopId] = vetEntries;
                    }

                    // The ownership flag arrived after the register did. Saves written before
                    // it simply have no list here, so those veterans load as nobody's men in
                    // particular and stay behind the ordinary access rules — deliberately, since
                    // guessing would hand the player free veterans all over the map.
                    vetEntries.Add(new VeteranEntry
                    {
                        DischargeDay = _savedVeteranDischargeDays[i],
                        Count = count,
                        FromPlayer = i < _savedVeteranFromPlayer.Count && _savedVeteranFromPlayer[i]
                    });
                }

                B1071_VerboseLog.Log(LogTag, $"Loaded {CountTrackedSoldiers()} tracked soldier service entr{(CountTrackedSoldiers() == 1 ? "y" : "ies")} across {_serviceCohorts.Count} part{(_serviceCohorts.Count == 1 ? "y" : "ies")}; transferReserve={CountReservedSoldiers()}, veterans={CountRegisteredVeterans()} at {_veteranRegister.Count} settlement(s).");
            }
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            Instance = this;
            _lastWarningDay = -1;
            _lastWarningEvalDay = -1;
            _lastPopupDay = -1;
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

                starter.AddGameMenuOption(
                    menu,
                    "b1071_demob_veterans_" + menu,
                    "{B1071_DEMOB_VETERANS_TEXT}",
                    VeteranMenuCondition,
                    VeteranMenuConsequence,
                    isLeave: false,
                    index: 5);
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

        private bool VeteranMenuCondition(MenuCallbackArgs args)
        {
            if (!Settings.EnableDemobilizationSystem || !Settings.EnableDemobilizationVeteranReturn) return false;
            if (MobileParty.MainParty == null) return false;

            Settlement? settlement = Settlement.CurrentSettlement;
            if (settlement == null) return false;

            // Hidden entirely where the player has no standing at all — a hostile town's
            // register is none of his business.
            if (!TryGetPlayerRegisterAccess(settlement, out bool ownMenOnly)) return false;

            int waiting = GetVeteranCountAt(settlement, ownMenOnly);

            // On foreign ground he is here for his own men and nothing else, so the option
            // only appears when some of them are actually waiting. An empty stranger's
            // register would put a dead entry on every friendly menu on the map.
            if (ownMenOnly && waiting <= 0) return false;

            MBTextManager.SetTextVariable("B1071_DEMOB_VETERANS_TEXT", ownMenOnly
                ? new TextObject("{=b1071_demob_menu_veterans_own}Veteran register ({COUNT} of your men here)")
                    .SetTextVariable("COUNT", waiting).ToString()
                : waiting > 0
                    ? new TextObject("{=b1071_demob_menu_veterans}Veteran register ({COUNT} at home)")
                        .SetTextVariable("COUNT", waiting).ToString()
                    : new TextObject("{=b1071_demob_menu_veterans_empty}Veteran register (none at home)").ToString());

            args.optionLeaveType = GameMenuOption.LeaveType.Recruit;
            args.IsEnabled = waiting > 0;
            if (waiting <= 0)
            {
                args.Tooltip = new TextObject("{=b1071_demob_menu_veterans_tip}No veterans are waiting here. Soldiers appear on a settlement's register when they finish their service and go home.");
            }

            return true;
        }

        private void VeteranMenuConsequence(MenuCallbackArgs args)
        {
            Settlement? settlement = Settlement.CurrentSettlement;
            if (settlement == null) return;
            B1071_VeteranRecallScreen.OpenScreen(settlement);
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

                string homeId = recruitmentSettlement?.StringId ?? ResolveHomeIdForParty(party);
                AddFreshCohort(party, troop, amount, GetToday(), "event", homeId);
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

                AddFreshCohort(mainParty, troop, amount, GetToday(), "unit_event", ResolveHomeIdForParty(mainParty));
            }
            catch (Exception ex)
            {
                B1071_VerboseLog.Log(LogTag, $"OnUnitRecruited skipped: {ex.GetType().Name}: {ex.Message}");
            }
        }

        public int RegisterDirectRecruitment(MobileParty party, CharacterObject troop, int amount, string source)
        {
            return RegisterDirectRecruitment(party, troop, amount, source, null);
        }

        /// <summary>
        /// Registers soldiers acquired outside the normal recruitment events.
        /// <paramref name="homeSettlement"/> is where the men were raised: they walk back
        /// there when their service ends. Pass null to fall back to the party's whereabouts.
        /// </summary>
        public int RegisterDirectRecruitment(MobileParty party, CharacterObject troop, int amount, string source, Settlement? homeSettlement)
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

                string homeId = homeSettlement?.StringId ?? ResolveHomeIdForParty(party);
                AddFreshCohort(party, troop, missing, GetToday(), source, homeId);
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

                CleanupVeteranRegister(today);
                ShowMainPartyWarningIfNeeded(today);
                B1071_VerboseLog.Log(LogTag, $"Daily tick day={today}: processedParties={eligibleParties.Count}, trackedSoldiers={CountTrackedSoldiers()}, trackedParties={_serviceCohorts.Count}, transferReserve={CountReservedSoldiers()}, veteransOnRegister={CountRegisteredVeterans()}, registeredSettlements={_veteranRegister.Count}.");
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

            int maxExtensions = GetMaxExtensions();

            foreach (var troopKvp in troopDict)
            {
                CharacterObject? troop = ResolveTroop(troopKvp.Key);
                if (troop == null) continue;

                int threshold = GetServiceThresholdDays(troop, party);

                // Service records are kept one man apiece — that is what makes per-man service
                // age, discharge order and the transfer reserve work — but printing them that
                // way gives a line per soldier. Men who are genuinely interchangeable (same
                // troop, same home, enlisted the same day, same number of extensions) collapse
                // into a single row here, and every button on that row acts across the group.
                var groups = new Dictionary<string, CohortView>(StringComparer.Ordinal);
                var order = new List<string>();

                for (int i = 0; i < troopKvp.Value.Count; i++)
                {
                    CohortEntry cohort = troopKvp.Value[i];
                    if (cohort.Count <= 0) continue;

                    string homeId = cohort.HomeId ?? string.Empty;
                    string key = homeId + "|" + cohort.JoinDay + "|" + cohort.ExtensionCount;
                    if (groups.TryGetValue(key, out CohortView existing))
                    {
                        existing.Count += cohort.Count;
                        continue;
                    }

                    int age = Math.Max(0, today - cohort.JoinDay);
                    int remaining = threshold - age;

                    // Quoted per man, because the buttons let the player take one, five or the
                    // whole row and a single total would be wrong for two of those three.
                    int costPerMan = GetExtensionCost(troop, 1, cohort.ExtensionCount);

                    var view = new CohortView
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
                        ExtendCost = costPerMan,
                        IsWarning = remaining <= Settings.DemobilizationWarningLeadDays,
                        IsOverdue = remaining <= 0,
                        ExtensionCount = cohort.ExtensionCount,
                        MaxExtensions = maxExtensions,
                        ExtensionsExhausted = cohort.ExtensionCount >= maxExtensions,
                        CanExtend = cohort.ExtensionCount < maxExtensions
                            && Hero.MainHero != null
                            && Hero.MainHero.Gold >= costPerMan,
                        HomeId = homeId,
                        HomeName = GetHomeDisplayName(cohort.HomeId),
                        ReturnsHome = Settings.EnableDemobilizationVeteranReturn
                    };

                    groups[key] = view;
                    order.Add(key);
                }

                foreach (string key in order)
                    rows.Add(groups[key]);
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

        /// <summary>
        /// Collects the one-man records behind a single display row, in slot order, so a button
        /// press consumes them predictably instead of in whatever order the list happens to hold.
        /// A row is identified by what the player can actually see of it: which troop, which
        /// home, which enlistment day, how many extensions.
        /// </summary>
        private static List<int> CollectGroupIndices(List<CohortEntry> cohorts, string homeId, int joinDay, int extensionCount)
        {
            var indices = new List<int>();
            string wantedHome = homeId ?? string.Empty;

            for (int i = 0; i < cohorts.Count; i++)
            {
                CohortEntry entry = cohorts[i];
                if (entry.Count <= 0) continue;
                if (entry.JoinDay != joinDay || entry.ExtensionCount != extensionCount) continue;
                if (!string.Equals(entry.HomeId ?? string.Empty, wantedHome, StringComparison.Ordinal)) continue;
                indices.Add(i);
            }

            return indices;
        }

        /// <summary>
        /// Extends service for men on one display row. Every man carries the same fee, so the
        /// bill is the quoted per-man cost times however many were asked for, and a batch the
        /// player cannot fully afford shrinks to what his purse covers rather than failing
        /// outright. Returns how many were actually kept on.
        /// </summary>
        public int TryExtendCohortGroup(string partyId, string troopId, string homeId, int joinDay, int extensionCount, int requested)
        {
            try
            {
                if (!Settings.EnableDemobilizationSystem || requested <= 0) return 0;

                MobileParty? mainParty = MobileParty.MainParty;
                if (mainParty == null || !string.Equals(GetPartyId(mainParty), partyId, StringComparison.Ordinal)) return 0;
                if (!_serviceCohorts.TryGetValue(partyId, out var troopDict)) return 0;
                if (!troopDict.TryGetValue(troopId, out var cohorts)) return 0;

                CharacterObject? troop = ResolveTroop(troopId);
                if (troop == null || Hero.MainHero == null) return 0;

                int maxExtensions = GetMaxExtensions();
                if (extensionCount >= maxExtensions)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TextObject("{=b1071_demob_extend_used}This soldier has already used all {MAX} of his allowed service extensions.")
                            .SetTextVariable("MAX", maxExtensions)
                            .ToString(), Colors.Red));
                    return 0;
                }

                List<int> group = CollectGroupIndices(cohorts, homeId, joinDay, extensionCount);
                int available = 0;
                foreach (int index in group)
                    available += cohorts[index].Count;

                int wanted = Math.Min(requested, available);
                if (wanted <= 0) return 0;

                int costPerMan = GetExtensionCost(troop, 1, extensionCount);
                if (costPerMan > 0)
                    wanted = Math.Min(wanted, Hero.MainHero.Gold / costPerMan);

                if (wanted <= 0)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TextObject("{=b1071_demob_extend_no_gold}Not enough gold to extend service. Need {COST}g.")
                            .SetTextVariable("COST", costPerMan)
                            .ToString(), Colors.Red));
                    return 0;
                }

                // Extend first, charge for what actually happened. Records hold one man each, so
                // the two numbers agree today; billing up front would owe a refund the day that
                // stops being true.
                int extraDays = Math.Max(1, Settings.DemobilizationExtensionDays);
                int applied = 0;
                foreach (int index in group)
                {
                    if (applied >= wanted) break;
                    CohortEntry entry = cohorts[index];
                    if (entry.Count > wanted - applied) break;
                    entry.JoinDay += extraDays;
                    entry.ExtensionCount++;
                    applied += entry.Count;
                }

                if (applied <= 0) return 0;

                int cost = costPerMan * applied;
                if (cost > 0)
                    GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, cost, disableNotification: true);

                B1071_VerboseLog.Log(LogTag, $"Extended service: party={partyId}, troop={troopId}, home={homeId}, joinDay={joinDay}, soldiers={applied}, days={extraDays}, cost={cost}, extension={extensionCount + 1}/{maxExtensions}.");

                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=b1071_demob_extend_done}Extended service for {COUNT} {TROOP} by {DAYS} days for {COST}g. Extension {USED} of {MAX}.")
                        .SetTextVariable("COUNT", applied)
                        .SetTextVariable("TROOP", troop.Name?.ToString() ?? new TextObject("{=b1071_ui_unknown}Unknown").ToString())
                        .SetTextVariable("DAYS", extraDays)
                        .SetTextVariable("COST", cost)
                        .SetTextVariable("USED", extensionCount + 1)
                        .SetTextVariable("MAX", maxExtensions)
                        .ToString(), new Color(0.35f, 0.75f, 0.55f)));

                return applied;
            }
            catch (Exception ex)
            {
                B1071_VerboseLog.Log(LogTag, $"TryExtendCohortGroup failed: {ex.GetType().Name}: {ex.Message}");
                return 0;
            }
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
                int maxExtensions = GetMaxExtensions();
                int cost = GetExtensionCost(troop, cohort.Count, cohort.ExtensionCount);
                if (cost < 0 || Hero.MainHero == null) return false;

                if (cohort.ExtensionCount >= maxExtensions)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TextObject("{=b1071_demob_extend_used}This soldier has already used all {MAX} of his allowed service extensions.")
                            .SetTextVariable("MAX", maxExtensions)
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
                cohort.ExtensionCount++;

                B1071_VerboseLog.Log(LogTag, $"Extended service: party={partyId}, troop={troopId}, entry={cohortIndex}, days={Math.Max(1, Settings.DemobilizationExtensionDays)}, cost={cost}, extension={cohort.ExtensionCount}/{maxExtensions}, newJoinDay={cohort.JoinDay}.");

                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=b1071_demob_extend_done}Extended service for {COUNT} {TROOP} by {DAYS} days for {COST}g. Extension {USED} of {MAX}.")
                        .SetTextVariable("COUNT", cohort.Count)
                        .SetTextVariable("TROOP", troop.Name?.ToString() ?? new TextObject("{=b1071_ui_unknown}Unknown").ToString())
                        .SetTextVariable("DAYS", Math.Max(1, Settings.DemobilizationExtensionDays))
                        .SetTextVariable("COST", cost)
                        .SetTextVariable("USED", cohort.ExtensionCount)
                        .SetTextVariable("MAX", maxExtensions)
                        .ToString(), new Color(0.35f, 0.75f, 0.55f)));

                return true;
            }
            catch (Exception ex)
            {
                B1071_VerboseLog.Log(LogTag, $"TryExtendCohort failed: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Releases men from a tracked service record before their term is up. Destination and
        /// accounting are identical to a term that ran its course — the only difference is that
        /// the player picked the moment. Returns how many actually left.
        /// </summary>
        public int TryDischargeCohort(string partyId, string troopId, string homeId, int joinDay, int extensionCount, int requested)
        {
            try
            {
                if (!Settings.EnableDemobilizationSystem || requested <= 0) return 0;

                MobileParty? mainParty = MobileParty.MainParty;
                if (mainParty == null || !string.Equals(GetPartyId(mainParty), partyId, StringComparison.Ordinal)) return 0;

                // Pulling men out of the roster while a battle or siege is resolving is exactly
                // the kind of mid-action state change that corrupts a party. Make him disengage.
                if (mainParty.MapEvent != null || mainParty.SiegeEvent != null)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TextObject("{=b1071_demob_release_busy}You cannot release soldiers in the middle of a battle.").ToString(), Colors.Red));
                    return 0;
                }

                if (!_serviceCohorts.TryGetValue(partyId, out var troopDict)) return 0;
                if (!troopDict.TryGetValue(troopId, out var cohorts)) return 0;

                CharacterObject? troop = ResolveTroop(troopId);
                if (troop == null) return 0;

                List<int> group = CollectGroupIndices(cohorts, homeId, joinDay, extensionCount);
                int available = 0;
                foreach (int index in group)
                    available += cohorts[index].Count;

                int wanted = Math.Min(requested, available);
                if (wanted <= 0) return 0;

                int today = GetToday();
                int removed = RemoveTroopsFromRoster(mainParty, troop, wanted);
                if (removed <= 0) return 0;

                // Everyone on this row shares a home and an enlistment day, so they all march to
                // the same place and the first record can stand in for the whole batch.
                CohortEntry cohort = cohorts[group[0]];

                // Deliberately exempt from the daily departure caps. Those exist to stop a party
                // bleeding a squad a day behind the player's back; this is his own decision, and
                // throttling it would only make him click the same button again tomorrow.
                Settlement? home = SendVeteranHome(mainParty, troop, cohort, removed, today, out int arrived);

                int toClear = removed;
                foreach (int index in group)
                {
                    if (toClear <= 0) break;
                    int take = Math.Min(cohorts[index].Count, toClear);
                    cohorts[index].Count -= take;
                    toClear -= take;
                }

                RemoveEmptyEntries(partyId);

                string troopName = troop.Name?.ToString() ?? new TextObject("{=b1071_ui_unknown}Unknown").ToString();
                B1071_VerboseLog.Log(LogTag, $"Released early: party={partyId}, troop={troopId}, homeId={homeId}, joinDay={joinDay}, requested={requested}, soldiers={removed}, arrived={arrived}, remainingInGroup={available - removed}, home={home?.StringId ?? "none"}.");

                // Below a 100% return rate not everyone survives the road, so quote the number
                // that actually reached the register rather than the number that marched off.
                TextObject message;
                if (home == null)
                {
                    message = new TextObject("{=b1071_demob_released_gone}Released {COUNT} {TROOP} from service. They left your party.");
                }
                else if (arrived < removed)
                {
                    message = new TextObject("{=b1071_demob_released_partial}Released {COUNT} {TROOP} from service. {ARRIVED} reached {HOME} and can be hired back from its veteran register; the rest did not make it home.")
                        .SetTextVariable("ARRIVED", arrived)
                        .SetTextVariable("HOME", home.Name?.ToString() ?? string.Empty);
                }
                else
                {
                    message = new TextObject("{=b1071_demob_released_home}Released {COUNT} {TROOP} from service. They set off for {HOME} and can be hired back from its veteran register.")
                        .SetTextVariable("HOME", home.Name?.ToString() ?? string.Empty);
                }

                InformationManager.DisplayMessage(new InformationMessage(
                    message
                        .SetTextVariable("COUNT", removed)
                        .SetTextVariable("TROOP", troopName)
                        .ToString(), new Color(0.85f, 0.55f, 0.25f)));

                return removed;
            }
            catch (Exception ex)
            {
                B1071_VerboseLog.Log(LogTag, $"TryDischargeCohort failed: {ex.GetType().Name}: {ex.Message}");
                return 0;
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
                    AddIndividualEntries(cohorts, today, newSoldiers, ResolveHomeIdForParty(party));
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
                    if (cohort.Count <= 0 || cohort.ExtensionCount >= GetMaxExtensions()) continue;

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
                            Cost = GetExtensionCost(troop, 1, cohort.ExtensionCount)
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
                if (candidate.Cohort.Count <= 0 || candidate.Cohort.ExtensionCount >= GetMaxExtensions()) continue;
                if (!CanAiAffordExtension(leader, candidate.Cost)) continue;

                if (candidate.Cost > 0)
                    GiveGoldAction.ApplyBetweenCharacters(leader, null, candidate.Cost, disableNotification: true);

                candidate.Cohort.JoinDay += extensionDays;
                candidate.Cohort.ExtensionCount++;
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
            string firstHomeName = string.Empty;

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

                // The man does not evaporate: he walks home, hands his manpower back to the
                // settlement that raised him, and stays on its register for a while. The daily
                // message says "set off home", which stays true whether or not he arrives.
                Settlement? home = SendVeteranHome(party, candidate.Troop, candidate.Cohort, removed, today, out _);
                if (home != null && string.IsNullOrEmpty(firstHomeName))
                    firstHomeName = home.Name?.ToString() ?? string.Empty;

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
                    string troopLabel = string.IsNullOrEmpty(firstTroopName)
                        ? new TextObject("{=b1071_ui_unknown}Unknown").ToString()
                        : firstTroopName;

                    TextObject message = (Settings.EnableDemobilizationVeteranReturn && !string.IsNullOrEmpty(firstHomeName))
                        ? new TextObject("{=b1071_demob_retired_home}{COUNT} soldier{PLURAL} completed service and set off home. First group: {TROOP}, heading for {HOME}. You can hire veterans back from a settlement's veteran register.")
                            .SetTextVariable("HOME", firstHomeName)
                        : new TextObject("{=b1071_demob_retired_main}{COUNT} soldier{PLURAL} completed service and left your party. First group: {TROOP}.");

                    InformationManager.DisplayMessage(new InformationMessage(
                        message
                            .SetTextVariable("COUNT", retiredTotal)
                            .SetTextVariable("PLURAL", retiredTotal == 1 ? string.Empty : "s")
                            .SetTextVariable("TROOP", troopLabel)
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
            if (_lastWarningEvalDay == today) return;
            if (!Settings.DemobilizationNotifyPlayer && !Settings.DemobilizationWarningPopup) return;
            _lastWarningEvalDay = today;

            List<CohortView> rows = GetMainPartyCohortsForUi();
            CohortView? earliest = null;
            int warningCount = 0;
            int warningMen = 0;
            bool shouldShowPopup = false;
            bool leavingToday = false;
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
                if (row.RemainingDays <= 0)
                    leavingToday = true;
            }

            if (earliest == null) return;

            // The same standing warning repeated every single day is what makes this system
            // feel like nagging. Say it once, then hold off for the configured interval.
            int notifyInterval = Math.Max(1, Settings.DemobilizationNotifyIntervalDays);
            bool messageDue = _lastWarningDay < 0 || today - _lastWarningDay >= notifyInterval;
            // Men leaving today are exempt from the interval. Otherwise a lead-day popup a
            // day or two earlier eats the one warning that still leaves you time to act.
            bool popupDue = leavingToday || _lastPopupDay < 0 || today - _lastPopupDay >= notifyInterval;

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

            if (Settings.DemobilizationNotifyPlayer && messageDue)
            {
                InformationManager.DisplayMessage(new InformationMessage(warning, new Color(0.9f, 0.7f, 0.25f)));
                _lastWarningDay = today;
                sentMessage = true;
            }

            if (Settings.DemobilizationWarningPopup && shouldShowPopup && popupDue)
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
                _lastPopupDay = today;
                sentPopup = true;
            }

            if (sentMessage || sentPopup)
                B1071_VerboseLog.Log(LogTag, $"Player warning issued: soldiers={warningMen}, nearestTroop={troopName}, remaining={earliest.RemainingDays}, message={sentMessage}, popup={sentPopup}.");
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  VETERANS — going home, waiting to be called back
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Best guess at where a party's men were raised, used when a soldier is picked up by
        /// roster reconciliation rather than through a recruitment event: the settlement the
        /// party is standing in, else its leader's clan seat.
        /// </summary>
        private static string ResolveHomeIdForParty(MobileParty? party)
        {
            if (party == null) return string.Empty;

            Settlement? current = party.CurrentSettlement;
            if (current != null && !string.IsNullOrEmpty(current.StringId))
                return current.StringId;

            Settlement? seat = party.LeaderHero?.Clan?.HomeSettlement ?? party.LeaderHero?.HomeSettlement;
            if (seat != null && !string.IsNullOrEmpty(seat.StringId))
                return seat.StringId;

            return string.Empty;
        }

        private static Settlement? ResolveSettlement(string? settlementId)
        {
            if (string.IsNullOrEmpty(settlementId)) return null;
            return MBObjectManager.Instance?.GetObject<Settlement>(settlementId);
        }

        private static string GetHomeDisplayName(string? homeId)
        {
            Settlement? home = ResolveSettlement(homeId);
            return home?.Name?.ToString() ?? string.Empty;
        }

        /// <summary>
        /// Discharges one batch of soldiers back to the settlement that raised them.
        /// Returns the settlement they went to, or null when they simply dispersed
        /// (feature disabled, no known home, or they did not make the journey).
        /// <paramref name="arrived"/> reports how many of <paramref name="count"/> actually
        /// reached the register, which is not the same number once the return rate is
        /// below 100 — a caller that announces the discharge has to quote the real figure.
        /// </summary>
        private Settlement? SendVeteranHome(MobileParty party, CharacterObject troop, CohortEntry cohort, int count, int today, out int arrived)
        {
            arrived = 0;
            if (!Settings.EnableDemobilizationVeteranReturn || count <= 0) return null;

            Settlement? home = ResolveSettlement(cohort.HomeId) ?? ResolveSettlement(ResolveHomeIdForParty(party));
            if (home == null || string.IsNullOrEmpty(home.StringId))
            {
                B1071_VerboseLog.Log(LogTag, $"Discharged soldier had no resolvable home: party={PartyLogName(party)}, troop={troop.StringId}, soldiers={count}.");
                return null;
            }

            // Not everyone makes it back. Rolled per man so the percentage still means
            // something at counts of one, which is how discharges actually arrive.
            int returnPercent = ClampInt(Settings.DemobilizationManpowerReturnPercent, 0, 100);
            for (int i = 0; i < count; i++)
            {
                if (returnPercent >= 100 || MBRandom.RandomInt(100) < returnPercent)
                    arrived++;
            }

            if (arrived <= 0)
            {
                B1071_VerboseLog.Log(LogTag, $"Discharged soldiers did not reach home: party={PartyLogName(party)}, troop={troop.StringId}, soldiers={count}, returnPercent={returnPercent}.");
                return null;
            }

            // Hand the manpower back to the pool that paid to raise these men, at the same
            // price recruitment charged for them. Without this, every completed term is a
            // permanent hole in the map's recruitment economy.
            B1071_ManpowerBehavior.Instance?.ReturnManpowerForTroops(home, troop, arrived);
            AddVeteransToRegister(home.StringId, troop.StringId, arrived, today, IsPlayerParty(party));

            B1071_VerboseLog.Log(LogTag, $"Veterans went home: party={PartyLogName(party)}, troop={troop.StringId}, soldiers={arrived}/{count}, home={home.StringId}, dischargeDay={today}.");
            return home;
        }

        private void AddVeteransToRegister(string settlementId, string troopId, int count, int today, bool fromPlayer)
        {
            if (string.IsNullOrEmpty(settlementId) || string.IsNullOrEmpty(troopId) || count <= 0) return;

            if (!_veteranRegister.TryGetValue(settlementId, out var troopDict))
            {
                troopDict = new Dictionary<string, List<VeteranEntry>>();
                _veteranRegister[settlementId] = troopDict;
            }

            if (!troopDict.TryGetValue(troopId, out var entries))
            {
                entries = new List<VeteranEntry>();
                troopDict[troopId] = entries;
            }

            // Same-day discharges merge into one batch to keep the register compact. Whose
            // men they are is part of the key: the player's veterans must never be folded
            // into a batch he has no claim on, nor lend his claim to somebody else's.
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].DischargeDay == today && entries[i].FromPlayer == fromPlayer)
                {
                    entries[i].Count += count;
                    return;
                }
            }

            entries.Add(new VeteranEntry { DischargeDay = today, Count = count, FromPlayer = fromPlayer });
        }

        /// <summary>Drops veterans who have waited too long; they settle down for good.</summary>
        private void CleanupVeteranRegister(int today)
        {
            int retentionDays = Math.Max(1, Settings.DemobilizationVeteranRetentionDays);
            int aged = 0;

            var settlementIds = new List<string>(_veteranRegister.Keys);
            foreach (string settlementId in settlementIds)
            {
                var troopDict = _veteranRegister[settlementId];
                var troopIds = new List<string>(troopDict.Keys);

                foreach (string troopId in troopIds)
                {
                    var entries = troopDict[troopId];
                    for (int i = entries.Count - 1; i >= 0; i--)
                    {
                        if (entries[i].Count > 0 && today - entries[i].DischargeDay <= retentionDays) continue;
                        aged += Math.Max(0, entries[i].Count);
                        entries.RemoveAt(i);
                    }

                    if (entries.Count == 0)
                        troopDict.Remove(troopId);
                    else
                        entries.Sort((a, b) => a.DischargeDay.CompareTo(b.DischargeDay));
                }

                if (troopDict.Count == 0)
                    _veteranRegister.Remove(settlementId);
            }

            if (aged > 0)
                B1071_VerboseLog.Log(LogTag, $"Veterans aged off registers: soldiers={aged}, retentionDays={retentionDays}, remaining={CountRegisteredVeterans()}.");
        }

        /// <summary>
        /// A sacked or conquered settlement loses part of its veteran register — the men
        /// who were waiting there scatter rather than sitting around for a new employer.
        /// </summary>
        private void ScatterVeteransAt(Settlement? settlement, string reason)
        {
            if (settlement == null || string.IsNullOrEmpty(settlement.StringId)) return;
            if (!_veteranRegister.TryGetValue(settlement.StringId, out var troopDict)) return;

            int scatterPercent = ClampInt(Settings.DemobilizationVeteranScatterPercent, 0, 100);
            if (scatterPercent <= 0) return;

            int scattered = 0;
            var troopIds = new List<string>(troopDict.Keys);
            foreach (string troopId in troopIds)
            {
                var entries = troopDict[troopId];
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    int lost = entries[i].Count * scatterPercent / 100;
                    // A partial man still counts as a real risk of losing the last holdout.
                    if (lost <= 0 && MBRandom.RandomInt(100) < scatterPercent)
                        lost = 1;

                    lost = Math.Min(lost, entries[i].Count);
                    if (lost <= 0) continue;

                    entries[i].Count -= lost;
                    scattered += lost;
                    if (entries[i].Count <= 0)
                        entries.RemoveAt(i);
                }

                if (entries.Count == 0)
                    troopDict.Remove(troopId);
            }

            if (troopDict.Count == 0)
                _veteranRegister.Remove(settlement.StringId);

            if (scattered > 0)
                B1071_VerboseLog.Log(LogTag, $"Veterans scattered: settlement={settlement.StringId}, reason={reason}, soldiers={scattered}, percent={scatterPercent}.");
        }

        private void OnRaidCompleted(BattleSideEnum winnerSide, RaidEventComponent raidComponent)
        {
            try
            {
                if (!Settings.EnableDemobilizationSystem || !Settings.EnableDemobilizationVeteranReturn) return;
                if (winnerSide != BattleSideEnum.Attacker) return;
                ScatterVeteransAt(raidComponent?.MapEventSettlement, "raid");
            }
            catch (Exception ex)
            {
                B1071_VerboseLog.Log(LogTag, $"OnRaidCompleted skipped: {ex.GetType().Name}: {ex.Message}");
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
            try
            {
                if (!Settings.EnableDemobilizationSystem || !Settings.EnableDemobilizationVeteranReturn) return;
                if (settlement == null) return;

                // Peaceful fief transfers inside one realm do not scatter anyone. MapFaction
                // rather than Kingdom deliberately: a clan outside a kingdom — an independent
                // lord, a rebel clan, a minor faction — has a null Kingdom, and testing that
                // silently skipped every conquest involving one. MapFaction returns the kingdom
                // for a clan that has one and the clan itself otherwise, so the same-realm test
                // still behaves exactly as before for ordinary kingdom-to-kingdom transfers.
                IFaction? oldFaction = oldOwner?.Clan?.MapFaction;
                IFaction? newFaction = newOwner?.Clan?.MapFaction;
                if (oldFaction == null || newFaction == null || oldFaction == newFaction) return;

                ScatterVeteransAt(settlement, "conquest");

                // Villages bound to a captured town lose their waiting veterans too.
                if (settlement.IsTown || settlement.IsCastle)
                {
                    foreach (Village village in settlement.BoundVillages)
                        ScatterVeteransAt(village?.Settlement, "conquest_bound");
                }
            }
            catch (Exception ex)
            {
                B1071_VerboseLog.Log(LogTag, $"OnSettlementOwnerChanged skipped: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private int CountRegisteredVeterans()
        {
            int total = 0;
            foreach (var settlementKvp in _veteranRegister)
            {
                foreach (var troopKvp in settlementKvp.Value)
                {
                    foreach (VeteranEntry entry in troopKvp.Value)
                        total += Math.Max(0, entry.Count);
                }
            }

            return total;
        }

        // ── Recall ────────────────────────────────────────────────────────────────

        /// <summary>Whether the player may hire <em>anyone</em> from the register at this settlement.</summary>
        public static bool CanPlayerAccessVeteranRegister(Settlement? settlement)
        {
            return CanFactionAccessVeteranRegister(Clan.PlayerClan?.MapFaction, Clan.PlayerClan, settlement);
        }

        /// <summary>
        /// Whether the player may open this settlement's register at all, and on what terms.
        /// Men he enlisted and discharged himself are always his to collect — the whole point
        /// of sending them home is being able to fetch them again, and a soldier raised at a
        /// castle that later fell outside your realm would otherwise be lost for good. Other
        /// lords' countrymen still obey the access setting, and war closes the gates entirely.
        /// <paramref name="ownMenOnly"/> is true when he may take back only his own veterans.
        /// </summary>
        public static bool TryGetPlayerRegisterAccess(Settlement? settlement, out bool ownMenOnly)
        {
            ownMenOnly = false;
            if (settlement == null) return false;

            Clan? owner = settlement.OwnerClan;
            if (owner == null) return false;

            IFaction? playerFaction = Clan.PlayerClan?.MapFaction;
            IFaction? ownerFaction = owner.MapFaction;
            if (playerFaction != null && ownerFaction != null
                && FactionManager.IsAtWarAgainstFaction(playerFaction, ownerFaction)) return false;

            if (CanFactionAccessVeteranRegister(playerFaction, Clan.PlayerClan, settlement)) return true;

            ownMenOnly = true;
            return true;
        }

        /// <summary>True when the men this party discharges count as the player's own.</summary>
        private static bool IsPlayerParty(MobileParty? party)
        {
            if (party == null) return false;
            if (party == MobileParty.MainParty) return true;
            Clan? playerClan = Clan.PlayerClan;
            return playerClan != null && party.ActualClan == playerClan;
        }

        private static bool CanFactionAccessVeteranRegister(IFaction? recruiterFaction, Clan? recruiterClan, Settlement? settlement)
        {
            if (settlement == null) return false;

            Clan? owner = settlement.OwnerClan;
            if (owner == null) return false;

            // The owning clan can always call on its own countrymen.
            if (recruiterClan != null && recruiterClan == owner) return true;

            IFaction? ownerFaction = owner.MapFaction;
            if (recruiterFaction == null || ownerFaction == null) return false;
            if (FactionManager.IsAtWarAgainstFaction(recruiterFaction, ownerFaction)) return false;

            switch (Settings.DemobilizationVeteranRecallAccess)
            {
                case 0: return true;                                 // Open — any non-hostile lord
                case 2: return false;                                // Clan only — handled above
                default: return recruiterFaction == ownerFaction;    // Kingdom only
            }
        }

        private static int GetRecallGoldCost(CharacterObject troop, int count)
        {
            int tier = ClampInt(troop.Tier, 1, 6);
            int perTier = Math.Max(0, Settings.DemobilizationRecallGoldPerTier);
            return Math.Max(0, count * tier * perTier);
        }

        public int GetVeteranCountAt(Settlement? settlement) => GetVeteranCountAt(settlement, false);

        /// <summary>
        /// Men waiting at this settlement. With <paramref name="ownMenOnly"/> the count covers
        /// just the player's own discharged soldiers, which is all he may take on foreign ground.
        /// </summary>
        public int GetVeteranCountAt(Settlement? settlement, bool ownMenOnly)
        {
            if (settlement == null || string.IsNullOrEmpty(settlement.StringId)) return 0;
            if (!_veteranRegister.TryGetValue(settlement.StringId, out var troopDict)) return 0;

            int total = 0;
            foreach (var troopKvp in troopDict)
            {
                foreach (VeteranEntry entry in troopKvp.Value)
                {
                    if (ownMenOnly && !entry.FromPlayer) continue;
                    total += Math.Max(0, entry.Count);
                }
            }

            return total;
        }

        /// <summary>Read-only snapshot of the veterans waiting at one settlement, for the recall screen.</summary>
        public List<VeteranView> GetVeteransForUi(Settlement? settlement)
        {
            var rows = new List<VeteranView>();
            if (settlement == null || string.IsNullOrEmpty(settlement.StringId)) return rows;

            int today = GetToday();
            CleanupVeteranRegister(today);
            if (!_veteranRegister.TryGetValue(settlement.StringId, out var troopDict)) return rows;

            int retentionDays = Math.Max(1, Settings.DemobilizationVeteranRetentionDays);
            bool hasAccess = TryGetPlayerRegisterAccess(settlement, out bool ownMenOnly);
            MobileParty? mainParty = MobileParty.MainParty;

            foreach (var troopKvp in troopDict)
            {
                CharacterObject? troop = ResolveTroop(troopKvp.Key);
                if (troop == null) continue;

                // On foreign ground the screen shows only the player's own veterans. Listing
                // the local lord's men he cannot touch would just be a wall of blocked rows.
                int count = 0;
                int oldestDischargeDay = int.MaxValue;
                foreach (VeteranEntry entry in troopKvp.Value)
                {
                    if (entry.Count <= 0) continue;
                    if (ownMenOnly && !entry.FromPlayer) continue;
                    count += entry.Count;
                    if (entry.DischargeDay < oldestDischargeDay)
                        oldestDischargeDay = entry.DischargeDay;
                }

                if (count <= 0) continue;

                int goldPerMan = GetRecallGoldCost(troop, 1);
                int manpowerPerMan = 1;
                if (B1071_ManpowerBehavior.Instance != null)
                    manpowerPerMan = Math.Max(1, B1071_ManpowerBehavior.Instance.GetManpowerChargePerTroop(troop));

                string blockReason = string.Empty;
                bool canRecallOne = true;

                if (!Settings.EnableDemobilizationVeteranReturn)
                {
                    canRecallOne = false;
                    blockReason = new TextObject("{=b1071_recall_block_disabled}Veteran return is switched off in the mod settings.").ToString();
                }
                else if (!hasAccess)
                {
                    canRecallOne = false;
                    blockReason = new TextObject("{=b1071_recall_block_access}You are not entitled to hire from this settlement's register.").ToString();
                }
                else if (Hero.MainHero == null || Hero.MainHero.Gold < goldPerMan)
                {
                    canRecallOne = false;
                    blockReason = new TextObject("{=b1071_recall_block_gold}Not enough gold: {COST}g needed per man.")
                        .SetTextVariable("COST", goldPerMan).ToString();
                }
                else if (!HasPartyRoomForOne(mainParty))
                {
                    canRecallOne = false;
                    blockReason = new TextObject("{=b1071_recall_block_party}Your party is full.").ToString();
                }
                else if (!HasManpowerForOne(settlement, mainParty, troop, out int available))
                {
                    canRecallOne = false;
                    blockReason = new TextObject("{=b1071_recall_block_manpower}Not enough manpower here: {COST} needed, {LEFT} left.")
                        .SetTextVariable("COST", manpowerPerMan)
                        .SetTextVariable("LEFT", available).ToString();
                }

                rows.Add(new VeteranView
                {
                    SettlementId = settlement.StringId,
                    TroopId = troopKvp.Key,
                    Troop = troop,
                    Count = count,
                    Tier = troop.Tier,
                    GoldCostPerMan = goldPerMan,
                    ManpowerCostPerMan = manpowerPerMan,
                    DaysUntilGone = oldestDischargeDay == int.MaxValue
                        ? retentionDays
                        : Math.Max(0, retentionDays - (today - oldestDischargeDay)),
                    CanRecallOne = canRecallOne,
                    BlockReason = blockReason
                });
            }

            rows.Sort((a, b) =>
            {
                int compare = b.Tier.CompareTo(a.Tier);
                if (compare != 0) return compare;
                compare = a.DaysUntilGone.CompareTo(b.DaysUntilGone);
                if (compare != 0) return compare;
                return string.Compare(a.Troop.Name?.ToString(), b.Troop.Name?.ToString(), StringComparison.Ordinal);
            });

            return rows;
        }

        private static bool HasPartyRoomForOne(MobileParty? party)
        {
            if (party == null) return false;
            return party.MemberRoster.TotalManCount < party.Party.PartySizeLimit;
        }

        private static bool HasManpowerForOne(Settlement settlement, MobileParty? party, CharacterObject troop, out int available)
        {
            available = 0;
            B1071_ManpowerBehavior? manpower = B1071_ManpowerBehavior.Instance;
            if (manpower == null || party == null) return true;

            return manpower.CanRecruitCountForPlayer(settlement, party, troop, 1, out available, out _, out _);
        }

        /// <summary>
        /// Hires <paramref name="requested"/> veterans of one troop type back into the main party.
        /// Charges the re-enlistment bounty and draws manpower exactly like ordinary recruitment,
        /// then starts their service clock fresh with this settlement as their home.
        /// Returns how many actually re-enlisted.
        /// </summary>
        public int TryRecallVeterans(Settlement? settlement, CharacterObject? troop, int requested)
        {
            try
            {
                if (!Settings.EnableDemobilizationSystem || !Settings.EnableDemobilizationVeteranReturn) return 0;
                if (settlement == null || troop == null || requested <= 0) return 0;
                if (Hero.MainHero == null) return 0;

                MobileParty? party = MobileParty.MainParty;
                if (party == null || party.MemberRoster == null) return 0;

                if (!TryGetPlayerRegisterAccess(settlement, out bool ownMenOnly))
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TextObject("{=b1071_recall_no_access}You may not hire from the veteran register at {SETTLEMENT}.")
                            .SetTextVariable("SETTLEMENT", settlement.Name?.ToString() ?? string.Empty)
                            .ToString(), Colors.Red));
                    return 0;
                }

                int today = GetToday();
                CleanupVeteranRegister(today);

                if (!_veteranRegister.TryGetValue(settlement.StringId, out var troopDict)) return 0;
                if (!troopDict.TryGetValue(troop.StringId, out var entries)) return 0;

                int registered = 0;
                foreach (VeteranEntry entry in entries)
                {
                    if (ownMenOnly && !entry.FromPlayer) continue;
                    registered += Math.Max(0, entry.Count);
                }
                if (registered <= 0) return 0;

                int wanted = Math.Min(requested, registered);

                // Trim to what the party can hold.
                int room = Math.Max(0, party.Party.PartySizeLimit - party.MemberRoster.TotalManCount);
                if (room <= 0)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TextObject("{=b1071_recall_party_full}Your party is full.").ToString(), Colors.Red));
                    return 0;
                }
                wanted = Math.Min(wanted, room);

                // Trim to what the purse allows.
                int goldPerMan = GetRecallGoldCost(troop, 1);
                if (goldPerMan > 0)
                    wanted = Math.Min(wanted, Hero.MainHero.Gold / goldPerMan);

                if (wanted <= 0)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TextObject("{=b1071_recall_no_gold}Not enough gold to call anyone back: {COST}g per man.")
                            .SetTextVariable("COST", goldPerMan)
                            .ToString(), Colors.Red));
                    return 0;
                }

                // Trim to what the settlement's manpower pool can cover.
                B1071_ManpowerBehavior? manpower = B1071_ManpowerBehavior.Instance;
                if (manpower != null)
                {
                    while (wanted > 0 && !manpower.CanRecruitCountForPlayer(
                               settlement, party, troop, wanted, out int available, out int costPer, out _))
                    {
                        int affordable = costPer > 0 ? available / costPer : 0;
                        if (affordable >= wanted || affordable <= 0)
                        {
                            if (affordable <= 0)
                            {
                                InformationManager.DisplayMessage(new InformationMessage(
                                    new TextObject("{=b1071_recall_no_manpower}{SETTLEMENT} has no manpower to spare for veterans.")
                                        .SetTextVariable("SETTLEMENT", settlement.Name?.ToString() ?? string.Empty)
                                        .ToString(), Colors.Red));
                                return 0;
                            }
                            break;
                        }
                        wanted = affordable;
                    }
                }

                if (wanted <= 0) return 0;

                // Execute.
                int goldCost = GetRecallGoldCost(troop, wanted);
                if (goldCost > 0)
                {
                    // The bounty is paid to the veterans themselves, not to the fief holder.
                    // Paying the owner nets to nothing at your own settlements — where most of
                    // your veterans are — so the price would be quoted and gated on, then never
                    // actually charged. Same sink idiom as the service extension fee.
                    GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, goldCost, disableNotification: true);
                }

                manpower?.ConsumeManpowerPublic(settlement, troop, wanted);
                party.MemberRoster.AddToCounts(troop, wanted);
                RemoveVeteransFromRegister(settlement.StringId, troop.StringId, wanted, ownMenOnly);

                // Their term starts over, and this settlement is now formally their home.
                AddFreshCohort(party, troop, wanted, today, "veteran_recall", settlement.StringId);

                B1071_VerboseLog.Log(LogTag, $"Veterans recalled: settlement={settlement.StringId}, troop={troop.StringId}, soldiers={wanted}, gold={goldCost}, remainingAtSettlement={GetVeteranCountAt(settlement)}.");

                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=b1071_recall_done}{COUNT} {TROOP} answered the call at {SETTLEMENT} for {COST}g.")
                        .SetTextVariable("COUNT", wanted)
                        .SetTextVariable("TROOP", troop.Name?.ToString() ?? new TextObject("{=b1071_ui_unknown}Unknown").ToString())
                        .SetTextVariable("SETTLEMENT", settlement.Name?.ToString() ?? string.Empty)
                        .SetTextVariable("COST", goldCost)
                        .ToString(), new Color(0.35f, 0.75f, 0.55f)));

                return wanted;
            }
            catch (Exception ex)
            {
                B1071_VerboseLog.Log(LogTag, $"TryRecallVeterans failed: {ex.GetType().Name}: {ex.Message}");
                return 0;
            }
        }

        /// <summary>Removes recalled men, longest-waiting first.</summary>
        private void RemoveVeteransFromRegister(string settlementId, string troopId, int count, bool ownMenOnly)
        {
            if (count <= 0 || !_veteranRegister.TryGetValue(settlementId, out var troopDict)) return;
            if (!troopDict.TryGetValue(troopId, out var entries)) return;

            entries.Sort((a, b) => a.DischargeDay.CompareTo(b.DischargeDay));

            // Draw from the same batches the count was taken from, or a recall on foreign
            // ground would quietly pocket the local lord's veterans instead of the player's.
            int remaining = count;
            for (int i = 0; i < entries.Count && remaining > 0; i++)
            {
                if (ownMenOnly && !entries[i].FromPlayer) continue;
                int take = Math.Min(entries[i].Count, remaining);
                entries[i].Count -= take;
                remaining -= take;
            }

            entries.RemoveAll(e => e.Count <= 0);
            if (entries.Count == 0)
                troopDict.Remove(troopId);
            if (troopDict.Count == 0)
                _veteranRegister.Remove(settlementId);
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

        private void AddFreshCohort(MobileParty party, CharacterObject troop, int amount, int today, string source, string homeId)
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

            AddIndividualEntries(cohorts, today, amount, homeId);
            B1071_VerboseLog.Log(LogTag, $"Fresh service soldiers registered: source={source}, party={PartyLogName(party)}, troop={troop.StringId}, soldiers={amount}, joinDay={today}, home={(string.IsNullOrEmpty(homeId) ? "<unknown>" : homeId)}.");
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
                    case 1: return 63;
                    case 2: return 84;
                    case 3: return 126;
                    case 4: return 168;
                    case 5: return 252;
                    default: return 336;
                }
            }

            if (preset == 2)
            {
                switch (tier)
                {
                    case 1: return 28;
                    case 2: return 42;
                    case 3: return 56;
                    case 4: return 84;
                    case 5: return 112;
                    default: return 168;
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
                case 1: return 42;
                case 2: return 63;
                case 3: return 84;
                case 4: return 126;
                case 5: return 168;
                default: return 252;
            }
        }

        private static int GetMaxExtensions()
        {
            return Math.Max(1, Settings.DemobilizationMaxExtensions);
        }

        /// <summary>
        /// Gold to extend <paramref name="count"/> soldiers once more.
        /// <paramref name="alreadyExtended"/> is how many extensions they have already had;
        /// every repeat costs 50% more than the one before, so retention gets steadily
        /// harder rather than being a single all-or-nothing purchase.
        /// </summary>
        private int GetExtensionCost(CharacterObject troop, int count, int alreadyExtended)
        {
            int tier = ClampInt(troop.Tier, 1, 6);
            int days = Math.Max(1, Settings.DemobilizationExtensionDays);
            int costPerTierDay = Math.Max(0, Settings.DemobilizationExtensionGoldPerTierDay);
            long baseCost = (long)count * tier * days * costPerTierDay;
            long scaled = baseCost * (2 + Math.Max(0, alreadyExtended)) / 2;
            return (int)Math.Max(0, Math.Min(int.MaxValue, scaled));
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
                    AddIndividualEntries(split, entry.JoinDay, entry.Count, entry.HomeId, entry.ExtensionCount);

                troopDict[troopId] = split;
            }
        }

        private static void AddIndividualEntries(List<CohortEntry> entries, int joinDay, int count, string homeId, int extensionCount = 0)
        {
            for (int i = 0; i < count; i++)
                entries.Add(new CohortEntry
                {
                    JoinDay = joinDay,
                    Count = 1,
                    ExtensionCount = extensionCount,
                    HomeId = homeId ?? string.Empty
                });
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
                    ExtensionCount = cohort.ExtensionCount,
                    HomeId = cohort.HomeId
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
                AddIndividualEntries(cohorts, entry.JoinDay, take, entry.HomeId, entry.ExtensionCount);
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
                AddIndividualEntries(toList, adjustedJoinDay, take, source.HomeId, source.ExtensionCount);
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