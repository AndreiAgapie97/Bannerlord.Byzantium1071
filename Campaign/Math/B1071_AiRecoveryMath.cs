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
        ExcludedPartyType = 1 << 12
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

        internal static bool IsEligible(B1071_AiRecoveryBlockReason blockReasons)
            => blockReasons == B1071_AiRecoveryBlockReason.None;

        internal static float CandidateScore(float nativeScore, int recruitable, int missing)
        {
            if (missing <= 0 || recruitable <= 0) return nativeScore;
            float usefulShare = Math.Min(recruitable, missing) / (float)missing;
            return nativeScore * (1f + usefulShare);
        }

        internal static bool IsWithinStickiness(float currentScore, float bestScore)
            => currentScore >= bestScore * 0.9f;

        internal static float WinningScore(float highestCompletedNativeScore)
            => highestCompletedNativeScore
             + Math.Max(0.1f, Math.Abs(highestCompletedNativeScore) * 0.05f);

        internal static bool IsReservationExpired(float nowDay, float expiryDay)
            => nowDay >= expiryDay;

        internal static bool CanReconstruct(
            int members,
            int partyLimit,
            bool isOrdinarySettlementJourney,
            bool hasFriendlyTarget,
            bool hasRecruitOffer)
            => !ShouldStart(members, partyLimit)
            && !HasReachedStop(members, partyLimit)
            && isOrdinarySettlementJourney
            && hasFriendlyTarget
            && hasRecruitOffer;

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
