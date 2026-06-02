using Byzantium1071.Campaign.Behaviors;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.UI
{
    public sealed class B1071_DemobilizationCohortVM : ViewModel
    {
        private readonly B1071_DemobilizationVM _parent;
        private readonly string _partyId;
        private readonly string _troopId;
        private readonly int _cohortIndex;

        private ImageIdentifierVM _visual;
        private string _name = string.Empty;
        private string _tier = string.Empty;
        private string _count = string.Empty;
        private string _age = string.Empty;
        private string _remaining = string.Empty;
        private string _extendCost = string.Empty;
        private string _statusText = string.Empty;
        private string _extendText = string.Empty;
        private bool _canExtend;
        private HintViewModel? _extendHint;

        public B1071_DemobilizationCohortVM(B1071_DemobilizationVM parent, B1071_DemobilizationBehavior.CohortView row)
        {
            _parent = parent;
            _partyId = row.PartyId;
            _troopId = row.TroopId;
            _cohortIndex = row.CohortIndex;

            _visual = new CharacterImageIdentifierVM(CharacterCode.CreateFrom(row.Troop));
            _name = row.Troop.Name?.ToString() ?? new TextObject("{=b1071_ui_unknown}Unknown").ToString();
            _tier = row.Troop.Tier.ToString();
            _count = row.Count.ToString();
            _age = row.AgeDays.ToString();
            _remaining = row.RemainingDays <= 0 ? "0" : row.RemainingDays.ToString();
            _extendCost = row.ExtendCost.ToString();
            _extendText = new TextObject("{=b1071_demob_extend}Extend").ToString();
            _canExtend = row.CanExtend;

            if (row.IsOverdue)
                _statusText = new TextObject("{=b1071_demob_status_due}Due").ToString();
            else if (row.IsWarning)
                _statusText = new TextObject("{=b1071_demob_status_warning}Warning").ToString();
            else
                _statusText = new TextObject("{=b1071_demob_status_serving}Serving").ToString();

            _extendHint = _canExtend
                ? new HintViewModel(new TextObject("{=b1071_demob_extend_hint}Pay {COST}g to extend this soldier once by the configured number of days.")
                    .SetTextVariable("COST", row.ExtendCost))
                : new HintViewModel(row.HasBeenExtended
                    ? new TextObject("{=b1071_demob_extend_hint_used}This soldier has already used his one allowed service extension.")
                    : new TextObject("{=b1071_demob_extend_hint_blocked}Not enough gold for this service extension."));
        }

        public void ExecuteExtend()
        {
            if (!_canExtend) return;
            if (B1071_DemobilizationBehavior.Instance?.TryExtendCohort(_partyId, _troopId, _cohortIndex) == true)
                _parent.RefreshList();
        }

        [DataSourceProperty]
        public ImageIdentifierVM Visual
        {
            get => _visual;
            set { if (_visual != value) { _visual = value; OnPropertyChangedWithValue(value, nameof(Visual)); } }
        }

        [DataSourceProperty]
        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChangedWithValue(value, nameof(Name)); } }
        }

        [DataSourceProperty]
        public string Tier
        {
            get => _tier;
            set { if (_tier != value) { _tier = value; OnPropertyChangedWithValue(value, nameof(Tier)); } }
        }

        [DataSourceProperty]
        public string Count
        {
            get => _count;
            set { if (_count != value) { _count = value; OnPropertyChangedWithValue(value, nameof(Count)); } }
        }

        [DataSourceProperty]
        public string Age
        {
            get => _age;
            set { if (_age != value) { _age = value; OnPropertyChangedWithValue(value, nameof(Age)); } }
        }

        [DataSourceProperty]
        public string Remaining
        {
            get => _remaining;
            set { if (_remaining != value) { _remaining = value; OnPropertyChangedWithValue(value, nameof(Remaining)); } }
        }

        [DataSourceProperty]
        public string ExtendCost
        {
            get => _extendCost;
            set { if (_extendCost != value) { _extendCost = value; OnPropertyChangedWithValue(value, nameof(ExtendCost)); } }
        }

        [DataSourceProperty]
        public string StatusText
        {
            get => _statusText;
            set { if (_statusText != value) { _statusText = value; OnPropertyChangedWithValue(value, nameof(StatusText)); } }
        }

        [DataSourceProperty]
        public string ExtendText
        {
            get => _extendText;
            set { if (_extendText != value) { _extendText = value; OnPropertyChangedWithValue(value, nameof(ExtendText)); } }
        }

        [DataSourceProperty]
        public bool CanExtend
        {
            get => _canExtend;
            set { if (_canExtend != value) { _canExtend = value; OnPropertyChangedWithValue(value, nameof(CanExtend)); } }
        }

        [DataSourceProperty]
        public HintViewModel? ExtendHint
        {
            get => _extendHint;
            set { if (_extendHint != value) { _extendHint = value; OnPropertyChangedWithValue(value, nameof(ExtendHint)); } }
        }
    }
}