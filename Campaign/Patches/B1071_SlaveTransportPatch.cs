using System;
using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.ViewModelCollection.Party;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.Patches
{
    internal static class B1071_SlaveTransport
    {
        internal static IB1071Settings Settings =>
            B1071_TestHooks.Settings ?? B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        internal static int Count(PartyBase? party) => Settings.EnableSlaveEconomy && party?.IsMobile == true
            ? B1071_SlaveEconomyBehavior.Instance?.GetSlaveCount(party.ItemRoster) ?? 0 : 0;

        internal static readonly TextObject CapacityLabel = new TextObject("{=b1071_slave_capacity}Capacity occupied by slaves");

        // Query the model, not the getter whose result reserves slave slots.
        internal static int TotalCapacity(PartyBase party) => Math.Max(0,
            (int)TaleWorlds.CampaignSystem.Campaign.Current.Models.PartySizeLimitModel.GetPartyPrisonerSizeLimit(party).ResultNumber);
    }

    [HarmonyPatch(typeof(DefaultMobilePartyFoodConsumptionModel), nameof(DefaultMobilePartyFoodConsumptionModel.CalculateDailyBaseFoodConsumptionf))]
    internal static class B1071_SlavePartyFoodPatch
    {
        private static readonly TextObject FoodLabel = new TextObject("{=b1071_slave_food}Slave Upkeep");
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(DefaultMobilePartyFoodConsumptionModel __instance, MobileParty party, ref ExplainedNumber __result)
        {
            try
            {
                int count = B1071_SlaveTransport.Count(party?.Party);
                if (count == 0 || !__instance.DoesPartyConsumeFood(party)) return;
                // Add to the ration basis so native perks and War Sails' later
                // modifiers retain their original additive-factor semantics.
                __result.Add(-B1071_SlaveMath.FoodConsumption(count, B1071_SlaveTransport.Settings), FoodLabel);
            }
            catch (Exception ex) { B1071_VerboseLog.Log("SlavePartyFood", ex.ToString()); }
        }
    }

    // Vanilla's cache tracks only the prison roster. Keep using the live model
    // at zero slaves too, so removing the last slave cannot restore a stale limit.
    [HarmonyPatch(typeof(PartyBase), nameof(PartyBase.PrisonerSizeLimit), MethodType.Getter)]
    internal static class B1071_SlavePrisonerCapacityPatch
    {
        public static void Postfix(PartyBase __instance, ref int __result)
        {
            try
            {
                if (!B1071_SlaveTransport.Settings.EnableSlaveEconomy || __instance?.IsMobile != true
                    || B1071_SlaveEconomyBehavior.Instance == null) return;
                int count = B1071_SlaveTransport.Count(__instance);
                __result = Math.Max(0, B1071_SlaveTransport.TotalCapacity(__instance) - count);
            }
            catch (Exception ex) { B1071_VerboseLog.Log("SlaveCapacity", ex.ToString()); }
        }
    }

    // Native screen logic snapshots this limit when opened. Normal troop transfers
    // edit the owner's roster in place, so use its current limit for labels, transfer
    // limits and warnings. Keep supplied limits for detached/quest preview rosters.
    [HarmonyPatch(typeof(PartyScreenLogic), nameof(PartyScreenLogic.RightPartyPrisonersSizeLimit), MethodType.Getter)]
    internal static class B1071_SlavePartyScreenCapacityPatch
    {
        public static void Postfix(PartyScreenLogic __instance, ref int __result)
        {
            try
            {
                if (!B1071_SlaveTransport.Settings.EnableSlaveEconomy) return;
                var party = __instance.RightOwnerParty;
                if (party?.MobileParty?.IsMainParty != true || __instance.MemberRosters == null
                    || !ReferenceEquals(__instance.MemberRosters[1], party.MemberRoster)) return;
                __result = party.PrisonerSizeLimit;
            }
            catch (Exception ex) { B1071_VerboseLog.Log("SlaveScreenCapacity", ex.ToString()); }
        }
    }

    // The native comparison cannot detect slave-only overload: both the regular
    // prisoner count and its clamped remaining limit can be zero. Update through
    // the setter so Gauntlet receives the corrected value, not just a getter override.
    // Verified in 1.5.3: this setter has no game-dependent static initialization.
    [HarmonyPatch(typeof(PartyVM), nameof(PartyVM.IsMainPrisonersLimitWarningEnabled), MethodType.Setter)]
    internal static class B1071_SlavePrisonerWarningPatch
    {
        public static void Postfix(PartyVM __instance)
        {
            try
            {
                if (__instance.IsMainPrisonersLimitWarningEnabled || !__instance.ArePrisonersRelevantOnCurrentMode) return;
                var party = __instance.PartyScreenLogic?.RightOwnerParty;
                if (party?.MobileParty?.IsMainParty != true) return;
                int count = B1071_SlaveTransport.Count(party);
                if (count > 0 && count > B1071_SlaveTransport.TotalCapacity(party))
                    __instance.IsMainPrisonersLimitWarningEnabled = true;
            }
            catch (Exception ex) { B1071_VerboseLog.Log("SlaveCapacityWarning", ex.ToString()); }
        }
    }

    [HarmonyPatch(typeof(PartyBase), nameof(PartyBase.PrisonerSizeLimitExplainer), MethodType.Getter)]
    internal static class B1071_SlavePrisonerCapacityExplanationPatch
    {
        public static void Postfix(PartyBase __instance, ref ExplainedNumber __result)
        {
            try
            {
                int count = B1071_SlaveTransport.Count(__instance);
                if (count == 0) return;
                var adjusted = new ExplainedNumber(0f, __result.IncludeDescriptions);
                adjusted.AddFromExplainedNumber(__result, null);
                adjusted.Add(-count, B1071_SlaveTransport.CapacityLabel);
                adjusted.LimitMin(0f);
                __result = adjusted;
            }
            catch (Exception ex) { B1071_VerboseLog.Log("SlaveCapacity", ex.ToString()); }
        }
    }

    [HarmonyPatch(typeof(DefaultPartySpeedCalculatingModel), nameof(DefaultPartySpeedCalculatingModel.CalculateBaseSpeed))]
    internal static class B1071_SlaveEscortSpeedPatch
    {
        private static readonly TextObject EscortLabel = new TextObject("{=b1071_slave_escort}Slave escort");
        private static readonly TextObject SharedCapacityLabel = new TextObject("{=b1071_slave_shared_capacity}Shared captive capacity adjustment");

        public static void Postfix(MobileParty mobileParty, int additionalTroopOnFootCount,
            int additionalTroopOnHorseCount, ref ExplainedNumber __result)
        {
            try
            {
                if (!B1071_SlaveTransport.Settings.EnableSlaveEconomy || mobileParty == null || mobileParty.IsCurrentlyAtSea) return;
                int slaves = B1071_SlaveTransport.Count(mobileParty.Party);
                int ownSlaves = slaves;
                int men = mobileParty.MemberRoster.TotalManCount + additionalTroopOnFootCount + additionalTroopOnHorseCount;
                int prisoners = mobileParty.PrisonRoster.TotalManCount;
                foreach (var attached in mobileParty.AttachedParties)
                {
                    slaves += B1071_SlaveTransport.Count(attached.Party);
                    men += attached.MemberRoster.TotalManCount;
                    prisoners += attached.PrisonRoster.TotalManCount;
                }
                if (slaves == 0) return;
                // Cargo weight remains native. Add only the incremental human escort burden.
                int nativeEscorted = mobileParty.IsCaravan ? 0 : prisoners;
                __result.AddFactor(B1071_SlaveMath.EscortFactor(men, nativeEscorted + slaves)
                    - B1071_SlaveMath.EscortFactor(men, nativeEscorted), EscortLabel);
                if (ownSlaves == 0) return;
                int ownPrisoners = mobileParty.PrisonRoster.TotalManCount;
                int totalCapacity = B1071_SlaveTransport.TotalCapacity(mobileParty.Party);
                float nativeOver = mobileParty.IsCaravan ? 0f : B1071_SlaveMath.OverCapacityFactor(
                    ownPrisoners, mobileParty.Party.PrisonerSizeLimit);
                // Native already uses the reduced regular-prisoner limit. Replace that
                // contribution with one combined ratio, including slave-only inventories.
                float sharedOver = B1071_SlaveMath.OverCapacityFactor(ownPrisoners + ownSlaves, totalCapacity);
                __result.AddFactor(sharedOver - nativeOver, SharedCapacityLabel);
            }
            catch (Exception ex) { B1071_VerboseLog.Log("SlaveEscort", ex.ToString()); }
        }
    }

    // Private target verified against Bannerlord 1.5.3. Vanilla samples only
    // PrisonRoster; while slave goods are carried, sample the combined captive pool.
    [HarmonyPatch(typeof(PrisonerReleaseCampaignBehavior), "HourlyPartyTick")]
    internal static class B1071_SlaveSharedEscapePatch
    {
        public static bool Prefix(MobileParty mobileParty)
        {
            bool handling = false;
            try
            {
                var behavior = B1071_SlaveEconomyBehavior.Instance;
                if (behavior == null || mobileParty == null || B1071_SlaveTransport.Count(mobileParty.Party) == 0) return true;
                if (mobileParty.MapEvent != null || mobileParty.SiegeEvent != null || mobileParty.IsGarrison || mobileParty.IsMilitia) return false;
                int capacity = B1071_SlaveTransport.TotalCapacity(mobileParty.Party);
                if (mobileParty.PrisonRoster.TotalManCount + behavior.GetSlaveCount(mobileParty.ItemRoster) <= capacity) return false;
                // Same hourly attempt chance and modifiers as vanilla's
                // ApplyEscapeChanceToExceededPrisoners (1.5.3).
                var chance = new ExplainedNumber(0.1f);
                if (mobileParty.HasPerk(DefaultPerks.Athletics.Stamina, out _, checkSecondaryRole: true))
                    chance.AddFactor(-0.1f, DefaultPerks.Athletics.Stamina.Name);
                if (mobileParty.LeaderHero != null)
                    TraitEffectHelper.ApplyTraitEffect(mobileParty.LeaderHero,
                        DefaultPersonalityTraitEffects.ValorPrisonerEscapeEffect, ref chance);
                handling = true;
                behavior.ApplySharedCaptiveEscapes(mobileParty, capacity, chance.ResultNumber);
                return false;
            }
            catch (Exception ex)
            {
                B1071_VerboseLog.Log("SlaveEscape", ex.ToString());
                // Never run a second escape pass after partial roster mutation.
                return !handling;
            }
        }
    }
}
