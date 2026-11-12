using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using System;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ScreenSystem;

namespace Byzantium1071.Campaign.UI
{
    /// <summary>
    /// Gauntlet layer for a settlement's veteran register. Added on top of the current
    /// screen rather than pushed, so the settlement menu stays visible behind it —
    /// same pattern as the castle recruitment and troop service screens.
    /// </summary>
    public sealed class B1071_VeteranRecallScreen
    {
        private static B1071_VeteranRecallScreen? _current;

        private ScreenBase? _parentScreen;
        private GauntletLayer? _gauntletLayer;
        private B1071_VeteranRecallVM? _viewModel;

        public bool IsAlive => _gauntletLayer != null;

        private B1071_VeteranRecallScreen(ScreenBase parentScreen, Settlement? settlement)
        {
            _parentScreen = parentScreen;

            try
            {
                _viewModel = new B1071_VeteranRecallVM(settlement, OnCloseRequested);
                _gauntletLayer = new GauntletLayer("B1071_VeteranRecall", 500);
                _gauntletLayer.LoadMovie("B1071_VeteranRecall", _viewModel);
                _gauntletLayer.InputRestrictions.SetInputRestrictions();
                _parentScreen.AddLayer(_gauntletLayer);
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=b1071_recall_open_fail}Veteran register failed to open: {ERR}")
                        .SetTextVariable("ERR", ex.Message)
                        .ToString(), Colors.Red));
                Cleanup();
            }
        }

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
        /// The register of every settlement the player may draw from, opened from the campaign
        /// map. Recalls placed here are sent by courier rather than answered on the spot.
        /// </summary>
        public static void OpenScreen() => OpenScreen(null);

        public static void OpenScreen(Settlement? settlement)
        {
            if (_current != null && !_current.IsAlive)
                _current = null;

            if (_current != null) return;

            if (B1071_DemobilizationBehavior.Instance == null || MobileParty.MainParty == null)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=b1071_recall_no_campaign}Load a campaign before opening the veteran register.").ToString(), Colors.Red));
                return;
            }

            ScreenBase? screen = ScreenManager.TopScreen;
            if (screen == null)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=b1071_recall_no_screen}Veteran register: no active screen.").ToString(), Colors.Red));
                return;
            }

            B1071_VerboseLog.Log("Demobilization", $"Opening veteran register for {settlement?.StringId ?? "<map-wide>"} on top screen '{screen.GetType().Name}'.");
            _current = new B1071_VeteranRecallScreen(screen, settlement);
            if (!_current.IsAlive)
            {
                _current = null;
                B1071_VerboseLog.Log("Demobilization", "Veteran register failed to initialise (Gauntlet layer null after construction).");
            }
        }

        /// <summary>
        /// Watches for the register hotkey on the campaign map, the same way the troop service
        /// screen watches for its own. Driven from SubModule.OnApplicationTick.
        /// </summary>
        internal static void Tick(float dt)
        {
            var settings = B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;
            if (!settings.EnableDemobilizationSystem || !settings.EnableDemobilizationVeteranReturn) return;
            if (!settings.EnableVeteranRecallHotkey) return;
            if (TaleWorlds.CampaignSystem.Campaign.Current == null || MobileParty.MainParty == null) return;
            if (B1071_HotkeyGuard.BlocksPanelHotkey()) return;

            InputKey hotkey = GetConfiguredHotkey(settings.VeteranRecallHotkeyChoice);
            if (Input.IsKeyPressed(hotkey))
            {
                B1071_VerboseLog.Log("Demobilization", $"Register hotkey {hotkey} detected; opening map-wide veteran register.");
                OpenScreen();
            }
        }

        private static InputKey GetConfiguredHotkey(int choice)
        {
            switch (choice)
            {
                case 1: return InputKey.F10;
                case 2: return InputKey.F11;
                case 3: return InputKey.F12;
                default: return InputKey.F8;
            }
        }

        internal static void Reset()
        {
            if (_current != null)
                _current.OnCloseRequested();
            _current = null;
        }
    }
}
