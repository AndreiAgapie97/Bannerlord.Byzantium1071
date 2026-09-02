using Byzantium1071.Campaign;
using Xunit;

namespace Byzantium1071.Tests
{
    public sealed class AiRecoveryMathTests
    {
        // ── Start and stop boundaries ─────────────────────────────────────────────

        [Theory]
        [InlineData(59, 100, true)]   // below 60% starts recovery
        [InlineData(60, 100, false)]  // exactly 60% does not
        [InlineData(61, 100, false)]
        [InlineData(0, 100, true)]
        public void ShouldStartUsesAnExclusiveSixtyPercentBoundary(int members, int limit, bool expected)
        {
            Assert.Equal(expected, B1071_AiRecoveryMath.ShouldStart(members, limit));
        }

        [Theory]
        [InlineData(79, 100, false)]  // still recovering
        [InlineData(80, 100, true)]   // exactly 80% stops
        [InlineData(81, 100, true)]
        public void HasReachedStopUsesAnInclusiveEightyPercentBoundary(int members, int limit, bool expected)
        {
            Assert.Equal(expected, B1071_AiRecoveryMath.HasReachedStop(members, limit));
        }

        /// <summary>
        /// PartySizeRatio counts wounded soldiers, so a party whose headcount includes them
        /// is measured on that same headcount and must not re-enter recovery.
        /// </summary>
        [Fact]
        public void WoundedMembersCountTowardBothBoundaries()
        {
            const int healthy = 40;
            const int wounded = 25;
            int members = healthy + wounded; // 65 of 100

            Assert.False(B1071_AiRecoveryMath.ShouldStart(members, 100));
            Assert.False(B1071_AiRecoveryMath.HasReachedStop(members, 100));
            Assert.True(B1071_AiRecoveryMath.ShouldStart(healthy, 100));
        }

        [Fact]
        public void AnUnknownPartyLimitNeitherStartsNorContinuesRecovery()
        {
            Assert.False(B1071_AiRecoveryMath.ShouldStart(10, 0));
            Assert.True(B1071_AiRecoveryMath.HasReachedStop(10, 0));
            Assert.Equal(0, B1071_AiRecoveryMath.MissingToStop(10, 0));
        }

        [Theory]
        [InlineData(50, 100, 30)]  // 80 needed, 50 held
        [InlineData(80, 100, 0)]   // already at the stop line
        [InlineData(90, 100, 0)]   // never negative
        [InlineData(0, 3, 3)]      // rounds the target up, not down
        public void MissingToStopCountsMenNeededToReachEightyPercent(int members, int limit, int expected)
        {
            Assert.Equal(expected, B1071_AiRecoveryMath.MissingToStop(members, limit));
        }

        // ── Eligibility ───────────────────────────────────────────────────────────

        [Fact]
        public void OnlyAnUnblockedPartyIsEligible()
        {
            Assert.True(B1071_AiRecoveryMath.IsEligible(B1071_AiRecoveryBlockReason.None));
        }

        /// <summary>
        /// Each flag is passed as its underlying value because the reason enum is internal
        /// and an xunit theory signature must be public.
        /// </summary>
        [Theory]
        [InlineData((int)B1071_AiRecoveryBlockReason.Inactive)]
        [InlineData((int)B1071_AiRecoveryBlockReason.InvalidLeader)]
        [InlineData((int)B1071_AiRecoveryBlockReason.Army)]
        [InlineData((int)B1071_AiRecoveryBlockReason.MapEvent)]
        [InlineData((int)B1071_AiRecoveryBlockReason.Siege)]
        [InlineData((int)B1071_AiRecoveryBlockReason.Transition)]
        [InlineData((int)B1071_AiRecoveryBlockReason.Disbanding)]
        [InlineData((int)B1071_AiRecoveryBlockReason.Retreating)]
        [InlineData((int)B1071_AiRecoveryBlockReason.Starving)]
        [InlineData((int)B1071_AiRecoveryBlockReason.UrgentFood)]
        [InlineData((int)B1071_AiRecoveryBlockReason.BesiegedSettlement)]
        [InlineData((int)B1071_AiRecoveryBlockReason.ProtectedObjective)]
        [InlineData((int)B1071_AiRecoveryBlockReason.ExcludedPartyType)]
        [InlineData((int)B1071_AiRecoveryBlockReason.Quest)]
        public void EveryProtectedStateBlocksRecovery(int reason)
        {
            Assert.False(B1071_AiRecoveryMath.IsEligible((B1071_AiRecoveryBlockReason)reason));
        }

        [Fact]
        public void CombinedBlockReasonsStillBlockRecovery()
        {
            B1071_AiRecoveryBlockReason reasons =
                B1071_AiRecoveryBlockReason.Army | B1071_AiRecoveryBlockReason.UrgentFood;

            Assert.False(B1071_AiRecoveryMath.IsEligible(reasons));
        }

        // ── Candidate ranking ─────────────────────────────────────────────────────

        [Fact]
        public void ASettlementOfferingNothingKeepsItsNativeScore()
        {
            Assert.Equal(10f, B1071_AiRecoveryMath.CandidateScore(10f, recruitable: 0, missing: 20));
        }

        [Fact]
        public void ASettlementCoveringTheWholeGapDoublesItsNativeScore()
        {
            Assert.Equal(20f, B1071_AiRecoveryMath.CandidateScore(10f, recruitable: 20, missing: 20));
        }

        [Fact]
        public void RecruitsBeyondTheGapAddNoFurtherWeight()
        {
            float exact = B1071_AiRecoveryMath.CandidateScore(10f, recruitable: 20, missing: 20);
            float surplus = B1071_AiRecoveryMath.CandidateScore(10f, recruitable: 500, missing: 20);

            Assert.Equal(exact, surplus);
        }

        [Fact]
        public void PartialCoverageScalesProportionally()
        {
            Assert.Equal(15f, B1071_AiRecoveryMath.CandidateScore(10f, recruitable: 10, missing: 20));
        }

        [Fact]
        public void APartyWithNoGapIsNeverReweighted()
        {
            Assert.Equal(10f, B1071_AiRecoveryMath.CandidateScore(10f, recruitable: 50, missing: 0));
        }

        // ── Target stickiness ─────────────────────────────────────────────────────

        [Theory]
        [InlineData(90f, 100f, true)]   // exactly 10% behind the best: keep the current target
        [InlineData(89f, 100f, false)]  // further behind: switch
        [InlineData(100f, 100f, true)]
        public void TheCurrentTargetSurvivesWhileWithinTenPercentOfTheBest(
            float current, float best, bool expected)
        {
            Assert.Equal(expected, B1071_AiRecoveryMath.IsWithinStickiness(current, best));
        }

        // ── Winning score ─────────────────────────────────────────────────────────

        [Fact]
        public void TheWinningScoreClearsTheHighestNativeScoreByAtLeastTheFlatFloor()
        {
            float winning = B1071_AiRecoveryMath.WinningScore(1f);

            Assert.True(winning > 1f);
            Assert.Equal(1.1f, winning, 3);
        }

        [Fact]
        public void LargeNativeScoresUseThePercentageMarginInstead()
        {
            Assert.Equal(105f, B1071_AiRecoveryMath.WinningScore(100f), 3);
        }

        [Fact]
        public void ANegativeNativeScoreStillProducesAStrictlyHigherResult()
        {
            float winning = B1071_AiRecoveryMath.WinningScore(-100f);

            Assert.True(winning > -100f);
        }

        // ── Reservations ──────────────────────────────────────────────────────────

        [Theory]
        [InlineData(9.9f, 10f, false)]
        [InlineData(10f, 10f, true)]   // expiry is inclusive
        [InlineData(10.1f, 10f, true)]
        public void ReservationsExpireOnOrAfterTheirExpiryDay(float now, float expiry, bool expected)
        {
            Assert.Equal(expected, B1071_AiRecoveryMath.IsReservationExpired(now, expiry));
        }

        [Theory]
        [InlineData(10f, 11f, false)]
        [InlineData(11f, 11f, true)]
        [InlineData(12f, 11f, true)]
        public void RecoveryIntentsExpireOnOrAfterTheirDeadline(float now, float expiry, bool expected)
        {
            Assert.Equal(expected, B1071_AiRecoveryMath.IsIntentExpired(now, expiry));
        }

        [Theory]
        [InlineData(10f, 1, 11f)]
        [InlineData(10f, 5, 15f)]
        [InlineData(10f, 0, 11f)]
        public void RecoveryIntentDurationIsTunableWithAOneDayMinimum(
            float now, int durationDays, float expected)
        {
            Assert.Equal(expected, B1071_AiRecoveryMath.IntentExpiryDay(now, durationDays));
        }

        [Fact]
        public void AClaimOnTheSameSettlementReservesItsTroopsAndItsManpower()
        {
            var reservation = new Campaign.Behaviors.B1071_AiRecoveryReservedSupply(
                veterans: 4,
                elites: 3,
                prisoners: 2,
                manpower: 25);

            Campaign.Behaviors.B1071_AiRecoveryReservedSupply applicable = reservation.ForCandidate(
                sameSettlement: true,
                sameManpowerPool: true);

            Assert.Equal(4, applicable.Veterans);
            Assert.Equal(3, applicable.Elites);
            Assert.Equal(2, applicable.Prisoners);
            Assert.Equal(25, applicable.Manpower);
        }

        [Fact]
        public void ASettlementOutsideThePoolStillReservesItsOwnTroops()
        {
            var reservation = new Campaign.Behaviors.B1071_AiRecoveryReservedSupply(
                veterans: 4,
                elites: 3,
                prisoners: 2,
                manpower: 25);

            // An orphan village has no bound pool, so a claim on it reserves the men standing
            // there and no manpower. Reading only the pool would hand the same men out twice.
            Campaign.Behaviors.B1071_AiRecoveryReservedSupply applicable = reservation.ForCandidate(
                sameSettlement: true,
                sameManpowerPool: false);

            Assert.Equal(4, applicable.Veterans);
            Assert.Equal(3, applicable.Elites);
            Assert.Equal(2, applicable.Prisoners);
            Assert.Equal(0, applicable.Manpower);
        }

        [Fact]
        public void DifferentSettlementsSharingAPoolReserveOnlyTheirCommonManpower()
        {
            var reservation = new Campaign.Behaviors.B1071_AiRecoveryReservedSupply(
                veterans: 4,
                elites: 3,
                prisoners: 2,
                manpower: 25);

            Campaign.Behaviors.B1071_AiRecoveryReservedSupply applicable = reservation.ForCandidate(
                sameSettlement: false,
                sameManpowerPool: true);

            Assert.Equal(0, applicable.Veterans);
            Assert.Equal(0, applicable.Elites);
            Assert.Equal(0, applicable.Prisoners);
            Assert.Equal(25, applicable.Manpower);
        }

        [Fact]
        public void UnrelatedSettlementsAndPoolsShareNoReservation()
        {
            var reservation = new Campaign.Behaviors.B1071_AiRecoveryReservedSupply(
                veterans: 4,
                elites: 3,
                prisoners: 2,
                manpower: 25);

            Campaign.Behaviors.B1071_AiRecoveryReservedSupply applicable = reservation.ForCandidate(
                sameSettlement: false,
                sameManpowerPool: false);

            Assert.Equal(0, applicable.Veterans);
            Assert.Equal(0, applicable.Elites);
            Assert.Equal(0, applicable.Prisoners);
            Assert.Equal(0, applicable.Manpower);
        }

        // ── Player-tunable priority ────────────────────────────────────────────────

        [Fact]
        public void PriorityModeClearsTheHighestCompletedNativeScore()
        {
            Assert.Equal(105f, B1071_AiRecoveryMath.FinalScore(
                adjustedSettlementScore: 40f,
                highestCompletedNativeScore: 100f,
                takesPriorityOverNewTasks: true), 3);
        }

        [Fact]
        public void CompetitiveModeKeepsTheAdjustedSettlementScore()
        {
            Assert.Equal(40f, B1071_AiRecoveryMath.FinalScore(
                adjustedSettlementScore: 40f,
                highestCompletedNativeScore: 100f,
                takesPriorityOverNewTasks: false), 3);
        }

        [Theory]
        [InlineData(40f, 100f, true, true)]
        [InlineData(101f, 100f, false, true)]
        [InlineData(100f, 100f, false, false)]
        [InlineData(40f, 100f, false, false)]
        public void IntentIsKeptOnlyWhenRecoveryActuallyWins(
            float adjustedScore,
            float highestNativeScore,
            bool takesPriority,
            bool expected)
        {
            Assert.Equal(expected, B1071_AiRecoveryMath.RecoveryWins(
                adjustedScore,
                highestNativeScore,
                takesPriority));
        }

        // ── Shared budget quoting ─────────────────────────────────────────────────

        [Fact]
        public void PartyRoomCapsTheQuoteBeforeAnyCostIsConsidered()
        {
            int units = B1071_AiRecoveryMath.AffordableUnits(
                available: 100, room: 3, gold: 1_000_000,
                goldCostPerUnit: 1, goldBufferMultiplier: 1,
                manpower: 1_000_000, manpowerGateCostPerUnit: 1);

            Assert.Equal(3, units);
        }

        [Fact]
        public void TheGoldBufferMultiplierReservesTreasuryBeyondTheStickerPrice()
        {
            int unbuffered = B1071_AiRecoveryMath.AffordableUnits(
                available: 100, room: 100, gold: 1001,
                goldCostPerUnit: 100, goldBufferMultiplier: 1,
                manpower: int.MaxValue, manpowerGateCostPerUnit: 0);
            int buffered = B1071_AiRecoveryMath.AffordableUnits(
                available: 100, room: 100, gold: 1001,
                goldCostPerUnit: 100, goldBufferMultiplier: 2,
                manpower: int.MaxValue, manpowerGateCostPerUnit: 0);

            Assert.Equal(10, unbuffered);
            Assert.Equal(5, buffered);
        }

        [Fact]
        public void ALordIsNeverLeftWithoutASingleCoin()
        {
            int units = B1071_AiRecoveryMath.AffordableUnits(
                available: 10, room: 10, gold: 100,
                goldCostPerUnit: 100, goldBufferMultiplier: 1,
                manpower: int.MaxValue, manpowerGateCostPerUnit: 0);

            Assert.Equal(0, units);
        }

        [Fact]
        public void ManpowerGatesTheQuoteIndependentlyOfGold()
        {
            int units = B1071_AiRecoveryMath.AffordableUnits(
                available: 100, room: 100, gold: int.MaxValue,
                goldCostPerUnit: 1, goldBufferMultiplier: 1,
                manpower: 25, manpowerGateCostPerUnit: 10);

            Assert.Equal(2, units);
        }

        /// <summary>
        /// Converted prisoners are quoted with a zero manpower gate; an empty pool must not
        /// stop them being offered, because they are paid for in gold alone.
        /// </summary>
        [Fact]
        public void ConvertedPrisonersRemainAvailableWithAnEmptyManpowerPool()
        {
            int units = B1071_AiRecoveryMath.AffordableUnits(
                available: 4, room: 4, gold: 10_000,
                goldCostPerUnit: 100, goldBufferMultiplier: 1,
                manpower: 0, manpowerGateCostPerUnit: 0);

            Assert.Equal(4, units);
        }

        [Fact]
        public void FreeTroopsAreLimitedOnlyByRoomAndAvailability()
        {
            int units = B1071_AiRecoveryMath.AffordableUnits(
                available: 6, room: 10, gold: 0,
                goldCostPerUnit: 0, goldBufferMultiplier: 1,
                manpower: 0, manpowerGateCostPerUnit: 0);

            Assert.Equal(6, units);
        }

        [Theory]
        [InlineData(-5, 10)]
        [InlineData(10, -5)]
        public void NegativeSuppliesQuoteNothingRatherThanGoingNegative(int available, int room)
        {
            int units = B1071_AiRecoveryMath.AffordableUnits(
                available, room, gold: 10_000,
                goldCostPerUnit: 1, goldBufferMultiplier: 1,
                manpower: 10_000, manpowerGateCostPerUnit: 1);

            Assert.Equal(0, units);
        }

        // ── Garrison manpower ─────────────────────────────────────────────────────

        [Theory]
        [InlineData(100, 10, 10)]
        [InlineData(95, 10, 9)]    // partial troops are not recruitable
        [InlineData(0, 10, 0)]
        [InlineData(-50, 10, 0)]
        public void NativeGarrisonRecruitmentIsCappedByAffordableTroops(
            int manpower, int costPerTroop, int expected)
        {
            Assert.Equal(expected, B1071_GarrisonRecruitmentMath.ManpowerLimitedCount(manpower, costPerTroop));
        }

        [Fact]
        public void AZeroCostPerTroopIsTreatedAsOneRatherThanDividingByZero()
        {
            Assert.Equal(40, B1071_GarrisonRecruitmentMath.ManpowerLimitedCount(40, 0));
        }

        [Theory]
        [InlineData(10, 14, 5, 20)]  // four recruits at five manpower each
        [InlineData(10, 10, 5, 0)]   // no growth, no charge
        [InlineData(14, 10, 5, 0)]   // garrison shrank: never refund
        public void ManpowerIsChargedForActualRosterGrowthOnly(
            int before, int after, int costPerTroop, int expected)
        {
            Assert.Equal(
                expected,
                B1071_GarrisonRecruitmentMath.ManpowerCostForRosterGrowth(before, after, costPerTroop));
        }
    }
}
