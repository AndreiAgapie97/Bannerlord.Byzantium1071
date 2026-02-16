using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Recruitment;
using TaleWorlds.Core;

namespace Byzantium1071.Campaign.Patches
{
    [HarmonyPatch]
    internal static class B1071_RecruitmentManpowerHintPatch
    {
        private static Settlement? GetPlayerSettlement()
        {
            return Hero.MainHero?.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(RecruitVolunteerTroopVM), nameof(RecruitVolunteerTroopVM.ExecuteBeginHint))]
        private static void Post_ExecuteBeginHint()
        {
            try
            {
                var s = GetPlayerSettlement();
                if (s == null || s.IsHideout) return;

                var behavior = Byzantium1071.Campaign.Behaviors.B1071_ManpowerBehavior.Instance;
                if (behavior == null) return;

                // Visibil pe ecran (hint UI), fără XAML.
                MBInformationManager.ShowHint(behavior.GetManpowerUiLine(s));
            }
            catch
            {
                // silent: nu vrem crash în UI
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(RecruitVolunteerTroopVM), nameof(RecruitVolunteerTroopVM.ExecuteEndHint))]
        private static void Pre_ExecuteEndHint()
        {
            try
            {
                MBInformationManager.HideInformations();
            }
            catch
            {
            }
        }
    }
}
