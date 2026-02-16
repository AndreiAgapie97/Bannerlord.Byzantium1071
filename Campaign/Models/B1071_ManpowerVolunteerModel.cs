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


        private static Settlement? ResolveRecruitmentSettlement(Hero? buyer, Hero? seller, Settlement? provided = null)
        {
            return provided
                   ?? buyer?.CurrentSettlement
                   ?? buyer?.StayingInSettlement
                   ?? seller?.CurrentSettlement
                   ?? seller?.StayingInSettlement
                   ?? seller?.HomeSettlement;
        }

        public override float GetDailyVolunteerProductionProbability(Hero hero, int index, Settlement settlement)
        {
            float baseProb = base.GetDailyVolunteerProductionProbability(hero, index, settlement);

            var mp = GetMP();
            if (mp == null) return baseProb;

            float ratio = mp.GetManpowerRatio(settlement);
            // Dacă manpower e 0 -> nu mai apar voluntari noi.
            // Dacă e mic -> apar rar.
            return baseProb * ratio;
        }

        public override int MaximumIndexHeroCanRecruitFromHero(Hero buyerHero, Hero sellerHero, int useValueAsRelation = -101)
        {
            int baseMax = base.MaximumIndexHeroCanRecruitFromHero(buyerHero, sellerHero, useValueAsRelation);
            if (baseMax < 0) return baseMax;

            var mp = GetMP();
            if (mp == null) return baseMax;

            Settlement? s = ResolveRecruitmentSettlement(buyerHero, sellerHero);
            if (s == null) return baseMax;

            float ratio = mp.GetManpowerRatio(s);
            if (ratio <= 0.01f) return -1; // nimic recruitabil

            // Slot cap by manpower ratio (simplu, dar “feels right”):
            int cap =
                ratio < 0.25f ? 0 :
                ratio < 0.50f ? 1 :
                ratio < 0.75f ? 2 :
                baseMax;

            return Math.Min(baseMax, cap);
        }

        public override int MaximumIndexGarrisonCanRecruitFromHero(Settlement settlement, Hero sellerHero)
        {
            int baseMax = base.MaximumIndexGarrisonCanRecruitFromHero(settlement, sellerHero);
            if (baseMax < 0) return baseMax;

            var mp = GetMP();
            if (mp == null) return baseMax;

            float ratio = mp.GetManpowerRatio(settlement);
            if (ratio <= 0.01f) return -1;

            int cap =
                ratio < 0.25f ? 0 :
                ratio < 0.50f ? 1 :
                ratio < 0.75f ? 2 :
                baseMax;

            return Math.Min(baseMax, cap);
        }
    }
}
