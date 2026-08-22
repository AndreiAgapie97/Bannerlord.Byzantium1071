using SandBox.View.Map;
using System;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.Core;
using TaleWorlds.ScreenSystem;

namespace Byzantium1071.Campaign.UI
{
    /// <summary>
    /// One shared answer to "should a panel hotkey fire right now?".
    ///
    /// The mod's screens listen for their key every frame from the application tick, which
    /// hears the key wherever the player happens to be — in the encyclopedia, in the save
    /// list, in the middle of a battle. Pressing F9 in the encyclopedia used to build the
    /// troop service window onto the map underneath it, where it sat unreachable until the
    /// encyclopedia was closed. The keys only mean anything on the campaign map, so that is
    /// where they are allowed to work.
    ///
    /// A settlement menu is deliberately still fair game: reading the register while standing
    /// in the town menu is one of the things these screens are for.
    /// </summary>
    internal static class B1071_HotkeyGuard
    {
        /// <summary>True when a panel hotkey should be ignored this frame.</summary>
        internal static bool BlocksPanelHotkey()
        {
            try
            {
                Game? game = Game.Current;
                if (game == null) return true;
                if (game.GameType is not TaleWorlds.CampaignSystem.Campaign) return true;

                // Battles, conversations, the inventory, the clan and kingdom screens: all of
                // them are a different game state, and none of them is a place to open a map
                // panel behind.
                if (game.GameStateManager?.ActiveState is not MapState) return true;

                return !IsMapInFront();
            }
            catch (Exception)
            {
                // A guard that throws must not be the thing that swallows the hotkey. If the
                // check cannot be made, let the key through as it always used to.
                return false;
            }
        }

        /// <summary>
        /// Whether the map itself is the screen the player is looking at. Kept in its own
        /// method so the SandBox view type is only loaded when this is actually called,
        /// and any failure to load it lands inside the caller's catch.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool IsMapInFront()
        {
            // The map state is still active while the encyclopedia or the save list is up —
            // they are pushed over the map rather than replacing it — so the state check
            // alone is not enough.
            if (ScreenManager.TopScreen is not MapScreen map) return false;

            // The escape menu is a layer on the map screen, so it passes everything above.
            return !map.IsEscapeMenuOpened;
        }
    }
}
