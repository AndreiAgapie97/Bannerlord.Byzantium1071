using System.Reflection;
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
    internal static class B1071_SettlementTooltipManpowerPatch
    {
        private static bool EnableSettlementTooltipPatch => false;
        private static bool _patchApplied;
        private static bool _loggedFirstInjection;

        internal static void TryEnableAndPatch(Harmony harmony)
        {
            if (!EnableSettlementTooltipPatch) return;
            if (_patchApplied || harmony == null) return;

            MethodInfo? original = AccessTools.Method(
                typeof(TooltipRefresherCollection),
                "RefreshSettlementTooltip",
                new[] { typeof(PropertyBasedTooltipVM), typeof(object[]) });

            MethodInfo? prefix = AccessTools.Method(typeof(B1071_SettlementTooltipManpowerPatch), nameof(Prefix));
            MethodInfo? postfix = AccessTools.Method(typeof(B1071_SettlementTooltipManpowerPatch), nameof(Postfix));

            if (original == null || prefix == null || postfix == null) return;

            harmony.Patch(original, new HarmonyMethod(prefix), new HarmonyMethod(postfix));
            _patchApplied = true;

            try
            {
                Debug.Print("[Byzantium1071] Settlement tooltip manpower patch applied.");
            }
            catch
            {
            }
        }

        private const string ManpowerLabel = "Manpower";
        private static readonly FieldInfo? GameTextManagerField =
            typeof(GameTexts).GetField("_gameTextManager", BindingFlags.NonPublic | BindingFlags.Static);

        private static bool Prefix(PropertyBasedTooltipVM propertyBasedTooltipVM, object[] __args)
        {
            if (propertyBasedTooltipVM == null) return false;
            if (!IsMapTooltipContextReady()) return false;
            if (!IsGameTextSystemReady()) return false;

            Settlement? settlement = ResolveSettlement(__args);
            return settlement != null && !settlement.IsHideout;
        }

        private static void Postfix(PropertyBasedTooltipVM propertyBasedTooltipVM, object[] __args, bool __runOriginal)
        {
            try
            {
                if (!__runOriginal) return;
                if (!IsMapTooltipContextReady()) return;
                if (!IsGameTextSystemReady()) return;

                var behavior = B1071_ManpowerBehavior.Instance;
                if (behavior == null || propertyBasedTooltipVM == null) return;

                Settlement? settlement = ResolveSettlement(__args);
                if (settlement == null || settlement.IsHideout) return;

                behavior.GetManpowerPool(settlement, out int current, out int maximum, out _);
                propertyBasedTooltipVM.AddProperty(
                    ManpowerLabel,
                    $"{current}/{maximum}",
                    0,
                    TooltipProperty.TooltipPropertyFlags.None);

                if (!_loggedFirstInjection)
                {
                    _loggedFirstInjection = true;
                    try
                    {
                        Debug.Print($"[Byzantium1071] Manpower tooltip injected for {settlement.StringId}: {current}/{maximum}");
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
                // Keep campaign UI patch fail-safe.
            }
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

        private static bool IsGameTextSystemReady()
        {
            try
            {
                return GameTextManagerField?.GetValue(null) != null;
            }
            catch
            {
                return false;
            }
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
