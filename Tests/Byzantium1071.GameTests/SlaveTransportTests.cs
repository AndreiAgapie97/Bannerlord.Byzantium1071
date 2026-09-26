#if GAME_TESTS_ENABLED
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Byzantium1071.Campaign;
using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Patches;
using Byzantium1071.Campaign.Settings;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using Xunit;

namespace Byzantium1071.GameTests
{
    [Collection(nameof(CastleRecruitmentActionCollection))]
    public sealed class SlaveTransportTests : IDisposable
    {
        private readonly Harmony _harmony = new Harmony("B1071.Tests.SlaveTransport");
        private readonly B1071_SlaveEconomyBehavior? _previous = B1071_SlaveEconomyBehavior.Instance;
        private readonly B1071_SlaveEconomyBehavior _behavior = new B1071_SlaveEconomyBehavior();
        private readonly B1071_McmSettings _settings = new B1071_McmSettings { EnableSlaveEconomy = true, SlaveFoodConsumptionPerUnit = 0.05f };
        private readonly ItemObject _slave = Empty<ItemObject>();
        private readonly MobileParty _party = Empty<MobileParty>();
        private readonly PartyBase _base = Empty<PartyBase>();

        public SlaveTransportTests()
        {
            B1071_TestHooks.Settings = _settings;
            B1071_SlaveEconomyBehavior.Instance = _behavior;
            AccessTools.Field(typeof(B1071_SlaveEconomyBehavior), "_slaveItem").SetValue(_behavior, _slave);
            Set(_party, "Party", _base);
            Set(_base, "MobileParty", _party);
            Set(_base, "ItemRoster", new ItemRoster());
            Set(_base, "PrisonRoster", TroopRoster.CreateDummyTroopRoster());
            Set(_base, "MemberRoster", TroopRoster.CreateDummyTroopRoster());
            AccessTools.Field(typeof(MobileParty), "_attachedParties").SetValue(_party, new MBList<MobileParty>());
            // No campaign is running; leave all actual roster operations intact.
            _harmony.Patch(AccessTools.PropertyGetter(typeof(MobileParty), "MainParty"),
                prefix: new HarmonyMethod(typeof(SlaveTransportTests), nameof(NoMainParty)));
        }

        private static bool NoMainParty(ref MobileParty __result) { __result = null!; return false; }
        private static bool True(ref bool __result) { __result = true; return false; }
        private static bool TotalCapacity(ref int __result) { __result = 100; return false; }
        private static bool TroopCapacity(PartyBase party, ref int __result)
        {
            __result = 10 + party.NumberOfHealthyMembers / 2;
            return false;
        }

        private void PatchCapacity(int cachedCapacity = 100, bool useTroopCount = false)
        {
            _harmony.Patch(AccessTools.Method(typeof(B1071_SlaveTransport), "TotalCapacity"),
                prefix: new HarmonyMethod(typeof(SlaveTransportTests), useTroopCount ? nameof(TroopCapacity) : nameof(TotalCapacity)));
            _harmony.CreateClassProcessor(typeof(B1071_SlavePrisonerCapacityPatch)).Patch();
            AccessTools.Field(typeof(PartyBase), "_cachedPrisonerSizeLimit").SetValue(_base, cachedCapacity);
            AccessTools.Field(typeof(PartyBase), "_prisonerSizeLastCheckVersion").SetValue(_base, _base.PrisonRoster.VersionNo);
        }

        [Fact]
        public void EveryTransportPatchActuallyBindsToInstalledGame()
        {
            foreach (var type in new[] { typeof(B1071_SlavePartyFoodPatch), typeof(B1071_SlavePrisonerCapacityPatch),
                typeof(B1071_SlavePrisonerCapacityExplanationPatch), typeof(B1071_SlavePrisonerWarningPatch),
                typeof(B1071_SlavePartyScreenCapacityPatch),
                typeof(B1071_SlaveEscortSpeedPatch), typeof(B1071_SlaveSharedEscapePatch) })
                Assert.NotEmpty(_harmony.CreateClassProcessor(type).Patch());
        }

        [Theory]
        [InlineData(80)]
        [InlineData(140)]
        public void RemovingLastSlaveDoesNotRestoreStaleCapacity(int cachedCapacity)
        {
            PatchCapacity(cachedCapacity);
            _base.ItemRoster.AddToCounts(_slave, 1);
            Assert.Equal(99, _base.PrisonerSizeLimit);
            _base.ItemRoster.AddToCounts(_slave, -1);
            Assert.Equal(100, _base.PrisonerSizeLimit);
            _settings.EnableSlaveEconomy = false;
            Assert.Equal(cachedCapacity, _base.PrisonerSizeLimit);
        }

        [Fact]
        public void OpenPartyScreenUpdatesMixedCaptiveWarningOnTroopTransferAndUndo()
        {
            var troop = Empty<CharacterObject>();
            _base.MemberRoster.AddToCounts(troop, 260);
            _base.PrisonRoster.AddToCounts(troop, 90);
            _base.ItemRoster.AddToCounts(_slave, 20);
            PatchCapacity(140, useTroopCount: true);
            _harmony.Patch(AccessTools.PropertyGetter(typeof(MobileParty), nameof(MobileParty.IsMainParty)),
                prefix: new HarmonyMethod(typeof(SlaveTransportTests), nameof(True)));
            _harmony.CreateClassProcessor(typeof(B1071_SlavePartyScreenCapacityPatch)).Patch();
            _harmony.CreateClassProcessor(typeof(B1071_SlavePrisonerWarningPatch)).Patch();
            var vm = Empty<PartyVM>();
            var logic = Empty<PartyScreenLogic>();
            Set(logic, nameof(PartyScreenLogic.RightOwnerParty), _base);
            Set(logic, nameof(PartyScreenLogic.RightPartyPrisonersSizeLimit), 120);
            logic.MemberRosters = new[] { TroopRoster.CreateDummyTroopRoster(), _base.MemberRoster };
            logic.PrisonerRosters = new[] { TroopRoster.CreateDummyTroopRoster(), _base.PrisonRoster };
            Set(vm, nameof(PartyVM.PartyScreenLogic), logic);
            vm.ArePrisonersRelevantOnCurrentMode = true;
            bool boundWarning = false;
            vm.PropertyChangedWithBoolValue += (_, e) =>
            {
                if (e.PropertyName == nameof(PartyVM.IsMainPrisonersLimitWarningEnabled)) boundWarning = e.Value;
            };
            Assert.Equal(120, logic.RightPartyPrisonersSizeLimit);
            _base.MemberRoster.AddToCounts(troop, -80);
            Assert.Equal(80, logic.RightPartyPrisonersSizeLimit);
            // Same comparison used by native RefreshPartyInformation.
            vm.IsMainPrisonersLimitWarningEnabled = logic.RightPartyPrisonersSizeLimit < logic.PrisonerRosters[1].TotalManCount;
            Assert.True(vm.IsMainPrisonersLimitWarningEnabled);
            Assert.True(boundWarning);
            _base.MemberRoster.AddToCounts(troop, 80); // undo the transfer
            Assert.Equal(120, logic.RightPartyPrisonersSizeLimit);
            vm.IsMainPrisonersLimitWarningEnabled = logic.RightPartyPrisonersSizeLimit < logic.PrisonerRosters[1].TotalManCount;
            Assert.False(vm.IsMainPrisonersLimitWarningEnabled);
            Assert.False(boundWarning);
            // A detached preview's supplied limit is not the owner's live capacity.
            logic.MemberRosters[1] = _base.MemberRoster.CloneRosterData();
            Set(logic, nameof(PartyScreenLogic.RightPartyPrisonersSizeLimit), 35);
            Assert.Equal(35, logic.RightPartyPrisonersSizeLimit);
            logic.MemberRosters[1] = _base.MemberRoster;
            _settings.EnableSlaveEconomy = false;
            Assert.Equal(35, logic.RightPartyPrisonersSizeLimit);
        }

        [Fact]
        public void InventoryAcquisitionAndRemovalImmediatelyReserveAndRestoreCachedCapacity()
        {
            PatchCapacity();
            Assert.Equal(100, _base.PrisonerSizeLimit);
            _base.ItemRoster.AddToCounts(_slave, 40); // purchase/loot/transfer all enter this same roster
            Assert.Equal(60, _base.PrisonerSizeLimit);
            _base.ItemRoster.AddToCounts(_slave, 80);
            Assert.Equal(0, _base.PrisonerSizeLimit);
            _base.ItemRoster.AddToCounts(_slave, -100);
            Assert.Equal(80, _base.PrisonerSizeLimit);
            _settings.EnableSlaveEconomy = false;
            Assert.Equal(100, _base.PrisonerSizeLimit);
        }

        [Fact]
        public void ConvertingPrisonersDoesNotFreeSharedCapacity()
        {
            var troop = Empty<CharacterObject>();
            _base.PrisonRoster.AddToCounts(troop, 80);
            int before = 100 - _base.PrisonRoster.TotalManCount;
            _base.PrisonRoster.AddToCounts(troop, -30);
            _base.ItemRoster.AddToCounts(_slave, 30);
            PatchCapacity();
            int remaining = 100;
            B1071_SlavePrisonerCapacityPatch.Postfix(_base, ref remaining);
            Assert.Equal(before, remaining - _base.PrisonRoster.TotalManCount);
        }

        [Theory]
        [InlineData(80)]
        [InlineData(140)]
        public void StaleNativeCapacityCannotChangeSharedLimitOrSpeed(int cachedCapacity)
        {
            var troop = Empty<CharacterObject>();
            _base.MemberRoster.AddToCounts(troop, 180);
            _base.PrisonRoster.AddToCounts(troop, 70);
            _base.ItemRoster.AddToCounts(_slave, 20);
            PatchCapacity(cachedCapacity);
            // Native cache predates a recruitment/dismissal or wounded-count change.
            // The current model says 100, so all 90 captives fit regardless of that cache.
            Assert.Equal(80, _base.PrisonerSizeLimit);
            var speed = new ExplainedNumber(5f);
            speed.AddFactor((float)Math.Pow(190d / 260d, 0.33d) - 1f);
            if (70 > _base.PrisonerSizeLimit)
                speed.AddFactor(_base.PrisonerSizeLimit / 70f - 1f);
            B1071_SlaveEscortSpeedPatch.Postfix(_party, 0, 0, ref speed);
            Assert.InRange(Math.Abs(speed.ResultNumber - 5d * Math.Pow(190d / 280d, 0.33d)), 0d, 0.00001d);
        }

        [Theory]
        [InlineData(120, true, true, true)]
        [InlineData(100, true, true, false)]
        [InlineData(80, true, true, false)]
        [InlineData(120, false, true, false)]
        [InlineData(120, true, false, false)]
        public void PartyScreenWarnsForSlaveOnlyOverload(int slaves, bool enabled, bool relevant, bool expected)
        {
            _base.ItemRoster.AddToCounts(_slave, slaves);
            _settings.EnableSlaveEconomy = enabled;
            PatchCapacity();
            _harmony.Patch(AccessTools.PropertyGetter(typeof(MobileParty), nameof(MobileParty.IsMainParty)),
                prefix: new HarmonyMethod(typeof(SlaveTransportTests), nameof(True)));
            _harmony.CreateClassProcessor(typeof(B1071_SlavePrisonerWarningPatch)).Patch();
            var vm = Empty<PartyVM>();
            var logic = Empty<PartyScreenLogic>();
            Set(logic, nameof(PartyScreenLogic.RightOwnerParty), _base);
            Set(vm, nameof(PartyVM.PartyScreenLogic), logic);
            vm.ArePrisonersRelevantOnCurrentMode = relevant;
            bool boundWarning = false;
            vm.PropertyChangedWithBoolValue += (_, e) =>
            {
                if (e.PropertyName == nameof(PartyVM.IsMainPrisonersLimitWarningEnabled))
                    boundWarning = e.Value;
            };
            vm.IsMainPrisonersLimitWarningEnabled = false; // native zero-prisoner comparison
            Assert.Equal(expected, vm.IsMainPrisonersLimitWarningEnabled);
            Assert.Equal(expected, boundWarning);
            // An ordinary-prisoner warning must never be suppressed by this patch.
            vm.IsMainPrisonersLimitWarningEnabled = true;
            Assert.True(vm.IsMainPrisonersLimitWarningEnabled);
            _base.ItemRoster.Clear();
            vm.IsMainPrisonersLimitWarningEnabled = false;
            Assert.False(vm.IsMainPrisonersLimitWarningEnabled);
            Assert.False(boundWarning);
        }

        [Fact]
        public void SlaveOverloadDoesNotWarnForAnotherParty()
        {
            _base.ItemRoster.AddToCounts(_slave, 120);
            PatchCapacity();
            _harmony.CreateClassProcessor(typeof(B1071_SlavePrisonerWarningPatch)).Patch();
            var vm = Empty<PartyVM>();
            var logic = Empty<PartyScreenLogic>();
            Set(logic, nameof(PartyScreenLogic.RightOwnerParty), _base);
            Set(vm, nameof(PartyVM.PartyScreenLogic), logic);
            vm.ArePrisonersRelevantOnCurrentMode = true;
            // The fixture's main-party getter returns null: this is another party.
            vm.IsMainPrisonersLimitWarningEnabled = false;
            Assert.False(vm.IsMainPrisonersLimitWarningEnabled);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ExcessCaptivesCanEscapeFromEitherRoster(bool selectSlave)
        {
            var troop = Empty<CharacterObject>();
            _base.PrisonRoster.AddToCounts(troop, 60);
            _base.ItemRoster.AddToCounts(_slave, 60);
            B1071_TestHooks.Random = new EscapeRandom(selectSlave);
            _behavior.ApplySharedCaptiveEscapes(_party, 100, 1f);
            Assert.Equal(selectSlave ? 40 : 60, _behavior.GetSlaveCount(_base.ItemRoster));
            Assert.Equal(selectSlave ? 60 : 40, _base.PrisonRoster.TotalManCount);
            _behavior.ApplySharedCaptiveEscapes(_party, 100, 1f);
            Assert.Equal(100, _behavior.GetSlaveCount(_base.ItemRoster) + _base.PrisonRoster.TotalManCount);
        }

        [Fact]
        public void SlaveOnlyOverloadAlsoEscapesButUnderCapacityDoesNot()
        {
            _base.ItemRoster.AddToCounts(_slave, 120);
            B1071_TestHooks.Random = new EscapeRandom(true);
            _behavior.ApplySharedCaptiveEscapes(_party, 100, 1f);
            Assert.Equal(100, _behavior.GetSlaveCount(_base.ItemRoster));
            _behavior.ApplySharedCaptiveEscapes(_party, 100, 1f);
            Assert.Equal(100, _behavior.GetSlaveCount(_base.ItemRoster));
        }

        [Fact]
        public void PartyFoodAddsConfiguredRationsBeforeNativeModifiersAndStopsWhenDisabled()
        {
            _base.ItemRoster.AddToCounts(_slave, 100);
            _party.IsActive = true;
            var model = new DefaultMobilePartyFoodConsumptionModel();
            var food = new ExplainedNumber(-10f);
            B1071_SlavePartyFoodPatch.Postfix(model, _party, ref food);
            Assert.Equal(-15f, food.ResultNumber);
            food.AddFactor(-0.5f);
            Assert.Equal(-7.5f, food.ResultNumber);
            _settings.EnableSlaveEconomy = false;
            B1071_SlavePartyFoodPatch.Postfix(model, _party, ref food);
            Assert.Equal(-7.5f, food.ResultNumber);
        }

        [Fact]
        public void MarketAndStashBothEatButOnlyMarketContributesLabor()
        {
            var settlement = Empty<Settlement>();
            var settlementParty = Empty<PartyBase>();
            var town = Empty<Town>();
            Set(settlement, "Party", settlementParty);
            Set(settlementParty, "Settlement", settlement);
            AccessTools.Field(typeof(SettlementComponent), "_owner").SetValue(town, settlementParty);
            Set(settlementParty, "ItemRoster", new ItemRoster());
            AccessTools.Field(typeof(Settlement), "Stash").SetValue(settlement, new ItemRoster());
            settlement.ItemRoster.AddToCounts(_slave, 20);
            settlement.Stash.AddToCounts(_slave, 80);
            var food = new ExplainedNumber(20f);
            B1071_SlaveFoodPatch.Postfix(town, ref food);
            Assert.Equal(15f, food.ResultNumber);
            Assert.Equal(20, _behavior.GetSlaveCountForTown(town));
        }

        [Theory]
        [InlineData("IsCaravan")]
        [InlineData("IsBandit")]
        [InlineData("IsVillager")]
        [InlineData("IsGarrison")]
        [InlineData("IsMilitia")]
        [InlineData("IsPatrolParty")]
        public void NativeFoodExemptionsRemainExempt(string property)
        {
            _base.ItemRoster.AddToCounts(_slave, 100);
            _party.IsActive = true;
            var setter = AccessTools.PropertySetter(typeof(MobileParty), property);
            if (setter != null) setter.Invoke(_party, new object[] { true });
            else _harmony.Patch(AccessTools.PropertyGetter(typeof(MobileParty), property),
                prefix: new HarmonyMethod(typeof(SlaveTransportTests), nameof(True)));
            var food = new ExplainedNumber(-10f);
            B1071_SlavePartyFoodPatch.Postfix(new DefaultMobilePartyFoodConsumptionModel(), _party, ref food);
            Assert.Equal(-10f, food.ResultNumber);
        }

        [Fact]
        public void CapacityTooltipReportsReservationsAndNeverGoesNegative()
        {
            _base.ItemRoster.AddToCounts(_slave, 120);
            var capacity = new ExplainedNumber(100f);
            B1071_SlavePrisonerCapacityExplanationPatch.Postfix(_base, ref capacity);
            Assert.Equal(0f, capacity.ResultNumber);
            _base.ItemRoster.AddToCounts(_slave, -80);
            capacity = new ExplainedNumber(100f);
            capacity.AddFactor(0.5f);
            B1071_SlavePrisonerCapacityExplanationPatch.Postfix(_base, ref capacity);
            Assert.Equal(110f, capacity.ResultNumber);
        }

        [Theory]
        [InlineData(0, 50)]
        [InlineData(50, 50)]
        [InlineData(60, 60)]
        [InlineData(0, 200)]
        [InlineData(200, 0)]
        public void SpeedCombinesNativeAndSlaveBurdenExactlyOnce(int prisoners, int slaves)
        {
            var troop = Empty<CharacterObject>();
            _base.MemberRoster.AddToCounts(troop, 100);
            _base.PrisonRoster.AddToCounts(troop, prisoners);
            _base.ItemRoster.AddToCounts(_slave, slaves);
            PatchCapacity();
            var speed = new ExplainedNumber(5f);
            // Independent native baseline, including the reduced capacity getter.
            speed.AddFactor((float)Math.Pow(110d / (110d + prisoners), 0.33d) - 1f);
            int reservedLimit = _base.PrisonerSizeLimit;
            if (prisoners > reservedLimit) speed.AddFactor((float)reservedLimit / prisoners - 1f);
            B1071_SlaveEscortSpeedPatch.Postfix(_party, 0, 0, ref speed);
            double escort = Math.Pow(110d / (110d + prisoners + slaves), 0.33d) - 1d;
            double over = prisoners + slaves > 100 ? 100d / (prisoners + slaves) - 1d : 0d;
            Assert.InRange(Math.Abs(speed.ResultNumber - 5d * (1d + escort + over)), 0d, 0.00001d);
        }

        [Theory]
        [InlineData(42)]
        [InlineData(17)]
        public void InventoryTransfersAndUndoKeepFoodCapacityAndLaborConsistent(int seed)
        {
            var settlement = Empty<Settlement>();
            var settlementParty = Empty<PartyBase>();
            var town = Empty<Town>();
            Set(settlement, "Party", settlementParty);
            Set(settlementParty, "Settlement", settlement);
            AccessTools.Field(typeof(SettlementComponent), "_owner").SetValue(town, settlementParty);
            Set(settlementParty, "ItemRoster", new ItemRoster());
            AccessTools.Field(typeof(Settlement), "Stash").SetValue(settlement, new ItemRoster());
            ItemRoster[] inventories = { _base.ItemRoster, settlement.Stash, settlement.ItemRoster };
            int[] expected = { 150, 70, 80 };
            for (int i = 0; i < inventories.Length; i++) inventories[i].AddToCounts(_slave, expected[i]);
            _party.IsActive = true;
            PatchCapacity(); // Controlled native capacity = 100; actual inventory getters/patch remain live.
            var model = new DefaultMobilePartyFoodConsumptionModel();
            var random = new Random(seed);
            for (int day = 1; day <= 365; day++)
            {
                _settings.EnableSlaveEconomy = day % 11 != 0;
                _settings.SlaveFoodConsumptionPerUnit = new[] { 0f, 0.05f, 0.2f }[day % 3];
                int source = random.Next(3), target = (source + random.Next(1, 3)) % 3;
                int count = random.Next(expected[source] + 1);
                Move(source, target, count);
                Verify();
                if (day % 7 == 0)
                {
                    Move(target, source, count);
                    Verify();
                }

                void Move(int from, int to, int number)
                {
                    inventories[from].AddToCounts(_slave, -number);
                    inventories[to].AddToCounts(_slave, number);
                    expected[from] -= number;
                    expected[to] += number;
                }

                void Verify()
                {
                    string context = $"Seed {seed}, day {day}";
                    Assert.True(expected.Sum() == 300, context + ": inventory accounting diverged");
                    for (int i = 0; i < inventories.Length; i++)
                        Assert.True(_behavior.GetSlaveCount(inventories[i]) == expected[i], context + ": roster transfer diverged");
                    Assert.True(_behavior.GetSlaveCountForTown(town) == expected[2], context + ": stash or party granted market labor");
                    float rate = _settings.EnableSlaveEconomy ? _settings.SlaveFoodConsumptionPerUnit : 0;
                    var townFood = new ExplainedNumber(20f);
                    townFood.AddFactor(0.5f); // Settlement upkeep must not inherit this native factor.
                    B1071_SlaveFoodPatch.Postfix(town, ref townFood);
                    Assert.True(Math.Abs(townFood.ResultNumber - (30 - (expected[1] + expected[2]) * rate)) < 0.0001f,
                        context + ": settlement rations diverged");
                    var partyFood = new ExplainedNumber(-10f);
                    B1071_SlavePartyFoodPatch.Postfix(model, _party, ref partyFood);
                    Assert.True(Math.Abs(townFood.ResultNumber + partyFood.ResultNumber - (20 - 300 * rate)) < 0.0001f,
                        context + ": transfers lost or duplicated food charges");
                    partyFood.AddFactor(-0.5f); // Party upkeep is subject to later native food modifiers.
                    Assert.True(Math.Abs(partyFood.ResultNumber - (-10 - expected[0] * rate) * 0.5f) < 0.0001f,
                        context + ": party rations diverged");
                    int reserved = _settings.EnableSlaveEconomy ? expected[0] : 0;
                    Assert.True(_base.PrisonerSizeLimit == Math.Max(0, 100 - reserved), context + ": stale reserved capacity");
                }
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(17)]
        [InlineData(53)]
        public void SettlementDecayKeepsIndependentFractionsAcrossCheckpoints(int interval)
        {
            _settings.ShowPlayerDebugMessages = false;
            _settings.SlaveCapPerProsperity = 0; // Isolate attrition from manumission/routing.
            var resumed = new B1071_SlaveEconomyBehavior();
            AccessTools.Field(typeof(B1071_SlaveEconomyBehavior), "_slaveItem").SetValue(resumed, _slave);
            Settlement MakeTown(string id)
            {
                var settlement = Empty<Settlement>();
                settlement.StringId = id;
                var party = Empty<PartyBase>();
                var town = Empty<Town>();
                Set(settlement, "Party", party);
                Set(party, "Settlement", settlement);
                Set(settlement, "SettlementComponent", town);
                AccessTools.Field(typeof(Settlement), "Town").SetValue(settlement, town);
                AccessTools.Field(typeof(SettlementComponent), "_owner").SetValue(town, party);
                Set(party, "ItemRoster", new ItemRoster());
                AccessTools.Field(typeof(Settlement), "Stash").SetValue(settlement, new ItemRoster());
                settlement.Stash.AddToCounts(_slave, 100);
                Assert.True(settlement.IsTown);
                Assert.Same(town, settlement.Town);
                return settlement;
            }
            var live = new[] { MakeTown("town_1"), MakeTown("town_11") };
            var loaded = new[] { MakeTown("town_1"), MakeTown("town_11") };
            int[] counts = { 7, 31 }, sixteenths = { 0, 0 }, acquired = { 7, 31 }, lost = { 0, 0 }, withdrawn = { 0, 0 };
            for (int i = 0; i < 2; i++)
            {
                live[i].ItemRoster.AddToCounts(_slave, counts[i]);
                loaded[i].ItemRoster.AddToCounts(_slave, counts[i]);
            }
            var random = new Random(42);
            var tick = AccessTools.Method(typeof(B1071_SlaveEconomyBehavior), "OnDailyTickSettlement");
            var fractions = AccessTools.Field(typeof(B1071_SlaveEconomyBehavior), "_decayAccumulator");
            for (int day = 1; day <= 365; day++)
            {
                _settings.EnableSlaveEconomy = day % 17 != 0;
                _settings.SlaveDailyDecayPercent = day % 13 == 0 ? 0 : 6.25f; // Exactly 1/16: integer oracle, no rounding ambiguity.
                for (int i = 0; i < 2; i++)
                {
                    int arrivals = day % 5 == 0 ? random.Next(1, 12) : 0;
                    acquired[i] += arrivals;
                    counts[i] += arrivals;
                    live[i].ItemRoster.AddToCounts(_slave, arrivals);
                    loaded[i].ItemRoster.AddToCounts(_slave, arrivals);
                    if (day % 37 == 0)
                    {
                        withdrawn[i] += counts[i];
                        live[i].ItemRoster.Clear();
                        loaded[i].ItemRoster.Clear();
                        counts[i] = 0;
                    }
                    if (_settings.EnableSlaveEconomy && _settings.SlaveDailyDecayPercent > 0 && counts[i] > 0)
                    {
                        sixteenths[i] += counts[i];
                        int loss = sixteenths[i] / 16;
                        sixteenths[i] %= 16;
                        counts[i] -= loss;
                        lost[i] += loss;
                    }
                    tick.Invoke(_behavior, new object[] { live[i] });
                    tick.Invoke(resumed, new object[] { loaded[i] });
                    string context = $"Day {day}, {live[i].StringId}, checkpoint interval {interval}";
                    foreach (var settlement in new[] { live[i], loaded[i] })
                    {
                        Assert.True(settlement.ItemRoster.GetItemNumber(_slave) == counts[i], context + ": market decay diverged");
                        Assert.True(settlement.Stash.GetItemNumber(_slave) == 100, context + ": decay touched stash");
                    }
                    foreach (var behavior in new[] { _behavior, resumed })
                    {
                        var values = (Dictionary<string, float>)fractions.GetValue(behavior);
                        values.TryGetValue(live[i].StringId, out float carry);
                        Assert.True(carry == sixteenths[i] / 16f, context + ": fractional carry diverged");
                    }
                    Assert.True(counts[i] + lost[i] + withdrawn[i] == acquired[i], context + ": count accounting diverged");
                }
                if (day % interval == 0)
                {
                    var saved = new DecayStore(false);
                    resumed.SyncData(saved);
                    resumed = new B1071_SlaveEconomyBehavior();
                    resumed.SyncData(new DecayStore(true, saved.Values));
                    AccessTools.Field(typeof(B1071_SlaveEconomyBehavior), "_slaveItem").SetValue(resumed, _slave);
                }
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MissingOrNullDecaySaveFieldsLoadWithSafeDefaults(bool explicitNull)
        {
            var values = new Dictionary<string, object>();
            if (explicitNull) values["b1071_slaveDecayAccum"] = null!;
            _behavior.SyncData(new DecayStore(true, values));
            var saved = new DecayStore(false);
            _behavior.SyncData(saved);
            Assert.False((bool)saved.Values["b1071_initialStockSeeded"]);
            Assert.Empty((Dictionary<string, float>)saved.Values["b1071_slaveDecayAccum"]);
        }

        [Fact]
        public void DecayCheckpointRetainsSeedingFlagAndDoesNotShareMutableState()
        {
            var fractions = new Dictionary<string, float> { ["town_1"] = 0.25f, ["town_11"] = 0.75f };
            _behavior.SyncData(new DecayStore(true, new Dictionary<string, object>
            {
                ["b1071_initialStockSeeded"] = true,
                ["b1071_slaveDecayAccum"] = fractions
            }));
            fractions.Clear();
            var saved = new DecayStore(false);
            _behavior.SyncData(saved);
            Assert.True((bool)saved.Values["b1071_initialStockSeeded"]);
            Assert.Equal(2, ((Dictionary<string, float>)saved.Values["b1071_slaveDecayAccum"]).Count);
            var live = (Dictionary<string, float>)AccessTools.Field(typeof(B1071_SlaveEconomyBehavior), "_decayAccumulator").GetValue(_behavior);
            live["town_1"] = 0;
            Assert.Equal(0.25f, ((Dictionary<string, float>)saved.Values["b1071_slaveDecayAccum"])["town_1"]);
            Assert.Equal(0.75f, ((Dictionary<string, float>)saved.Values["b1071_slaveDecayAccum"])["town_11"]);
        }

        private sealed class DecayStore : IDataStore
        {
            internal readonly Dictionary<string, object> Values;
            public bool IsLoading { get; }
            public bool IsSaving => !IsLoading;
            internal DecayStore(bool loading, Dictionary<string, object>? values = null)
            {
                IsLoading = loading;
                Values = values ?? new Dictionary<string, object>();
            }
            public bool SyncData<T>(string key, ref T data)
            {
                if (IsSaving) { Values[key] = Copy(data!); return true; }
                if (Values.TryGetValue(key, out var value) && value is T)
                {
                    data = (T)Copy(value);
                    return true;
                }
                data = default!;
                return false;
            }
            // Detached copies prevent shared dictionary references from disguising a broken checkpoint.
            private static object Copy(object value) => value is Dictionary<string, float> map
                ? new Dictionary<string, float>(map) : value;
        }

        private sealed class EscapeRandom : IB1071Random
        {
            private readonly bool _slave;
            internal EscapeRandom(bool slave) { _slave = slave; }
            public int Next(int maxExclusive) => _slave ? 0 : maxExclusive - 1;
            public int Next(int minInclusive, int maxExclusive) => minInclusive + Next(maxExclusive - minInclusive);
            public float RangeFloat(float minInclusive, float maxInclusive) => minInclusive;
            public int RoundRandomized(float value) => (int)value;
        }

        private static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        private static void Set(object target, string name, object value) => AccessTools.PropertySetter(target.GetType(), name).Invoke(target, new[] { value });
        public void Dispose()
        {
            _harmony.UnpatchAll(_harmony.Id);
            B1071_TestHooks.Reset();
            B1071_SlaveEconomyBehavior.Instance = _previous;
        }
    }
}
#endif
