using Byzantium1071.Campaign.Behaviors;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.UI
{
    /// <summary>
    /// One troop type waiting on a settlement's veteran register. The register groups by
    /// troop, not by man, so a row is "N of these are at home here, and this is what it
    /// costs to call them back".
    /// </summary>
    public sealed class B1071_VeteranRecallTroopVM : ViewModel
    {
        /// <summary>How many a shift-click acts on. Matches the troop service screen.</summary>
        private const int BatchAmount = 5;

        private readonly B1071_VeteranRecallVM _parent;
        private readonly Settlement _settlement;
        private readonly CharacterObject _character;
        private readonly int _available;

        private ImageIdentifierVM _visual;
        private string _name = string.Empty;
        private string _settlementName = string.Empty;
        private string _tier = string.Empty;
        private string _count = string.Empty;
        private string _daysLeft = string.Empty;
        private string _etaText = string.Empty;
        private string _goldCost = string.Empty;
        private string _manpowerCost = string.Empty;
        private string _recallText = string.Empty;
        private bool _isAlternateRow;
        private bool _showSettlement;
        private bool _canRecall;
        private HintViewModel? _recallHint;

        private static TextObject T(string id, string fallback) => new TextObject($"{{={id}}}{fallback}");

        private static string L(string id, string fallback) => T(id, fallback).ToString();

        public B1071_VeteranRecallTroopVM(B1071_VeteranRecallVM parent, B1071_DemobilizationBehavior.VeteranView row, bool isAlternateRow, bool showSettlement)
        {
            _parent = parent;
            _settlement = row.Settlement;
            _character = row.Troop;
            _available = row.Count;
            _isAlternateRow = isAlternateRow;
            _showSettlement = showSettlement;

            _visual = new CharacterImageIdentifierVM(CharacterCode.CreateFrom(row.Troop));
            _name = row.Troop.Name?.ToString() ?? L("b1071_ui_unknown", "Unknown");
            _settlementName = row.SettlementName;
            _tier = row.Tier.ToString();

            // Everyone on the register here, including the men still resting out their days.
            // They are at home whether or not they will answer today, and hiding them would
            // make the column disagree with the line at the top of the window.
            _count = (row.Count + row.RestingCount).ToString();
            _daysLeft = row.DaysUntilGone.ToString();
            _goldCost = row.GoldCostPerMan.ToString();
            _manpowerCost = row.ManpowerCostPerMan.ToString();
            _canRecall = row.CanRecallOne;

            // A dash rather than a zero when the player is standing in the place: the men
            // fall in as he clicks, so there is no journey to put a number on.
            _etaText = row.IsRemote ? row.EtaDays.ToString() : "—";

            // One labelled button instead of a row of bare numbers. Three buttons reading
            // "1 5 All" sat next to two numeric columns and read as more numbers; the word
            // says what the button does, and the modifiers say how many, exactly as on the
            // troop service screen.
            _recallText = L("b1071_recall_action", "Recall");

            if (_canRecall)
            {
                string hint = (row.IsRemote
                        ? T("b1071_recall_hint_remote", "Send for these men: {GOLD}g and {MANPOWER} manpower each, paid when the order goes out. Word has to reach {SETTLEMENT} first, then they march to you — about {ETA} day{EPLURAL} in all. Their service term starts over on arrival. They disperse in {DAYS} days if left alone.{NEWLINE}Click for one, Shift+click for {BATCH}, Ctrl+click for all {COUNT}.")
                            .SetTextVariable("ETA", row.EtaDays)
                            .SetTextVariable("EPLURAL", row.EtaDays == 1 ? string.Empty : "s")
                        : T("b1071_recall_hint", "Call these men back: {GOLD}g and {MANPOWER} manpower each. Their service term starts over, and {SETTLEMENT} becomes their home again. They disperse in {DAYS} days if left alone.{NEWLINE}Click for one, Shift+click for {BATCH}, Ctrl+click for all {COUNT}."))
                    .SetTextVariable("GOLD", row.GoldCostPerMan)
                    .SetTextVariable("MANPOWER", row.ManpowerCostPerMan)
                    .SetTextVariable("SETTLEMENT", row.SettlementName)
                    .SetTextVariable("DAYS", row.DaysUntilGone)
                    .SetTextVariable("BATCH", BatchAmount)
                    .SetTextVariable("COUNT", _available)
                    .SetTextVariable("NEWLINE", "\n")
                    .ToString();

                // The column counts every man at home, but only the rested ones will sign on,
                // so "all {COUNT}" is the smaller number. Say where the rest of them are, or
                // the button looks as though it is short-changing the player.
                if (row.RestingCount > 0)
                {
                    hint += "\n" + T("b1071_recall_hint_resting", "{RESTING} more are still resting from their last term and will not sign on for another {WAIT} day{WPLURAL}.")
                        .SetTextVariable("RESTING", row.RestingCount)
                        .SetTextVariable("WAIT", row.DaysUntilReady)
                        .SetTextVariable("WPLURAL", row.DaysUntilReady == 1 ? string.Empty : "s")
                        .ToString();
                }

                _recallHint = new HintViewModel(new TextObject(hint));
            }
            else
            {
                _recallHint = new HintViewModel(string.IsNullOrEmpty(row.BlockReason)
                    ? T("b1071_recall_hint_blocked", "These veterans cannot be called back right now.")
                    : new TextObject(row.BlockReason));
            }
        }

        /// <summary>
        /// How many men a click means. A register can hold dozens of one troop type, so the
        /// usual modifiers scale the action: plain click one, Shift a handful, Ctrl the lot.
        /// The recall itself trims the number again against gold, party room and manpower,
        /// so asking for all of them is always safe.
        /// </summary>
        private int AmountFromModifiers()
        {
            if (Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl))
                return _available;
            if (Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift))
                return Math.Min(BatchAmount, _available);
            return 1;
        }

        public void ExecuteRecall()
        {
            if (!_canRecall) return;

            int amount = AmountFromModifiers();
            if (amount <= 0) return;

            if (B1071_DemobilizationBehavior.Instance?.TryRecallVeterans(_settlement, _character, amount) > 0)
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
        public string SettlementName
        {
            get => _settlementName;
            set { if (_settlementName != value) { _settlementName = value; OnPropertyChangedWithValue(value, nameof(SettlementName)); } }
        }

        [DataSourceProperty]
        public bool ShowSettlement
        {
            get => _showSettlement;
            set { if (_showSettlement != value) { _showSettlement = value; OnPropertyChangedWithValue(value, nameof(ShowSettlement)); } }
        }

        [DataSourceProperty]
        public string EtaText
        {
            get => _etaText;
            set { if (_etaText != value) { _etaText = value; OnPropertyChangedWithValue(value, nameof(EtaText)); } }
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
        public string DaysLeft
        {
            get => _daysLeft;
            set { if (_daysLeft != value) { _daysLeft = value; OnPropertyChangedWithValue(value, nameof(DaysLeft)); } }
        }

        [DataSourceProperty]
        public string GoldCost
        {
            get => _goldCost;
            set { if (_goldCost != value) { _goldCost = value; OnPropertyChangedWithValue(value, nameof(GoldCost)); } }
        }

        [DataSourceProperty]
        public string ManpowerCost
        {
            get => _manpowerCost;
            set { if (_manpowerCost != value) { _manpowerCost = value; OnPropertyChangedWithValue(value, nameof(ManpowerCost)); } }
        }

        [DataSourceProperty]
        public string RecallText
        {
            get => _recallText;
            set { if (_recallText != value) { _recallText = value; OnPropertyChangedWithValue(value, nameof(RecallText)); } }
        }

        [DataSourceProperty]
        public bool IsAlternateRow
        {
            get => _isAlternateRow;
            set { if (_isAlternateRow != value) { _isAlternateRow = value; OnPropertyChangedWithValue(value, nameof(IsAlternateRow)); } }
        }

        [DataSourceProperty]
        public bool CanRecall
        {
            get => _canRecall;
            set { if (_canRecall != value) { _canRecall = value; OnPropertyChangedWithValue(value, nameof(CanRecall)); } }
        }

        [DataSourceProperty]
        public HintViewModel? RecallHint
        {
            get => _recallHint;
            set { if (_recallHint != value) { _recallHint = value; OnPropertyChangedWithValue(value, nameof(RecallHint)); } }
        }
    }
}
