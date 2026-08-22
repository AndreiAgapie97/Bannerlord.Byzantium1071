using System;
using System.Collections.Generic;

namespace Byzantium1071.Campaign
{
    internal enum B1071Season
    {
        Spring,
        Summer,
        Autumn,
        Winter
    }

    internal readonly struct PoolFacts
    {
        internal PoolFacts(
            bool isTown,
            bool isCastle,
            bool hasTown,
            float prosperity,
            float security,
            IReadOnlyList<float>? villageHearths = null,
            int governorLeadership = 0,
            float foodStocks = 0f,
            float loyalty = 0f,
            bool isUnderSiege = false,
            bool ownerAtPeace = false,
            int governorSteward = 0,
            B1071Season season = B1071Season.Autumn,
            float exhaustion = 0f,
            float recoveryPenalty = 0f,
            int currentPool = 0)
        {
            IsTown = isTown;
            IsCastle = isCastle;
            HasTown = hasTown;
            Prosperity = prosperity;
            Security = security;
            VillageHearths = villageHearths ?? Array.Empty<float>();
            GovernorLeadership = governorLeadership;
            FoodStocks = foodStocks;
            Loyalty = loyalty;
            IsUnderSiege = isUnderSiege;
            OwnerAtPeace = ownerAtPeace;
            GovernorSteward = governorSteward;
            Season = season;
            Exhaustion = exhaustion;
            RecoveryPenalty = recoveryPenalty;
            CurrentPool = currentPool;
        }

        internal bool IsTown { get; }
        internal bool IsCastle { get; }
        internal bool HasTown { get; }
        internal float Prosperity { get; }
        internal float Security { get; }
        internal IReadOnlyList<float> VillageHearths { get; }
        internal int GovernorLeadership { get; }
        internal float FoodStocks { get; }
        internal float Loyalty { get; }
        internal bool IsUnderSiege { get; }
        internal bool OwnerAtPeace { get; }
        internal int GovernorSteward { get; }
        internal B1071Season Season { get; }
        internal float Exhaustion { get; }
        internal float RecoveryPenalty { get; }
        internal int CurrentPool { get; }
    }

    internal readonly struct DailyRegenResult
    {
        internal DailyRegenResult(
            int amount,
            float basePercent,
            float finalPercent,
            float securityMultiplier,
            float foodMultiplier,
            float loyaltyMultiplier,
            float siegeMultiplier,
            float seasonalMultiplier,
            float peaceMultiplier,
            float governorAdd,
            float exhaustionMultiplier,
            float recoveryMultiplier,
            float softCapMultiplier,
            float varianceMultiplier,
            int depletedBonus)
        {
            Amount = amount;
            BasePercent = basePercent;
            FinalPercent = finalPercent;
            SecurityMultiplier = securityMultiplier;
            FoodMultiplier = foodMultiplier;
            LoyaltyMultiplier = loyaltyMultiplier;
            SiegeMultiplier = siegeMultiplier;
            SeasonalMultiplier = seasonalMultiplier;
            PeaceMultiplier = peaceMultiplier;
            GovernorAdd = governorAdd;
            ExhaustionMultiplier = exhaustionMultiplier;
            RecoveryMultiplier = recoveryMultiplier;
            SoftCapMultiplier = softCapMultiplier;
            VarianceMultiplier = varianceMultiplier;
            DepletedBonus = depletedBonus;
        }

        internal int Amount { get; }
        internal float BasePercent { get; }
        internal float FinalPercent { get; }
        internal float SecurityMultiplier { get; }
        internal float FoodMultiplier { get; }
        internal float LoyaltyMultiplier { get; }
        internal float SiegeMultiplier { get; }
        internal float SeasonalMultiplier { get; }
        internal float PeaceMultiplier { get; }
        internal float GovernorAdd { get; }
        internal float ExhaustionMultiplier { get; }
        internal float RecoveryMultiplier { get; }
        internal float SoftCapMultiplier { get; }
        internal float VarianceMultiplier { get; }
        internal int DepletedBonus { get; }
    }
}
