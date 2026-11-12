using Byzantium1071.Campaign.Behaviors;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.UI
{
    /// <summary>
    /// One recall order still on the road: men who have been paid for and are making their
    /// way to the player. They are not a party on the map, only a promise with a date on it,
    /// which is why the row can say where they came from but not point at them.
    /// </summary>
    public sealed class B1071_VeteranRecallTransitVM : ViewModel
    {
        private readonly B1071_VeteranRecallVM _parent;

        /// <summary>The order's own handle, not its position in the list — rows move.</summary>
        private readonly int _orderId;
        private readonly string _settlementId;
        private readonly string _troopId;

        private ImageIdentifierVM _visual;
        private string _name = string.Empty;
        private string _settlementName = string.Empty;
        private string _count = string.Empty;
        private string _status = string.Empty;
        private string _goldPaid = string.Empty;
        private string _cancelText = string.Empty;
        private bool _isAlternateRow;
        private HintViewModel? _cancelHint;

        private static TextObject T(string id, string fallback) => new TextObject($"{{={id}}}{fallback}");

        private static string L(string id, string fallback) => T(id, fallback).ToString();

        public B1071_VeteranRecallTransitVM(B1071_VeteranRecallVM parent, B1071_DemobilizationBehavior.PendingRecallView row, bool isAlternateRow)
        {
            _parent = parent;
            _orderId = row.OrderId;
            _settlementId = row.SettlementId;
            _troopId = row.TroopId;
            _isAlternateRow = isAlternateRow;

            _visual = new CharacterImageIdentifierVM(CharacterCode.CreateFrom(row.Troop));
            _name = row.Troop.Name?.ToString() ?? L("b1071_ui_unknown", "Unknown");
            _settlementName = row.SettlementName;
            _count = row.Count.ToString();
            _goldPaid = row.GoldPaid.ToString();
            _cancelText = L("b1071_recall_cancel", "Call Off");

            // The status line is the whole point of the row, so it says the one thing the
            // player wants to know: are they still being sent for, walking, or held up.
            if (!string.IsNullOrEmpty(row.HoldReason))
                _status = row.HoldReason;
            else if (row.CourierStillRiding)
                _status = T("b1071_recall_status_courier", "Rider on the way — about {DAYS} day{DPLURAL}")
                    .SetTextVariable("DAYS", row.EtaDays)
                    .SetTextVariable("DPLURAL", row.EtaDays == 1 ? string.Empty : "s")
                    .ToString();
            else
                _status = T("b1071_recall_status_marching", "Marching to you — about {DAYS} day{DPLURAL}")
                    .SetTextVariable("DAYS", row.EtaDays)
                    .SetTextVariable("DPLURAL", row.EtaDays == 1 ? string.Empty : "s")
                    .ToString();

            _cancelHint = new HintViewModel(
                T("b1071_recall_cancel_hint", "Call off the order. The men go back on the register at {SETTLEMENT} and the manpower is returned.{NEWLINE}The {COST}g bounty is not returned — it was paid when the order went out.")
                    .SetTextVariable("SETTLEMENT", row.SettlementName)
                    .SetTextVariable("COST", row.GoldPaid)
                    .SetTextVariable("NEWLINE", "\n"));
        }

        public void ExecuteCancel()
        {
            if (B1071_DemobilizationBehavior.Instance?.TryCancelPendingRecall(_orderId, _settlementId, _troopId) == true)
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
        public string Count
        {
            get => _count;
            set { if (_count != value) { _count = value; OnPropertyChangedWithValue(value, nameof(Count)); } }
        }

        [DataSourceProperty]
        public string Status
        {
            get => _status;
            set { if (_status != value) { _status = value; OnPropertyChangedWithValue(value, nameof(Status)); } }
        }

        [DataSourceProperty]
        public string GoldPaid
        {
            get => _goldPaid;
            set { if (_goldPaid != value) { _goldPaid = value; OnPropertyChangedWithValue(value, nameof(GoldPaid)); } }
        }

        [DataSourceProperty]
        public string CancelText
        {
            get => _cancelText;
            set { if (_cancelText != value) { _cancelText = value; OnPropertyChangedWithValue(value, nameof(CancelText)); } }
        }

        [DataSourceProperty]
        public bool IsAlternateRow
        {
            get => _isAlternateRow;
            set { if (_isAlternateRow != value) { _isAlternateRow = value; OnPropertyChangedWithValue(value, nameof(IsAlternateRow)); } }
        }

        [DataSourceProperty]
        public HintViewModel? CancelHint
        {
            get => _cancelHint;
            set { if (_cancelHint != value) { _cancelHint = value; OnPropertyChangedWithValue(value, nameof(CancelHint)); } }
        }
    }
}
