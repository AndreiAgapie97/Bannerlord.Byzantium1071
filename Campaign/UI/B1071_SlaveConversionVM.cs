using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.UI
{
    /// <summary>
    /// ViewModel for the slave conversion selection screen.
    /// Lists all T1-T3 prisoner stacks with per-stack quantity controls.
    /// The player can select which prisoners to enslave and confirm.
    /// </summary>
    public sealed class B1071_SlaveConversionVM : ViewModel
    {
        private Action? _onClose;
        private Action<Dictionary<CharacterObject, int>>? _onConfirm;

        private string _titleText = string.Empty;
        private string _troopColumnText = string.Empty;
        private string _tierColumnText = string.Empty;
        private string _totalColumnText = string.Empty;
        private string _selectedColumnText = string.Empty;
        private string _confirmText = string.Empty;
        private string _selectAllText = string.Empty;
        private string _deselectAllText = string.Empty;
        private string _totalSelectedText = string.Empty;
        private MBBindingList<B1071_SlaveConversionTroopVM> _troops;
        private bool _hasTroops;
        private bool _canConfirm;

        public B1071_SlaveConversionVM(
            Action? onClose,
            Action<Dictionary<CharacterObject, int>>? onConfirm)
        {
            _onClose = onClose;
            _onConfirm = onConfirm;
            _troops = new MBBindingList<B1071_SlaveConversionTroopVM>();

            RefreshLocalizedLabels();
            RefreshList();
        }

        private static string L(string id, string fallback)
            => new TextObject($"{{={id}}}{fallback}").ToString();

        private void RefreshLocalizedLabels()
        {
            int maxTier = (B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults).CastlePrisonerAutoEnslaveTierMax;
            TitleText = new TextObject("{=b1071_sc_title}Select Prisoners to Enslave (T1-{MAXTIER})")
                .SetTextVariable("MAXTIER", maxTier)
                .ToString();
            TroopColumnText = L("b1071_ui_troop", "Troop");
            TierColumnText = L("b1071_ui_tier", "Tier");
            TotalColumnText = L("b1071_sc_total", "Total");
            SelectedColumnText = L("b1071_sc_selected", "Selected");
            ConfirmText = L("b1071_sc_confirm", "Enslave Selected");
            SelectAllText = L("b1071_sc_select_all", "Select All");
            DeselectAllText = L("b1071_sc_deselect_all", "Deselect All");
        }

        private void RefreshList()
        {
            _troops.Clear();

            var roster = MobileParty.MainParty?.PrisonRoster;
            if (roster == null) { HasTroops = false; return; }

            int maxTier = (B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults).CastlePrisonerAutoEnslaveTierMax;

            foreach (var element in roster.GetTroopRoster())
            {
                if (element.Character != null && !element.Character.IsHero
                    && element.Number > 0 && element.Character.Tier <= maxTier)
                {
                    _troops.Add(new B1071_SlaveConversionTroopVM(this, element.Character, element.Number));
                }
            }

            HasTroops = _troops.Count > 0;
            RefreshTotalSelected();
        }

        /// <summary>
        /// Called by child VMs when their selected count changes.
        /// </summary>
        public void RefreshTotalSelected()
        {
            int total = 0;
            foreach (var vm in _troops)
                total += vm.SelectedCount;

            TotalSelectedText = new TextObject("{=b1071_sc_total_selected}{COUNT} prisoner{PLURAL} selected")
                .SetTextVariable("COUNT", total)
                .SetTextVariable("PLURAL", total != 1 ? "s" : string.Empty)
                .ToString();
            CanConfirm = total > 0;
        }

        // ── Commands ──────────────────────────────────────────────────────────────

        public void ExecuteClose()
        {
            _onClose?.Invoke();
        }

        public void ExecuteConfirm()
        {
            var selections = new Dictionary<CharacterObject, int>();
            foreach (var vm in _troops)
            {
                if (vm.SelectedCount > 0)
                    selections[vm.Character] = vm.SelectedCount;
            }

            if (selections.Count > 0)
                _onConfirm?.Invoke(selections);

            _onClose?.Invoke();
        }

        public void ExecuteSelectAll()
        {
            foreach (var vm in _troops)
                vm.ExecuteSelectAll();
            RefreshTotalSelected();
        }

        public void ExecuteDeselectAll()
        {
            foreach (var vm in _troops)
                vm.ExecuteSelectNone();
            RefreshTotalSelected();
        }

        // ── Data-bound properties ─────────────────────────────────────────────────

        [DataSourceProperty]
        public string TitleText
        {
            get => _titleText;
            set { if (_titleText != value) { _titleText = value; OnPropertyChangedWithValue(value, nameof(TitleText)); } }
        }

        [DataSourceProperty]
        public string TroopColumnText
        {
            get => _troopColumnText;
            set { if (_troopColumnText != value) { _troopColumnText = value; OnPropertyChangedWithValue(value, nameof(TroopColumnText)); } }
        }

        [DataSourceProperty]
        public string TierColumnText
        {
            get => _tierColumnText;
            set { if (_tierColumnText != value) { _tierColumnText = value; OnPropertyChangedWithValue(value, nameof(TierColumnText)); } }
        }

        [DataSourceProperty]
        public string TotalColumnText
        {
            get => _totalColumnText;
            set { if (_totalColumnText != value) { _totalColumnText = value; OnPropertyChangedWithValue(value, nameof(TotalColumnText)); } }
        }

        [DataSourceProperty]
        public string SelectedColumnText
        {
            get => _selectedColumnText;
            set { if (_selectedColumnText != value) { _selectedColumnText = value; OnPropertyChangedWithValue(value, nameof(SelectedColumnText)); } }
        }

        [DataSourceProperty]
        public string ConfirmText
        {
            get => _confirmText;
            set { if (_confirmText != value) { _confirmText = value; OnPropertyChangedWithValue(value, nameof(ConfirmText)); } }
        }

        [DataSourceProperty]
        public string SelectAllText
        {
            get => _selectAllText;
            set { if (_selectAllText != value) { _selectAllText = value; OnPropertyChangedWithValue(value, nameof(SelectAllText)); } }
        }

        [DataSourceProperty]
        public string DeselectAllText
        {
            get => _deselectAllText;
            set { if (_deselectAllText != value) { _deselectAllText = value; OnPropertyChangedWithValue(value, nameof(DeselectAllText)); } }
        }

        [DataSourceProperty]
        public string TotalSelectedText
        {
            get => _totalSelectedText;
            set { if (_totalSelectedText != value) { _totalSelectedText = value; OnPropertyChangedWithValue(value, nameof(TotalSelectedText)); } }
        }

        [DataSourceProperty]
        public MBBindingList<B1071_SlaveConversionTroopVM> Troops
        {
            get => _troops;
            set { if (_troops != value) { _troops = value; OnPropertyChangedWithValue(value, nameof(Troops)); } }
        }

        [DataSourceProperty]
        public bool HasTroops
        {
            get => _hasTroops;
            set { if (_hasTroops != value) { _hasTroops = value; OnPropertyChangedWithValue(value, nameof(HasTroops)); } }
        }

        [DataSourceProperty]
        public bool CanConfirm
        {
            get => _canConfirm;
            set { if (_canConfirm != value) { _canConfirm = value; OnPropertyChangedWithValue(value, nameof(CanConfirm)); } }
        }
    }
}
