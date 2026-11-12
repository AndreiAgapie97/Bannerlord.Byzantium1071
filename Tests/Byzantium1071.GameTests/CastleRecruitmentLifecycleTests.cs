#if GAME_TESTS_ENABLED
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using Byzantium1071.Campaign.UI;
using HarmonyLib;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.ScreenSystem;
using Xunit;

namespace Byzantium1071.GameTests
{
    [Collection(nameof(CastleRecruitmentActionCollection))]
    public sealed class CastleRecruitmentLifecycleTests
    {
        private static readonly List<string> Calls = new List<string>();
        private static GauntletMovieIdentifier? _expectedMovie;

        private static bool ResetPrefix() { Calls.Add("reset"); return false; }
        private static bool ReleasePrefix(GauntletMovieIdentifier __0)
        {
            Assert.Same(_expectedMovie, __0);
            Calls.Add("release");
            return false;
        }
        private static bool RemovePrefix() { Calls.Add("remove"); return false; }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, false)] // Opening failed before a movie was returned.
        [InlineData(true, true)] // Parent screen already finalized its layer.
        public void CloseReleasesMovieBeforeLayerAndCanBeCalledTwice(bool hasMovie, bool finalized)
        {
            var harmony = new Harmony("B1071.Tests.CastleRecruitmentLifecycle");
            try
            {
                Calls.Clear();
                harmony.Patch(AccessTools.Method(typeof(InputRestrictions), "ResetInputRestrictions"),
                    prefix: new HarmonyMethod(typeof(CastleRecruitmentLifecycleTests), nameof(ResetPrefix)));
                harmony.Patch(AccessTools.Method(typeof(GauntletLayer), "ReleaseMovie"),
                    prefix: new HarmonyMethod(typeof(CastleRecruitmentLifecycleTests), nameof(ReleasePrefix)));
                harmony.Patch(AccessTools.Method(typeof(ScreenBase), "RemoveLayer"),
                    prefix: new HarmonyMethod(typeof(CastleRecruitmentLifecycleTests), nameof(RemovePrefix)));
                var screen = Empty<B1071_CastleRecruitmentScreen>();
                var layer = Empty<GauntletLayer>();
                typeof(ScreenLayer).GetProperty("InputRestrictions").SetValue(layer, Empty<InputRestrictions>());
                typeof(ScreenLayer).GetProperty("IsFinalized").SetValue(layer, finalized);
                _expectedMovie = hasMovie ? Empty<GauntletMovieIdentifier>() : null;
                Set(screen, "_gauntletLayer", layer);
                Set(screen, "_parentScreen", Empty<TestScreen>());
                Set(screen, "_movie", _expectedMovie);

                MethodInfo close = AccessTools.Method(typeof(B1071_CastleRecruitmentScreen), "OnCloseRequested");
                close.Invoke(screen, null);
                Assert.Equal(finalized ? Array.Empty<string>() : hasMovie
                    ? new[] { "reset", "release", "remove" } : new[] { "reset", "remove" }, Calls);
                Assert.False(screen.IsAlive);
                Assert.Null(AccessTools.Field(screen.GetType(), "_movie").GetValue(screen));
                Assert.Null(AccessTools.Field(screen.GetType(), "_parentScreen").GetValue(screen));
                int count = Calls.Count;
                close.Invoke(screen, null);
                Assert.Equal(count, Calls.Count);
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
                _expectedMovie = null;
                Calls.Clear();
            }
        }

        private sealed class TestScreen : ScreenBase { }
        private static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        private static void Set(object target, string name, object? value)
            => AccessTools.Field(target.GetType(), name).SetValue(target, value);
    }
}
#endif
