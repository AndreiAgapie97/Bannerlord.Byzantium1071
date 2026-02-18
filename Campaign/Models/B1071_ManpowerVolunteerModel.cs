using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using Byzantium1071.Campaign.Behaviors;


namespace Byzantium1071.Campaign.Models
{
    public sealed class B1071_ManpowerVolunteerModel : DefaultVolunteerModel
    {
        private static B1071_ManpowerBehavior? GetMP()
            => global::TaleWorlds.CampaignSystem.Campaign.Current?.GetCampaignBehavior<B1071_ManpowerBehavior>();

        public override float GetDailyVolunteerProductionProbability(Hero hero, int index, Settlement settlement)
        {
            float baseProb = base.GetDailyVolunteerProductionProbability(hero, index, settlement);

            var mp = GetMP();
            if (mp == null) return baseProb;

            float ratio = mp.GetManpowerRatio(settlement);
            // Low manpower → fewer volunteers appear. Zero manpower → none.
            return baseProb * ratio;
        }
    }
}
