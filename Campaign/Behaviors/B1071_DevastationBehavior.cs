using Byzantium1071.Campaign.Patches;
using Byzantium1071.Campaign.Settings;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace Byzantium1071.Campaign.Behaviors
{
    /// <summary>
    /// Frontier Devastation 2.0 (RV2):
    ///
    /// Tracks per-village "devastation" (0–100) that accumulates from completed raids
    /// and decays slowly over time. Unlike vanilla's binary Looted/Normal state, this
    /// creates a persistent regional degradation that makes repeated frontier raiding
    /// meaningfully destructive — even after the village returns to Normal state.
    ///
    /// ──────────────────────────────────────────────────────────────────────
    /// ACCUMULATION
    /// ──────────────────────────────────────────────────────────────────────
    ///
    /// Each completed village loot (VillageLooted event) adds +25 devastation
    /// (configurable) to that village, capped at 100.
    ///
    /// Two raids within 50 days → 50 devastation → severe economic penalties.
    /// Four raids in quick succession → 100 devastation (maximum disruption).
    ///
    /// ──────────────────────────────────────────────────────────────────────
    /// DECAY
    /// ──────────────────────────────────────────────────────────────────────
    ///
    /// -0.5/day during Normal state only (configurable). Looted or being-raided
    /// villages do not decay — devastation is frozen while under active threat.
    /// A single +25 raid takes 50 days to fully decay.
    ///
    /// ──────────────────────────────────────────────────────────────────────
    /// EFFECTS (applied via Harmony patches in separate files)
    /// ──────────────────────────────────────────────────────────────────────
    ///
    /// | Patch                          | Effect at dev 50 | Effect at dev 100 |
    /// |--------------------------------|------------------|-------------------|
    /// | B1071_DevastationHearthPatch   | -1.0 hearth/day  | -2.0 hearth/day   |
    /// | B1071_DevastationProsperityPatch| -1.0 pros/day*  | -2.0 pros/day*    |
    /// | B1071_DevastationFoodPatch     | -50% food per village | -100% food    |
    /// | B1071_DevastationSecurityPatch | -0.75 sec/day*   | -1.5 sec/day*     |
    ///
    /// * Prosperity and security penalties are applied to the bound town/castle,
    ///   averaged across all bound villages' devastation values.
    ///
    /// ──────────────────────────────────────────────────────────────────────
    /// PERSISTENCE
    /// ──────────────────────────────────────────────────────────────────────
    ///
    /// Dictionary&lt;string, float&gt; keyed by Village.Settlement.StringId,
    /// persisted via SyncData. Compatible with existing saves (empty dict on first load).
    /// </summary>
    public sealed class B1071_DevastationBehavior : CampaignBehaviorBase
    {
        public static B1071_DevastationBehavior? Instance { get; internal set; }

        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        // Per-village devastation (0 to 100). Key = Village.Settlement.StringId.
        private Dictionary<string, float> _devastationByVillage = new Dictionary<string, float>();

        // ── Public API (used by Harmony patches) ──────────────────────────

        /// <summary>Returns devastation for a specific village (0–100).</summary>
        public float GetDevastation(Village village)
        {
            if (village?.Settlement == null) return 0f;
            return _devastationByVillage.TryGetValue(village.Settlement.StringId, out float val) ? val : 0f;
        }

        /// <summary>Returns the average devastation across all bound villages of a town.</summary>
        public float GetAverageBoundVillageDevastation(Town town)
        {
            if (town?.Settlement == null) return 0f;

            var villages = town.Settlement.BoundVillages;
            if (villages == null || villages.Count == 0) return 0f;

            float sum = 0f;
            int count = 0;
            foreach (Village v in villages)
            {
                sum += GetDevastation(v);
                count++;
            }

            return count > 0 ? sum / count : 0f;
        }

        // ── CampaignBehaviorBase ──────────────────────────────────────────

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.VillageLooted.AddNonSerializedListener(this, OnVillageLooted);
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailyTickSettlement);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("b1071_villageDevastation", ref _devastationByVillage);
            _devastationByVillage ??= new Dictionary<string, float>();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────

        /// <summary>Once-per-application flag — dynamic Harmony patch survives across game loads.</summary>
        private static bool _dynamicFoodPatchApplied;

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            Instance = this;
            ApplyDynamicFoodPatchIfNeeded();
        }

        /// <summary>
        /// Resets the static dynamic-patch flag so that the next session correctly
        /// re-evaluates whether food model compat patches need to be applied.
        /// Called from <see cref="SubModule.OnSubModuleUnloaded"/> after UnpatchAll().
        /// </summary>
        internal static void ResetDynamicPatchFlag() => _dynamicFoodPatchApplied = false;

        /// <summary>
        /// If a third-party mod (e.g. EconomyOverhaul) replaces the food model with a class
        /// that does NOT inherit from DefaultSettlementFoodModel, our static Harmony patches on
        /// DefaultSettlementFoodModel.CalculateTownFoodStocksChange become dead code.
        ///
        /// This method detects that situation at runtime and applies the same Postfix methods
        /// (DevastationFoodPatch + SlaveFoodPatch) directly to the actual food model.
        /// </summary>
        private static void ApplyDynamicFoodPatchIfNeeded()
        {
            if (_dynamicFoodPatchApplied) return;

            try
            {
                var foodModel = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.SettlementFoodModel;
                if (foodModel == null) return;

                Type modelType = foodModel.GetType();

                if (typeof(DefaultSettlementFoodModel).IsAssignableFrom(modelType))
                {
                    // Model inherits from Default → static [HarmonyPatch] works → nothing to do.
                    return;
                }

                // Non-default food model detected (e.g. BLM_TownProductionModel from EconomyOverhaul).
                MethodInfo? targetMethod = modelType.GetMethod(
                    "CalculateTownFoodStocksChange",
                    BindingFlags.Instance | BindingFlags.Public);

                if (targetMethod == null)
                {
                    Debug.Print($"[Byzantium1071] WARNING: Non-default food model {modelType.Name} has no CalculateTownFoodStocksChange — food patches disabled");
                    return;
                }

                var harmony = new Harmony("com.andrei.byzantium1071");
                int applied = 0;

                // Patch 1: Devastation food penalty
                MethodInfo? devPostfix = typeof(B1071_DevastationFoodPatch)
                    .GetMethod("Postfix", BindingFlags.Static | BindingFlags.Public);
                if (devPostfix != null)
                {
                    harmony.Patch(targetMethod, postfix: new HarmonyMethod(devPostfix));
                    applied++;
                }

                // Patch 2: Slave food consumption
                MethodInfo? slavePostfix = typeof(B1071_SlaveFoodPatch)
                    .GetMethod("Postfix", BindingFlags.Static | BindingFlags.Public);
                if (slavePostfix != null)
                {
                    harmony.Patch(targetMethod, postfix: new HarmonyMethod(slavePostfix));
                    applied++;
                }

                _dynamicFoodPatchApplied = true;
                Debug.Print($"[Byzantium1071] Non-default food model detected ({modelType.Name}), applied {applied} dynamic compat food patches");
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071] Failed to apply dynamic food compat patch: {ex.Message}");
            }
        }

        // ── Event: Village looted → +DevastationPerRaid to that village ──

        private void OnVillageLooted(Village village)
        {
            try
            {
                if (!Settings.EnableFrontierDevastation) return;
                if (village?.Settlement == null) return;

                string key = village.Settlement.StringId;
                float current = _devastationByVillage.TryGetValue(key, out float val) ? val : 0f;
                float added = Settings.DevastationPerRaid;
                _devastationByVillage[key] = Math.Min(100f, current + added);

                if (Settings.TelemetryDebugLogs)
                {
                    Debug.Print(
                        $"[Byzantium1071][Devastation] {village.Settlement.Name} looted: " +
                        $"devastation {current:F1} \u2192 {_devastationByVillage[key]:F1}");
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][Devastation] VillageLooted error: {ex.Message}");
            }
        }

        // ── Daily decay (villages in Normal state only) ──────────────────

        private void OnDailyTickSettlement(Settlement settlement)
        {
            try
            {
                if (!Settings.EnableFrontierDevastation) return;
                if (settlement?.Village == null) return;  // only process villages

                // Only decay when village is in Normal state — frozen during Looted/BeingRaided
                if (settlement.Village.VillageState != Village.VillageStates.Normal) return;

                string key = settlement.StringId;
                if (!_devastationByVillage.TryGetValue(key, out float dev) || dev <= 0f) return;

                float decay = Settings.DevastationDecayPerDay;
                dev = Math.Max(0f, dev - decay);

                if (dev <= 0f)
                    _devastationByVillage.Remove(key);
                else
                    _devastationByVillage[key] = dev;
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][Devastation] DailyTick error: {ex.Message}");
            }
        }
    }
}
