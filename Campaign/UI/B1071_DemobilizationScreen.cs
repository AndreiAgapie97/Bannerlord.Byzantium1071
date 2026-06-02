using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ScreenSystem;

namespace Byzantium1071.Campaign.UI
{
    public sealed class B1071_DemobilizationScreen
    {
        private static B1071_DemobilizationScreen? _current;

        private ScreenBase? _parentScreen;
        private GauntletLayer? _gauntletLayer;
        private B1071_DemobilizationVM? _viewModel;

        public bool IsAlive => _gauntletLayer != null;

        private B1071_DemobilizationScreen(ScreenBase parentScreen)
        {
            _parentScreen = parentScreen;

            try
            {
                _viewModel = new B1071_DemobilizationVM(OnCloseRequested);
                _gauntletLayer = new GauntletLayer("B1071_Demobilization", 500);
                _gauntletLayer.LoadMovie("B1071_Demobilization", _viewModel);
                _gauntletLayer.InputRestrictions.SetInputRestrictions();
                _parentScreen.AddLayer(_gauntletLayer);
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=b1071_demob_open_fail}Service screen failed to open: {ERR}")
                        .SetTextVariable("ERR", ex.Message)
                        .ToString(), Colors.Red));
                Cleanup();
            }
        }

        internal static void Tick(float dt)
        {
            var settings = B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;
            if (!settings.EnableDemobilizationSystem || !settings.EnableDemobilizationHotkey) return;
            if (TaleWorlds.CampaignSystem.Campaign.Current == null || MobileParty.MainParty == null) return;

            if (Input.IsKeyPressed(GetConfiguredHotkey(settings.DemobilizationHotkeyChoice)))
                OpenScreen();
        }

        private static InputKey GetConfiguredHotkey(int choice)
        {
            switch (choice)
            {
                case 1: return InputKey.F10;
                case 2: return InputKey.F11;
                case 3: return InputKey.F12;
                default: return InputKey.F9;
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

        public static void OpenScreen()
        {
            if (_current != null && !_current.IsAlive)
                _current = null;

            if (_current != null) return;

            if (B1071_DemobilizationBehavior.Instance == null || MobileParty.MainParty == null)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=b1071_demob_no_campaign}Load a campaign before opening troop service.").ToString(), Colors.Red));
                return;
            }

            ScreenBase? screen = ScreenManager.TopScreen;
            if (screen == null)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=b1071_demob_no_screen}Troop service: no active screen.").ToString(), Colors.Red));
                return;
            }

            _current = new B1071_DemobilizationScreen(screen);
            if (!_current.IsAlive)
                _current = null;
        }

        internal static void Reset()
        {
            if (_current != null)
                _current.OnCloseRequested();
            _current = null;
        }
    }
}