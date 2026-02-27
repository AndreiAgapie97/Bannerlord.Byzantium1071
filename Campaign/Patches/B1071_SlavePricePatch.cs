using System;
using Byzantium1071.Campaign.Settings;
using HarmonyLib;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Core;

namespace Byzantium1071.Campaign.Patches
{
    /// <summary>
    /// Overrides the vanilla trade price formula for the slave ItemCategory with
    /// a custom exponential-decay curve that produces gradual, realistic price
    /// degradation as stock increases.
    ///
    /// ──────────────────────────────────────────────────────────────────────
    /// PROBLEM
    /// ──────────────────────────────────────────────────────────────────────
    ///
    /// Vanilla's GetBasePriceFactor formula:
    ///   priceFactor = Pow(demand / (0.1 × supply + inStoreValue × 0.04 + 2), 0.6)
    ///
    /// With a high base value, each slave adds baseValue × 0.04 to the denominator,
    /// which quickly overwhelms the typical demand (~60). The formula was designed
    /// for low-value bulk goods (grain=10, pottery=15), not trade items worth
    /// hundreds of denars.
    ///
    /// Result: stock 0 → ~1500d, stock 1 → ~700d, stock 2 → ~450d, stock 5 → floor.
    /// This makes the price curve absurdly steep — a town of 3000 prosperity
    /// shouldn't see slave prices halve because 1 slave arrived.
    ///
    /// ──────────────────────────────────────────────────────────────────────
    /// FIX: Custom exponential-decay price factor for slaves
    /// ──────────────────────────────────────────────────────────────────────
    ///
    /// Replaces the vanilla formula with:
    ///   priceFactor = max(0.1, decayRate ^ stock)
    ///
    /// where stock = inStoreValue / slaveBaseValue (300) and decayRate is an
    /// MCM-configurable setting (default 0.98). This gives a gradual, natural
    /// price curve that creates meaningful price differentials across towns:
    ///
    ///   Stock  Factor  Price    Notes
    ///     0    1.000    300d    Empty market, full value
    ///    10    0.817    245d    Very scarce — high demand
    ///    20    0.668    200d    Light supply
    ///    30    0.545    164d    Moderate supply
    ///    50    0.364    109d    Heavy supply (~2.4× ransom)
    ///    80    0.199     60d    Saturated market
    ///   114    0.100     30d    At floor (0.1 × 300)
    ///
    /// Enslaving pays ~2–2.5× T1-T3 ransom (avg ~45d) at typical stock levels,
    /// making it consistently better than ransoming without being exploitative.
    /// Price differentials between towns drive caravan slave trading.
    ///
    /// SCOPE: Only affects ItemCategory "b1071_slaves". All other items use
    /// the vanilla formula unmodified.
    ///
    /// HOOKS: Postfix on DefaultTradeItemPriceFactorModel.GetBasePriceFactor.
    /// This is the single bottleneck for all price queries:
    ///   - Player trade screen (buy/sell)
    ///   - NPC enslavement gold (town.GetItemPrice)
    ///   - TownMarketData.GetPriceFactor (price snapshots)
    ///   - Caravan trade evaluation
    /// </summary>
    [HarmonyPatch(typeof(DefaultTradeItemPriceFactorModel), nameof(DefaultTradeItemPriceFactorModel.GetBasePriceFactor))]
    public static class B1071_SlavePricePatch
    {
        /// <summary>
        /// The base value of the slave item (from items.xml value="300").
        /// Used to derive stock count from inStoreValue.
        /// </summary>
        private const int SlaveBaseValue = 300;

        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        /// <summary>
        /// Postfix: if the <paramref name="itemCategory"/> is our slave category,
        /// replace vanilla's result with our exponential-decay formula.
        /// </summary>
        public static void Postfix(
            ItemCategory itemCategory,
            float inStoreValue,
            float supply,
            float demand,
            bool isSelling,
            int transferValue,
            ref float __result)
        {
            try
            {
                if (itemCategory == null || itemCategory.StringId != "b1071_slaves")
                    return;

                var settings = Settings;
                if (settings == null || !settings.EnableSlaveEconomy)
                    return;

                // Derive stock from inStoreValue. Vanilla's GetBasePriceFactor
                // adds transferValue to a local copy of inStoreValue when
                // isSelling is true, but the Harmony postfix receives the
                // ORIGINAL parameter — so we must re-apply the addition here.
                float effectiveValue = inStoreValue;
                if (isSelling) effectiveValue += transferValue;

                int stock = (int)(effectiveValue / SlaveBaseValue);

                float decayRate = settings.SlavePriceDecayRate;

                // priceFactor = max(0.1, decayRate ^ stock)
                // At stock=0: 1.0 (full price). Decays exponentially per unit.
                float factor = (float)Math.Pow(decayRate, stock);

                // Clamp to [0.1, 10.0] — same bounds as vanilla IsTradeGood.
                __result = Math.Max(0.1f, Math.Min(10f, factor));
            }
            catch (Exception)
            {
                // Fall through to vanilla result on any error.
            }
        }
    }
}
