using System;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapBar;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ScreenSystem;

namespace Byzantium1071.Campaign.UI
{
    internal static class B1071_FullScreenLedger
    {
        private static ScreenBase? _parent;
        private static GauntletLayer? _layer;
        private static GauntletMovieIdentifier? _movie;
        private static MapBarVM? _pendingOpen;
        private static bool _pendingClose;
        private static bool _hideOnClose;

        internal static void RequestOpen(MapBarVM vm) => _pendingOpen = vm;
        internal static void RequestClose(bool hide)
        {
            _pendingClose = true;
            _hideOnClose = hide;
        }

        // Process movie changes outside Gauntlet's button dispatch and release input after
        // the Escape press frame, so it cannot also open the map's pause menu.
        internal static void Tick()
        {
            if (_pendingClose)
            {
                bool hide = _hideOnClose;
                Close();
                if (hide && B1071_OverlayController.IsVisible) B1071_OverlayController.ToggleVisibility();
                else B1071_OverlayController.RefreshNow();
                B1071_OverlayController._forceSyncCallback?.Invoke();
            }
            if (_pendingOpen != null)
            {
                MapBarVM vm = _pendingOpen;
                _pendingOpen = null;
                Open(vm);
            }
            if (_layer == null) return;
            if (_parent != ScreenManager.TopScreen || !B1071_OverlayController.IsVisible)
            {
                Close();
                return;
            }
            Resize();
            if (Input.IsKeyPressed(InputKey.Escape)) RequestClose(true);
        }

        private static void Open(MapBarVM vm)
        {
            if (_layer != null || ScreenManager.TopScreen == null) return;
            try
            {
                _parent = ScreenManager.TopScreen;
                _layer = new GauntletLayer("B1071_FullScreenLedger", 500);
                Resize();
                _movie = _layer.LoadMovie("B1071_FullScreenLedger", vm);
                _layer.InputRestrictions.SetInputRestrictions();
                _layer.IsFocusLayer = true;
                _parent.AddLayer(_layer);
                ScreenManager.TrySetFocus(_layer);
            }
            catch (Exception ex)
            {
                Close();
                B1071_OverlayController.RefreshNow();
                B1071_OverlayController._forceSyncCallback?.Invoke();
                InformationManager.DisplayMessage(new InformationMessage(new TextObject(
                    "{=b1071_ledger_fullscreen_error}Ledger could not open full screen: {ERROR}")
                    .SetTextVariable("ERROR", ex.Message).ToString(), Colors.Red));
            }
        }

        private static void Resize()
        {
            var context = _layer!.UIContext;
            if (B1071_OverlayController.SetFullScreenViewport(
                (int)(context.TwoDimensionContext.Width * context.CustomInverseScale),
                (int)(context.TwoDimensionContext.Height * context.CustomInverseScale)))
            {
                B1071_OverlayController.RefreshNow();
                B1071_OverlayController._forceSyncCallback?.Invoke();
            }
        }

        internal static void Close()
        {
            _pendingOpen = null;
            _pendingClose = false;
            if (_layer != null)
            {
                _layer.InputRestrictions.ResetInputRestrictions();
                _layer.IsFocusLayer = false;
                ScreenManager.TryLoseFocus(_layer);
                if (_movie != null) _layer.ReleaseMovie(_movie);
                _parent?.RemoveLayer(_layer);
            }
            _movie = null;
            _layer = null;
            _parent = null;
            // MapBarVM belongs to the map: releasing this movie must not finalize it.
            B1071_OverlayController.SetFullScreenViewport(0, 0);
        }
    }
}
