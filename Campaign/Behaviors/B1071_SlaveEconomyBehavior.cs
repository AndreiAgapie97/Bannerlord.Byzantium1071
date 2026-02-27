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
    /// Slave Economy System (v5):
    ///
    ///  ACQUISITION
    ///   1. Village raids (ItemsLooted + MapEvent.IsRaid): fires for all parties (player and AI lords).
    ///      Slave count per batch = floor(village.Hearth / SlaveHearthDivisor).
    ///      Notification is shown only for the player's own raids.
    ///   2. Prisoner enslavement (player): at any town the player can convert non-hero prisoners
    ///      at or below CastlePrisonerAutoEnslaveTierMax (default T3) via the "Enslave prisoners"
    ///      town menu option (1:1; hero prisoners and T4+ excluded).
    ///      T4+ prisoners must be taken to a castle for recruitment conversion or ransomed.
    ///   3. Prisoner enslavement (AI): handled by the Harmony Prefix in
    ///      <see cref="Patches.B1071_CastlePrisonerDepositPatch"/>. When an AI lord enters a
    ///      town, the prefix intercepts vanilla's OnSettlementEntered BEFORE vanilla can sell
    ///      the prisoners, converts T1-T3 to slave goods in the town market, and leaves T4+
    ///      for vanilla to ransom/sell. This solves the event-ordering race condition where
    ///      vanilla would vaporize prisoners before our handler could act.
    ///   4. Castle auto-enslavement: T1-T3 prisoners deposited at castles are auto-enslaved
    ///      on the next daily tick by B1071_CastleRecruitmentBehavior.AutoEnslaveLowTierPrisoners.
    ///
    ///  DISTRIBUTION
    ///   Slaves are sold through the normal civilian town market (like any other trade good).
    ///   AI lords deposit slave items (from raids) into the town market on arrival via this
    ///   behavior's OnSettlementEntered handler (slave goods only — no prisoner enslavement).
    ///   On new-game creation, each town is seeded with 0-10 slaves to make the system
    ///   visible from turn one without saturating markets (OnNewGameCreatedPartialFollowUpEnd, seeded once).
    ///
    ///  BONUSES
    ///   Each campaign day, every town with Slave goods in its market ItemRoster receives
    ///   proportional daily bonuses (until the last slave is bought from the market):
    ///     Prosperity   += slavesInMarket * SlaveProsperityPerUnit * SlaveRansomMultiplier
    ///     Construction += min(cap, slavesInMarket * SlaveConstructionAcceleration * SlaveRansomMultiplier)
    ///   Slaves are labor — historically they were NOT a source of military manpower.
    ///   Manpower injection was removed in v0.1.8.3.
    ///
    ///  SUBMENU (town game-menu entry point: "Enslave prisoners")
    ///   Visible only when the player has non-hero prisoners.
    ///   * "Enslave prisoners" -- converts all eligible (T1-T3) non-hero prisoners to Slave ItemObjects.
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
            CampaignEvents.OnCaravanTransactionCompletedEvent.AddNonSerializedListener(this, OnCaravanTransaction);
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
            {
                Debug.Print("[Byzantium1071][WARN] b1071_slave item not found in MBObjectManager — slave economy will be disabled.");
            }
            else
            {
                // ── Fix ItemCategory assignment ──────────────────────────────
                // Bannerlord loads items.xml BEFORE RegisterSubModuleObjects, so
                // item_category="b1071_slaves" fails to resolve (category not yet
                // in ObjectManager) and the item falls back to "unassigned".
                // We fix this by reassigning the ItemCategory property to our
                // code-registered category after load.
                var registeredCat = SubModule.SlaveItemCategory;
                string currentCatId = _slaveItem.ItemCategory?.StringId ?? "NULL";

                if (registeredCat != null && currentCatId != "b1071_slaves")
                {
                    try
                    {
                        var catProp = typeof(ItemObject).GetProperty("ItemCategory");
                        if (catProp != null && catProp.GetSetMethod(nonPublic: true) != null)
                        {
                            catProp.GetSetMethod(nonPublic: true)!.Invoke(_slaveItem, new object[] { registeredCat });
                            Debug.Print($"[Byzantium1071][SlaveEconomy] Reassigned ItemCategory: {currentCatId} → {registeredCat.StringId} (via property setter)");
                        }
                        else
                        {
                            // Fallback: try backing field
                            var field = typeof(ItemObject).GetField(
                                "<ItemCategory>k__BackingField",
                                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (field != null)
                            {
                                field.SetValue(_slaveItem, registeredCat);
                                Debug.Print($"[Byzantium1071][SlaveEconomy] Reassigned ItemCategory: {currentCatId} → {registeredCat.StringId} (via backing field)");
                            }
                            else
                            {
                                Debug.Print("[Byzantium1071][WARN] Could not find ItemCategory setter or backing field!");
                            }
                        }

                        // Force all towns to re-index their market data under the
                        // correct category key (InStoreValue was tracked under "unassigned").
                        foreach (Town town in Town.AllTowns)
                        {
                            town?.MarketData?.UpdateStores();
                        }
                        Debug.Print("[Byzantium1071][SlaveEconomy] Forced UpdateStores on all towns to re-index under b1071_slaves category.");
                    }
                    catch (Exception ex)
                    {
                        Debug.Print($"[Byzantium1071][ERROR] Failed to reassign ItemCategory: {ex.GetType().Name}: {ex.Message}");
                    }
                }

            }
            InitializeSlaveMarketData();
            RegisterMenus(starter);
        }

        /// <summary>
        /// Ensures every town's TownMarketData has supply/demand entries for the slave
        /// ItemCategory. Without this, vanilla's InitializeMarkets may skip our custom
        /// category (IsValid defaults to false for XML-loaded categories prior to the
        /// is_valid="true" fix), leaving demand=0 and producing a flat price regardless
        /// of stock. Runs every session launch for save-load compatibility — idempotent
        /// because we skip towns that already have demand &gt; 0 for the category.
        /// </summary>
        private void InitializeSlaveMarketData()
        {
            if (_slaveItem == null) return;
            ItemCategory? slaveCat = _slaveItem.ItemCategory;
            if (slaveCat == null) return;

            int initialized = 0;
            foreach (Town town in Town.AllTowns)
            {
                if (town == null) continue;
                // Only initialize if the category has no demand yet — prevents
                // accumulating extra demand across repeated session loads.
                float existingDemand = town.MarketData.GetDemand(slaveCat);
                if (existingDemand > 0f) continue;

                // Match vanilla TradeCampaignBehavior.InitializeMarkets: demand=3, supply=2.
                town.MarketData.AddDemand(slaveCat, 3f);
                town.MarketData.AddSupply(slaveCat, 2f);
                initialized++;
            }

            if (initialized > 0)
                Debug.Print($"[Byzantium1071][SlaveEconomy] Initialized slave market data for {initialized} town(s).");
        }

        /// <summary>
        /// Corrects the MarketData Supply EMA for the slave category when it's
        /// grossly out of proportion with actual stock.
        ///
        /// Bannerlord's Supply EMA decays at only 15%/day (half-life ~4.3 days).
        /// When 100+ slaves sit in a town, Supply converges to ~30,000+ (count × 300 value).
        /// If a player buys them all, InStoreValue drops to 0 instantly but the EMA
        /// retains the old value for weeks, pinning the price at the 0.1× floor (~30 denars).
        /// This makes slave trading unprofitable — you can't buy low and sell high
        /// because the "memory" of old stock kills prices everywhere.
        ///
        /// Fix: each daily tick, if Supply EMA exceeds 2× actual InStoreValue + a
        /// baseline buffer, snap it down.  This lets prices recover within 1–3 days
        /// instead of 30+, making slave trade arbitrage viable.
        /// </summary>
        private void CorrectSlaveSupplyEma(Town town, int currentSlaveCount)
        {
            if (_slaveItem == null) return;
            ItemCategory slaveCat = _slaveItem.ItemCategory;
            if (slaveCat == null) return;

            float supply = town.MarketData.GetSupply(slaveCat);
            float demand = town.MarketData.GetDemand(slaveCat);
            int inStoreValue = currentSlaveCount * _slaveItem.Value;

            // Allow Supply to be at most 2× actual InStoreValue + a small baseline.
            // The baseline (3000 = 2 slaves worth at 1500d base) prevents snapping
            // to zero when stock is legitimately empty.
            float maxReasonableSupply = inStoreValue * 2f + 3000f;

            if (supply > maxReasonableSupply)
            {
                town.MarketData.SetSupplyDemand(slaveCat, maxReasonableSupply, demand);
                B1071_VerboseLog.Log("SlaveEconomy",
                    $"Supply EMA correction {town.Name}: {supply:F0} → {maxReasonableSupply:F0} " +
                    $"(stock={currentSlaveCount}, inStoreValue={inStoreValue}, demand={demand:F1}).");
            }
        }

        /// <summary>
        /// Logs a daily price/supply/demand snapshot for the slave category at a town.
        /// Only fires when verbose logging is enabled.
        /// </summary>
        private void LogSlavePriceSnapshot(Town town, int slaveCount)
        {
            if (!B1071_VerboseLog.Enabled || _slaveItem == null) return;
            ItemCategory slaveCat = _slaveItem.ItemCategory;
            if (slaveCat == null) return;

            float supply = town.MarketData.GetSupply(slaveCat);
            float demand = town.MarketData.GetDemand(slaveCat);
            int price = town.MarketData.GetPrice(_slaveItem);
            B1071_VerboseLog.Log("SlaveEconomy",
                $"Price snapshot {town.Name}: price={price}d, stock={slaveCount}, " +
                $"supply={supply:F0}, demand={demand:F1}.");
        }

        /// <summary>
        /// Fires when a caravan completes a buy/sell transaction at a town.
        /// Logs any slave items traded with price details.
        /// </summary>
        private void OnCaravanTransaction(MobileParty caravan, Town town,
            List<(EquipmentElement, int)> itemList)
        {
            if (!B1071_VerboseLog.Enabled || _slaveItem == null) return;
            if (itemList == null) return;

            foreach (var (eq, count) in itemList)
            {
                if (eq.Item == null || eq.Item.StringId != _slaveItem.StringId) continue;

                string action = count > 0 ? "SOLD" : "BOUGHT";
                int absCount = Math.Abs(count);
                int price = town.MarketData.GetPrice(_slaveItem);
                float supply = town.MarketData.GetSupply(_slaveItem.ItemCategory);
                float demand = town.MarketData.GetDemand(_slaveItem.ItemCategory);
                int stock = town.Owner.ItemRoster.GetItemNumber(_slaveItem);

                B1071_VerboseLog.Log("SlaveEconomy",
                    $"Caravan trade: {caravan.Name} {action} {absCount} slave(s) " +
                    $"at {town.Name} (price={price}d, stock={stock}, " +
                    $"supply={supply:F0}, demand={demand:F1}).");
            }
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

            B1071_VerboseLog.Log("SlaveEconomy", $"Raid slaves: {mobileParty.Name} captured {count} slave(s) from {mapEvent.MapEventSettlement?.Name} ({(int)hearths} hearths / {divisor}).");

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

            // ── Supply EMA correction ─────────────────────────────────────────
            // Bannerlord's MarketData tracks a Supply EMA that decays at only
            // 15%/day toward InStoreValue.  When a large slave stock (e.g. 112)
            // accumulates and is then bought/removed, the EMA retains the old
            // high value for weeks, pinning the price at floor (~30 denars).
            // We accelerate the decay: if Supply > 2× actual InStoreValue, snap
            // it down so prices recover within a few days instead of 30+.
            CorrectSlaveSupplyEma(settlement.Town, slaveCount);

            // ── Price snapshot (verbose log) ─────────────────────────────────
            LogSlavePriceSnapshot(settlement.Town, slaveCount);

            if (slaveCount <= 0) return;

            float eff = Settings.SlaveRansomMultiplier; // bonus effectiveness multiplier

            // ── Manpower removed (v0.1.8.3) ─────────────────────────────────────
            // Slaves are labor (construction, prosperity) — historically NOT a
            // recruitment source. The +174K MP/session firehose was the #1 reason
            // settlement pools never ran dry. Removed entirely.

            B1071_VerboseLog.Log("SlaveEconomy", $"Daily bonus {settlement.Name}: {slaveCount} slave(s), eff={eff:0.##} (labor only, no MP).");

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
                    B1071_VerboseLog.Log("SlaveEconomy", $"Decay {settlement.Name}: -{wholeLoss} slave(s) attrition, {slaveCount} remaining.");
                }
                _decayAccumulator[decayKey] = accum;
            }

            // ── Manumission (slave cap overflow → freed → MP) ─────────────────────
            // If the town holds more slaves than its prosperity can support, the excess
            // are manumitted (freed) and converted 1:1 into the town's manpower pool.
            // This prevents global oversaturation (~119 slaves/town at equilibrium)
            // and creates supply differentials that drive caravan trade.
            if (Settings.SlaveCapPerProsperity > 0f && slaveCount > 0)
            {
                int cap = Math.Max(Settings.SlaveCapMinimum,
                                   (int)(settlement.Town.Prosperity * Settings.SlaveCapPerProsperity));
                if (slaveCount > cap)
                {
                    int freed = slaveCount - cap;
                    settlement.ItemRoster.AddToCounts(_slaveItem, -freed);
                    slaveCount -= freed;

                    // Convert freed slaves to manpower in the town's MP pool.
                    B1071_ManpowerBehavior? mpBehavior = B1071_ManpowerBehavior.Instance;
                    if (mpBehavior != null)
                    {
                        mpBehavior.AddManpowerToSettlement(settlement, freed);
                    }

                    B1071_VerboseLog.Log("SlaveEconomy",
                        $"Manumission at {settlement.Name}: {freed} slave(s) freed " +
                        $"(cap={cap}, stock was {slaveCount + freed}, now {slaveCount}). " +
                        $"+{freed} MP to town pool.");

                    // Notify player if at this settlement.
                    if (Settings.ShowPlayerDebugMessages && Settlement.CurrentSettlement == settlement)
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"\u2694 {settlement.Name}: {freed} slave{(freed != 1 ? "s" : "")} manumitted (cap {cap}). +{freed} MP to town pool.",
                            new Color(0.4f, 0.8f, 0.4f)));
                    }
                }
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
                    $"\u26d3 {settlement.Name}: {slaveCount} slave{(slaveCount != 1 ? "s" : "")} in market. " +
                    $"Daily: +{prosDisplay:F1} prosperity, +{consDisplay:F1} construction.",
                    new Color(0.83f, 0.67f, 0.05f)));
            }
        }

        /// <summary>
        /// Fires when any mobile party enters a settlement.
        /// For AI lord parties (not the player) entering towns:
        ///   Deposits any slave items already in the party's item roster to the town market.
        ///   (Example: slave goods acquired through village raids sitting in inventory.)
        ///
        /// NOTE: Prisoner enslavement (T1-T3 → slave goods) is handled by the Harmony
        /// Prefix in B1071_CastlePrisonerDepositPatch.HandleTownEnslavement, which runs
        /// BEFORE vanilla's OnSettlementEntered to prevent the event-ordering race condition.
        /// This handler only moves existing slave ITEMS from party inventory to town market.
        /// </summary>
        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            if (!Settings.EnableSlaveEconomy || _slaveItem == null) return;
            if (party == null || party == MobileParty.MainParty) return;  // player uses the menu
            if (!party.IsLordParty) return;                                // ignore caravans, bandits, etc.
            if (settlement == null || !settlement.IsTown) return;

            // Deposit any slaves the lord is already carrying into the town market.
            int slavesCarried = party.ItemRoster.GetItemNumber(_slaveItem);
            if (slavesCarried > 0)
            {
                party.ItemRoster.AddToCounts(_slaveItem, -slavesCarried);
                settlement.ItemRoster.AddToCounts(_slaveItem, slavesCarried);
                B1071_VerboseLog.Log("SlaveEconomy", $"AI deposit: {party.Name} deposited {slavesCarried} slave(s) at {settlement.Name}.");
            }
        }

        /// <summary>
        /// Fires at the end of new-game creation (after all settlements are fully initialised).
        /// Seeds each town with a random starting stock of 0-10 slaves so the system is
        /// visible to the player from turn one without saturating all markets.
        /// Low initial stock creates supply differentials that attract caravan trade.
        /// Guarded by <see cref="_initialStockSeeded"/> so it never re-runs on save-load.
        /// </summary>
        private void OnNewGameCreatedEnd(CampaignGameStarter starter)
        {
            if (_initialStockSeeded) return;
            _initialStockSeeded = true;

            // Ensure item is resolved (OnSessionLaunched fires before this event).
            _slaveItem ??= MBObjectManager.Instance.GetObject<ItemObject>("b1071_slave");
            if (_slaveItem == null) return;

            int seededCount = 0;
            foreach (Settlement settlement in Settlement.All)
            {
                if (!settlement.IsTown) continue;
                int count = MBRandom.RandomInt(0, 11); // 0-10 inclusive
                if (count > 0)
                {
                    settlement.ItemRoster.AddToCounts(_slaveItem, count);
                    seededCount++;
                }
            }
            B1071_VerboseLog.Log("SlaveEconomy", $"Initial stock seeded: {seededCount} town(s) with starting slaves.");
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

            int enslavable = CountEnslavablePrisoners();
            int totalPrisoners = CountAllNonHeroPrisoners();
            if (totalPrisoners <= 0) return false;

            int maxTier = Settings.CastlePrisonerAutoEnslaveTierMax;
            args.optionLeaveType = GameMenuOption.LeaveType.Trade;

            if (enslavable > 0)
            {
                args.IsEnabled = true;
                int skipped = totalPrisoners - enslavable;
                string skippedNote = skipped > 0
                    ? $", {skipped} T{maxTier + 1}+ kept"
                    : "";
                MBTextManager.SetTextVariable("B1071_SLAVE_ENTER_TEXT",
                    $"\u26d3 Enslave prisoners  ({enslavable} T1\u2013{maxTier} eligible{skippedNote})");
            }
            else
            {
                args.IsEnabled = false;
                MBTextManager.SetTextVariable("B1071_SLAVE_ENTER_TEXT",
                    $"\u26d3 Enslave prisoners  (0 eligible \u2014 {totalPrisoners} T{maxTier + 1}+)");
                args.Tooltip = new TextObject(
                    $"Only Tier 1\u2013{maxTier} prisoners can be enslaved. " +
                    $"Take T{maxTier + 1}+ to a castle for recruitment conversion or ransom at the tavern.");
            }
            return true;
        }

        // ── Submenu body ───────────────────────────────────────────────────────────

        private void RefreshSlaveMenuBody()
        {
            Settlement? s         = Settlement.CurrentSettlement;
            string settlementName = s?.Name?.ToString() ?? "this town";
            int    enslavable     = CountEnslavablePrisoners();
            int    totalPrisoners = CountAllNonHeroPrisoners();
            int    highTier       = totalPrisoners - enslavable;
            int    maxTier        = Settings.CastlePrisonerAutoEnslaveTierMax;
            int    marketSlaves   = (_slaveItem != null && s?.Town != null)
                                    ? s.ItemRoster.GetItemNumber(_slaveItem)
                                    : 0;
            int    inventorySlaves = GetSlaveGoodsCount();

            string highTierNote = highTier > 0
                ? $"\nPrisoners T{maxTier + 1}+ (not enslaveable): {highTier}  \u2192 take to castle or ransom\n"
                : "\n";

            string body =
                $"\u26d3  Slave Trade \u2014 {settlementName}\n\n" +
                $"Prisoners (T1\u2013{maxTier}, enslaveable):   {enslavable}\n" +
                highTierNote +
                $"Slave goods in your inventory:      {inventorySlaves}\n" +
                $"Slaves in {settlementName} market:  {marketSlaves}\n\n" +
                $"Sell slaves through the Trade screen.\n" +
                $"Daily town bonuses apply as long as slaves are in the market.";
            MBTextManager.SetTextVariable("B1071_SLAVE_MENU_BODY", body);
        }

        // ── Enslave option ─────────────────────────────────────────────────────────

        private bool EnslaveCondition(MenuCallbackArgs args)
        {
            int enslavable = CountEnslavablePrisoners();
            if (enslavable <= 0) return false;

            int maxTier = Settings.CastlePrisonerAutoEnslaveTierMax;
            int highTier = CountAllNonHeroPrisoners() - enslavable;
            string skippedNote = highTier > 0
                ? $" ({highTier} T{maxTier + 1}+ prisoner{(highTier != 1 ? "s" : "")} kept)"
                : "";

            args.optionLeaveType = GameMenuOption.LeaveType.Trade;
            args.IsEnabled       = true;
            MBTextManager.SetTextVariable("B1071_ENSLAVE_TEXT",
                $"Enslave prisoners  ({enslavable} T1\u2013{maxTier} \u2192 +{enslavable} Slave goods){skippedNote}");
            args.Tooltip = new TextObject(
                $"Converts all non-hero prisoners at Tier {maxTier} or below to Slave trade goods (1:1). " +
                $"Heroes and T{maxTier + 1}+ prisoners cannot be enslaved \u2014 " +
                $"take them to a castle for recruitment conversion or ransom at the tavern. " +
                "Sell Slave goods via the town Trade screen. " +
                "While Slave goods remain in the market, the town gains daily " +
                "manpower, prosperity, and construction bonuses.");
            return true;
        }

        private void EnslaveConsequence(MenuCallbackArgs args)
        {
            MobileParty? party = MobileParty.MainParty;
            if (_slaveItem == null || party == null) return;

            int maxTier = Settings.CastlePrisonerAutoEnslaveTierMax;
            int highTierBefore = CountAllNonHeroPrisoners() - CountEnslavablePrisoners();
            int converted = ConvertPrisonersToSlaves(party);
            if (converted > 0)
            {
                string keptNote = highTierBefore > 0
                    ? $" ({highTierBefore} T{maxTier + 1}+ kept \u2014 take to castle or ransom)"
                    : "";
                InformationManager.DisplayMessage(new InformationMessage(
                    $"\u26d3 Enslaved {converted} T1\u2013{maxTier} prisoner{(converted != 1 ? "s" : "")}.{keptNote} " +
                    $"Slave goods in inventory: {party.ItemRoster.GetItemNumber(_slaveItem)}. " +
                    $"Open the Trade screen to sell them to the market.",
                    new Color(0.83f, 0.67f, 0.05f)));
            }
            GameMenu.SwitchToMenu("b1071_slave_trade_menu");
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Counts non-hero prisoners that are eligible for enslavement (Tier &lt;= CastlePrisonerAutoEnslaveTierMax).
        /// </summary>
        private static int CountEnslavablePrisoners()
        {
            var roster = MobileParty.MainParty?.PrisonRoster;
            if (roster == null) return 0;
            int maxTier = (B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults).CastlePrisonerAutoEnslaveTierMax;
            int total = 0;
            foreach (var element in roster.GetTroopRoster())
            {
                if (element.Character != null && !element.Character.IsHero
                    && element.Number > 0 && element.Character.Tier <= maxTier)
                    total += element.Number;
            }
            return total;
        }

        /// <summary>
        /// Counts ALL non-hero prisoners regardless of tier.
        /// </summary>
        private static int CountAllNonHeroPrisoners()
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
        /// Converts non-hero party prisoners at or below CastlePrisonerAutoEnslaveTierMax
        /// to Slave ItemObjects (1:1). Heroes and T4+ prisoners are always skipped.
        /// Returns the number converted.
        /// </summary>
        private int ConvertPrisonersToSlaves(MobileParty party)
        {
            if (_slaveItem == null) return 0;
            int maxTier = Settings.CastlePrisonerAutoEnslaveTierMax;
            var enslavable = party.PrisonRoster
                .GetTroopRoster()
                .Where(e => e.Character != null && !e.Character.IsHero
                         && e.Number > 0 && e.Character.Tier <= maxTier)
                .ToList();

            int total = 0;
            foreach (var element in enslavable)
            {
                int count = element.Number;
                party.PrisonRoster.RemoveTroop(element.Character, count);
                party.ItemRoster.AddToCounts(_slaveItem, count);
                total += count;
            }
            B1071_VerboseLog.Log("SlaveEconomy", $"Enslavement: {party.Name} converted {total} prisoner(s) to slave goods (max tier {maxTier}).");
            return total;
        }
    }
}
