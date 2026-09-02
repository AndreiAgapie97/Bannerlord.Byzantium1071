using System;

namespace Byzantium1071.Campaign
{
    [Flags]
    internal enum B1071_AiRecoveryBlockReason
    {
        None = 0,
        Inactive = 1 << 0,
        InvalidLeader = 1 << 1,
        Army = 1 << 2,
        MapEvent = 1 << 3,
        Siege = 1 << 4,
        Transition = 1 << 5,
        Disbanding = 1 << 6,
        Retreating = 1 << 7,
        Starving = 1 << 8,
        UrgentFood = 1 << 9,
        BesiegedSettlement = 1 << 10,
        ProtectedObjective = 1 << 11,
        ExcludedPartyType = 1 << 12,
        Quest = 1 << 13
    }

    internal static class B1071_AiRecoveryMath
    {
        internal static bool ShouldStart(int members, int partyLimit)
            => partyLimit > 0 && (long)Math.Max(0, members) * 5L < (long)partyLimit * 3L;

        internal static bool HasReachedStop(int members, int partyLimit)
            => partyLimit <= 0 || (long)Math.Max(0, members) * 5L >= (long)partyLimit * 4L;

        internal static int MissingToStop(int members, int partyLimit)
        {
            if (partyLimit <= 0) return 0;
            int target = (int)Math.Min(int.MaxValue, ((long)partyLimit * 4L + 4L) / 5L);
            return Math.Max(0, target - Math.Max(0, members));
        }

        /// <summary>
        /// Reasons that are recorded for diagnosis but do not, on their own, disqualify a
        /// party.
        ///
        /// UrgentFood is the only one. It fires below
        /// MobilePartyAIModel.NeededFoodsInDaysThresholdForSiege, which is 12f in v1.5.2 --
        /// a SIEGE PROVISIONING line, not an emergency: a lord with eleven days of food is
        /// perfectly healthy. Vanilla itself never blocks on it. AiVisitSettlementBehavior
        /// reads the same threshold to RAISE the score of towns and villages that sell food,
        /// so by the time this system runs, a hungry lord's food sources are already ranked
        /// up -- and CandidateScore multiplies the native score, so that boost is carried
        /// through rather than fought. Blocking outright turned away 24% of the weak lords
        /// this system exists for. Actual starvation is a separate flag and still blocks.
        /// </summary>
        internal const B1071_AiRecoveryBlockReason AdvisoryReasons =
            B1071_AiRecoveryBlockReason.UrgentFood;

        internal static bool IsEligible(B1071_AiRecoveryBlockReason blockReasons)
            => (blockReasons & ~AdvisoryReasons) == B1071_AiRecoveryBlockReason.None;

        internal static float CandidateScore(float nativeScore, int recruitable, int missing)
        {
            if (missing <= 0 || recruitable <= 0) return nativeScore;
            float usefulShare = Math.Min(recruitable, missing) / (float)missing;
            return nativeScore * (1f + usefulShare);
        }

        /// <summary>
        /// How far a settlement that can supply nobody is pushed down.
        ///
        /// <see cref="CandidateScore"/> is a multiplier of at least 1, so it can only ever
        /// recommend. That leaves the AI free to walk to a drained village anyway whenever
        /// its own score for that village beats the boosted score of the one with recruits.
        /// This is the other half, and the caller applies it ONLY when some other settlement
        /// quoted men: pushing a lord away from every settlement at once would leave him
        /// wandering, and a settlement is worth visiting for food, healing and safety even
        /// when it has no one to recruit.
        /// </summary>
        internal const float DrainedCandidateFactor = 0.5f;

        /// <summary>
        /// How many times vanilla runs its recruiting pass over a party arriving at a
        /// settlement, and therefore the most men one notable can supply in a single visit.
        ///
        /// RecruitmentCampaignBehavior.OnBeforeSettlementEntered computes this as `num` and
        /// loops CheckRecruiting that many times. It is 1 for a caravan and 1-3 for a party
        /// inside the player's army; for every ordinary AI lord party -- the only population
        /// this system quotes, since Army disqualifies the rest -- it is 7.
        /// </summary>
        internal const int VanillaRecruitPassesPerArrival = 7;

        /// <summary>
        /// Discourages, never forbids. A negative native score is returned untouched --
        /// halving it would make an unattractive settlement look better, not worse.
        /// </summary>
        internal static float DrainedCandidateScore(float nativeScore)
            => nativeScore > 0f ? nativeScore * DrainedCandidateFactor : nativeScore;

        internal static bool IsWithinStickiness(float currentScore, float bestScore)
            => currentScore >= bestScore * 0.9f;

        internal static float WinningScore(float highestCompletedNativeScore)
            => highestCompletedNativeScore
             + Math.Max(0.1f, Math.Abs(highestCompletedNativeScore) * 0.05f);

        internal static float FinalScore(
            float adjustedSettlementScore,
            float highestCompletedNativeScore,
            bool takesPriorityOverNewTasks)
            => takesPriorityOverNewTasks
                ? WinningScore(highestCompletedNativeScore)
                : adjustedSettlementScore;

        internal static bool RecoveryWins(
            float adjustedSettlementScore,
            float highestCompletedNativeScore,
            bool takesPriorityOverNewTasks)
            => takesPriorityOverNewTasks || adjustedSettlementScore > highestCompletedNativeScore;

        internal static bool IsReservationExpired(float nowDay, float expiryDay)
            => nowDay >= expiryDay;

        internal static float IntentExpiryDay(float nowDay, int durationDays)
            => nowDay + Math.Max(1, durationDays);

        internal static bool IsIntentExpired(float nowDay, float expiryDay)
            => nowDay >= expiryDay;

        internal static int AffordableUnits(
            int available,
            int room,
            int gold,
            int goldCostPerUnit,
            int goldBufferMultiplier,
            int manpower,
            int manpowerGateCostPerUnit)
        {
            int result = Math.Min(Math.Max(0, available), Math.Max(0, room));
            if (result <= 0) return 0;

            if (goldCostPerUnit > 0)
            {
                long bufferedCost = (long)goldCostPerUnit * Math.Max(1, goldBufferMultiplier);
                result = (int)Math.Min(result, Math.Max(0L, ((long)gold - 1L) / bufferedCost));
            }

            if (manpowerGateCostPerUnit > 0)
                result = Math.Min(result, Math.Max(0, manpower) / manpowerGateCostPerUnit);

            return Math.Max(0, result);
        }
    }

    internal static class B1071_GarrisonRecruitmentMath
    {
        internal static int ManpowerLimitedCount(int currentManpower, int baseManpowerCostPerTroop)
            => Math.Max(0, currentManpower) / Math.Max(1, baseManpowerCostPerTroop);

        internal static int ManpowerCostForRosterGrowth(int before, int after, int baseManpowerCostPerTroop)
            => Math.Max(0, after - before) * Math.Max(1, baseManpowerCostPerTroop);
    }
}
