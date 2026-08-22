using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.UI
{
    /// <summary>
    /// The veteran register of one settlement: every soldier who finished his term here and
    /// went home, still willing to march again for a re-enlistment bounty.
    /// </summary>
    public sealed class B1071_VeteranRecallVM : ViewModel
    {
        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        private readonly Settlement _settlement;
        private Action? _onClose;

        private string _titleText = string.Empty;
        private string _goldLabelText = string.Empty;
        private string _goldText = string.Empty;
        private string _summaryText = string.Empty;
        private string _noticeText = string.Empty;
        private bool _hasNotice;
        private string _troopColumnText = string.Empty;
        private string _tierColumnText = string.Empty;
        private string _countColumnText = string.Empty;
        private string _daysColumnText = string.Empty;
        private string _goldColumnText = string.Empty;
        private string _manpowerColumnText = string.Empty;
        private string _recallColumnText = string.Empty;
        private string _noVeteransText = string.Empty;
        private MBBindingList<B1071_VeteranRecallTroopVM> _veterans;
        private bool _hasVeterans;
        private bool _hasNoVeterans = true;

        public B1071_VeteranRecallVM(Settlement settlement, Action? onClose)
        {
            _settlement = settlement;
            _onClose = onClose;
            _veterans = new MBBindingList<B1071_VeteranRecallTroopVM>();
            RefreshLocalizedLabels();
            RefreshList();
        }

        private static string L(string id, string fallback)
            => new TextObject($"{{={id}}}{fallback}").ToString();

        private void RefreshLocalizedLabels()
        {
            TitleText = new TextObject("{=b1071_recall_title}Veteran Register — {SETTLEMENT}")
                .SetTextVariable("SETTLEMENT", _settlement.Name?.ToString() ?? string.Empty)
                .ToString();
            GoldLabelText = L("b1071_ui_gold", "Gold:");
            TroopColumnText = L("b1071_ui_troop", "Troop");
            TierColumnText = L("b1071_ui_tier", "Tier");
            CountColumnText = L("b1071_recall_waiting", "At Home");
            DaysColumnText = L("b1071_recall_days", "Disperse In");
            GoldColumnText = L("b1071_recall_gold_each", "Gold Each");
            ManpowerColumnText = L("b1071_recall_manpower_each", "Manpower Each");
            RecallColumnText = L("b1071_recall_action", "Recall");
            NoVeteransText = L("b1071_recall_none", "No veterans are waiting here. Soldiers who finish their service return to the settlement that raised them and appear on its register.");
        }

        public void RefreshList()
        {
            _veterans.Clear();

            GoldText = Hero.MainHero != null ? Hero.MainHero.Gold.ToString("N0") : L("b1071_ui_na", "N/A");

            var behavior = B1071_DemobilizationBehavior.Instance;
            if (behavior == null)
            {
                HasVeterans = false;
                HasNoVeterans = true;
                SummaryText = L("b1071_recall_unavailable", "The veteran register is not available in this campaign.");
                HasNotice = false;
                return;
            }

            if (!B1071_DemobilizationBehavior.TryGetPlayerRegisterAccess(_settlement, out bool ownMenOnly))
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
                _veterans.Add(new B1071_VeteranRecallTroopVM(this, _settlement, row, _veterans.Count % 2 == 1));
                totalMen += row.Count;
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
        public MBBindingList<B1071_VeteranRecallTroopVM> Veterans
        {
            get => _veterans;
            set { if (_veterans != value) { _veterans = value; OnPropertyChangedWithValue(value, nameof(Veterans)); } }
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
    }
}
