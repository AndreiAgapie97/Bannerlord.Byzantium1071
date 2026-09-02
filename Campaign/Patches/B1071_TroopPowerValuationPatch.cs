using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;

namespace Byzantium1071.Campaign.Patches
{
    /// <summary>
    /// Teaches the native AI what a high-tier troop is actually worth under Campaign++.
    ///
    /// THE GAP:
    ///   B1071_TierArmorSimulationPatch lowers the damage a high-tier troop takes in
    ///   autoresolve, and B1071_FatalityPatch turns more of its remaining fatal hits into
    ///   wounds. Both are real: elite troops genuinely survive far longer than vanilla.
    ///   Neither touches how the AI PRICES those troops. Vanilla power is a pure tier
    ///   curve -- DefaultMilitaryPowerModel.GetDefaultTroopPower returns
    ///   (2 + tier) * (10 + tier) * 0.02f, a flat T6 = 3.9x T1 -- and Campaign++ never
    ///   changes a troop's tier, only what that tier can absorb. So the AI kept valuing
    ///   elite stacks at vanilla worth: declining fights it would now win, and walking
    ///   into elite garrisons it should refuse. B1071_CombatRealismTuning applies "to AI
    ///   and player equally", but that is parity of effect, not parity of knowledge.
    ///
    /// WHY THIS HOOK:
    ///   Every strength comparison in the campaign funnels through this one method --
    ///   PartyBase.EstimatedStrength, GetCustomStrength, MobileParty
    ///   .GetTotalLandStrengthWithFollowers, Army.EstimatedStrength and
    ///   Kingdom.CurrentTotalStrength all reach MilitaryPowerModel.GetPowerOfParty, which
    ///   calls GetTroopPower, which calls GetDefaultTroopPower. Correcting it here fixes
    ///   engage-vs-avoid, siege target scoring, army formation and the diplomacy war
    ///   calculus in one place, with no double counting. Patching GetPowerOfParty instead
    ///   would miss GetTroopPower's other callers and would have to re-derive the
    ///   per-troop tier the roster loop already has.
    ///
    ///   PartyBase caches the result against MemberRoster.VersionNo (GetStrengthVersionNo),
    ///   so this runs only when a roster actually changes.
    ///
    ///   NOT ONLY AI DECISIONS: MapEventSide.OnTroopWounded/Killed/Routed accumulate
    ///   CasualtyStrength from GetTroopPower, and MapEvent.RecalculateStrengthOfSides sums
    ///   GetCustomStrength; both feed CalculateWinnerPartiesRenownInfluenceAndMoraleShares,
    ///   so renown and influence SHARES shift slightly. Battle OUTCOME does not: the only
    ///   thing SimulateHit takes is GetBattleAdvantage, which is purely Tactics skill and
    ///   perks (DefaultCombatSimulationModel.GetPartyBattleAdvantage) and reads no power at
    ///   all -- so this cannot double-count with B1071_TierArmorSimulationPatch.
    ///
    /// WHY THE FACTOR IS CENTRED, NOT INFLATIONARY:
    ///   Some vanilla gates compare power against hard-coded absolute constants tuned to
    ///   the vanilla scale -- DefaultArmyManagementCalculationModel.CanLordCreateArmy needs
    ///   a summed GetCustomStrength of 1000, and DefaultDiplomacyModel refuses war below a
    ///   CurrentTotalStrength of 500. The AI's combat logic only needs the RATIO between
    ///   tiers to be right, but those gates read the ABSOLUTE value, so scaling every tier
    ///   up would quietly change how often kingdoms raise armies and declare war.
    ///   B1071_EconomyMath.PowerFactor therefore holds tier 3 at exactly vanilla and moves
    ///   the other tiers around it. See TroopPowerMathTests for the guard.
    ///
    /// Heroes are skipped: GetDefaultTroopPower prices them off Hero.Level rather than
    /// Tier, and neither combat patch changes a hero's survivability (B1071_FatalityPatch
    /// exits early on IsHero), so there is nothing here to correct for them.
    /// </summary>
    [HarmonyPatch(typeof(DefaultMilitaryPowerModel), nameof(DefaultMilitaryPowerModel.GetDefaultTroopPower))]
    public static class B1071_TroopPowerValuationPatch
    {
        public static void Postfix(CharacterObject troop, ref float __result)
        {
            try
            {
                // Heroes are priced off Hero.Level, not Tier, and neither combat patch moves them.
                if (troop == null || troop.IsHero) return;

                // Preset 0 (vanilla) returns exactly 1f for every tier.
                float factor = B1071_CombatRealismTuning.GetPowerFactor(troop.Tier);
                if (factor == 1f) return;

                __result *= factor;
            }
            catch (Exception ex) { TaleWorlds.Library.Debug.Print($"[Byzantium1071] TroopPowerValuationPatch error: {ex}"); }
        }
    }
}
