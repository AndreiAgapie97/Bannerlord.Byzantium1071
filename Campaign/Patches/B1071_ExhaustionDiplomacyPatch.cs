using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using HarmonyLib;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Library;

namespace Byzantium1071.Campaign.Patches
{
    internal static class B1071_ExhaustionDiplomacyHelpers
    {
        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        internal static bool TryGetExhaustion(Kingdom? kingdom, out float exhaustion)
        {
            exhaustion = 0f;

            B1071_ManpowerBehavior? behavior = B1071_ManpowerBehavior.Instance;
            if (behavior == null || kingdom == null || !Settings.EnableWarExhaustion || !Settings.EnableExhaustionDiplomacyPressure)
                return false;

            exhaustion = behavior.GetWarExhaustion(kingdom.StringId);
            return true;
        }

        internal static bool IsPlayerInfluencedContext(Kingdom kingdom, Clan? clan = null)
        {
            if (Settings.DiplomacyEnforcePlayerParity)
                return false;

            if (Clan.PlayerClan?.Kingdom == kingdom)
                return true;

            if (clan != null && clan == Clan.PlayerClan)
                return true;

            return false;
        }

        internal static bool EnableDebugLogs => Settings.DiplomacyDebugLogs;
        internal static bool UsePressureBands => Settings.EnableDiplomacyPressureBands;

        // Legacy thresholds (used when bands disabled)
        internal static float NoWarThreshold =>
            (B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults).DiplomacyNoNewWarThreshold;

        internal static float PeaceThreshold =>
            (B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults).DiplomacyPeacePressureThreshold;

        // Per-point rates (legacy path)
        internal static float WarPenaltyPerPoint =>
            (B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults).DiplomacyWarSupportPenaltyPerPoint;

        internal static float PeaceBonusPerPoint =>
            (B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults).DiplomacyPeaceSupportBonusPerPoint;

        // WP5 caps
        internal static float WarSupportPenaltyCap => Settings.WarSupportPenaltyCap;  // negative value
        internal static float PeaceSupportBonusCap => Settings.PeaceSupportBonusCap;  // positive value

        /// <summary>
        /// Gets the current pressure band for a kingdom. Requires behavior instance.
        /// </summary>
        internal static DiplomacyPressureBand GetBand(Kingdom kingdom)
        {
            B1071_ManpowerBehavior? behavior = B1071_ManpowerBehavior.Instance;
            return behavior?.GetPressureBand(kingdom.StringId) ?? DiplomacyPressureBand.Low;
        }

        /// <summary>
        /// Gets band-specific per-point peace bias. Requires behavior instance.
        /// </summary>
        internal static float GetBandPeaceBias(Kingdom kingdom)
        {
            B1071_ManpowerBehavior? behavior = B1071_ManpowerBehavior.Instance;
            DiplomacyPressureBand band = behavior?.GetPressureBand(kingdom.StringId) ?? DiplomacyPressureBand.Low;
            return behavior?.GetBandPeaceBias(band) ?? PeaceBonusPerPoint;
        }

        internal static float GetMajorWarPressureBias(Kingdom kingdom)
        {
            int pressureStart = Math.Max(1, Settings.DiplomacyMajorWarPressureStartCount);
            float biasPerWar = Math.Max(0f, Settings.DiplomacyExtraPeaceBiasPerMajorWar);

            int majorWars = 0;
            if (kingdom.FactionsAtWarWith == null) return 0f;
            for (int i = 0; i < kingdom.FactionsAtWarWith.Count; i++)
            {
                IFaction enemy = kingdom.FactionsAtWarWith[i];
                if (enemy is Kingdom && kingdom.IsAtWarWith(enemy))
                    majorWars++;
            }

            int extraWarCount = Math.Max(0, majorWars - pressureStart + 1);
            return extraWarCount * biasPerWar;
        }

        internal static bool IsKingdomVsKingdomWarTarget(DeclareWarDecision decision)
        {
            return decision?.Kingdom != null && decision.FactionToDeclareWarOn is Kingdom;
        }

        internal static bool IsKingdomVsKingdomPeaceTarget(MakePeaceKingdomDecision decision)
        {
            return decision?.Kingdom != null && decision.FactionToMakePeaceWith is Kingdom;
        }

        internal static void RecordTelemetry(string reason)
        {
            if (string.IsNullOrEmpty(reason))
                return;

            B1071_ManpowerBehavior.Instance?.RecordDiplomacyTelemetry(reason);
        }
    }

    [HarmonyPatch(typeof(DeclareWarDecision), nameof(DeclareWarDecision.DetermineSupport))]
    public static class B1071_DeclareWarDecisionExhaustionSupportPatch
    {
        static void Postfix(DeclareWarDecision __instance, Clan clan, DecisionOutcome possibleOutcome, ref float __result)
        {
            if (__instance?.Kingdom == null || clan == null)
                return;

            if (!B1071_ExhaustionDiplomacyHelpers.IsKingdomVsKingdomWarTarget(__instance))
                return;

            if (B1071_ExhaustionDiplomacyHelpers.IsPlayerInfluencedContext(__instance.Kingdom, clan))
                return;

            if (!B1071_ExhaustionDiplomacyHelpers.TryGetExhaustion(__instance.Kingdom, out float exhaustion))
                return;

            if (possibleOutcome is not DeclareWarDecision.DeclareWarDecisionOutcome outcome)
                return;

            // Truce enforcement: always hard-block (truce is an absolute constraint).
            B1071_ManpowerBehavior? behavior = B1071_ManpowerBehavior.Instance;
            if (behavior != null && behavior.IsKingdomPairUnderTruce(__instance.Kingdom, __instance.FactionToDeclareWarOn, out _))
            {
                if (B1071_ExhaustionDiplomacyHelpers.EnableDebugLogs)
                    Debug.Print($"[Byzantium1071][Diplomacy][Debug] DeclareWar support forced against war due to active truce: {__instance.Kingdom.Name} vs {__instance.FactionToDeclareWarOn.Name}.");
                if (outcome.ShouldWarBeDeclared)
                    __result = -10000f;
                else
                    __result = 10000f;
                B1071_ExhaustionDiplomacyHelpers.RecordTelemetry($"DeclareWar support override: truce active for {__instance.Kingdom.Name} vs {__instance.FactionToDeclareWarOn.Name}.");
                return;
            }

            // ─── WP5 Pressure Band path ───
            if (B1071_ExhaustionDiplomacyHelpers.UsePressureBands)
            {
                DiplomacyPressureBand band = B1071_ExhaustionDiplomacyHelpers.GetBand(__instance.Kingdom);
                float warBias = B1071_ExhaustionDiplomacyHelpers.GetMajorWarPressureBias(__instance.Kingdom);
                float penaltyCap = Math.Abs(B1071_ExhaustionDiplomacyHelpers.WarSupportPenaltyCap);

                float penalty;
                switch (band)
                {
                    case DiplomacyPressureBand.Crisis:
                        // Strong capped penalty — not infinite, but very heavy.
                        penalty = Math.Min(penaltyCap, exhaustion * B1071_ExhaustionDiplomacyHelpers.WarPenaltyPerPoint + warBias);
                        break;
                    case DiplomacyPressureBand.Rising:
                        // Moderate scaling penalty.
                        penalty = Math.Min(penaltyCap * 0.6f, exhaustion * B1071_ExhaustionDiplomacyHelpers.WarPenaltyPerPoint * 0.7f + warBias);
                        break;
                    default: // Low
                        penalty = exhaustion * B1071_ExhaustionDiplomacyHelpers.WarPenaltyPerPoint * 0.3f + warBias;
                        break;
                }

                if (outcome.ShouldWarBeDeclared)
                    __result -= penalty;
                else
                    __result += penalty;

                B1071_ExhaustionDiplomacyHelpers.RecordTelemetry($"DeclareWar band({band}) penalty {penalty:0.0} at exhaustion {exhaustion:0.0} for {__instance.Kingdom.Name}.");
                return;
            }

            // ─── Legacy threshold path (bands disabled) ───
            float threshold = B1071_ExhaustionDiplomacyHelpers.NoWarThreshold;
            if (exhaustion >= threshold)
            {
                if (outcome.ShouldWarBeDeclared)
                    __result = -10000f;
                else
                    __result = 10000f;
                B1071_ExhaustionDiplomacyHelpers.RecordTelemetry($"DeclareWar support override: exhaustion {exhaustion:0.0} >= {threshold:0.0} for {__instance.Kingdom.Name}.");
                return;
            }

            float legacyPenalty = exhaustion * B1071_ExhaustionDiplomacyHelpers.WarPenaltyPerPoint;
            legacyPenalty += B1071_ExhaustionDiplomacyHelpers.GetMajorWarPressureBias(__instance.Kingdom);
            if (outcome.ShouldWarBeDeclared)
                __result -= legacyPenalty;
            else
                __result += legacyPenalty;

            B1071_ExhaustionDiplomacyHelpers.RecordTelemetry($"DeclareWar support adjusted by {legacyPenalty:0.0} at exhaustion {exhaustion:0.0} for {__instance.Kingdom.Name}.");
        }
    }

    [HarmonyPatch(typeof(MakePeaceKingdomDecision), nameof(MakePeaceKingdomDecision.DetermineSupport))]
    public static class B1071_MakePeaceDecisionExhaustionSupportPatch
    {
        static void Postfix(MakePeaceKingdomDecision __instance, Clan clan, DecisionOutcome possibleOutcome, ref float __result)
        {
            if (__instance?.Kingdom == null || clan == null)
                return;

            if (!B1071_ExhaustionDiplomacyHelpers.IsKingdomVsKingdomPeaceTarget(__instance))
                return;

            if (B1071_ExhaustionDiplomacyHelpers.IsPlayerInfluencedContext(__instance.Kingdom, clan))
                return;

            if (!B1071_ExhaustionDiplomacyHelpers.TryGetExhaustion(__instance.Kingdom, out float exhaustion))
                return;

            if (possibleOutcome is not MakePeaceKingdomDecision.MakePeaceDecisionOutcome outcome)
                return;

            // ─── WP5 Pressure Band path ───
            if (B1071_ExhaustionDiplomacyHelpers.UsePressureBands)
            {
                DiplomacyPressureBand band = B1071_ExhaustionDiplomacyHelpers.GetBand(__instance.Kingdom);
                float perPointBias = B1071_ExhaustionDiplomacyHelpers.GetBandPeaceBias(__instance.Kingdom);
                float warBias = B1071_ExhaustionDiplomacyHelpers.GetMajorWarPressureBias(__instance.Kingdom);
                float bonusCap = Math.Max(0f, B1071_ExhaustionDiplomacyHelpers.PeaceSupportBonusCap);

                float bonus = Math.Min(bonusCap, exhaustion * perPointBias + warBias);

                if (outcome.ShouldPeaceBeDeclared)
                    __result += bonus;
                else
                    __result -= bonus;

                B1071_ExhaustionDiplomacyHelpers.RecordTelemetry($"MakePeace band({band}) bonus {bonus:0.0} at exhaustion {exhaustion:0.0} for {__instance.Kingdom.Name}.");
                return;
            }

            // ─── Legacy threshold path ───
            float threshold = B1071_ExhaustionDiplomacyHelpers.PeaceThreshold;
            if (exhaustion >= threshold)
            {
                __result = outcome.ShouldPeaceBeDeclared ? 200f : 0f;
                B1071_ExhaustionDiplomacyHelpers.RecordTelemetry($"MakePeace support override: exhaustion {exhaustion:0.0} >= {threshold:0.0} for {__instance.Kingdom.Name}.");
                return;
            }

            float legacyBonus = exhaustion * B1071_ExhaustionDiplomacyHelpers.PeaceBonusPerPoint;
            legacyBonus += B1071_ExhaustionDiplomacyHelpers.GetMajorWarPressureBias(__instance.Kingdom);
            if (outcome.ShouldPeaceBeDeclared)
                __result += legacyBonus;
            else
                __result -= legacyBonus;

            B1071_ExhaustionDiplomacyHelpers.RecordTelemetry($"MakePeace support adjusted by {legacyBonus:0.0} at exhaustion {exhaustion:0.0} for {__instance.Kingdom.Name}.");
        }
    }

    [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.AddDecision))]
    public static class B1071_BlockWarDecisionAtHighExhaustionPatch
    {
        static bool Prefix(KingdomDecision kingdomDecision)
        {
            if (kingdomDecision is not DeclareWarDecision declareWarDecision)
                return true;

            if (!B1071_ExhaustionDiplomacyHelpers.IsKingdomVsKingdomWarTarget(declareWarDecision))
                return true;

            Kingdom? kingdom = declareWarDecision.Kingdom;
            if (kingdom == null)
                return true;

            if (B1071_ExhaustionDiplomacyHelpers.IsPlayerInfluencedContext(kingdom))
                return true;

            B1071_ManpowerBehavior? behavior = B1071_ManpowerBehavior.Instance;
            if (behavior != null && behavior.IsKingdomPairUnderTruce(kingdom, declareWarDecision.FactionToDeclareWarOn, out float truceDaysLeft))
            {
                if (B1071_ExhaustionDiplomacyHelpers.EnableDebugLogs)
                    Debug.Print($"[Byzantium1071][Diplomacy] Blocked DeclareWarDecision for {kingdom.Name}: truce active for {truceDaysLeft:0.0} more days.");
                B1071_ExhaustionDiplomacyHelpers.RecordTelemetry($"Blocked AddDecision DeclareWar: truce {truceDaysLeft:0.0}d for {kingdom.Name}.");
                return false;
            }

            if (!B1071_ExhaustionDiplomacyHelpers.TryGetExhaustion(kingdom, out float exhaustion))
                return true;

            // WP5: Use band-based blocking (Crisis only) when bands enabled.
            if (B1071_ExhaustionDiplomacyHelpers.UsePressureBands)
            {
                DiplomacyPressureBand band = B1071_ExhaustionDiplomacyHelpers.GetBand(kingdom);
                if (band != DiplomacyPressureBand.Crisis)
                    return true; // Allow war decisions in Low/Rising bands

                if (B1071_ExhaustionDiplomacyHelpers.EnableDebugLogs)
                    Debug.Print($"[Byzantium1071][Diplomacy] Blocked DeclareWarDecision for {kingdom.Name}: Crisis band at exhaustion {exhaustion:0.0}.");
                B1071_ExhaustionDiplomacyHelpers.RecordTelemetry($"Blocked AddDecision DeclareWar: Crisis band at {exhaustion:0.0} for {kingdom.Name}.");
                return false;
            }

            // Legacy threshold path
            float threshold = B1071_ExhaustionDiplomacyHelpers.NoWarThreshold;
            if (exhaustion < threshold)
                return true;

            if (B1071_ExhaustionDiplomacyHelpers.EnableDebugLogs)
                Debug.Print($"[Byzantium1071][Diplomacy] Blocked DeclareWarDecision for {kingdom.Name} due to exhaustion {exhaustion:0.0} >= {threshold:0.0}.");
            B1071_ExhaustionDiplomacyHelpers.RecordTelemetry($"Blocked AddDecision DeclareWar: exhaustion {exhaustion:0.0} >= {threshold:0.0} for {kingdom.Name}.");
            return false;
        }
    }

    [HarmonyPatch(typeof(MakePeaceAction), nameof(MakePeaceAction.Apply))]
    public static class B1071_RegisterTruceAfterAnyPeacePatch
    {
        static void Postfix(IFaction faction1, IFaction faction2)
        {
            B1071_ManpowerBehavior.Instance?.RegisterKingdomPairTruce(faction1, faction2);
        }
    }

    [HarmonyPatch(typeof(MakePeaceAction), nameof(MakePeaceAction.ApplyByKingdomDecision))]
    public static class B1071_RegisterTruceAfterDecisionPeacePatch
    {
        static void Postfix(IFaction faction1, IFaction faction2, int dailyTributeFrom1To2, int dailyTributeDuration)
        {
            B1071_ManpowerBehavior.Instance?.RegisterKingdomPairTruce(faction1, faction2);
        }
    }
}
