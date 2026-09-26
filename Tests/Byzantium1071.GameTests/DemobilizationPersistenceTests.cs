#if GAME_TESTS_ENABLED
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Byzantium1071.Campaign;
using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using Xunit;

namespace Byzantium1071.GameTests
{
    [Collection(nameof(CastleRecruitmentActionCollection))]
    public sealed class DemobilizationPersistenceTests : IDisposable
    {
        private static Clan? _playerClan;
        private readonly B1071_McmSettings _settings = new B1071_McmSettings();
        private readonly Harmony _harmony = new Harmony("B1071.Tests.DemobilizationPersistence");

        public DemobilizationPersistenceTests()
        {
            B1071_TestHooks.Settings = _settings;
            // Supply settings, clock and player identity; state transitions and SyncData run unchanged.
            _harmony.Patch(AccessTools.Method(typeof(B1071_DemobilizationBehavior), "GetToday"),
                prefix: new HarmonyMethod(typeof(DemobilizationPersistenceTests), nameof(Today)));
            _harmony.Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"),
                prefix: new HarmonyMethod(typeof(DemobilizationPersistenceTests), nameof(PlayerClan)));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(50)]
        public void CompleteStateSurvivesRepeatedDetachedRoundTrips(int checkpoints)
        {
            Store expected = Seed();
            Store snapshot = Seed();
            for (int checkpoint = 0; checkpoint < checkpoints; checkpoint++)
            {
                var behavior = Load(snapshot);
                // The loader must work with detached copies, not references retained by this store.
                foreach (IList? values in snapshot.Values.Values) values?.Clear();
                snapshot = Save(behavior);
                Assert.Equal(expected.Values.Keys.OrderBy(k => k), snapshot.Values.Keys.OrderBy(k => k));
                foreach (var pair in expected.Values)
                    Assert.Equal(pair.Value!.Cast<object>(), snapshot.Values[pair.Key]!.Cast<object>());

                Assert.Equal(3, Invoke(behavior, "CountTrackedSoldiers"));
                Assert.Equal(2, Invoke(behavior, "CountReservedSoldiers"));
                Assert.Equal(7, Invoke(behavior, "CountRegisteredVeterans"));
                Assert.Equal(7, Invoke(behavior, "CountPendingRecallSoldiers"));
                Assert.Equal(9, AccessTools.Field(behavior.GetType(), "_nextRecallOrderId").GetValue(behavior));
                AssertIndividualEntries(behavior, "_serviceCohorts", 3);
                AssertIndividualEntries(behavior, "_transferReserve", 2);
            }
        }

        [Fact]
        public void PlayerOwnedRecallCountIsDerivedFromPreservedBatches()
        {
            _playerClan = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
            _playerClan.StringId = "clan_a";
            var stored = Seed();
            // The legacy aggregate is intentionally stale; provenance must be authoritative.
            stored.Set("pendOwnCounts", 3, 4);
            var saved = Save(Load(stored));
            Assert.Equal(new[] { 1, 0 }, saved.Get<int>("pendOwnCounts"));
            Assert.Equal(new[] { "clan_a", "clan_b", "clan_c" }, saved.Get<string>("pendBatchEmployerClanIds"));
            Assert.Equal(new[] { true, false }, saved.Get<bool>("vetFromPlayer"));
            var again = Save(Load(saved));
            Assert.Equal(new[] { 1, 0 }, again.Get<int>("pendOwnCounts"));
        }

        [Fact]
        public void LegacyGroupedRecordsExpandWithoutLosingHistory()
        {
            var stored = Seed();
            stored.Set("counts", 2, 1, 1);
            stored.Set("reserveCounts", 2, 1);
            stored.Values.Remove("b1071_demob_extensionCounts");
            stored.Values.Remove("b1071_demob_reserveExtensionCounts");
            var behavior = Load(stored);
            var saved = Save(behavior);
            AssertIndividualEntries(behavior, "_serviceCohorts", 4);
            AssertIndividualEntries(behavior, "_transferReserve", 3);
            Assert.Equal(new[] { 1, 1, 1, 1 }, saved.Get<int>("counts"));
            Assert.Equal(new[] { 1, 1, 1, 0 }, saved.Get<int>("extensionCounts"));
            Assert.Equal(new[] { "home_a", "home_a", "home_a", "home_b" }, saved.Get<string>("homeIds"));
            Assert.Equal(new[] { 1, 1, 0 }, saved.Get<int>("reserveExtensionCounts"));
            Assert.Equal(new[] { "clan_a", "clan_a", "clan_b" }, saved.Get<string>("reserveEmployerClanIds"));
            Assert.Equal(new[] { "party_a", "party_a", "party_b" }, saved.Get<string>("reserveSourcePartyIds"));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MissingOrNullSaveFieldsClearPreviouslyLoadedState(bool explicitNull)
        {
            var behavior = Load(Seed());
            var empty = Seed();
            foreach (string key in empty.Values.Keys.ToArray())
                if (explicitNull) empty.Values[key] = null; else empty.Values.Remove(key);
            behavior.SyncData(empty);
            Assert.All(Save(behavior).Values.Values, values => Assert.Empty(values!));
            Assert.Equal(1, AccessTools.Field(behavior.GetType(), "_nextRecallOrderId").GetValue(behavior));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void InvalidFirstRecallHeaderDoesNotStealTheNextOrdersBatches(bool missingSettlement)
        {
            var stored = Seed();
            if (missingSettlement) stored.Set("pendSettlementIds", "", "town_b");
            else stored.Set("pendCounts", 0, 4);
            var behavior = Load(stored);
            var saved = Save(behavior);
            Assert.Equal(new[] { 8 }, saved.Get<int>("pendOrderIds"));
            Assert.Equal(new[] { "town_b" }, saved.Get<string>("pendSettlementIds"));
            Assert.Equal(new[] { 4 }, saved.Get<int>("pendCounts"));
            Assert.Equal(new[] { 400 }, saved.Get<int>("pendGold"));
            Assert.Equal(new[] { 4 }, saved.Get<int>("pendManpower"));
            Assert.Equal(new[] { 1 }, saved.Get<int>("pendBatchesPerOrder"));
            Assert.Equal(new[] { "origin_c" }, saved.Get<string>("pendBatchOriginClanIds"));
            Assert.Equal(new[] { "clan_c" }, saved.Get<string>("pendBatchEmployerClanIds"));
            Assert.Equal(new[] { 4 }, saved.Get<int>("pendBatchCounts"));
        }

        [Theory]
        [InlineData(0, 0, 14)]
        [InlineData(30, 7, 30)]
        [InlineData(7, 45, 45)]
        [InlineData(30, 45, 45)]
        public void ReserveExpiryUsesCurrentPolicyAndSurvivesCheckpoints(int warningDays, int extensionDays, int retention)
        {
            _settings.DemobilizationWarningLeadDays = warningDays;
            _settings.DemobilizationExtensionDays = extensionDays;
            AssertExpiry("CleanupTransferReserve", "_transferReserve", "reserve", 90 + retention,
                "reserveSourcePartyIds", "party_b");
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 1)]
        [InlineData(30, 30)]
        public void VeteranExpiryHonorsBoundaryAndSurvivesCheckpoints(int configuredDays, int retention)
        {
            _settings.DemobilizationVeteranRetentionDays = configuredDays;
            AssertExpiry("CleanupVeteranRegister", "_veteranRegister", "vet", 70 + retention,
                "vetEmployerClanIds", "clan_b");
        }

        private static void AssertExpiry(string method, string field, string prefix, int boundaryDay,
            string identityKey, string survivingIdentity)
        {
            Store initial = Seed();
            var behavior = Load(initial);
            // The first record remains eligible through the final retention day.
            Invoke(behavior, method, boundaryDay);
            AssertSavedListsEqual(initial, Save(behavior));
            behavior = Load(Save(behavior));
            Invoke(behavior, method, boundaryDay + 1);
            Store partial = Save(behavior);
            Assert.Equal(new[] { survivingIdentity }, partial.Get<string>(identityKey));
            foreach (var pair in initial.Values.Where(p => p.Key.StartsWith("b1071_demob_" + prefix)))
                Assert.Equal(new[] { pair.Value![1] }, partial.Values[pair.Key]!.Cast<object>());
            Assert.Single((IDictionary)AccessTools.Field(behavior.GetType(), field).GetValue(behavior));
            AssertSavedListsEqual(initial, partial, prefix);
            behavior = Load(partial);
            Invoke(behavior, method, boundaryDay + 5);
            AssertSavedListsEqual(partial, Save(behavior));
            // The second record was stored five days later; expire it and prune its bucket too.
            behavior = Load(Save(behavior));
            Invoke(behavior, method, boundaryDay + 6);
            Store empty = Save(behavior);
            foreach (var pair in empty.Values.Where(p => p.Key.StartsWith("b1071_demob_" + prefix)))
                Assert.Empty(pair.Value!);
            Assert.Empty((IDictionary)AccessTools.Field(behavior.GetType(), field).GetValue(behavior));
            AssertSavedListsEqual(initial, empty, prefix);
            behavior = Load(empty);
            Invoke(behavior, method, boundaryDay + 6);
            AssertSavedListsEqual(empty, Save(behavior)); // Cleanup is idempotent after reload.
        }

        [Theory]
        [InlineData(104, 1)]
        [InlineData(105, 0)]
        public void RestorationChecksExpiryBeforeReturningAnIndividual(int today, int expectedRestored)
        {
            _settings.DemobilizationWarningLeadDays = 0;
            _settings.DemobilizationExtensionDays = 0; // Fourteen-day minimum reserve retention.
            var behavior = Load(Seed());
            var parties = (IDictionary)AccessTools.Field(behavior.GetType(), "_serviceCohorts").GetValue(behavior);
            var cohorts = (IList)((IDictionary)parties["party_a"]!)["troop_a"]!;
            var restore = AccessTools.Method(behavior.GetType(), "RestoreTransferReserveEntriesForEmployer");
            Assert.Equal(expectedRestored, restore.Invoke(behavior, new object[] { "clan_a", "troop_a", cohorts, 10, today }));
            Assert.Equal(2 + expectedRestored, cohorts.Count);
            if (expectedRestored > 0)
            {
                object restored = cohorts.Cast<object>().Single(e => (int)AccessTools.Field(e.GetType(), "JoinDay").GetValue(e) == 5);
                foreach (var pair in new Dictionary<string, object>
                {
                    ["Count"] = 1, ["ExtensionCount"] = 3, ["HomeId"] = "home_a",
                    ["OriginClanId"] = "origin_a", ["EmployerClanId"] = "clan_a"
                })
                    Assert.Equal(pair.Value, AccessTools.Field(restored.GetType(), pair.Key).GetValue(restored));
            }
            Assert.Equal(0, restore.Invoke(behavior, new object[] { "clan_a", "troop_a", cohorts, 10, today }));
            Store saved = Save(behavior);
            Assert.Equal(new[] { "clan_b" }, saved.Get<string>("reserveEmployerClanIds"));
            Assert.Equal(new[] { "party_b" }, saved.Get<string>("reserveSourcePartyIds"));
            Assert.Equal(3 + expectedRestored, Invoke(behavior, "CountTrackedSoldiers"));
            Assert.Equal(7, Invoke(behavior, "CountRegisteredVeterans"));
            Assert.Equal(7, Invoke(behavior, "CountPendingRecallSoldiers"));
            AssertSavedListsEqual(saved, Save(Load(saved)));
        }

        private static void AssertSavedListsEqual(Store expected, Store actual, string? exceptPrefix = null)
        {
            Assert.Equal(expected.Values.Keys.OrderBy(k => k), actual.Values.Keys.OrderBy(k => k));
            foreach (var pair in expected.Values)
                if (exceptPrefix == null || !pair.Key.StartsWith("b1071_demob_" + exceptPrefix))
                    Assert.Equal(pair.Value!.Cast<object>(), actual.Values[pair.Key]!.Cast<object>());
        }

        private static Store Seed()
        {
            var data = Save(new B1071_DemobilizationBehavior());
            data.IsLoading = true;
            data.Set("partyIds", "party_a", "party_a", "party_b");
            data.Set("troopIds", "troop_a", "troop_a", "troop_b");
            data.Set("joinDays", 12, 12, 16);
            data.Set("counts", 1, 1, 1);
            data.Set("extendedFlags", true, true, false);
            data.Set("extensionCounts", 2, 2, 0);
            data.Set("homeIds", "home_a", "home_a", "home_b");
            data.Set("originClanIds", "origin_a", "origin_a", "origin_b");
            data.Set("employerClanIds", "clan_a", "clan_a", "clan_b");
            data.Set("reserveTroopIds", "troop_a", "troop_b");
            data.Set("reserveJoinDays", 5, 8);
            data.Set("reserveStoredDays", 90, 95);
            data.Set("reserveCounts", 1, 1);
            data.Set("reserveExtendedFlags", true, false);
            data.Set("reserveExtensionCounts", 3, 0);
            data.Set("reserveHomeIds", "home_a", "home_b");
            data.Set("reserveOriginClanIds", "origin_a", "origin_b");
            data.Set("reserveEmployerClanIds", "clan_a", "clan_b");
            data.Set("reserveSourcePartyIds", "party_a", "party_b");
            data.Set("vetSettlementIds", "town_a", "town_b");
            data.Set("vetTroopIds", "troop_a", "troop_b");
            data.Set("vetDischargeDays", 70, 75);
            data.Set("vetCounts", 3, 4);
            data.Set("vetFromPlayer", true, false);
            data.Set("vetOriginClanIds", "origin_a", "origin_b");
            data.Set("vetEmployerClanIds", "clan_a", "clan_b");
            data.Set("pendOrderIds", 3, 8);
            data.Set("pendSettlementIds", "town_a", "town_b");
            data.Set("pendTroopIds", "troop_a", "troop_b");
            data.Set("pendCounts", 3, 4);
            data.Set("pendOrderDays", 90, 95);
            data.Set("pendGold", 300, 400);
            data.Set("pendManpower", 3, 4);
            data.Set("pendOwnCounts", 0, 0);
            data.Set("pendCourier", 1.5f, 2.5f);
            data.Set("pendPosX", 100.5f, 200.5f);
            data.Set("pendPosY", 300.5f, 400.5f);
            data.Set("pendBatchesPerOrder", 2, 1);
            data.Set("pendBatchOriginClanIds", "origin_a", "origin_b", "origin_c");
            data.Set("pendBatchEmployerClanIds", "clan_a", "clan_b", "clan_c");
            data.Set("pendBatchCounts", 1, 2, 4);
            return data;
        }

        private static void AssertIndividualEntries(B1071_DemobilizationBehavior behavior, string field, int expected)
        {
            var outer = (IDictionary)AccessTools.Field(behavior.GetType(), field).GetValue(behavior);
            int count = 0;
            foreach (IDictionary troops in outer.Values)
                foreach (IEnumerable entries in troops.Values)
                    foreach (object entry in entries)
                    {
                        Assert.Equal(1, AccessTools.Field(entry.GetType(), "Count").GetValue(entry));
                        count++;
                    }
            Assert.Equal(expected, count);
        }

        private static B1071_DemobilizationBehavior Load(Store store)
        {
            store.IsLoading = true;
            var behavior = new B1071_DemobilizationBehavior();
            behavior.SyncData(store);
            return behavior;
        }
        private static Store Save(B1071_DemobilizationBehavior behavior)
        {
            var store = new Store();
            behavior.SyncData(store);
            return store;
        }
        private sealed class Store : IDataStore
        {
            internal readonly Dictionary<string, IList?> Values = new Dictionary<string, IList?>();
            public bool IsLoading { get; set; }
            public bool IsSaving => !IsLoading;
            internal void Set<T>(string suffix, params T[] items) => Values["b1071_demob_" + suffix] = new List<T>(items);
            internal IEnumerable<T> Get<T>(string suffix) => Values["b1071_demob_" + suffix]!.Cast<T>();
            public bool SyncData<T>(string key, ref T data)
            {
                if (IsSaving) { Values[key] = Copy((IList)(object)data!); return true; }
                bool exists = Values.TryGetValue(key, out var saved);
                data = saved == null ? default! : (T)(object)Copy(saved);
                return exists;
            }
            private static IList Copy(IList source)
            {
                var copy = (IList)Activator.CreateInstance(source.GetType())!;
                foreach (object item in source) copy.Add(item); // Save lists contain primitives and strings only.
                return copy;
            }
        }
        private static object? Invoke(object target, string method, params object[] args) =>
            AccessTools.Method(target.GetType(), method, args.Select(a => a.GetType()).ToArray()).Invoke(target, args);
        private static bool Today(ref int __result) { __result = 100; return false; }
        private static bool PlayerClan(ref Clan __result) { __result = _playerClan!; return false; }
        public void Dispose()
        {
            _harmony.UnpatchAll(_harmony.Id);
            _playerClan = null;
            B1071_TestHooks.Reset();
        }
    }
}
#endif
