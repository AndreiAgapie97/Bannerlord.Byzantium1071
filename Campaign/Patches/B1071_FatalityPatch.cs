using System;
using Byzantium1071.Campaign.Settings;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;

namespace Byzantium1071.Campaign.Patches
{
    /// <summary>
    /// Boosts autoresolve survival probability for higher-tier NON-HERO troops.
    ///
    /// ROOT CAUSE of the old patch (PartyGroupTroopSupplier.OnTroopScoreHit):
    ///   • Not called during autoresolve at all — simulation goes through
    ///     MapEventSide.ApplySimulationDamageToSelectedTroop directly.
    ///   • Even in live battles, the isFatal flag in OnTroopScoreHit only
    ///     feeds XP calculation (CombatXpModel.GetXpFromHit); actual kill/wound
    ///     outcome is decided by the Mission engine via IAgentOriginBase callbacks.
    ///
    /// CORRECT HOOK: DefaultPartyHealingModel.GetSurvivalChance
    ///   Called from MapEventSide.ApplySimulationDamageToSelectedTroop — the
    ///   actual kill-vs-wound gate for autoresolve.
    ///
    ///   Regular troop path (our primary target):
    ///     • if (RandomInt(MaxHitPoints) &lt; damage)           → entering survival check
    ///     • if (RandomFloat &lt; survivalChance)              → WOUNDED (survived)
    ///     • else                                             → KILLED
    ///     Higher __result = more likely wounded. ✓
    ///
    ///   Hero path (WHY WE SKIP):
    ///     • Only triggered on the SECOND hit (first hit just wounds them)
    ///     • AddFactor(50f) in the formula makes base survival ~97-99% for heroes
    ///     • Our bonus would always be clamped to 1.0 — zero effective change
    ///
    ///   Garrison/siege note:
    ///     Garrison defenders often have no MobileParty (settlement party).
    ///     Vanilla base survival for them = 0% (no surgeon, no level bonus applied).
    ///     This patch intentionally gives T6 garrison defenders +25% — elite
    ///     troops resist total wipe-outs even without a field surgeon.
    ///
    ///   Fast-path early exits (mirrors base method guard conditions):
    ///     • __result >= 1f  → blunt no-kill damage / VeryEasy / Easy difficulty
    ///     • character.IsHero → skip, heroes unaffected (see above)
    ///
    /// Tier bonus (additive flat, on top of Medicine-based base rate):
    ///   T1 = +0%   T2 = +5%   T3 = +10%
    ///   T4 = +15%  T5 = +20%  T6+ = +25% (capped)
    /// </summary>
    [HarmonyPatch(typeof(DefaultPartyHealingModel), nameof(DefaultPartyHealingModel.GetSurvivalChance))]
    public static class B1071_FatalityPatch
    {
        public static void Postfix(CharacterObject character, ref float __result)
        {
            try
            {
                // Fast path: base already returned max survival (blunt no-kill, VeryEasy hero, Easy player)
                if (__result >= 1f) return;
                // Skip heroes: AddFactor(50) pushes their base to ~98%, bonus always clamps to 1.0
                if (character == null || character.IsHero) return;
                if (!(B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults).EnableTierSurvivability) return;

                int tier = Math.Max(1, character.Tier);

                // T1=+0%, T2=+5%, T3=+10%, T4=+15%, T5=+20%, T6+=+25%
                float tierBonus = (tier - 1) * 0.05f;
                if (tierBonus > 0.25f) tierBonus = 0.25f;

                __result = Math.Min(1f, __result + tierBonus);
            }
            catch (Exception ex) { TaleWorlds.Library.Debug.Print($"[Byzantium1071] FatalityPatch error: {ex}"); }
        }
    }
}
