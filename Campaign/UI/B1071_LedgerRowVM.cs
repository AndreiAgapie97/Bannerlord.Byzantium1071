using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using System.Linq;
using TaleWorlds.TwoDimension;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.UI
{
    /// <summary>
    /// ViewModel for a single ledger row. Each row has up to 5 cell values, an optional
    /// highlight flag (for player-faction rows), and an even/odd flag for zebra striping.
    /// Cell5 defaults to empty — only tabs that use a 5th column pass a value.
    /// The Hint property provides full untruncated values on hover.
    /// </summary>
    public sealed class B1071_LedgerRowVM : ViewModel
    {
        private B1071_LedgerColumns _columns = B1071_LedgerColumns.For(B1071LedgerTab.Current);
        private string _hintText = string.Empty;
        private string _cell1 = string.Empty;
        private string _cell2 = string.Empty;
        private string _cell3 = string.Empty;
        private string _cell4 = string.Empty;
        private string _cell5 = string.Empty;
        private bool _isHighlighted;
        private bool _isEven;
        private HintViewModel _hint = new HintViewModel();

        public B1071_LedgerRowVM() { }

        public B1071_LedgerRowVM(string c1, string c2, string c3, string c4, bool highlight, bool even, string c5 = "", string hintText = "", bool isDetail = false)
        {
            IsDetail = isDetail;
            _columns = B1071_OverlayController.Columns;
            _cell1 = c1;
            _cell2 = c2;
            _cell3 = c3;
            _cell4 = c4;
            _cell5 = c5;
            _isHighlighted = highlight;
            _isEven = even;
            SetHint(c1, c2, c3, c4, c5, hintText);
        }

        // Current's status and diagnostics are details, not values under the statistics headings.
        [DataSourceProperty] public bool IsDetail { get; private set; }
        [DataSourceProperty] public bool IsTableRow => !IsDetail;
        // Set before binding the first diagnostics row; compact mode keeps its original height.
        [DataSourceProperty] public bool BeginsDiagnostics { get; internal set; }
        [DataSourceProperty] public int FullScreenRowHeight => BeginsDiagnostics ? 44 : 28;
        [DataSourceProperty] public int FullScreenDetailTopMargin => BeginsDiagnostics ? 16 : 0;
        [DataSourceProperty] public string DetailLabel => B1071_DisplayMath.TruncateForColumn(_cell1, _columns.CharacterWidth > 7f ? 27 : 25);
        [DataSourceProperty] public string DetailText => B1071_DisplayMath.TruncateForColumn(
            string.Join("  |  ", new[] { _cell2, _cell3, _cell4, _cell5 }.Where(value => !string.IsNullOrEmpty(value))), _columns.CharacterWidth > 7f ? (int)((_columns.Widths.Sum() - 188f) / _columns.CharacterWidth) : 134);

        [DataSourceProperty]
        public string Cell1
        {
            get => B1071_DisplayMath.TruncateForColumn(_cell1, (int)(_columns.Widths[0] / _columns.CharacterWidth));
            set
            {
                if (_cell1 != value) { _cell1 = value; OnPropertyChangedWithValue(Cell1, nameof(Cell1)); }
            }
        }

        [DataSourceProperty]
        public string Cell2
        {
            get => B1071_DisplayMath.TruncateForColumn(_cell2, (int)(_columns.Widths[1] / _columns.CharacterWidth));
            set
            {
                if (_cell2 != value) { _cell2 = value; OnPropertyChangedWithValue(Cell2, nameof(Cell2)); }
            }
        }

        [DataSourceProperty]
        public string Cell3
        {
            get => B1071_DisplayMath.TruncateForColumn(_cell3, (int)(_columns.Widths[2] / _columns.CharacterWidth));
            set
            {
                if (_cell3 != value) { _cell3 = value; OnPropertyChangedWithValue(Cell3, nameof(Cell3)); }
            }
        }

        [DataSourceProperty]
        public string Cell4
        {
            get => B1071_DisplayMath.TruncateForColumn(_cell4, (int)(_columns.Widths[3] / _columns.CharacterWidth));
            set
            {
                if (_cell4 != value) { _cell4 = value; OnPropertyChangedWithValue(Cell4, nameof(Cell4)); }
            }
        }

        [DataSourceProperty]
        public string Cell5
        {
            get => B1071_DisplayMath.TruncateForColumn(_cell5, (int)(_columns.Widths[4] / _columns.CharacterWidth));
            set
            {
                if (_cell5 != value) { _cell5 = value; OnPropertyChangedWithValue(Cell5, nameof(Cell5)); }
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

        [DataSourceProperty]
        public HintViewModel Hint
        {
            get => _hint;
            set
            {
                if (_hint != value) { _hint = value; OnPropertyChangedWithValue(value, nameof(Hint)); }
            }
        }

        // Use boolean visibility with literal alignment in XML; enum data sources crash VM registration.
        [DataSourceProperty] public float Width1 => _columns.Widths[0];
        [DataSourceProperty] public bool Right2 => _columns.Alignments[1] == TextHorizontalAlignment.Right;
        [DataSourceProperty] public bool Left2 => !Right2;
        [DataSourceProperty] public float Width2 => _columns.Widths[1];
        [DataSourceProperty] public bool Right3 => _columns.Alignments[2] == TextHorizontalAlignment.Right;
        [DataSourceProperty] public bool Left3 => !Right3;
        [DataSourceProperty] public float Width3 => _columns.Widths[2];
        [DataSourceProperty] public bool Right4 => _columns.Alignments[3] == TextHorizontalAlignment.Right;
        [DataSourceProperty] public bool Left4 => !Right4;
        [DataSourceProperty] public float Width4 => _columns.Widths[3];
        [DataSourceProperty] public bool Right5 => _columns.Alignments[4] == TextHorizontalAlignment.Right;
        [DataSourceProperty] public bool Left5 => !Right5;
        [DataSourceProperty] public float Width5 => _columns.Widths[4];

        private void SetHint(string c1, string c2, string c3, string c4, string c5, string extra)
        {
            string text = string.Join("\n", new[] { c1, c2, c3, c4, c5 });
            if (!string.IsNullOrEmpty(extra) && !text.Contains(extra)) text += "\n" + extra;
            if (text == _hintText) return;
            _hintText = text;
            Hint = new HintViewModel(new TextObject("{=!}" + text));
        }

        /// <summary>Reuse this VM instance with new data to avoid GC churn.</summary>
        internal void Update(string c1, string c2, string c3, string c4, bool highlight, bool even, string c5 = "", string hintText = "")
        {
            B1071_LedgerColumns columns = B1071_OverlayController.Columns;
            if (!ReferenceEquals(_columns, columns))
            {
                _columns = columns;
                for (int i = 1; i <= 5; i++)
                {
                    OnPropertyChanged("Width" + i);
                    if (i > 1)
                    {
                        OnPropertyChanged("Left" + i);
                        OnPropertyChanged("Right" + i);
                    }
                    OnPropertyChanged("Cell" + i);
                }
            }
            Cell1 = c1;
            Cell2 = c2;
            Cell3 = c3;
            Cell4 = c4;
            Cell5 = c5;
            if (IsDetail)
            {
                OnPropertyChanged(nameof(DetailLabel));
                OnPropertyChanged(nameof(DetailText));
            }
            IsHighlighted = highlight;
            IsEven = even;
            SetHint(c1, c2, c3, c4, c5, hintText);
        }
    }
}
