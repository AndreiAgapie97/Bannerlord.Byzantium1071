using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Byzantium1071.Tests
{
    /// <summary>
    /// One numeric setting as the MCM attribute declares it: the slider's two ends and the value a
    /// fresh install starts at. A setting with no slider (a dropdown, or a value the player cannot
    /// reach) reports its default for all three.
    /// </summary>
    internal readonly struct DeclaredSettingRange
    {
        internal DeclaredSettingRange(string name, bool isInteger, float minimum, float maximum, float defaultValue)
        {
            Name = name;
            IsInteger = isInteger;
            Minimum = minimum;
            Maximum = maximum;
            DefaultValue = defaultValue;
        }

        internal string Name { get; }
        internal bool IsInteger { get; }
        internal float Minimum { get; }
        internal float Maximum { get; }
        internal float DefaultValue { get; }
    }

    /// <summary>
    /// Reads the declared slider ranges straight out of the settings source. The fast suite cannot
    /// reference the MCM attributes themselves (they live behind the game assemblies), so the
    /// declaration is read as text — the same approach the settings contract tests already use.
    /// </summary>
    internal static class DeclaredSettingRanges
    {
        private static readonly Regex RangePattern = new(
            @"^\s*\[SettingProperty(?<kind>Integer|FloatingInteger)\(.*?,\s*(?<min>-?\d+(?:\.\d+)?f?)\s*,\s*(?<max>-?\d+(?:\.\d+)?f?)\s*,",
            RegexOptions.Compiled);

        private static readonly Regex NumericPropertyPattern = new(
            @"^\s*public\s+(?<type>int|float)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\{\s*get;\s*set;\s*\}\s*(?:=\s*(?<value>-?\d+(?:\.\d+)?f?)\s*;)?",
            RegexOptions.Compiled);

        private static readonly Lazy<IReadOnlyList<DeclaredSettingRange>> Cached =
            new(Read, isThreadSafe: true);

        internal static IReadOnlyList<DeclaredSettingRange> All => Cached.Value;

        private static IReadOnlyList<DeclaredSettingRange> Read()
        {
            string[] lines = File.ReadAllLines(
                RepositoryPaths.FromRoot("Campaign", "Settings", "B1071_McmSettings.cs"));
            List<DeclaredSettingRange> ranges = new();

            for (int index = 0; index < lines.Length; index++)
            {
                Match property = NumericPropertyPattern.Match(lines[index]);
                if (!property.Success) continue;

                float defaultValue = property.Groups["value"].Success
                    ? ParseFloat(property.Groups["value"].Value)
                    : 0f;
                bool isInteger = property.Groups["type"].Value == "int";

                // The slider attribute sits on the line directly above the property it decorates.
                Match range = index > 0 ? RangePattern.Match(lines[index - 1]) : Match.Empty;
                if (range.Success)
                {
                    ranges.Add(new DeclaredSettingRange(
                        property.Groups["name"].Value,
                        isInteger,
                        ParseFloat(range.Groups["min"].Value),
                        ParseFloat(range.Groups["max"].Value),
                        defaultValue));
                }
                else
                {
                    // No slider: the player is stuck with whatever the mod ships.
                    ranges.Add(new DeclaredSettingRange(
                        property.Groups["name"].Value, isInteger, defaultValue, defaultValue, defaultValue));
                }
            }

            return ranges;
        }

        private static float ParseFloat(string value) =>
            float.Parse(value.TrimEnd('f'), CultureInfo.InvariantCulture);
    }
}
