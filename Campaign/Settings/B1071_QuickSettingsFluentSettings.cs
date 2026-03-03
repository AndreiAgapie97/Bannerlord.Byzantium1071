using MCM.Abstractions.Base.Global;
using MCM.Abstractions.FluentBuilder;
using MCM.Common;
using System;
using TaleWorlds.Localization;

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
                    .Create("b1071_quick", L("b1071_qs_title", "Campaign++ - Quick Settings"))
                    ?.SetFolderName("Byzantium1071")
                    ?.SetFormat("json");

                if (builder == null) return null;

                var s = B1071_McmSettings.Instance;
                if (s == null) return null;

                // ── Core Systems ───────────────────────────────────────────────────
                builder = builder.CreateGroup(L("b1071_qs_group_core", "Core Systems"), g =>
                {
                    g.SetGroupOrder(0);

                    AddToggle(g, "qs_war_effects", L("b1071_qs_name_war_effects", "War Effects"),
                        L("b1071_qs_hint_war_effects", "Battle casualties and siege aftermath drain settlement manpower pools. Raids deplete the bound settlement."),
                        0, () => s.EnableWarEffects, v => s.EnableWarEffects = v);

                    AddToggle(g, "qs_war_exhaustion", L("b1071_qs_name_war_exhaustion", "War Exhaustion"),
                        L("b1071_qs_hint_war_exhaustion", "Kingdoms accumulate exhaustion from battles, raids, and sieges. High exhaustion slows regen and pushes the AI toward peace."),
                        1, () => s.EnableWarExhaustion, v => s.EnableWarExhaustion = v);

                    AddToggle(g, "qs_diplomacy_pressure", L("b1071_qs_name_diplomacy_pressure", "Diplomacy Pressure"),
                        L("b1071_qs_hint_diplomacy_pressure", "War exhaustion influences AI peace/war voting. Exhausted kingdoms become more willing to accept peace."),
                        2, () => s.EnableExhaustionDiplomacyPressure, v => s.EnableExhaustionDiplomacyPressure = v);

                    AddToggle(g, "qs_forced_peace", L("b1071_qs_name_forced_peace", "Forced Peace at Crisis"),
                        L("b1071_qs_hint_forced_peace", "When exhaustion exceeds a critical threshold the kingdom is forced to make peace with one enemy."),
                        3, () => s.EnableForcedPeaceAtCrisis, v => s.EnableForcedPeaceAtCrisis = v);

                    AddToggle(g, "qs_delayed_recovery", L("b1071_qs_name_delayed_recovery", "Delayed Recovery"),
                        L("b1071_qs_hint_delayed_recovery", "Settlements suffer a regen penalty after raids, sieges, or conquest that decays over time."),
                        4, () => s.EnableDelayedRecovery, v => s.EnableDelayedRecovery = v);

                    AddToggle(g, "qs_militia_link", L("b1071_qs_name_militia_link", "Militia Link"),
                        L("b1071_qs_hint_militia_link", "Militia growth scales with the settlement's manpower ratio. Empty pools produce no militia."),
                        5, () => s.EnableMilitiaLink, v => s.EnableMilitiaLink = v);
                });

                // ── Economy & Investment ───────────────────────────────────────────
                builder = builder.CreateGroup(L("b1071_qs_group_economy", "Economy & Investment"), g =>
                {
                    g.SetGroupOrder(1);

                    AddToggle(g, "qs_slave_economy", L("b1071_qs_name_slave_economy", "Slave Economy"),
                        L("b1071_qs_hint_slave_economy", "Raid villages for slaves, sell them at town markets. Slaves boost town prosperity and construction."),
                        0, () => s.EnableSlaveEconomy, v => s.EnableSlaveEconomy = v);

                    AddToggle(g, "qs_village_investment", L("b1071_qs_name_village_investment", "Village Investment"),
                        L("b1071_qs_hint_village_investment", "Spend gold at friendly villages for hearth growth, notable relations, influence, and power."),
                        1, () => s.EnableVillageInvestment, v => s.EnableVillageInvestment = v);

                    AddToggle(g, "qs_town_investment", L("b1071_qs_name_town_investment", "Town Investment"),
                        L("b1071_qs_hint_town_investment", "Spend gold at friendly towns for prosperity growth, notable relations, influence, and power."),
                        2, () => s.EnableTownInvestment, v => s.EnableTownInvestment = v);

                    AddToggle(g, "qs_minor_faction", L("b1071_qs_name_minor_faction", "Minor Faction Economy"),
                        L("b1071_qs_hint_minor_faction", "Non-bandit minor factions receive a daily stipend so they don't go bankrupt."),
                        3, () => s.EnableMinorFactionIncome, v => s.EnableMinorFactionIncome = v);

                    AddToggle(g, "qs_garrison_wage", L("b1071_qs_name_garrison_wage", "Garrison Wage Discount"),
                        L("b1071_qs_hint_garrison_wage", "Garrison troops cost a reduced percentage of field wages (default 60%)."),
                        4, () => s.EnableGarrisonWageDiscount, v => s.EnableGarrisonWageDiscount = v);
                });

                // ── Recruitment & Military ─────────────────────────────────────────
                builder = builder.CreateGroup(L("b1071_qs_group_recruitment", "Recruitment & Military"), g =>
                {
                    g.SetGroupOrder(2);

                    AddToggle(g, "qs_castle_recruitment", L("b1071_qs_name_castle_recruitment", "Castle Recruitment"),
                        L("b1071_qs_hint_castle_recruitment", "Castles generate elite troops from manpower and hold T4+ prisoners for conversion."),
                        0, () => s.EnableCastleRecruitment, v => s.EnableCastleRecruitment = v);

                    AddToggle(g, "qs_castle_access", L("b1071_qs_name_castle_access", "Open Castle Access"),
                        L("b1071_qs_hint_castle_access", "Removes the vanilla clan-tier bribe requirement for entering neutral castles."),
                        1, () => s.CastleOpenAccess, v => s.CastleOpenAccess = v);

                    AddToggle(g, "qs_combat_survivability", L("b1071_qs_name_combat_survivability", "Combat Tier Survivability"),
                        L("b1071_qs_hint_combat_survivability", "Higher-tier troops are more likely to be wounded instead of killed."),
                        2, () => s.EnableTierSurvivability, v => s.EnableTierSurvivability = v);

                    AddToggle(g, "qs_combat_armor", L("b1071_qs_name_combat_armor", "Combat Tier Armor Simulation"),
                        L("b1071_qs_hint_combat_armor", "Troop armor is factored into autoresolve battle outcomes."),
                        3, () => s.EnableTierArmorSimulation, v => s.EnableTierArmorSimulation = v);

                    AddToggle(g, "qs_clan_survival", L("b1071_qs_name_clan_survival", "Clan Survival"),
                        L("b1071_qs_hint_clan_survival", "Prevents clan annihilation when their kingdom is destroyed. Rescued clans seek mercenary service."),
                        4, () => s.EnableClanSurvival, v => s.EnableClanSurvival = v);
                });

                // ── Province & Governance ──────────────────────────────────────────
                builder = builder.CreateGroup(L("b1071_qs_group_province", "Province & Governance"), g =>
                {
                    g.SetGroupOrder(3);

                    AddToggle(g, "qs_governance", L("b1071_qs_name_governance", "Governance Strain"),
                        L("b1071_qs_hint_governance", "Owning many distant settlements reduces loyalty, security, and prosperity of far-flung holdings."),
                        0, () => s.EnableGovernanceStrain, v => s.EnableGovernanceStrain = v);

                    AddToggle(g, "qs_devastation", L("b1071_qs_name_devastation", "Frontier Devastation"),
                        L("b1071_qs_hint_devastation", "Raids cause persistent hearth, prosperity, security, and food penalties that decay slowly."),
                        1, () => s.EnableFrontierDevastation, v => s.EnableFrontierDevastation = v);

                    AddToggle(g, "qs_castle_supply", L("b1071_qs_name_castle_supply", "Castle Supply Chain"),
                        L("b1071_qs_hint_castle_supply", "Castle regen above the local trickle is transferred from the nearest same-faction town, not created from nothing."),
                        2, () => s.EnableCastleSupplyChain, v => s.EnableCastleSupplyChain = v);
                });

                // ── Immersion & Modifiers ──────────────────────────────────────────
                builder = builder.CreateGroup(L("b1071_qs_group_immersion", "Immersion & Modifiers"), g =>
                {
                    g.SetGroupOrder(4);

                    AddToggle(g, "qs_seasonal", L("b1071_qs_name_seasonal", "Seasonal Regen"),
                        L("b1071_qs_hint_seasonal", "Spring/summer boost manpower regen, winter reduces it."),
                        0, () => s.EnableSeasonalRegen, v => s.EnableSeasonalRegen = v);

                    AddToggle(g, "qs_peace_dividend", L("b1071_qs_name_peace_dividend", "Peace Dividend"),
                        L("b1071_qs_hint_peace_dividend", "Kingdoms at peace get a manpower regen multiplier bonus."),
                        1, () => s.EnablePeaceDividend, v => s.EnablePeaceDividend = v);

                    AddToggle(g, "qs_culture_disc", L("b1071_qs_name_culture_disc", "Culture Discount"),
                        L("b1071_qs_hint_culture_disc", "Recruiting from matching-culture settlements costs less manpower."),
                        2, () => s.EnableCultureDiscount, v => s.EnableCultureDiscount = v);

                    AddToggle(g, "qs_governor", L("b1071_qs_name_governor", "Governor Bonus"),
                        L("b1071_qs_hint_governor", "Governors boost regen (Steward) and max pool size (Leadership)."),
                        3, () => s.EnableGovernorBonus, v => s.EnableGovernorBonus = v);

                    AddToggle(g, "qs_overlay", L("b1071_qs_name_overlay", "Overlay (M key)"),
                        L("b1071_qs_hint_overlay", "In-game intelligence panel showing manpower, wars, factions, and more. Press M to toggle."),
                        4, () => s.EnableOverlay, v => s.EnableOverlay = v);

                    AddToggle(g, "qs_alerts", L("b1071_qs_name_alerts", "Manpower Alerts"),
                        L("b1071_qs_hint_alerts", "Yellow warning messages when a settlement's manpower drops below 25%."),
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

        private static string L(string key, string fallback) =>
            new TextObject($"{{={key}}}{fallback}").ToString();
    }
}
