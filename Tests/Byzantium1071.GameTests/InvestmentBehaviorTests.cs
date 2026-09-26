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
    public sealed class InvestmentBehaviorTests : IDisposable
    {
        private readonly B1071_McmSettings _settings = new B1071_McmSettings();
        public InvestmentBehaviorTests() => B1071_TestHooks.Settings = _settings;

        [Theory]
        [InlineData(false, 1)]
        [InlineData(false, 17)]
        [InlineData(true, 1)]
        [InlineData(true, 17)]
        public void SettlementTicksExpireOnlyTheirOwnInvestmentsAcrossCheckpoints(bool village, int interval)
        {
            CampaignBehaviorBase live = CreateBehavior(village), resumed = CreateBehavior(village);
            var places = new[] { Place("place_A1", village), Place("place_A11", village) };
            string daysKey = village ? "b1071_villageInvestDays" : "b1071_townInvestDays";
            string bonusKey = village ? "b1071_villageInvestHearth" : "b1071_townInvestProsperity";
            var durations = new Dictionary<string, float>();
            var bonuses = new Dictionary<string, float>();
            int[] lifetimes = { 1, 3, 40 };
            foreach (var place in places)
                for (int investor = 0; investor < lifetimes.Length; investor++)
                {
                    string key = place.StringId + "_lord_" + investor;
                    durations[key] = lifetimes[investor];
                    bonuses[key] = investor + 1;
                }
            var initial = new Dictionary<string, Dictionary<string, float>> { [daysKey] = durations, [bonusKey] = bonuses };
            live.SyncData(new Store(true, initial));
            resumed.SyncData(new Store(true, initial));
            durations.Clear(); // Loading must use detached state in this fixture.
            bonuses.Clear();
            int[] ticks = { 0, 0 };
            var tick = AccessTools.Method(live.GetType(), "OnDailyTickSettlement");
            for (int day = 1; day <= 120; day++)
            {
                bool enabled = day % 7 != 0;
                _settings.EnableTownInvestment = _settings.EnableVillageInvestment = enabled;
                for (int i = 0; i < places.Length; i++)
                {
                    // Different schedules expose accidentally ticking every settlement's dictionary.
                    if (i == 1 && day % 2 != 0) continue;
                    if (enabled) ticks[i]++;
                    tick.Invoke(live, new object[] { places[i] });
                    tick.Invoke(resumed, new object[] { places[i] });
                    for (int p = 0; p < places.Length; p++)
                    {
                        int expected = Enumerable.Range(0, 3).Where(n => lifetimes[n] > ticks[p]).Sum(n => n + 1);
                        Assert.True(Bonus(live, places[p], village) == expected, $"Day {day}, {places[p].StringId}: live bonus diverged");
                        Assert.True(Bonus(resumed, places[p], village) == expected, $"Day {day}, {places[p].StringId}: checkpoint bonus diverged");
                    }
                }
                var expectedDays = new Dictionary<string, float>();
                for (int p = 0; p < places.Length; p++)
                    for (int n = 0; n < 3; n++)
                        if (lifetimes[n] > ticks[p]) expectedDays[places[p].StringId + "_lord_" + n] = lifetimes[n] - ticks[p];
                foreach (var behavior in new[] { live, resumed })
                {
                    var saved = new Store(false);
                    behavior.SyncData(saved);
                    Assert.Equal(expectedDays.OrderBy(e => e.Key), saved.Values[daysKey].OrderBy(e => e.Key));
                    Assert.Equal(expectedDays.Keys.OrderBy(k => k), saved.Values[bonusKey].Keys.OrderBy(k => k));
                }
                if (day % interval == 0)
                {
                    var saved = new Store(false);
                    resumed.SyncData(saved);
                    resumed = CreateBehavior(village);
                    resumed.SyncData(new Store(true, saved.Values));
                }
            }
            Assert.All(places, place => Assert.Equal(0f, Bonus(resumed, place, village)));
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void MissingAndNullSaveDictionariesAllowQueriesAndTicks(bool village, bool explicitNull)
        {
            var behavior = CreateBehavior(village);
            var stored = new Store(false);
            behavior.SyncData(stored);
            foreach (string key in stored.Values.Keys.ToArray())
                if (explicitNull) stored.Values[key] = null!;
                else stored.Values.Remove(key);
            behavior.SyncData(new Store(true, stored.Values));
            _settings.EnableTownInvestment = _settings.EnableVillageInvestment = true;
            var place = Place("empty", village);
            AccessTools.Method(behavior.GetType(), "OnDailyTickSettlement").Invoke(behavior, new object[] { place });
            Assert.Equal(0f, Bonus(behavior, place, village));
            var saved = new Store(false);
            behavior.SyncData(saved);
            Assert.Equal(2, saved.Values.Count);
            Assert.All(saved.Values.Values, map => Assert.Empty(map));
        }

        private static CampaignBehaviorBase CreateBehavior(bool village) => village
            ? (CampaignBehaviorBase)new B1071_VillageInvestmentBehavior() : new B1071_TownInvestmentBehavior();

        private static float Bonus(CampaignBehaviorBase behavior, Settlement place, bool village) => village
            ? ((B1071_VillageInvestmentBehavior)behavior).GetActiveHearthBonus(place.Village)
            : ((B1071_TownInvestmentBehavior)behavior).GetActiveProsperityBonus(place.Town);

        private static Settlement Place(string id, bool village)
        {
            // Construct managed component links; no campaign, menus or gold actions are running.
            var place = Empty<Settlement>();
            place.StringId = id;
            var party = Empty<PartyBase>();
            SettlementComponent component = village ? (SettlementComponent)Empty<Village>() : Empty<Town>();
            AccessTools.PropertySetter(typeof(Settlement), "Party").Invoke(place, new object[] { party });
            AccessTools.PropertySetter(typeof(PartyBase), "Settlement").Invoke(party, new object[] { place });
            AccessTools.PropertySetter(typeof(Settlement), "SettlementComponent").Invoke(place, new object[] { component });
            AccessTools.Field(typeof(Settlement), village ? "Village" : "Town").SetValue(place, component);
            AccessTools.Field(typeof(SettlementComponent), "_owner").SetValue(component, party);
            Assert.True(village ? place.IsVillage : place.IsTown);
            return place;
        }

        private sealed class Store : IDataStore
        {
            internal readonly Dictionary<string, Dictionary<string, float>> Values;
            public bool IsLoading { get; }
            public bool IsSaving => !IsLoading;
            internal Store(bool loading, Dictionary<string, Dictionary<string, float>>? values = null)
            {
                IsLoading = loading;
                Values = values ?? new Dictionary<string, Dictionary<string, float>>();
            }
            public bool SyncData<T>(string key, ref T data)
            {
                if (IsSaving) { Values[key] = new Dictionary<string, float>((Dictionary<string, float>)(object)data!); return true; }
                if (Values.TryGetValue(key, out var map) && map != null)
                {
                    data = (T)(object)new Dictionary<string, float>(map);
                    return true;
                }
                data = default!;
                return false;
            }
        }

        private static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        public void Dispose() => B1071_TestHooks.Reset();
    }
}
#endif
