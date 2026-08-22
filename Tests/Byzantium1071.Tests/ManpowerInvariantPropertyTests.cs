using Byzantium1071.Campaign;
using FsCheck.Xunit;

namespace Byzantium1071.Tests
{
    public sealed class ManpowerInvariantPropertyTests
    {
        [Property(MaxTest = 1000)]
        public bool MaxPoolIsAlwaysAtLeastOne(
            bool isTown,
            bool isCastle,
            bool hasTown,
            int prosperity,
            int security,
            int governorLeadership)
        {
            FakeSettings settings = new()
            {
                TownPoolMax = Bounded(prosperity, 10_000),
                CastlePoolMax = Bounded(security, 10_000),
                OtherPoolMax = Bounded(governorLeadership, 10_000),
                ProsperityNormalizer = 1_000f,
                MaxPoolProsperityMinScale = Bounded(prosperity, 101),
                MaxPoolProsperityMaxScale = Bounded(security, 101),
                SecurityBonusMinScale = Bounded(governorLeadership, 101),
                SecurityBonusMaxScale = Bounded(prosperity + security, 101),
                MaxPoolHearthMultiplier = 0.1f,
                EnableGovernorBonus = true,
                GovernorLeadershipPoolDivisor = 100f
            };
            PoolFacts facts = new(
                isTown: isTown,
                isCastle: isCastle,
                hasTown: hasTown,
                prosperity: Bounded(prosperity, 20_001),
                security: Bounded(security, 101),
                villageHearths: new[] { (float)Bounded(governorLeadership, 1_001) },
                governorLeadership: Bounded(governorLeadership, 1_001));

            return B1071_ManpowerMath.MaxPool(facts, settings) >= 1;
        }

        [Property(MaxTest = 1000)]
        public bool DailyRegenIsNonNegativeAndDoesNotExceedThePoolMaximum(
            int maximum,
            int currentPool,
            int townRate,
            int castleRate,
            int otherRate)
        {
            int max = 1 + Bounded(maximum, 10_000);
            FakeSettings settings = NeutralRegenSettings();
            settings.TownRegenMinPercent = Bounded(townRate, 101);
            settings.TownRegenMaxPercent = Bounded(townRate, 101);
            settings.CastleRegenMinPercent = Bounded(castleRate, 101);
            settings.CastleRegenMaxPercent = Bounded(castleRate, 101);
            settings.OtherRegenPercent = Bounded(otherRate, 101);

            DailyRegenResult regen = B1071_ManpowerMath.DailyRegen(
                new PoolFacts(
                    isTown: true,
                    isCastle: false,
                    hasTown: true,
                    prosperity: 1_000f,
                    security: 100f,
                    foodStocks: 100f,
                    loyalty: 100f,
                    currentPool: Bounded(currentPool, max + 1)),
                max,
                settings,
                new FakeRandom());

            return regen.Amount >= 0 && regen.Amount <= max;
        }

        [Property(MaxTest = 1000)]
        public bool RaidDrainHonorsTheRequestedAmountAndRemainingDailyBudget(
            int currentPool,
            int maximumPool,
            int drainPercent,
            int dailyCapPercent,
            int spentToday)
        {
            int current = Bounded(currentPool, 10_001);
            int maximum = Bounded(maximumPool, 10_001);
            float percentage = Bounded(drainPercent, 201);
            int capPercent = Bounded(dailyCapPercent, 201);
            int spent = Bounded(spentToday, 20_001);
            int amount = B1071_ManpowerMath.RaidDrainAmount(current, maximum, percentage, capPercent, spent);
            int requested = (int)(current * (percentage / 100f));
            if (requested <= 0 && percentage > 0f && current > 0) requested = 1;

            if (amount < 0 || amount > requested) return false;

            int cap = (int)(maximum * (capPercent / 100f));
            return cap <= 0 || amount <= System.Math.Max(0, cap - spent);
        }

        [Property(MaxTest = 1000)]
        public bool PoolRetentionNeverCreatesManpower(
            int currentPool,
            int maximumPool,
            int siegeRetainPercent,
            int conquestRetainPercent,
            int depletedThresholdPercent,
            int depletedRetainPercent,
            bool enableProtection)
        {
            int current = Bounded(currentPool, 10_001);
            int maximum = Bounded(maximumPool, 10_001);
            PoolRetentionResult siege = B1071_ManpowerMath.SiegeRetention(
                current, maximum, Bounded(siegeRetainPercent, 101));
            FakeSettings settings = new()
            {
                ConquestPoolRetainPercent = Bounded(conquestRetainPercent, 101),
                ConquestDepletedThresholdPercent = Bounded(depletedThresholdPercent, 101),
                ConquestDepletedRetainPercent = Bounded(depletedRetainPercent, 101),
                EnableDynamicConquestProtection = enableProtection
            };
            PoolRetentionResult conquest = B1071_ManpowerMath.ConquestRetention(current, maximum, settings);

            return siege.AppliedPool >= 0 && siege.AppliedPool <= current &&
                conquest.AppliedPool >= 0 && conquest.AppliedPool <= current;
        }

        [Property(MaxTest = 1000)]
        public bool TierDrainAndExhaustionFunctionsStayNonNegative(
            int count,
            int tier,
            int multiplier,
            int deaths,
            int wounded,
            int decay)
        {
            float drain = B1071_ManpowerMath.TierWeightedDrain(
                Bounded(count, 10_001), tier, Bounded(multiplier, 101) / 10f);
            float battleExhaustion = B1071_ExhaustionMath.BattleExhaustion(
                Bounded(deaths, 10_001), Bounded(wounded, 10_001), Bounded(multiplier, 101) / 10f);
            float remaining = B1071_ExhaustionMath.DailyDecay(
                Bounded(count, 10_001), Bounded(decay, 10_001));

            return drain >= 0f && battleExhaustion >= 0f && remaining >= 0f;
        }

        private static FakeSettings NeutralRegenSettings() => new()
        {
            ProsperityNormalizer = 1_000f,
            HearthNormalizer = 1,
            SecurityRegenMinScale = 100f,
            SecurityRegenMaxScale = 100f,
            FoodStocksNormalizer = 1f,
            FoodRegenMinScale = 100f,
            FoodRegenMaxScale = 100f,
            LoyaltyRegenMinScale = 100f,
            LoyaltyRegenMaxScale = 100f,
            SiegeRegenMultiplierPercent = 100f,
            RegenCapPercent = 100f
        };

        private static int Bounded(int value, int upperExclusive)
        {
            int remainder = value % upperExclusive;
            return remainder < 0 ? -remainder : remainder;
        }
    }
}
