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
        public void SettlementNameplateTooltipsAreEnabledByDefault()
        {
            Assert.True(new B1071_McmSettings().EnableSettlementNameplateTooltips);
        }

        [Fact]
        public void CrossClanVeteranRecruitmentDefaultsToOffWithoutMigration()
        {
            B1071_McmSettings newProfile = new();
            B1071_McmSettings existingProfile = new() { SettingsProfileVersion = B1071_McmSettings.LATEST_PROFILE_VERSION };

            existingProfile.MigrateToLatestProfile();

            Assert.False(newProfile.EnableDemobilizationVeteranCrossClanRecruitment);
            Assert.False(existingProfile.EnableDemobilizationVeteranCrossClanRecruitment);
        }

        [Fact]
        public void RecoveryPriorityDefaultsOnAndMigratesExistingProfilesOn()
        {
            B1071_McmSettings newProfile = new();
            B1071_McmSettings v25Profile = new()
            {
                SettingsProfileVersion = 25,
                AiRecoveryTakesPriorityOverNewTasks = false
            };

            v25Profile.MigrateToLatestProfile();

            Assert.True(newProfile.AiRecoveryTakesPriorityOverNewTasks);
            Assert.True(v25Profile.AiRecoveryTakesPriorityOverNewTasks);
        }

        [Fact]
        public void RecoveryIntentDurationDefaultsToOneDayAndMigratesExistingProfiles()
        {
            B1071_McmSettings newProfile = new();
            B1071_McmSettings v26Profile = new()
            {
                SettingsProfileVersion = 26,
                AiRecoveryIntentDurationDays = 12
            };

            v26Profile.MigrateToLatestProfile();

            Assert.Equal(1, newProfile.AiRecoveryIntentDurationDays);
            Assert.Equal(1, v26Profile.AiRecoveryIntentDurationDays);
        }

        /// <summary>
        /// Profile v28 installs the enabled trade preset once; later player choices survive.
        /// </summary>
        [Fact]
        public void SettlementRevenuePresetDefaultsAndMigratesExistingProfiles()
        {
            B1071_McmSettings fresh = new();
            B1071_McmSettings existing = new()
            {
                SettingsProfileVersion = 27,
                EnableSettlementRevenueTuning = false,
                SettlementTariffStrengthTown = 100,
                SettlementTariffCurveTown = 2.0f,
                SettlementTariffKneeTown = 0,
                SettlementTariffStrengthVillage = 100,
                SettlementTariffCurveVillage = 2.0f,
                SettlementTariffKneeVillage = 0,
                SettlementTaxStrength = 40,
                SettlementTaxCurve = 5.0f,
                SettlementTaxKnee = 2000,
                EnableAiRecoveryRouting = false
            };

            existing.MigrateToLatestProfile();

            Assert.Equal(28, existing.SettingsProfileVersion);
            Assert.False(existing.EnableAiRecoveryRouting);
            foreach (B1071_McmSettings settings in new[] { fresh, existing })
            {
                Assert.True(settings.EnableSettlementRevenueTuning);
                Assert.Equal(90, settings.SettlementTariffStrengthTown);
                Assert.Equal(90, settings.SettlementTariffStrengthVillage);
                Assert.Equal(100, settings.SettlementTaxStrength);
                Assert.Equal(1.0f, settings.SettlementTariffCurveTown);
                Assert.Equal(1.0f, settings.SettlementTariffCurveVillage);
                Assert.Equal(2.0f, settings.SettlementTaxCurve);
                Assert.Equal(2000, settings.SettlementTariffKneeTown);
                Assert.Equal(500, settings.SettlementTariffKneeVillage);
                Assert.Equal(0, settings.SettlementTaxKnee);
            }
        }

        [Fact]
        public void CurrentRevenueProfilePreservesPlayerChoices()
        {
            B1071_McmSettings settings = new() { SettingsProfileVersion = 27 };
            settings.MigrateToLatestProfile();
            settings.EnableSettlementRevenueTuning = false;
            settings.SettlementTariffStrengthTown = 75;
            settings.SettlementTariffKneeVillage = 800;
            settings.SettlementTaxStrength = 85;
            Dictionary<string, object?> customized = Snapshot(settings);

            Assert.Null(settings.MigrateToLatestProfile());
            AssertSnapshotsEqual(customized, Snapshot(settings));
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
