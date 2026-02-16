using TaleWorlds.Library;

namespace Byzantium1071.Campaign.UI
{
    internal sealed class B1071_OverlayVM : ViewModel
    {
        private bool _isVisible;
        private bool _isExpanded;
        private string _titleText = "Byzantium 1071";
        private string _contentText = string.Empty;
        private string _toggleText = "Byz 1071";

        [DataSourceProperty]
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (value == _isVisible) return;
                _isVisible = value;
                OnPropertyChangedWithValue(value, nameof(IsVisible));
            }
        }

        [DataSourceProperty]
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (value == _isExpanded) return;
                _isExpanded = value;
                OnPropertyChangedWithValue(value, nameof(IsExpanded));
                ToggleText = value ? "Hide" : "Byz 1071";
            }
        }

        [DataSourceProperty]
        public string TitleText
        {
            get => _titleText;
            set
            {
                if (value == _titleText) return;
                _titleText = value;
                OnPropertyChangedWithValue(value, nameof(TitleText));
            }
        }

        [DataSourceProperty]
        public string ContentText
        {
            get => _contentText;
            set
            {
                if (value == _contentText) return;
                _contentText = value;
                OnPropertyChangedWithValue(value, nameof(ContentText));
            }
        }

        [DataSourceProperty]
        public string ToggleText
        {
            get => _toggleText;
            set
            {
                if (value == _toggleText) return;
                _toggleText = value;
                OnPropertyChangedWithValue(value, nameof(ToggleText));
            }
        }

        public void ExecuteToggleExpanded()
        {
            IsExpanded = !IsExpanded;
        }
    }
}
