#if GAME_TESTS_ENABLED
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using TaleWorlds.Localization;
using Xunit;

namespace Byzantium1071.GameTests
{
    [CollectionDefinition(nameof(LanguageLoadingCollection), DisableParallelization = true)]
    public sealed class LanguageLoadingCollection { }

    // Runs the game's own loader over Native and this module. Every translation once shipped
    // without a language_data.xml and was never read, while every file-level check passed:
    // Bannerlord finds a module's translations only through those files, and files them under
    // the LanguageData id, which has to match one Native defines.
    [Collection(nameof(LanguageLoadingCollection))]
    public sealed class LanguageLoadingTests
    {
        private const string SampleId = "b1071_demob_age";

        private static readonly MethodInfo LoadLanguage = typeof(LocalizedTextManager).GetMethod(
            "LoadLanguage", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(string) }, null)!;

        private static string GameFolder() =>
            typeof(LanguageLoadingTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute => attribute.Key == "GameFolder").Value!;

        private static string ModuleFolder()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Byzantium1071.csproj")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            return Path.Combine(directory!.FullName, "_Module");
        }

        [Theory]
        [InlineData("Français", "French")]
        [InlineData("Deutsch", "German")]
        [InlineData("简体中文", "Chinese")]
        [InlineData("한국어", "Korean")]
        public void GameLoaderServesTheTranslation(string languageId, string folder)
        {
            string module = ModuleFolder();
            string expected = XDocument.Load(Path.Combine(module, "ModuleData", "Languages", folder, "std_module_strings_xml.xml"))
                .Descendants("string")
                .Single(element => (string?)element.Attribute("id") == SampleId)
                .Attribute("text")!.Value;

            try
            {
                LocalizedTextManager.LoadLocalizationXmls(new[] { Path.Combine(GameFolder(), "Modules", "Native"), module });

                // GetLanguageIds lists only languages with ISO codes, which only Native supplies,
                // so a module id that matches no game language is not offered at all.
                Assert.Contains(languageId, LocalizedTextManager.GetLanguageIds(developmentMode: true));

                // The text can only come from this module's file if a language_data.xml under
                // this language id lists it.
                LoadLanguage.Invoke(null, new object[] { languageId });
                Assert.Equal(expected, LocalizedTextManager.GetTranslatedText(languageId, SampleId));
            }
            finally
            {
                // Leave the loader as an uninitialised test process has it: no languages and
                // no loaded text. LoadLanguage clears the text before finding no "English".
                LocalizedTextManager.LoadLocalizationXmls(Array.Empty<string>());
                LoadLanguage.Invoke(null, new object[] { "English" });
            }
        }
    }
}
#endif
