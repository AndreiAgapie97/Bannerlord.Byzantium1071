using Byzantium1071.Campaign;
using FsCheck.Xunit;
using Xunit;

namespace Byzantium1071.Tests
{
    public sealed class ServiceMathTests
    {
        [Theory]
        [InlineData(0, 1, 63)]
        [InlineData(0, 6, 336)]
        [InlineData(1, 1, 42)]
        [InlineData(1, 6, 252)]
        [InlineData(2, 1, 28)]
        [InlineData(2, 6, 168)]
        [InlineData(3, 1, 31)]
        [InlineData(3, 6, 186)]
        public void BaseServiceDaysUsesTheSelectedPreset(int preset, int tier, int expected)
        {
            FakeSettings settings = ServiceSettings();
            settings.DemobilizationIntensityPreset = preset;

            Assert.Equal(expected, B1071_ServiceMath.BaseServiceDays(tier, settings));
        }

        [Fact]
        public void ServiceThresholdCompoundsSeasonAndCrisisUsingIntegerPercentages()
        {
            FakeSettings settings = ServiceSettings();
            settings.EnableDemobilizationSeasonality = true;
            settings.DemobilizationSpringSummerThresholdPercent = 110;
            settings.EnableDemobilizationCrisisCompression = true;
            settings.DemobilizationCrisisThresholdPercent = 90;

            int days = B1071_ServiceMath.ServiceThresholdDays(1, B1071Season.Spring, true, settings);

            Assert.Equal(41, days);
        }

        [Fact]
        public void ServiceThresholdClampsInvalidTierAndNeverFallsBelowOneDay()
        {
            FakeSettings settings = ServiceSettings();
            settings.DemobilizationIntensityPreset = 3;
            settings.DemobilizationT1ServiceDays = 0;

            Assert.Equal(1, B1071_ServiceMath.ServiceThresholdDays(-1, B1071Season.Winter, false, settings));
        }

        [Fact]
        public void ExtensionCostIncreasesWithEachPriorExtensionAndSaturates()
        {
            FakeSettings settings = ServiceSettings();
            settings.DemobilizationExtensionDays = 21;
            settings.DemobilizationExtensionGoldPerTierDay = 2;

            Assert.Equal(210, B1071_ServiceMath.ExtensionCost(5, 1, 0, settings));
            Assert.Equal(315, B1071_ServiceMath.ExtensionCost(5, 1, 1, settings));
            Assert.Equal(int.MaxValue, B1071_ServiceMath.ExtensionCost(6, int.MaxValue, int.MaxValue, settings));
        }

        [Fact]
        public void RecallAndRetirementCostsRespectTheirBounds()
        {
            FakeSettings settings = ServiceSettings();
            settings.DemobilizationRecallGoldPerTier = 40;

            Assert.Equal(240, B1071_ServiceMath.RecallGoldCost(7, 1, settings));
            Assert.Equal(1, B1071_ServiceMath.DailyRetirementCap(1, 1));
            Assert.Equal(8, B1071_ServiceMath.DailyRetirementCap(100, 8));
        }

        [Fact]
        public void TravelEstimatesUseConfiguredMinimumSpeedsAndCeilingDays()
        {
            FakeSettings settings = ServiceSettings();
            settings.DemobilizationCourierSpeed = 120;
            settings.DemobilizationMarchSpeed = 60;

            Assert.Equal(3, B1071_ServiceMath.EstimateRecallDays(100f, settings));
            Assert.Equal(2, B1071_ServiceMath.EstimateArrivalDays(90f, 60f, settings));

            settings.DemobilizationCourierSpeed = 0;
            settings.DemobilizationMarchSpeed = 0;
            Assert.Equal(2, B1071_ServiceMath.EstimateRecallDays(1f, settings));
        }

        [Fact]
        public void VeteranReturnUsesTheInjectedRolls()
        {
            int arrived = B1071_ServiceMath.VeteranReturnCount(
                4,
                50,
                new FakeRandom(integers: new[] { 0, 49, 50, 99 }));

            Assert.Equal(2, arrived);
            Assert.Equal(3, B1071_ServiceMath.VeteranReturnCount(3, 100, new FakeRandom()));
        }

        [Fact]
        public void ScatterUsesAnExtraRollOnlyForFractionalLosses()
        {
            Assert.Equal(5, B1071_ServiceMath.ScatterCount(10, 50, new FakeRandom()));
            Assert.Equal(1, B1071_ServiceMath.ScatterCount(1, 50, new FakeRandom(integers: new[] { 49 })));
            Assert.Equal(0, B1071_ServiceMath.ScatterCount(1, 50, new FakeRandom(integers: new[] { 50 })));
        }

        [Fact]
        public void PartialDeliveryProratesOnlyWhatRemainsOnTheLedger()
        {
            PendingRecallBalance balance = B1071_ServiceMath.ProrateAfterDeparture(
                orderedCount: 20,
                departedCount: 7,
                remainingCount: 13,
                goldPaid: 1_000,
                manpowerDrawn: 20);

            Assert.Equal(650, balance.GoldPaid);
            Assert.Equal(13, balance.ManpowerDrawn);
        }

        [Property(MaxTest = 1000)]
        public bool ProrationNeverCreatesOrLosesRecordedValue(
            int rawOrdered,
            int rawDeparted,
            int rawGold,
            int rawManpower)
        {
            int ordered = Positive(rawOrdered);
            int departed = NonNegative(rawDeparted) % ordered;
            int remaining = ordered - departed;
            int gold = NonNegative(rawGold) % 1_000_000;
            int manpower = NonNegative(rawManpower) % 1_000_000;
            PendingRecallBalance balance = B1071_ServiceMath.ProrateAfterDeparture(
                ordered,
                departed,
                remaining,
                gold,
                manpower);

            return balance.GoldPaid >= 0
                && balance.GoldPaid <= gold
                && balance.ManpowerDrawn >= 0
                && balance.ManpowerDrawn <= manpower;
        }

        [Fact]
        public void ServiceCohortSaveRowsRoundTripAndRetainEachExtensionCount()
        {
            var partyIds = new System.Collections.Generic.List<string>();
            var troopIds = new System.Collections.Generic.List<string>();
            var joinDays = new System.Collections.Generic.List<int>();
            var counts = new System.Collections.Generic.List<int>();
            var extendedFlags = new System.Collections.Generic.List<bool>();
            var extensionCounts = new System.Collections.Generic.List<int>();
            var homeIds = new System.Collections.Generic.List<string>();
            var originClanIds = new System.Collections.Generic.List<string>();
            var employerClanIds = new System.Collections.Generic.List<string>();

            B1071_ServiceMath.AppendServiceCohortRows(
                partyIds,
                troopIds,
                joinDays,
                counts,
                extendedFlags,
                extensionCounts,
                homeIds,
                originClanIds,
                employerClanIds,
                "party_a",
                "troop_a",
                42,
                2,
                3,
                "home_a",
                "origin_a",
                "employer_a");

            var rows = B1071_ServiceMath.ReadServiceCohortRows(
                partyIds,
                troopIds,
                joinDays,
                counts,
                extendedFlags,
                extensionCounts,
                homeIds,
                originClanIds,
                employerClanIds);

            Assert.Equal(2, rows.Count);
            Assert.All(rows, row =>
            {
                Assert.Equal("party_a", row.PartyId);
                Assert.Equal("troop_a", row.TroopId);
                Assert.Equal(42, row.JoinDay);
                Assert.Equal(1, row.Count);
                Assert.Equal(3, row.ExtensionCount);
                Assert.Equal("home_a", row.HomeId);
                Assert.Equal("origin_a", row.OriginClanId);
                Assert.Equal("employer_a", row.EmployerClanId);
            });
        }

        [Fact]
        public void ServiceCohortSaveRowsIgnoreTruncatedEntriesAndReadLegacyExtensionFlags()
        {
            var rows = B1071_ServiceMath.ReadServiceCohortRows(
                new[] { "party_a", "party_b" },
                new[] { "troop_a" },
                new[] { 42 },
                new[] { 1 },
                new[] { true },
                System.Array.Empty<int>(),
                System.Array.Empty<string>());

            ServiceCohortSaveRow row = Assert.Single(rows);
            Assert.Equal(1, row.ExtensionCount);
            Assert.Equal(string.Empty, row.HomeId);
            Assert.Equal(string.Empty, row.OriginClanId);
            Assert.Equal(string.Empty, row.EmployerClanId);
        }

        [Fact]
        public void TransferReserveRowsRoundTripPerSoldierAndRetainExtensions()
        {
            var troopIds = new System.Collections.Generic.List<string>();
            var joinDays = new System.Collections.Generic.List<int>();
            var storedDays = new System.Collections.Generic.List<int>();
            var counts = new System.Collections.Generic.List<int>();
            var extendedFlags = new System.Collections.Generic.List<bool>();
            var extensionCounts = new System.Collections.Generic.List<int>();
            var homeIds = new System.Collections.Generic.List<string>();
            var originClanIds = new System.Collections.Generic.List<string>();
            var employerClanIds = new System.Collections.Generic.List<string>();
            var sourcePartyIds = new System.Collections.Generic.List<string>();

            B1071_ServiceMath.AppendTransferReserveRows(
                troopIds, joinDays, storedDays, counts, extendedFlags, extensionCounts, homeIds,
                originClanIds, employerClanIds, sourcePartyIds,
                "troop_a", 5, 9, 2, 3, "home_a", "origin_a", "employer_a", "party_a");
            var rows = B1071_ServiceMath.ReadTransferReserveRows(
                troopIds, joinDays, storedDays, counts, extendedFlags, extensionCounts, homeIds,
                originClanIds, employerClanIds, sourcePartyIds);

            Assert.Equal(2, rows.Count);
            Assert.All(rows, row =>
            {
                Assert.Equal("troop_a", row.TroopId);
                Assert.Equal(5, row.JoinDay);
                Assert.Equal(9, row.StoredDay);
                Assert.Equal(1, row.Count);
                Assert.Equal(3, row.ExtensionCount);
                Assert.Equal("home_a", row.HomeId);
                Assert.Equal("origin_a", row.OriginClanId);
                Assert.Equal("employer_a", row.EmployerClanId);
                Assert.Equal("party_a", row.SourcePartyId);
            });
        }

        [Fact]
        public void TransferReserveRowsReadLegacyFlagsAndIgnoreMalformedRows()
        {
            var rows = B1071_ServiceMath.ReadTransferReserveRows(
                new[] { "troop_a", "" },
                new[] { 5, 6 },
                new[] { 9, 10 },
                new[] { 1, 1 },
                new[] { true },
                System.Array.Empty<int>(),
                System.Array.Empty<string>());

            TransferReserveSaveRow row = Assert.Single(rows);
            Assert.Equal(1, row.ExtensionCount);
            Assert.Equal(string.Empty, row.HomeId);
            Assert.Equal(string.Empty, row.EmployerClanId);
        }

        [Fact]
        public void VeteranRowsRoundTripAndDefaultOldSavesToNonPlayerOwnership()
        {
            var settlementIds = new System.Collections.Generic.List<string>();
            var troopIds = new System.Collections.Generic.List<string>();
            var dischargeDays = new System.Collections.Generic.List<int>();
            var counts = new System.Collections.Generic.List<int>();
            var fromPlayer = new System.Collections.Generic.List<bool>();
            var originClanIds = new System.Collections.Generic.List<string>();
            var employerClanIds = new System.Collections.Generic.List<string>();

            B1071_ServiceMath.AppendVeteranRow(
                settlementIds, troopIds, dischargeDays, counts, fromPlayer, originClanIds, employerClanIds,
                "settlement_a", "troop_a", 12, 4, true, "origin_a", "employer_a");
            var rows = B1071_ServiceMath.ReadVeteranRows(
                settlementIds, troopIds, dischargeDays, counts, fromPlayer, originClanIds, employerClanIds);

            VeteranSaveRow row = Assert.Single(rows);
            Assert.Equal("settlement_a", row.SettlementId);
            Assert.Equal("troop_a", row.TroopId);
            Assert.Equal(12, row.DischargeDay);
            Assert.Equal(4, row.Count);
            Assert.True(row.FromPlayer);
            Assert.Equal("origin_a", row.OriginClanId);
            Assert.Equal("employer_a", row.EmployerClanId);
            Assert.False(Assert.Single(B1071_ServiceMath.ReadVeteranRows(
                settlementIds, troopIds, dischargeDays, counts, System.Array.Empty<bool>())).FromPlayer);
        }

        [Fact]
        public void PendingRecallRowsRoundTripAndSafelyDefaultOldFields()
        {
            var orderIds = new System.Collections.Generic.List<int>();
            var settlementIds = new System.Collections.Generic.List<string>();
            var troopIds = new System.Collections.Generic.List<string>();
            var counts = new System.Collections.Generic.List<int>();
            var orderDays = new System.Collections.Generic.List<int>();
            var goldPaid = new System.Collections.Generic.List<int>();
            var manpowerDrawn = new System.Collections.Generic.List<int>();
            var playerOwned = new System.Collections.Generic.List<int>();
            var courier = new System.Collections.Generic.List<float>();
            var posX = new System.Collections.Generic.List<float>();
            var posY = new System.Collections.Generic.List<float>();

            B1071_ServiceMath.AppendPendingRecallRow(
                orderIds, settlementIds, troopIds, counts, orderDays, goldPaid, manpowerDrawn,
                playerOwned, courier, posX, posY,
                4, "settlement_a", "troop_a", 3, 8, 90, 2, 5, 12.5f, 4.5f, 7.5f);
            PendingRecallSaveRow row = Assert.Single(B1071_ServiceMath.ReadPendingRecallRows(
                orderIds, settlementIds, troopIds, counts, orderDays, goldPaid, manpowerDrawn,
                playerOwned, courier, posX, posY, fallbackOrderDay: 99));

            Assert.Equal(4, row.OrderId);
            Assert.Equal(0, row.SourceIndex);
            Assert.Equal(3, row.PlayerOwnedCount);
            Assert.Equal(4.5f, row.PosX);
            Assert.Equal(7.5f, row.PosY);

            PendingRecallSaveRow legacy = Assert.Single(B1071_ServiceMath.ReadPendingRecallRows(
                System.Array.Empty<int>(),
                new[] { "settlement_b" },
                new[] { "troop_b" },
                new[] { 2 },
                System.Array.Empty<int>(),
                System.Array.Empty<int>(),
                System.Array.Empty<int>(),
                System.Array.Empty<int>(),
                System.Array.Empty<float>(),
                System.Array.Empty<float>(),
                System.Array.Empty<float>(),
                fallbackOrderDay: 77));

            Assert.Equal(0, legacy.OrderId);
            Assert.Equal(0, legacy.SourceIndex);
            Assert.Equal(77, legacy.OrderDay);
            Assert.Equal(0, legacy.PlayerOwnedCount);
            Assert.True(float.IsNaN(legacy.PosX));
            Assert.True(float.IsNaN(legacy.PosY));
        }

        [Fact]
        public void PendingRecallBatchGroupsKeepRawHeaderAlignmentWhenInvalidHeadersAreSkipped()
        {
            string[] settlementIds = { string.Empty, "settlement_b" };
            string[] troopIds = { "troop_a", "troop_b" };
            int[] counts = { 1, 2 };

            PendingRecallSaveRow row = Assert.Single(B1071_ServiceMath.ReadPendingRecallRows(
                orderIds: new[] { 1, 2 },
                settlementIds: settlementIds,
                troopIds: troopIds,
                counts: counts,
                orderDays: System.Array.Empty<int>(),
                goldPaid: System.Array.Empty<int>(),
                manpowerDrawn: System.Array.Empty<int>(),
                playerOwnedCounts: System.Array.Empty<int>(),
                courierRemaining: System.Array.Empty<float>(),
                posX: System.Array.Empty<float>(),
                posY: System.Array.Empty<float>(),
                fallbackOrderDay: 0));
            var groups = B1071_ServiceMath.ReadRecallBatchGroups(
                B1071_ServiceMath.GetPendingRecallHeaderCount(settlementIds, troopIds, counts),
                batchesPerOrder: new[] { 1, 1 },
                originClanIds: new[] { "origin_a", "origin_b" },
                employerClanIds: new[] { "employer_a", "employer_b" },
                counts: new[] { 1, 2 });

            Assert.Equal(1, row.SourceIndex);
            Assert.Equal("employer_b", Assert.Single(groups[row.SourceIndex]).EmployerClanId);
        }

        [Fact]
        public void RecallBatchRowsStayAlignedByHeaderOrderAndFailClosedWhenTruncated()
        {
            var originClanIds = new System.Collections.Generic.List<string>();
            var employerClanIds = new System.Collections.Generic.List<string>();
            var counts = new System.Collections.Generic.List<int>();
            B1071_ServiceMath.AppendRecallBatchRow(originClanIds, employerClanIds, counts, "origin_a", "employer_a", 2);
            B1071_ServiceMath.AppendRecallBatchRow(originClanIds, employerClanIds, counts, "origin_b", "employer_b", 3);

            var groups = B1071_ServiceMath.ReadRecallBatchGroups(
                headerCount: 2,
                batchesPerOrder: new[] { 1, 1 },
                originClanIds: originClanIds,
                employerClanIds: employerClanIds,
                counts: counts);

            Assert.Equal("employer_a", Assert.Single(groups[0]).EmployerClanId);
            Assert.Equal("employer_b", Assert.Single(groups[1]).EmployerClanId);

            var truncated = B1071_ServiceMath.ReadRecallBatchGroups(
                headerCount: 2,
                batchesPerOrder: new[] { 2, 1 },
                originClanIds: new[] { "origin_a" },
                employerClanIds: new[] { "employer_a" },
                counts: new[] { 2 });
            Assert.Single(truncated[0]);
            Assert.Empty(truncated[1]);
        }

        [Fact]
        public void RecallBatchRowsReportWhetherTheyWereActuallyAppended()
        {
            var originClanIds = new System.Collections.Generic.List<string>();
            var employerClanIds = new System.Collections.Generic.List<string>();
            var counts = new System.Collections.Generic.List<int>();

            Assert.False(B1071_ServiceMath.AppendRecallBatchRow(
                originClanIds, employerClanIds, counts, "origin_a", "employer_a", 0));
            Assert.True(B1071_ServiceMath.AppendRecallBatchRow(
                originClanIds, employerClanIds, counts, "origin_b", "employer_b", 2));

            Assert.Single(originClanIds);
            Assert.Single(employerClanIds);
            Assert.Single(counts);
        }

        [Fact]
        public void LargestRemainderScatterKeepsTotalAndUsesOldestBatchForTies()
        {
            var allocation = B1071_ServiceMath.AllocateLargestRemainder(2, new[] { 1, 1, 1 });

            Assert.Equal(new[] { 1, 1, 0 }, allocation);
            Assert.Equal(2, allocation[0] + allocation[1] + allocation[2]);
        }

        [Fact]
        public void TenThousandServiceCohortsRoundTripWithEveryParallelFieldAligned()
        {
            var partyIds = new System.Collections.Generic.List<string>();
            var troopIds = new System.Collections.Generic.List<string>();
            var joinDays = new System.Collections.Generic.List<int>();
            var counts = new System.Collections.Generic.List<int>();
            var extendedFlags = new System.Collections.Generic.List<bool>();
            var extensionCounts = new System.Collections.Generic.List<int>();
            var homeIds = new System.Collections.Generic.List<string>();
            var originClanIds = new System.Collections.Generic.List<string>();
            var employerClanIds = new System.Collections.Generic.List<string>();

            for (int index = 0; index < 10_000; index++)
            {
                B1071_ServiceMath.AppendServiceCohortRows(
                    partyIds, troopIds, joinDays, counts, extendedFlags, extensionCounts, homeIds,
                    originClanIds, employerClanIds,
                    "party_" + index, "troop_" + index, index, 1, index % 4, "home_" + index,
                    "origin_" + index, "employer_" + index);
            }

            var rows = B1071_ServiceMath.ReadServiceCohortRows(
                partyIds, troopIds, joinDays, counts, extendedFlags, extensionCounts, homeIds,
                originClanIds, employerClanIds);

            Assert.Equal(10_000, rows.Count);
            for (int index = 0; index < rows.Count; index++)
            {
                ServiceCohortSaveRow row = rows[index];
                Assert.Equal("party_" + index, row.PartyId);
                Assert.Equal("troop_" + index, row.TroopId);
                Assert.Equal(index, row.JoinDay);
                Assert.Equal(index % 4, row.ExtensionCount);
                Assert.Equal("home_" + index, row.HomeId);
                Assert.Equal("origin_" + index, row.OriginClanId);
                Assert.Equal("employer_" + index, row.EmployerClanId);
            }
        }

        private static FakeSettings ServiceSettings() =>
            new()
            {
                DemobilizationIntensityPreset = 1,
                DemobilizationT1ServiceDays = 31,
                DemobilizationT2ServiceDays = 62,
                DemobilizationT3ServiceDays = 93,
                DemobilizationT4ServiceDays = 124,
                DemobilizationT5ServiceDays = 155,
                DemobilizationT6ServiceDays = 186,
                DemobilizationMaxExtensions = 3,
                DemobilizationExtensionDays = 21,
                DemobilizationExtensionGoldPerTierDay = 2,
                DemobilizationRecallGoldPerTier = 40,
                DemobilizationCourierSpeed = 120,
                DemobilizationMarchSpeed = 60
            };

        private static int Positive(int value) => NonNegative(value % 1_000) + 1;

        private static int NonNegative(int value) => value == int.MinValue ? 0 : System.Math.Abs(value);
    }
}
