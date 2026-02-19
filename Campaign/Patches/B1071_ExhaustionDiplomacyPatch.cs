using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
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
            if (Clan.PlayerClan?.Kingdom == kingdom)
                return true;

            if (clan != null && clan == Clan.PlayerClan)
                return true;

            return false;
        }

        internal static float NoWarThreshold =>
            (B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults).DiplomacyNoNewWarThreshold;

        internal static float PeaceThreshold =>
            (B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults).DiplomacyPeacePressureThreshold;

        internal static float WarPenaltyPerPoint =>
            (B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults).DiplomacyWarSupportPenaltyPerPoint;

        internal static float PeaceBonusPerPoint =>
            (B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults).DiplomacyPeaceSupportBonusPerPoint;
    }

    [HarmonyPatch(typeof(DeclareWarDecision), nameof(DeclareWarDecision.DetermineSupport))]
    public static class B1071_DeclareWarDecisionExhaustionSupportPatch
    {
        static void Postfix(DeclareWarDecision __instance, Clan clan, DecisionOutcome possibleOutcome, ref float __result)
        {
            if (__instance?.Kingdom == null || clan == null)
                return;

            if (B1071_ExhaustionDiplomacyHelpers.IsPlayerInfluencedContext(__instance.Kingdom, clan))
                return;

            if (!B1071_ExhaustionDiplomacyHelpers.TryGetExhaustion(__instance.Kingdom, out float exhaustion))
                return;

            if (possibleOutcome is not DeclareWarDecision.DeclareWarDecisionOutcome outcome)
                return;

            float threshold = B1071_ExhaustionDiplomacyHelpers.NoWarThreshold;
            if (exhaustion >= threshold)
            {
                if (outcome.ShouldWarBeDeclared)
                    __result = -10000f;
                else
                    __result = 10000f;
                return;
            }

            float penalty = exhaustion * B1071_ExhaustionDiplomacyHelpers.WarPenaltyPerPoint;
            if (outcome.ShouldWarBeDeclared)
                __result -= penalty;
            else
                __result += penalty;
        }
    }

    [HarmonyPatch(typeof(MakePeaceKingdomDecision), nameof(MakePeaceKingdomDecision.DetermineSupport))]
    public static class B1071_MakePeaceDecisionExhaustionSupportPatch
    {
        static void Postfix(MakePeaceKingdomDecision __instance, Clan clan, DecisionOutcome possibleOutcome, ref float __result)
        {
            if (__instance?.Kingdom == null || clan == null)
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
                return;
            }

            float bonus = exhaustion * B1071_ExhaustionDiplomacyHelpers.PeaceBonusPerPoint;
            if (outcome.ShouldPeaceBeDeclared)
                __result += bonus;
            else
                __result -= bonus;
        }
    }

    [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.AddDecision))]
    public static class B1071_BlockWarDecisionAtHighExhaustionPatch
    {
        static bool Prefix(KingdomDecision kingdomDecision)
        {
            if (kingdomDecision is not DeclareWarDecision declareWarDecision)
                return true;

            Kingdom? kingdom = declareWarDecision.Kingdom;
            if (kingdom == null)
                return true;

            if (B1071_ExhaustionDiplomacyHelpers.IsPlayerInfluencedContext(kingdom))
                return true;

            if (!B1071_ExhaustionDiplomacyHelpers.TryGetExhaustion(kingdom, out float exhaustion))
                return true;

            float threshold = B1071_ExhaustionDiplomacyHelpers.NoWarThreshold;
            if (exhaustion < threshold)
                return true;

            Debug.Print($"[Byzantium1071][Diplomacy] Blocked DeclareWarDecision for {kingdom.Name} due to exhaustion {exhaustion:0.0} >= {threshold:0.0}.");
            return false;
        }
    }
}
