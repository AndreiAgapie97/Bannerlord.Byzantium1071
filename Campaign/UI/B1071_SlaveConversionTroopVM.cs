using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.UI
{
    /// <summary>
    /// ViewModel for a single prisoner troop row in the slave conversion selection screen.
    /// Each row shows the troop name, tier, total count, and a selected count the player
    /// can adjust with +/- buttons.
    /// </summary>
    public sealed class B1071_SlaveConversionTroopVM : ViewModel
    {
        private readonly B1071_SlaveConversionVM _parent;
        private readonly CharacterObject _character;
        private readonly int _totalCount;

        private ImageIdentifierVM _visual;
        private string _name = string.Empty;
        private string _tier = string.Empty;
        private string _totalText = string.Empty;
        private int _selectedCount;
        private string _selectedText = string.Empty;

        public B1071_SlaveConversionTroopVM(
            B1071_SlaveConversionVM parent,
            CharacterObject character,
            int count)
        {
            _parent = parent;
            _character = character;
            _totalCount = count;
            _selectedCount = count; // Default: all selected

            _visual = new CharacterImageIdentifierVM(CharacterCode.CreateFrom(character));
            _name = character.Name?.ToString()
                    ?? new TextObject("{=b1071_ui_unknown}Unknown").ToString();
            _tier = character.Tier.ToString();
            _totalText = count.ToString();
            _selectedText = count.ToString();
        }

        public CharacterObject Character => _character;
        public int TotalCount => _totalCount;
        public int SelectedCount => _selectedCount;

        // ── Commands ──────────────────────────────────────────────────────────────

        public void ExecuteIncrement()
        {
            if (_selectedCount < _totalCount)
            {
                _selectedCount++;
                SelectedText = _selectedCount.ToString();
                _parent.RefreshTotalSelected();
            }
        }

        public void ExecuteDecrement()
        {
            if (_selectedCount > 0)
            {
                _selectedCount--;
                SelectedText = _selectedCount.ToString();
                _parent.RefreshTotalSelected();
            }
        }

        public void ExecuteSelectAll()
        {
            if (_selectedCount != _totalCount)
            {
                _selectedCount = _totalCount;
                SelectedText = _selectedCount.ToString();
                _parent.RefreshTotalSelected();
            }
        }

        public void ExecuteSelectNone()
        {
            if (_selectedCount != 0)
            {
                _selectedCount = 0;
                SelectedText = _selectedCount.ToString();
                _parent.RefreshTotalSelected();
            }
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
        public string TotalText
        {
            get => _totalText;
            set { if (_totalText != value) { _totalText = value; OnPropertyChangedWithValue(value, nameof(TotalText)); } }
        }

        [DataSourceProperty]
        public string SelectedText
        {
            get => _selectedText;
            set { if (_selectedText != value) { _selectedText = value; OnPropertyChangedWithValue(value, nameof(SelectedText)); } }
        }
    }
}
