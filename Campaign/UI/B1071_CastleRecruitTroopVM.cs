using Byzantium1071.Campaign.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

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
        private int _numericCount;

        private ImageIdentifierVM _visual;
        private string _name = string.Empty;
        private string _tier = string.Empty;
        private string _goldCost = string.Empty;
        private string _count = string.Empty;
        private bool _canRecruit;
        private bool _isReady;
        private string _statusText = string.Empty;
        private string _recruitText = string.Empty;
        private HintViewModel? _recruitHint;

        private static TextObject T(string id, string fallback)
        {
            return new TextObject($"{{={id}}}{fallback}");
        }

        private static TextObject TV(string id, string fallback, params (string Name, string Value)[] vars)
        {
            var text = T(id, fallback);
            foreach (var (name, value) in vars)
            {
                text.SetTextVariable(name, value);
            }

            return text;
        }

        private static string L(string id, string fallback)
        {
            return T(id, fallback).ToString();
        }

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
            _name = character.Name?.ToString() ?? L("b1071_ui_unknown", "Unknown");
            _tier = character.Tier.ToString();
            _goldCost = goldCost.ToString();
            _count = count.ToString();
            _numericCount = count;
            _isReady = true;
            _recruitText = L("b1071_ui_recruit", "Recruit");

            // B-3: Same-clan lords pay 50% (family discount) — match backend TryRecruitElite logic.
            bool isSameClan = (Clan.PlayerClan == castle.OwnerClan);
            int effectiveCost = isSameClan ? goldCost / 2 : goldCost;
            _canRecruit = Hero.MainHero.Gold >= effectiveCost;
            _statusText = isSameClan
                ? L("b1071_cr_status_elite_discount", "Elite (50%)")
                : L("b1071_cr_status_elite", "Elite");

            _recruitHint = _canRecruit
                ? new HintViewModel(
                    isSameClan
                        ? TV("b1071_cr_hint_recruit_sameclan", "Recruit one {TROOP} (same clan — 50% cost: {COST} gold)",
                            ("TROOP", _name),
                            ("COST", effectiveCost.ToString()))
                        : TV("b1071_cr_hint_recruit_for_gold", "Recruit one {TROOP} for {COST} gold",
                            ("TROOP", _name),
                            ("COST", goldCost.ToString())))
                : new HintViewModel(TV("b1071_cr_hint_not_enough_gold", "Not enough gold (need {COST})", ("COST", effectiveCost.ToString())));
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
            _name = character.Name?.ToString() ?? L("b1071_ui_unknown", "Unknown");
            _tier = character.Tier.ToString();
            _goldCost = goldCost.ToString();
            _count = count.ToString();
            _numericCount = count;
            _isReady = isReady;
            _recruitText = L("b1071_ui_recruit", "Recruit");

            // Compute effective cost for hint text (used by both ready and pending branches).
            int hintEffectiveCost = goldCost;
            if (isReady)
            {
                // Use effective cost after clan waivers — match backend TryRecruitPrisoner logic.
                var behavior = B1071_CastleRecruitmentBehavior.Instance;
                hintEffectiveCost = behavior?.GetPlayerEffectivePrisonerCost(castle, character) ?? goldCost;
                _canRecruit = Hero.MainHero.Gold >= hintEffectiveCost;
                _statusText = hintEffectiveCost == 0
                    ? L("b1071_cr_status_ready_free", "Ready (Free)")
                    : L("b1071_cr_status_ready", "Ready");
            }
            else
            {
                _canRecruit = false;
                int remaining = daysRequired - daysHeld;
                _statusText = remaining > 0
                    ? TV("b1071_cr_status_days_remaining", "{DAYS} day{PLURAL} remaining",
                        ("DAYS", remaining.ToString()),
                        ("PLURAL", remaining == 1 ? string.Empty : "s")).ToString()
                    : L("b1071_cr_status_ready", "Ready");
            }

            _recruitHint = _canRecruit
                ? new HintViewModel(
                    hintEffectiveCost == 0
                        ? TV("b1071_cr_hint_recruit_clan_waiver", "Recruit one {TROOP} (clan waivers — free)",
                            ("TROOP", _name))
                        : TV("b1071_cr_hint_recruit_for_gold", "Recruit one {TROOP} for {COST} gold",
                            ("TROOP", _name),
                            ("COST", hintEffectiveCost.ToString())))
                : new HintViewModel(
                    !isReady
                        ? TV("b1071_cr_hint_not_ready", "Not yet ready — {STATUS}", ("STATUS", _statusText))
                        : TV("b1071_cr_hint_not_enough_gold", "Not enough gold (need {COST})", ("COST", hintEffectiveCost.ToString())));
        }

        public CharacterObject Character => _character;
        public int NumericCount => _numericCount;

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
        public string RecruitText
        {
            get => _recruitText;
            set { if (_recruitText != value) { _recruitText = value; OnPropertyChangedWithValue(value, nameof(RecruitText)); } }
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
