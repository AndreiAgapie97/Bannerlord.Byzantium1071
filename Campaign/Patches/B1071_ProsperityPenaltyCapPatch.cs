using Byzantium1071.Campaign.Settings;
using HarmonyLib;
using System;
using TaleWorlds.CampaignSystem.GameComponents;

namespace Byzantium1071.Campaign.Patches
{
    /// <summary>
    /// Coordinates the combined prosperity penalty cap (G-1) across Governance Strain
    /// and Devastated Hinterlands patches.
    ///
    /// A <see cref="HarmonyPrefix"/> on <c>CalculateProsperityChange</c> resets the accumulator
    /// before each invocation. Each penalty-producing Postfix then calls <see cref="ClampPenalty"/>
    /// to register its contribution and receive the allowed (potentially clamped) value.
    ///
    /// Thread-safe for single-threaded Bannerlord game loop.
    /// </summary>
    [HarmonyPatch(typeof(DefaultSettlementProsperityModel), "CalculateProsperityChange")]
    internal static class B1071_ProsperityPenaltyCapPatch
    {
        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        private static float _accumulatedPenalty;

        /// <summary>
        /// Prefix resets the accumulated penalty before each CalculateProsperityChange call.
        /// This ensures tooltip refreshes and daily ticks both get clean state.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static void Prefix()
        {
            try
            {
                _accumulatedPenalty = 0f;
            }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print($"[Byzantium1071] ProsperityPenaltyCapPatch Prefix error: {ex}");
            }
        }

        /// <summary>
        /// Registers a penalty contribution and returns the allowed value after applying
        /// the combined cap. If the accumulated penalty has already reached the cap,
        /// returns 0 (no further penalty allowed).
        /// </summary>
        /// <param name="rawPenalty">Negative value (e.g. -1.5).</param>
        /// <returns>The allowed penalty (between rawPenalty and 0).</returns>
        internal static float ClampPenalty(float rawPenalty)
        {
            if (rawPenalty >= 0f) return rawPenalty; // Not a penalty — pass through.

            float cap = Settings.MaxCombinedModProsperityPenalty; // e.g. -8.0
            float remaining = cap - _accumulatedPenalty;          // e.g. -8.0 - (-3.0) = -5.0
            if (remaining >= 0f) return 0f;                       // Cap already reached.

            float allowed = Math.Max(remaining, rawPenalty);      // More negative = worse → Max clamps.
            _accumulatedPenalty += allowed;
            return allowed;
        }
    }
}
