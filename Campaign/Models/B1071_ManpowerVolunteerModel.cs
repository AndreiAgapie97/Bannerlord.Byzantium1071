using System;
using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;


namespace Byzantium1071.Campaign.Models
{
    public sealed class B1071_ManpowerVolunteerModel : DefaultVolunteerModel
    {
        private static B1071_ManpowerBehavior? GetMP()
            => B1071_ManpowerBehavior.Instance;

        private static B1071_McmSettings Settings
            => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        public override float GetDailyVolunteerProductionProbability(Hero hero, int index, Settlement settlement)
        {
            float baseProb = base.GetDailyVolunteerProductionProbability(hero, index, settlement);

            var mp = GetMP();
            if (mp == null) return baseProb;

            float ratio = mp.GetManpowerRatio(settlement);
            // Low manpower → fewer volunteers appear. Zero manpower → none.
            float result = baseProb * ratio;

            // WP4: bounded stochastic variance on volunteer production.
            if (Settings.EnableRecruitmentVariance && Settings.VolunteerVariancePercent > 0)
            {
                float spread = Math.Min(Settings.VolunteerVariancePercent, 100f) / 100f;
                // MBRandom.RandomFloatRanged gives a uniform value in [min, max).
                float factor = MBRandom.RandomFloatRanged(1f - spread, 1f + spread);
                result *= factor;
            }

            return Math.Max(0f, result);
        }
    }
}
