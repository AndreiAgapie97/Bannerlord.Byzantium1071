#if GAME_TESTS_ENABLED
using Byzantium1071.Campaign.UI;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Xunit;

namespace Byzantium1071.GameTests
{
    public sealed class SlaveConversionScreenContractTests
    {
        [Fact]
        public void ResetClearsAnAbandonedInstanceWithoutThrowing()
        {
            FieldInfo? current = typeof(B1071_SlaveConversionScreen).GetField(
                "_current",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(current);

            object abandoned = FormatterServices.GetUninitializedObject(
                typeof(B1071_SlaveConversionScreen));
            current!.SetValue(null, abandoned);

            B1071_SlaveConversionScreen.Reset();

            Assert.Null(current.GetValue(null));
        }

        [Fact]
        public void CampaignAndModuleShutdownBothResetTheSlaveConversionScreen()
        {
            string source = File.ReadAllText(Path.Combine(RepositoryRoot(), "SubModule.cs"));
            const string resetCall = "B1071_SlaveConversionScreen.Reset();";

            Assert.Equal(2, CountOccurrences(source, resetCall));
            Assert.Contains(resetCall, MethodBody(source, "protected override void OnSubModuleUnloaded()"));
            Assert.Contains(resetCall, MethodBody(source, "public override void OnGameEnd(Game game)"));
        }

        [Fact]
        public void OpeningTheScreenClearsAnyPreviousInstanceBeforeReadingTheTopScreen()
        {
            string source = File.ReadAllText(Path.Combine(
                RepositoryRoot(), "Campaign", "UI", "B1071_SlaveConversionScreen.cs"));
            string openScreen = MethodBody(
                source,
                "public static void OpenScreen(Action<Dictionary<CharacterObject, int>>? onConfirm)");

            int reset = openScreen.IndexOf("Reset();", StringComparison.Ordinal);
            int topScreen = openScreen.IndexOf("ScreenManager.TopScreen", StringComparison.Ordinal);
            Assert.True(reset >= 0 && reset < topScreen);
            Assert.DoesNotContain("if (_current != null) return", openScreen);
        }

        private static int CountOccurrences(string source, string value)
        {
            int count = 0;
            int index = 0;
            while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        private static string MethodBody(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Missing method signature: {signature}");

            int openingBrace = source.IndexOf('{', start + signature.Length);
            Assert.True(openingBrace >= 0, $"Missing opening brace for: {signature}");

            int depth = 0;
            for (int index = openingBrace; index < source.Length; index++)
            {
                if (source[index] == '{')
                    depth++;
                else if (source[index] == '}')
                    depth--;

                if (depth == 0)
                    return source.Substring(start, index - start + 1);
            }

            throw new InvalidOperationException($"Missing closing brace for: {signature}");
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
#endif
