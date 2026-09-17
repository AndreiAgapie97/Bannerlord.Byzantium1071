#if GAME_TESTS_ENABLED
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Patches;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using Xunit;

namespace Byzantium1071.GameTests
{
    [Collection(nameof(CastleRecruitmentActionCollection))]
    public sealed class CastleConformityTests : IDisposable
    {
        private readonly Harmony _harmony = new Harmony("B1071.Tests.CastleConformity");
        private static bool TierPrefix(ref int __result) { __result = 4; return false; }
        private static bool CostPrefix(ref int __result) { __result = 1000; return false; }
        private static bool LevelPrefix(ref int __result) { __result = 21; return false; }
        private static bool NoBattlePrefix() => false;
        private static PartyBase? _queryParty;
        private static TroopRoster? _queryRoster;
        private static bool CastlePrefix(ref bool __result) { __result = true; return false; }
        private static bool PartyPrefix(ref PartyBase __result) { __result = _queryParty!; return false; }
        private static bool RosterPrefix(ref TroopRoster __result) { __result = _queryRoster!; return false; }

        public CastleConformityTests()
        {
            // The game roster and all mod logic run normally. Character campaign/model
            // lookups alone are supplied because no campaign is running in this process.
            _harmony.Patch(AccessTools.PropertyGetter(typeof(CharacterObject), "Tier"),
                prefix: new HarmonyMethod(typeof(CastleConformityTests), nameof(TierPrefix)));
            _harmony.Patch(AccessTools.PropertyGetter(typeof(CharacterObject), "ConformityNeededToRecruitPrisoner"),
                prefix: new HarmonyMethod(typeof(CastleConformityTests), nameof(CostPrefix)));
            _harmony.Patch(AccessTools.PropertyGetter(typeof(PlayerEncounter), "CurrentBattleSimulation"),
                prefix: new HarmonyMethod(typeof(CastleConformityTests), nameof(NoBattlePrefix)));
        }

        public void Dispose()
        {
            _harmony.UnpatchAll(_harmony.Id);
            _queryParty = null;
            _queryRoster = null;
        }

        private static CharacterObject Troop(string id)
        {
            var troop = (CharacterObject)FormatterServices.GetUninitializedObject(typeof(CharacterObject));
            troop.StringId = id;
            return troop;
        }

        private static int Points(TroopRoster roster, CharacterObject troop) => roster.GetElementXp(roster.FindIndexOfTroop(troop));

        [Theory]
        [InlineData(5, 2380, 0, 2980)] // Full deposit carries all existing progress.
        [InlineData(2, 4380, 3000, 1980)] // Partial deposit keeps ready prisoners in the party first.
        [InlineData(2, 2380, 2380, 600)]
        [InlineData(5, 0, 0, 600)]
        public void AiDepositsConserveConformityAndWoundedPrisoners(int moved, int startingXp,
            int remainingXp, int castleXp)
        {
            var troop = Troop("deposit");
            var party = TroopRoster.CreateDummyTroopRoster();
            var castle = TroopRoster.CreateDummyTroopRoster();
            party.AddToCounts(troop, 5, woundedCount: 2);
            castle.AddToCounts(troop, 2, xpChange: 600);
            party.GetTroopRoster(); // Exercise an already-cached roster snapshot.
            party.SetElementXp(0, startingXp);

            B1071_CastlePrisonerDepositPatch.TransferCastlePrisoners(party, castle, troop, moved, 2);

            Assert.Equal(5 - moved, party.GetTroopCount(troop));
            Assert.Equal(2 + moved, castle.GetTroopCount(troop));
            Assert.Equal(remainingXp, moved == 5 ? 0 : Points(party, troop));
            Assert.Equal(castleXp, Points(castle, troop));
            Assert.Equal(startingXp + 600, remainingXp + castleXp);
            Assert.Equal(2, castle.GetElementCopyAtIndex(0).WoundedNumber);
            Assert.Equal(0, party.TotalWounded);
        }

        [Fact]
        public void MenuQueriesSplitReadyAndPendingWithoutDuplicatingPrisoners()
        {
            var castle = (Settlement)FormatterServices.GetUninitializedObject(typeof(Settlement));
            _queryParty = (PartyBase)FormatterServices.GetUninitializedObject(typeof(PartyBase));
            _queryRoster = TroopRoster.CreateDummyTroopRoster();
            _harmony.Patch(AccessTools.PropertyGetter(typeof(Settlement), "IsCastle"),
                prefix: new HarmonyMethod(typeof(CastleConformityTests), nameof(CastlePrefix)));
            _harmony.Patch(AccessTools.PropertyGetter(typeof(Settlement), "Party"),
                prefix: new HarmonyMethod(typeof(CastleConformityTests), nameof(PartyPrefix)));
            _harmony.Patch(AccessTools.PropertyGetter(typeof(PartyBase), "PrisonRoster"),
                prefix: new HarmonyMethod(typeof(CastleConformityTests), nameof(RosterPrefix)));
            var troop = Troop("a");
            _queryRoster.AddToCounts(troop, 5, xpChange: 2380);
            var behavior = new B1071_CastleRecruitmentBehavior();
            Assert.Equal(2, behavior.GetRecruitablePrisoners(castle).Single().Count);
            var pending = behavior.GetPendingPrisoners(castle).Single();
            Assert.Equal(3, pending.Count);
            Assert.Equal(0, pending.DaysHeld);
            Assert.Equal(0, pending.DaysRequired);
            Assert.Equal((2380, 1000), behavior.GetPrisonerConformity(castle, troop));
            B1071_CastleRecruitmentBehavior.ConsumePrisonerConformity(_queryRoster, troop);
            Assert.Equal(1, behavior.GetRecruitablePrisoners(castle).Single().Count);
            Assert.Equal(3, behavior.GetPendingPrisoners(castle).Single().Count);
            Assert.Equal((1380, 1000), behavior.GetPrisonerConformity(castle, troop));
        }

        [Fact]
        public void InstalledVanillaRequirementStillUsesLevelRatherThanTier()
        {
            _harmony.Patch(AccessTools.PropertyGetter(typeof(CharacterObject), "Level"),
                prefix: new HarmonyMethod(typeof(CastleConformityTests), nameof(LevelPrefix)));
            Assert.Equal(719, new DefaultPrisonerRecruitmentCalculationModel().GetConformityNeededToRecruitPrisoner(Troop("level21")));
        }

        [Fact]
        public void RepeatedDailyTicksUseLiveXpAndShareOneBudget()
        {
            var behavior = new B1071_CastleRecruitmentBehavior();
            var roster = TroopRoster.CreateDummyTroopRoster();
            var a = Troop("a"); var b = Troop("b");
            roster.AddToCounts(b, 100); // Reverse roster order must not affect fairness.
            roster.AddToCounts(a, 100);
            for (int day = 0; day < 10; day++) behavior.AdvancePrisonerConformity("castle", roster, 0);
            Assert.Equal(1200, Points(roster, a));
            Assert.Equal(1200, Points(roster, b));
            Assert.Equal(1, B1071_CastleRecruitmentBehavior.ReadyPrisonerCount(roster, a));
            Assert.Equal(1, B1071_CastleRecruitmentBehavior.ReadyPrisonerCount(roster, b));
        }

        [Fact]
        public void NativeRosterRecruitmentConsumesPointsAndArrivalsDoNotBecomeReady()
        {
            var troop = Troop("a"); var roster = TroopRoster.CreateDummyTroopRoster();
            roster.AddToCounts(troop, 3, xpChange: 3000);
            roster.AddToCounts(troop, 40);
            Assert.Equal(3, B1071_CastleRecruitmentBehavior.ReadyPrisonerCount(roster, troop));
            for (int i = 0; i < 3; i++) B1071_CastleRecruitmentBehavior.ConsumePrisonerConformity(roster, troop);
            Assert.Equal(40, roster.GetTroopCount(troop));
            Assert.Equal(0, Points(roster, troop));
            Assert.Equal(0, B1071_CastleRecruitmentBehavior.ReadyPrisonerCount(roster, troop));
        }

        [Fact]
        public void FullRostersAndEmptyCastlesCannotBankProgressForFutureArrivals()
        {
            var behavior = new B1071_CastleRecruitmentBehavior();
            var troop = Troop("a"); var roster = TroopRoster.CreateDummyTroopRoster();
            behavior.AdvancePrisonerConformity("castle", roster, 1);
            roster.AddToCounts(troop, 1, xpChange: 990);
            for (int day = 0; day < 20; day++) behavior.AdvancePrisonerConformity("castle", roster, 1);
            Assert.Equal(1000, Points(roster, troop));
            var saved = new Store(false); behavior.SyncData(saved);
            Assert.Empty((List<string>)saved.Values["b1071_cr_conformityCastles"]);
            roster.AddToCounts(troop, 20);
            behavior.AdvancePrisonerConformity("castle", roster, 1);
            Assert.Equal(1241, Points(roster, troop));
        }

        [Fact]
        public void SaveResumePreservesFractionalBudgetAndRotation()
        {
            var original = new B1071_CastleRecruitmentBehavior();
            var roster = TroopRoster.CreateDummyTroopRoster();
            var troops = new[] { Troop("a"), Troop("b"), Troop("c") };
            foreach (var troop in troops) roster.AddToCounts(troop, 30);
            original.AdvancePrisonerConformity("castle", roster, 1);
            var saved = new Store(false); original.SyncData(saved);
            var resumed = new B1071_CastleRecruitmentBehavior(); resumed.SyncData(new Store(true, saved.Values));
            var copied = TroopRoster.CreateDummyTroopRoster();
            for (int i = 0; i < roster.Count; i++) copied.Add(roster.GetElementCopyAtIndex(i));
            for (int day = 0; day < 14; day++)
            {
                original.AdvancePrisonerConformity("castle", roster, 1);
                resumed.AdvancePrisonerConformity("castle", copied, 1);
                foreach (var troop in troops) Assert.Equal(Points(roster, troop), Points(copied, troop));
            }
            Assert.Equal(3618, troops.Sum(t => Points(copied, t)));
        }

        [Fact]
        public void LegacyMigrationIsConsumedOnceAndKeepsReadyAndPartialProgress()
        {
            var a = Troop("ready"); var b = Troop("pending");
            var roster = TroopRoster.CreateDummyTroopRoster();
            roster.AddToCounts(a, 40); roster.AddToCounts(b, 40);
            var behavior = new B1071_CastleRecruitmentBehavior();
            behavior.SyncData(new Store(true, new Dictionary<string, object> {
                ["b1071_cr_prisonerCastles"] = new List<string> { "castle", "castle" },
                ["b1071_cr_prisonerTroops"] = new List<string> { "ready", "pending" },
                ["b1071_cr_prisonerDays"] = new List<int> { 1000, 1 }
            }));
            behavior.MigrateLegacyPrisonerProgress("castle", roster);
            Assert.Equal(40, B1071_CastleRecruitmentBehavior.ReadyPrisonerCount(roster, a));
            Assert.Equal(1000 / behavior.GetRequiredDaysForTier(4), Points(roster, b));
            B1071_CastleRecruitmentBehavior.ConsumePrisonerConformity(roster, a);
            var saved = new Store(false); behavior.SyncData(saved);
            Assert.Empty((List<int>)saved.Values["b1071_cr_prisonerDays"]);
            var resumed = new B1071_CastleRecruitmentBehavior(); resumed.SyncData(new Store(true, saved.Values));
            resumed.MigrateLegacyPrisonerProgress("castle", roster);
            roster.AddToCounts(a, 1);
            Assert.Equal(39, B1071_CastleRecruitmentBehavior.ReadyPrisonerCount(roster, a));
        }

        [Theory]
        [InlineData("TryRecruitPrisoner")]
        [InlineData("AiAutoRecruit")]
        [InlineData("GarrisonAbsorbPrisoners")]
        public void EveryRecruitmentPathConsumesTheTestedNativeConformity(string method)
        {
            var type = typeof(B1071_CastleRecruitmentBehavior);
            Assert.Contains(PatchProcessor.GetOriginalInstructions(AccessTools.Method(type, method)),
                i => Equals(i.operand, AccessTools.Method(type, "ConsumePrisonerConformity")));
        }

        private sealed class Store : IDataStore
        {
            internal readonly Dictionary<string, object> Values;
            public bool IsLoading { get; }
            public bool IsSaving => !IsLoading;
            internal Store(bool loading, Dictionary<string, object>? values = null)
            {
                IsLoading = loading;
                Values = values ?? new Dictionary<string, object>();
            }
            public bool SyncData<T>(string key, ref T data)
            {
                if (!IsLoading) { Values[key] = data!; return true; }
                if (Values.TryGetValue(key, out var value) && value is T typed) { data = typed; return true; }
                data = default!; // Older saves may lack all new fields.
                return false;
            }
        }
    }
}
#endif
