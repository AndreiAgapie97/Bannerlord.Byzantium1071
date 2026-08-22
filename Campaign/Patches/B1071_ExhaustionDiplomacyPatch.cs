using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using HarmonyLib;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace Byzantium1071.Campaign.Patches
{
    internal static class B1071_ExhaustionDiplomacyHelpers
    {
        internal static IB1071Settings Settings => B1071_TestHooks.Settings ?? B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

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

        internal static bool EnableDebugLogs => Settings.DiplomacyDebugLogs || B1071_VerboseLog.Enabled;
        internal static bool UsePressureBands => Settings.EnableDiplomacyPressureBands;
        internal static bool IsTruceEnforcementEnabled => Settings.EnableTruceEnforcement;

        // Legacy thresholds (used when bands disabled)
        internal static float NoWarThreshold => Settings.DiplomacyNoNewWarThreshold;

        internal static float PeaceThreshold => Settings.DiplomacyPeacePressureThreshold;

        // Per-point rates (legacy path)
        internal static float WarPenaltyPerPoint => Settings.DiplomacyWarSupportPenaltyPerPoint;

        internal static float PeaceBonusPerPoint => Settings.DiplomacyPeaceSupportBonusPerPoint;

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
            int majorWars = 0;
            if (kingdom.FactionsAtWarWith == null) return 0f;
            for (int i = 0; i < kingdom.FactionsAtWarWith.Count; i++)
            {
                IFaction enemy = kingdom.FactionsAtWarWith[i];
                if (enemy is Kingdom && kingdom.IsAtWarWith(enemy))
                    majorWars++;
            }

            return B1071_ExhaustionMath.MajorWarPressureBias(majorWars, Settings);
        }

        internal static bool IsKingdomVsKingdomWarTarget(DeclareWarDecision decision)
        {
            return decision?.Kingdom != null && decision.FactionToDeclareWarOn is Kingdom;
        }

        internal static bool IsKingdomVsKingdomPeaceTarget(MakePeaceKingdomDecision decision)
        {
            return decision?.Kingdom != null && decision.FactionToMakePeaceWith is Kingdom;
        }

        /// <summary>
        /// Gets a peace-support bias driven by how depleted a kingdom's manpower pools are.
        /// Returns 0 when <see cref="B1071_McmSettings.EnableManpowerDiplomacyPressure"/> is off,
        /// or when average manpower fill is at or above the configured threshold.
        /// </summary>
        internal static float GetManpowerDiplomacyPeaceBias(Kingdom kingdom)
        {
            if (!Settings.EnableManpowerDiplomacyPressure) return 0f;

            B1071_ManpowerBehavior? behavior = B1071_ManpowerBehavior.Instance;
            if (behavior == null) return 0f;

            float avgRatio = behavior.GetKingdomAverageManpowerRatio(kingdom);
            return B1071_ExhaustionMath.ManpowerDiplomacyPeaceBias(avgRatio, Settings);
        }

        /// <summary>
        /// Returns true when <paramref name="attacker"/> has any war-party
        /// currently besieging a settlement belonging to <paramref name="defender"/>.
        /// Used to suppress peace bonuses during active offensive operations.
        /// </summary>
        internal static bool IsKingdomBesiegingFaction(Kingdom attacker, IFaction defender)
        {
            if (attacker == null || defender == null) return false;

            foreach (WarPartyComponent wpc in attacker.WarPartyComponents)
            {
                MobileParty? party = wpc.MobileParty;
                if (party == null) continue;

                Settlement? besieged = party.BesiegedSettlement;
                if (besieged != null && besieged.MapFaction == defender)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Returns a peace-vote penalty for wars younger than MinWarDurationDaysBeforeForcedPeace.
        /// Penalty is 300 at day 0 (virtually guarantees vote fails), fading linearly to 0 at the threshold.
        /// Returns 0 when there is no stance, no war, or the war is old enough.
        /// </summary>
        internal static float GetEarlyWarPeacePenalty(Kingdom kingdom, IFaction peaceTarget)
        {
            int minDays = Math.Max(0, Settings.MinWarDurationDaysBeforeForcedPeace);
            if (minDays <= 0) return 0f;

            if (peaceTarget == null) return 0f;

            StanceLink? stance = kingdom.GetStanceWith(peaceTarget);
            if (stance == null || !stance.IsAtWar) return 0f;

            // C+: Under multi-front crisis, use the emergency minimum for the penalty ramp.
            float elapsedDays = stance.WarStartDate.ElapsedDaysUntilNow;
            bool canApplyMultiFrontRelief = Settings.EnableMultiFrontWarRelief
                && Math.Max(1, Settings.EmergencyMinWarDays) < minDays;
            bool isMultiFrontCrisis = canApplyMultiFrontRelief
                && B1071_ManpowerBehavior.Instance?.IsMultiFrontCrisis(kingdom) == true;
            float penalty = B1071_ExhaustionMath.EarlyWarPeacePenalty(elapsedDays, isMultiFrontCrisis, Settings);
            int effectiveMinDays = minDays;
            if (isMultiFrontCrisis)
                effectiveMinDays = Math.Min(minDays, Math.Max(1, Settings.EmergencyMinWarDays));

            if (EnableDebugLogs)
                TaleWorlds.Library.Debug.Print(
                    $"[Byzantium1071][Diplomacy][Debug] Early-war peace penalty {penalty:0.0} " +
                    $"for {kingdom.Name} vs {peaceTarget.Name}: war age {elapsedDays:0.0} < {effectiveMinDays} days" +
                      (effectiveMinDays < minDays ? " (multi-front relief)." : "."));

            return penalty;
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
            try
            {
            if (__instance?.Kingdom == null || clan == null)
                return;

            if (!B1071_ExhaustionDiplomacyHelpers.IsKingdomVsKingdomWarTarget(__instance))
                return;

            if (B1071_ExhaustionDiplomacyHelpers.IsPlayerInfluencedContext(__instance.Kingdom, clan))
                return;

            // Manpower-diplomacy pressure (runs independently of war exhaustion system).
            if (possibleOutcome is DeclareWarDecision.DeclareWarDecisionOutcome mpDeclareOutcome)
            {
                float mpBias = B1071_ExhaustionDiplomacyHelpers.GetManpowerDiplomacyPeaceBias(__instance.Kingdom);
                if (mpBias > 0f)
                {
                    if (mpDeclareOutcome.ShouldWarBeDeclared) __result -= mpBias;
                    else __result += mpBias;
                }
            }

            if (!B1071_ExhaustionDiplomacyHelpers.TryGetExhaustion(__instance.Kingdom, out float exhaustion))
                return;

            if (possibleOutcome is not DeclareWarDecision.DeclareWarDecisionOutcome outcome)
                return;

            // Truce enforcement: always hard-block (truce is an absolute constraint).
            B1071_ManpowerBehavior? behavior = B1071_ManpowerBehavior.Instance;
            if (behavior != null && B1071_ExhaustionDiplomacyHelpers.IsTruceEnforcementEnabled
                && behavior.IsKingdomPairUnderTruce(__instance.Kingdom, __instance.FactionToDeclareWarOn, out _))
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
                float penalty = B1071_ExhaustionMath.WarSupportPenalty(
                    band,
                    exhaustion,
                    warBias,
                    B1071_ExhaustionDiplomacyHelpers.Settings);

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

            float legacyPenalty = B1071_ExhaustionMath.LegacyWarSupportPenalty(
                exhaustion,
                B1071_ExhaustionDiplomacyHelpers.GetMajorWarPressureBias(__instance.Kingdom),
                B1071_ExhaustionDiplomacyHelpers.Settings);
            if (outcome.ShouldWarBeDeclared)
                __result -= legacyPenalty;
            else
                __result += legacyPenalty;

            B1071_ExhaustionDiplomacyHelpers.RecordTelemetry($"DeclareWar support adjusted by {legacyPenalty:0.0} at exhaustion {exhaustion:0.0} for {__instance.Kingdom.Name}.");
            }
            catch (Exception ex) { TaleWorlds.Library.Debug.Print($"[Byzantium1071] DeclareWarExhaustionSupportPatch error: {ex}"); }
        }
    }

    [HarmonyPatch(typeof(MakePeaceKingdomDecision), nameof(MakePeaceKingdomDecision.DetermineSupport))]
    public static class B1071_MakePeaceDecisionExhaustionSupportPatch
    {
        static void Postfix(MakePeaceKingdomDecision __instance, Clan clan, DecisionOutcome possibleOutcome, ref float __result)
        {
            try
            {
            if (__instance?.Kingdom == null || clan == null)
                return;

            if (!B1071_ExhaustionDiplomacyHelpers.IsKingdomVsKingdomPeaceTarget(__instance))
                return;

            if (B1071_ExhaustionDiplomacyHelpers.IsPlayerInfluencedContext(__instance.Kingdom, clan))
                return;

            // ─── Siege-awareness guard ───
            // If this kingdom has armies actively besieging the peace target,
            // suppress all mod-added peace bonuses. Vanilla's own scoring still
            // applies — we just don't amplify it while a siege is in progress.
            if (B1071_ExhaustionDiplomacyHelpers.IsKingdomBesiegingFaction(
                    __instance.Kingdom, __instance.FactionToMakePeaceWith))
            {
                if (B1071_ExhaustionDiplomacyHelpers.EnableDebugLogs)
                    TaleWorlds.Library.Debug.Print(
                        $"[Byzantium1071][Diplomacy][Debug] MakePeace support suppressed for {__instance.Kingdom.Name} vs " +
                        $"{__instance.FactionToMakePeaceWith.Name}: active siege in progress.");
                B1071_ExhaustionDiplomacyHelpers.RecordTelemetry(
                    $"MakePeace support suppressed: {__instance.Kingdom.Name} besieging {__instance.FactionToMakePeaceWith.Name}.");
                return;
            }

            // ─── Early-war penalty: discourage council peace votes before minimum war duration ───
            // Applies a large negative bias to peace outcomes for wars younger than MinWarDurationDaysBeforeForcedPeace.
            // Penalty scales linearly: full strength at day 0, fading to zero at the threshold.
            // Player parity enforced when DiplomacyEnforcePlayerParity is on.
            if (possibleOutcome is MakePeaceKingdomDecision.MakePeaceDecisionOutcome earlyWarOutcome)
            {
                float earlyWarPenalty = B1071_ExhaustionDiplomacyHelpers.GetEarlyWarPeacePenalty(
                    __instance.Kingdom, __instance.FactionToMakePeaceWith);
                if (earlyWarPenalty > 0f)
                {
                    if (earlyWarOutcome.ShouldPeaceBeDeclared) __result -= earlyWarPenalty;
                    else __result += earlyWarPenalty;
                }
            }

            // Manpower-diplomacy pressure (runs independently of war exhaustion system).
            if (possibleOutcome is MakePeaceKingdomDecision.MakePeaceDecisionOutcome mpOutcome)
            {
                float mpBias = B1071_ExhaustionDiplomacyHelpers.GetManpowerDiplomacyPeaceBias(__instance.Kingdom);
                if (mpBias > 0f)
                {
                    if (mpOutcome.ShouldPeaceBeDeclared) __result += mpBias;
                    else __result -= mpBias;
                }
            }

            if (!B1071_ExhaustionDiplomacyHelpers.TryGetExhaustion(__instance.Kingdom, out float exhaustion))
                return;

            if (possibleOutcome is not MakePeaceKingdomDecision.MakePeaceDecisionOutcome outcome)
                return;

            // ─── WP5 Pressure Band path ───
            if (B1071_ExhaustionDiplomacyHelpers.UsePressureBands)
            {
                DiplomacyPressureBand band = B1071_ExhaustionDiplomacyHelpers.GetBand(__instance.Kingdom);
                float warBias = B1071_ExhaustionDiplomacyHelpers.GetMajorWarPressureBias(__instance.Kingdom);
                float bonus = B1071_ExhaustionMath.PeaceSupportBonus(
                    band,
                    exhaustion,
                    warBias,
                    B1071_ExhaustionDiplomacyHelpers.Settings);

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

            float legacyBonus = B1071_ExhaustionMath.LegacyPeaceSupportBonus(
                exhaustion,
                B1071_ExhaustionDiplomacyHelpers.GetMajorWarPressureBias(__instance.Kingdom),
                B1071_ExhaustionDiplomacyHelpers.Settings);
            if (outcome.ShouldPeaceBeDeclared)
                __result += legacyBonus;
            else
                __result -= legacyBonus;

            B1071_ExhaustionDiplomacyHelpers.RecordTelemetry($"MakePeace support adjusted by {legacyBonus:0.0} at exhaustion {exhaustion:0.0} for {__instance.Kingdom.Name}.");
            }
            catch (Exception ex) { TaleWorlds.Library.Debug.Print($"[Byzantium1071] MakePeaceExhaustionSupportPatch error: {ex}"); }
        }
    }

    [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.AddDecision))]
    public static class B1071_BlockWarDecisionAtHighExhaustionPatch
    {
        static bool Prefix(KingdomDecision kingdomDecision)
        {
            try
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
                if (!B1071_ExhaustionDiplomacyHelpers.IsTruceEnforcementEnabled)
                    return true;

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
            catch (Exception ex) { TaleWorlds.Library.Debug.Print($"[Byzantium1071] BlockWarDecisionPatch error: {ex}"); return true; }
        }
    }

    [HarmonyPatch(typeof(MakePeaceAction), nameof(MakePeaceAction.Apply))]
    public static class B1071_RegisterTruceAfterAnyPeacePatch
    {
        static void Postfix(IFaction faction1, IFaction faction2)
        {
            try
            {
                if (!(B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults).EnableTruceEnforcement) return;
                B1071_ManpowerBehavior.Instance?.RegisterKingdomPairTruce(faction1, faction2);
            }
            catch (Exception ex) { TaleWorlds.Library.Debug.Print($"[Byzantium1071] RegisterTruceAfterAnyPeacePatch error: {ex}"); }
        }
    }

    [HarmonyPatch(typeof(MakePeaceAction), nameof(MakePeaceAction.ApplyByKingdomDecision))]
    public static class B1071_RegisterTruceAfterDecisionPeacePatch
    {
        static void Postfix(IFaction faction1, IFaction faction2, int dailyTributeFrom1To2, int dailyTributeDuration)
        {
            try
            {
                if (!(B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults).EnableTruceEnforcement) return;
                B1071_ManpowerBehavior.Instance?.RegisterKingdomPairTruce(faction1, faction2);
            }
            catch (Exception ex) { TaleWorlds.Library.Debug.Print($"[Byzantium1071] RegisterTruceAfterDecisionPeacePatch error: {ex}"); }
        }
    }
}
