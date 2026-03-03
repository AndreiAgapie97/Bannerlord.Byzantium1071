using MCM.Abstractions.Base.Global;
using MCM.Abstractions.FluentBuilder;
using MCM.Common;
using System;

namespace Byzantium1071.Campaign.Settings
{
    /// <summary>
    /// Manages the "Campaign++ - Quick Settings" tab in the MCM mod options menu.
    /// Mirrors the master enable/disable bool for every major system so players can
    /// toggle systems on/off from one screen without scrolling through 250+ settings.
    ///
    /// All toggles are backed by ProxyRef wrappers around B1071_McmSettings.Instance
    /// properties — changes here are identical to changes made in the full settings tab.
    ///
    /// Lifecycle:
    ///   BuildAndRegister() — called at OnBeforeInitialModuleScreenSetAsRoot
    ///   Unregister()       — called at OnSubModuleUnloaded
    /// </summary>
    internal static class B1071_QuickSettingsFluentSettings
    {
        private static FluentGlobalSettings? _instance;

        // ── Public lifecycle ───────────────────────────────────────────────────────

        internal static void BuildAndRegister()
        {
            _instance?.Unregister();
            _instance = Build();
            _instance?.Register();
        }

        internal static void Unregister()
        {
            _instance?.Unregister();
            _instance = null;
        }

        // ── Builder ────────────────────────────────────────────────────────────────

        private static FluentGlobalSettings? Build()
        {
            try
            {
                ISettingsBuilder? builder = BaseSettingsBuilder
                    .Create("b1071_quick", "Campaign++ - Quick Settings")
                    ?.SetFolderName("Byzantium1071")
                    ?.SetFormat("json");

                if (builder == null) return null;

                var s = B1071_McmSettings.Instance;
                if (s == null) return null;

                // ── Core Systems ───────────────────────────────────────────────────
                builder = builder.CreateGroup("Core Systems", g =>
                {
                    g.SetGroupOrder(0);

                    AddToggle(g, "qs_war_effects", "War Effects",
                        "Battle casualties and siege aftermath drain settlement manpower pools. Raids deplete the bound settlement.",
                        0, () => s.EnableWarEffects, v => s.EnableWarEffects = v);

                    AddToggle(g, "qs_war_exhaustion", "War Exhaustion",
                        "Kingdoms accumulate exhaustion from battles, raids, and sieges. High exhaustion slows regen and pushes the AI toward peace.",
                        1, () => s.EnableWarExhaustion, v => s.EnableWarExhaustion = v);

                    AddToggle(g, "qs_diplomacy_pressure", "Diplomacy Pressure",
                        "War exhaustion influences AI peace/war voting. Exhausted kingdoms become more willing to accept peace.",
                        2, () => s.EnableExhaustionDiplomacyPressure, v => s.EnableExhaustionDiplomacyPressure = v);

                    AddToggle(g, "qs_forced_peace", "Forced Peace at Crisis",
                        "When exhaustion exceeds a critical threshold the kingdom is forced to make peace with one enemy.",
                        3, () => s.EnableForcedPeaceAtCrisis, v => s.EnableForcedPeaceAtCrisis = v);

                    AddToggle(g, "qs_delayed_recovery", "Delayed Recovery",
                        "Settlements suffer a regen penalty after raids, sieges, or conquest that decays over time.",
                        4, () => s.EnableDelayedRecovery, v => s.EnableDelayedRecovery = v);

                    AddToggle(g, "qs_militia_link", "Militia Link",
                        "Militia growth scales with the settlement's manpower ratio. Empty pools produce no militia.",
                        5, () => s.EnableMilitiaLink, v => s.EnableMilitiaLink = v);
                });

                // ── Economy & Investment ───────────────────────────────────────────
                builder = builder.CreateGroup("Economy & Investment", g =>
                {
                    g.SetGroupOrder(1);

                    AddToggle(g, "qs_slave_economy", "Slave Economy",
                        "Raid villages for slaves, sell them at town markets. Slaves boost town prosperity and construction.",
                        0, () => s.EnableSlaveEconomy, v => s.EnableSlaveEconomy = v);

                    AddToggle(g, "qs_village_investment", "Village Investment",
                        "Spend gold at friendly villages for hearth growth, notable relations, influence, and power.",
                        1, () => s.EnableVillageInvestment, v => s.EnableVillageInvestment = v);

                    AddToggle(g, "qs_town_investment", "Town Investment",
                        "Spend gold at friendly towns for prosperity growth, notable relations, influence, and power.",
                        2, () => s.EnableTownInvestment, v => s.EnableTownInvestment = v);

                    AddToggle(g, "qs_minor_faction", "Minor Faction Economy",
                        "Non-bandit minor factions receive a daily stipend so they don't go bankrupt.",
                        3, () => s.EnableMinorFactionIncome, v => s.EnableMinorFactionIncome = v);

                    AddToggle(g, "qs_garrison_wage", "Garrison Wage Discount",
                        "Garrison troops cost a reduced percentage of field wages (default 60%).",
                        4, () => s.EnableGarrisonWageDiscount, v => s.EnableGarrisonWageDiscount = v);
                });

                // ── Recruitment & Military ─────────────────────────────────────────
                builder = builder.CreateGroup("Recruitment & Military", g =>
                {
                    g.SetGroupOrder(2);

                    AddToggle(g, "qs_castle_recruitment", "Castle Recruitment",
                        "Castles generate elite troops from manpower and hold T4+ prisoners for conversion.",
                        0, () => s.EnableCastleRecruitment, v => s.EnableCastleRecruitment = v);

                    AddToggle(g, "qs_castle_access", "Open Castle Access",
                        "Removes the vanilla clan-tier bribe requirement for entering neutral castles.",
                        1, () => s.CastleOpenAccess, v => s.CastleOpenAccess = v);

                    AddToggle(g, "qs_combat_survivability", "Combat Tier Survivability",
                        "Higher-tier troops are more likely to be wounded instead of killed.",
                        2, () => s.EnableTierSurvivability, v => s.EnableTierSurvivability = v);

                    AddToggle(g, "qs_combat_armor", "Combat Tier Armor Simulation",
                        "Troop armor is factored into autoresolve battle outcomes.",
                        3, () => s.EnableTierArmorSimulation, v => s.EnableTierArmorSimulation = v);

                    AddToggle(g, "qs_clan_survival", "Clan Survival",
                        "Prevents clan annihilation when their kingdom is destroyed. Rescued clans seek mercenary service.",
                        4, () => s.EnableClanSurvival, v => s.EnableClanSurvival = v);
                });

                // ── Province & Governance ──────────────────────────────────────────
                builder = builder.CreateGroup("Province & Governance", g =>
                {
                    g.SetGroupOrder(3);

                    AddToggle(g, "qs_governance", "Governance Strain",
                        "Owning many distant settlements reduces loyalty, security, and prosperity of far-flung holdings.",
                        0, () => s.EnableGovernanceStrain, v => s.EnableGovernanceStrain = v);

                    AddToggle(g, "qs_devastation", "Frontier Devastation",
                        "Raids cause persistent hearth, prosperity, security, and food penalties that decay slowly.",
                        1, () => s.EnableFrontierDevastation, v => s.EnableFrontierDevastation = v);

                    AddToggle(g, "qs_castle_supply", "Castle Supply Chain",
                        "Castle regen above the local trickle is transferred from the nearest same-faction town, not created from nothing.",
                        2, () => s.EnableCastleSupplyChain, v => s.EnableCastleSupplyChain = v);
                });

                // ── Immersion & Modifiers ──────────────────────────────────────────
                builder = builder.CreateGroup("Immersion & Modifiers", g =>
                {
                    g.SetGroupOrder(4);

                    AddToggle(g, "qs_seasonal", "Seasonal Regen",
                        "Spring/summer boost manpower regen, winter reduces it.",
                        0, () => s.EnableSeasonalRegen, v => s.EnableSeasonalRegen = v);

                    AddToggle(g, "qs_peace_dividend", "Peace Dividend",
                        "Kingdoms at peace get a manpower regen multiplier bonus.",
                        1, () => s.EnablePeaceDividend, v => s.EnablePeaceDividend = v);

                    AddToggle(g, "qs_culture_disc", "Culture Discount",
                        "Recruiting from matching-culture settlements costs less manpower.",
                        2, () => s.EnableCultureDiscount, v => s.EnableCultureDiscount = v);

                    AddToggle(g, "qs_governor", "Governor Bonus",
                        "Governors boost regen (Steward) and max pool size (Leadership).",
                        3, () => s.EnableGovernorBonus, v => s.EnableGovernorBonus = v);

                    AddToggle(g, "qs_overlay", "Overlay (M key)",
                        "In-game intelligence panel showing manpower, wars, factions, and more. Press M to toggle.",
                        4, () => s.EnableOverlay, v => s.EnableOverlay = v);

                    AddToggle(g, "qs_alerts", "Manpower Alerts",
                        "Yellow warning messages when a settlement's manpower drops below 25%.",
                        5, () => s.EnableManpowerAlerts, v => s.EnableManpowerAlerts = v);
                });

                return builder.BuildAsGlobal();
            }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print(
                    $"[Byzantium1071] QuickSettingsFluentSettings.Build error: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        private static void AddToggle(
            MCM.Abstractions.FluentBuilder.ISettingsPropertyGroupBuilder g,
            string id, string name, string hint, int order,
            Func<bool> getter, Action<bool> setter)
        {
            g.AddBool(id, name,
                new ProxyRef<bool>(getter, setter),
                b =>
                {
                    b.SetOrder(order);
                    b.SetRequireRestart(false);
                    b.SetHintText(hint);
                });
        }
    }
}
