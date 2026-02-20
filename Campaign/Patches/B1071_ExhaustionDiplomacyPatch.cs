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

        internal static float NoWarThreshold =>
            (B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults).DiplomacyNoNewWarThreshold;

        internal static float PeaceThreshold =>
            (B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults).DiplomacyPeacePressureThreshold;

        internal static float WarPenaltyPerPoint =>
            (B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults).DiplomacyWarSupportPenaltyPerPoint;

        internal static float PeaceBonusPerPoint =>
            (B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults).DiplomacyPeaceSupportBonusPerPoint;

        internal static float GetMajorWarPressureBias(Kingdom kingdom)
        {
            int pressureStart = Math.Max(1, Settings.DiplomacyMajorWarPressureStartCount);
            float biasPerWar = Math.Max(0f, Settings.DiplomacyExtraPeaceBiasPerMajorWar);

            int majorWars = 0;
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

            float penalty = exhaustion * B1071_ExhaustionDiplomacyHelpers.WarPenaltyPerPoint;
            penalty += B1071_ExhaustionDiplomacyHelpers.GetMajorWarPressureBias(__instance.Kingdom);
            if (outcome.ShouldWarBeDeclared)
                __result -= penalty;
            else
                __result += penalty;

            B1071_ExhaustionDiplomacyHelpers.RecordTelemetry($"DeclareWar support adjusted by {penalty:0.0} at exhaustion {exhaustion:0.0} for {__instance.Kingdom.Name}.");
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

            float threshold = B1071_ExhaustionDiplomacyHelpers.PeaceThreshold;
            if (exhaustion >= threshold)
            {
                __result = outcome.ShouldPeaceBeDeclared ? 200f : 0f;
                B1071_ExhaustionDiplomacyHelpers.RecordTelemetry($"MakePeace support override: exhaustion {exhaustion:0.0} >= {threshold:0.0} for {__instance.Kingdom.Name}.");
                return;
            }

            float bonus = exhaustion * B1071_ExhaustionDiplomacyHelpers.PeaceBonusPerPoint;
            bonus += B1071_ExhaustionDiplomacyHelpers.GetMajorWarPressureBias(__instance.Kingdom);
            if (outcome.ShouldPeaceBeDeclared)
                __result += bonus;
            else
                __result -= bonus;

            B1071_ExhaustionDiplomacyHelpers.RecordTelemetry($"MakePeace support adjusted by {bonus:0.0} at exhaustion {exhaustion:0.0} for {__instance.Kingdom.Name}.");
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
