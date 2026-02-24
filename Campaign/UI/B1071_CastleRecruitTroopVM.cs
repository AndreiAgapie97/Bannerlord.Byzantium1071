using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;

namespace Byzantium1071.Campaign.UI
{
    /// <summary>
    /// ViewModel for a single troop row in the castle recruitment screen.
    /// Three states:
    ///   - Elite: CanRecruit = true (if gold sufficient), culture-based pool troop.
    ///   - Ready: CanRecruit = true, prisoner that served their waiting period.
    ///   - Pending: CanRecruit = false, shows "X days remaining" status text.
    /// </summary>
    public sealed class B1071_CastleRecruitTroopVM : ViewModel
    {
        private readonly B1071_CastleRecruitmentVM _parent;
        private readonly CharacterObject _character;
        private readonly Settlement _castle;
        private readonly bool _isElite;

        private ImageIdentifierVM _visual;
        private string _name = string.Empty;
        private string _tier = string.Empty;
        private string _goldCost = string.Empty;
        private string _count = string.Empty;
        private bool _canRecruit;
        private bool _isReady;
        private string _statusText = string.Empty;
        private HintViewModel? _recruitHint;

        /// <summary>
        /// Constructor for ELITE pool troops (no days tracking).
        /// </summary>
        public B1071_CastleRecruitTroopVM(
            B1071_CastleRecruitmentVM parent,
            CharacterObject character,
            Settlement castle,
            int count,
            int goldCost,
            bool isElite)
        {
            _parent = parent;
            _character = character;
            _castle = castle;
            _isElite = isElite;

            _visual = new CharacterImageIdentifierVM(CharacterCode.CreateFrom(character));
            _name = character.Name?.ToString() ?? "Unknown";
            _tier = character.Tier.ToString();
            _goldCost = goldCost.ToString();
            _count = count.ToString();
            _isReady = true;

            _canRecruit = Hero.MainHero.Gold >= goldCost;
            _statusText = "Elite";

            _recruitHint = _canRecruit
                ? new HintViewModel(new TaleWorlds.Localization.TextObject($"Recruit one {_name} for {goldCost} gold"))
                : new HintViewModel(new TaleWorlds.Localization.TextObject($"Not enough gold (need {goldCost})"));
        }

        /// <summary>
        /// Constructor for PRISONER troops (with day tracking).
        /// </summary>
        public B1071_CastleRecruitTroopVM(
            B1071_CastleRecruitmentVM parent,
            CharacterObject character,
            Settlement castle,
            int count,
            int daysHeld,
            int goldCost,
            bool isReady,
            int daysRequired)
        {
            _parent = parent;
            _character = character;
            _castle = castle;
            _isElite = false;

            _visual = new CharacterImageIdentifierVM(CharacterCode.CreateFrom(character));
            _name = character.Name?.ToString() ?? "Unknown";
            _tier = character.Tier.ToString();
            _goldCost = goldCost.ToString();
            _count = count.ToString();
            _isReady = isReady;

            if (isReady)
            {
                _canRecruit = Hero.MainHero.Gold >= goldCost;
                _statusText = "Ready";
            }
            else
            {
                _canRecruit = false;
                int remaining = daysRequired - daysHeld;
                _statusText = remaining > 0 ? $"{remaining} day{(remaining != 1 ? "s" : "")} remaining" : "Ready";
            }

            _recruitHint = _canRecruit
                ? new HintViewModel(new TaleWorlds.Localization.TextObject($"Recruit one {_name} for {goldCost} gold"))
                : new HintViewModel(new TaleWorlds.Localization.TextObject(
                    !isReady ? $"Not yet ready — {_statusText}" : $"Not enough gold (need {goldCost})"));
        }

        public CharacterObject Character => _character;

        // ── Data-bound properties ─────────────────────────────────────────────────

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
        public string GoldCost
        {
            get => _goldCost;
            set { if (_goldCost != value) { _goldCost = value; OnPropertyChangedWithValue(value, nameof(GoldCost)); } }
        }

        [DataSourceProperty]
        public string Count
        {
            get => _count;
            set { if (_count != value) { _count = value; OnPropertyChangedWithValue(value, nameof(Count)); } }
        }

        [DataSourceProperty]
        public bool CanRecruit
        {
            get => _canRecruit;
            set { if (_canRecruit != value) { _canRecruit = value; OnPropertyChangedWithValue(value, nameof(CanRecruit)); } }
        }

        [DataSourceProperty]
        public bool IsReady
        {
            get => _isReady;
            set { if (_isReady != value) { _isReady = value; OnPropertyChangedWithValue(value, nameof(IsReady)); } }
        }

        [DataSourceProperty]
        public string StatusText
        {
            get => _statusText;
            set { if (_statusText != value) { _statusText = value; OnPropertyChangedWithValue(value, nameof(StatusText)); } }
        }

        [DataSourceProperty]
        public HintViewModel? RecruitHint
        {
            get => _recruitHint;
            set { if (_recruitHint != value) { _recruitHint = value; OnPropertyChangedWithValue(value, nameof(RecruitHint)); } }
        }

        // ── Commands ──────────────────────────────────────────────────────────────

        public void ExecuteRecruit()
        {
            if (!_canRecruit || !_isReady) return;

            var behavior = Byzantium1071.Campaign.Behaviors.B1071_CastleRecruitmentBehavior.Instance;
            if (behavior == null) return;

            bool success = _isElite
                ? behavior.TryRecruitElite(_castle, _character)
                : behavior.TryRecruitPrisoner(_castle, _character);

            if (success)
            {
                // Refresh the parent VM to reflect the change.
                _parent.RefreshLists();
            }
        }
    }
}
