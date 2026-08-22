using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Byzantium1071.Tests
{
    public sealed class SettingsContractTests
    {
        private static readonly Regex PublicAutoPropertyPattern = new(
            @"public\s+(?<type>bool|int|float|string|double|long)\??\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\{\s*get;\s*set;\s*\}",
            RegexOptions.Compiled);
        private static readonly Regex InterfacePropertyPattern = new(
            @"(?<type>bool|int|float|string|double|long)\??\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\{\s*get;\s*set;\s*\}",
            RegexOptions.Compiled);
        private static readonly Regex NumericSettingPattern = new(
            @"^\s*\[SettingProperty(?:Integer|FloatingInteger)\(.*?,\s*(?<min>-?\d+(?:\.\d+)?f?)\s*,\s*(?<max>-?\d+(?:\.\d+)?f?)\s*,",
            RegexOptions.Compiled);
        private static readonly Regex NumericPropertyDefaultPattern = new(
            @"^\s*public\s+(?:int|float)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\{\s*get;\s*set;\s*\}\s*=\s*(?<value>-?\d+(?:\.\d+)?f?)\s*;",
            RegexOptions.Compiled);
        private static readonly Regex SettingTextIdPattern = new(
            @"\[SettingProperty(?:Integer|FloatingInteger|Bool)\(\s*""\{=(?<id>[^}]+)\}",
            RegexOptions.Compiled);
        private static readonly Regex DirectSettingsDivisionPattern = new(
            @"/\s*(?:\([^)]*\)\s*)?Settings\.(?<name>[A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.Compiled);

        [Fact]
        public void SettingsInterfaceMatchesSettingsClass()
        {
            HashSet<string> settingsClassProperties = ReadPropertySignatures(
                RepositoryPaths.FromRoot("Campaign", "Settings", "B1071_McmSettings.cs"),
                PublicAutoPropertyPattern);
            HashSet<string> interfaceProperties = ReadPropertySignatures(
                RepositoryPaths.FromRoot("Campaign", "Settings", "IB1071Settings.cs"),
                InterfacePropertyPattern);

            string[] missingFromInterface = settingsClassProperties.Except(interfaceProperties).OrderBy(property => property).ToArray();
            string[] missingFromClass = interfaceProperties.Except(settingsClassProperties).OrderBy(property => property).ToArray();

            Assert.True(
                missingFromInterface.Length == 0 && missingFromClass.Length == 0,
                $"Interface/class mismatch. Missing from interface: {string.Join(", ", missingFromInterface)}. Missing from class: {string.Join(", ", missingFromClass)}.");
        }

        [Fact]
        public void NumericSettingDefaultsAreWithinDeclaredRanges()
        {
            string[] lines = File.ReadAllLines(RepositoryPaths.FromRoot("Campaign", "Settings", "B1071_McmSettings.cs"));
            List<string> failures = new();

            for (int index = 0; index < lines.Length - 1; index++)
            {
                Match range = NumericSettingPattern.Match(lines[index]);
                if (!range.Success)
                {
                    continue;
                }

                Match property = NumericPropertyDefaultPattern.Match(lines[index + 1]);
                if (!property.Success)
                {
                    failures.Add($"Could not read the numeric property following line {index + 1}.");
                    continue;
                }

                float minimum = ParseFloat(range.Groups["min"].Value);
                float maximum = ParseFloat(range.Groups["max"].Value);
                float value = ParseFloat(property.Groups["value"].Value);
                if (value < minimum || value > maximum)
                {
                    failures.Add($"{property.Groups["name"].Value} default {value} is outside [{minimum}, {maximum}].");
                }
            }

            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        }

        [Fact]
        public void SettingLabelsUseUniqueTextIds()
        {
            string source = File.ReadAllText(RepositoryPaths.FromRoot("Campaign", "Settings", "B1071_McmSettings.cs"));
            string[] duplicates = SettingTextIdPattern.Matches(source)
                .Cast<Match>()
                .Select(match => match.Groups["id"].Value)
                .GroupBy(id => id, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .OrderBy(id => id)
                .ToArray();

            Assert.True(duplicates.Length == 0, $"Duplicate setting label IDs: {string.Join(", ", duplicates)}");
        }

        [Fact]
        public void SettingsUsedAsDirectDivisorsCannotBeZero()
        {
            Dictionary<string, float> minimums = ReadNumericMinimums();
            string campaignDirectory = RepositoryPaths.FromRoot("Campaign");
            string[] divisorSettings = Directory.EnumerateFiles(campaignDirectory, "*.cs", SearchOption.AllDirectories)
                .SelectMany(path => DirectSettingsDivisionPattern.Matches(File.ReadAllText(path)).Cast<Match>())
                .Select(match => match.Groups["name"].Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            string[] missingRanges = divisorSettings.Where(name => !minimums.ContainsKey(name)).ToArray();
            string[] zeroPermitted = divisorSettings.Where(name => minimums.TryGetValue(name, out float minimum) && minimum <= 0f).ToArray();

            Assert.True(
                missingRanges.Length == 0 && zeroPermitted.Length == 0,
                $"Direct divisor settings need positive minimums. Missing ranges: {string.Join(", ", missingRanges)}. Zero-permitted: {string.Join(", ", zeroPermitted)}.");
        }

        private static HashSet<string> ReadPropertySignatures(string path, Regex pattern) =>
            pattern.Matches(File.ReadAllText(path))
                .Cast<Match>()
                .Select(match => $"{match.Groups["type"].Value} {match.Groups["name"].Value}")
                .ToHashSet(StringComparer.Ordinal);

        private static Dictionary<string, float> ReadNumericMinimums()
        {
            string[] lines = File.ReadAllLines(RepositoryPaths.FromRoot("Campaign", "Settings", "B1071_McmSettings.cs"));
            Dictionary<string, float> minimums = new(StringComparer.Ordinal);

            for (int index = 0; index < lines.Length - 1; index++)
            {
                Match range = NumericSettingPattern.Match(lines[index]);
                Match property = NumericPropertyDefaultPattern.Match(lines[index + 1]);
                if (range.Success && property.Success)
                {
                    minimums.Add(property.Groups["name"].Value, ParseFloat(range.Groups["min"].Value));
                }
            }

            return minimums;
        }

        private static float ParseFloat(string value) =>
            float.Parse(value.TrimEnd('f'), CultureInfo.InvariantCulture);
    }
}
