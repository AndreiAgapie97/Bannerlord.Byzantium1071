using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;

namespace Byzantium1071.Campaign.Patches
{
    [HarmonyPatch(typeof(DefaultSettlementGarrisonModel), nameof(DefaultSettlementGarrisonModel.GetMaximumDailyAutoRecruitmentCount))]
    public static class B1071_GarrisonAutoRecruitManpowerPatch
    {
        static void Postfix(Town town, ref int __result)
        {
            try
            {
                var mp = Behaviors.B1071_ManpowerBehavior.Instance;
                Settlement? settlement = town?.Settlement;
                if (mp == null || settlement == null) return;

                mp.GetManpowerPool(settlement, out int cur, out _, out _);
                int original = __result;
                if (cur <= 0)
                    __result = 0;
                else if (__result > cur)
                    __result = cur;

                if (__result < original)
                    B1071_VerboseLog.Log("Garrison", $"Auto-recruit capped at {settlement.Name}: {original}->{__result} (manpower={cur}).");
            }
            catch (Exception ex) { TaleWorlds.Library.Debug.Print($"[Byzantium1071] GarrisonAutoRecruitPatch error: {ex}"); }
        }
    }
}
