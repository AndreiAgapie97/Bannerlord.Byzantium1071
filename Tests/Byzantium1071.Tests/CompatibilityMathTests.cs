using Byzantium1071.Campaign;
using Xunit;

namespace Byzantium1071.Tests
{
    public sealed class CompatibilityMathTests
    {
        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("tAlEwOrLdS.CampaignSystem", true)]
        [InlineData("SandBox.ViewModelCollection", true)]
        [InlineData("StoryMode", true)]
        [InlineData("CustomBattle", true)]
        [InlineData("NavalDLC", true)]
        [InlineData("BirthAndDeath", true)]
        [InlineData("Multiplayer", true)]
        [InlineData("Native", true)]
        [InlineData("com.example.SandboxBalance", false)]
        [InlineData("com.example.gameplay", false)]
        public void NativeAssemblyClassificationIsPrefixBased(string? assemblyName, bool expected)
        {
            Assert.Equal(expected, B1071_CompatibilityMath.IsNativeAssembly(assemblyName));
        }

        [Theory]
        [InlineData(null, true)]
        [InlineData("", true)]
        [InlineData("TaleWorlds.CampaignSystem", true)]
        [InlineData("ButterLib", true)]
        [InlineData("ButLib", true)]
        [InlineData("com.example.mcm.integration", true)]
        [InlineData("ModLib", true)]
        [InlineData("UIExtenderEx", true)]
        [InlineData("MBOptionScreen", true)]
        [InlineData("BetterExceptionWindow", true)]
        [InlineData("DebugMode", true)]
        [InlineData("NativeModule", true)]
        [InlineData("Unpatch", true)]
        [InlineData("BLSE", true)]
        [InlineData("LauncherEx", true)]
        [InlineData("0Harmony", true)]
        [InlineData("0Harmony.Managed", true)]
        [InlineData("com.example.sandbox.balance", false)]
        [InlineData("com.example.gameplay", false)]
        public void FrameworkIdClassificationKeepsGameplayModsVisible(string? harmonyId, bool expected)
        {
            Assert.Equal(expected, B1071_CompatibilityMath.IsFrameworkId(harmonyId));
        }
    }
}
