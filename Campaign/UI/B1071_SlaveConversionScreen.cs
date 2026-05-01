using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace Byzantium1071.Campaign.UI
{
    /// <summary>
    /// Manages the slave conversion selection Gauntlet layer — added on top of the
    /// current screen so the map stays visible behind a semi-transparent overlay,
    /// matching the castle recruitment popup pattern.
    /// </summary>
    public sealed class B1071_SlaveConversionScreen
    {
        private static B1071_SlaveConversionScreen? _current;

        private ScreenBase? _parentScreen;
        private GauntletLayer? _gauntletLayer;
        private B1071_SlaveConversionVM? _viewModel;

        private B1071_SlaveConversionScreen(
            ScreenBase parentScreen,
            Action<Dictionary<CharacterObject, int>>? onConfirm)
        {
            _parentScreen = parentScreen;

            try
            {
                _viewModel = new B1071_SlaveConversionVM(OnCloseRequested, onConfirm);
                _gauntletLayer = new GauntletLayer("B1071_SlaveConversion", 500);
                _gauntletLayer.LoadMovie("B1071_SlaveConversion", _viewModel);
                _gauntletLayer.InputRestrictions.SetInputRestrictions();
                _parentScreen.AddLayer(_gauntletLayer);
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TaleWorlds.Localization.TextObject("{=b1071_sc_open_fail}Slave conversion screen failed to open: {ERR}")
                        .SetTextVariable("ERR", ex.Message)
                        .ToString(),
                    Colors.Red));
                Cleanup();
            }
        }

        /// <summary>
        /// Whether the screen opened successfully and has a live Gauntlet layer.
        /// </summary>
        private bool IsAlive => _gauntletLayer != null;

        private void OnCloseRequested()
        {
            try
            {
                if (_gauntletLayer != null && _parentScreen != null)
                {
                    _gauntletLayer.InputRestrictions.ResetInputRestrictions();
                    _parentScreen.RemoveLayer(_gauntletLayer);
                }
            }
            catch (Exception)
            {
                // Parent screen may have been popped in the meantime; ignore.
            }

            Cleanup();
        }

        private void Cleanup()
        {
            _gauntletLayer = null;
            _viewModel?.OnFinalize();
            _viewModel = null;
            _parentScreen = null;
            _current = null;
        }

        /// <summary>
        /// Opens the slave conversion selection panel as a layer on the current screen.
        /// Called from <see cref="Behaviors.B1071_SlaveEconomyBehavior"/>'s
        /// enslave menu consequence when selection mode is enabled.
        /// </summary>
        public static void OpenScreen(Action<Dictionary<CharacterObject, int>>? onConfirm)
        {
            // If a previous instance died (layer destroyed externally), clear it.
            if (_current != null && !_current.IsAlive)
                _current = null;

            if (_current != null) return; // Already open.

            var screen = ScreenManager.TopScreen;
            if (screen == null)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TaleWorlds.Localization.TextObject("{=b1071_sc_no_screen}Slave conversion: no active screen.").ToString(),
                    Colors.Red));
                return;
            }

            _current = new B1071_SlaveConversionScreen(screen, onConfirm);

            // If construction failed (caught exception → Cleanup ran), reset singleton.
            if (!_current.IsAlive)
                _current = null;
        }
    }
}
