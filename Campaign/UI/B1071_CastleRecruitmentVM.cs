using Byzantium1071.Campaign.Behaviors;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.UI
{
    /// <summary>
    /// ViewModel for the castle recruitment screen.
    /// Displays three lists:
    ///   - EliteTroops: Culture-based T4-T6 troops from the castle's elite pool.
    ///   - AvailableTroops: T4+ prisoners that have served their waiting period (recruitable).
    ///   - PendingTroops: T4+ prisoners still waiting for loyalty conversion.
    /// Also shows the castle name, player gold, and castle manpower.
    /// </summary>
    public sealed class B1071_CastleRecruitmentVM : ViewModel
    {
        private readonly Settlement _castle;
        private Action? _onClose;

        private string _titleText = string.Empty;
        private string _goldText = string.Empty;
        private string _manpowerText = string.Empty;
        private string _goldLabelText = string.Empty;
        private string _manpowerLabelText = string.Empty;
        private string _eliteHeaderText = string.Empty;
        private string _readyHeaderText = string.Empty;
        private string _pendingHeaderText = string.Empty;
        private string _troopColumnText = string.Empty;
        private string _tierColumnText = string.Empty;
        private string _countColumnText = string.Empty;
        private string _goldCostColumnText = string.Empty;
        private string _statusColumnText = string.Empty;
        private string _noEliteText = string.Empty;
        private string _noReadyText = string.Empty;
        private string _noPendingText = string.Empty;
        private string _recruitAllText = string.Empty;
        private MBBindingList<B1071_CastleRecruitTroopVM> _eliteTroops;
        private MBBindingList<B1071_CastleRecruitTroopVM> _availableTroops;
        private MBBindingList<B1071_CastleRecruitTroopVM> _pendingTroops;
        private bool _hasEliteTroops;
        private bool _hasAvailableTroops;
        private bool _hasPendingTroops;
        private bool _hasNoEliteTroops = true;
        private bool _hasNoAvailableTroops = true;
        private bool _hasNoPendingTroops = true;

        public B1071_CastleRecruitmentVM(Settlement castle, Action? onClose)
        {
            _castle = castle;
            _onClose = onClose;
            _eliteTroops = new MBBindingList<B1071_CastleRecruitTroopVM>();
            _availableTroops = new MBBindingList<B1071_CastleRecruitTroopVM>();
            _pendingTroops = new MBBindingList<B1071_CastleRecruitTroopVM>();

            RefreshLocalizedLabels();
            RefreshLists();
        }

        private static string L(string id, string fallback)
        {
            return new TextObject($"{{={id}}}{fallback}").ToString();
        }

        private void RefreshLocalizedLabels()
        {
            GoldLabelText = L("b1071_ui_gold", "Gold:");
            ManpowerLabelText = L("b1071_ui_manpower", "Manpower:");
            EliteHeaderText = L("b1071_cr_elite_header", "Elite Troops (Culture Pool)");
            ReadyHeaderText = L("b1071_cr_ready_header", "Ready to Recruit (Prisoners)");
            PendingHeaderText = L("b1071_cr_pending_header", "Pending (Training)");
            TroopColumnText = L("b1071_ui_troop", "Troop");
            TierColumnText = L("b1071_ui_tier", "Tier");
            CountColumnText = L("b1071_ui_count", "Count");
            GoldCostColumnText = L("b1071_ui_gold_cost", "Gold Cost");
            StatusColumnText = L("b1071_ui_status", "Status");
            NoEliteText = L("b1071_cr_no_elite", "No elite troops available. Pool regenerates daily from castle manpower.");
            NoReadyText = L("b1071_cr_no_ready", "No prisoners ready for recruitment.");
            NoPendingText = L("b1071_cr_no_pending", "No prisoners pending.");
            RecruitAllText = L("b1071_cr_recruit_all", "Recruit All");
        }

        /// <summary>
        /// Rebuilds both lists from current prison state. Called after each recruitment.
        /// </summary>
        public void RefreshLists()
        {
            _eliteTroops.Clear();
            _availableTroops.Clear();
            _pendingTroops.Clear();

            var behavior = B1071_CastleRecruitmentBehavior.Instance;
            if (behavior == null) return;

            // Title
            TitleText = $"{L("b1071_cr_title", "Castle Recruitment")} \u2014 {_castle.Name}";

            // Gold
            GoldText = $"{Hero.MainHero.Gold:N0}";

            // Manpower
            var manpowerBehavior = B1071_ManpowerBehavior.Instance;
            if (manpowerBehavior != null)
            {
                manpowerBehavior.GetManpowerPool(_castle, out int cur, out int max, out _);
                ManpowerText = $"{cur:N0} / {max:N0}";
            }
            else
            {
                ManpowerText = L("b1071_ui_na", "N/A");
            }

            // Elite pool troops (culture-based T4-T6)
            var elites = behavior.GetElitePoolTroops(_castle);
            foreach (var (troop, count, goldCost) in elites)
            {
                _eliteTroops.Add(new B1071_CastleRecruitTroopVM(
                    this,
                    troop,
                    _castle,
                    count,
                    goldCost,
                    isElite: true));
            }

            // Recruitable (ready) prisoners
            var recruitable = behavior.GetRecruitablePrisoners(_castle);
            foreach (var (troop, count, daysHeld, goldCost) in recruitable)
            {
                _availableTroops.Add(new B1071_CastleRecruitTroopVM(
                    this,
                    troop,
                    _castle,
                    count,
                    daysHeld,
                    goldCost,
                    isReady: true,
                    daysRequired: behavior.GetRequiredDaysForTier(troop.Tier)));
            }

            // Pending (not yet ready) prisoners
            var pending = behavior.GetPendingPrisoners(_castle);
            foreach (var (troop, count, daysHeld, daysRequired) in pending)
            {
                _pendingTroops.Add(new B1071_CastleRecruitTroopVM(
                    this,
                    troop,
                    _castle,
                    count,
                    daysHeld,
                    goldCost: behavior.GetGoldCostForTier(troop.Tier),
                    isReady: false,
                    daysRequired: daysRequired));
            }

            HasEliteTroops = _eliteTroops.Count > 0;
            HasNoEliteTroops = _eliteTroops.Count == 0;
            HasAvailableTroops = _availableTroops.Count > 0;
            HasNoAvailableTroops = _availableTroops.Count == 0;
            HasPendingTroops = _pendingTroops.Count > 0;
            HasNoPendingTroops = _pendingTroops.Count == 0;
        }

        // ── Data-bound properties ─────────────────────────────────────────────────

        [DataSourceProperty]
        public string TitleText
        {
            get => _titleText;
            set { if (_titleText != value) { _titleText = value; OnPropertyChangedWithValue(value, nameof(TitleText)); } }
        }

        [DataSourceProperty]
        public string GoldText
        {
            get => _goldText;
            set { if (_goldText != value) { _goldText = value; OnPropertyChangedWithValue(value, nameof(GoldText)); } }
        }

        [DataSourceProperty]
        public string ManpowerText
        {
            get => _manpowerText;
            set { if (_manpowerText != value) { _manpowerText = value; OnPropertyChangedWithValue(value, nameof(ManpowerText)); } }
        }

        [DataSourceProperty]
        public string GoldLabelText
        {
            get => _goldLabelText;
            set { if (_goldLabelText != value) { _goldLabelText = value; OnPropertyChangedWithValue(value, nameof(GoldLabelText)); } }
        }

        [DataSourceProperty]
        public string ManpowerLabelText
        {
            get => _manpowerLabelText;
            set { if (_manpowerLabelText != value) { _manpowerLabelText = value; OnPropertyChangedWithValue(value, nameof(ManpowerLabelText)); } }
        }

        [DataSourceProperty]
        public string EliteHeaderText
        {
            get => _eliteHeaderText;
            set { if (_eliteHeaderText != value) { _eliteHeaderText = value; OnPropertyChangedWithValue(value, nameof(EliteHeaderText)); } }
        }

        [DataSourceProperty]
        public string ReadyHeaderText
        {
            get => _readyHeaderText;
            set { if (_readyHeaderText != value) { _readyHeaderText = value; OnPropertyChangedWithValue(value, nameof(ReadyHeaderText)); } }
        }

        [DataSourceProperty]
        public string PendingHeaderText
        {
            get => _pendingHeaderText;
            set { if (_pendingHeaderText != value) { _pendingHeaderText = value; OnPropertyChangedWithValue(value, nameof(PendingHeaderText)); } }
        }

        [DataSourceProperty]
        public string TroopColumnText
        {
            get => _troopColumnText;
            set { if (_troopColumnText != value) { _troopColumnText = value; OnPropertyChangedWithValue(value, nameof(TroopColumnText)); } }
        }

        [DataSourceProperty]
        public string TierColumnText
        {
            get => _tierColumnText;
            set { if (_tierColumnText != value) { _tierColumnText = value; OnPropertyChangedWithValue(value, nameof(TierColumnText)); } }
        }

        [DataSourceProperty]
        public string CountColumnText
        {
            get => _countColumnText;
            set { if (_countColumnText != value) { _countColumnText = value; OnPropertyChangedWithValue(value, nameof(CountColumnText)); } }
        }

        [DataSourceProperty]
        public string GoldCostColumnText
        {
            get => _goldCostColumnText;
            set { if (_goldCostColumnText != value) { _goldCostColumnText = value; OnPropertyChangedWithValue(value, nameof(GoldCostColumnText)); } }
        }

        [DataSourceProperty]
        public string StatusColumnText
        {
            get => _statusColumnText;
            set { if (_statusColumnText != value) { _statusColumnText = value; OnPropertyChangedWithValue(value, nameof(StatusColumnText)); } }
        }

        [DataSourceProperty]
        public string NoEliteText
        {
            get => _noEliteText;
            set { if (_noEliteText != value) { _noEliteText = value; OnPropertyChangedWithValue(value, nameof(NoEliteText)); } }
        }

        [DataSourceProperty]
        public string NoReadyText
        {
            get => _noReadyText;
            set { if (_noReadyText != value) { _noReadyText = value; OnPropertyChangedWithValue(value, nameof(NoReadyText)); } }
        }

        [DataSourceProperty]
        public string NoPendingText
        {
            get => _noPendingText;
            set { if (_noPendingText != value) { _noPendingText = value; OnPropertyChangedWithValue(value, nameof(NoPendingText)); } }
        }

        [DataSourceProperty]
        public string RecruitAllText
        {
            get => _recruitAllText;
            set { if (_recruitAllText != value) { _recruitAllText = value; OnPropertyChangedWithValue(value, nameof(RecruitAllText)); } }
        }

        [DataSourceProperty]
        public MBBindingList<B1071_CastleRecruitTroopVM> EliteTroops
        {
            get => _eliteTroops;
            set { if (_eliteTroops != value) { _eliteTroops = value; OnPropertyChangedWithValue(value, nameof(EliteTroops)); } }
        }

        [DataSourceProperty]
        public MBBindingList<B1071_CastleRecruitTroopVM> AvailableTroops
        {
            get => _availableTroops;
            set { if (_availableTroops != value) { _availableTroops = value; OnPropertyChangedWithValue(value, nameof(AvailableTroops)); } }
        }

        [DataSourceProperty]
        public MBBindingList<B1071_CastleRecruitTroopVM> PendingTroops
        {
            get => _pendingTroops;
            set { if (_pendingTroops != value) { _pendingTroops = value; OnPropertyChangedWithValue(value, nameof(PendingTroops)); } }
        }

        [DataSourceProperty]
        public bool HasEliteTroops
        {
            get => _hasEliteTroops;
            set { if (_hasEliteTroops != value) { _hasEliteTroops = value; OnPropertyChangedWithValue(value, nameof(HasEliteTroops)); } }
        }

        [DataSourceProperty]
        public bool HasAvailableTroops
        {
            get => _hasAvailableTroops;
            set { if (_hasAvailableTroops != value) { _hasAvailableTroops = value; OnPropertyChangedWithValue(value, nameof(HasAvailableTroops)); } }
        }

        [DataSourceProperty]
        public bool HasPendingTroops
        {
            get => _hasPendingTroops;
            set { if (_hasPendingTroops != value) { _hasPendingTroops = value; OnPropertyChangedWithValue(value, nameof(HasPendingTroops)); } }
        }

        [DataSourceProperty]
        public bool HasNoEliteTroops
        {
            get => _hasNoEliteTroops;
            set { if (_hasNoEliteTroops != value) { _hasNoEliteTroops = value; OnPropertyChangedWithValue(value, nameof(HasNoEliteTroops)); } }
        }

        [DataSourceProperty]
        public bool HasNoAvailableTroops
        {
            get => _hasNoAvailableTroops;
            set { if (_hasNoAvailableTroops != value) { _hasNoAvailableTroops = value; OnPropertyChangedWithValue(value, nameof(HasNoAvailableTroops)); } }
        }

        [DataSourceProperty]
        public bool HasNoPendingTroops
        {
            get => _hasNoPendingTroops;
            set { if (_hasNoPendingTroops != value) { _hasNoPendingTroops = value; OnPropertyChangedWithValue(value, nameof(HasNoPendingTroops)); } }
        }

        // ── Commands ──────────────────────────────────────────────────────────────

        public void ExecuteClose()
        {
            _onClose?.Invoke();
        }

        /// <summary>
        /// Recruits all affordable elite troops from the pool in one action.
        /// Loops through all elite troop types and recruits every available unit.
        /// </summary>
        public void ExecuteRecruitAllElites()
        {
            try
            {
                var behavior = B1071_CastleRecruitmentBehavior.Instance;
                if (behavior == null) return;

                bool anyRecruited = false;
                // Snapshot the current elite list to avoid collection modification during iteration
                var snapshot = new System.Collections.Generic.List<(CharacterObject troop, int count)>();
                foreach (var vm in _eliteTroops)
                    snapshot.Add((vm.Character, vm.NumericCount));

                foreach (var (troop, count) in snapshot)
                {
                    for (int i = 0; i < count; i++)
                    {
                        if (!behavior.TryRecruitElite(_castle, troop))
                            break;   // out of gold or manpower — stop recruiting this troop type
                        anyRecruited = true;
                    }
                }

                if (anyRecruited)
                    RefreshLists();
            }
            catch (Exception ex)
            {
                B1071_VerboseLog.Log("CastleRecruitment", $"ExecuteRecruitAllElites error: {ex.Message}");
            }
        }

        /// <summary>
        /// Recruits all affordable ready prisoners in one action.
        /// Loops through all ready prisoner types and recruits every available unit.
        /// </summary>
        public void ExecuteRecruitAllReady()
        {
            try
            {
                var behavior = B1071_CastleRecruitmentBehavior.Instance;
                if (behavior == null) return;

                bool anyRecruited = false;
                var snapshot = new System.Collections.Generic.List<(CharacterObject troop, int count)>();
                foreach (var vm in _availableTroops)
                    snapshot.Add((vm.Character, vm.NumericCount));

                foreach (var (troop, count) in snapshot)
                {
                    for (int i = 0; i < count; i++)
                    {
                        if (!behavior.TryRecruitPrisoner(_castle, troop))
                            break;
                        anyRecruited = true;
                    }
                }

                if (anyRecruited)
                    RefreshLists();
            }
            catch (Exception ex)
            {
                B1071_VerboseLog.Log("CastleRecruitment", $"ExecuteRecruitAllReady error: {ex.Message}");
            }
        }
    }
}
