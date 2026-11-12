using System;
using Byzantium1071.Campaign.Settings;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.Patches
{
    /// <summary>
    /// Progressive taper on settlement revenue: town tax, town tariffs, and village income.
    ///
    /// ──────────────────────────────────────────────────────────────────────
    /// PROBLEM
    /// ──────────────────────────────────────────────────────────────────────
    ///
    /// Vanilla pays a fief's owner a fixed share of the settlement's revenue, with no curvature
    /// anywhere in the chain:
    ///
    ///   • Town tax       = Prosperity * 0.35, then policy, loyalty, security and building factors
    ///   • Town tariff    = town.TradeTaxAccumulated / RevenueSmoothenFraction(), a stored pool
    ///   • Village income = village.TradeTaxAccumulated / RevenueSmoothenFraction(), a stored pool
    ///
    /// Neither base is bounded by the game. Tax rises with prosperity without limit, and a tariff
    /// pool is resupplied by every sale the town makes with nothing in the game capping it -- it
    /// settles at roughly five days of the town's trade throughput rather than growing forever,
    /// but the throughput itself is what grows as a campaign matures, and neither model answers
    /// to how the campaign is actually played. By the late game a single mature town can out-earn
    /// an entire early-game kingdom, which is what makes settlement income feel unearned rather
    /// than earned. This is vanilla behaviour -- Campaign++ patches no tax or tariff model and
    /// never has -- but it is the one part of the settlement economy that does not respond to the
    /// campaign's own state.
    ///
    /// ──────────────────────────────────────────────────────────────────────
    /// FIX
    /// ──────────────────────────────────────────────────────────────────────
    ///
    /// Postfixes that scale the amount the CLAN receives, using the curve, the reference base and
    /// the knee documented on B1071_RevenueMath. The strength is a ceiling on the share a settlement
    /// keeps: at or below the knee every settlement keeps exactly the
    /// strength share (at 100% that is exactly vanilla), and the curve reduces only the portion
    /// above that knee. The knee is what aims that extra cut at the largest
    /// fiefs instead of spreading it evenly -- it does not shield small ones; the only hard
    /// exemptions are an empty pool and the fixed 40-prosperity tax floor.
    ///
    /// The taper deliberately scales the payout and NOT the settlement's side of the transaction.
    /// The drain a tariff applies to TradeTaxAccumulated stays at its vanilla value, so this can
    /// only ever remove gold from the campaign, never create it: the settlement pays exactly what
    /// it paid before, and the clan receives what is left after the taper. That asymmetry is the
    /// safe direction. The reverse -- cutting the drain while paying the full amount -- is the
    /// money-printing shape.
    ///
    /// RevenueSmoothenFraction is NOT patched. It is shared with caravan income, workshop income
    /// and mercenary pay, so changing it would silently nerf all three.
    ///
    /// SCOPE: town tax, town tariffs, and village income only. Caravans, workshops, mercenary pay,
    /// trade agreements, tribute, and the CrownDuty / RoadTolls / LandTax policy drains are left
    /// exactly as they were.
    ///
    /// One knock-on is not scope but consequence: DefaultClanFinanceModel.AddRulingClanIncome sizes
    /// the WarTax policy's payout from SettlementTaxModel.CalculateTownTax, so tapering tax also
    /// reduces WarTax income for whichever clan rules the kingdom. That is inherent in tapering the
    /// tax the policy is derived from, not a second patch.
    ///
    /// MCM: "Settlement Revenue" group, master toggle plus a strength, a curve and a knee per base.
    /// All three targets are public overrides named with nameof(), so PatchSignatureTests.
    /// DeclarativeHarmonyPatchTargetStillExists picks them up by reflection automatically, and
    /// SubModule.VerifyCriticalPatches asserts at launch that each postfix actually attached.
    /// </summary>
    internal static class B1071_RevenueTaper
    {
        /// <summary>
        /// The tooltip line a taper reports itself under. Built once, because this runs for every
        /// fief of every clan on every daily finance tick.
        /// </summary>
        private static readonly TextObject Label = new TextObject("{=b1071_revenue_taper}Revenue Taper");

        internal static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        /// <summary>
        /// Scales an ExplainedNumber down toward zero and records the change as its own line, so
        /// the finance tooltip's breakdown still adds up to the total it shows.
        ///
        /// The comparison against zero is not defensive padding. Town tax is clamped to
        /// [0, float.MaxValue] by DefaultSettlementTaxModel, but the tariff result is not clamped
        /// anywhere, and it sums perk and building contributions that can be negative. For a
        /// negative value, multiplying by (scale - 1) yields a POSITIVE delta, so without this
        /// guard a taper would quietly raise income instead of lowering it.
        /// </summary>
        internal static void Apply(ref ExplainedNumber result, float scale)
        {
            float current = result.ResultNumber;
            if (current <= 0f || float.IsNaN(current) || float.IsInfinity(current)) return;
            if (scale >= 1f) return;

            // Add() changes BaseNumber, so existing factors would multiply a delta derived
            // from ResultNumber a second time. Flatten the already-resolved breakdown first.
            float target = (float)Math.Floor((double)current * Math.Max(0f, scale));
            var adjusted = new ExplainedNumber(0f, result.IncludeDescriptions);
            adjusted.AddFromExplainedNumber(result, null);
            adjusted.Add(target - current, Label);
            result = adjusted;
        }
    }

    /// <summary>
    /// Postfix on the town tariff payout. The amount withdrawn from the town is computed inside
    /// the original method from the untampered pool, so scaling only the returned value is what
    /// keeps the drain at its vanilla size.
    /// </summary>
    [HarmonyPatch(typeof(DefaultClanFinanceModel), nameof(DefaultClanFinanceModel.CalculateTownIncomeFromTariffs))]
    internal static class B1071_SettlementTariffTuningPatch
    {
        // Capture before vanilla drains the pool; a postfix alone cannot recover that basis.
        // This prefix only observes state and always allows the original method to run.
        public static void Prefix(DefaultClanFinanceModel __instance, Town town, out int __state)
        {
            __state = 0;
            try
            {
                var settings = B1071_RevenueTaper.Settings;
                if (settings == null || !settings.EnableSettlementRevenueTuning || town == null) return;
                __state = (int)((float)town.TradeTaxAccumulated / __instance.RevenueSmoothenFraction());
            }
            catch (Exception ex)
            {
                B1071_SessionAudit.RecordRevenueTaperFailure("town basis", ex);
            }
        }

        public static void Postfix(
            Clan clan,
            Town town,
            bool applyWithdrawals,
            ref ExplainedNumber __result,
            int __state)
        {
            try
            {
                var settings = B1071_RevenueTaper.Settings;
                if (settings == null || !settings.EnableSettlementRevenueTuning) return;
                if (town == null) return;

                // Both the preview and the daily payout use the pre-withdrawal basis.
                int basis = __state;
                if (applyWithdrawals)
                {
                    B1071_SessionAudit.RecordTownTariffBasis(basis);
                }

                if (basis <= 0) return;

                B1071_RevenueTaper.Apply(
                    ref __result,
                    B1071_RevenueMath.TownTariffScale(
                        basis,
                        settings.SettlementTariffStrengthTown,
                        settings.SettlementTariffCurveTown,
                        settings.SettlementTariffKneeTown));
            }
            catch (Exception ex)
            {
                B1071_SessionAudit.RecordRevenueTaperFailure("town tariff", ex);
                B1071_VerboseLog.Log("SettlementRevenue", $"town tariff taper error: {ex}");
            }
        }
    }

    /// <summary>
    /// Postfix on village income. A village's pool is a different quantity from a town's and
    /// roughly a fifth the size, so it carries its own strength, curve and reference.
    /// </summary>
    [HarmonyPatch(typeof(DefaultClanFinanceModel), nameof(DefaultClanFinanceModel.CalculateVillageIncome))]
    internal static class B1071_SettlementVillageTariffTuningPatch
    {
        // Capture before vanilla drains the pool; a postfix alone cannot recover that basis.
        // This prefix only observes state and always allows the original method to run.
        public static void Prefix(DefaultClanFinanceModel __instance, Village village, out int __state)
        {
            __state = 0;
            try
            {
                var settings = B1071_RevenueTaper.Settings;
                if (settings == null || !settings.EnableSettlementRevenueTuning || village == null) return;
                __state = (int)((float)village.TradeTaxAccumulated / __instance.RevenueSmoothenFraction());
            }
            catch (Exception ex)
            {
                B1071_SessionAudit.RecordRevenueTaperFailure("village basis", ex);
            }
        }

        public static void Postfix(
            Clan clan,
            Village village,
            bool applyWithdrawals,
            ref int __result,
            int __state)
        {
            try
            {
                var settings = B1071_RevenueTaper.Settings;
                if (settings == null || !settings.EnableSettlementRevenueTuning) return;
                if (village == null) return;

                int basis = __state;
                if (applyWithdrawals)
                {
                    B1071_SessionAudit.RecordVillageTariffBasis(basis);
                }

                // A single denar cannot be tapered in an integer currency, so it is left alone
                // rather than being zeroed by truncation. Above that, progress is guaranteed: a
                // value that would truncate to nothing lands on one denar instead. This is tested
                // AFTER the basis is recorded, because a village too small to taper is exactly the
                // kind of village the reference is calibrated against.
                if (basis <= 0 || __result <= 1) return;

                float scale = B1071_RevenueMath.VillageTariffScale(
                    basis,
                    settings.SettlementTariffStrengthVillage,
                    settings.SettlementTariffCurveVillage,
                    settings.SettlementTariffKneeVillage);
                if (scale >= 1f) return;

                __result = Math.Max(1, (int)(__result * scale));
            }
            catch (Exception ex)
            {
                B1071_SessionAudit.RecordRevenueTaperFailure("village tariff", ex);
                B1071_VerboseLog.Log("SettlementRevenue", $"village tariff taper error: {ex}");
            }
        }
    }

    /// <summary>
    /// Postfix on town tax, the largest of the three lines. The original clamps its result to
    /// [0, float.MaxValue] before returning, so the guard inside Apply also covers the
    /// low-loyalty case where vanilla zeroes the tax outright.
    /// </summary>
    [HarmonyPatch(typeof(DefaultSettlementTaxModel), nameof(DefaultSettlementTaxModel.CalculateTownTax))]
    internal static class B1071_SettlementTaxTuningPatch
    {
        public static void Postfix(Town town, bool includeDescriptions, ref ExplainedNumber __result)
        {
            try
            {
                var settings = B1071_RevenueTaper.Settings;
                if (settings == null || !settings.EnableSettlementRevenueTuning) return;
                if (town == null) return;

                B1071_RevenueTaper.Apply(
                    ref __result,
                    B1071_RevenueMath.TownTaxScale(
                        town.Prosperity,
                        settings.SettlementTaxStrength,
                        settings.SettlementTaxCurve,
                        settings.SettlementTaxKnee));
            }
            catch (Exception ex)
            {
                B1071_SessionAudit.RecordRevenueTaperFailure("town tax", ex);
                B1071_VerboseLog.Log("SettlementRevenue", $"town tax taper error: {ex}");
            }
        }
    }
}
