using Byzantium1071.Campaign.Behaviors;
using System;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace Byzantium1071.Campaign.UI
{
    /// <summary>
    /// Manages the castle recruitment Gauntlet layer — added on top of the current
    /// screen (MapScreen / GameMenu) so the map stays visible behind a
    /// semi-transparent overlay, matching the native recruitment popup pattern.
    /// Using PushScreen hides the map entirely (black background); adding a layer
    /// to the existing screen keeps the map dimmed but visible.
    /// </summary>
    public sealed class B1071_CastleRecruitmentScreen
    {
        private static B1071_CastleRecruitmentScreen? _current;

        private ScreenBase? _parentScreen;
        private GauntletLayer? _gauntletLayer;
        private B1071_CastleRecruitmentVM? _viewModel;

        private B1071_CastleRecruitmentScreen(ScreenBase parentScreen, Settlement castle)
        {
            _parentScreen = parentScreen;

            try
            {
                _viewModel = new B1071_CastleRecruitmentVM(castle, OnCloseRequested);
                _gauntletLayer = new GauntletLayer("B1071_CastleRecruitment", 500);
                _gauntletLayer.LoadMovie("B1071_CastleRecruitment", _viewModel);
                _gauntletLayer.InputRestrictions.SetInputRestrictions();
                _parentScreen.AddLayer(_gauntletLayer);
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TaleWorlds.Localization.TextObject("{=b1071_cr_open_fail}Castle recruit screen failed to open: {ERR}")
                        .SetTextVariable("ERR", ex.Message)
                        .ToString(),
                    Colors.Red));
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
        /// Opens the castle recruitment panel as a layer on the current screen.
        /// Called from <see cref="B1071_CastleRecruitmentBehavior"/>'s
        /// GameMenu consequence callback.
        /// </summary>
        public static void OpenScreen(Settlement castle)
        {
            if (_current != null) return; // Already open.

            var screen = ScreenManager.TopScreen;
            if (screen == null)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TaleWorlds.Localization.TextObject("{=b1071_cr_no_screen}Castle recruitment: no active screen.").ToString(), Colors.Red));
                return;
            }

            _current = new B1071_CastleRecruitmentScreen(screen, castle);
        }
    }
}
