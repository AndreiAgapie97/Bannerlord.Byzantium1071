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
            var mp = Behaviors.B1071_ManpowerBehavior.Instance;
            Settlement? settlement = town?.Settlement;
            if (mp == null || settlement == null) return;

            mp.GetManpowerPool(settlement, out int cur, out _, out _);
            if (cur <= 0)
            {
                __result = 0;
            }
        }
    }
}
