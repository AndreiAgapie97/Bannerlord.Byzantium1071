using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using static TaleWorlds.CampaignSystem.ComponentInterfaces.SettlementAccessModel;

namespace Byzantium1071.Campaign.Patches
{
    /// <summary>
    /// Relaxes vanilla castle access restrictions so the player can interact with
    /// the castle recruitment system (prisoner deposit, elite pool, recruit UI)
    /// from early game onward.
    ///
    /// VANILLA RESTRICTIONS (decompiled from DefaultSettlementAccessModel v1.3.15):
    ///
    /// 1. CanMainHeroEnterCastle — SETTLEMENT ENTRY:
    ///    - Neutral + owner relation &lt; 0  → NoAccess (completely blocked)
    ///    - Neutral + crime rating         → NoAccess
    ///    - At war                          → NoAccess
    ///
    /// 2. CanMainHeroEnterKeepInternal — LORD'S HALL (keep/dungeon):
    ///    - Neutral + clan tier &lt; 3       → LimitedAccess (bribe, ~800 denars)
    ///    - Neutral + crime rating         → LimitedAccess (bribe)
    ///    - Neutral + disguised            → LimitedAccess (disguise)
    ///
    /// WHY THIS MATTERS:
    /// The player needs castle entry to auto-deposit prisoners (fires on settlement enter
    /// via <see cref="B1071_CastlePrisonerDepositPatch"/>), and lord's hall access to use
    /// the castle recruitment overlay (elite pool + converted prisoner lists).
    /// A new player (clan tier 0-2, no relations) is locked out of most castles entirely.
    ///
    /// WHAT WE PATCH:
    /// Postfixes on the two public entry-point methods. For non-hostile castles only:
    ///   - Settlement entry: upgrades NoAccess → FullAccess when blocked only by low
    ///     owner relation (not crime, not war).
    ///   - Lord's hall: upgrades LimitedAccess → FullAccess when blocked only by low
    ///     clan tier (waives the bribe). Crime and disguise blocks are preserved.
    ///
    /// Hostile castles (at war) are never touched — vanilla restrictions remain.
    /// Controlled by MCM toggle <see cref="Settings.B1071_McmSettings.CastleOpenAccess"/>.
    /// </summary>

    /// <summary>
    /// Postfix on <c>CanMainHeroEnterSettlement</c>.
    /// If the settlement is a castle and access was denied due to low owner relation
    /// (neutral faction, relation &lt; 0), upgrades to FullAccess so the player can
    /// enter and auto-deposit prisoners.
    /// </summary>
    [HarmonyPatch(typeof(DefaultSettlementAccessModel), "CanMainHeroEnterSettlement")]
    public static class B1071_CastleAccessPatch_Settlement
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        static void Postfix(Settlement settlement, ref AccessDetails accessDetails)
        {
            if (settlement == null || !settlement.IsCastle) return;

            var settings = Settings.B1071_McmSettings.Instance ?? Settings.B1071_McmSettings.Defaults;
            if (!settings.CastleOpenAccess) return;

            // Only relax the "owner relation < 0" block for neutral factions.
            // Crime rating and hostile faction blocks are preserved (vanilla).
            if (accessDetails.AccessLevel == AccessLevel.NoAccess
                && accessDetails.AccessLimitationReason == AccessLimitationReason.RelationshipWithOwner
                && !FactionManager.IsAtWarAgainstFaction(Hero.MainHero.MapFaction, settlement.MapFaction))
            {
                accessDetails.AccessLevel = AccessLevel.FullAccess;
                accessDetails.AccessMethod = AccessMethod.ByRequest;
                accessDetails.AccessLimitationReason = AccessLimitationReason.None;
            }
        }
    }

    /// <summary>
    /// Postfix on <c>CanMainHeroEnterLordsHall</c>.
    /// If the settlement is a castle and the lord's hall requires a bribe due to low
    /// clan tier (tier &lt; 3, neutral faction), waives the bribe by upgrading to
    /// FullAccess. Crime- and disguise-based restrictions are preserved.
    /// </summary>
    [HarmonyPatch(typeof(DefaultSettlementAccessModel), "CanMainHeroEnterLordsHall")]
    public static class B1071_CastleAccessPatch_LordsHall
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        static void Postfix(Settlement settlement, ref AccessDetails accessDetails)
        {
            if (settlement == null || !settlement.IsCastle) return;

            var settings = Settings.B1071_McmSettings.Instance ?? Settings.B1071_McmSettings.Defaults;
            if (!settings.CastleOpenAccess) return;

            // Only waive the clan-tier bribe for neutral factions.
            // Crime-based bribe and hostile disguise blocks remain untouched.
            if (accessDetails.AccessLevel == AccessLevel.LimitedAccess
                && accessDetails.LimitedAccessSolution == LimitedAccessSolution.Bribe
                && accessDetails.AccessLimitationReason == AccessLimitationReason.ClanTier
                && !FactionManager.IsAtWarAgainstFaction(Hero.MainHero.MapFaction, settlement.MapFaction))
            {
                accessDetails.AccessLevel = AccessLevel.FullAccess;
                accessDetails.AccessMethod = AccessMethod.ByRequest;
                accessDetails.AccessLimitationReason = AccessLimitationReason.None;
                accessDetails.LimitedAccessSolution = LimitedAccessSolution.None;
            }
        }
    }
}
