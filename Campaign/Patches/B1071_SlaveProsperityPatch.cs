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
    /// Postfix patch on DefaultSettlementProsperityModel.CalculateProsperityChange.
    ///
    /// Adds a "Slave Labor" line to the town's daily prosperity ExplainedNumber,
    /// proportional to how many Slave goods are in the town's civilian market.
    ///
    /// Because this modifies the same ExplainedNumber the game uses for both the tooltip
    /// AND the actual daily prosperity application, the slave bonus:
    ///   • Appears as "Slave Labor +X.XX" in the Prosperity tooltip (hover over town name)
    ///   • Is included in the Expected Change value
    ///   • Actually increments Town.Prosperity each day
    ///
    /// The behavior's direct Town.Prosperity += was REMOVED; this patch is the sole
    /// prosperity driver for slave labour. Double-counting is therefore impossible.
    ///
    /// Formula: bonus = slaveCount × SlaveProsperityPerUnit × SlaveRansomMultiplier
    /// (no cap — prosperity gain is already very small at default settings)
    ///
    /// Verified: DefaultSettlementProsperityModel.CalculateProsperityChange(Town, bool)
    ///           returns ExplainedNumber (struct → ref required in Postfix) — v1.3.15
    /// </summary>
    [HarmonyPatch(typeof(DefaultSettlementProsperityModel), "CalculateProsperityChange")]
    public static class B1071_SlaveProsperityPatch
    {
        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        // Cached label — avoids TextObject allocation every prosperity tick.
        private static readonly TextObject _slaveLabel = new TextObject("{=b1071_slave_labor}Slave Labor");

        private static void Postfix(Town fortification, ref ExplainedNumber __result)
        {
            try
            {
                if (!Settings.EnableSlaveEconomy) return;
                if (fortification == null) return;

                var behavior = B1071_SlaveEconomyBehavior.Instance;
                if (behavior == null) return;

                int slaveCount = behavior.GetSlaveCountForTown(fortification);
                if (slaveCount <= 0) return;

                float bonus = slaveCount * Settings.SlaveProsperityPerUnit * Settings.SlaveRansomMultiplier;
                if (bonus > 0f)
                    __result.Add(bonus, _slaveLabel);
            }
            catch (Exception ex) { TaleWorlds.Library.Debug.Print($"[Byzantium1071] SlaveProsperityPatch error: {ex}"); }
        }
    }
}
