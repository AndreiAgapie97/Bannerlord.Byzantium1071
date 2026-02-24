using Byzantium1071.Campaign.Settings;
using HarmonyLib;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.Patches
{
    /// <summary>
    /// Adds a "Frontier Revenue" income line to minor faction clans to prevent
    /// bankruptcy under the mod's tier-exponential wage system.
    ///
    /// ──────────────────────────────────────────────────────────────────────
    /// PROBLEM
    /// ──────────────────────────────────────────────────────────────────────
    ///
    /// Minor factions have no settlement income — their only revenue is:
    ///   • Mercenary pay (if under merc service): Influence × MercenaryAwardMultiplier
    ///   • Base income (landless): Tier × 80 (vassal) or Tier × 40 (mercenary)
    ///   • Party trade gold above 10,000 threshold
    ///   • Kingdom budget support (if gold &lt; 30,000 and not mercenary)
    ///
    /// With the B1071_ArmyEconomicsPatch active (Moderate preset), a Tier 4
    /// minor faction with 50 average-T3 troops pays ~400d/day in wages but
    /// earns only ~160d/day (merc) or ~320d/day (unaligned). This forces
    /// wage restriction at gold &lt; 10,000 → troop dismissal → combat
    /// irrelevance. In playtesting, minor factions are nearly bankrupt.
    ///
    /// ──────────────────────────────────────────────────────────────────────
    /// FIX
    /// ──────────────────────────────────────────────────────────────────────
    ///
    /// Postfix on DefaultClanFinanceModel.CalculateClanIncomeInternal adds a
    /// flat tier-scaled daily income representing frontier revenue: raiding
    /// income, toll collection, protection fees, and tribute extraction —
    /// historically authentic sources for minor warbands in the 1071 context.
    ///
    /// Mercenary clans (already receiving merc pay) get a lower stipend.
    /// Unaligned minor clans get a higher stipend (they have no kingdom backing).
    /// Bandit factions are excluded (they skip DailyTickClan entirely).
    ///
    /// MCM: "Minor Faction Economy" group with master toggle and per-tier amounts.
    /// Verified against v1.3.15.
    /// </summary>
    [HarmonyPatch(typeof(DefaultClanFinanceModel), "CalculateClanIncomeInternal")]
    public static class B1071_MinorFactionIncomePatch
    {
        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        private static readonly TextObject _label = new TextObject("{=b1071_frontier_rev}Frontier Revenue");

        public static void Postfix(Clan clan, ref ExplainedNumber goldChange, bool applyWithdrawals)
        {
            try
            {
                if (!Settings.EnableMinorFactionIncome) return;
                if (clan == null || !clan.IsMinorFaction || clan.IsBanditFaction) return;
                if (clan == Clan.PlayerClan) return; // Player already has settlement income; this is for AI only.

                int tier = Math.Max(1, clan.Tier);

                int stipend;
                if (clan.IsUnderMercenaryService)
                    stipend = tier * Settings.MinorFactionMercenaryStipendPerTier;
                else
                    stipend = tier * Settings.MinorFactionUnalignedStipendPerTier;

                if (stipend > 0)
                    goldChange.Add(stipend, _label);
            }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print($"[Byzantium1071] MinorFactionIncomePatch error: {ex}");
            }
        }
    }
}
