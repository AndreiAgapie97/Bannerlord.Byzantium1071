using Byzantium1071.Campaign.Behaviors;
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace Byzantium1071.Campaign.UI
{
    /// <summary>
    /// ViewModel for a single troop row in the castle recruitment screen.
    /// Three states:
    ///   - Elite: CanRecruit = true (if gold sufficient), culture-based pool troop.
    ///   - Ready: CanRecruit = true, prisoner backed by sufficient conformity.
    ///   - Pending: CanRecruit = false, shows conformity toward the next recruit.
    /// </summary>
    public sealed class B1071_CastleRecruitTroopVM : ViewModel
    {
        private readonly B1071_CastleRecruitmentVM _parent;
        private readonly CharacterObject _character;
        private readonly Settlement _castle;
        private readonly bool _isElite;
        private int _numericCount;
        private bool _isEven;

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
            _count = count.ToString();
            _numericCount = count;
            _isReady = true;
            _recruitText = L("b1071_cr_recruit_one", "Recruit 1");

            // B-3: Same-clan lords pay 50% (family discount) — match backend TryRecruitElite logic.
            bool isSameClan = (Clan.PlayerClan == castle.OwnerClan);
            int effectiveCost = isSameClan ? goldCost / 2 : goldCost;
            _goldCost = FormatGoldCost(effectiveCost);
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
        /// Constructor for PRISONER troops. Legacy day arguments are retained for compatibility.
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
            _count = count.ToString();
            _numericCount = count;
            _isReady = isReady;
            _recruitText = L("b1071_cr_recruit_one", "Recruit 1");

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
                var progress = B1071_CastleRecruitmentBehavior.Instance?.GetPrisonerConformity(castle, character) ?? (0, 1);
                _statusText = TV("b1071_cr_conformity_points", "{POINTS} / {REQUIRED}",
                    ("POINTS", (progress.Item1 % Math.Max(1, progress.Item2)).ToString("N0")),
                    ("REQUIRED", progress.Item2.ToString("N0"))).ToString();
            }

            // Quote the next FIFO prisoner, not the undiscounted tier price.
            _goldCost = FormatGoldCost(hintEffectiveCost);

            _recruitHint = new HintViewModel(BuildPrisonerHint());
        }

        private TextObject BuildPrisonerHint()
        {
            var behavior = B1071_CastleRecruitmentBehavior.Instance;
            if (behavior == null) return new TextObject("{=!}" + _name);

            var progress = behavior.GetPrisonerConformity(_castle, _character);
            // Pending and ready rows may now share a type. Pending ownership describes
            // the whole type, so its quote still refers to the actual next FIFO recruit.
            int displayedCount = _numericCount + (_isReady ? 0 : progress.Points / Math.Max(1, progress.Required));
            var deposits = behavior.GetPrisonerDepositors(_castle.StringId, _character.StringId, displayedCount);
            var fee = behavior.GetPlayerPrisonerFeeBreakdown(_castle, _character);
            var lines = new List<string> { _name, _statusText };
            lines.Add(L("b1071_cr_conformity_help", "Each day the castle shares 240 conformity points plus 1.2 per point of governor Leadership among unfinished troop types. No party perks apply. Recruiting one prisoner spends that troop's requirement; higher-level troops need more points."));
            lines.Add(_isReady ? L("b1071_cr_depositors", "Deposited by:")
                : L("b1071_cr_all_type_depositors", "Deposited by (all prisoners of this type):"));
            // Aggregate only the display. The underlying batches and recruitment order stay intact.
            foreach (var group in deposits.GroupBy(entry => entry.HeroId))
                lines.Add(TV("b1071_cr_depositor_count", "{NAME}: {COUNT}",
                    ("NAME", DepositorName(group.Key)), ("COUNT", group.Sum(entry => entry.Count).ToString("N0"))).ToString());

            lines.Add(string.Empty);
            lines.Add(TV("b1071_cr_next_depositor", "Next depositor: {NAME}",
                ("NAME", DepositorName(deposits.FirstOrDefault().HeroId))).ToString());
            lines.Add(TV("b1071_cr_next_fee", "Next recruit: {COST}",
                ("COST", FormatGoldCost(fee.RecruiterCost))).ToString());
            lines.Add(TV("b1071_cr_fee_split", "Gold to castle owner: {OWNER}; to depositor: {DEPOSITOR}.",
                ("OWNER", fee.OwnerPayment.ToString("N0")), ("DEPOSITOR", fee.DepositorPayment.ToString("N0"))).ToString());
            lines.Add(L("b1071_cr_fee_rules", "Shares owed to your clan are waived. Without an eligible separate depositor, the fee belongs to the castle owner."));
            lines.Add(L("b1071_cr_fifo_note", "Recruitment follows deposit order. Later recruits may cost a different amount."));
            lines.Add(L("b1071_cr_withdraw_rule", "Troop types with recruitment fees still owed must stay in the dungeon. Recruit them through this menu. Deposits made in the open dungeon screen can still be undone."));
            if (!_isReady)
                lines.Add(L("b1071_cr_pending_quote", "This quote is for the next recruit of this type, including any already ready. Pending prisoners need more conformity; later recruits may have different fees."));
            else if (!_canRecruit)
                lines.Add(L("b1071_cr_unaffordable_next", "Not enough gold for the next recruit."));
            return new TextObject("{=!}" + string.Join("\n", lines));
        }

        private static string DepositorName(string? heroId)
        {
            if (string.IsNullOrEmpty(heroId)) return L("b1071_cr_depositor_unrecorded", "Unrecorded depositor");
            Hero? hero = MBObjectManager.Instance.GetObject<Hero>(heroId);
            if (hero == null) return L("b1071_cr_depositor_unknown", "Unknown depositor");
            if (hero == Hero.MainHero) return L("b1071_cr_depositor_you", "You");
            string name = hero.Name.ToString();
            if (hero.IsDead)
                return TV("b1071_cr_depositor_deceased", "{NAME} (deceased)", ("NAME", name)).ToString();
            if (hero.Clan == Clan.PlayerClan)
                return TV("b1071_cr_depositor_clan", "{NAME} (your clan)", ("NAME", name)).ToString();
            return hero.Clan == null ? name : TV("b1071_cr_depositor_affiliation", "{NAME} ({CLAN})",
                ("NAME", name), ("CLAN", hero.Clan.Name.ToString())).ToString();
        }

        public CharacterObject Character => _character;
        public int NumericCount => _numericCount;

        internal static string FormatGoldCost(int cost) => cost == 0
            ? L("b1071_cr_free", "Free") : cost.ToString("N0");

        // Notify existing bindings when the parent refreshes the row order.
        [DataSourceProperty]
        public bool IsEven
        {
            get => _isEven;
            set { if (_isEven != value) { _isEven = value; OnPropertyChangedWithValue(value, nameof(IsEven)); } }
        }

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
        public string RecruitAllText
        {
            get => L("b1071_cr_recruit_all", "Recruit All");
        }

        [DataSourceProperty]
        public HintViewModel? RecruitHint
        {
            get => _recruitHint;
            set { if (_recruitHint != value) { _recruitHint = value; OnPropertyChangedWithValue(value, nameof(RecruitHint)); } }
        }

        // ── Commands ──────────────────────────────────────────────────────────────

        public void ExecuteRecruit() => RecruitUnits(1);

        public void ExecuteRecruitAll() => RecruitUnits(_numericCount);

        private void RecruitUnits(int maximum)
        {
            if (!_canRecruit || !_isReady) return;

            var behavior = Byzantium1071.Campaign.Behaviors.B1071_CastleRecruitmentBehavior.Instance;
            if (behavior == null) return;

            int recruited = 0;
            try
            {
                // Each call rechecks the current price, stock, and applicable manpower requirement.
                while (recruited < maximum)
                {
                    bool success = _isElite
                        ? behavior.TryRecruitElite(_castle, _character)
                        : behavior.TryRecruitPrisoner(_castle, _character);
                    if (!success) break;
                    recruited++;
                }
            }
            finally
            {
                // Refresh once, including when a later attempt fails after partial success.
                if (recruited > 0) _parent.RefreshLists();
            }
        }
    }
}
