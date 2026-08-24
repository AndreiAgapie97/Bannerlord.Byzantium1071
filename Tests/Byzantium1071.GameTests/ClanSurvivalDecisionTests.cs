using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Patches;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using Xunit;

namespace Byzantium1071.GameTests
{
    [CollectionDefinition(nameof(ClanSurvivalHarmonyCollection), DisableParallelization = true)]
    public sealed class ClanSurvivalHarmonyCollection
    {
    }

    [Collection(nameof(ClanSurvivalHarmonyCollection))]
    public sealed class ClanSurvivalDecisionTests
    {
        [Theory]
        [InlineData(false, false, true, true)]
        [InlineData(false, false, false, false)]
        [InlineData(false, true, true, false)]
        [InlineData(false, true, false, false)]
        [InlineData(true, false, true, false)]
        [InlineData(true, false, false, false)]
        [InlineData(true, true, true, false)]
        [InlineData(true, true, false, false)]
        public void TrackedClanProtectionRequiresActiveIndependentClanWithValidLivingLeader(
            bool isEliminated,
            bool hasKingdom,
            bool hasValidLivingLeader,
            bool expected)
        {
            bool actual = B1071_ClanSurvivalDestroyClanPatch
                .ShouldSuppressTrackedClanDestruction(
                    isEliminated,
                    hasKingdom,
                    hasValidLivingLeader);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void RebelNormalizationReflectionContractsStillExist()
        {
            PropertyInfo? minorFactionProperty = typeof(Clan).GetProperty(
                nameof(Clan.IsMinorFaction),
                BindingFlags.Instance | BindingFlags.Public);
            MethodInfo? minorFactionSetter = minorFactionProperty?.GetSetMethod(nonPublic: true);
            FieldInfo? rebellionTrackingField = typeof(RebellionsCampaignBehavior).GetField(
                "_rebelClansAndDaysPassedAfterCreation",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(minorFactionSetter);
            Assert.NotNull(rebellionTrackingField);
            Assert.True(
                typeof(IDictionary).IsAssignableFrom(rebellionTrackingField!.FieldType),
                "Rebellion tracking no longer implements IDictionary; normalization cannot remove the rescued clan.");
        }

        [Theory]
        [InlineData(false, true, true)]
        [InlineData(false, false, false)]
        [InlineData(true, true, false)]
        [InlineData(true, false, false)]
        public void RebelNormalizationRequiresBothCriticalFlags(
            bool isRebelClan,
            bool isMinorFaction,
            bool expected)
        {
            bool actual = B1071_ClanSurvivalBehavior.IsRebelNormalizationComplete(
                isRebelClan,
                isMinorFaction);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void DestroyClanPatchBindsToBothVanillaEntryPoints()
        {
            string owner = $"byzantium1071.tests.clan-survival.{Guid.NewGuid():N}";
            var harmony = new Harmony(owner);

            try
            {
                harmony.CreateClassProcessor(typeof(B1071_ClanSurvivalDestroyClanPatch)).Patch();

                AssertPatchPair(owner, nameof(DestroyClanAction.Apply));
                AssertPatchPair(owner, nameof(DestroyClanAction.ApplyByClanLeaderDeath));
            }
            finally
            {
                harmony.UnpatchAll(owner);
            }
        }

        [Fact]
        public void RescueCallbacksNeverStartNestedLeaderSuccession()
        {
            foreach (string relativePath in new[]
            {
                "Campaign/Behaviors/B1071_ClanSurvivalBehavior.cs",
                "Campaign/Patches/B1071_ClanSurvivalPatch.cs"
            })
            {
                string source = File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath));

                Assert.DoesNotContain("ChangeClanLeaderAction.Apply", source);
            }
        }

        [Fact]
        public void RescueOperationReportsWhetherTrackingCommitted()
        {
            MethodInfo? rescue = typeof(B1071_ClanSurvivalPatch).GetMethod(
                nameof(B1071_ClanSurvivalPatch.PerformRescue),
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.NotNull(rescue);
            Assert.Equal(typeof(bool), rescue!.ReturnType);
        }

        [Fact]
        public void DisablingNewRescuesDoesNotDisableExistingClanMaintenance()
        {
            string source = File.ReadAllText(Path.Combine(
                RepositoryRoot(),
                "Campaign/Behaviors/B1071_ClanSurvivalBehavior.cs"));
            int dailyTick = source.IndexOf("private void OnDailyTick()", StringComparison.Ordinal);

            Assert.True(dailyTick >= 0, "Clan survival no longer has its daily maintenance entry point.");
            Assert.DoesNotContain(
                "if (!Settings.EnableClanSurvival) return;",
                source.Substring(dailyTick));
        }

        private static void AssertPatchPair(string owner, string methodName)
        {
            MethodInfo? original = AccessTools.Method(
                typeof(DestroyClanAction),
                methodName,
                new[] { typeof(Clan) });
            Assert.NotNull(original);

            Patches? patches = Harmony.GetPatchInfo(original!);
            Assert.NotNull(patches);
            Assert.Contains(patches!.Prefixes, patch => patch.owner == owner);
            Assert.Contains(patches.Postfixes, patch => patch.owner == owner);
        }

        private static string RepositoryRoot()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);

            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
                    return directory.FullName;

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not find the repository root.");
        }
    }
}
