#if GAME_TESTS_ENABLED
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Byzantium1071.Campaign;
using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using Xunit;

namespace Byzantium1071.GameTests
{
    [Collection(nameof(CastleRecruitmentActionCollection))]
    public sealed class DevastationBehaviorTests : IDisposable
    {
        private const string SaveKey = "b1071_villageDevastation";
        private readonly B1071_McmSettings _settings = new B1071_McmSettings
        {
            EnableFrontierDevastation = true,
            DevastationPerRaid = 25f,
            DevastationDecayPerDay = 0.5f,
            TelemetryDebugLogs = false
        };

        public DevastationBehaviorTests() => B1071_TestHooks.Settings = _settings;

        [Theory]
        [InlineData(1)]
        [InlineData(17)]
        [InlineData(53)]
        public void RaidRecoveryMatchesIndependentVillagesAcrossCheckpoints(int interval)
        {
            var live = new B1071_DevastationBehavior();
            var resumed = new B1071_DevastationBehavior();
            var places = new[] { Place("village_A1"), Place("village_A11") };
            int[] halfPoints = { 0, 0 };
            for (int day = 1; day <= 365; day++)
            {
                _settings.EnableFrontierDevastation = day % 13 != 0;
                for (int i = 0; i < places.Length; i++)
                {
                    Village village = places[i].Village;
                    if (day <= 50 && (day + i) % 11 == 0)
                    {
                        // Repeated raids deliberately hit the cap, including from an empty save.
                        for (int raid = 0; raid < 5; raid++)
                        {
                            Invoke(live, "OnVillageLooted", village);
                            Invoke(resumed, "OnVillageLooted", village);
                            if (_settings.EnableFrontierDevastation)
                                halfPoints[i] = Math.Min(200, halfPoints[i] + 50);
                        }
                    }
                    var state = (day + i) % 7 == 0 ? Village.VillageStates.Looted
                        : (day + i) % 5 == 0 ? Village.VillageStates.BeingRaided : Village.VillageStates.Normal;
                    SetState(village, state);
                    int version = live.ChangeVersion;
                    bool decays = _settings.EnableFrontierDevastation && state == Village.VillageStates.Normal && halfPoints[i] > 0;
                    if (decays) halfPoints[i]--;
                    Invoke(live, "OnDailyTickSettlement", places[i]);
                    Invoke(resumed, "OnDailyTickSettlement", places[i]);
                    Assert.Equal(version + (decays ? 1 : 0), live.ChangeVersion);
                    // Check both villages after each tick to detect cross-settlement mutation.
                    for (int p = 0; p < places.Length; p++)
                    {
                        Assert.True(live.GetDevastation(places[p].Village) == halfPoints[p] / 2f,
                            $"Day {day}, village {p}: live devastation diverged");
                        Assert.True(resumed.GetDevastation(places[p].Village) == halfPoints[p] / 2f,
                            $"Day {day}, village {p}: checkpoint devastation diverged");
                    }
                }
                var expected = new Dictionary<string, float>();
                for (int i = 0; i < places.Length; i++)
                    if (halfPoints[i] > 0) expected[places[i].StringId] = halfPoints[i] / 2f;
                Assert.Equal(expected.OrderBy(e => e.Key), Save(live).OrderBy(e => e.Key));
                Assert.Equal(expected.OrderBy(e => e.Key), Save(resumed).OrderBy(e => e.Key));
                if (day % interval == 0)
                {
                    var stored = Save(resumed);
                    resumed = new B1071_DevastationBehavior();
                    resumed.SyncData(new Store(true, stored));
                    stored.Clear(); // The load fixture must not share mutable state with its source.
                }
            }
            Assert.Empty(Save(live));
            Assert.Empty(Save(resumed));
        }

        [Theory]
        [InlineData(Village.VillageStates.Normal, 9.5f)]
        [InlineData(Village.VillageStates.Looted, 10f)]
        [InlineData(Village.VillageStates.BeingRaided, 10f)]
        [InlineData(Village.VillageStates.ForcedForSupplies, 10f)]
        [InlineData(Village.VillageStates.ForcedForVolunteers, 10f)]
        public void OnlyNormalVillagesRecover(Village.VillageStates state, float expected)
        {
            var behavior = new B1071_DevastationBehavior();
            var place = Place("village");
            behavior.SyncData(new Store(true, new Dictionary<string, float> { [place.StringId] = 10f }));
            SetState(place.Village, state);
            Invoke(behavior, "OnDailyTickSettlement", place);
            Assert.Equal(expected, behavior.GetDevastation(place.Village));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MissingOrNullSaveCanRecoverFromItsFirstRaid(bool explicitNull)
        {
            var behavior = new B1071_DevastationBehavior();
            var place = Place("new_village");
            behavior.SyncData(new Store(true, null, explicitNull));
            Invoke(behavior, "OnDailyTickSettlement", place);
            Assert.Equal(0f, behavior.GetDevastation(place.Village));
            Assert.Empty(Save(behavior));
            Invoke(behavior, "OnVillageLooted", place.Village);
            Invoke(behavior, "OnDailyTickSettlement", place);
            Assert.Equal(24.5f, behavior.GetDevastation(place.Village));
        }

        private static Dictionary<string, float> Save(B1071_DevastationBehavior behavior)
        {
            var store = new Store(false);
            behavior.SyncData(store);
            return store.Values!;
        }

        private sealed class Store : IDataStore
        {
            internal Dictionary<string, float>? Values;
            private readonly bool _exists;
            public bool IsLoading { get; }
            public bool IsSaving => !IsLoading;
            internal Store(bool loading, Dictionary<string, float>? values = null, bool exists = true)
            { IsLoading = loading; Values = values; _exists = exists; }
            public bool SyncData<T>(string key, ref T data)
            {
                Assert.Equal(SaveKey, key);
                if (IsSaving) Values = new Dictionary<string, float>((Dictionary<string, float>)(object)data!);
                else data = Values == null ? default! : (T)(object)new Dictionary<string, float>(Values);
                return IsSaving || _exists;
            }
        }

        private static Settlement Place(string id)
        {
            var place = Empty<Settlement>();
            place.StringId = id;
            var party = Empty<PartyBase>();
            var village = Empty<Village>();
            AccessTools.PropertySetter(typeof(Settlement), "Party").Invoke(place, new object[] { party });
            AccessTools.PropertySetter(typeof(PartyBase), "Settlement").Invoke(party, new object[] { place });
            AccessTools.PropertySetter(typeof(Settlement), "SettlementComponent").Invoke(place, new object[] { village });
            AccessTools.Field(typeof(Settlement), "Village").SetValue(place, village);
            AccessTools.Field(typeof(SettlementComponent), "_owner").SetValue(village, party);
            return place;
        }

        // Bypass the native setter's campaign event dispatch; invoke the mod handlers explicitly.
        private static void SetState(Village village, Village.VillageStates state) =>
            AccessTools.Field(typeof(Village), "_villageState").SetValue(village, state);
        private static void Invoke(object target, string method, object argument) =>
            AccessTools.Method(target.GetType(), method).Invoke(target, new[] { argument });
        private static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        public void Dispose() => B1071_TestHooks.Reset();
    }
}
#endif
