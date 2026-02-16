using System;
using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.Models
{
    /// <summary>
    /// Links militia growth to manpower ratio.
    /// Depleted pools produce less militia; empty pools produce none.
    /// </summary>
    public sealed class B1071_ManpowerMilitiaModel : DefaultSettlementMilitiaModel
    {
        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        public override ExplainedNumber CalculateMilitiaChange(Settlement settlement, bool includeDescriptions = false)
        {
            ExplainedNumber result = base.CalculateMilitiaChange(settlement, includeDescriptions);

            if (!Settings.EnableMilitiaLink) return result;

            B1071_ManpowerBehavior? mp = B1071_ManpowerBehavior.Instance;
            if (mp == null) return result;

            float ratio = mp.GetManpowerRatio(settlement);

            // Scale militia growth by manpower ratio.
            // At 0% manpower → militia grows at MilitiaManpowerMinScale.
            // At 100% manpower → militia grows at MilitiaManpowerMaxScale.
            float minScale = Math.Max(0f, Settings.MilitiaManpowerMinScale) / 100f;
            float maxScale = Math.Max(minScale, Settings.MilitiaManpowerMaxScale / 100f);
            float scale = minScale + ((maxScale - minScale) * ratio);

            // AddFactor expects a factor to multiply by, so subtract 1 to convert from multiplier to factor.
            // e.g., scale=0.5 → factor=-0.5 → result is halved.
            float factor = scale - 1f;
            if (Math.Abs(factor) > 0.001f)
            {
                result.AddFactor(factor, new TextObject("{=B1071_Militia}Manpower availability"));
            }

            return result;
        }
    }
}
