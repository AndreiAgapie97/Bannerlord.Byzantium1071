using System.Collections.Generic;
using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.UI
{
    /// <summary>
    /// The Campaign++ settlement rows, shared by both surfaces that show them: the nameplate
    /// hover tooltip (B1071_SettlementNameplateVMMixin) and the section added to the vanilla
    /// settlement tooltip (B1071_SettlementTooltipRefresher).
    ///
    /// One builder is what stops the two from drifting into reporting different numbers for the
    /// same settlement — they are shown side by side on the campaign map, so any disagreement
    /// between them reads as a bug.
    /// </summary>
    public static class B1071_SettlementTooltipContent
    {
        private static IB1071Settings Settings =>
            B1071_TestHooks.Settings ?? B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        /// <summary>
        /// Builds the manpower and devastation rows for a settlement, or an empty list when
        /// there is nothing to report. Callers gate on EnableSettlementNameplateTooltips before
        /// calling; the per-system toggles are checked here.
        /// </summary>
        public static List<TooltipProperty> Build(Settlement? settlement)
        {
            List<TooltipProperty> properties = new();
            if (settlement == null)
                return properties;

            B1071_ManpowerBehavior? manpower = B1071_ManpowerBehavior.Instance;
            if (manpower != null)
            {
                manpower.GetManpowerPool(settlement, out int current, out int maximum, out Settlement pool);
                if (pool != null)
                {
                    string manpowerLabel;
                    if (settlement.IsVillage)
                    {
                        manpowerLabel = new TextObject("{=b1071_nameplate_regional_manpower}Regional manpower ({POOL})")
                            .SetTextVariable("POOL", pool.Name)
                            .ToString();
                    }
                    else
                    {
                        manpowerLabel = new TextObject("{=b1071_nameplate_manpower}Manpower").ToString();
                    }

                    string manpowerValue = $"{current}/{maximum}";
                    if (current >= maximum)
                    {
                        manpowerValue += $" ({new TextObject("{=b1071_nameplate_full}Full").ToString()})";
                    }

                    // textHeight stays 0 on every row here. TooltipPropertyWidget only lays a
                    // row out as two aligned columns when the definition and value are both
                    // non-empty AND TextHeight == 0; any other height drops it into the
                    // single-column path, where the label is right-aligned and the value
                    // left-aligned against it, so the two render touching.
                    properties.Add(new TooltipProperty(manpowerLabel, manpowerValue, 0));

                    if (settlement.IsTown && current < maximum)
                    {
                        int dailyRecovery = B1071_ManpowerBehavior.GetDailyRegen(pool, maximum);
                        string recoveryLabel = new TextObject("{=b1071_nameplate_daily_recovery}Daily recovery").ToString();
                        properties.Add(new TooltipProperty(recoveryLabel, $"+{dailyRecovery}/day", 0));
                    }
                }
            }

            if (Settings.EnableFrontierDevastation && B1071_DevastationBehavior.Instance != null)
            {
                string devastationLabel = new TextObject("{=b1071_nameplate_devastation}Devastation").ToString();
                properties.Add(new TooltipProperty(devastationLabel, $"{GetDevastation(settlement):0.#}/100", 0));
            }

            return properties;
        }

        /// <summary>
        /// A village reports its own devastation; a town or castle reports the average across its
        /// bound villages, which is the same figure the security and food penalties are drawn
        /// from. Anything else (hideout, retirement settlement) has none.
        /// </summary>
        public static float GetDevastation(Settlement settlement)
        {
            B1071_DevastationBehavior? devastation = B1071_DevastationBehavior.Instance;
            if (devastation == null)
                return 0f;

            if (settlement.IsVillage && settlement.Village != null)
                return devastation.GetDevastation(settlement.Village);

            if ((settlement.IsTown || settlement.IsCastle) && settlement.Town != null)
                return devastation.GetAverageBoundVillageDevastation(settlement.Town);

            return 0f;
        }
    }
}
