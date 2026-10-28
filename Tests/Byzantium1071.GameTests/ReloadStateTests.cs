#if GAME_TESTS_ENABLED
using System;
using System.Collections.Generic;
using Byzantium1071.Campaign;
using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using Xunit;

namespace Byzantium1071.GameTests
{
    // State that changes AI or diplomacy decisions must read back the same after a load.
    [Collection(nameof(CastleRecruitmentActionCollection))]
    public sealed class ReloadStateTests : IDisposable
    {
        private readonly B1071_McmSettings _settings = new B1071_McmSettings
        {
            EnableDiplomacyPressureBands = true,
            PressureBandRisingStart = 35f, PressureBandCrisisStart = 85f, PressureBandHysteresis = 5f
        };

        public ReloadStateTests() => B1071_TestHooks.Settings = _settings;

        public static IEnumerable<object[]> Cooldowns() => new[]
        {
            new object[] { typeof(B1071_TownInvestmentBehavior), "_heroLastInvestDay", "b1071_townInvestHeroLastDay" },
            new object[] { typeof(B1071_VillageInvestmentBehavior), "_heroLastInvestDay", "b1071_villageInvestHeroLastDay" },
            new object[] { typeof(B1071_GovernanceStabilizationBehavior), "_heroLastStabilizationDay", "b1071_govStabHeroLastDay" }
        };

        [Theory]
        [MemberData(nameof(Cooldowns))]
        public void AiHeroCooldownSurvivesSaveAndLoad(Type behaviorType, string field, string key)
        {
            var original = (CampaignBehaviorBase)Activator.CreateInstance(behaviorType);
            Cooldown(original, field)["lord_1_1"] = 42.5f;

            var saved = new Store(false);
            original.SyncData(saved);
            var resumed = (CampaignBehaviorBase)Activator.CreateInstance(behaviorType);
            resumed.SyncData(new Store(true, saved.Values));

            Assert.True(saved.Values.ContainsKey(key));
            Assert.Equal(42.5f, Cooldown(resumed, field)["lord_1_1"]);
        }

        [Theory]
        [MemberData(nameof(Cooldowns))]
        public void AiHeroCooldownLoadsEmptyFromAnOlderSave(Type behaviorType, string field, string key)
        {
            var original = (CampaignBehaviorBase)Activator.CreateInstance(behaviorType);
            Cooldown(original, field)["lord_1_1"] = 42.5f;
            var saved = new Store(false);
            original.SyncData(saved);
            // An older save carries the behavior's other keys but not this one.
            Assert.True(saved.Values.Remove(key));

            var legacy = (CampaignBehaviorBase)Activator.CreateInstance(behaviorType);
            legacy.SyncData(new Store(true, saved.Values));

            Assert.Empty(Cooldown(legacy, field));
        }

        [Fact]
        public void CrisisInsideTheHysteresisMarginSurvivesLoad()
        {
            var behavior = new B1071_ManpowerBehavior();
            behavior.SyncData(new Store(true, Exhaustion(82f, new Dictionary<string, object>
            {
                ["B1071_PressureBand_Ids"] = new List<string> { "empire" },
                ["B1071_PressureBand_Values"] = new List<int> { (int)DiplomacyPressureBand.Crisis }
            })));

            Assert.Equal(DiplomacyPressureBand.Crisis, behavior.GetPressureBand("empire"));
        }

        [Fact]
        public void SaveWithoutBandsReevaluatesFromLow()
        {
            var behavior = new B1071_ManpowerBehavior();
            behavior.SyncData(new Store(true, Exhaustion(82f, new Dictionary<string, object>())));

            Assert.Equal(DiplomacyPressureBand.Rising, behavior.GetPressureBand("empire"));
        }

        private static Dictionary<string, object> Exhaustion(float value, Dictionary<string, object> values)
        {
            values["B1071_WarExhaustion_Ids"] = new List<string> { "empire" };
            values["B1071_WarExhaustion_Values"] = new List<float> { value };
            return values;
        }

        private static Dictionary<string, float> Cooldown(object behavior, string field) =>
            (Dictionary<string, float>)AccessTools.Field(behavior.GetType(), field).GetValue(behavior);

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
                data = default!; // Older saves lack the new keys.
                return false;
            }
        }

        public void Dispose() => B1071_TestHooks.Reset();
    }
}
#endif
