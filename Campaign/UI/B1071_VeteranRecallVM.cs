using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.UI
{
    /// <summary>
    /// The veteran register: every soldier who finished his term and went home, still willing
    /// to march again for a re-enlistment bounty.
    ///
    /// Two shapes from one screen. Opened from a settlement it shows that one register and the
    /// men fall in as they are hired. Opened from the campaign map it shows every register the
    /// player may draw from, adds how long each recall would take to reach him, and lists the
    /// orders already on the road underneath.
    /// </summary>
    public sealed class B1071_VeteranRecallVM : ViewModel
    {
        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        /// <summary>Null when the screen was opened from the map rather than from a settlement.</summary>
        private readonly Settlement? _settlement;
        private Action? _onClose;

        private string _titleText = string.Empty;
        private string _goldLabelText = string.Empty;
        private string _goldText = string.Empty;
        private string _summaryText = string.Empty;
        private string _noticeText = string.Empty;
        private bool _hasNotice;
        private string _settlementColumnText = string.Empty;
        private string _troopColumnText = string.Empty;
        private string _tierColumnText = string.Empty;
        private string _countColumnText = string.Empty;
        private string _daysColumnText = string.Empty;
        private string _etaColumnText = string.Empty;
        private string _goldColumnText = string.Empty;
        private string _manpowerColumnText = string.Empty;
        private string _recallColumnText = string.Empty;
        private string _noVeteransText = string.Empty;
        private string _transitTitleText = string.Empty;
        private string _transitStatusColumnText = string.Empty;
        private string _transitPaidColumnText = string.Empty;
        private string _transitCancelColumnText = string.Empty;
        private MBBindingList<B1071_VeteranRecallTroopVM> _veterans;
        private MBBindingList<B1071_VeteranRecallTransitVM> _transit;
        private bool _hasVeterans;
        private bool _hasNoVeterans = true;
        private bool _hasTransit;
        private bool _isMapWide;
        // Float, not int. These three are bound to a Widget's SuggestedWidth / SuggestedHeight,
        // which Gauntlet holds as a float and writes back into the data source whenever the
        // widget's own size changes. An int setter is handed that float and throws inside the
        // binding invoke, which is a hard crash rather than a caught exception — and it only
        // ever fired on the first recall, because that is when the transit table appears and
        // the list height changes for the first time.
        private float _windowWidth = 900f;
        private float _windowHeight = 600f;
        private float _listHeight = 410f;

        public B1071_VeteranRecallVM(Settlement? settlement, Action? onClose)
        {
            _settlement = settlement;
            _isMapWide = settlement == null;
            _onClose = onClose;
            _veterans = new MBBindingList<B1071_VeteranRecallTroopVM>();
            _transit = new MBBindingList<B1071_VeteranRecallTransitVM>();

            // The map-wide list carries two more columns and a second table, so it needs a
            // wider, taller frame. The settlement view keeps the size it always had.
            WindowWidth = _isMapWide ? 1100f : 900f;
            WindowHeight = _isMapWide ? 720f : 600f;

            RefreshLocalizedLabels();
            RefreshList();
        }

        private static string L(string id, string fallback)
            => new TextObject($"{{={id}}}{fallback}").ToString();

        private void RefreshLocalizedLabels()
        {
            TitleText = _settlement != null
                ? new TextObject("{=b1071_recall_title}Veteran Register — {SETTLEMENT}")
                    .SetTextVariable("SETTLEMENT", _settlement.Name?.ToString() ?? string.Empty)
                    .ToString()
                : L("b1071_recall_title_map", "Veteran Register");
            GoldLabelText = L("b1071_ui_gold", "Gold:");
            SettlementColumnText = L("b1071_ui_settlement", "Settlement");
            TroopColumnText = L("b1071_ui_troop", "Troop");
            TierColumnText = L("b1071_ui_tier", "Tier");
            CountColumnText = L("b1071_recall_waiting", "At Home");
            DaysColumnText = L("b1071_recall_days", "Disperse In");
            EtaColumnText = L("b1071_recall_eta", "Arrives In");
            GoldColumnText = L("b1071_recall_gold_each", "Gold Each");
            ManpowerColumnText = L("b1071_recall_manpower_each", "Manpower Each");
            RecallColumnText = L("b1071_recall_action", "Recall");
            TransitTitleText = L("b1071_recall_transit_title", "On the road to you");
            TransitStatusColumnText = L("b1071_recall_transit_status", "Status");
            TransitPaidColumnText = L("b1071_recall_transit_paid", "Paid");
            TransitCancelColumnText = L("b1071_recall_cancel", "Call Off");
            NoVeteransText = _settlement != null
                ? L("b1071_recall_none", "No veterans are waiting here. Soldiers who finish their service return to the settlement that raised them and appear on its register.")
                : L("b1071_recall_none_map", "No veterans are waiting on any register you may draw from. Soldiers who finish their service return to the settlement that raised them.");
        }

        public void RefreshList()
        {
            _veterans.Clear();
            _transit.Clear();

            GoldText = Hero.MainHero != null ? Hero.MainHero.Gold.ToString("N0") : L("b1071_ui_na", "N/A");

            var behavior = B1071_DemobilizationBehavior.Instance;
            if (behavior == null)
            {
                HasVeterans = false;
                HasNoVeterans = true;
                HasTransit = false;
                SummaryText = L("b1071_recall_unavailable", "The veteran register is not available in this campaign.");
                HasNotice = false;
                UpdateListHeight();
                return;
            }

            // Orders already sent belong on both views: a player standing in a town may well
            // want to call one off, and it is the only place he can.
            foreach (var pending in behavior.GetPendingRecallsForUi())
                _transit.Add(new B1071_VeteranRecallTransitVM(this, pending, _transit.Count % 2 == 1));

            HasTransit = _transit.Count > 0;

            if (_settlement != null)
                RefreshSettlementList(behavior);
            else
                RefreshMapWideList(behavior);

            UpdateListHeight();
        }

        private void RefreshSettlementList(B1071_DemobilizationBehavior behavior)
        {
            if (!B1071_DemobilizationBehavior.TryGetPlayerRegisterAccess(_settlement!, out bool ownMenOnly))
            {
                HasVeterans = false;
                HasNoVeterans = true;
                SummaryText = L("b1071_recall_no_entitlement", "You are not entitled to hire from this settlement's veteran register.");
                HasNotice = false;
                return;
            }

            var rows = behavior.GetVeteransForUi(_settlement);
            int totalMen = 0;

            foreach (var row in rows)
            {
                // Striping is decided here, while the order is still known: a list template
                // has no index of its own to shade itself by.
                _veterans.Add(new B1071_VeteranRecallTroopVM(this, row, _veterans.Count % 2 == 1, showSettlement: false));

                // Men still resting out their days at home are counted too. They are on the
                // register and on the row, so leaving them out of the headline would have the
                // line contradict the list right underneath it.
                totalMen += row.Count + row.RestingCount;
            }

            HasVeterans = _veterans.Count > 0;
            HasNoVeterans = _veterans.Count == 0;

            // Kept to one short line. The header row shares its width with the gold readout
            // on the right, and the old sentence was long enough to run underneath it.
            SummaryText = (ownMenOnly
                    ? new TextObject("{=b1071_recall_summary_own}{MEN} of your own men here, {TYPES} troop type{TPLURAL}. They disperse {RETENTION} days after discharge.")
                    : new TextObject("{=b1071_recall_summary}{MEN} veteran{MPLURAL} at home here, {TYPES} troop type{TPLURAL}. They disperse {RETENTION} days after discharge."))
                .SetTextVariable("MEN", totalMen)
                .SetTextVariable("MPLURAL", totalMen == 1 ? string.Empty : "s")
                .SetTextVariable("TYPES", _veterans.Count)
                .SetTextVariable("TPLURAL", _veterans.Count == 1 ? string.Empty : "s")
                .SetTextVariable("RETENTION", Math.Max(1, Settings.DemobilizationVeteranRetentionDays))
                .ToString();

            // Away from your own realm the list is only ever your own discharged men. Saying
            // so on its own line explains why the local lord's veterans are nowhere to be
            // seen, without crowding the line above.
            HasNotice = ownMenOnly && _veterans.Count > 0;
            NoticeText = HasNotice
                ? L("b1071_recall_notice_own", "This is not your realm, so only the men you sent home yourself will answer.")
                : string.Empty;
        }

        private void RefreshMapWideList(B1071_DemobilizationBehavior behavior)
        {
            var rows = behavior.GetAllVeteransForUi();
            int totalMen = 0;

            // Counted by name rather than by watching for the name to change: the list is
            // sorted by how far off the men are, so one town's rows need not sit together
            // and a town seen twice would otherwise be counted twice.
            var places = new HashSet<string>(StringComparer.Ordinal);

            foreach (var row in rows)
            {
                _veterans.Add(new B1071_VeteranRecallTroopVM(this, row, _veterans.Count % 2 == 1, showSettlement: true));
                totalMen += row.Count + row.RestingCount;
                places.Add(row.SettlementId);
            }

            HasVeterans = _veterans.Count > 0;
            HasNoVeterans = _veterans.Count == 0;

            SummaryText = new TextObject("{=b1071_recall_summary_map}{MEN} veteran{MPLURAL} across {PLACES} register{PPLURAL} you may draw from.")
                .SetTextVariable("MEN", totalMen)
                .SetTextVariable("MPLURAL", totalMen == 1 ? string.Empty : "s")
                .SetTextVariable("PLACES", places.Count)
                .SetTextVariable("PPLURAL", places.Count == 1 ? string.Empty : "s")
                .ToString();

            HasNotice = _veterans.Count > 0;
            NoticeText = Settings.EnableDemobilizationRemoteRecall
                ? L("b1071_recall_notice_map", "Word must ride out before anyone can set off, and the men then march to wherever you are. Gold and manpower are paid when the order goes out.")
                : L("b1071_recall_notice_map_off", "Recall from a distance is switched off in the mod settings, so you must ride to a settlement in person to call its veterans back.");
        }

        /// <summary>
        /// The two tables share one window, so the register list gives up height whenever
        /// there are orders on the road to show underneath it.
        /// </summary>
        private void UpdateListHeight()
        {
            float available = WindowHeight - 190f;
            ListHeight = HasTransit ? Math.Max(120f, available - 220f) : available;
        }

        public void ExecuteClose()
        {
            _onClose?.Invoke();
        }

        [DataSourceProperty]
        public string TitleText
        {
            get => _titleText;
            set { if (_titleText != value) { _titleText = value; OnPropertyChangedWithValue(value, nameof(TitleText)); } }
        }

        [DataSourceProperty]
        public string GoldLabelText
        {
            get => _goldLabelText;
            set { if (_goldLabelText != value) { _goldLabelText = value; OnPropertyChangedWithValue(value, nameof(GoldLabelText)); } }
        }

        [DataSourceProperty]
        public string GoldText
        {
            get => _goldText;
            set { if (_goldText != value) { _goldText = value; OnPropertyChangedWithValue(value, nameof(GoldText)); } }
        }

        [DataSourceProperty]
        public string SummaryText
        {
            get => _summaryText;
            set { if (_summaryText != value) { _summaryText = value; OnPropertyChangedWithValue(value, nameof(SummaryText)); } }
        }

        [DataSourceProperty]
        public string NoticeText
        {
            get => _noticeText;
            set { if (_noticeText != value) { _noticeText = value; OnPropertyChangedWithValue(value, nameof(NoticeText)); } }
        }

        [DataSourceProperty]
        public bool HasNotice
        {
            get => _hasNotice;
            set { if (_hasNotice != value) { _hasNotice = value; OnPropertyChangedWithValue(value, nameof(HasNotice)); } }
        }

        [DataSourceProperty]
        public string SettlementColumnText
        {
            get => _settlementColumnText;
            set { if (_settlementColumnText != value) { _settlementColumnText = value; OnPropertyChangedWithValue(value, nameof(SettlementColumnText)); } }
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
        public string DaysColumnText
        {
            get => _daysColumnText;
            set { if (_daysColumnText != value) { _daysColumnText = value; OnPropertyChangedWithValue(value, nameof(DaysColumnText)); } }
        }

        [DataSourceProperty]
        public string EtaColumnText
        {
            get => _etaColumnText;
            set { if (_etaColumnText != value) { _etaColumnText = value; OnPropertyChangedWithValue(value, nameof(EtaColumnText)); } }
        }

        [DataSourceProperty]
        public string GoldColumnText
        {
            get => _goldColumnText;
            set { if (_goldColumnText != value) { _goldColumnText = value; OnPropertyChangedWithValue(value, nameof(GoldColumnText)); } }
        }

        [DataSourceProperty]
        public string ManpowerColumnText
        {
            get => _manpowerColumnText;
            set { if (_manpowerColumnText != value) { _manpowerColumnText = value; OnPropertyChangedWithValue(value, nameof(ManpowerColumnText)); } }
        }

        [DataSourceProperty]
        public string RecallColumnText
        {
            get => _recallColumnText;
            set { if (_recallColumnText != value) { _recallColumnText = value; OnPropertyChangedWithValue(value, nameof(RecallColumnText)); } }
        }

        [DataSourceProperty]
        public string NoVeteransText
        {
            get => _noVeteransText;
            set { if (_noVeteransText != value) { _noVeteransText = value; OnPropertyChangedWithValue(value, nameof(NoVeteransText)); } }
        }

        [DataSourceProperty]
        public string TransitTitleText
        {
            get => _transitTitleText;
            set { if (_transitTitleText != value) { _transitTitleText = value; OnPropertyChangedWithValue(value, nameof(TransitTitleText)); } }
        }

        [DataSourceProperty]
        public string TransitStatusColumnText
        {
            get => _transitStatusColumnText;
            set { if (_transitStatusColumnText != value) { _transitStatusColumnText = value; OnPropertyChangedWithValue(value, nameof(TransitStatusColumnText)); } }
        }

        [DataSourceProperty]
        public string TransitPaidColumnText
        {
            get => _transitPaidColumnText;
            set { if (_transitPaidColumnText != value) { _transitPaidColumnText = value; OnPropertyChangedWithValue(value, nameof(TransitPaidColumnText)); } }
        }

        [DataSourceProperty]
        public string TransitCancelColumnText
        {
            get => _transitCancelColumnText;
            set { if (_transitCancelColumnText != value) { _transitCancelColumnText = value; OnPropertyChangedWithValue(value, nameof(TransitCancelColumnText)); } }
        }

        [DataSourceProperty]
        public MBBindingList<B1071_VeteranRecallTroopVM> Veterans
        {
            get => _veterans;
            set { if (_veterans != value) { _veterans = value; OnPropertyChangedWithValue(value, nameof(Veterans)); } }
        }

        [DataSourceProperty]
        public MBBindingList<B1071_VeteranRecallTransitVM> Transit
        {
            get => _transit;
            set { if (_transit != value) { _transit = value; OnPropertyChangedWithValue(value, nameof(Transit)); } }
        }

        [DataSourceProperty]
        public bool HasVeterans
        {
            get => _hasVeterans;
            set { if (_hasVeterans != value) { _hasVeterans = value; OnPropertyChangedWithValue(value, nameof(HasVeterans)); } }
        }

        [DataSourceProperty]
        public bool HasNoVeterans
        {
            get => _hasNoVeterans;
            set { if (_hasNoVeterans != value) { _hasNoVeterans = value; OnPropertyChangedWithValue(value, nameof(HasNoVeterans)); } }
        }

        [DataSourceProperty]
        public bool HasTransit
        {
            get => _hasTransit;
            set { if (_hasTransit != value) { _hasTransit = value; OnPropertyChangedWithValue(value, nameof(HasTransit)); } }
        }

        [DataSourceProperty]
        public bool IsMapWide
        {
            get => _isMapWide;
            set { if (_isMapWide != value) { _isMapWide = value; OnPropertyChangedWithValue(value, nameof(IsMapWide)); } }
        }

        [DataSourceProperty]
        public float WindowWidth
        {
            get => _windowWidth;
            set { if (_windowWidth != value) { _windowWidth = value; OnPropertyChangedWithValue(value, nameof(WindowWidth)); } }
        }

        [DataSourceProperty]
        public float WindowHeight
        {
            get => _windowHeight;
            set { if (_windowHeight != value) { _windowHeight = value; OnPropertyChangedWithValue(value, nameof(WindowHeight)); } }
        }

        [DataSourceProperty]
        public float ListHeight
        {
            get => _listHeight;
            set { if (_listHeight != value) { _listHeight = value; OnPropertyChangedWithValue(value, nameof(ListHeight)); } }
        }
    }
}
