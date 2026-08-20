using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
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
    ///       With -24%: 45/180 = 25% chance gate fires per hit
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
    /// Damage reduction per tier comes from B1071_CombatRealismTuning, driven by the
    /// EliteSurvivabilityPreset setting. At the default Light preset:
    ///   T1 = 0%   T2 = 0%   T3 = -3%   T4 = -6%   T5 = -9%   T6+ = -12%
    /// Preset 3 restores the pre-v1.0.2.5 curve (-6/-12/-18/-24%). See that class for
    /// why the two tier systems must be tuned as one.
    ///
    /// Heroes excluded: their path uses AddHeroDamage (HP accumulation), not
    /// the single-hit RandomInt gate. They are near-invincible by default.
    ///
    /// VERSION NOTE: DefaultCombatSimulationModel.SimulateHit verified against
    /// Bannerlord v1.5.0. The troop-vs-troop overload is identified by:
    ///   (CharacterObject, CharacterObject, PartyBase, PartyBase,
    ///    float, MapEvent, BattleEnvironment, float, float)
    /// ⚠ v1.5.0 INSERTED the BattleEnvironment parameter in position 7. The previous
    /// 8-type array silently stopped resolving, so this patch did not attach at all on
    /// 1.5.0 until v1.0.2.5 — the class-level try/catch in PatchAssemblySafely swallowed
    /// it and the feature went quietly dead. If tier armor ever stops having an effect
    /// after a game update, check this array against the live signature FIRST.
    /// The ship-vs-ship overload (Ship, Ship, PartyBase, PartyBase, SiegeEngineType,
    /// float, MapEvent, ref int) added alongside Warsails uses different types, so the
    /// explicit argumentTypes array below still resolves the troop overload unambiguously.
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
            typeof(BattleEnvironment),// battleEnvironment (added in Bannerlord v1.5.0)
            typeof(float),            // strikerSideMorale
            typeof(float),            // struckSideMorale
        })]
    public static class B1071_TierArmorSimulationPatch
    {
        private static readonly TextObject _label = new TextObject("{=b1071_lbl_tier_armor}B1071 Tier Armor");

        // Postfix: inject struckTroop by parameter name (matches game method signature).
        // ref __result receives the ExplainedNumber returned by SimulateHit.
        // AddFactor(negative) reduces ResultNumber → lower damage integer fed into
        // the fatal-hit gate → gate fires less often for high-tier troops.
        public static void Postfix(CharacterObject struckTroop, ref ExplainedNumber __result)
        {
            try
            {
                if (struckTroop == null || struckTroop.IsHero) return;

                // Preset 0 (vanilla) and T1-T2 both come back as 0f.
                float armorFactor = B1071_CombatRealismTuning.GetArmorFactor(struckTroop.Tier);
                if (armorFactor == 0f) return;

                __result.AddFactor(armorFactor, _label);
            }
            catch (Exception ex) { TaleWorlds.Library.Debug.Print($"[Byzantium1071] TierArmorSimulationPatch error: {ex}"); }
        }
    }
}
