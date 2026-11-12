using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Byzantium1071.GameTests
{
    /// <summary>
    /// Confirms this project is wired to the installed game correctly. Without the game
    /// present the signature suites are excluded at compile time, so these checks only
    /// assert what must hold when the game-referencing tests are actually compiled in.
    /// </summary>
    public sealed class GameTestEnvironmentTests
    {
#if GAME_TESTS_ENABLED
        [Fact]
        public void ModAssemblyLoadsAgainstTheInstalledGame()
        {
            Assembly modAssembly = typeof(Byzantium1071.SubModule).Assembly;

            Assert.Contains(
                modAssembly.GetReferencedAssemblies(),
                reference => reference.Name == "TaleWorlds.CampaignSystem");
        }

        [Fact]
        public void CampaignSystemResolvesFromTheGameFolderNotAStaleCopy()
        {
            Assembly campaignSystem = typeof(TaleWorlds.CampaignSystem.Hero).Assembly;

            Assert.False(string.IsNullOrEmpty(campaignSystem.Location));
            Assert.True(
                File.Exists(campaignSystem.Location),
                $"TaleWorlds.CampaignSystem resolved to a path that no longer exists: {campaignSystem.Location}");
        }

        [Fact]
        public void SignatureSuitesAreCompiledInWhenTheGameIsPresent()
        {
            Type[] suites = typeof(GameTestEnvironmentTests).Assembly.GetTypes();

            Assert.Contains(suites, type => type.Name == "PatchSignatureTests");
            Assert.Contains(suites, type => type.Name == "SettingsMigrationTests");
        }
#else
        [Fact]
        public void GameTestsAreSkippedWithoutAnInstalledGame()
        {
            Type[] suites = typeof(GameTestEnvironmentTests).Assembly.GetTypes();

            // The project must still build and run on a machine with no Bannerlord install;
            // the game-referencing suites are simply excluded from compilation.
            Assert.DoesNotContain(suites, type => type.Name == "PatchSignatureTests");
        }
#endif
    }
}
