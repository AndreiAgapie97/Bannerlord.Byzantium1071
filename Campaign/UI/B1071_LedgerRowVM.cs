using TaleWorlds.Library;

namespace Byzantium1071.Campaign.UI
{
    /// <summary>
    /// ViewModel for a single ledger row. Each row has 4 cell values, an optional
    /// highlight flag (for player-faction rows), and an even/odd flag for zebra striping.
    /// </summary>
    public sealed class B1071_LedgerRowVM : ViewModel
    {
        private string _cell1 = string.Empty;
        private string _cell2 = string.Empty;
        private string _cell3 = string.Empty;
        private string _cell4 = string.Empty;
        private bool _isHighlighted;
        private bool _isEven;

        public B1071_LedgerRowVM() { }

        public B1071_LedgerRowVM(string c1, string c2, string c3, string c4, bool highlight, bool even)
        {
            _cell1 = c1;
            _cell2 = c2;
            _cell3 = c3;
            _cell4 = c4;
            _isHighlighted = highlight;
            _isEven = even;
        }

        [DataSourceProperty]
        public string Cell1
        {
            get => _cell1;
            set
            {
                if (_cell1 != value) { _cell1 = value; OnPropertyChangedWithValue(value, nameof(Cell1)); }
            }
        }

        [DataSourceProperty]
        public string Cell2
        {
            get => _cell2;
            set
            {
                if (_cell2 != value) { _cell2 = value; OnPropertyChangedWithValue(value, nameof(Cell2)); }
            }
        }

        [DataSourceProperty]
        public string Cell3
        {
            get => _cell3;
            set
            {
                if (_cell3 != value) { _cell3 = value; OnPropertyChangedWithValue(value, nameof(Cell3)); }
            }
        }

        [DataSourceProperty]
        public string Cell4
        {
            get => _cell4;
            set
            {
                if (_cell4 != value) { _cell4 = value; OnPropertyChangedWithValue(value, nameof(Cell4)); }
            }
        }

        [DataSourceProperty]
        public bool IsHighlighted
        {
            get => _isHighlighted;
            set
            {
                if (_isHighlighted != value) { _isHighlighted = value; OnPropertyChangedWithValue(value, nameof(IsHighlighted)); }
            }
        }

        [DataSourceProperty]
        public bool IsEven
        {
            get => _isEven;
            set
            {
                if (_isEven != value) { _isEven = value; OnPropertyChangedWithValue(value, nameof(IsEven)); }
            }
        }

        /// <summary>Reuse this VM instance with new data to avoid GC churn.</summary>
        internal void Update(string c1, string c2, string c3, string c4, bool highlight, bool even)
        {
            Cell1 = c1;
            Cell2 = c2;
            Cell3 = c3;
            Cell4 = c4;
            IsHighlighted = highlight;
            IsEven = even;
        }
    }
}
