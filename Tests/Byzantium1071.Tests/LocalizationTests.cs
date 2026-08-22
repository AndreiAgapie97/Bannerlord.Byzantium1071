using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Byzantium1071.Tests
{
    public sealed class LocalizationTests
    {
        // Case-insensitive: at least one shipped ID uses "B1071_" rather than "b1071_".
        private static readonly Regex TextIdPattern = new(
            @"{=(?<id>b1071_[^}{]+)}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PlaceholderPattern = new(@"{(?<name>[A-Z][A-Z0-9_]*)}", RegexOptions.Compiled);
        private static readonly Regex EscapedUnicodePattern = new(@"\\u[0-9A-Fa-f]{4}", RegexOptions.Compiled);

        public static IEnumerable<object[]> TranslationFiles()
        {
            yield return new object[] { "French", LanguageFile("French") };
            yield return new object[] { "German", LanguageFile("German") };
            yield return new object[] { "Chinese", LanguageFile("Chinese") };
        }

        [Fact]
        public void TextIdScanFindsTheExpectedBodyOfIds()
        {
            HashSet<string> referencedIds = FindCodeTextIds();

            // Without this the scanning tests below would pass vacuously if the pattern broke.
            Assert.True(
                referencedIds.Count >= 900,
                $"Only {referencedIds.Count} text IDs were found in the mod source; the scan pattern is likely broken.");
        }

        [Fact]
        public void CodeTextIdsExistInEnglish()
        {
            HashSet<string> referencedIds = FindCodeTextIds();
            Dictionary<string, string> english = ReadEntries(LanguageFile());

            string[] missing = referencedIds.Except(english.Keys, StringComparer.Ordinal).OrderBy(id => id).ToArray();

            Assert.True(
                missing.Length == 0,
                $"English is missing {missing.Length} code text IDs: {string.Join(", ", missing)}");
        }

        [Theory]
        [MemberData(nameof(TranslationFiles))]
        public void EveryEnglishTextIdExistsInTranslation(string language, string translationPath)
        {
            Dictionary<string, string> english = ReadEntries(LanguageFile());
            Dictionary<string, string> translation = ReadEntries(translationPath);

            string[] missing = english.Keys.Except(translation.Keys, StringComparer.Ordinal).OrderBy(id => id).ToArray();

            Assert.True(
                missing.Length == 0,
                $"{language} is missing {missing.Length} English text IDs: {string.Join(", ", missing)}");
        }

        [Theory]
        [MemberData(nameof(TranslationFiles))]
        public void TranslationPlaceholdersMatchEnglish(string language, string translationPath)
        {
            Dictionary<string, string> english = ReadEntries(LanguageFile());
            Dictionary<string, string> translation = ReadEntries(translationPath);
            List<string> mismatches = new();

            foreach ((string id, string englishText) in english)
            {
                if (!translation.TryGetValue(id, out string? translatedText))
                {
                    mismatches.Add($"{id} is missing");
                    continue;
                }

                string[] expected = Placeholders(englishText);
                string[] actual = Placeholders(translatedText);
                string[] missing = expected
                    .Where(name => !IsLanguageSpecificPluralSuffix(name))
                    .Except(actual, StringComparer.Ordinal)
                    .ToArray();
                string[] unexpected = actual.Except(expected, StringComparer.Ordinal).ToArray();

                if (missing.Length > 0 || unexpected.Length > 0)
                {
                    mismatches.Add(
                        $"{id}: missing [{string.Join(", ", missing)}], unexpected [{string.Join(", ", unexpected)}]");
                }
            }

            Assert.True(
                mismatches.Count == 0,
                $"{language} has {mismatches.Count} placeholder mismatches:{Environment.NewLine}{string.Join(Environment.NewLine, mismatches)}");
        }

        [Fact]
        public void LanguageFilesDoNotContainEscapingTraps()
        {
            foreach (object[] translation in TranslationFiles().Append(new object[] { "English", LanguageFile() }))
            {
                string language = (string)translation[0];
                string path = (string)translation[1];
                string source = File.ReadAllText(path);

                Assert.False(source.Contains(@"\n", StringComparison.Ordinal), $"{language} contains a literal \\n.");
                Assert.False(EscapedUnicodePattern.IsMatch(source), $"{language} contains a literal Unicode escape.");
                Assert.False(source.Contains("&amp;#10;", StringComparison.Ordinal), $"{language} double-escapes a line break.");
            }
        }

        [Fact]
        public void LanguageFilesRetainExpectedBomAndXmlDeclaration()
        {
            AssertLanguageFileConvention(LanguageFile(), hasBom: true, "<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            AssertLanguageFileConvention(LanguageFile("French"), hasBom: false, "<?xml version='1.0' encoding='utf-8'?>");
            AssertLanguageFileConvention(LanguageFile("German"), hasBom: false, "<?xml version='1.0' encoding='utf-8'?>");
            AssertLanguageFileConvention(LanguageFile("Chinese"), hasBom: true, "<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        }

        private static void AssertLanguageFileConvention(string path, bool hasBom, string xmlDeclaration)
        {
            byte[] bytes = File.ReadAllBytes(path);
            bool actualBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            Assert.Equal(hasBom, actualBom);

            string source = File.ReadAllText(path);
            Assert.StartsWith(xmlDeclaration, source, StringComparison.Ordinal);
        }

        private static HashSet<string> FindCodeTextIds()
        {
            string[] sourceFiles = Directory
                .EnumerateFiles(RepositoryPaths.FromRoot("Campaign"), "*.cs", SearchOption.AllDirectories)
                .Append(RepositoryPaths.FromRoot("SubModule.cs"))
                .ToArray();
            return sourceFiles
                .SelectMany(path => TextIdPattern.Matches(File.ReadAllText(path)).Cast<Match>())
                .Select(match => match.Groups["id"].Value)
                .ToHashSet(StringComparer.Ordinal);
        }

        private static Dictionary<string, string> ReadEntries(string path)
        {
            XDocument document = XDocument.Load(path);
            XElement[] entries = document.Descendants("string").ToArray();
            string[] duplicates = entries
                .GroupBy(entry => (string?)entry.Attribute("id"), StringComparer.Ordinal)
                .Where(group => group.Key is null || group.Count() > 1)
                .Select(group => group.Key ?? "<missing id>")
                .OrderBy(id => id)
                .ToArray();

            Assert.True(duplicates.Length == 0, $"{path} contains duplicate or missing IDs: {string.Join(", ", duplicates)}");

            return entries.ToDictionary(
                entry => (string)entry.Attribute("id")!,
                entry => (string?)entry.Attribute("text") ?? string.Empty,
                StringComparer.Ordinal);
        }

        private static string[] Placeholders(string text) =>
            PlaceholderPattern.Matches(text)
                .Cast<Match>()
                .Select(match => match.Groups["name"].Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

        private static bool IsLanguageSpecificPluralSuffix(string placeholder) =>
            placeholder.EndsWith("PLURAL", StringComparison.Ordinal);

        private static string LanguageFile(string? language = null) =>
            language is null
                ? RepositoryPaths.FromRoot("_Module", "ModuleData", "Languages", "std_module_strings_xml.xml")
                : RepositoryPaths.FromRoot("_Module", "ModuleData", "Languages", language, "std_module_strings_xml.xml");
    }
}
