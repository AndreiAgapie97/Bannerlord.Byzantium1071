using Byzantium1071.Campaign.Behaviors;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.UI
{
    public sealed class B1071_DemobilizationVM : ViewModel
    {
        private Action? _onClose;
        private string _titleText = string.Empty;
        private string _goldLabelText = string.Empty;
        private string _goldText = string.Empty;
        private string _summaryText = string.Empty;
        private string _troopColumnText = string.Empty;
        private string _tierColumnText = string.Empty;
        private string _countColumnText = string.Empty;
        private string _ageColumnText = string.Empty;
        private string _remainingColumnText = string.Empty;
        private string _costColumnText = string.Empty;
        private string _statusColumnText = string.Empty;
        private string _noCohortsText = string.Empty;
        private MBBindingList<B1071_DemobilizationCohortVM> _cohorts;
        private bool _hasCohorts;
        private bool _hasNoCohorts = true;

        public B1071_DemobilizationVM(Action? onClose)
        {
            _onClose = onClose;
            _cohorts = new MBBindingList<B1071_DemobilizationCohortVM>();
            RefreshLocalizedLabels();
            RefreshList();
        }

        private static string L(string id, string fallback)
            => new TextObject($"{{={id}}}{fallback}").ToString();

        private void RefreshLocalizedLabels()
        {
            TitleText = L("b1071_demob_title", "Troop Service");
            GoldLabelText = L("b1071_ui_gold", "Gold:");
            TroopColumnText = L("b1071_ui_troop", "Troop");
            TierColumnText = L("b1071_ui_tier", "Tier");
            CountColumnText = L("b1071_ui_count", "Count");
            AgeColumnText = L("b1071_demob_age", "Age");
            RemainingColumnText = L("b1071_demob_remaining", "Days Left");
            CostColumnText = L("b1071_demob_extend_cost", "Extend Cost");
            StatusColumnText = L("b1071_ui_status", "Status");
            NoCohortsText = L("b1071_demob_no_cohorts", "No tracked soldiers yet. Existing troops are enrolled when the system next reconciles your party.");
        }

        public void RefreshList()
        {
            _cohorts.Clear();

            GoldText = Hero.MainHero != null ? Hero.MainHero.Gold.ToString("N0") : L("b1071_ui_na", "N/A");

            var behavior = B1071_DemobilizationBehavior.Instance;
            if (behavior == null)
            {
                HasCohorts = false;
                HasNoCohorts = true;
                SummaryText = L("b1071_demob_unavailable", "Troop service is not available in this campaign.");
                return;
            }

            var rows = behavior.GetMainPartyCohortsForUi();
            int warningRows = 0;
            int warningMen = 0;

            foreach (var row in rows)
            {
                _cohorts.Add(new B1071_DemobilizationCohortVM(this, row));
                if (row.IsWarning)
                {
                    warningRows++;
                    warningMen += row.Count;
                }
            }

            HasCohorts = _cohorts.Count > 0;
            HasNoCohorts = _cohorts.Count == 0;
            SummaryText = new TextObject("{=b1071_demob_summary}{SOLDIERS} soldier service record{SPLURAL} tracked. {WARNINGS} soldier{WPLURAL} within the warning window.")
                .SetTextVariable("SOLDIERS", _cohorts.Count)
                .SetTextVariable("SPLURAL", _cohorts.Count == 1 ? string.Empty : "s")
                .SetTextVariable("WARNINGS", warningRows)
                .SetTextVariable("WPLURAL", warningRows == 1 ? string.Empty : "s")
                .SetTextVariable("MEN", warningMen)
                .SetTextVariable("MPLURAL", warningMen == 1 ? string.Empty : "s")
                .ToString();
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
        public string AgeColumnText
        {
            get => _ageColumnText;
            set { if (_ageColumnText != value) { _ageColumnText = value; OnPropertyChangedWithValue(value, nameof(AgeColumnText)); } }
        }

        [DataSourceProperty]
        public string RemainingColumnText
        {
            get => _remainingColumnText;
            set { if (_remainingColumnText != value) { _remainingColumnText = value; OnPropertyChangedWithValue(value, nameof(RemainingColumnText)); } }
        }

        [DataSourceProperty]
        public string CostColumnText
        {
            get => _costColumnText;
            set { if (_costColumnText != value) { _costColumnText = value; OnPropertyChangedWithValue(value, nameof(CostColumnText)); } }
        }

        [DataSourceProperty]
        public string StatusColumnText
        {
            get => _statusColumnText;
            set { if (_statusColumnText != value) { _statusColumnText = value; OnPropertyChangedWithValue(value, nameof(StatusColumnText)); } }
        }

        [DataSourceProperty]
        public string NoCohortsText
        {
            get => _noCohortsText;
            set { if (_noCohortsText != value) { _noCohortsText = value; OnPropertyChangedWithValue(value, nameof(NoCohortsText)); } }
        }

        [DataSourceProperty]
        public MBBindingList<B1071_DemobilizationCohortVM> Cohorts
        {
            get => _cohorts;
            set { if (_cohorts != value) { _cohorts = value; OnPropertyChangedWithValue(value, nameof(Cohorts)); } }
        }

        [DataSourceProperty]
        public bool HasCohorts
        {
            get => _hasCohorts;
            set { if (_hasCohorts != value) { _hasCohorts = value; OnPropertyChangedWithValue(value, nameof(HasCohorts)); } }
        }

        [DataSourceProperty]
        public bool HasNoCohorts
        {
            get => _hasNoCohorts;
            set { if (_hasNoCohorts != value) { _hasNoCohorts = value; OnPropertyChangedWithValue(value, nameof(HasNoCohorts)); } }
        }
    }
}