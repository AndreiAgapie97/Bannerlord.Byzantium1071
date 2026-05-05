using Byzantium1071.Campaign.Settings;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.Behaviors
{
    /// <summary>
    /// Provincial Stabilization.
    ///
    /// Lets player and AI lords fund settlement recovery after conquest pressure:
    /// immediate governance strain reduction plus temporary loyalty/security recovery.
    /// State is keyed by Settlement.StringId and persists through SyncData.
    /// </summary>
    public sealed class B1071_GovernanceStabilizationBehavior : CampaignBehaviorBase
    {
        public static B1071_GovernanceStabilizationBehavior? Instance { get; internal set; }

        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        private Dictionary<string, float> _daysRemaining = new Dictionary<string, float>();
        private Dictionary<string, float> _loyaltyBonus = new Dictionary<string, float>();
        private Dictionary<string, float> _securityBonus = new Dictionary<string, float>();
        private Dictionary<string, float> _strainDecayBonus = new Dictionary<string, float>();

        private readonly Dictionary<string, float> _heroLastStabilizationDay = new Dictionary<string, float>();

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailyTickSettlement);
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("b1071_govStabDays", ref _daysRemaining);
            _daysRemaining ??= new Dictionary<string, float>();

            dataStore.SyncData("b1071_govStabLoyalty", ref _loyaltyBonus);
            _loyaltyBonus ??= new Dictionary<string, float>();

            dataStore.SyncData("b1071_govStabSecurity", ref _securityBonus);
            _securityBonus ??= new Dictionary<string, float>();

            dataStore.SyncData("b1071_govStabDecay", ref _strainDecayBonus);
            _strainDecayBonus ??= new Dictionary<string, float>();
        }

        public float GetActiveLoyaltyBonus(Town town)
            => town?.Settlement != null ? GetActiveBonus(town.Settlement, _loyaltyBonus) : 0f;

        public float GetActiveSecurityBonus(Town town)
            => town?.Settlement != null ? GetActiveBonus(town.Settlement, _securityBonus) : 0f;

        public float GetActiveStrainDecayBonus(Settlement settlement)
            => GetActiveBonus(settlement, _strainDecayBonus);

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            Instance = this;
            RegisterMenus(starter);
        }

        private void OnDailyTickSettlement(Settlement settlement)
        {
            try
            {
                if (!Settings.EnableGovernanceStabilization) return;
                if (!IsValidTarget(settlement)) return;

                string key = settlement.StringId;
                if (!_daysRemaining.TryGetValue(key, out float days) || days <= 0f) return;

                days -= 1f;
                if (days <= 0f)
                {
                    _daysRemaining.Remove(key);
                    _loyaltyBonus.Remove(key);
                    _securityBonus.Remove(key);
                    _strainDecayBonus.Remove(key);
                    B1071_VerboseLog.Log("Governance", $"Stabilization expired at {settlement.Name}.");
                }
                else
                {
                    _daysRemaining[key] = days;
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][GovernanceStabilization] DailyTick error: {ex.Message}");
            }
        }

        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            try
            {
                if (!Settings.EnableGovernanceStrain || !Settings.EnableGovernanceStabilization || !Settings.GovernanceStabilizationAiEnabled) return;
                if (party == null || party == MobileParty.MainParty) return;
                if (!party.IsLordParty || hero == null) return;
                if (!IsValidTarget(settlement)) return;
                if (settlement.MapFaction != party.MapFaction) return;
                if (!CanHeroStabilizeSettlement(hero, settlement)) return;
                if (IsActive(settlement)) return;

                var governance = B1071_GovernanceBehavior.Instance;
                if (governance == null) return;

                float strain = governance.GetStrain(settlement);
                if (strain < Settings.GovernanceStabilizationAiStrainThreshold) return;

                int heroCooldownDays = Settings.GovernanceStabilizationAiHeroCooldownDays;
                if (heroCooldownDays > 0 && _heroLastStabilizationDay.TryGetValue(hero.StringId, out float lastDay))
                {
                    float now = (float)CampaignTime.Now.ToDays;
                    if (now - lastDay < heroCooldownDays) return;
                }

                int tier = SelectAiTier(hero);
                if (tier <= 0) return;

                ApplyStabilization(settlement, hero, tier, isPlayer: false);
                _heroLastStabilizationDay[hero.StringId] = (float)CampaignTime.Now.ToDays;
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][GovernanceStabilization] SettlementEntered error: {ex.Message}");
            }
        }

        private void ApplyStabilization(Settlement settlement, Hero sponsor, int tier, bool isPlayer)
        {
            if (!Settings.EnableGovernanceStrain || !Settings.EnableGovernanceStabilization) return;
            if (!IsValidTarget(settlement) || sponsor == null) return;
            if (!CanHeroStabilizeSettlement(sponsor, settlement)) return;
            if (IsActive(settlement)) return;

            var governance = B1071_GovernanceBehavior.Instance;
            if (governance == null) return;

            GetTierValues(tier, out int cost, out int duration, out float strainReduction,
                out float loyalty, out float security, out float decay, out TextObject tierName);

            if (cost <= 0 || duration <= 0) return;
            if (sponsor.Gold < cost) return;

            float beforeStrain = governance.GetStrain(settlement);
            if (beforeStrain <= 0f) return;

            GiveGoldAction.ApplyBetweenCharacters(sponsor, null, cost, disableNotification: true);
            governance.ReduceStrain(settlement, strainReduction);
            float afterStrain = governance.GetStrain(settlement);

            string key = settlement.StringId;
            _daysRemaining[key] = duration;
            _loyaltyBonus[key] = Math.Max(0f, loyalty);
            _securityBonus[key] = Math.Max(0f, security);
            _strainDecayBonus[key] = Math.Max(0f, decay);

            if (isPlayer)
            {
                TextObject msg = new TextObject("{=b1071_gs_player_msg}{TIER} at {SETTLEMENT}: -{COST}d, strain {BEFORE}->{AFTER}, +{LOYALTY} loyalty/day and +{SECURITY} security/day for {DAYS} days.")
                    .SetTextVariable("TIER", tierName)
                    .SetTextVariable("SETTLEMENT", settlement.Name)
                    .SetTextVariable("COST", cost)
                    .SetTextVariable("BEFORE", beforeStrain.ToString("0.#"))
                    .SetTextVariable("AFTER", afterStrain.ToString("0.#"))
                    .SetTextVariable("LOYALTY", loyalty.ToString("0.#"))
                    .SetTextVariable("SECURITY", security.ToString("0.#"))
                    .SetTextVariable("DAYS", duration);

                InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), new Color(0.35f, 0.75f, 0.55f)));
            }
            else if (Settings.GovernanceStabilizationNotifyPlayer && settlement.OwnerClan == Clan.PlayerClan)
            {
                TextObject note = new TextObject("{=b1071_gs_ai_note}{LORD} stabilized {SETTLEMENT} with {TIER}.")
                    .SetTextVariable("LORD", sponsor.Name)
                    .SetTextVariable("SETTLEMENT", settlement.Name)
                    .SetTextVariable("TIER", tierName);

                InformationManager.DisplayMessage(new InformationMessage(note.ToString(), new Color(0.55f, 0.75f, 0.9f)));
            }

            B1071_VerboseLog.Log("Governance",
                $"{(isPlayer ? "Player" : sponsor.Name?.ToString() ?? "AI")} stabilized {settlement.Name}: " +
                $"tier={tier}, cost={cost}, strain {beforeStrain:0.#}->{afterStrain:0.#}, " +
                $"loyalty=+{loyalty:0.#}/day, security=+{security:0.#}/day, decay=+{decay:0.#}/day, duration={duration}d.");
        }

        private float GetActiveBonus(Settlement settlement, Dictionary<string, float> values)
        {
            if (!Settings.EnableGovernanceStabilization) return 0f;
            if (settlement == null || values == null) return 0f;

            string key = settlement.StringId;
            if (!_daysRemaining.TryGetValue(key, out float days) || days <= 0f) return 0f;

            return values.TryGetValue(key, out float value) ? Math.Max(0f, value) : 0f;
        }

        private bool IsActive(Settlement settlement)
        {
            if (settlement == null) return false;
            return _daysRemaining.TryGetValue(settlement.StringId, out float days) && days > 0f;
        }

        private float GetDaysRemaining(Settlement settlement)
        {
            if (settlement == null) return 0f;
            return _daysRemaining.TryGetValue(settlement.StringId, out float days) ? Math.Max(0f, days) : 0f;
        }

        private int SelectAiTier(Hero hero)
        {
            if (hero == null) return 0;

            int multiplier = Math.Max(1, Settings.GovernanceStabilizationAiGoldMultiplier);
            int gold = hero.Gold;

            if (gold > Settings.GovernanceStabilizationCostAmnesty * multiplier) return 3;
            if (gold > Settings.GovernanceStabilizationCostElites * multiplier) return 2;
            if (gold > Settings.GovernanceStabilizationCostDonative * multiplier) return 1;
            return 0;
        }

        private static bool IsValidTarget(Settlement settlement)
        {
            return settlement != null
                && (settlement.IsTown || settlement.IsCastle)
                && settlement.Town != null
                && !settlement.IsUnderSiege;
        }

        private static bool CanHeroStabilizeSettlement(Hero hero, Settlement settlement)
        {
            if (hero == null || settlement == null) return false;
            if (settlement.OwnerClan == hero.Clan) return true;
            return hero.MapFaction != null && settlement.MapFaction != null && hero.MapFaction == settlement.MapFaction;
        }

        private static void GetTierValues(int tier, out int cost, out int duration,
            out float strainReduction, out float loyalty, out float security,
            out float decay, out TextObject tierName)
        {
            switch (tier)
            {
                case 1:
                    cost = Settings.GovernanceStabilizationCostDonative;
                    duration = Settings.GovernanceStabilizationDurationDonative;
                    strainReduction = Settings.GovernanceStabilizationStrainDonative;
                    loyalty = Settings.GovernanceStabilizationLoyaltyDonative;
                    security = Settings.GovernanceStabilizationSecurityDonative;
                    decay = Settings.GovernanceStabilizationDecayDonative;
                    tierName = new TextObject("{=b1071_gs_tier_donative}Emergency Relief");
                    break;
                case 2:
                    cost = Settings.GovernanceStabilizationCostElites;
                    duration = Settings.GovernanceStabilizationDurationElites;
                    strainReduction = Settings.GovernanceStabilizationStrainElites;
                    loyalty = Settings.GovernanceStabilizationLoyaltyElites;
                    security = Settings.GovernanceStabilizationSecurityElites;
                    decay = Settings.GovernanceStabilizationDecayElites;
                    tierName = new TextObject("{=b1071_gs_tier_elites}Placate Local Elites");
                    break;
                case 3:
                    cost = Settings.GovernanceStabilizationCostAmnesty;
                    duration = Settings.GovernanceStabilizationDurationAmnesty;
                    strainReduction = Settings.GovernanceStabilizationStrainAmnesty;
                    loyalty = Settings.GovernanceStabilizationLoyaltyAmnesty;
                    security = Settings.GovernanceStabilizationSecurityAmnesty;
                    decay = Settings.GovernanceStabilizationDecayAmnesty;
                    tierName = new TextObject("{=b1071_gs_tier_amnesty}Grant Amnesty");
                    break;
                default:
                    cost = 0;
                    duration = 0;
                    strainReduction = 0f;
                    loyalty = 0f;
                    security = 0f;
                    decay = 0f;
                    tierName = new TextObject(string.Empty);
                    break;
            }
        }

        private void RegisterMenus(CampaignGameStarter starter)
        {
            starter.AddGameMenuOption(
                "town",
                "b1071_governance_stabilize_town",
                "{B1071_GOV_STABILIZE_TEXT}",
                StabilizeEnterCondition,
                _ => GameMenu.SwitchToMenu("b1071_governance_stabilize_menu"),
                isLeave: false,
                index: 4);

            starter.AddGameMenuOption(
                "castle",
                "b1071_governance_stabilize_castle",
                "{B1071_GOV_STABILIZE_TEXT}",
                StabilizeEnterCondition,
                _ => GameMenu.SwitchToMenu("b1071_governance_stabilize_menu"),
                isLeave: false,
                index: 4);

            starter.AddGameMenu(
                "b1071_governance_stabilize_menu",
                "{B1071_GOV_STABILIZE_BODY}",
                _ => RefreshMenuBody());

            starter.AddGameMenuOption(
                "b1071_governance_stabilize_menu",
                "b1071_governance_stabilize_donative",
                "{B1071_GOV_STABILIZE_DONATIVE_TEXT}",
                args => StabilizeTierCondition(args, 1),
                args => StabilizeTierConsequence(args, 1),
                isLeave: false);

            starter.AddGameMenuOption(
                "b1071_governance_stabilize_menu",
                "b1071_governance_stabilize_elites",
                "{B1071_GOV_STABILIZE_ELITES_TEXT}",
                args => StabilizeTierCondition(args, 2),
                args => StabilizeTierConsequence(args, 2),
                isLeave: false);

            starter.AddGameMenuOption(
                "b1071_governance_stabilize_menu",
                "b1071_governance_stabilize_amnesty",
                "{B1071_GOV_STABILIZE_AMNESTY_TEXT}",
                args => StabilizeTierCondition(args, 3),
                args => StabilizeTierConsequence(args, 3),
                isLeave: false);

            starter.AddGameMenuOption(
                "b1071_governance_stabilize_menu",
                "b1071_governance_stabilize_leave",
                "{=b1071_ui_leave}Leave",
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Leave;
                    args.IsEnabled = true;
                    return true;
                },
                _ => GameMenu.SwitchToMenu(GetCurrentFortificationMenuId()),
                isLeave: true);
        }

        private bool StabilizeEnterCondition(MenuCallbackArgs args)
        {
            if (!Settings.EnableGovernanceStrain || !Settings.EnableGovernanceStabilization) return false;

            Settlement settlement = Settlement.CurrentSettlement;
            if (!IsValidTarget(settlement)) return false;
            if (Hero.MainHero == null || !CanHeroStabilizeSettlement(Hero.MainHero, settlement)) return false;

            var governance = B1071_GovernanceBehavior.Instance;
            if (governance == null) return false;

            args.optionLeaveType = GameMenuOption.LeaveType.Trade;

            float activeDays = GetDaysRemaining(settlement);
            if (activeDays > 0f)
            {
                args.IsEnabled = false;
                MBTextManager.SetTextVariable("B1071_GOV_STABILIZE_TEXT",
                    new TextObject("{=b1071_gs_enter_active}Stabilize province  (active - {DAYS} days remaining)")
                        .SetTextVariable("DAYS", (int)Math.Ceiling(activeDays)));
                args.Tooltip = new TextObject("{=b1071_gs_tip_active}A stabilization program is already active here.");
                return true;
            }

            if (governance.GetStrain(settlement) <= 0f) return false;

            if (Hero.MainHero.Gold < Settings.GovernanceStabilizationCostDonative)
            {
                args.IsEnabled = false;
                MBTextManager.SetTextVariable("B1071_GOV_STABILIZE_TEXT",
                    new TextObject("{=b1071_gs_enter_need}Stabilize province  (need {COST}d)")
                        .SetTextVariable("COST", Settings.GovernanceStabilizationCostDonative));
                args.Tooltip = new TextObject("{=b1071_gs_tip_need}You need at least {COST} denars for Emergency Relief.")
                    .SetTextVariable("COST", Settings.GovernanceStabilizationCostDonative);
                return true;
            }

            args.IsEnabled = true;
            MBTextManager.SetTextVariable("B1071_GOV_STABILIZE_TEXT",
                new TextObject("{=b1071_gs_enter}Stabilize province"));
            return true;
        }

        private void RefreshMenuBody()
        {
            Settlement settlement = Settlement.CurrentSettlement;
            if (!IsValidTarget(settlement)) return;

            float strain = B1071_GovernanceBehavior.Instance?.GetStrain(settlement) ?? 0f;
            float activeDays = GetDaysRemaining(settlement);
            TextObject active = activeDays > 0f
                ? new TextObject("{=b1071_yes_days}{DAYS} days").SetTextVariable("DAYS", (int)Math.Ceiling(activeDays))
                : new TextObject("{=b1071_none}none");

            TextObject body = new TextObject("{=b1071_gs_body}Provincial Stabilization - {SETTLEMENT}\n\nGovernance strain: {STRAIN}   |   Owner: {OWNER}   |   Active program: {ACTIVE}\n\nFund a stabilization program to reduce governance strain immediately and support daily loyalty and security recovery. Duration also serves as cooldown.")
                .SetTextVariable("SETTLEMENT", settlement.Name)
                .SetTextVariable("STRAIN", strain.ToString("0.#"))
                .SetTextVariable("OWNER", settlement.OwnerClan?.Name?.ToString() ?? "unowned")
                .SetTextVariable("ACTIVE", active);

            MBTextManager.SetTextVariable("B1071_GOV_STABILIZE_BODY", body.ToString());
        }

        private bool StabilizeTierCondition(MenuCallbackArgs args, int tier)
        {
            Settlement settlement = Settlement.CurrentSettlement;
            if (!IsValidTarget(settlement)) return false;

            SetTierText(tier);
            args.optionLeaveType = GameMenuOption.LeaveType.Trade;

            if (GetDaysRemaining(settlement) > 0f)
            {
                args.IsEnabled = false;
                args.Tooltip = new TextObject("{=b1071_gs_tip_active}A stabilization program is already active here.");
                return true;
            }

            GetTierValues(tier, out int cost, out _, out _, out _, out _, out _, out _);
            if (Hero.MainHero == null || Hero.MainHero.Gold < cost)
            {
                args.IsEnabled = false;
                args.Tooltip = new TextObject("{=b1071_gs_tip_need_exact}You need {COST} denars for this stabilization program.")
                    .SetTextVariable("COST", cost);
            }
            else
            {
                args.IsEnabled = true;
            }

            return true;
        }

        private void SetTierText(int tier)
        {
            GetTierValues(tier, out int cost, out int duration, out float strainReduction,
                out float loyalty, out float security, out _, out TextObject tierName);

            string textVar = tier == 1 ? "B1071_GOV_STABILIZE_DONATIVE_TEXT"
                           : tier == 2 ? "B1071_GOV_STABILIZE_ELITES_TEXT"
                           : "B1071_GOV_STABILIZE_AMNESTY_TEXT";

            string label = new TextObject("{=b1071_gs_tier_label}{TIER}  ({COST}d - -{STRAIN} strain, +{LOYALTY} loyalty/day, +{SECURITY} security/day, {DAYS} days)")
                .SetTextVariable("TIER", tierName)
                .SetTextVariable("COST", cost)
                .SetTextVariable("STRAIN", strainReduction.ToString("0.#"))
                .SetTextVariable("LOYALTY", loyalty.ToString("0.#"))
                .SetTextVariable("SECURITY", security.ToString("0.#"))
                .SetTextVariable("DAYS", duration)
                .ToString();

            MBTextManager.SetTextVariable(textVar, label);
        }

        private void StabilizeTierConsequence(MenuCallbackArgs args, int tier)
        {
            Settlement settlement = Settlement.CurrentSettlement;
            if (settlement == null || Hero.MainHero == null) return;

            ApplyStabilization(settlement, Hero.MainHero, tier, isPlayer: true);
            GameMenu.SwitchToMenu(GetCurrentFortificationMenuId());
        }

        private static string GetCurrentFortificationMenuId()
        {
            Settlement settlement = Settlement.CurrentSettlement;
            return settlement != null && settlement.IsCastle ? "castle" : "town";
        }
    }
}