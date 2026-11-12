using Byzantium1071.Campaign;
using Byzantium1071.Campaign.Behaviors;
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

        /// <summary>
        /// UrgentFood fires below MobilePartyAIModel.NeededFoodsInDaysThresholdForSiege --
        /// twelve days in v1.5.2, a siege-provisioning line rather than an emergency. It is
        /// recorded and it changes how the pass behaves, but it does not disqualify anyone.
        /// </summary>
        [Fact]
        public void AFoodShortPartyIsStillEligible()
        {
            Assert.True(B1071_AiRecoveryMath.IsEligible(B1071_AiRecoveryBlockReason.UrgentFood));
        }

        /// <summary>
        /// Real starvation is a separate flag and is not advisory. Asserted beside the one
        /// above because the two look alike and confusing them would let a party that is
        /// actively losing men be routed on a recruiting errand.
        /// </summary>
        [Fact]
        public void ActualStarvationIsNotAdvisory()
        {
            Assert.False(B1071_AiRecoveryMath.IsEligible(B1071_AiRecoveryBlockReason.Starving));
        }

        /// <summary>
        /// Pins the mask itself. Every other flag is asserted blocking one by one above, so
        /// widening this set silently would turn one of those assertions into a contradiction
        /// -- but only if someone remembers to look. This says it in one line instead.
        /// </summary>
        [Fact]
        public void UrgentFoodIsTheOnlyAdvisoryReason()
        {
            Assert.Equal(
                B1071_AiRecoveryBlockReason.UrgentFood,
                B1071_AiRecoveryMath.AdvisoryReasons);
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

        // -- Drained settlements --------------------------------------------------

        [Fact]
        public void ASettlementThatCanSupplyNobodyIsPushedBelowItsNativeScore()
        {
            Assert.Equal(50f, B1071_AiRecoveryMath.DrainedCandidateScore(100f));
        }

        [Fact]
        public void TheDrainPenaltyDiscouragesButNeverForbids()
        {
            // A score of zero would remove the settlement from consideration outright,
            // which is not this factor's job -- a lord still visits for food and safety.
            Assert.True(B1071_AiRecoveryMath.DrainedCandidateScore(100f) > 0f);
            Assert.True(B1071_AiRecoveryMath.DrainedCandidateFactor < 1f);
        }

        [Fact]
        public void ANegativeNativeScoreIsLeftAloneRatherThanHalvedUpwards()
        {
            // Halving -100 gives -50, which RANKS HIGHER. Penalising an already
            // unattractive settlement must never make it look better.
            Assert.Equal(-100f, B1071_AiRecoveryMath.DrainedCandidateScore(-100f));
            Assert.Equal(0f, B1071_AiRecoveryMath.DrainedCandidateScore(0f));
        }

        [Fact]
        public void TheDrainPenaltyRanksAnEmptySettlementBelowASupplyingOne()
        {
            // The failure this closes: the boost is a multiplier of at least 1, so a
            // drained settlement with a higher native score used to win outright.
            float drained = B1071_AiRecoveryMath.DrainedCandidateScore(100f);
            float supplying = B1071_AiRecoveryMath.CandidateScore(80f, recruitable: 10, missing: 20);

            Assert.True(supplying > drained);
        }

        // -- Volunteers in the quote ----------------------------------------------

        [Fact]
        public void VolunteersCountTowardTheQuoteTotal()
        {
            var quote = new B1071_AiRecoverySourceQuote(0, 0, 0, 0, volunteers: 7);

            Assert.Equal(7, quote.Total);
            Assert.Equal(7, quote.Volunteers);
        }

        [Fact]
        public void QuotesFromDifferentSourcesAddUpIncludingVolunteers()
        {
            var veterans = new B1071_AiRecoverySourceQuote(3, 0, 0, 12);
            var board = new B1071_AiRecoverySourceQuote(0, 0, 0, 8, volunteers: 5);

            B1071_AiRecoverySourceQuote sum = veterans + board;

            Assert.Equal(3, sum.Veterans);
            Assert.Equal(5, sum.Volunteers);
            Assert.Equal(8, sum.Total);
            Assert.Equal(20, sum.Manpower);
        }

        [Fact]
        public void AQuoteBuiltWithoutVolunteersStillReadsAsZeroRatherThanBreaking()
        {
            // The four-argument constructor is what the veteran and castle quoters call.
            var quote = new B1071_AiRecoverySourceQuote(2, 1, 1, 30);

            Assert.Equal(0, quote.Volunteers);
            Assert.Equal(4, quote.Total);
        }

        [Fact]
        public void AVolunteerQuoteCarriesTheManpowerThoseHiresWillCost()
        {
            // The men themselves are not reserved -- vanilla hands them to whichever lord
            // arrives -- but the manpower pool they drain is shared, so it has to reach the
            // reservation. A quote reporting volunteers with zero manpower promised the same
            // village to two recovering lords.
            var board = new B1071_AiRecoverySourceQuote(0, 0, 0, manpower: 24, volunteers: 4);

            Assert.Equal(4, board.Total);
            Assert.Equal(24, board.Manpower);
        }

        // -- Actionable vs Total ---------------------------------------------------

        [Fact]
        public void ActionableExcludesTheVanillaVolunteerBoard()
        {
            // The split that matters: routing asks Total, because all of the supply is a
            // reason to go there. Recruiting asks Actionable, because Campaign++ never
            // hands over the volunteer board itself -- vanilla hires it on arrival -- so
            // acting on a volunteer-only quote is a guaranteed no-op.
            var boardOnly = new B1071_AiRecoverySourceQuote(0, 0, 0, 12, volunteers: 5);

            Assert.Equal(5, boardOnly.Total);
            Assert.Equal(0, boardOnly.Actionable);
        }

        [Fact]
        public void ActionableCountsEverySourceCampaignPlusPlusCanHandOver()
        {
            var mixed = new B1071_AiRecoverySourceQuote(2, 1, 1, 30, volunteers: 6);

            Assert.Equal(10, mixed.Total);
            Assert.Equal(4, mixed.Actionable);
        }

        // -- Vanilla's recruiting passes -------------------------------------------

        [Fact]
        public void TheVanillaPassCountOutrunsASixSlotVolunteerBoard()
        {
            // Hero.VolunteerTypes is a six-slot array, and
            // RecruitmentCampaignBehavior.OnBeforeSettlementEntered runs CheckRecruiting
            // seven times for an ordinary AI lord party. The quote's per-notable cap is
            // therefore the pass count, and it must stay above the board size or the quote
            // starts under-reporting a full board again -- the sevenfold undercount that
            // collapsed CandidateScore's usefulShare before v1.0.3.8.
            Assert.True(B1071_AiRecoveryMath.VanillaRecruitPassesPerArrival > 6);
        }
    }
}
