using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Recruitment;
using TaleWorlds.Core.ViewModelCollection.Generic;
using TaleWorlds.Localization;
using TaleWorlds.Core.ViewModelCollection.Information;
using Byzantium1071.Campaign.Behaviors;


namespace Byzantium1071.Campaign.Patches
{
    // Minimal “real UI”: we append manpower line into existing icon hints.
    // RecruitVolunteerTroopVM exposes TierIconData / TypeIconData (StringItemWithHintVM).
    [HarmonyPatch(typeof(RecruitVolunteerTroopVM), "ExecuteBeginHint")]
    internal static class B1071_RecruitVolunteerTroopVM_ExecuteBeginHint_Patch
    {
        private const string Marker = "Manpower:";

        static void Prefix(RecruitVolunteerTroopVM __instance)
        {
            if (__instance == null) return;

            var behavior = B1071_ManpowerBehavior.Instance;
            if (behavior == null) return;

            Settlement? s = Hero.MainHero?.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement;
            if (s == null) return;

            string line = behavior.GetManpowerUiLine(s);
            if (string.IsNullOrEmpty(line)) return;

            AppendHint(__instance.TierIconData, line);
            AppendHint(__instance.TypeIconData, line);
        }

        private static void AppendHint(StringItemWithHintVM? vm, string manpowerLine)
        {
            if (vm == null) return;

            string existing = vm.Hint?.HintText?.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(existing) && existing.Contains(Marker))
                return;

            string combined = string.IsNullOrEmpty(existing)
                ? manpowerLine
                : (existing + "\n" + manpowerLine);

            vm.Hint = new HintViewModel(new TextObject(combined));
        }

    }
}
