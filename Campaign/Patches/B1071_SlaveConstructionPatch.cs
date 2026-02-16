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
    /// Postfix patch on DefaultBuildingConstructionModel.CalculateDailyConstructionPower.
    ///
    /// Adds a "Slave Labor" line to the settlement's daily construction ExplainedNumber,
    /// proportional to how many Slave goods are currently in the town's civilian market.
    ///
    /// Because this modifies the ExplainedNumber that the game uses both for the tooltip
    /// AND for actually advancing BuildingProgress each day, the slave bonus:
    ///   • Appears as "Slave Labor +XX" in the Construction tooltip (Manage screen)
    ///   • Actually speeds up building completion
    ///
    /// Formula: bonus = min(SlaveConstructionBonusCap, slaveCount × SlaveConstructionAcceleration × SlaveRansomMultiplier)
    /// </summary>
    [HarmonyPatch(typeof(DefaultBuildingConstructionModel), "CalculateDailyConstructionPower")]
    public static class B1071_SlaveConstructionPatch
    {
        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        // TextObject is cached to avoid allocation every construction tick.
        private static readonly TextObject _slaveLabel = new TextObject("{=b1071_slave_labor}Slave Labor");

        private static void Postfix(Town town, ref ExplainedNumber __result)
        {
            try
            {
                if (!Settings.EnableSlaveEconomy) return;
                if (town == null) return;

                var behavior = B1071_SlaveEconomyBehavior.Instance;
                if (behavior == null) return;

                int slaveCount = behavior.GetSlaveCountForTown(town);
                if (slaveCount <= 0) return;

                float eff   = Settings.SlaveRansomMultiplier;
                float bonus = Math.Min(
                    Settings.SlaveConstructionBonusCap,
                    slaveCount * Settings.SlaveConstructionAcceleration * eff);

                if (bonus > 0f)
                    __result.Add(bonus, _slaveLabel);
            }
            catch (Exception ex) { TaleWorlds.Library.Debug.Print($"[Byzantium1071] SlaveConstructionPatch error: {ex}"); }
        }
    }
}
