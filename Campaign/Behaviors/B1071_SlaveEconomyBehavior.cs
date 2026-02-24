using Byzantium1071.Campaign.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.ObjectSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.Behaviors
{
    /// <summary>
    /// Slave Economy System (v3):
    ///
    ///  ACQUISITION
    ///   1. Village raids (ItemsLooted + MapEvent.IsRaid): fires for all parties (player and AI lords).
    ///      Slave count per batch = floor(village.Hearth / SlaveHearthDivisor).
    ///      Notification is shown only for the player's own raids.
    ///   2. Prisoner enslavement (player): at any town the player can convert non-hero prisoners via
    ///      the "Enslave prisoners" town menu option (1:1; hero prisoners excluded).
    ///   3. Prisoner enslavement (AI): when an AI lord enters a town, Tier 1-2 non-hero prisoners
    ///      are automatically enslaved and deposited into the town market. Tier 3+ are left for
    ///      vanilla ransom/release behaviour.
    ///
    ///  DISTRIBUTION
    ///   Slaves are sold through the normal civilian town market (like any other trade good).
    ///   AI lords deposit slave items into the town market on arrival (OnSettlementEntered).
    ///   On new-game creation, each town is seeded with 0-30 slaves to make the system
    ///   immediately visible from turn one (OnNewGameCreatedPartialFollowUpEnd, seeded once).
    ///
    ///  BONUSES
    ///   Each campaign day, every town with Slave goods in its market ItemRoster receives
    ///   proportional daily bonuses (until the last slave is bought from the market):
    ///     Manpower     += slavesInMarket * SlaveManpowerPerUnit * SlaveRansomMultiplier
    ///     Prosperity   += slavesInMarket * SlaveProsperityPerUnit * SlaveRansomMultiplier
    ///     Construction += min(cap, slavesInMarket * SlaveConstructionAcceleration * SlaveRansomMultiplier)
    ///
    ///  SUBMENU (town game-menu entry point: "Enslave prisoners")
    ///   Visible only when the player has non-hero prisoners.
    ///   * "Enslave prisoners" -- converts all non-hero prisoners to Slave ItemObjects.
    ///   * "Leave"             -- returns to town menu.
    ///
    ///  PRICE / GOLD
    ///   SlaveRansomMultiplier acts as a "slave effectiveness" multiplier that scales all daily
    ///   bonus outputs. Gold from selling slaves comes from the normal market trade price,
    ///   which is based on the item base value in items.xml.
    ///
    ///  HISTORY NOTE
    ///   The 1071 Byzantine-Seljuk context saw widespread slave trade; this mechanic mirrors
    ///   documented practice and is gameplay-balanced via MCM settings.
    /// </summary>
    public sealed class B1071_SlaveEconomyBehavior : CampaignBehaviorBase
    {
        public static B1071_SlaveEconomyBehavior? Instance { get; internal set; }

        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        // Slave trade ItemObject resolved from XML on session launch.
        private ItemObject? _slaveItem;

        // Tracks whether starting slave stocks have been seeded into town markets (new game only).
        private bool _initialStockSeeded = false;

        // Fractional decay accumulator per settlement (key = StringId).
        // Prevents rounding loss when decay rate is <1 slave/day.
        private Dictionary<string, float> _decayAccumulator = new Dictionary<string, float>();

        // ── CampaignBehaviorBase ──────────────────────────────────────────────────

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.ItemsLooted.AddNonSerializedListener(this, OnItemsLooted);
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailyTickSettlement);
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
            CampaignEvents.OnNewGameCreatedPartialFollowUpEndEvent.AddNonSerializedListener(this, OnNewGameCreatedEnd);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // _initialStockSeeded ensures starting slave stocks are seeded only once on new games.
            dataStore.SyncData("b1071_initialStockSeeded", ref _initialStockSeeded);
            dataStore.SyncData("b1071_slaveDecayAccum", ref _decayAccumulator);
            _decayAccumulator ??= new Dictionary<string, float>();
        }

        // ── Event handlers ────────────────────────────────────────────────────────

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            Instance   = this;
            _slaveItem = MBObjectManager.Instance.GetObject<ItemObject>("b1071_slave");
            if (_slaveItem == null)
                Debug.Print("[Byzantium1071][WARN] b1071_slave item not found in MBObjectManager — slave economy will be disabled.");
            RegisterMenus(starter);
        }

        /// <summary>
        /// Fires each time the player's party receives a loot batch.
        /// We filter for raid MapEvents (MapEvent.IsRaid == true) to distinguish village raids
        /// from post-battle loot pickup.
        /// Slave count per event = floor(village.Hearth / SlaveHearthDivisor).
        /// This runs at the same cadence as the plunder items shown in the raid log
        /// (each grain/clay line = one ItemsLooted call).
        /// </summary>
        private void OnItemsLooted(MobileParty mobileParty, ItemRoster items)
        {
            if (!Settings.EnableSlaveEconomy || _slaveItem == null) return;

            // MapEvent.IsRaid distinguishes village raids from field-battle loot.
            MapEvent? mapEvent = mobileParty.MapEvent;
            if (mapEvent == null || !mapEvent.IsRaid) return;

            // Derive slave count from the raided village's hearth count.
            // More hearths = larger village = more slaves captured.
            Village? village = mapEvent.MapEventSettlement?.Village;
            float   hearths  = village?.Hearth ?? 0f;
            int     divisor  = Math.Max(1, Settings.SlaveHearthDivisor);
            int     count    = (int)(hearths / divisor);
            if (count <= 0) return;

            mobileParty.ItemRoster.AddToCounts(_slaveItem, count);

            // Only notify the player for their own raids.
            if (mobileParty == MobileParty.MainParty)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"\u26d3 +{count} Slave{(count > 1 ? "s" : "")} " +
                    $"({(int)hearths} hearths / {divisor} = {count}).",
                    new Color(0.83f, 0.67f, 0.05f)));
            }
        }

        /// <summary>
        /// Fires once per campaign day per settlement.
        /// Applies proportional daily bonuses to towns that currently have Slave goods in their market.
        /// Bonuses cease automatically when the market slave stock reaches zero.
        /// </summary>
        private void OnDailyTickSettlement(Settlement settlement)
        {
            if (!Settings.EnableSlaveEconomy || _slaveItem == null) return;
            if (!settlement.IsTown || settlement.Town == null) return;

            int slaveCount = settlement.ItemRoster.GetItemNumber(_slaveItem);
            if (slaveCount <= 0) return;

            float eff = Settings.SlaveRansomMultiplier; // bonus effectiveness multiplier

            // ── Manpower ─────────────────────────────────────────────────────────
            int manpowerGain = Math.Max(1, (int)(slaveCount * Settings.SlaveManpowerPerUnit * eff));
            if (B1071_ManpowerBehavior.Instance != null)
                B1071_ManpowerBehavior.Instance.AddManpowerToSettlement(settlement, manpowerGain);

            // NOTE: Prosperity and Construction bonuses are applied via Harmony postfixes:
            //   B1071_SlaveProsperityPatch  → DefaultSettlementProsperityModel.CalculateProsperityChange
            //   B1071_SlaveConstructionPatch → DefaultBuildingConstructionModel.CalculateDailyConstructionPower
            //   B1071_SlaveFoodPatch         → DefaultSettlementFoodModel.CalculateTownFoodStocksChange
            // This makes all bonuses visible in-game tooltips and prevents double-counting.

            // ── Slave attrition (deaths, escapes, manumission) ────────────────────
            // Removes a fraction of the slave population each day.
            // Without decay, slave populations only grow — decay creates natural equilibrium.
            if (Settings.SlaveDailyDecayPercent > 0f)
            {
                float decayFraction = Settings.SlaveDailyDecayPercent / 100f;
                float rawLoss = slaveCount * decayFraction;
                // Accumulate fractional loss — when >= 1.0, remove whole slaves.
                string decayKey = settlement.StringId;
                if (!_decayAccumulator.TryGetValue(decayKey, out float accum))
                    accum = 0f;
                accum += rawLoss;
                int wholeLoss = Math.Min((int)accum, slaveCount);
                if (wholeLoss > 0)
                {
                    settlement.ItemRoster.AddToCounts(_slaveItem, -wholeLoss);
                    slaveCount -= wholeLoss;  // update local count for notification
                    accum -= wholeLoss;
                }
                _decayAccumulator[decayKey] = accum;
            }

            // ── Daily notification ────────────────────────────────────────────────
            // Only show for the settlement the player is currently inside — prevent
            // spamming a message for every slave-holding town in the world each day.
            if (Settings.ShowPlayerDebugMessages && Settlement.CurrentSettlement == settlement)
            {
                float prosDisplay = slaveCount * Settings.SlaveProsperityPerUnit * eff;
                float consDisplay = Math.Min(
                    Settings.SlaveConstructionBonusCap,
                    slaveCount * Settings.SlaveConstructionAcceleration * eff);
                InformationManager.DisplayMessage(new InformationMessage(
                    $"\u26d3 {settlement.Name}: {slaveCount} slaves \u2192 " +
                    $"+{manpowerGain} MP, +{prosDisplay:F2} pros, " +
                    $"+{consDisplay:F1} cons/day.",
                    new Color(0.83f, 0.67f, 0.05f)));
            }
        }

        /// <summary>
        /// Fires when any mobile party enters a settlement.
        /// For AI lord parties (not the player):
        ///   1. Any slave items already in the party's item roster are deposited to the town market.
        ///   2. Non-hero prisoners of Tier 1-2 are enslaved and added directly to the town market.
        ///      Tier 3+ prisoners are left for vanilla ransom/release logic.
        /// </summary>
        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            if (!Settings.EnableSlaveEconomy || _slaveItem == null) return;
            if (party == null || party == MobileParty.MainParty) return;  // player uses the menu
            if (!party.IsLordParty) return;                                // ignore caravans, bandits, etc.
            if (settlement == null || !settlement.IsTown) return;

            // 1. Deposit any slaves the lord is already carrying into the town market.
            int slavesCarried = party.ItemRoster.GetItemNumber(_slaveItem);
            if (slavesCarried > 0)
            {
                party.ItemRoster.AddToCounts(_slaveItem, -slavesCarried);
                settlement.ItemRoster.AddToCounts(_slaveItem, slavesCarried);
            }

            // 2. Tier-based prisoner enslavement: T1-T2 → slaves, T3+ stays as prisoners.
            var roster = party.PrisonRoster;
            if (roster == null) return;

            var toEnslave = roster.GetTroopRoster()
                .Where(e => e.Character != null && !e.Character.IsHero && e.Number > 0 && e.Character.Tier <= 2)
                .ToList();

            foreach (var element in toEnslave)
            {
                int count = element.Number;
                roster.RemoveTroop(element.Character, count);
                settlement.ItemRoster.AddToCounts(_slaveItem, count);
            }
        }

        /// <summary>
        /// Fires at the end of new-game creation (after all settlements are fully initialised).
        /// Seeds each town with a random starting stock of 0-30 slaves so the system is
        /// immediately visible to the player from turn one.
        /// Guarded by <see cref="_initialStockSeeded"/> so it never re-runs on save-load.
        /// </summary>
        private void OnNewGameCreatedEnd(CampaignGameStarter starter)
        {
            if (_initialStockSeeded) return;
            _initialStockSeeded = true;

            // Ensure item is resolved (OnSessionLaunched fires before this event).
            _slaveItem ??= MBObjectManager.Instance.GetObject<ItemObject>("b1071_slave");
            if (_slaveItem == null) return;

            foreach (Settlement settlement in Settlement.All)
            {
                if (!settlement.IsTown) continue;
                int count = MBRandom.RandomInt(0, 31); // 0-30 inclusive
                if (count > 0)
                    settlement.ItemRoster.AddToCounts(_slaveItem, count);
            }
        }

        // ── Public helpers used by Harmony patches ────────────────────────────────

        /// <summary>
        /// Returns the number of Slave goods currently in the given town's civilian market.
        /// Used by B1071_SlaveConstructionPatch to add the slave-labor construction bonus
        /// to the vanilla CalculateDailyConstructionPower ExplainedNumber.
        /// </summary>
        public int GetSlaveCountForTown(Town town)
            => (_slaveItem != null && town?.Settlement != null)
               ? town.Settlement.ItemRoster.GetItemNumber(_slaveItem)
               : 0;

        // ── Game menus ────────────────────────────────────────────────────────────

        private void RegisterMenus(CampaignGameStarter starter)
        {
            // Entry point: visible in town main menu when player has enslaveable prisoners.
            starter.AddGameMenuOption(
                "town",
                "b1071_slave_trade_enter",
                "{B1071_SLAVE_ENTER_TEXT}",
                SlaveTradeEnterCondition,
                _ => GameMenu.SwitchToMenu("b1071_slave_trade_menu"),
                isLeave: false,
                index: 5);

            // Slave trade submenu.
            starter.AddGameMenu(
                "b1071_slave_trade_menu",
                "{B1071_SLAVE_MENU_BODY}",
                _ => RefreshSlaveMenuBody());

            // Enslave prisoners.
            starter.AddGameMenuOption(
                "b1071_slave_trade_menu",
                "b1071_enslave_prisoners",
                "{B1071_ENSLAVE_TEXT}",
                EnslaveCondition,
                EnslaveConsequence,
                isLeave: false);

            // Leave.
            starter.AddGameMenuOption(
                "b1071_slave_trade_menu",
                "b1071_slave_trade_leave",
                "Leave",
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Leave;
                    args.IsEnabled       = true;
                    return true;
                },
                _ => GameMenu.SwitchToMenu("town"),
                isLeave: true);
        }

        // ── Condition: town menu entry ─────────────────────────────────────────────

        private bool SlaveTradeEnterCondition(MenuCallbackArgs args)
        {
            if (!Settings.EnableSlaveEconomy) return false;
            Settlement? s = Settlement.CurrentSettlement;
            if (s == null || !s.IsTown) return false;

            int prisoners = CountNonHeroPrisoners();
            if (prisoners <= 0) return false;

            args.optionLeaveType = GameMenuOption.LeaveType.Trade;
            args.IsEnabled       = true;
            MBTextManager.SetTextVariable("B1071_SLAVE_ENTER_TEXT",
                $"\u26d3 Enslave prisoners  ({prisoners} non-hero prisoner{(prisoners != 1 ? "s" : "")})");
            return true;
        }

        // ── Submenu body ───────────────────────────────────────────────────────────

        private void RefreshSlaveMenuBody()
        {
            Settlement? s         = Settlement.CurrentSettlement;
            string settlementName = s?.Name?.ToString() ?? "this town";
            int    prisoners      = CountNonHeroPrisoners();
            int    marketSlaves   = (_slaveItem != null && s?.Town != null)
                                    ? s.ItemRoster.GetItemNumber(_slaveItem)
                                    : 0;
            int    inventorySlaves = GetSlaveGoodsCount();

            string body =
                $"\u26d3  Slave Trade \u2014 {settlementName}\n\n" +
                $"Prisoners (non-hero, enslaveable):  {prisoners}\n" +
                $"Slave goods in your inventory:      {inventorySlaves}\n" +
                $"Slaves in {settlementName} market:  {marketSlaves}\n\n" +
                $"Sell slaves through the Trade screen.\n" +
                $"Daily town bonuses apply as long as slaves are in the market.";
            MBTextManager.SetTextVariable("B1071_SLAVE_MENU_BODY", body);
        }

        // ── Enslave option ─────────────────────────────────────────────────────────

        private bool EnslaveCondition(MenuCallbackArgs args)
        {
            int count = CountNonHeroPrisoners();
            if (count <= 0) return false;

            args.optionLeaveType = GameMenuOption.LeaveType.Trade;
            args.IsEnabled       = true;
            MBTextManager.SetTextVariable("B1071_ENSLAVE_TEXT",
                $"Enslave prisoners  ({count} non-hero prisoner{(count != 1 ? "s" : "")} \u2192 +{count} Slave goods)");
            args.Tooltip = new TextObject(
                "Converts ALL non-hero prisoners in your camp to Slave trade goods (1:1). " +
                "Heroes can never be enslaved. " +
                "Sell Slave goods via the town Trade screen. " +
                "While Slave goods remain in the market, the town gains daily " +
                "manpower, prosperity, and construction bonuses.");
            return true;
        }

        private void EnslaveConsequence(MenuCallbackArgs args)
        {
            MobileParty? party = MobileParty.MainParty;
            if (_slaveItem == null || party == null) return;

            int converted = ConvertPrisonersToSlaves(party);
            if (converted > 0)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"\u26d3 Enslaved {converted} prisoner{(converted != 1 ? "s" : "")}. " +
                    $"Slave goods in inventory: {party.ItemRoster.GetItemNumber(_slaveItem)}. " +
                    $"Open the Trade screen to sell them to the market.",
                    new Color(0.83f, 0.67f, 0.05f)));
            }
            GameMenu.SwitchToMenu("b1071_slave_trade_menu");
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        private static int CountNonHeroPrisoners()
        {
            var roster = MobileParty.MainParty?.PrisonRoster;
            if (roster == null) return 0;
            int total = 0;
            foreach (var element in roster.GetTroopRoster())
            {
                if (element.Character != null && !element.Character.IsHero && element.Number > 0)
                    total += element.Number;
            }
            return total;
        }

        private int GetSlaveGoodsCount()
            => _slaveItem != null
               ? (MobileParty.MainParty?.ItemRoster?.GetItemNumber(_slaveItem) ?? 0)
               : 0;

        /// <summary>
        /// Converts ALL non-hero party prisoners to Slave ItemObjects (1:1).
        /// Heroes are always skipped. Returns the number converted.
        /// </summary>
        private int ConvertPrisonersToSlaves(MobileParty party)
        {
            if (_slaveItem == null) return 0;
            var nonHeroes = party.PrisonRoster
                .GetTroopRoster()
                .Where(e => e.Character != null && !e.Character.IsHero && e.Number > 0)
                .ToList();

            int total = 0;
            foreach (var element in nonHeroes)
            {
                int count = element.Number;
                party.PrisonRoster.RemoveTroop(element.Character, count);
                party.ItemRoster.AddToCounts(_slaveItem, count);
                total += count;
            }
            return total;
        }
    }
}
