using System;
using Byzantium1071.Campaign.Settings;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.Patches
{
    /// <summary>
    /// Reduces simulated hit damage against higher-tier troops, modelling
    /// superior armor and battle experience deflecting blows.
    ///
    /// ──────────────────────────────────────────────────────────────────────
    /// ROOT CAUSE ANALYSIS: Why T6 troops die disproportionately in autoresolve
    /// ──────────────────────────────────────────────────────────────────────
    ///
    /// Vanilla troop victim selection (MapEventSide.SelectRandomSimulationTroop):
    ///   _selectedSimulationTroopIndex = MBRandom.RandomInt(NumRemainingSimulationTroops)
    ///   → Uniform random. Every surviving simulation troop is equally likely
    ///     to be selected as the current victim per simulation tick.
    ///
    /// Vanilla spawn prioritization (DefaultTroopSupplierProbabilityModel):
    ///   Default = UnitSpawnPrioritizations.HighLevel → key = troop.Troop.Level
    ///   Priority list is sorted DESCENDING by level before AllocateTroops.
    ///   sizeOfSide = sum(party.NumberOfHealthyMembers) = full party size.
    ///   Result: all troops enter the simulation list, highest-tier first,
    ///   but since SelectRandomSimulationTroop is uniform-random the insertion
    ///   order does not bias who gets hit.
    ///
    /// Fatal-hit gate (ApplySimulationDamageToSelectedTroop):
    ///   MBRandom.RandomInt(troop.MaxHitPoints()) &lt; damage
    ///   → Higher MaxHP (T6) means a proportionally larger range, so the gate
    ///     fires LESS often for high-tier troops per hit at equal damage values.
    ///   BUT vanilla SimulateHit returns the same damage regardless of the struck
    ///   troop's tier or armor — damage is determined purely by the STRIKER's
    ///   stats and the battle's strength ratio. So a T1 bandit deals the same
    ///   simulated damage to a T6 knight as to a T1 recruit.
    ///   Combined with T6's higher MaxHP, T6 already has mild protection, but
    ///   at typical bandit damage values this protection is often insufficient.
    ///
    /// GetSurvivalChance (B1071_FatalityPatch, already in place):
    ///   Adds +25% survival bonus for T6 at the wound-vs-kill decision point.
    ///   This only applies AFTER the fatal-hit gate fires. It does not reduce
    ///   how often T6 enters the check.
    ///
    /// ──────────────────────────────────────────────────────────────────────
    /// FIX: Reduce SimulateHit damage for higher-tier struck troops
    /// ──────────────────────────────────────────────────────────────────────
    ///
    /// CORRECT HOOK: DefaultCombatSimulationModel.SimulateHit
    ///   (public override, 8-parameter troop-vs-troop overload)
    ///
    ///   Called from MapEvent.SimulateSingleTroopHit immediately before
    ///   ApplySimulationDamageToSelectedTroop. The ExplainedNumber result's
    ///   ResultNumber is cast to int and becomes the `damage` parameter at the
    ///   fatal-hit gate: RandomInt(MaxHitPoints) &lt; damage.
    ///
    ///   By reducing damage in the result ExplainedNumber, we lower the
    ///   probability of the fatal-hit gate firing for high-tier targets:
    ///
    ///     Example, T6 knight, MaxHP=180, base damage=60:
    ///       Vanilla:   60/180 = 33% chance gate fires per hit
    ///       With -30%: 42/180 = 23% chance gate fires per hit
    ///
    ///   Stacked with FatalityPatch (+25% T6 survival when gate fires), T6
    ///   troops now benefit from BOTH fewer kill-check triggers AND better
    ///   survival when triggered.
    ///
    /// LIVE BATTLE NOTE:
    ///   DefaultCombatSimulationModel.SimulateHit is ONLY called during
    ///   autoresolve simulation. In live battles, troop damage is handled
    ///   by the Mission combat engine using actual weapon/armor stats.
    ///   GetSurvivalChance (FatalityPatch) covers live-battle aftermath.
    ///   For live fights, T6 going down frequently is a positioning effect
    ///   (they engage the most enemies) — not addressable without mission AI
    ///   changes.
    ///
    /// Damage reduction per tier (additive negative factor applied in Postfix):
    ///   T1 = 0%   T2 = -6%   T3 = -12%   T4 = -18%   T5 = -24%   T6+ = -30%
    ///
    /// Heroes excluded: their path uses AddHeroDamage (HP accumulation), not
    /// the single-hit RandomInt gate. They are near-invincible by default.
    ///
    /// VERSION NOTE: DefaultCombatSimulationModel.SimulateHit verified against
    /// Bannerlord v1.3.15. The 8-param overload is identified by:
    ///   (CharacterObject, CharacterObject, PartyBase, PartyBase,
    ///    float, MapEvent, float, float)
    /// The ship-vs-ship overload uses different types and is not affected.
    /// Reverify the overload signature after Bannerlord patches.
    /// </summary>
    [HarmonyPatch(typeof(DefaultCombatSimulationModel),
        nameof(DefaultCombatSimulationModel.SimulateHit),
        new Type[]
        {
            typeof(CharacterObject),  // strikerTroop
            typeof(CharacterObject),  // struckTroop
            typeof(PartyBase),        // strikerParty
            typeof(PartyBase),        // struckParty
            typeof(float),            // strikerAdvantage
            typeof(MapEvent),         // battle
            typeof(float),            // strikerSideMorale
            typeof(float),            // struckSideMorale
        })]
    public static class B1071_TierArmorSimulationPatch
    {
        private static readonly TextObject _label = new TextObject("B1071 Tier Armor");

        // Postfix: inject struckTroop by parameter name (matches game method signature).
        // ref __result receives the ExplainedNumber returned by SimulateHit.
        // AddFactor(negative) reduces ResultNumber → lower damage integer fed into
        // the fatal-hit gate → gate fires less often for high-tier troops.
        public static void Postfix(CharacterObject struckTroop, ref ExplainedNumber __result)
        {
            try
            {
                if (struckTroop == null || struckTroop.IsHero) return;
                if (!(B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults).EnableTierArmorSimulation) return;

                int tier = Math.Max(1, struckTroop.Tier);
                if (tier < 2) return; // T1 gets no reduction

                // T2 = -6%, T3 = -12%, T4 = -18%, T5 = -24%, T6+ = -30%
                float armorFactor = -(tier - 1) * 0.06f;
                if (armorFactor < -0.30f) armorFactor = -0.30f;

                __result.AddFactor(armorFactor, _label);
            }
            catch (Exception ex) { TaleWorlds.Library.Debug.Print($"[Byzantium1071] TierArmorSimulationPatch error: {ex}"); }
        }
    }
}
