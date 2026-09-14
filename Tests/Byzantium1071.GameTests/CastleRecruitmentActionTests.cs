#if GAME_TESTS_ENABLED
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.UI;
using HarmonyLib;
using Xunit;

namespace Byzantium1071.GameTests
{
    [CollectionDefinition(nameof(CastleRecruitmentActionCollection), DisableParallelization = true)]
    public sealed class CastleRecruitmentActionCollection { }

    [Collection(nameof(CastleRecruitmentActionCollection))]
    public sealed class CastleRecruitmentActionTests
    {
        private static int _attempts, _successes, _refreshes, _affordable;
        private static string? _calledMethod;

        private static bool RecruitPrefix(MethodBase __originalMethod, ref bool __result)
        {
            _calledMethod = __originalMethod.Name;
            _attempts++;
            __result = _successes < _affordable;
            if (__result) _successes++;
            return false;
        }

        private static bool RefreshPrefix() { _refreshes++; return false; }

        [Theory]
        [InlineData(true, true, true, true, 12, 5, 5, 6)]
        [InlineData(false, true, true, true, 12, 5, 5, 6)]
        [InlineData(false, true, true, true, 12, 20, 12, 12)]
        [InlineData(true, true, true, true, 12, 0, 0, 1)]
        [InlineData(false, false, true, true, 12, 20, 0, 0)]
        [InlineData(false, true, false, true, 12, 20, 0, 0)]
        [InlineData(false, true, true, false, 12, 20, 1, 1)]
        public void RowCommandsUseTheCorrectBackendStopOnFailureAndRefreshOnce(
            bool elite, bool ready, bool enabled, bool all, int count, int affordable, int expectedSuccesses, int expectedAttempts)
        {
            var harmony = new Harmony("B1071.Tests.CastleRecruitmentActions");
            var previous = B1071_CastleRecruitmentBehavior.Instance;
            try
            {
                _attempts = _successes = _refreshes = 0;
                _affordable = affordable;
                _calledMethod = null;
                foreach (string method in new[] { "TryRecruitElite", "TryRecruitPrisoner" })
                    harmony.Patch(AccessTools.Method(typeof(B1071_CastleRecruitmentBehavior), method),
                        prefix: new HarmonyMethod(typeof(CastleRecruitmentActionTests), nameof(RecruitPrefix)));
                harmony.Patch(AccessTools.Method(typeof(B1071_CastleRecruitmentVM), "RefreshLists"),
                    prefix: new HarmonyMethod(typeof(CastleRecruitmentActionTests), nameof(RefreshPrefix)));
                B1071_CastleRecruitmentBehavior.Instance = new B1071_CastleRecruitmentBehavior();
                var row = (B1071_CastleRecruitTroopVM)FormatterServices.GetUninitializedObject(typeof(B1071_CastleRecruitTroopVM));
                Set(row, "_isElite", elite);
                Set(row, "_isReady", ready);
                Set(row, "_canRecruit", enabled);
                Set(row, "_numericCount", count);
                Set(row, "_parent", FormatterServices.GetUninitializedObject(typeof(B1071_CastleRecruitmentVM)));
                if (all) row.ExecuteRecruitAll(); else row.ExecuteRecruit();
                Assert.Equal(expectedSuccesses, _successes);
                Assert.Equal(expectedAttempts, _attempts);
                Assert.Equal(expectedSuccesses > 0 ? 1 : 0, _refreshes);
                if (expectedAttempts > 0)
                    Assert.Equal(elite ? "TryRecruitElite" : "TryRecruitPrisoner", _calledMethod);
            }
            finally
            {
                B1071_CastleRecruitmentBehavior.Instance = previous;
                harmony.UnpatchAll(harmony.Id);
            }
        }

        [Fact]
        public void DepositorSnapshotPreservesBatchesCapsCountsAndDoesNotExposeTracking()
        {
            var behavior = new B1071_CastleRecruitmentBehavior();
            var batches = new List<(string HeroId, int Count)> { ("other", 5), ("player", 8), ("other", 3) };
            var tracking = new Dictionary<string, Dictionary<string, List<(string, int)>>> {
                ["castle"] = new Dictionary<string, List<(string, int)>> { ["troop"] = batches }
            };
            Set(behavior, "_depositorTracking", tracking);
            var snapshot = behavior.GetPrisonerDepositors("castle", "troop", 10);
            Assert.Equal(new (string?, int)[] { ("other", 5), ("player", 5) }, snapshot.ToArray());
            Assert.Equal(new[] { ("other", 5), ("player", 8), ("other", 3) }, batches.ToArray());
            Assert.True(((ICollection<(string, int)>)snapshot).IsReadOnly);
            batches[0] = ("changed", 2);
            Assert.Equal(("other", 5), snapshot[0]);
            Assert.Equal((null, 7), behavior.GetPrisonerDepositors("missing", "troop", 7).Single());
            Assert.Equal((null, 7), behavior.GetPrisonerDepositors("castle", "troop", 20).Last());
            Assert.Empty(behavior.GetPrisonerDepositors("castle", "troop", 0));
        }

        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
#endif
