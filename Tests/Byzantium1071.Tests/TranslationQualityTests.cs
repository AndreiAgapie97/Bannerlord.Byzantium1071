using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Byzantium1071.Tests
{
    public sealed class TranslationQualityTests
    {
        private static readonly Regex LatinLetterPattern = new(@"[A-Za-z]", RegexOptions.Compiled);
        private static readonly Regex CjkPattern = new(@"[\u3400-\u4DBF\u4E00-\u9FFF\uF900-\uFAFF]", RegexOptions.Compiled);
        private static readonly Regex PlaceholderPattern = new(@"{[^}]*}", RegexOptions.Compiled);
        private static readonly Regex WordPattern = new(@"[A-Za-z]+", RegexOptions.Compiled);
        private static readonly HashSet<string> EnglishFunctionWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "and", "are", "at", "by", "for", "from", "in", "is", "of", "on", "that", "the", "this", "to", "when", "with", "you", "your"
        };
        private static readonly HashSet<string> IntentionalChineseLatinOnlyIds = new(StringComparer.Ordinal)
        {
            "b1071_mod_name",
            "b1071_mcm_display_name",
            "b1071_ui_close",
            "b1071_overlay_band_value"
        };

        public static IEnumerable<object[]> EuropeanLanguages()
        {
            yield return new object[] { "French" };
            yield return new object[] { "German" };
        }

        [Fact]
        public void ChineseEntriesWithLatinTextContainChineseUnlessExplicitlyLanguageNeutral()
        {
            string[] untranslated = ReadEntries("Chinese")
                .Where(entry => LatinLetterPattern.IsMatch(entry.Value) && !CjkPattern.IsMatch(entry.Value))
                .Select(entry => entry.Key)
                .Where(id => !IntentionalChineseLatinOnlyIds.Contains(id))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();

            Assert.True(
                untranslated.Length == 0,
                $"Chinese entries with Latin text and no CJK characters: {string.Join(", ", untranslated)}");
        }

        [Theory]
        [MemberData(nameof(EuropeanLanguages))]
        public void EuropeanTranslationsDoNotContainUntranslatedEnglishProse(string language)
        {
            IReadOnlyDictionary<string, string> english = ReadEntries();
            IReadOnlyDictionary<string, string> translation = ReadEntries(language);
            List<string> untranslated = new();

            foreach ((string id, string englishText) in english)
            {
                if (!translation.TryGetValue(id, out string? translatedText) || !LooksLikeUntranslatedEnglish(englishText, translatedText))
                {
                    continue;
                }

                untranslated.Add(id);
            }

            Assert.True(
                untranslated.Count == 0,
                $"{language} contains untranslated English prose: {string.Join(", ", untranslated)}");
        }

        private static bool LooksLikeUntranslatedEnglish(string englishText, string translatedText)
        {
            string[] englishWords = Words(englishText);
            string[] translatedWords = Words(translatedText);
            if (englishWords.Length < 3 || translatedWords.Length < 3)
            {
                return false;
            }

            int overlap = englishWords.Intersect(translatedWords, StringComparer.OrdinalIgnoreCase).Count();
            bool hasEnglishFunctionWord = translatedWords.Any(word => EnglishFunctionWords.Contains(word));

            return hasEnglishFunctionWord && overlap >= englishWords.Length * 0.6;
        }

        private static string[] Words(string text) =>
            WordPattern.Matches(PlaceholderPattern.Replace(text, string.Empty))
                .Cast<Match>()
                .Select(match => match.Value)
                .ToArray();

        private static IReadOnlyDictionary<string, string> ReadEntries(string? language = null)
        {
            string path = language is null
                ? RepositoryPaths.FromRoot("_Module", "ModuleData", "Languages", "std_module_strings_xml.xml")
                : RepositoryPaths.FromRoot("_Module", "ModuleData", "Languages", language, "std_module_strings_xml.xml");
            XDocument document = XDocument.Load(path);

            return document.Descendants("string").ToDictionary(
                entry => (string)entry.Attribute("id")!,
                entry => (string?)entry.Attribute("text") ?? string.Empty,
                StringComparer.Ordinal);
        }
    }
}
