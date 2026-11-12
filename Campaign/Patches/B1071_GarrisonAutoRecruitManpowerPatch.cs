using System;
using Byzantium1071.Campaign.Settings;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.Patches
{
    [HarmonyPatch(
        typeof(DefaultSettlementGarrisonModel),
        nameof(DefaultSettlementGarrisonModel.GetMaximumDailyAutoRecruitmentCount),
        new[] { typeof(Town), typeof(bool) })]
    public static class B1071_GarrisonAutoRecruitManpowerPatch
    {
        static void Postfix(Town town, ref ExplainedNumber __result)
        {
            try
            {
                var mp = Behaviors.B1071_ManpowerBehavior.Instance;
                Settlement? settlement = town?.Settlement;
                if (mp == null || settlement == null) return;

                mp.GetManpowerPool(settlement, out int cur, out _, out _);
                int costPerTroop = Math.Max(1,
                    (B1071_TestHooks.Settings ?? B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults)
                        .BaseManpowerCostPerTroop);
                int manpowerCap = B1071_GarrisonRecruitmentMath.ManpowerLimitedCount(cur, costPerTroop);
                float original = __result.ResultNumber;
                __result.LimitMax(
                    manpowerCap,
                    new TextObject("{=b1071_garrison_manpower_limit}Campaign++ manpower"));

                if (__result.ResultNumber < original)
                    B1071_VerboseLog.Log("Garrison", $"Auto-recruit capped at {settlement.Name}: {original:0.##}->{__result.ResultNumber:0.##} (manpower={cur}, cost={costPerTroop}).");
            }
            catch (Exception ex) { TaleWorlds.Library.Debug.Print($"[Byzantium1071] GarrisonAutoRecruitPatch error: {ex}"); }
        }
    }

    /// <summary>
    /// Bannerlord v1.5.2 adds garrison volunteers directly to the roster and does not
    /// dispatch the normal troop-recruited event. Charge the positive roster delta here;
    /// Campaign++ prisoner absorption is a separate method and remains manpower-free.
    /// </summary>
    [HarmonyPatch(
        typeof(GarrisonRecruitmentCampaignBehavior),
        "TickAutoRecruitmentGarrisonChange",
        new[] { typeof(Town) })]
    internal static class B1071_GarrisonAutoRecruitManpowerConsumptionPatch
    {
        private static void Prefix(Town town, out int __state)
        {
            try
            {
                __state = town?.GarrisonParty?.Party.NumberOfAllMembers ?? 0;
            }
            catch (Exception ex)
            {
                __state = -1;
                TaleWorlds.Library.Debug.Print($"[Byzantium1071] GarrisonAutoRecruit consumption snapshot error: {ex}");
            }
        }

        private static void Postfix(Town town, int __state)
        {
            try
            {
                if (__state < 0) return;

                Settlement? settlement = town?.Settlement;
                var manpower = Behaviors.B1071_ManpowerBehavior.Instance;
                if (settlement == null || manpower == null) return;

                int after = town?.GarrisonParty?.Party.NumberOfAllMembers ?? 0;
                int costPerTroop = Math.Max(1,
                    (B1071_TestHooks.Settings ?? B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults)
                        .BaseManpowerCostPerTroop);
                int manpowerCost = B1071_GarrisonRecruitmentMath.ManpowerCostForRosterGrowth(
                    __state,
                    after,
                    costPerTroop);
                if (manpowerCost <= 0) return;

                manpower.ConsumeManpowerFlat(settlement, manpowerCost);
                B1071_VerboseLog.Log(
                    "Garrison",
                    $"Native volunteer recruitment consumed {manpowerCost} manpower at {settlement.Name} for {after - __state} recruit(s).");
            }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print($"[Byzantium1071] GarrisonAutoRecruit consumption error: {ex}");
            }
        }
    }
}
