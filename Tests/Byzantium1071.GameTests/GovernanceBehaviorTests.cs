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
using OwnershipChange = TaleWorlds.CampaignSystem.Actions.ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail;

namespace Byzantium1071.GameTests
{
    [Collection(nameof(CastleRecruitmentActionCollection))]
    public sealed class GovernanceBehaviorTests : IDisposable
    {
        private const string StrainKey = "b1071_governanceStrain";
        private const string DaysKey = "b1071_govStabDays";
        private static readonly string[] BonusKeys = { "b1071_govStabLoyalty", "b1071_govStabSecurity", "b1071_govStabDecay" };
        private readonly B1071_GovernanceStabilizationBehavior? _previous = B1071_GovernanceStabilizationBehavior.Instance;
        private readonly B1071_McmSettings _settings = new B1071_McmSettings
        {
            EnableGovernanceStrain = true, EnableGovernanceStabilization = true,
            GovernanceStrainDecayPerDay = 1f, GovernanceStrainCap = 100f
        };

        public GovernanceBehaviorTests() => B1071_TestHooks.Settings = _settings;

        [Theory]
        [InlineData(1, false)]
        [InlineData(1, true)]
        [InlineData(17, false)]
        [InlineData(17, true)]
        public void StabilizationAndStrainSurviveCheckpointsInEitherTickOrder(int interval, bool stabilizationFirst)
        {
            var places = new[] { Place("town_A1"), Place("town_A11") };
            var live = new B1071_GovernanceBehavior();
            var resumed = new B1071_GovernanceBehavior();
            var liveAid = new B1071_GovernanceStabilizationBehavior();
            var resumedAid = new B1071_GovernanceStabilizationBehavior();
            int[] strain = { 30, 45 }, days = { 4, 9 };
            var initial = new Dictionary<string, Dictionary<string, float>>
            {
                [StrainKey] = new Dictionary<string, float> { [places[0].StringId] = 30, [places[1].StringId] = 45 },
                [DaysKey] = new Dictionary<string, float> { [places[0].StringId] = 4, [places[1].StringId] = 9 }
            };
            for (int bonus = 0; bonus < BonusKeys.Length; bonus++)
                initial[BonusKeys[bonus]] = new Dictionary<string, float>
                { [places[0].StringId] = bonus + 1, [places[1].StringId] = bonus + 2 };
            foreach (CampaignBehaviorBase behavior in new CampaignBehaviorBase[] { live, resumed, liveAid, resumedAid })
                behavior.SyncData(new Store(true, initial));

            for (int day = 1; day <= 120; day++)
            {
                _settings.EnableGovernanceStrain = day % 7 != 0;
                _settings.EnableGovernanceStabilization = day % 5 != 0;
                for (int i = 0; i < places.Length; i++)
                {
                    if (day == 3)
                    {
                        live.ReduceStrain(places[i], 7);
                        resumed.ReduceStrain(places[i], 7);
                        strain[i] = Math.Max(0, strain[i] - 7);
                    }
                    if (stabilizationFirst && _settings.EnableGovernanceStabilization)
                        days[i] = Math.Max(0, days[i] - 1);
                    if (_settings.EnableGovernanceStrain)
                        strain[i] = Math.Max(0, strain[i] - 1 -
                            (_settings.EnableGovernanceStabilization && days[i] > 0 ? i + 3 : 0));
                    if (!stabilizationFirst && _settings.EnableGovernanceStabilization)
                        days[i] = Math.Max(0, days[i] - 1);

                    TickPair(live, liveAid, places[i], stabilizationFirst);
                    TickPair(resumed, resumedAid, places[i], stabilizationFirst);
                    // Both settlements are checked after each tick to expose shared-key mutations.
                    for (int p = 0; p < places.Length; p++)
                    {
                        Assert.Equal((float)strain[p], live.GetStrain(places[p]));
                        Assert.Equal((float)strain[p], resumed.GetStrainForTown(places[p].Town));
                        foreach (var aid in new[] { liveAid, resumedAid })
                        {
                            bool active = _settings.EnableGovernanceStabilization && days[p] > 0;
                            Assert.Equal(active ? p + 1f : 0f, aid.GetActiveLoyaltyBonus(places[p].Town));
                            Assert.Equal(active ? p + 2f : 0f, aid.GetActiveSecurityBonus(places[p].Town));
                            Assert.Equal(active ? p + 3f : 0f, aid.GetActiveStrainDecayBonus(places[p]));
                        }
                    }
                }
                var snapshot = Save(resumed, resumedAid);
                Assert.Equal(5, snapshot.Count);
                foreach (var pair in Save(live, liveAid))
                    Assert.Equal(pair.Value.OrderBy(e => e.Key), snapshot[pair.Key].OrderBy(e => e.Key));
                for (int i = 0; i < places.Length; i++)
                {
                    string id = places[i].StringId;
                    Assert.Equal(days[i] > 0, snapshot[DaysKey].ContainsKey(id));
                    if (days[i] > 0) Assert.Equal((float)days[i], snapshot[DaysKey][id]);
                    foreach (string key in BonusKeys) Assert.Equal(days[i] > 0, snapshot[key].ContainsKey(id));
                }
                if (day % interval == 0)
                {
                    resumed = new B1071_GovernanceBehavior();
                    resumedAid = new B1071_GovernanceStabilizationBehavior();
                    resumed.SyncData(new Store(true, snapshot));
                    resumedAid.SyncData(new Store(true, snapshot));
                    foreach (var values in snapshot.Values) values.Clear();
                }
            }
            Assert.All(Save(resumed, resumedAid).Values, values => Assert.Empty(values));
        }

        [Theory]
        [InlineData(OwnershipChange.BySiege, 20)]
        [InlineData(OwnershipChange.ByBarter, 20)]
        [InlineData(OwnershipChange.ByRebellion, 20)]
        [InlineData(OwnershipChange.Default, 0)]
        [InlineData(OwnershipChange.ByLeaveFaction, 0)]
        [InlineData(OwnershipChange.ByKingDecision, 0)]
        [InlineData(OwnershipChange.ByGift, 0)]
        [InlineData(OwnershipChange.ByClanDestruction, 0)]
        public void OwnershipChangesApplyOnlyTheIntendedStrain(OwnershipChange detail, int expected)
        {
            var behavior = new B1071_GovernanceBehavior();
            var town = Place("town");
            var village = Place("village", true);
            foreach (var place in new[] { town, village })
                Invoke(behavior, "OnSettlementOwnerChanged", place, false, null!, null!, null!, detail);
            Assert.Equal((float)expected, behavior.GetStrain(town));
            Assert.Equal(0f, behavior.GetStrain(village));
            for (int i = 0; i < 10; i++)
                Invoke(behavior, "OnSettlementOwnerChanged", town, false, null!, null!, null!, detail);
            Assert.Equal(expected == 0 ? 0f : 100f, behavior.GetStrain(town));
            behavior.ReduceStrain(town, 500f);
            Assert.Empty(Save(behavior)[StrainKey]);
            _settings.EnableGovernanceStrain = false;
            Invoke(behavior, "OnSettlementOwnerChanged", town, false, null!, null!, null!, detail);
            Assert.Empty(Save(behavior)[StrainKey]);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MissingAndNullFieldsLoadWithoutBonusesOrStrain(bool explicitNull)
        {
            var strain = new B1071_GovernanceBehavior();
            var aid = new B1071_GovernanceStabilizationBehavior();
            var data = Save(strain, aid);
            foreach (string key in data.Keys.ToArray())
                if (explicitNull) data[key] = null!; else data.Remove(key);
            strain.SyncData(new Store(true, data));
            aid.SyncData(new Store(true, data));
            var town = Place("empty");
            TickPair(strain, aid, town, true);
            Assert.Equal(0f, strain.GetStrain(town));
            Assert.Equal(0f, aid.GetActiveLoyaltyBonus(town.Town));
            Assert.Equal(0f, aid.GetActiveSecurityBonus(town.Town));
            Assert.Equal(0f, aid.GetActiveStrainDecayBonus(town));
            Assert.All(Save(strain, aid).Values, values => Assert.Empty(values));
        }

        private static void TickPair(B1071_GovernanceBehavior strain, B1071_GovernanceStabilizationBehavior aid,
            Settlement place, bool stabilizationFirst)
        {
            B1071_GovernanceStabilizationBehavior.Instance = aid;
            if (stabilizationFirst) Invoke(aid, "OnDailyTickSettlement", place);
            Invoke(strain, "OnDailyTickSettlement", place);
            if (!stabilizationFirst) Invoke(aid, "OnDailyTickSettlement", place);
        }

        private static Dictionary<string, Dictionary<string, float>> Save(params CampaignBehaviorBase[] behaviors)
        {
            var store = new Store(false, new Dictionary<string, Dictionary<string, float>>());
            foreach (var behavior in behaviors) behavior.SyncData(store);
            return store.Values;
        }

        private sealed class Store : IDataStore
        {
            internal readonly Dictionary<string, Dictionary<string, float>> Values;
            public bool IsLoading { get; }
            public bool IsSaving => !IsLoading;
            internal Store(bool loading, Dictionary<string, Dictionary<string, float>> values)
            { IsLoading = loading; Values = values; }
            public bool SyncData<T>(string key, ref T data)
            {
                if (IsSaving) { Values[key] = new Dictionary<string, float>((Dictionary<string, float>)(object)data!); return true; }
                bool exists = Values.TryGetValue(key, out var map);
                data = map == null ? default! : (T)(object)new Dictionary<string, float>(map);
                return exists;
            }
        }

        private static Settlement Place(string id, bool village = false)
        {
            var place = Empty<Settlement>();
            place.StringId = id;
            var party = Empty<PartyBase>();
            SettlementComponent component = village ? (SettlementComponent)Empty<Village>() : Empty<Town>();
            AccessTools.PropertySetter(typeof(Settlement), "Party").Invoke(place, new object[] { party });
            AccessTools.PropertySetter(typeof(PartyBase), "Settlement").Invoke(party, new object[] { place });
            AccessTools.PropertySetter(typeof(Settlement), "SettlementComponent").Invoke(place, new object[] { component });
            AccessTools.Field(typeof(Settlement), village ? "Village" : "Town").SetValue(place, component);
            AccessTools.Field(typeof(SettlementComponent), "_owner").SetValue(component, party);
            return place;
        }

        private static void Invoke(object target, string method, params object[] arguments) =>
            AccessTools.Method(target.GetType(), method).Invoke(target, arguments);
        private static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        public void Dispose()
        {
            B1071_GovernanceStabilizationBehavior.Instance = _previous;
            B1071_TestHooks.Reset();
        }
    }
}
#endif
