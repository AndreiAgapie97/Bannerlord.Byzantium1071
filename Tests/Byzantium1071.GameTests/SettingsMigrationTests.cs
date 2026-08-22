using System;
using System.Collections.Generic;
using System.Linq;
using Byzantium1071.Campaign.Settings;
using Xunit;

namespace Byzantium1071.GameTests
{
    public sealed class SettingsMigrationTests
    {
        [Fact]
        public void SettingsInterfaceMatchesSettingsClassByReflection()
        {
            string[] interfaceProperties = typeof(IB1071Settings)
                .GetProperties()
                .Select(property => $"{property.PropertyType.FullName} {property.Name}")
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            string[] settingsProperties = typeof(B1071_McmSettings)
                .GetProperties()
                .Where(property => property.CanRead && property.CanWrite)
                .Select(property => $"{property.PropertyType.FullName} {property.Name}")
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(interfaceProperties, settingsProperties);
        }

        [Fact]
        public void MigrationIsIdempotentAndLeavesNewProfilesCurrent()
        {
            B1071_McmSettings settings = new();

            settings.MigrateToLatestProfile();
            Dictionary<string, object?> afterFirstMigration = Snapshot(settings);
            string? secondMigration = settings.MigrateToLatestProfile();

            Assert.Null(secondMigration);
            Assert.Equal(B1071_McmSettings.LATEST_PROFILE_VERSION, settings.SettingsProfileVersion);
            AssertSnapshotsEqual(afterFirstMigration, Snapshot(settings));
        }

        [Fact]
        public void EveryHistoricalProfileVersionConvergesOnCurrentDefaults()
        {
            B1071_McmSettings baseline = new();
            baseline.SettingsProfileVersion = 0;
            baseline.MigrateToLatestProfile();
            Dictionary<string, object?> expected = Snapshot(baseline);

            for (int version = 0; version < B1071_McmSettings.LATEST_PROFILE_VERSION; version++)
            {
                B1071_McmSettings settings = new() { SettingsProfileVersion = version };
                settings.MigrateToLatestProfile();

                Assert.Equal(B1071_McmSettings.LATEST_PROFILE_VERSION, settings.SettingsProfileVersion);
                AssertSnapshotsEqual(expected, Snapshot(settings));
            }
        }

        private static Dictionary<string, object?> Snapshot(B1071_McmSettings settings) =>
            typeof(B1071_McmSettings)
                .GetProperties()
                .Where(property => property.CanRead && property.CanWrite)
                .ToDictionary(property => property.Name, property => (object?)property.GetValue(settings), StringComparer.Ordinal);

        private static void AssertSnapshotsEqual(
            IReadOnlyDictionary<string, object?> expected,
            IReadOnlyDictionary<string, object?> actual)
        {
            Assert.Equal(expected.Count, actual.Count);

            foreach (KeyValuePair<string, object?> entry in expected)
            {
                Assert.True(actual.TryGetValue(entry.Key, out object? actualValue), $"Missing property {entry.Key}.");
                Assert.Equal(entry.Value, actualValue);
            }
        }
    }
}
