using System;
using Byzantium1071.Campaign;
using FsCheck.Xunit;
using Xunit;

namespace Byzantium1071.Tests
{
    /// <summary>
    /// Guards the daily telemetry aggregation. Nothing here changes gameplay, so these tests
    /// protect a different thing than the rest of the suite: they protect the ability to
    /// DIAGNOSE gameplay. A CSV whose header and rows drift apart, or a visit classifier that
    /// counts a full party as a wasted recruitment trip, produces numbers that look valid and
    /// point the next change in the wrong direction.
    /// </summary>
    public sealed class TelemetryMathTests
    {
        // --- Visit classification -------------------------------------------------------
        //
        // This is the load-bearing part. The weak/healthy split of wasted trips is what
        // decides whether AI recruitment reachability belongs inside the recovery routing
        // system or somewhere else, so a miscount here misdirects a whole release.

        [Fact]
        public void APartyThatArrivedFullIsNotSeekingRecruits()
        {
            Assert.Equal(
                B1071_VisitOutcome.NotSeeking,
                B1071_TelemetryMath.ClassifyVisit(membersOnEntry: 100, membersOnExit: 100, sizeLimit: 100));
        }

        [Fact]
        public void AnOverfullPartyIsNotSeekingEither()
        {
            // Parties exceed their limit after a battle capture or a garrison handover.
            Assert.Equal(
                B1071_VisitOutcome.NotSeeking,
                B1071_TelemetryMath.ClassifyVisit(120, 118, 100));
        }

        [Fact]
        public void AnUnknownSizeLimitIsNotCountedAsAWastedTrip()
        {
            Assert.Equal(
                B1071_VisitOutcome.NotSeeking,
                B1071_TelemetryMath.ClassifyVisit(10, 10, 0));
        }

        [Fact]
        public void LeavingFullerThanYouArrivedIsAGain()
        {
            Assert.Equal(
                B1071_VisitOutcome.Gained,
                B1071_TelemetryMath.ClassifyVisit(40, 55, 100));
        }

        [Theory]
        [InlineData(40, 40)]   // nothing to recruit
        [InlineData(40, 32)]   // left smaller: still no gain, and still a wasted trip
        public void RoomToFillAndNoGainIsAWastedTrip(int entry, int exit)
        {
            Assert.Equal(
                B1071_VisitOutcome.NoGain,
                B1071_TelemetryMath.ClassifyVisit(entry, exit, 100));
        }

        [Fact]
        public void TheWeakBucketUsesTheSameThresholdAsRecoveryRouting()
        {
            // If these ever diverge, "weak" stops meaning "recovery routing could have seen
            // this lord" and the bucket answers a question nobody asked.
            for (int members = 0; members <= 100; members++)
            {
                Assert.Equal(
                    B1071_AiRecoveryMath.ShouldStart(members, 100),
                    B1071_TelemetryMath.IsWeakEnoughForRecovery(members, 100));
            }
        }

        [Fact]
        public void TheWeakBucketBoundaryIsSixtyPercent()
        {
            Assert.True(B1071_TelemetryMath.IsWeakEnoughForRecovery(59, 100));
            Assert.False(B1071_TelemetryMath.IsWeakEnoughForRecovery(60, 100));
        }

        // --- Power ratio ----------------------------------------------------------------

        [Fact]
        public void PowerRatioIsOneWhenThereIsNoBaseline()
        {
            // An empty day must read as "no change", never as a spike -- this number is the
            // one being watched for the absolute-threshold side effects.
            Assert.Equal(1f, B1071_TelemetryMath.PowerRatio(0f, 0f));
            Assert.Equal(1f, B1071_TelemetryMath.PowerRatio(0f, 500f));
        }

        [Fact]
        public void PowerRatioDividesPatchedByVanilla()
        {
            Assert.Equal(1.02f, B1071_TelemetryMath.PowerRatio(1000f, 1020f), 4);
        }

        [Property]
        public bool PowerRatioIsAlwaysFinite(float vanilla, float patched)
        {
            float ratio = B1071_TelemetryMath.PowerRatio(vanilla, patched);
            return !float.IsNaN(ratio) && !float.IsInfinity(ratio);
        }

        [Theory]
        [InlineData(1, 0.66f)]
        [InlineData(3, 1.3f)]
        [InlineData(6, 2.56f)]
        public void VanillaTroopPowerMatchesTheGameCurve(int tier, float expected)
        {
            // (2 + tier) * (10 + tier) * 0.02f, as decompiled from
            // DefaultMilitaryPowerModel.GetDefaultTroopPower.
            Assert.Equal(expected, B1071_TelemetryMath.VanillaTroopPower(tier), 3);
        }

        // --- CSV shape ------------------------------------------------------------------

        [Fact]
        public void EveryRowHasExactlyAsManyColumnsAsTheHeader()
        {
            // A row that silently gains or loses a field shifts every column after it, and a
            // spreadsheet will happily plot the wrong series without complaint.
            int headerColumns = B1071_TelemetryMath.CsvHeader().Split(',').Length;

            Assert.Equal(headerColumns, B1071_TelemetryMath.CsvRow(new B1071_TelemetryDay()).Split(',').Length);
            Assert.Equal(headerColumns, B1071_TelemetryMath.CsvRow(FullDay()).Split(',').Length);
        }

        [Fact]
        public void RowsCarryNoSeparatorsOfTheirOwn()
        {
            // Guards against a locale writing "1.234,56": the row would parse as extra
            // columns. The formatter is invariant, and this is what says so.
            string row = B1071_TelemetryMath.CsvRow(FullDay());
            foreach (string field in row.Split(','))
            {
                Assert.False(string.IsNullOrEmpty(field));
                Assert.DoesNotContain("\"", field);
            }
        }

        [Fact]
        public void TheHeaderLeadsWithTheDay()
        {
            Assert.StartsWith("day,", B1071_TelemetryMath.CsvHeader());
            Assert.StartsWith("13,", B1071_TelemetryMath.CsvRow(FullDay()));
        }

        // --- Digest lines ---------------------------------------------------------------

        [Fact]
        public void ThePowerDigestReportsTheDriftAsASignedPercentage()
        {
            var day = FullDay();
            day.VanillaPower = 1000f;
            day.PatchedPower = 1020f;

            string digest = B1071_TelemetryMath.PowerDigest(day);
            Assert.Contains("d13", digest);
            Assert.Contains("+2", digest);
            Assert.Contains("armies=8", digest);
            Assert.Contains("wars=1", digest);
        }

        [Fact]
        public void TheVisitDigestShowsTheWeakHealthySplit()
        {
            string digest = B1071_TelemetryMath.VisitDigest(FullDay());
            Assert.Contains("noGain=19", digest);
            Assert.Contains("weak=4", digest);
            Assert.Contains("healthy=15", digest);
        }

        [Fact]
        public void EveryDigestNamesTheDayItDescribes()
        {
            var day = FullDay();
            Assert.Contains("d13", B1071_TelemetryMath.PowerDigest(day));
            Assert.Contains("d13", B1071_TelemetryMath.RecoveryDigest(day));
            Assert.Contains("d13", B1071_TelemetryMath.VisitDigest(day));
            Assert.Contains("d13", B1071_TelemetryMath.DemobDigest(day));
        }

        // --- Blocked-party histogram ----------------------------------------------------
        //
        // The histogram is the only thing that can tell "recovery helps almost nobody"
        // apart from "almost nobody needs recovery". If a bucket is tied to the wrong flag
        // the answer is confidently wrong, so the ordering is asserted directly.

        [Fact]
        public void BlockReasonNamesMatchTheFlagOrder()
        {
            // Index i must be the flag 1 << i. Anything else silently mislabels the column.
            string[] expected =
            {
                nameof(B1071_AiRecoveryBlockReason.Inactive),
                nameof(B1071_AiRecoveryBlockReason.InvalidLeader),
                nameof(B1071_AiRecoveryBlockReason.Army),
                nameof(B1071_AiRecoveryBlockReason.MapEvent),
                nameof(B1071_AiRecoveryBlockReason.Siege),
                nameof(B1071_AiRecoveryBlockReason.Transition),
                nameof(B1071_AiRecoveryBlockReason.Disbanding),
                nameof(B1071_AiRecoveryBlockReason.Retreating),
                nameof(B1071_AiRecoveryBlockReason.Starving),
                nameof(B1071_AiRecoveryBlockReason.UrgentFood),
                "Besieged",          // BesiegedSettlement, shortened for the column header
                "Objective",         // ProtectedObjective
                "PartyType",         // ExcludedPartyType
                nameof(B1071_AiRecoveryBlockReason.Quest)
            };

            Assert.Equal(expected.Length, B1071_TelemetryMath.BlockReasonNames.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], B1071_TelemetryMath.BlockReasonNames[i]);
                // The name at index i must describe the flag at bit i. Three headers are
                // shortened (Besieged, Objective, PartyType), so the tie is containment
                // rather than equality.
                var flag = (B1071_AiRecoveryBlockReason)(1 << i);
                Assert.Contains(expected[i], flag.ToString(), StringComparison.Ordinal);
            }
        }

        [Fact]
        public void EveryFlagHasABucket()
        {
            // A flag added to the enum without a name here would be counted into nothing.
            int highest = 0;
            foreach (B1071_AiRecoveryBlockReason flag in Enum.GetValues(typeof(B1071_AiRecoveryBlockReason)))
            {
                if (flag == B1071_AiRecoveryBlockReason.None) continue;
                highest = Math.Max(highest, (int)flag);
            }

            Assert.True(highest < 1 << B1071_TelemetryMath.BlockReasonNames.Length,
                "B1071_AiRecoveryBlockReason has a flag with no BlockReasonNames entry.");
        }

        [Fact]
        public void TheBlockDigestLeadsWithTheDominantReason()
        {
            var day = FullDay();
            day.BlockedReasons[(int)BitIndex(B1071_AiRecoveryBlockReason.Army)] = 7;
            day.BlockedReasons[(int)BitIndex(B1071_AiRecoveryBlockReason.ProtectedObjective)] = 91;
            day.BlockedWeak = 98;

            string digest = B1071_TelemetryMath.BlockDigest(day);

            Assert.Contains("blockedWeak=98", digest);
            Assert.Contains("Objective=91", digest);
            Assert.Contains("Army=7", digest);
            Assert.True(
                digest.IndexOf("Objective=", StringComparison.Ordinal)
                    < digest.IndexOf("Army=", StringComparison.Ordinal),
                "Dominant reason must come first: " + digest);
        }

        [Fact]
        public void ReasonsThatNeverFiredAreLeftOutOfTheDigest()
        {
            // Fourteen zeroes on every line would make the interesting one unreadable.
            var day = FullDay();
            Assert.DoesNotContain("Quest", B1071_TelemetryMath.BlockDigest(day));
        }

        [Fact]
        public void TheHistogramReachesTheCsvAndKeepsTheColumnCount()
        {
            var day = FullDay();
            day.BlockedReasons[(int)BitIndex(B1071_AiRecoveryBlockReason.Siege)] = 4;

            string[] header = B1071_TelemetryMath.CsvHeader().Split(',');
            string[] row = B1071_TelemetryMath.CsvRow(day).Split(',');

            Assert.Equal(header.Length, row.Length);
            int column = Array.IndexOf(header, "blkSiege");
            Assert.True(column >= 0, "blkSiege column missing from the header");
            Assert.Equal("4", row[column]);
        }

        private static int BitIndex(B1071_AiRecoveryBlockReason flag)
        {
            int value = (int)flag;
            int index = 0;
            while (value > 1) { value >>= 1; index++; }
            return index;
        }

        private static B1071_TelemetryDay FullDay() => new B1071_TelemetryDay
        {
            Day = 13,
            LordParties = 214,
            Armies = 8,
            WarsDeclared = 1,
            SurvivabilityPreset = 1,
            VanillaPower = 1841.2f,
            PatchedPower = 1855.7f,
            RecoveryEligible = 96,
            RecoveryQuoted = 340,
            RecoveryZeroQuote = 122,
            RecoveryProposed = 71,
            RecoveryConfirmed = 12,
            Visits = 88,
            VisitsNoGain = 19,
            VisitsNoGainWeak = 4,
            VisitsNoGainHealthy = 15,
            AiExtensions = 33,
            AiExtensionGold = 4210,
            AiRetired = 27
        };
    }
}
