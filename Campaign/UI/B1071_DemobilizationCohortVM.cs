using Byzantium1071.Campaign.Behaviors;
using System;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.UI
{
    public sealed class B1071_DemobilizationCohortVM : ViewModel
    {
        /// <summary>Releasing more men than this at once asks for confirmation first.</summary>
        private const int ConfirmReleaseThreshold = 10;

        /// <summary>How many a shift-click acts on.</summary>
        private const int BatchAmount = 5;

        private readonly B1071_DemobilizationVM _parent;
        private readonly string _partyId;
        private readonly string _troopId;
        private readonly string _homeId;
        private readonly int _joinDay;
        private readonly int _extensionCount;
        private readonly int _available;

        private ImageIdentifierVM _visual;
        private string _name = string.Empty;
        private string _tier = string.Empty;
        private string _count = string.Empty;
        private string _age = string.Empty;
        private string _remaining = string.Empty;
        private string _extendCost = string.Empty;
        private string _home = string.Empty;
        private string _statusText = string.Empty;
        private string _extendText = string.Empty;
        private string _releaseText = string.Empty;
        private bool _isDue;
        private bool _isWarning;
        private bool _isServing;
        private bool _isAlternateRow;
        private bool _canExtend;
        private bool _canRelease;
        private HintViewModel? _extendHint;
        private HintViewModel? _releaseHint;

        public B1071_DemobilizationCohortVM(B1071_DemobilizationVM parent, B1071_DemobilizationBehavior.CohortView row, bool isAlternateRow)
        {
            _parent = parent;
            _partyId = row.PartyId;
            _troopId = row.TroopId;
            _homeId = row.HomeId;
            _joinDay = row.JoinDay;
            _extensionCount = row.ExtensionCount;
            _available = row.Count;
            _isAlternateRow = isAlternateRow;

            _visual = new CharacterImageIdentifierVM(CharacterCode.CreateFrom(row.Troop));
            _name = row.Troop.Name?.ToString() ?? new TextObject("{=b1071_ui_unknown}Unknown").ToString();
            _tier = row.Troop.Tier.ToString();
            _count = row.Count.ToString();
            _age = row.AgeDays.ToString();
            _remaining = row.RemainingDays <= 0 ? "0" : row.RemainingDays.ToString();
            _extendCost = row.ExtendCost.ToString();
            _home = string.IsNullOrEmpty(row.HomeName)
                ? new TextObject("{=b1071_demob_home_unknown}Unknown").ToString()
                : row.HomeName;
            _extendText = new TextObject("{=b1071_demob_extend}Extend").ToString();
            _releaseText = new TextObject("{=b1071_demob_release}Send Home").ToString();
            _canExtend = row.CanExtend;
            _canRelease = row.Count > 0;

            // Three flags rather than a colour string, because the prefab shows one of three
            // fixed-colour labels. Binding a colour would depend on Gauntlet converting a
            // string to a Color on a bound property, which is not worth gambling a release on.
            _isDue = row.IsOverdue;
            _isWarning = !row.IsOverdue && row.IsWarning;
            _isServing = !row.IsOverdue && !row.IsWarning;

            if (_isDue)
                _statusText = new TextObject("{=b1071_demob_status_due}Due").ToString();
            else if (_isWarning)
                _statusText = new TextObject("{=b1071_demob_status_warning}Warning").ToString();
            else
                _statusText = new TextObject("{=b1071_demob_status_serving}Serving").ToString();

            // The modifier keys are the only way to act on more than one man, and nothing on
            // screen can show that, so the hints have to carry it.
            _extendHint = _canExtend
                ? new HintViewModel(new TextObject("{=b1071_demob_extend_hint}Pay {COST}g per man to keep these soldiers for another term. Extension {NEXT} of {MAX}; each one costs more than the last. When they finally go, they return to {HOME}.{NEWLINE}Click for one, Shift+click for {BATCH}, Ctrl+click for all {COUNT}.")
                    .SetTextVariable("COST", row.ExtendCost)
                    .SetTextVariable("NEXT", row.ExtensionCount + 1)
                    .SetTextVariable("MAX", row.MaxExtensions)
                    .SetTextVariable("HOME", _home)
                    .SetTextVariable("BATCH", BatchAmount)
                    .SetTextVariable("COUNT", _available)
                    .SetTextVariable("NEWLINE", "\n"))
                : new HintViewModel(row.ExtensionsExhausted
                    ? new TextObject("{=b1071_demob_extend_hint_used}These soldiers have used all {MAX} of their allowed service extensions. They return to {HOME} when their term ends, and can be hired back from its veteran register.")
                        .SetTextVariable("MAX", row.MaxExtensions)
                        .SetTextVariable("HOME", _home)
                    : new TextObject("{=b1071_demob_extend_hint_blocked}Not enough gold for this service extension."));

            _releaseHint = row.ReturnsHome
                ? new HintViewModel(new TextObject("{=b1071_demob_release_hint}Send these men home now, before their term is up. It costs nothing: they hand their manpower back to {HOME} and wait on its veteran register, where you can hire them again at this tier.{NEWLINE}Click for one, Shift+click for {BATCH}, Ctrl+click for all {COUNT}.")
                    .SetTextVariable("HOME", _home)
                    .SetTextVariable("BATCH", BatchAmount)
                    .SetTextVariable("COUNT", _available)
                    .SetTextVariable("NEWLINE", "\n"))
                : new HintViewModel(new TextObject("{=b1071_demob_release_hint_off}Dismiss these men now. Veteran return is switched off in the mod settings, so they simply leave and their manpower is lost.{NEWLINE}Click for one, Shift+click for {BATCH}, Ctrl+click for all {COUNT}.")
                    .SetTextVariable("BATCH", BatchAmount)
                    .SetTextVariable("COUNT", _available)
                    .SetTextVariable("NEWLINE", "\n"));
        }

        /// <summary>
        /// How many men a click means. One row can hold a hundred soldiers and clicking a
        /// hundred times is not an interface, so the usual modifiers scale the action:
        /// plain click one, Shift a handful, Ctrl the lot.
        /// </summary>
        private int AmountFromModifiers()
        {
            if (Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl))
                return _available;
            if (Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift))
                return Math.Min(BatchAmount, _available);
            return 1;
        }

        public void ExecuteExtend()
        {
            if (!_canExtend) return;

            int amount = AmountFromModifiers();
            if (amount <= 0) return;

            if (B1071_DemobilizationBehavior.Instance?.TryExtendCohortGroup(
                    _partyId, _troopId, _homeId, _joinDay, _extensionCount, amount) > 0)
                _parent.RefreshList();
        }

        private void Release(int amount)
        {
            if (!_canRelease || amount <= 0) return;

            if (B1071_DemobilizationBehavior.Instance?.TryDischargeCohort(
                    _partyId, _troopId, _homeId, _joinDay, _extensionCount, amount) > 0)
                _parent.RefreshList();
        }

        public void ExecuteRelease()
        {
            if (!_canRelease) return;

            int amount = AmountFromModifiers();
            if (amount <= 0) return;

            // A misclick on a big row is expensive to undo: buying those men back costs the
            // re-enlistment bounty and their home settlement's manpower a second time, and only
            // works while they are still on the register. Small releases stay a single click,
            // because asking every time would make the button useless.
            if (amount <= ConfirmReleaseThreshold)
            {
                Release(amount);
                return;
            }

            InformationManager.ShowInquiry(new InquiryData(
                titleText: new TextObject("{=b1071_demob_release_confirm_title}Release Soldiers").ToString(),
                text: new TextObject("{=b1071_demob_release_confirm}Send {COUNT} {TROOP} home now? Hiring them back later costs the re-enlistment bounty and their home settlement's manpower again.")
                    .SetTextVariable("COUNT", amount)
                    .SetTextVariable("TROOP", _name)
                    .ToString(),
                isAffirmativeOptionShown: true,
                isNegativeOptionShown: true,
                affirmativeText: new TextObject("{=b1071_demob_release_confirm_yes}Send them home").ToString(),
                negativeText: new TextObject("{=b1071_ui_cancel}Cancel").ToString(),
                affirmativeAction: () => Release(amount),
                negativeAction: null));
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
        public string Home
        {
            get => _home;
            set { if (_home != value) { _home = value; OnPropertyChangedWithValue(value, nameof(Home)); } }
        }

        [DataSourceProperty]
        public string StatusText
        {
            get => _statusText;
            set { if (_statusText != value) { _statusText = value; OnPropertyChangedWithValue(value, nameof(StatusText)); } }
        }

        [DataSourceProperty]
        public bool IsDue
        {
            get => _isDue;
            set { if (_isDue != value) { _isDue = value; OnPropertyChangedWithValue(value, nameof(IsDue)); } }
        }

        [DataSourceProperty]
        public bool IsWarning
        {
            get => _isWarning;
            set { if (_isWarning != value) { _isWarning = value; OnPropertyChangedWithValue(value, nameof(IsWarning)); } }
        }

        [DataSourceProperty]
        public bool IsServing
        {
            get => _isServing;
            set { if (_isServing != value) { _isServing = value; OnPropertyChangedWithValue(value, nameof(IsServing)); } }
        }

        [DataSourceProperty]
        public bool IsAlternateRow
        {
            get => _isAlternateRow;
            set { if (_isAlternateRow != value) { _isAlternateRow = value; OnPropertyChangedWithValue(value, nameof(IsAlternateRow)); } }
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

        [DataSourceProperty]
        public string ReleaseText
        {
            get => _releaseText;
            set { if (_releaseText != value) { _releaseText = value; OnPropertyChangedWithValue(value, nameof(ReleaseText)); } }
        }

        [DataSourceProperty]
        public bool CanRelease
        {
            get => _canRelease;
            set { if (_canRelease != value) { _canRelease = value; OnPropertyChangedWithValue(value, nameof(CanRelease)); } }
        }

        [DataSourceProperty]
        public HintViewModel? ReleaseHint
        {
            get => _releaseHint;
            set { if (_releaseHint != value) { _releaseHint = value; OnPropertyChangedWithValue(value, nameof(ReleaseHint)); } }
        }
    }
}
