using System;
using Byzantium1071.Campaign.Settings;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.Patches
{
    /// <summary>
    /// Army Economics — Tier-exponential scaling for hire costs and daily wages.
    ///
    /// ──────────────────────────────────────────────────────────────────────
    /// DESIGN INTENT
    /// ──────────────────────────────────────────────────────────────────────
    ///
    /// Historically, raised armies were expensive in ways Bannerlord's vanilla
    /// model does not reflect:
    ///
    ///   • Hiring a veteran knight required a large one-time signing fee —
    ///     comparable to equipping them. A spearman cost a fraction of that.
    ///   • Elite troops demanded higher wages because they were scarce specialists
    ///     with real alternative employment (noble retinues, mercenary companies).
    ///   • Upgrading a soldier implied reequipping them, training them, and
    ///     absorbing the time value — the gold cost should grow sharply by tier.
    ///
    /// Result: players must think hard before amassing T5-T6 forces.
    ///   Maintaining 50 T6 knights at Moderate is ~60 denars/day each.
    ///   A 100-man T6 retinue costs more per day than a mid-tier lord earns.
    ///
    /// ──────────────────────────────────────────────────────────────────────
    /// HOOKS
    /// ──────────────────────────────────────────────────────────────────────
    ///
    /// DefaultPartyWageModel.GetTroopRecruitmentCost (public override)
    ///   → Returns ExplainedNumber. Additive factor applied per tier index.
    ///   → Called for player and AI recruiting via RecruitmentCampaignBehavior.
    ///   → Also called internally by DefaultPartyTroopUpgradeModel.GetGoldCostForUpgrade:
    ///       stat = (recruitCost(target) - recruitCost(current)) / 2
    ///     This means our hire cost scaling AUTOMATICALLY cascades to upgrade
    ///     gold cost. No separate patch needed — and no double-stacking risk.
    ///     Example at Moderate:
    ///       T5 hire: 400 × (1+2.50) = 1400g
    ///       T6 hire: 600 × (1+4.00) = 3000g
    ///       T5→T6 upgrade: (3000 - 1400) / 2 = 800g  (vanilla: ~100g)
    ///
    /// DefaultPartyWageModel.GetCharacterWage (public override)
    ///   → Returns int. Multiplied directly (rounded to nearest int).
    ///   → Heroes excluded — companion/lord wages are a separate concern.
    ///   → CaravanGuards excluded — exempted from wage inflation so caravan
    ///     profitability is not destroyed by higher-tier wage presets.
    ///   → Called for every troop in every party every settlement tick.
    ///   → Mercenaries already pay a 1.5× vanilla bonus on top; our factor stacks
    ///     on the mercenary wage (vanilla 1.5× × our multiplier), keeping mercs
    ///     expensive to maintain without applying extra hire cost.
    ///
    /// DefaultPartyWageModel.GetTotalWage (public override)
    ///   → Returns ExplainedNumber. Patched (B1071_GarrisonWagePatch) only when
    ///     mobileParty.IsGarrison == true. Applies a flat negative factor to
    ///     reduce the aggregate garrison wage bill by a configurable percentage.
    ///     Requires GetTotalWage instead of GetCharacterWage because the troop
    ///     CharacterObject has no party context (cannot detect garrison on its own).
    ///
    /// ──────────────────────────────────────────────────────────────────────
    /// PARITY
    /// ──────────────────────────────────────────────────────────────────────
    ///
    /// Both hooks apply to ALL parties — player and AI equally. AI lords pay the
    /// same elevated hire/upgrade costs and the same higher wages. This makes
    /// large-scale campaigning expensive for everyone, reflecting why historical
    /// kingdoms rarely fielded armies of elite soldiers.
    ///
    /// ──────────────────────────────────────────────────────────────────────
    /// OCCUPATION-SPECIFIC ADJUSTMENTS
    /// ──────────────────────────────────────────────────────────────────────
    ///
    /// Vanilla DefaultPartyWageModel.GetTroopRecruitmentCost applies:
    ///   result.Add(BaseNumber * 2f)  for Occupation.Mercenary, .Gangster, .CaravanGuard
    ///   → These troops are 3× more expensive to hire than regular soldiers.
    ///
    /// Our patch cancels this via result.Add(-BaseNumber * 2f / 3f).
    ///
    /// WHY /3: ExplainedNumber.Add() MUTATES BaseNumber in place. After vanilla's
    ///   result.Add(B * 2f), BaseNumber changes from B to B + 2B = 3B.
    ///   In our Postfix, __result.BaseNumber is already 3B (or (B+horse)*3 if mounted).
    ///   The occupation premium actually added was (3B / 3) * 2 = 2B, so we subtract
    ///   __result.BaseNumber * 2f / 3f to cancel exactly that amount, leaving B.
    ///   After this: _unclampedResultNumber = B * (1 + SumOfFactors + our_tier_factor).
    ///
    /// Vanilla GetCharacterWage applies:
    ///   num *= 1.5f  for Occupation.Mercenary
    ///   → Mercs cost 1.5× more per day than regular soldiers (maintained).
    ///
    /// Caravan Guards (Occupation.CaravanGuard):
    ///   • Hire cost: normalised to regular soldier cost (vanilla 3× cancelled).
    ///   • Daily wage: EXEMPT from B1071_WagePatch. Vanilla wages retained so
    ///     caravan profitability is not destroyed by higher wage presets.
    ///
    /// Garrison parties:
    ///   • Hire cost: same as field troops of the same tier (standard tier factor).
    ///   • Daily wage: reduced by GarrisonWagePercent% of field wage via the
    ///     GetTotalWage patch (B1071_GarrisonWagePatch). Default 60% = 40% discount.
    ///
    /// ──────────────────────────────────────────────────────────────────────
    /// PRESET REFERENCE TABLE
    /// ──────────────────────────────────────────────────────────────────────
    ///
    /// Hire cost AddFactor per preset and tier (based on vanilla level brackets):
    ///
    /// IMPORTANT: vanilla GetTroopRecruitmentCost is LEVEL-based, not tier-based.
    /// The bracket for T6 troops spans levels 26–36+:
    ///   L≤26 = 400g  │  L≤31 = 600g  │  L≤36 = 1000g  │  L>36 = 1500g
    /// Our tier_index lookup (troop.Tier - 1) applies one factor to all T6 troops
    /// regardless of their exact level, so final costs vary within the tier:
    ///
    ///   Tier │ Vanilla range   │ Light (+%)       │ Moderate (+%)         │ Severe (+%)
    ///   ─────┼─────────────────┼──────────────────┼───────────────────────┼──────────────────
    ///   T1   │  10–20g         │  10–20g  (+0%)   │  11–22g   (+10%)      │  13–25g  (+25%)
    ///   T2   │  50g            │  58g     (+15%)  │  65g      (+30%)      │  88g     (+75%)
    ///   T3   │  100–200g       │ 135–270g (+35%)  │ 175–350g  (+75%)      │ 275–550g (+175%)
    ///   T4   │  200–400g       │ 330–660g (+65%)  │  500g–1g  (+150%)     │  900g–2g (+350%)
    ///   T5   │  400–600g       │  800g–1g (+100%) │ 1400–2100g(+250%)     │  2800–4g (+600%)
    ///   T6   │  600–1500g      │ 1500–3750g(+150%)│ 3000–7500g(+400%)     │ 6600–16g (+1000%)
    ///   (Horse mounts add their own vanilla bonus on top)
    ///
    /// ⚠ AI MERCENARY CAP INTERACTION (vanilla hardcoded):
    ///   RecruitmentCampaignBehavior.CheckRecruiting contains:
    ///     if (roundedResultNumber2 < 5000) { ... recruit ... }
    ///   Any troop costing ≥5000g is COMPLETELY SKIPPED for AI mercenary tavern purchases.
    ///   At Moderate: T6 with base 1000g → 5000g = blocked. Base 600g → 3000g = fine.
    ///   At Severe:   T5+ with base ≥455g → 5000g = blocked from AI tavern purchase.
    ///   This only affects TAVERN MERCENARIES. Volunteer recruitment from notables
    ///   has no such cap (per-troop gold check only). The interaction is intentional
    ///   — very expensive mercs being too costly even for AI is realistic.
    ///
    /// Upgrade gold cost (cascades automatically — no direct patch):
    ///   T4→T5 │ Vanilla≈50g  │ Light≈235g  │ Moderate≈450g  │ Severe≈950g
    ///   T5→T6 │ Vanilla≈100g │ Light≈350g  │ Moderate≈800g  │ Severe≈1900g
    ///
    /// Daily wage per troop (rounds to nearest integer):
    ///   Tier │ Vanilla │ Light      │ Moderate       │ Severe
    ///   ─────┼─────────┼────────────┼────────────────┼─────────────
    ///   T1   │  1d/day │  1d  (+0%) │  1d  (+0%)     │  2d (+10%)
    ///   T2   │  2d/day │  2d  (+0%) │  2d  (+10%)    │  3d (+25%)
    ///   T3   │  5d/day │  6d (+20%) │  8d  (+50%)    │  9d (+70%)
    ///   T4   │  8d/day │ 11d (+40%) │ 16d (+100%)    │ 20d (+150%)
    ///   T5   │ 12d/day │ 20d (+70%) │ 31d (+160%)    │ 42d (+250%)
    ///   T6   │ 17d/day │ 34d (+100%)│ 60d (+250%)    │ 85d (+400%)
    ///   (100 T6 at Moderate = 6000d/day; at Severe = 8500d/day)
    ///
    /// ──────────────────────────────────────────────────────────────────────
    /// VERSION NOTE
    /// ──────────────────────────────────────────────────────────────────────
    ///
    /// DefaultPartyWageModel.GetTroopRecruitmentCost and GetCharacterWage
    /// are public virtual methods on the abstract base class — identified by
    /// nameof() at compile time. Should be stable across minor Bannerlord patches.
    /// Verified against v1.3.15.
    /// </summary>

    // ── Hire Cost ─────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(DefaultPartyWageModel), nameof(DefaultPartyWageModel.GetTroopRecruitmentCost))]
    public static class B1071_HireCostPatch
    {
        private static readonly TextObject _label = new TextObject("{=b1071_lbl_tier_econ}B1071 Tier Economics");
        private static readonly TextObject _cancelLabel = new TextObject("{=b1071_lbl_norm_occ}B1071 Normalises Occupation");

        // [preset 0-3][tier_index 0-5]  — AddFactor values
        // tier_index = Math.Clamp(troop.Tier - 1, 0, 5)
        private static readonly float[][] _hireFactors =
        {
            // 0 = Off (vanilla)
            new[] { 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f },
            // 1 = Light
            new[] { 0.00f, 0.15f, 0.35f, 0.65f, 1.00f, 1.50f },
            // 2 = Moderate
            new[] { 0.10f, 0.30f, 0.75f, 1.50f, 2.50f, 4.00f },
            // 3 = Severe
            new[] { 0.25f, 0.75f, 1.75f, 3.50f, 6.00f, 10.0f },
        };

        public static void Postfix(CharacterObject troop, ref ExplainedNumber __result)
        {
            try
            {
            if (troop == null) return;

            int preset = (B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults).HireUpgradeCostPreset;
            if (preset <= 0 || preset > 3) return;

            int tierIdx = Math.Max(0, Math.Min(5, troop.Tier - 1));
            float factor = _hireFactors[preset][tierIdx];

            // Vanilla GetTroopRecruitmentCost applies result.Add(BaseNumber * 2f) for
            // Occupation.Mercenary, Occupation.Gangster, and Occupation.CaravanGuard.
            //
            // CRITICAL: ExplainedNumber.Add() MUTATES BaseNumber in-place.
            //   After vanilla's Add(B*2f): BaseNumber = B + 2B = 3B (not 10 anymore!)
            //   _unclampedResultNumber = BaseNumber * (1 + SumOfFactors)
            //
            // To cancel exactly what vanilla added, divide by 3:
            //   premium_added = BaseNumber_before_add * 2 = (current_BaseNumber / 3) * 2
            //                 = current_BaseNumber * 2f / 3f
            // After subtracting: BaseNumber returns to original_B (or original_B+horse).
            //
            // ⚠ Add(-BaseNumber * 2f) subtracts 3× too much (BaseNumber is already 3×
            //   original). This drives _unclampedResultNumber to -3B * something,
            //   which floors at LimitMin(1f): 5 T4 CaravanGuards = 5 denars.
            // ⚠ AddFactor(-2f) fails for the same reason: subtracts 3B×2 = 6× original.
            bool hasVanillaHirePremium = troop.Occupation == Occupation.Mercenary
                                      || troop.Occupation == Occupation.Gangster
                                      || troop.Occupation == Occupation.CaravanGuard;
            if (hasVanillaHirePremium)
                __result.Add(-__result.BaseNumber * 2f / 3f, _cancelLabel);

            if (factor != 0f)
                __result.AddFactor(factor, _label);
            }
            catch (Exception ex) { TaleWorlds.Library.Debug.Print($"[Byzantium1071] HireCostPatch error: {ex}"); }
        }
    }

    // ── Daily Wages ────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(DefaultPartyWageModel), nameof(DefaultPartyWageModel.GetCharacterWage))]
    public static class B1071_WagePatch
    {
        // [preset 0-3][tier_index 0-5]  — multiplicative factors
        // applied as: result = Round(vanilla * (1 + factor))
        private static readonly float[][] _wageFactors =
        {
            // 0 = Off (vanilla)
            new[] { 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f },
            // 1 = Light
            new[] { 0.00f, 0.00f, 0.20f, 0.40f, 0.70f, 1.00f },
            // 2 = Moderate
            new[] { 0.00f, 0.10f, 0.50f, 1.00f, 1.60f, 2.50f },
            // 3 = Severe
            new[] { 0.10f, 0.25f, 0.70f, 1.50f, 2.50f, 4.00f },
        };

        public static void Postfix(CharacterObject character, ref int __result)
        {
            try
            {
            // Heroes: companions and lords have different wage logic; skip.
            if (character == null || character.IsHero) return;

            // Caravan guards: exempt from wage inflation so caravans remain profitable.
            // Vanilla already handles their wage tier via the base formula; inflating it
            // further would make caravans economically unviable at higher presets.
            if (character.Occupation == Occupation.CaravanGuard) return;

            int preset = (B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults).WagePreset;
            if (preset <= 0 || preset > 3) return;

            int tierIdx = Math.Max(0, Math.Min(5, character.Tier - 1));
            float factor = _wageFactors[preset][tierIdx];
            if (factor == 0f) return;

            // Preserve a minimum of 1d/day (LimitMin in GetTotalWage handles the party
            // aggregate floor, but individual wages should not go below 1 either).
            __result = Math.Max(1, (int)Math.Round(__result * (1f + factor)));
            }
            catch (Exception ex) { TaleWorlds.Library.Debug.Print($"[Byzantium1071] WagePatch error: {ex}"); }
        }
    }

    // ── Garrison Wages ────────────────────────────────────────────────────

    /// <summary>
    /// Applies a flat wage discount to garrison parties by reducing the total wage
    /// result returned from DefaultPartyWageModel.GetTotalWage when the party is
    /// detected as a garrison (mobileParty.IsGarrison == true).
    ///
    /// WHY GetTotalWage instead of GetCharacterWage:
    ///   GetCharacterWage(CharacterObject) has no party context — the troop object
    ///   alone cannot tell whether it is stationed in a garrison or on the march.
    ///   GetTotalWage(MobileParty, bool) has the full party reference, allowing us to
    ///   detect IsGarrison and reduce the aggregate wage bill accordingly.
    ///
    /// HISTORICAL RATIONALE:
    ///   Garrison troops were typically lower-cost soldiers (militia, city guards)
    ///   who received reduced or irregular pay compared to campaigning soldiers.
    ///   They often received food and lodging in lieu of full coin payment.
    ///   Vanilla already applies perk-based and building-based garrison discounts
    ///   in the same method (DrillSergant, StiffUpperLip, GarrisonWageReduction).
    ///   This patch extends those discounts with a configurable flat multiplier.
    ///
    /// MCM: "Garrison wage % of field" (Army Economics group)
    ///   60 = garrison party pays 60% of what field troops pay (40% discount).
    ///   100 = no discount (vanilla + our WagePatch behaviour applies).
    /// </summary>
    [HarmonyPatch(typeof(DefaultPartyWageModel), nameof(DefaultPartyWageModel.GetTotalWage))]
    public static class B1071_GarrisonWagePatch
    {
        private static readonly TextObject _label = new TextObject("{=b1071_lbl_garrison_discount}B1071 Garrison Discount");

        public static void Postfix(MobileParty mobileParty, ref ExplainedNumber __result)
        {
            try
            {
            if (mobileParty == null || !mobileParty.IsGarrison) return;

            var settings = B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;
            if (!settings.EnableGarrisonWageDiscount) return;

            // Convert percent-of-field to AddFactor offset:
            //   GarrisonWagePercent = 60 → factor = 60/100 - 1 = -0.40 (40% discount)
            //   GarrisonWagePercent = 100 → factor = 0 (no change)
            float factor = settings.GarrisonWagePercent / 100f - 1f;
            if (factor >= 0f) return;

            __result.AddFactor(factor, _label);
            }
            catch (Exception ex) { TaleWorlds.Library.Debug.Print($"[Byzantium1071] GarrisonWagePatch error: {ex}"); }
        }
    }
}
