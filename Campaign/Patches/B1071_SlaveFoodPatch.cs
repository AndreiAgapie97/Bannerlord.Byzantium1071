using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using HarmonyLib;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.Patches
{
    /// <summary>
    /// Slave population → town food drain.
    ///
    /// Postfix on DefaultSettlementFoodModel.CalculateTownFoodStocksChange.
    ///
    /// Each slave in the market or settlement stash consumes food daily.
    /// Stash holdings grant no market labor bonuses.
    /// This creates a natural economic cap on slave hoarding: at some point,
    /// the food cost of maintaining a large slave population exceeds the
    /// prosperity/construction benefits, forcing players and AI to balance
    /// their slave holdings.
    ///
    /// Formula: penalty = slaveCount × SlaveFoodConsumptionPerUnit
    ///
    /// At default settings (0.05 food/slave/day):
    ///   50 slaves  → -2.5 food/day  (noticeable)
    ///   100 slaves → -5.0 food/day  (significant — roughly a village's output)
    ///   200 slaves → -10.0 food/day (severe economic cap)
    ///
    /// This patch also works with non-default food models (e.g. EconomyOverhaul)
    /// via the dynamic patching system in B1071_DevastationBehavior.
    /// </summary>
    [HarmonyPatch(typeof(DefaultSettlementFoodModel), nameof(DefaultSettlementFoodModel.CalculateTownFoodStocksChange))]
    public static class B1071_SlaveFoodPatch
    {
        private static IB1071Settings Settings => B1071_TestHooks.Settings ?? B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        private static readonly TextObject _label = new TextObject("{=b1071_slave_food}Slave Upkeep");

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Town town, ref ExplainedNumber __result)
        {
            try
            {
                if (!Settings.EnableSlaveEconomy) return;
                if (town == null) return;

                var behavior = B1071_SlaveEconomyBehavior.Instance;
                if (behavior == null) return;

                int slaveCount = behavior.GetSlaveCountForTown(town) + behavior.GetSlaveCount(town.Settlement.Stash);
                if (slaveCount <= 0) return;

                float consumption = B1071_SlaveMath.FoodConsumption(slaveCount, Settings);
                if (consumption <= 0f) return;

                ApplyUpkeep(ref __result, consumption);
            }
            catch (Exception ex)
            {
                B1071_VerboseLog.Log("SlaveFood", ex.ToString());
            }
        }

        internal static void ApplyUpkeep(ref ExplainedNumber result, float consumption)
        {
            if (consumption <= 0f) return;
            // Preserve the resolved native breakdown: its existing factors must
            // not multiply the configured per-slave ration a second time.
            var adjusted = new ExplainedNumber(0f, result.IncludeDescriptions);
            adjusted.AddFromExplainedNumber(result, null);
            adjusted.Add(-consumption, _label);
            result = adjusted;
        }
    }
}
