using Byzantium1071.Campaign.Behaviors;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

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

            RefreshLists();
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
            TitleText = $"Castle Recruitment \u2014 {_castle.Name}";

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
                ManpowerText = "N/A";
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
    }
}
