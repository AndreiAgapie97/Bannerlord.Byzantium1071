using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Byzantium1071.Campaign.Behaviors;
using TaleWorlds.CampaignSystem.MapEvents;
using Xunit;

namespace Byzantium1071.GameTests
{
    public sealed class DemobilizationServiceContractTests
    {
        [Theory]
        [InlineData("CohortEntry")]
        [InlineData("TransferReserveEntry")]
        [InlineData("VeteranEntry")]
        public void ServiceRecordsCarryOriginAndEmployerProvenance(string nestedTypeName)
        {
            Type entryType = GetNested(nestedTypeName);

            Assert.NotNull(entryType.GetField("OriginClanId", BindingFlags.Instance | BindingFlags.Public));
            Assert.NotNull(entryType.GetField("EmployerClanId", BindingFlags.Instance | BindingFlags.Public));
        }

        [Fact]
        public void TransferReserveIsPartitionedByEmployerBeforeTroopType()
        {
            FieldInfo reserve = typeof(B1071_DemobilizationBehavior).GetField(
                "_transferReserve", BindingFlags.Instance | BindingFlags.NonPublic)!;

            Assert.NotNull(reserve);
            Assert.Equal(typeof(Dictionary<,>), reserve.FieldType.GetGenericTypeDefinition());
            Type nested = reserve.FieldType.GetGenericArguments()[1];
            Assert.Equal(typeof(Dictionary<,>), nested.GetGenericTypeDefinition());
        }

        [Fact]
        public void PendingRecallUsesOrderedBatchesInsteadOfAStoredPlayerCount()
        {
            Type pending = GetNested("PendingRecallEntry");
            FieldInfo batches = pending.GetField("Batches", BindingFlags.Instance | BindingFlags.Public)!;

            Assert.NotNull(batches);
            Assert.Equal(typeof(List<>), batches.FieldType.GetGenericTypeDefinition());
            Assert.Null(pending.GetField("PlayerOwnedCount", BindingFlags.Instance | BindingFlags.Public));
            Assert.NotNull(pending.GetField("LegacyPlayerOwnedCount", BindingFlags.Instance | BindingFlags.Public));
        }

        [Fact]
        public void BattleCasualtyContractExposesDiedInBattleRoster()
        {
            PropertyInfo died = typeof(MapEventParty).GetProperty("DiedInBattle", BindingFlags.Instance | BindingFlags.Public)!;

            Assert.NotNull(died);
            Assert.NotNull(died.PropertyType.GetMethod("GetTroopRoster", BindingFlags.Instance | BindingFlags.Public));
        }

        [Fact]
        public void RecallBatchDeliveryIsFifoAndScatterTiesRemoveOldestBatches()
        {
            var behavior = new B1071_DemobilizationBehavior();
            object delivery = CreatePendingRecall();
            AddRecallBatch(delivery, "origin_a", "employer_a", 2);
            AddRecallBatch(delivery, "origin_b", "employer_b", 2);

            var deliveredEmployers = new List<string>();
            foreach (object batch in (IEnumerable)InvokeInstance(behavior, "TakeRecallBatches", delivery, 3)!)
                deliveredEmployers.Add((string)GetField(batch, "EmployerClanId")!);

            Assert.Equal(new[] { "employer_a", "employer_b" }, deliveredEmployers);
            Assert.Equal(1, (int)GetField(delivery, "Count")!);
            Assert.Equal("employer_b", (string)GetField(RecallBatches(delivery)[0]!, "EmployerClanId")!);

            object scattering = CreatePendingRecall();
            AddRecallBatch(scattering, "origin_a", "employer_a", 1);
            AddRecallBatch(scattering, "origin_b", "employer_b", 1);
            AddRecallBatch(scattering, "origin_c", "employer_c", 1);

            int scattered = (int)InvokeInstance(behavior, "ScatterRecallBatches", scattering, 2)!;

            Assert.Equal(2, scattered);
            Assert.Equal(1, (int)GetField(scattering, "Count")!);
            Assert.Equal("employer_c", (string)GetField(RecallBatches(scattering)[0]!, "EmployerClanId")!);
        }

        [Fact]
        public void VeteranRecruitmentPolicyProtectsPlayerAndAiEqually()
        {
            Type accessType = GetNested("VeteranAccess");
            object ownEmployerOnly = Enum.Parse(accessType, "OwnEmployerOnly");
            object all = Enum.Parse(accessType, "All");
            object denied = Enum.Parse(accessType, "Denied");
            object playerVeteran = CreateVeteran("player_clan");
            object aiVeteran = CreateVeteran("ai_clan");

            // The policy receives no player-specific information: equal ladder results give
            // equal access to a player clan and an AI clan.
            object playerDefaultAccess = InvokeStatic("ApplyVeteranRecruitmentPolicy", false, true, true, false)!;
            object aiDefaultAccess = InvokeStatic("ApplyVeteranRecruitmentPolicy", false, true, true, false)!;
            object playerCrossClanAccess = InvokeStatic("ApplyVeteranRecruitmentPolicy", false, true, true, true)!;
            object aiCrossClanAccess = InvokeStatic("ApplyVeteranRecruitmentPolicy", false, true, true, true)!;

            Assert.Equal(ownEmployerOnly, playerDefaultAccess);
            Assert.Equal(playerDefaultAccess, aiDefaultAccess);
            Assert.Equal(all, playerCrossClanAccess);
            Assert.Equal(playerCrossClanAccess, aiCrossClanAccess);
            Assert.Equal(ownEmployerOnly, InvokeStatic("ApplyVeteranRecruitmentPolicy", false, true, false, true));
            Assert.Equal(denied, InvokeStatic("ApplyVeteranRecruitmentPolicy", true, true, true, true));

            Assert.True((bool)InvokeStatic("Matches", playerVeteran, ownEmployerOnly, "player_clan")!);
            Assert.False((bool)InvokeStatic("Matches", playerVeteran, ownEmployerOnly, "ai_clan")!);
            Assert.True((bool)InvokeStatic("Matches", aiVeteran, ownEmployerOnly, "ai_clan")!);
            Assert.True((bool)InvokeStatic("Matches", aiVeteran, all, "player_clan")!);
        }

        [Fact]
        public void TransferReserveRestoreUsesOnlyTheCurrentEmployersBucket()
        {
            var behavior = new B1071_DemobilizationBehavior();

            IList firstClan = (IList)InvokeInstance(behavior, "GetOrCreateReserveEntries", "clan_a", "troop")!;
            IList secondClan = (IList)InvokeInstance(behavior, "GetOrCreateReserveEntries", "clan_b", "troop")!;
            firstClan.Add(CreateTransferReserve(joinDay: 2, storedDay: 9, count: 2, origin: "origin_a", employer: "clan_a"));
            secondClan.Add(CreateTransferReserve(joinDay: 1, storedDay: 9, count: 3, origin: "origin_b", employer: "clan_b"));
            IList restored = CreateCohortList();

            int restoredCount = (int)InvokeInstance(
                behavior, "RestoreTransferReserveEntriesForEmployer", "clan_a", "troop", restored, 2, 10)!;

            Assert.Equal(2, restoredCount);
            Assert.Equal(2, restored.Count);
            foreach (object cohort in restored)
                Assert.Equal("clan_a", (string)GetField(cohort, "EmployerClanId")!);

            IDictionary reserve = (IDictionary)GetField(behavior, "_transferReserve")!;
            Assert.False(reserve.Contains("clan_a"));
            Assert.True(reserve.Contains("clan_b"));
            Assert.Equal(3, (int)GetField(secondClan[0]!, "Count")!);
        }

        [Fact]
        public void RecallCancellationRestoresEachBatchUnderItsPriorEmployer()
        {
            var behavior = new B1071_DemobilizationBehavior();
            object pending = CreatePendingRecall();
            SetField(pending, "SettlementId", "settlement");
            SetField(pending, "TroopId", "troop");
            AddRecallBatch(pending, "origin_a", "employer_a", 2);
            AddRecallBatch(pending, "origin_b", "employer_b", 3);

            InvokeInstance(behavior, "RestoreCancelledRecallBatches", pending, 10);

            IList entries = VeteranEntries(behavior, "settlement", "troop");
            Assert.Equal(2, entries.Count);
            Assert.Equal("employer_a", (string)GetField(entries[0]!, "EmployerClanId")!);
            Assert.Equal("employer_b", (string)GetField(entries[1]!, "EmployerClanId")!);
        }

        [Fact]
        public void DischargeRegistrationPreservesEmployerAndNormalizesOriginBeforeMerge()
        {
            var behavior = new B1071_DemobilizationBehavior();

            InvokeInstance(behavior, "AddVeteransToRegister", new object[]
            {
                "settlement", "troop", 2, 10, null!, "employer"
            });
            InvokeInstance(behavior, "AddVeteransToRegister", "settlement", "troop", 3, 10, string.Empty, "employer");

            IList entries = VeteranEntries(behavior, "settlement", "troop");
            object entry = Assert.Single((IEnumerable)entries)!;
            Assert.Equal(5, (int)GetField(entry, "Count")!);
            Assert.Equal(string.Empty, (string)GetField(entry, "OriginClanId")!);
        }

        [Fact]
        public void UpgradeCarryoverPreservesOriginAndEmployerProvenance()
        {
            IDictionary cohorts = CreateCohortDictionary();
            IList source = CreateCohortList();
            source.Add(CreateCohort(joinDay: 4, count: 2, origin: "origin_a", employer: "employer_a"));
            source.Add(CreateCohort(joinDay: 8, count: 1, origin: "origin_b", employer: "employer_b"));
            cohorts.Add("source", source);

            int moved = (int)InvokeStatic("MoveOldestCohorts", cohorts, "source", "upgraded", 1, 0, 10)!;

            Assert.Equal(1, moved);
            IList upgraded = (IList)cohorts["upgraded"]!;
            Assert.Equal("origin_a", (string)GetField(upgraded[0]!, "OriginClanId")!);
            Assert.Equal("employer_a", (string)GetField(upgraded[0]!, "EmployerClanId")!);
        }

        [Fact]
        public void NonDeathShrinkageBanksCohortsWhileConfirmedDeathsDeleteThem()
        {
            var behavior = new B1071_DemobilizationBehavior();
            IDictionary cohorts = CreateCohortDictionary();
            IList source = CreateCohortList();
            source.Add(CreateCohort(joinDay: 4, count: 2, origin: "origin_a", employer: string.Empty));
            cohorts.Add("troop", source);

            int banked = (int)InvokeInstance(
                behavior, "BankMissingCohorts", cohorts, "troop", 1, 10, "clan_a", "party_a")!;

            Assert.Equal(1, banked);
            Assert.Equal(1, (int)GetField(source[0]!, "Count")!);
            IDictionary reserve = (IDictionary)GetField(behavior, "_transferReserve")!;
            IList reserved = (IList)((IDictionary)reserve["clan_a"]!)["troop"]!;
            Assert.Equal(1, (int)GetField(reserved[0]!, "Count")!);
            Assert.Equal("party_a", (string)GetField(reserved[0]!, "SourcePartyId")!);

            Assert.Equal(1, (int)GetField(source[0]!, "Count")!);
            InvokeStatic("RemoveOldestCohorts", cohorts, "troop", 1);
            Assert.Empty((IEnumerable)source);
            Assert.Single((IEnumerable)reserved);
        }

        private static object CreatePendingRecall()
        {
            object pending = Activator.CreateInstance(GetNested("PendingRecallEntry"), nonPublic: true)!;
            SetField(pending, "LegacyPlayerOwnedCount", -1);
            return pending;
        }

        private static void AddRecallBatch(object pending, string origin, string employer, int count)
        {
            object batch = Activator.CreateInstance(GetNested("RecallBatch"), nonPublic: true)!;
            SetField(batch, "OriginClanId", origin);
            SetField(batch, "EmployerClanId", employer);
            SetField(batch, "Count", count);
            RecallBatches(pending).Add(batch);
        }

        private static IList RecallBatches(object pending) => (IList)GetField(pending, "Batches")!;

        private static IList VeteranEntries(B1071_DemobilizationBehavior behavior, string settlementId, string troopId)
        {
            IDictionary register = (IDictionary)GetField(behavior, "_veteranRegister")!;
            IDictionary troops = (IDictionary)register[settlementId]!;
            return (IList)troops[troopId]!;
        }

        private static object CreateVeteran(string employer)
        {
            object veteran = Activator.CreateInstance(GetNested("VeteranEntry"), nonPublic: true)!;
            SetField(veteran, "Count", 1);
            SetField(veteran, "EmployerClanId", employer);
            return veteran;
        }

        private static IDictionary CreateCohortDictionary()
        {
            Type cohortType = GetNested("CohortEntry");
            Type listType = typeof(List<>).MakeGenericType(cohortType);
            Type dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(string), listType);
            return (IDictionary)Activator.CreateInstance(dictionaryType)!;
        }

        private static IList CreateCohortList()
        {
            Type listType = typeof(List<>).MakeGenericType(GetNested("CohortEntry"));
            return (IList)Activator.CreateInstance(listType)!;
        }

        private static object CreateCohort(int joinDay, int count, string origin, string employer)
        {
            object cohort = Activator.CreateInstance(GetNested("CohortEntry"), nonPublic: true)!;
            SetField(cohort, "JoinDay", joinDay);
            SetField(cohort, "Count", count);
            SetField(cohort, "OriginClanId", origin);
            SetField(cohort, "EmployerClanId", employer);
            return cohort;
        }

        private static object CreateTransferReserve(int joinDay, int storedDay, int count, string origin, string employer)
        {
            object entry = Activator.CreateInstance(GetNested("TransferReserveEntry"), nonPublic: true)!;
            SetField(entry, "JoinDay", joinDay);
            SetField(entry, "StoredDay", storedDay);
            SetField(entry, "Count", count);
            SetField(entry, "OriginClanId", origin);
            SetField(entry, "EmployerClanId", employer);
            return entry;
        }

        private static object? InvokeInstance(object target, string methodName, params object[] arguments)
        {
            MethodInfo? method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return method!.Invoke(target, arguments);
        }

        private static object? InvokeStatic(string methodName, params object[] arguments)
        {
            MethodInfo? method = typeof(B1071_DemobilizationBehavior).GetMethod(
                methodName, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return method!.Invoke(null, arguments);
        }

        private static object? GetField(object target, string fieldName)
        {
            FieldInfo? field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return field!.GetValue(target);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo? field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field!.SetValue(target, value);
        }

        private static Type GetNested(string name)
        {
            Type? type = typeof(B1071_DemobilizationBehavior).GetNestedType(name, BindingFlags.NonPublic);
            Assert.NotNull(type);
            return type!;
        }
    }
}
