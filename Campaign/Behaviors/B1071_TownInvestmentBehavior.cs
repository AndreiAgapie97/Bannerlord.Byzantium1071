using Byzantium1071.Campaign.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using Debug = TaleWorlds.Library.Debug;

namespace Byzantium1071.Campaign.Behaviors
{
    /// <summary>
    /// Town Investment ("Civic Patronage") system.
    ///
    /// Provides a gold-sink mechanic where the player (and AI lords) can spend gold
    /// at non-hostile towns for:
    ///   - Daily prosperity growth bonus (via Harmony postfix in B1071_TownInvestmentProsperityPatch)
    ///   - Notable relation gain (immediate)
    ///   - Influence gain (immediate, only if town is in investor's kingdom)
    ///   - Notable power gain (immediate, capped)
    ///   - Cross-clan diplomatic relation (immediate, if investing in another clan's town)
    ///
    /// Three investment tiers: Modest / Generous / Grand — each with increasing
    /// cost, bonuses, and duration. Duration doubles as cooldown — no re-investment
    /// at the same town by the same lord until the previous investment expires.
    ///
    /// Mirrors the village investment system (B1071_VillageInvestmentBehavior) but
    /// targets towns with higher costs and prosperity instead of hearth.
    ///
    /// SAVE/LOAD SAFETY:
    ///   State persisted via SyncData as two dictionaries keyed by
    ///   "{settlementStringId}_{heroStringId}":
    ///     _investDaysRemaining    — float, days left on active investment
    ///     _investProsperityBonus  — float, daily prosperity bonus amount
    ///
    /// MOD REMOVAL SAFETY:
    ///   Prosperity bonus is via Harmony postfix (disappears with mod removal).
    ///   Any prosperity accumulated is within vanilla's normal range.
    ///   Relation/influence/power changes are vanilla-native and persist harmlessly.
    ///
    /// AI PARITY:
    ///   AI lords invest in their own faction's towns via SettlementEntered event,
    ///   using the same tier costs and bonuses.  Throttled by:
    ///     - Configurable gold safety multiplier (default ×15)
    ///     - Random chance gate (default 30%)
    ///     - Random tier selection from affordable tiers
    ///     - Per-hero global cooldown (default 5 days between any investment)
    ///     - Prosperity ceiling (skip wealthy towns above threshold)
    /// </summary>
    public sealed class B1071_TownInvestmentBehavior : CampaignBehaviorBase
    {
        public static B1071_TownInvestmentBehavior? Instance { get; internal set; }

        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        // Key = "{settlement.StringId}_{hero.StringId}" → days remaining on active investment.
        private Dictionary<string, float> _investDaysRemaining = new();

        // Key = same → daily prosperity bonus amount for that investment.
        private Dictionary<string, float> _investProsperityBonus = new();

        // Key = "{hero.StringId}" → campaign day of hero's last town investment (any town).
        // Non-serialized — resets on load, which is fine (conservative: first visit post-load is allowed).
        private readonly Dictionary<string, float> _heroLastInvestDay = new();

        // ── CampaignBehaviorBase ──────────────────────────────────────────────────

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailyTickSettlement);
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("b1071_townInvestDays", ref _investDaysRemaining);
            _investDaysRemaining ??= new Dictionary<string, float>();

            dataStore.SyncData("b1071_townInvestProsperity", ref _investProsperityBonus);
            _investProsperityBonus ??= new Dictionary<string, float>();
        }

        // ── Session launched ──────────────────────────────────────────────────────

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            Instance = this;
            if (Settings.EnableTownInvestment)
            {
                RegisterMenus(starter);
            }
        }

        // ── Public API (used by Harmony patch) ────────────────────────────────────

        /// <summary>
        /// Returns the total active prosperity bonus for a town from ALL investors.
        /// Called by B1071_TownInvestmentProsperityPatch each time vanilla calculates
        /// prosperity change, to add the "Civic Patronage" bonus line.
        /// </summary>
        public float GetActiveProsperityBonus(Town town)
        {
            if (town?.Settlement == null) return 0f;
            string prefix = town.Settlement.StringId + "_";
            float total = 0f;
            int activeCount = 0;
            foreach (var kvp in _investProsperityBonus)
            {
                if (kvp.Key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    // Only count if days remaining > 0.
                    if (_investDaysRemaining.TryGetValue(kvp.Key, out float days) && days > 0f)
                    {
                        total += kvp.Value;
                        activeCount++;
                    }
                }
            }
            return total;
        }

        // ── Daily tick: decrement durations, expire investments ────────────────────

        private void OnDailyTickSettlement(Settlement settlement)
        {
            if (!Settings.EnableTownInvestment) return;
            if (!settlement.IsTown) return;

            string prefix = settlement.StringId + "_";
            var keysToRemove = new List<string>();

            foreach (var key in _investDaysRemaining.Keys.ToList())
            {
                if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;

                float remaining = _investDaysRemaining[key] - 1f;
                if (remaining <= 0f)
                {
                    keysToRemove.Add(key);
                }
                else
                {
                    _investDaysRemaining[key] = remaining;
                }
            }

            foreach (string key in keysToRemove)
            {
                _investDaysRemaining.Remove(key);
                _investProsperityBonus.Remove(key);
                B1071_VerboseLog.Log("TownInvestment",
                    $"Investment expired: key={key} at {settlement.Name}.");
            }

            // Log active prosperity bonus once per day (was previously in GetActiveProsperityBonus
            // which fires on every model query — multiple times per tick per town).
            if (settlement.Town != null)
            {
                float bonus = GetActiveProsperityBonus(settlement.Town);
                if (bonus > 0f)
                {
                    int activeCount = 0;
                    foreach (var kvp in _investDaysRemaining)
                    {
                        if (kvp.Key.StartsWith(prefix, StringComparison.Ordinal) && kvp.Value > 0f)
                            activeCount++;
                    }
                    B1071_VerboseLog.Log("TownInvestment",
                        $"Prosperity bonus for {settlement.Name}: +{bonus:F2}/day from {activeCount} active patron(s).");
                }
            }
        }

        // ── AI investment on settlement entry ─────────────────────────────────────

        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            if (!Settings.EnableTownInvestment || !Settings.TownInvestAiEnabled) return;
            if (party == null || party == MobileParty.MainParty) return; // player uses the menu
            if (!party.IsLordParty || hero == null) return;
            if (settlement == null || !settlement.IsTown) return;

            Town town = settlement.Town;
            if (town == null) return;

            // Must not be under siege (parity with player menu).
            if (settlement.IsUnderSiege) return;

            // AI only invests in own faction's towns.
            if (settlement.MapFaction != party.MapFaction) return;
            // Must not be hostile (shouldn't be if same faction, but guard).
            if (FactionManager.IsAtWarAgainstFaction(party.MapFaction, settlement.MapFaction)) return;

            // Check cooldown — no active investment by this hero at this town.
            string investKey = MakeKey(settlement, hero);
            if (_investDaysRemaining.ContainsKey(investKey)) return;

            // Per-hero global cooldown — prevent carpet-bombing every town on a route.
            float now = (float)CampaignTime.Now.ToDays;
            int heroCooldownDays = Settings.TownInvestAiHeroCooldownDays;
            if (heroCooldownDays > 0 && _heroLastInvestDay.TryGetValue(hero.StringId, out float lastDay))
            {
                if (now - lastDay < heroCooldownDays) return;
            }

            // Prosperity priority — skip wealthy towns so AI focuses on those in need.
            int prosperityCeiling = Settings.TownInvestAiProsperityCeiling;
            if (prosperityCeiling > 0 && town.Prosperity >= prosperityCeiling) return;

            // Random chance gate — only a percentage of eligible visits result in investment.
            if (MBRandom.RandomInt(100) >= Settings.TownInvestAiChance) return;

            // Build list of affordable tiers using configurable gold safety multiplier.
            int gold = hero.Gold;
            int mult = Settings.TownInvestAiGoldMultiplier;
            var affordableTiers = new List<int>(3);
            if (gold > Settings.TownInvestCostModest * mult) affordableTiers.Add(1);
            if (gold > Settings.TownInvestCostGenerous * mult) affordableTiers.Add(2);
            if (gold > Settings.TownInvestCostGrand * mult) affordableTiers.Add(3);

            if (affordableTiers.Count == 0) return;

            // Pick tier: random selection from affordable tiers, or highest if random-tier is disabled.
            int tier;
            if (Settings.TownInvestAiRandomTier)
                tier = affordableTiers[MBRandom.RandomInt(affordableTiers.Count)];
            else
                tier = affordableTiers[affordableTiers.Count - 1]; // last = highest

            ApplyInvestment(settlement, hero, tier, isPlayer: false);

            // Record this hero's last investment day for global cooldown.
            _heroLastInvestDay[hero.StringId] = (float)CampaignTime.Now.ToDays;
        }

        // ── Core investment logic (shared by player and AI) ───────────────────────

        private void ApplyInvestment(Settlement settlement, Hero investor, int tier, bool isPlayer)
        {
            int cost;
            int duration;
            float prosperityBonus;
            int relation;
            float influence;
            int power;

            switch (tier)
            {
                case 1: // Modest
                    cost = Settings.TownInvestCostModest;
                    duration = Settings.TownInvestDurationModest;
                    prosperityBonus = Settings.TownInvestProsperityModest;
                    relation = Settings.TownInvestRelationModest;
                    influence = Settings.TownInvestInfluenceModest;
                    power = Settings.TownInvestPowerModest;
                    break;
                case 2: // Generous
                    cost = Settings.TownInvestCostGenerous;
                    duration = Settings.TownInvestDurationGenerous;
                    prosperityBonus = Settings.TownInvestProsperityGenerous;
                    relation = Settings.TownInvestRelationGenerous;
                    influence = Settings.TownInvestInfluenceGenerous;
                    power = Settings.TownInvestPowerGenerous;
                    break;
                case 3: // Grand
                    cost = Settings.TownInvestCostGrand;
                    duration = Settings.TownInvestDurationGrand;
                    prosperityBonus = Settings.TownInvestProsperityGrand;
                    relation = Settings.TownInvestRelationGrand;
                    influence = Settings.TownInvestInfluenceGrand;
                    power = Settings.TownInvestPowerGrand;
                    break;
                default:
                    return;
            }

            // Safety: verify affordability again.
            if (investor.Gold < cost) return;

            // Guard: prevent duplicate investment (defense-in-depth against multi-click).
            string key = MakeKey(settlement, investor);
            if (_investDaysRemaining.ContainsKey(key)) return;

            // ── Deduct gold (gold sink — null recipient = money destroyed) ────────
            GiveGoldAction.ApplyBetweenCharacters(investor, null, cost, disableNotification: true);

            // ── Record investment state ───────────────────────────────────────────
            _investDaysRemaining[key] = duration;
            _investProsperityBonus[key] = prosperityBonus;

            // ── Notable relation + power ──────────────────────────────────────────
            int powerCap = Settings.TownInvestPowerCap;
            foreach (Hero notable in settlement.Notables)
            {
                if (notable == null || !notable.IsNotable) continue;

                // Relation
                if (relation > 0)
                {
                    if (isPlayer)
                        ChangeRelationAction.ApplyPlayerRelation(notable, relation, affectRelatives: false, showQuickNotification: false);
                    else
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(investor, notable, relation, showQuickNotification: false);
                }

                // Power (capped)
                if (power > 0 && notable.Power < powerCap)
                {
                    float actualPowerAdd = Math.Min(power, powerCap - notable.Power);
                    if (actualPowerAdd > 0f)
                        notable.AddPower(actualPowerAdd);
                }
            }

            // ── Influence (only if town is in investor's kingdom) ─────────────────
            if (influence > 0f && investor.Clan?.Kingdom != null
                && settlement.MapFaction == investor.Clan?.Kingdom)
            {
                GainKingdomInfluenceAction.ApplyForDefault(investor, influence);
            }

            // ── Cross-clan diplomatic relation ────────────────────────────────────
            int crossClanRelation = Settings.TownInvestCrossClanRelation;
            if (crossClanRelation > 0)
            {
                Clan? ownerClan = settlement.OwnerClan;
                if (ownerClan != null && ownerClan != investor.Clan && ownerClan.Leader != null)
                {
                    if (isPlayer)
                        ChangeRelationAction.ApplyPlayerRelation(ownerClan.Leader, crossClanRelation, affectRelatives: false, showQuickNotification: false);
                    else
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(investor, ownerClan.Leader, crossClanRelation, showQuickNotification: false);
                }
            }

            // ── Player notifications ──────────────────────────────────────────────
            if (isPlayer)
            {
                string tierName = tier == 1 ? new TextObject("{=b1071_tier_modest}Modest").ToString()
                    : tier == 2 ? new TextObject("{=b1071_tier_generous}Generous").ToString()
                    : new TextObject("{=b1071_tier_grand}Grand").ToString();
                string townName = settlement.Name?.ToString() ?? "the town";
                string msg = new TextObject("{=b1071_ti_player_msg}{TIER} investment in {TOWN}: -{COST}d, +{PROS} prosperity/day for {DAYS} days")
                    .SetTextVariable("TIER", tierName)
                    .SetTextVariable("TOWN", townName)
                    .SetTextVariable("COST", cost)
                    .SetTextVariable("PROS", prosperityBonus.ToString("0.#"))
                    .SetTextVariable("DAYS", duration)
                    .ToString();
                if (relation > 0 && settlement.Notables.Count > 0)
                    msg += new TextObject("{=b1071_ti_plus_relation}, +{REL} relation").SetTextVariable("REL", relation).ToString();
                if (influence > 0f && investor.Clan?.Kingdom != null
                    && settlement.MapFaction == investor.Clan?.Kingdom)
                    msg += new TextObject("{=b1071_ti_plus_influence}, +{INF} influence")
                        .SetTextVariable("INF", influence.ToString("0.#")).ToString();
                msg += ".";
                InformationManager.DisplayMessage(new InformationMessage(msg, new Color(0.35f, 0.65f, 0.85f)));
            }
            else if (Settings.TownInvestNotifyPlayer)
            {
                // Notify player when an AI lord invests in one of the player's towns.
                Clan? playerClan = Clan.PlayerClan;
                if (playerClan != null && settlement.OwnerClan == playerClan)
                {
                    string lordName = investor.Name?.ToString() ?? "A lord";
                    string tierLabel = tier == 1 ? new TextObject("{=b1071_tier_modest_l}modest").ToString()
                        : tier == 2 ? new TextObject("{=b1071_tier_generous_l}generous").ToString()
                        : new TextObject("{=b1071_tier_grand_l}grand").ToString();
                    string tName = settlement.Name?.ToString() ?? "your town";
                    string note = new TextObject("{=b1071_ti_ai_note}{LORD} made a {TIER} investment in {TOWN}.")
                        .SetTextVariable("LORD", lordName)
                        .SetTextVariable("TIER", tierLabel)
                        .SetTextVariable("TOWN", tName)
                        .ToString();
                    InformationManager.DisplayMessage(new InformationMessage(note, new Color(0.55f, 0.75f, 0.9f)));
                }
            }

            B1071_VerboseLog.Log("TownInvestment",
                $"{(isPlayer ? "Player" : investor.Name?.ToString() ?? "AI")} invested " +
                $"tier {tier} ({cost}d) at {settlement.Name} - " +
                $"prosperity +{prosperityBonus}/day for {duration}d, " +
                $"relation +{relation}, power +{power}, influence +{influence:0.#}.");
        }

        // ── Game menus ────────────────────────────────────────────────────────────

        private void RegisterMenus(CampaignGameStarter starter)
        {
            // Entry point in the town menu → opens the investment submenu.
            starter.AddGameMenuOption(
                "town",
                "b1071_town_invest_enter",
                "{B1071_TOWN_INVEST_TEXT}",
                TownInvestEnterCondition,
                _ => GameMenu.SwitchToMenu("b1071_town_invest_menu"),
                isLeave: false,
                index: 3);

            // Investment submenu.
            starter.AddGameMenu(
                "b1071_town_invest_menu",
                "{B1071_TOWN_INVEST_BODY}",
                _ => RefreshInvestMenuBody());

            // Modest tier.
            starter.AddGameMenuOption(
                "b1071_town_invest_menu",
                "b1071_town_invest_modest",
                "{B1071_TOWN_INVEST_MODEST_TEXT}",
                args => InvestTierCondition(args, 1),
                args => InvestTierConsequence(args, 1),
                isLeave: false);

            // Generous tier.
            starter.AddGameMenuOption(
                "b1071_town_invest_menu",
                "b1071_town_invest_generous",
                "{B1071_TOWN_INVEST_GENEROUS_TEXT}",
                args => InvestTierCondition(args, 2),
                args => InvestTierConsequence(args, 2),
                isLeave: false);

            // Grand tier.
            starter.AddGameMenuOption(
                "b1071_town_invest_menu",
                "b1071_town_invest_grand",
                "{B1071_TOWN_INVEST_GRAND_TEXT}",
                args => InvestTierCondition(args, 3),
                args => InvestTierConsequence(args, 3),
                isLeave: false);

            // Leave.
            starter.AddGameMenuOption(
                "b1071_town_invest_menu",
                "b1071_town_invest_leave",
                "{=b1071_ui_leave}Leave",
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Leave;
                    args.IsEnabled = true;
                    return true;
                },
                _ => GameMenu.SwitchToMenu("town"),
                isLeave: true);
        }

        // ── Condition: town menu entry ─────────────────────────────────────────────

        private bool TownInvestEnterCondition(MenuCallbackArgs args)
        {
            if (!Settings.EnableTownInvestment) return false;

            Settlement? s = Settlement.CurrentSettlement;
            if (s == null || !s.IsTown || s.Town == null) return false;

            // Must not be hostile.
            if (s.MapFaction != null && Hero.MainHero?.MapFaction != null
                && FactionManager.IsAtWarAgainstFaction(Hero.MainHero.MapFaction, s.MapFaction))
                return false;

            // Must not be under siege.
            if (s.IsUnderSiege) return false;

            args.optionLeaveType = GameMenuOption.LeaveType.Trade;

            if (Hero.MainHero == null) return false;

            // Check if there's already an active investment by this player at this town.
            string key = MakeKey(s, Hero.MainHero);
            if (_investDaysRemaining.TryGetValue(key, out float daysLeft) && daysLeft > 0f)
            {
                args.IsEnabled = false;
                MBTextManager.SetTextVariable("B1071_TOWN_INVEST_TEXT",
                    new TextObject("{=b1071_ti_enter_active}Invest in town  (active - {DAYS} days remaining)")
                        .SetTextVariable("DAYS", (int)Math.Ceiling(daysLeft)));
                args.Tooltip = new TextObject("{=b1071_ti_tip_active}You already have an active investment in this town. You can invest again once the current patronage expires.");
                return true;
            }

            // Check if player can afford at least the cheapest tier.
            if (Hero.MainHero.Gold < Settings.TownInvestCostModest)
            {
                args.IsEnabled = false;
                MBTextManager.SetTextVariable("B1071_TOWN_INVEST_TEXT",
                    new TextObject("{=b1071_ti_enter_need}Invest in town  (need {COST}d)")
                        .SetTextVariable("COST", Settings.TownInvestCostModest));
                args.Tooltip = new TextObject("{=b1071_ti_tip_need}You need at least {COST} denars for a Modest investment.")
                    .SetTextVariable("COST", Settings.TownInvestCostModest);
                return true;
            }

            args.IsEnabled = true;
            MBTextManager.SetTextVariable("B1071_TOWN_INVEST_TEXT",
                new TextObject("{=b1071_ti_enter}Invest in town"));
            return true;
        }

        // ── Submenu body ───────────────────────────────────────────────────────────

        private void RefreshInvestMenuBody()
        {
            Settlement? s = Settlement.CurrentSettlement;
            if (s == null || s.Town == null) return;

            string townName = s.Name?.ToString() ?? "this town";
            int notableCount = s.Notables?.Count ?? 0;
            float prosperity = s.Town.Prosperity;
            string ownerName = s.OwnerClan?.Name?.ToString() ?? "unowned";

            string body = new TextObject("{=b1071_ti_body}Civic Patronage - {TOWN}\n\nProsperity: {PROSP}   |   Notables: {NOTABLES}   |   Owner: {OWNER}\n\nChoose an investment tier. Higher tiers cost more but provide stronger prosperity growth, notable relations, influence, and power bonuses.\n\nInvestment duration also serves as cooldown - no re-investment until the current patronage expires.")
                .SetTextVariable("TOWN", townName)
                .SetTextVariable("PROSP", prosperity.ToString("F0"))
                .SetTextVariable("NOTABLES", notableCount)
                .SetTextVariable("OWNER", ownerName)
                .ToString();
            MBTextManager.SetTextVariable("B1071_TOWN_INVEST_BODY", body);
        }

        // ── Tier conditions ────────────────────────────────────────────────────────

        private bool InvestTierCondition(MenuCallbackArgs args, int tier)
        {
            // ── Guard: block ALL tier options if an active investment exists ──
            Settlement? current = Settlement.CurrentSettlement;
            if (current != null && Hero.MainHero != null)
            {
                string activeKey = MakeKey(current, Hero.MainHero);
                if (_investDaysRemaining.TryGetValue(activeKey, out float remaining) && remaining > 0f)
                {
                    args.IsEnabled = false;
                    args.optionLeaveType = GameMenuOption.LeaveType.Trade;
                    args.Tooltip = new TextObject("{=b1071_ti_tip_days}Active investment - {DAYS} days remaining.")
                        .SetTextVariable("DAYS", (int)Math.Ceiling(remaining));
                    return true;
                }
            }

            int cost;
            int duration;
            float prosperityBonus;
            int relation;
            string tierName;

            switch (tier)
            {
                case 1:
                    cost = Settings.TownInvestCostModest;
                    duration = Settings.TownInvestDurationModest;
                    prosperityBonus = Settings.TownInvestProsperityModest;
                    relation = Settings.TownInvestRelationModest;
                    tierName = new TextObject("{=b1071_ti_tier_modest}Modest gift").ToString();
                    break;
                case 2:
                    cost = Settings.TownInvestCostGenerous;
                    duration = Settings.TownInvestDurationGenerous;
                    prosperityBonus = Settings.TownInvestProsperityGenerous;
                    relation = Settings.TownInvestRelationGenerous;
                    tierName = new TextObject("{=b1071_ti_tier_generous}Generous patronage").ToString();
                    break;
                case 3:
                    cost = Settings.TownInvestCostGrand;
                    duration = Settings.TownInvestDurationGrand;
                    prosperityBonus = Settings.TownInvestProsperityGrand;
                    relation = Settings.TownInvestRelationGrand;
                    tierName = new TextObject("{=b1071_ti_tier_grand}Grand investment").ToString();
                    break;
                default:
                    return false;
            }

            string textVar = tier == 1 ? "B1071_TOWN_INVEST_MODEST_TEXT"
                           : tier == 2 ? "B1071_TOWN_INVEST_GENEROUS_TEXT"
                           : "B1071_TOWN_INVEST_GRAND_TEXT";

            args.optionLeaveType = GameMenuOption.LeaveType.Trade;

            string label = $"{tierName}  ({cost}d - +{prosperityBonus:0.#} prosperity/day, " +
                           $"+{relation} relation, {duration} days)";

            if (Hero.MainHero?.Gold < cost)
            {
                args.IsEnabled = false;
                MBTextManager.SetTextVariable(textVar, label);
                args.Tooltip = new TextObject("{=b1071_ti_tip_need_exact}You need {COST} denars for this investment.")
                    .SetTextVariable("COST", cost);
            }
            else
            {
                args.IsEnabled = true;
                MBTextManager.SetTextVariable(textVar, label);
            }

            return true;
        }

        // ── Tier consequences ──────────────────────────────────────────────────────

        private void InvestTierConsequence(MenuCallbackArgs args, int tier)
        {
            Settlement? s = Settlement.CurrentSettlement;
            if (s == null || Hero.MainHero == null) return;

            ApplyInvestment(s, Hero.MainHero, tier, isPlayer: true);
            // Return to town menu — shows updated cooldown on the entry option.
            GameMenu.SwitchToMenu("town");
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        private static string MakeKey(Settlement settlement, Hero hero)
            => $"{settlement.StringId}_{hero.StringId}";
    }
}
