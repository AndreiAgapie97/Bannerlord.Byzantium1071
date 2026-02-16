using System;
using HarmonyLib;
using Byzantium1071.Campaign.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;

namespace Byzantium1071.Campaign.Patches
{
    [HarmonyPatch(typeof(TooltipRefresherCollection), "RefreshSettlementTooltip")]
    internal static class B1071_SettlementTooltipManpowerPatch
    {
        private const string ManpowerLabel = "Manpower";

        [HarmonyPostfix]
        private static void Postfix(PropertyBasedTooltipVM propertyBasedTooltipVM, object[] args)
        {
            try
            {
                if (!IsMapTooltipContextReady()) return;

                var behavior = B1071_ManpowerBehavior.Instance;
                if (behavior == null || propertyBasedTooltipVM == null) return;

                Settlement? settlement = ResolveSettlement(args);
                if (settlement == null || settlement.IsHideout) return;

                behavior.GetManpowerPool(settlement, out int current, out int maximum, out _);
                propertyBasedTooltipVM.AddProperty(
                    ManpowerLabel,
                    $"{current}/{maximum}",
                    0,
                    TooltipProperty.TooltipPropertyFlags.None);
            }
            catch
            {
                // Keep campaign UI patch fail-safe.
            }
        }

        [HarmonyFinalizer]
        private static Exception? Finalizer(Exception? __exception)
        {
            if (__exception == null) return null;

            try
            {
                Debug.Print($"[Byzantium1071] Swallowed settlement tooltip exception: {__exception.GetType().Name}: {__exception.Message}");
            }
            catch
            {
            }

            // Tooltip should never hard-crash campaign load.
            return null;
        }

        private static bool IsMapTooltipContextReady()
        {
            Game? game = Game.Current;
            if (game == null) return false;

            var gsm = game.GameStateManager;
            if (gsm == null || gsm.ActiveState is not MapState) return false;

            TaleWorlds.CampaignSystem.Campaign? campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            if (campaign == null || campaign.MapSceneWrapper == null) return false;

            return true;
        }

        private static Settlement? ResolveSettlement(object[]? args)
        {
            if (args == null || args.Length == 0) return null;

            object? arg0 = args[0];
            return arg0 switch
            {
                Settlement settlement => settlement,
                Town town => town.Settlement,
                Village village => village.Settlement,
                _ => null
            };
        }
    }
}
