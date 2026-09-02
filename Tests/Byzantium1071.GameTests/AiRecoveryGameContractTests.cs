using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Patches;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CampaignBehaviors.AiBehaviors;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using Xunit;

namespace Byzantium1071.GameTests
{
    [CollectionDefinition(nameof(AiRecoveryHarmonyCollection), DisableParallelization = true)]
    public sealed class AiRecoveryHarmonyCollection
    {
    }

    [Collection(nameof(AiRecoveryHarmonyCollection))]
    public sealed class AiRecoveryGameContractTests
    {
        private sealed class StubBehavior : CampaignBehaviorBase
        {
            public override void RegisterEvents()
            {
            }

            public override void SyncData(IDataStore dataStore)
            {
            }
        }

        private sealed class LoadingDataStore : IDataStore
        {
            private readonly IReadOnlyDictionary<string, object> _values;

            internal LoadingDataStore(IReadOnlyDictionary<string, object> values)
            {
                _values = values;
            }

            public bool IsSaving => false;
            public bool IsLoading => true;

            public bool SyncData<T>(string key, ref T data)
            {
                if (!_values.TryGetValue(key, out object? value) || !(value is T typed))
                    return false;

                data = typed;
                return true;
            }
        }

        private sealed class SavingDataStore : IDataStore
        {
            internal List<string> Keys { get; } = new List<string>();
            internal Dictionary<string, object?> Values { get; } = new Dictionary<string, object?>();

            public bool IsSaving => true;
            public bool IsLoading => false;

            public bool SyncData<T>(string key, ref T data)
            {
                Keys.Add(key);
                Values[key] = data;
                return true;
            }
        }

        [Fact]
        public void MbEventInvokesLaterRegistrationsFirst()
        {
            var campaignEvent = new MbEvent<int, int>();
            var calls = new List<string>();
            object firstOwner = new();
            object secondOwner = new();

            campaignEvent.AddNonSerializedListener(firstOwner, (_, _) => calls.Add("first"));
            campaignEvent.AddNonSerializedListener(secondOwner, (_, _) => calls.Add("second"));
            campaignEvent.Invoke(0, 0);

            Assert.Equal(new[] { "second", "first" }, calls);
        }

        [Fact]
        public void RecoveryInsertionPlacesItImmediatelyBeforeTheFirstNativeAiScorer()
        {
            var starter = new CampaignGameStarter(null!, null!);
            starter.AddBehavior(new StubBehavior());
            var nativeScorer = new AiArmyMemberBehavior();
            starter.AddBehavior(nativeScorer);
            starter.AddBehavior(new StubBehavior());

            Assert.True(SubModule.TryInsertAiRecoveryBehavior(starter));

            CampaignBehaviorBase[] behaviors = starter.CampaignBehaviors.ToArray();
            int nativeIndex = Array.IndexOf(behaviors, nativeScorer);
            Assert.True(nativeIndex > 0);
            Assert.IsType<B1071_AiRecoveryBehavior>(behaviors[nativeIndex - 1]);
        }

        [Fact]
        public void RecoveryRegistrationOrderObservesCompletedNativeScores()
        {
            var campaignEvent = new MbEvent<int, PartyThinkParams>();
            var thinkParams = new PartyThinkParams(null!);
            object recoveryOwner = new();
            object nativeOwner = new();
            int observedScoreCount = -1;

            // Recovery is registered first because its behavior is inserted before native AI.
            campaignEvent.AddNonSerializedListener(
                recoveryOwner,
                (_, parameters) => observedScoreCount = parameters.AIBehaviorScores.Count);
            campaignEvent.AddNonSerializedListener(
                nativeOwner,
                (_, parameters) =>
                {
                    AIBehaviorData nativeScore = AIBehaviorData.Invalid;
                    parameters.AddBehaviorScore((nativeScore, 1f));
                });

            campaignEvent.Invoke(0, thinkParams);

            Assert.Equal(1, observedScoreCount);
        }

        [Fact]
        public void PartyThinkAndRouteContractsSupportRecoveryRescoring()
        {
            var thinkParams = new PartyThinkParams(null!);
            var behavior = new AIBehaviorData(
                CampaignVec2.Zero,
                AiBehavior.GoToSettlement,
                MobileParty.NavigationType.None,
                willGatherArmy: false,
                isFromPort: true,
                isTargetingPort: true);

            thinkParams.AddBehaviorScore((behavior, 3f));
            Assert.True(thinkParams.TryGetBehaviorScore(in behavior, out float original));
            Assert.Equal(3f, original);

            thinkParams.SetBehaviorScore(in behavior, 7f);
            Assert.True(thinkParams.TryGetBehaviorScore(in behavior, out float adjusted));
            Assert.Equal(7f, adjusted);
            Assert.Equal(AiBehavior.GoToSettlement, behavior.AiBehavior);
            Assert.Equal(MobileParty.NavigationType.None, behavior.NavigationType);
            Assert.True(behavior.IsFromPort);
            Assert.True(behavior.IsTargetingPort);
            Assert.False(behavior.WillGatherArmy);
        }

        [Fact]
        public void BothGarrisonManpowerPatchesAttachToV152Targets()
        {
            string owner = $"byzantium1071.tests.ai-recovery.{Guid.NewGuid():N}";
            var harmony = new Harmony(owner);

            MethodInfo? capTarget = AccessTools.Method(
                typeof(DefaultSettlementGarrisonModel),
                nameof(DefaultSettlementGarrisonModel.GetMaximumDailyAutoRecruitmentCount),
                new[] { typeof(Town), typeof(bool) });
            MethodInfo? consumptionTarget = AccessTools.Method(
                typeof(GarrisonRecruitmentCampaignBehavior),
                "TickAutoRecruitmentGarrisonChange",
                new[] { typeof(Town) });

            Assert.NotNull(capTarget);
            Assert.NotNull(consumptionTarget);

            try
            {
                harmony.CreateClassProcessor(typeof(B1071_GarrisonAutoRecruitManpowerPatch)).Patch();
                harmony.CreateClassProcessor(typeof(B1071_GarrisonAutoRecruitManpowerConsumptionPatch)).Patch();

                Patches? capPatches = Harmony.GetPatchInfo(capTarget!);
                Assert.NotNull(capPatches);
                Assert.Contains(capPatches!.Postfixes, patch => patch.owner == owner);

                Patches? consumptionPatches = Harmony.GetPatchInfo(consumptionTarget!);
                Assert.NotNull(consumptionPatches);
                Assert.Contains(consumptionPatches!.Prefixes, patch => patch.owner == owner);
                Assert.Contains(consumptionPatches.Postfixes, patch => patch.owner == owner);
            }
            finally
            {
                harmony.UnpatchAll(owner);
            }
        }

        /// <summary>
        /// Every native member the recovery pass reads to decide eligibility, to recognise a
        /// confirmed destination, and to re-anchor a party after a roster change. A rename on
        /// the TaleWorlds side would leave the feature compiling against nothing here.
        /// </summary>
        [Fact]
        public void MobilePartyStillExposesEveryMemberTheRecoveryPassReads()
        {
            Assert.NotNull(AccessTools.PropertyGetter(typeof(MobileParty), "IsCurrentlyUsedByAQuest"));
            Assert.NotNull(AccessTools.PropertyGetter(typeof(MobileParty), "DefaultBehavior"));
            Assert.NotNull(AccessTools.PropertyGetter(typeof(MobileParty), "TargetSettlement"));
            Assert.NotNull(AccessTools.PropertyGetter(typeof(MobileParty), "CurrentSettlement"));
            Assert.NotNull(AccessTools.PropertyGetter(typeof(MobileParty), "DesiredAiNavigationType"));
            Assert.NotNull(AccessTools.PropertyGetter(typeof(MobileParty), "IsTargetingPort"));

            Assert.NotNull(AccessTools.Method(
                typeof(MobileParty),
                "SetMoveGoToSettlement",
                new[] { typeof(Settlement), typeof(MobileParty.NavigationType), typeof(bool) }));
            Assert.NotNull(AccessTools.Method(
                typeof(MobileParty),
                "RecalculateShortTermBehavior",
                Type.EmptyTypes));
        }

        /// <summary>
        /// The recovery pass recruits at the settlement a lord is already standing in by
        /// calling into the two systems that own those troops. Both entry points must keep
        /// the signature it calls, or the immediate pass silently recruits nothing.
        /// </summary>
        [Fact]
        public void ImmediateRecruitmentHelpersKeepTheSignatureTheRecoveryPassCalls()
        {
            MethodInfo? veterans = AccessTools.Method(
                typeof(B1071_DemobilizationBehavior),
                "TryAiHireVeteransAtCurrentSettlement",
                new[] { typeof(MobileParty), typeof(Settlement) });
            MethodInfo? castleRecruits = AccessTools.Method(
                typeof(B1071_CastleRecruitmentBehavior),
                "TryAiAutoRecruitOnArrival",
                new[] { typeof(MobileParty), typeof(Settlement) });

            Assert.NotNull(veterans);
            Assert.NotNull(castleRecruits);
            Assert.Equal(typeof(void), veterans!.ReturnType);
            Assert.Equal(typeof(void), castleRecruits!.ReturnType);
        }

        [Fact]
        public void RecoverySyncDataLoadsOnlyTheConfirmedPartyAndTargetIds()
        {
            var behavior = new B1071_AiRecoveryBehavior();
            behavior.SyncData(new LoadingDataStore(new Dictionary<string, object>
            {
                ["b1071_aiRecoveryPartyIds"] = new List<string> { "party_a" },
                ["b1071_aiRecoveryTargetIds"] = new List<string> { "castle_a" },
                ["b1071_aiRecoveryExpiryDays"] = new List<float> { 42f }
            }));

            Assert.Equal(new[] { "party_a" }, SavedPartyIds(behavior));
            Assert.Equal(new[] { "castle_a" }, SavedTargetIds(behavior));
            Assert.Equal(new[] { 42f }, SavedExpiryDays(behavior));
        }

        /// <summary>
        /// The save half and the load half are one serializer: the three lists must be written
        /// in the order they are read, an empty register must write no rows, and a save from
        /// before the feature existed carries none of the three keys and must still load.
        /// </summary>
        [Fact]
        public void RecoverySyncDataRoundTripsInOrderAndToleratesASaveWithoutTheKeys()
        {
            var behavior = new B1071_AiRecoveryBehavior();
            var saved = new SavingDataStore();
            behavior.SyncData(saved);

            Assert.Equal(
                new[]
                {
                    "b1071_aiRecoveryPartyIds",
                    "b1071_aiRecoveryTargetIds",
                    "b1071_aiRecoveryExpiryDays"
                },
                saved.Keys);
            Assert.Empty(Assert.IsType<List<string>>(saved.Values["b1071_aiRecoveryPartyIds"]));
            Assert.Empty(Assert.IsType<List<string>>(saved.Values["b1071_aiRecoveryTargetIds"]));
            Assert.Empty(Assert.IsType<List<float>>(saved.Values["b1071_aiRecoveryExpiryDays"]));

            var legacy = new B1071_AiRecoveryBehavior();
            legacy.SyncData(new LoadingDataStore(new Dictionary<string, object>()));

            Assert.Empty(SavedPartyIds(legacy));
            Assert.Empty(SavedTargetIds(legacy));
            Assert.Empty(SavedExpiryDays(legacy));
        }

        private static List<string> SavedPartyIds(B1071_AiRecoveryBehavior behavior)
            => SavedList<string>(behavior, "_savedIntentPartyIds");

        private static List<string> SavedTargetIds(B1071_AiRecoveryBehavior behavior)
            => SavedList<string>(behavior, "_savedIntentTargetIds");

        private static List<float> SavedExpiryDays(B1071_AiRecoveryBehavior behavior)
            => SavedList<float>(behavior, "_savedIntentExpiryDays");

        private static List<T> SavedList<T>(B1071_AiRecoveryBehavior behavior, string fieldName)
        {
            FieldInfo? field = typeof(B1071_AiRecoveryBehavior)
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return Assert.IsType<List<T>>(field!.GetValue(behavior));
        }
    }
}
