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
    /// Village Investment ("Patronage") system.
    ///
    /// Provides a gold-sink mechanic where the player (and AI lords) can spend gold
    /// at non-hostile, non-looted villages for:
    ///   - Daily hearth growth bonus (via Harmony postfix in B1071_VillageInvestmentHearthPatch)
    ///   - Notable relation gain (immediate)
    ///   - Influence gain (immediate, only if village is in investor's kingdom)
    ///   - Notable power gain (immediate, capped)
    ///   - Cross-clan diplomatic relation (immediate, if investing in another clan's village)
    ///
    /// Three investment tiers: Modest / Generous / Grand — each with increasing
    /// cost, bonuses, and duration. Duration doubles as cooldown — no re-investment
    /// at the same village by the same lord until the previous investment expires.
    ///
    /// SAVE/LOAD SAFETY:
    ///   State persisted via SyncData as two dictionaries keyed by
    ///   "{settlementStringId}_{heroStringId}":
    ///     _investDaysRemaining — float, days left on active investment
    ///     _investHearthBonus   — float, daily hearth bonus amount
    ///
    /// MOD REMOVAL SAFETY:
    ///   Hearth bonus is via Harmony postfix (disappears with mod removal).
    ///   Any hearth accumulated is within vanilla's normal range.
    ///   Relation/influence/power changes are vanilla-native and persist harmlessly.
    ///
    /// AI PARITY:
    ///   AI lords invest in their own faction's villages via SettlementEntered event,
    ///   using the same tier costs and bonuses.  Throttled by:
    ///     - Configurable gold safety multiplier (default ×10)
    ///     - Random chance gate (default 30%)
    ///     - Random tier selection from affordable tiers
    ///     - Per-hero global cooldown (default 5 days between any investment)
    ///     - Hearth ceiling (skip wealthy villages above threshold)
    /// </summary>
    public sealed class B1071_VillageInvestmentBehavior : CampaignBehaviorBase
    {
        public static B1071_VillageInvestmentBehavior? Instance { get; internal set; }

        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        // Key = "{settlement.StringId}_{hero.StringId}" → days remaining on active investment.
        private Dictionary<string, float> _investDaysRemaining = new();

        // Key = same → daily hearth bonus amount for that investment.
        private Dictionary<string, float> _investHearthBonus = new();

        // Key = "{hero.StringId}" → campaign day of hero's last investment (any village).
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
            dataStore.SyncData("b1071_villageInvestDays", ref _investDaysRemaining);
            _investDaysRemaining ??= new Dictionary<string, float>();

            dataStore.SyncData("b1071_villageInvestHearth", ref _investHearthBonus);
            _investHearthBonus ??= new Dictionary<string, float>();
        }

        // ── Session launched ──────────────────────────────────────────────────────

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            Instance = this;
            if (Settings.EnableVillageInvestment)
            {
                RegisterMenus(starter);
            }
        }

        // ── Public API (used by Harmony patch) ────────────────────────────────────

        /// <summary>
        /// Returns the total active hearth bonus for a village from ALL investors.
        /// Called by B1071_VillageInvestmentHearthPatch each time vanilla calculates
        /// hearth change, to add the "Patronage" bonus line.
        /// </summary>
        public float GetActiveHearthBonus(Village village)
        {
            if (village?.Settlement == null) return 0f;
            string prefix = village.Settlement.StringId + "_";
            float total = 0f;
            foreach (var kvp in _investHearthBonus)
            {
                if (kvp.Key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    // Only count if days remaining > 0.
                    if (_investDaysRemaining.TryGetValue(kvp.Key, out float days) && days > 0f)
                    {
                        total += kvp.Value;
                    }
                }
            }
            return total;
        }

        // ── Daily tick: decrement durations, expire investments ────────────────────

        private void OnDailyTickSettlement(Settlement settlement)
        {
            if (!Settings.EnableVillageInvestment) return;
            if (!settlement.IsVillage) return;

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
                _investHearthBonus.Remove(key);
            }
        }

        // ── AI investment on settlement entry ─────────────────────────────────────

        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            if (!Settings.EnableVillageInvestment || !Settings.VillageInvestAiEnabled) return;
            if (party == null || party == MobileParty.MainParty) return; // player uses the menu
            if (!party.IsLordParty || hero == null) return;
            if (settlement == null || !settlement.IsVillage) return;

            Village village = settlement.Village;
            if (village == null || village.VillageState != Village.VillageStates.Normal) return;

            // AI only invests in own faction's villages.
            if (settlement.MapFaction != party.MapFaction) return;
            // Must not be hostile (shouldn't be if same faction, but guard).
            if (FactionManager.IsAtWarAgainstFaction(party.MapFaction, settlement.MapFaction)) return;

            // Check cooldown — no active investment by this hero at this village.
            string investKey = MakeKey(settlement, hero);
            if (_investDaysRemaining.ContainsKey(investKey)) return;

            // Per-hero global cooldown — prevent carpet-bombing every village on a route.
            float now = (float)CampaignTime.Now.ToDays;
            int heroCooldownDays = Settings.VillageInvestAiHeroCooldownDays;
            if (heroCooldownDays > 0 && _heroLastInvestDay.TryGetValue(hero.StringId, out float lastDay))
            {
                if (now - lastDay < heroCooldownDays) return;
            }

            // Hearth priority — skip wealthy villages so AI focuses on those in need.
            int hearthCeiling = Settings.VillageInvestAiHearthCeiling;
            if (hearthCeiling > 0 && village.Hearth >= hearthCeiling) return;

            // Random chance gate — only a percentage of eligible visits result in investment.
            if (MBRandom.RandomInt(100) >= Settings.VillageInvestAiChance) return;

            // Build list of affordable tiers using configurable gold safety multiplier.
            int gold = hero.Gold;
            int mult = Settings.VillageInvestAiGoldMultiplier;
            var affordableTiers = new List<int>(3);
            if (gold > Settings.VillageInvestCostModest * mult) affordableTiers.Add(1);
            if (gold > Settings.VillageInvestCostGenerous * mult) affordableTiers.Add(2);
            if (gold > Settings.VillageInvestCostGrand * mult) affordableTiers.Add(3);

            if (affordableTiers.Count == 0) return;

            // Pick tier: random selection from affordable tiers, or highest if random-tier is disabled.
            int tier;
            if (Settings.VillageInvestAiRandomTier)
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
            float hearthBonus;
            int relation;
            float influence;
            int power;

            switch (tier)
            {
                case 1: // Modest
                    cost = Settings.VillageInvestCostModest;
                    duration = Settings.VillageInvestDurationModest;
                    hearthBonus = Settings.VillageInvestHearthModest;
                    relation = Settings.VillageInvestRelationModest;
                    influence = Settings.VillageInvestInfluenceModest;
                    power = Settings.VillageInvestPowerModest;
                    break;
                case 2: // Generous
                    cost = Settings.VillageInvestCostGenerous;
                    duration = Settings.VillageInvestDurationGenerous;
                    hearthBonus = Settings.VillageInvestHearthGenerous;
                    relation = Settings.VillageInvestRelationGenerous;
                    influence = Settings.VillageInvestInfluenceGenerous;
                    power = Settings.VillageInvestPowerGenerous;
                    break;
                case 3: // Grand
                    cost = Settings.VillageInvestCostGrand;
                    duration = Settings.VillageInvestDurationGrand;
                    hearthBonus = Settings.VillageInvestHearthGrand;
                    relation = Settings.VillageInvestRelationGrand;
                    influence = Settings.VillageInvestInfluenceGrand;
                    power = Settings.VillageInvestPowerGrand;
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
            _investHearthBonus[key] = hearthBonus;

            // ── Notable relation + power ──────────────────────────────────────────
            int powerCap = Settings.VillageInvestPowerCap;
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

            // ── Influence (only if village is in investor's kingdom) ──────────────
            if (influence > 0f && investor.Clan?.Kingdom != null
                && settlement.MapFaction == investor.Clan?.Kingdom)
            {
                GainKingdomInfluenceAction.ApplyForDefault(investor, influence);
            }

            // ── Cross-clan diplomatic relation ────────────────────────────────────
            int crossClanRelation = Settings.VillageInvestCrossClanRelation;
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
                string villageName = settlement.Name?.ToString() ?? "the village";
                string msg = new TextObject("{=b1071_vi_player_msg}{TIER} investment in {VILLAGE}: -{COST}d, +{HEARTH} hearth/day for {DAYS} days")
                    .SetTextVariable("TIER", tierName)
                    .SetTextVariable("VILLAGE", villageName)
                    .SetTextVariable("COST", cost)
                    .SetTextVariable("HEARTH", hearthBonus.ToString("0.#"))
                    .SetTextVariable("DAYS", duration)
                    .ToString();
                if (relation > 0 && settlement.Notables.Count > 0)
                    msg += new TextObject("{=b1071_vi_plus_relation}, +{REL} relation").SetTextVariable("REL", relation).ToString();
                if (influence > 0f && investor.Clan?.Kingdom != null
                    && settlement.MapFaction == investor.Clan?.Kingdom)
                    msg += new TextObject("{=b1071_vi_plus_influence}, +{INF} influence")
                        .SetTextVariable("INF", influence.ToString("0.#")).ToString();
                msg += ".";
                InformationManager.DisplayMessage(new InformationMessage(msg, new Color(0.35f, 0.75f, 0.35f)));
            }
            else if (Settings.VillageInvestNotifyPlayer)
            {
                // Notify player when an AI lord invests in one of the player's villages.
                Clan? playerClan = Clan.PlayerClan;
                if (playerClan != null && settlement.OwnerClan == playerClan)
                {
                    string lordName = investor.Name?.ToString() ?? "A lord";
                    string tierLabel = tier == 1 ? new TextObject("{=b1071_tier_modest_l}modest").ToString()
                        : tier == 2 ? new TextObject("{=b1071_tier_generous_l}generous").ToString()
                        : new TextObject("{=b1071_tier_grand_l}grand").ToString();
                    string vName = settlement.Name?.ToString() ?? "your village";
                    string note = new TextObject("{=b1071_vi_ai_note}{LORD} made a {TIER} investment in {VILLAGE}.")
                        .SetTextVariable("LORD", lordName)
                        .SetTextVariable("TIER", tierLabel)
                        .SetTextVariable("VILLAGE", vName)
                        .ToString();
                    InformationManager.DisplayMessage(new InformationMessage(note, new Color(0.55f, 0.75f, 0.9f)));
                }
            }

            B1071_VerboseLog.Log("VillageInvestment",
                $"{(isPlayer ? "Player" : investor.Name?.ToString() ?? "AI")} invested " +
                $"tier {tier} ({cost}d) at {settlement.Name} — " +
                $"hearth +{hearthBonus}/day for {duration}d, " +
                $"relation +{relation}, power +{power}, influence +{influence:0.#}.");
        }

        // ── Game menus ────────────────────────────────────────────────────────────

        private void RegisterMenus(CampaignGameStarter starter)
        {
            // Entry point in the village menu → opens the investment submenu.
            starter.AddGameMenuOption(
                "village",
                "b1071_village_invest_enter",
                "{B1071_VILLAGE_INVEST_TEXT}",
                VillageInvestEnterCondition,
                _ => GameMenu.SwitchToMenu("b1071_village_invest_menu"),
                isLeave: false,
                index: 3);

            // Investment submenu.
            starter.AddGameMenu(
                "b1071_village_invest_menu",
                "{B1071_VILLAGE_INVEST_BODY}",
                _ => RefreshInvestMenuBody());

            // Modest tier.
            starter.AddGameMenuOption(
                "b1071_village_invest_menu",
                "b1071_invest_modest",
                "{B1071_INVEST_MODEST_TEXT}",
                args => InvestTierCondition(args, 1),
                args => InvestTierConsequence(args, 1),
                isLeave: false);

            // Generous tier.
            starter.AddGameMenuOption(
                "b1071_village_invest_menu",
                "b1071_invest_generous",
                "{B1071_INVEST_GENEROUS_TEXT}",
                args => InvestTierCondition(args, 2),
                args => InvestTierConsequence(args, 2),
                isLeave: false);

            // Grand tier.
            starter.AddGameMenuOption(
                "b1071_village_invest_menu",
                "b1071_invest_grand",
                "{B1071_INVEST_GRAND_TEXT}",
                args => InvestTierCondition(args, 3),
                args => InvestTierConsequence(args, 3),
                isLeave: false);

            // Leave.
            starter.AddGameMenuOption(
                "b1071_village_invest_menu",
                "b1071_village_invest_leave",
                "{=b1071_ui_leave}Leave",
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Leave;
                    args.IsEnabled = true;
                    return true;
                },
                _ => GameMenu.SwitchToMenu("village"),
                isLeave: true);
        }

        // ── Condition: village menu entry ──────────────────────────────────────────

        private bool VillageInvestEnterCondition(MenuCallbackArgs args)
        {
            if (!Settings.EnableVillageInvestment) return false;

            Settlement? s = Settlement.CurrentSettlement;
            if (s == null || !s.IsVillage || s.Village == null) return false;

            // Must not be hostile.
            if (s.MapFaction != null && Hero.MainHero?.MapFaction != null
                && FactionManager.IsAtWarAgainstFaction(Hero.MainHero.MapFaction, s.MapFaction))
                return false;

            // Must not be looted or being raided.
            if (s.Village.VillageState != Village.VillageStates.Normal) return false;

            args.optionLeaveType = GameMenuOption.LeaveType.Trade;

            if (Hero.MainHero == null) return false;

            // Check if there's already an active investment by this player at this village.
            string key = MakeKey(s, Hero.MainHero);
            if (_investDaysRemaining.TryGetValue(key, out float daysLeft) && daysLeft > 0f)
            {
                args.IsEnabled = false;
                MBTextManager.SetTextVariable("B1071_VILLAGE_INVEST_TEXT",
                    new TextObject("{=b1071_vi_enter_active}Invest in village  (active — {DAYS} days remaining)")
                        .SetTextVariable("DAYS", (int)Math.Ceiling(daysLeft)));
                args.Tooltip = new TextObject("{=b1071_vi_tip_active}You already have an active investment in this village. You can invest again once the current patronage expires.");
                return true;
            }

            // Check if player can afford at least the cheapest tier.
            if (Hero.MainHero.Gold < Settings.VillageInvestCostModest)
            {
                args.IsEnabled = false;
                MBTextManager.SetTextVariable("B1071_VILLAGE_INVEST_TEXT",
                    new TextObject("{=b1071_vi_enter_need}Invest in village  (need {COST}d)")
                        .SetTextVariable("COST", Settings.VillageInvestCostModest));
                args.Tooltip = new TextObject("{=b1071_vi_tip_need}You need at least {COST} denars for a Modest investment.")
                    .SetTextVariable("COST", Settings.VillageInvestCostModest);
                return true;
            }

            args.IsEnabled = true;
            MBTextManager.SetTextVariable("B1071_VILLAGE_INVEST_TEXT",
                new TextObject("{=b1071_vi_enter}Invest in village"));
            return true;
        }

        // ── Submenu body ───────────────────────────────────────────────────────────

        private void RefreshInvestMenuBody()
        {
            Settlement? s = Settlement.CurrentSettlement;
            if (s == null || s.Village == null) return;

            string villageName = s.Name?.ToString() ?? "this village";
            int notableCount = s.Notables?.Count ?? 0;
            float hearth = s.Village.Hearth;
            string ownerName = s.OwnerClan?.Name?.ToString() ?? "unowned";

            string body = new TextObject("{=b1071_vi_body}Village Patronage - {VILLAGE}\n\nHearth: {HEARTH}   |   Notables: {NOTABLES}   |   Owner: {OWNER}\n\nChoose an investment tier. Higher tiers cost more but provide stronger hearth growth, notable relations, influence, and power bonuses.\n\nInvestment duration also serves as cooldown — no re-investment until the current patronage expires.")
                .SetTextVariable("VILLAGE", villageName)
                .SetTextVariable("HEARTH", hearth.ToString("F0"))
                .SetTextVariable("NOTABLES", notableCount)
                .SetTextVariable("OWNER", ownerName)
                .ToString();
            MBTextManager.SetTextVariable("B1071_VILLAGE_INVEST_BODY", body);
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
                    args.Tooltip = new TextObject("{=b1071_vi_tip_days}Active investment — {DAYS} days remaining.")
                        .SetTextVariable("DAYS", (int)Math.Ceiling(remaining));
                    return true;
                }
            }

            int cost;
            int duration;
            float hearthBonus;
            int relation;
            string tierName;

            switch (tier)
            {
                case 1:
                    cost = Settings.VillageInvestCostModest;
                    duration = Settings.VillageInvestDurationModest;
                    hearthBonus = Settings.VillageInvestHearthModest;
                    relation = Settings.VillageInvestRelationModest;
                    tierName = new TextObject("{=b1071_vi_tier_modest}Modest gift").ToString();
                    break;
                case 2:
                    cost = Settings.VillageInvestCostGenerous;
                    duration = Settings.VillageInvestDurationGenerous;
                    hearthBonus = Settings.VillageInvestHearthGenerous;
                    relation = Settings.VillageInvestRelationGenerous;
                    tierName = new TextObject("{=b1071_vi_tier_generous}Generous patronage").ToString();
                    break;
                case 3:
                    cost = Settings.VillageInvestCostGrand;
                    duration = Settings.VillageInvestDurationGrand;
                    hearthBonus = Settings.VillageInvestHearthGrand;
                    relation = Settings.VillageInvestRelationGrand;
                    tierName = new TextObject("{=b1071_vi_tier_grand}Grand investment").ToString();
                    break;
                default:
                    return false;
            }

            string textVar = tier == 1 ? "B1071_INVEST_MODEST_TEXT"
                           : tier == 2 ? "B1071_INVEST_GENEROUS_TEXT"
                           : "B1071_INVEST_GRAND_TEXT";

            args.optionLeaveType = GameMenuOption.LeaveType.Trade;

            string label = $"{tierName}  ({cost}\u2b2e — +{hearthBonus:0.#} hearth/day, " +
                           $"+{relation} relation, {duration} days)";

            if (Hero.MainHero.Gold < cost)
            {
                args.IsEnabled = false;
                MBTextManager.SetTextVariable(textVar, label);
                args.Tooltip = new TextObject("{=b1071_vi_tip_need_exact}You need {COST} denars for this investment.")
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
            // Return to village menu — shows updated cooldown on the entry option.
            GameMenu.SwitchToMenu("village");
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        private static string MakeKey(Settlement settlement, Hero hero)
            => $"{settlement.StringId}_{hero.StringId}";
    }
}
