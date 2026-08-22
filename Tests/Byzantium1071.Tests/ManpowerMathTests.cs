using Byzantium1071.Campaign;
using Xunit;

namespace Byzantium1071.Tests
{
    public sealed class ManpowerMathTests
    {
        [Fact]
        public void MaxPoolNeverDropsBelowOneForInvalidSettings()
        {
            FakeSettings settings = new()
            {
                TownPoolMax = 0,
                ProsperityNormalizer = 0f,
                MaxPoolProsperityMinScale = 0f,
                MaxPoolProsperityMaxScale = 0f,
                SecurityBonusMinScale = 0f,
                SecurityBonusMaxScale = 0f
            };

            int pool = B1071_ManpowerMath.MaxPool(
                new PoolFacts(isTown: true, isCastle: false, hasTown: true, prosperity: 0f, security: 0f),
                settings);

            Assert.Equal(1, pool);
        }

        [Fact]
        public void MaxPoolHandlesInvertedProsperityScales()
        {
            FakeSettings settings = BaseSettings();
            settings.MaxPoolProsperityMinScale = 30f;
            settings.MaxPoolProsperityMaxScale = 20f;

            int pool = B1071_ManpowerMath.MaxPool(
                new PoolFacts(isTown: true, isCastle: false, hasTown: true, prosperity: 1_000f, security: 100f),
                settings);

            Assert.Equal(300, pool);
        }

        [Fact]
        public void MaxPoolTruncatesEachVillageHearthContributionSeparately()
        {
            FakeSettings settings = BaseSettings();
            settings.MaxPoolHearthMultiplier = 0.1f;

            int pool = B1071_ManpowerMath.MaxPool(
                new PoolFacts(
                    isTown: true,
                    isCastle: false,
                    hasTown: true,
                    prosperity: 1_000f,
                    security: 100f,
                    villageHearths: new[] { 15f, 15f }),
                settings);

            Assert.Equal(1_002, pool);
        }

        [Fact]
        public void GovernorLeadershipBonusCapsAtOneHundredPercent()
        {
            FakeSettings settings = BaseSettings();
            settings.EnableGovernorBonus = true;
            settings.GovernorLeadershipPoolDivisor = 500f;

            int pool = B1071_ManpowerMath.MaxPool(
                new PoolFacts(
                    isTown: true,
                    isCastle: false,
                    hasTown: true,
                    prosperity: 1_000f,
                    security: 100f,
                    governorLeadership: 1_000),
                settings);

            Assert.Equal(2_000, pool);
        }

        [Fact]
        public void TinyPoolOverrideUsesItsMinimum()
        {
            FakeSettings settings = BaseSettings();
            settings.UseTinyPoolsForTesting = true;
            settings.TinyPoolDivisor = 500;
            settings.TinyPoolMinimum = 10;

            int pool = B1071_ManpowerMath.MaxPool(
                new PoolFacts(isTown: true, isCastle: false, hasTown: true, prosperity: 1_000f, security: 100f),
                settings);

            Assert.Equal(10, pool);
        }

        [Fact]
        public void DailyRegenUsesTheConfiguredBaseRate()
        {
            DailyRegenResult regen = B1071_ManpowerMath.DailyRegen(
                new PoolFacts(isTown: true, isCastle: false, hasTown: true, prosperity: 1_000f, security: 100f),
                1_000,
                BaseDailySettings(),
                new FakeRandom());

            Assert.Equal(100, regen.Amount);
        }

        [Fact]
        public void DailyRegenSoftCapEngagesAtFullPool()
        {
            FakeSettings settings = BaseDailySettings();
            settings.EnableRegenSoftCap = true;
            settings.RegenSoftCapStartRatio = 0.75f;
            settings.RegenSoftCapStrength = 1f;

            DailyRegenResult regen = B1071_ManpowerMath.DailyRegen(
                new PoolFacts(
                    isTown: true,
                    isCastle: false,
                    hasTown: true,
                    prosperity: 1_000f,
                    security: 100f,
                    currentPool: 1_000),
                1_000,
                settings,
                new FakeRandom());

            Assert.Equal(10, regen.Amount);
            Assert.Equal(0.1f, regen.SoftCapMultiplier);
        }

        [Fact]
        public void DailyRegenEmergencyBonusBypassesTheNormalCap()
        {
            FakeSettings settings = BaseDailySettings();
            settings.TownRegenMinPercent = 0f;
            settings.TownRegenMaxPercent = 0f;
            settings.RegenCapPercent = 0.1f;
            settings.EnableDepletedEmergencyRegen = true;
            settings.DepletedRegenThresholdPercent = 15;
            settings.DepletedRegenBonusAtZero = 2;

            DailyRegenResult regen = B1071_ManpowerMath.DailyRegen(
                new PoolFacts(
                    isTown: true,
                    isCastle: false,
                    hasTown: true,
                    prosperity: 1_000f,
                    security: 100f,
                    currentPool: 0),
                1_000,
                settings,
                new FakeRandom());

            Assert.Equal(2, regen.Amount);
            Assert.Equal(2, regen.DepletedBonus);
        }

        [Fact]
        public void DailyRegenHardCapWinsOverCastleMinimum()
        {
            FakeSettings settings = BaseDailySettings();
            settings.TownRegenMinPercent = 0f;
            settings.TownRegenMaxPercent = 0f;
            settings.RegenCapPercent = 0.1f;
            settings.CastleMinimumDailyRegen = 5;

            DailyRegenResult regen = B1071_ManpowerMath.DailyRegen(
                new PoolFacts(isTown: false, isCastle: true, hasTown: true, prosperity: 1_000f, security: 100f),
                1_000,
                settings,
                new FakeRandom());

            Assert.Equal(1, regen.Amount);
        }

        [Fact]
        public void DailyRegenUsesInjectedVariance()
        {
            FakeSettings settings = BaseDailySettings();
            settings.EnableRecruitmentVariance = true;
            settings.RecoveryVariancePercent = 10;

            DailyRegenResult regen = B1071_ManpowerMath.DailyRegen(
                new PoolFacts(isTown: true, isCastle: false, hasTown: true, prosperity: 1_000f, security: 100f),
                1_000,
                settings,
                new FakeRandom(floats: new[] { 1.1f }));

            Assert.Equal(110, regen.Amount);
            Assert.Equal(1.1f, regen.VarianceMultiplier);
        }

        [Theory]
        [InlineData(-1f, 0f)]
        [InlineData(0f, 0f)]
        [InlineData(0.5f, 0.5f)]
        [InlineData(1f, 1f)]
        [InlineData(2f, 1f)]
        public void Clamp01StaysWithinItsBounds(float input, float expected)
        {
            Assert.Equal(expected, B1071_ManpowerMath.Clamp01(input));
        }

        [Theory]
        [InlineData(0, 100, 0)]
        [InlineData(24, 100, 1)]
        [InlineData(25, 100, 2)]
        [InlineData(49, 100, 2)]
        [InlineData(50, 100, 3)]
        [InlineData(74, 100, 3)]
        [InlineData(75, 100, 4)]
        [InlineData(1, 0, 0)]
        public void PoolBandUsesQuarterThresholds(int current, int max, int expected)
        {
            Assert.Equal(expected, B1071_ManpowerMath.PoolBand(current, max));
        }

        private static FakeSettings BaseSettings() =>
            new()
            {
                TownPoolMax = 1_000,
                CastlePoolMax = 400,
                OtherPoolMax = 100,
                ProsperityNormalizer = 1_000f,
                MaxPoolProsperityMinScale = 100f,
                MaxPoolProsperityMaxScale = 100f,
                SecurityBonusMinScale = 100f,
                SecurityBonusMaxScale = 100f,
                MaxPoolHearthMultiplier = 0f
            };

        private static FakeSettings BaseDailySettings() =>
            new()
            {
                ProsperityNormalizer = 1_000f,
                TownRegenMinPercent = 10f,
                TownRegenMaxPercent = 10f,
                CastleRegenMinPercent = 10f,
                CastleRegenMaxPercent = 10f,
                OtherRegenPercent = 10f,
                HearthNormalizer = 1,
                HearthBonusMaxPercent = 0f,
                SecurityRegenMinScale = 100f,
                SecurityRegenMaxScale = 100f,
                FoodStocksNormalizer = 1f,
                FoodRegenMinScale = 100f,
                FoodRegenMaxScale = 100f,
                LoyaltyRegenMinScale = 100f,
                LoyaltyRegenMaxScale = 100f,
                SiegeRegenMultiplierPercent = 100f,
                RegenCapPercent = 100f,
                MinimumDailyRegen = 0,
                CastleMinimumDailyRegen = 0
            };
    }
}
