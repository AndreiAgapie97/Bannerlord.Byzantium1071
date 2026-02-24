using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using HarmonyLib;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.Patches
{
    /// <summary>
    /// Slave population → town food drain.
    ///
    /// Postfix on DefaultSettlementFoodModel.CalculateTownFoodStocksChange.
    ///
    /// Each slave in the town market consumes a small amount of food daily.
    /// This creates a natural economic cap on slave hoarding: at some point,
    /// the food cost of maintaining a large slave population exceeds the
    /// prosperity/construction benefits, forcing players and AI to balance
    /// their slave holdings.
    ///
    /// Formula: penalty = slaveCount × SlaveFoodConsumptionPerUnit
    ///
    /// At default settings (0.02 food/slave/day):
    ///   50 slaves  → -1.0 food/day  (minor)
    ///   100 slaves → -2.0 food/day  (noticeable)
    ///   200 slaves → -4.0 food/day  (significant — roughly a village's output)
    ///
    /// This patch also works with non-default food models (e.g. EconomyOverhaul)
    /// via the dynamic patching system in B1071_DevastationBehavior.
    /// </summary>
    [HarmonyPatch(typeof(DefaultSettlementFoodModel), "CalculateTownFoodStocksChange")]
    public static class B1071_SlaveFoodPatch
    {
        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        private static readonly TextObject _label = new TextObject("{=b1071_slave_food}Slave Upkeep");

        [HarmonyPostfix]
        public static void Postfix(Town town, ref ExplainedNumber __result)
        {
            try
            {
                if (!Settings.EnableSlaveEconomy) return;
                if (town == null) return;

                var behavior = B1071_SlaveEconomyBehavior.Instance;
                if (behavior == null) return;

                int slaveCount = behavior.GetSlaveCountForTown(town);
                if (slaveCount <= 0) return;

                float consumption = slaveCount * Settings.SlaveFoodConsumptionPerUnit;
                if (consumption <= 0f) return;

                __result.Add(-consumption, _label);
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][SlaveFoodPatch] Error: {ex.Message}");
            }
        }
    }
}
