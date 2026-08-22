using System.Collections.Generic;
using Byzantium1071.Campaign;
using FsCheck.Xunit;
using Xunit;

namespace Byzantium1071.Tests
{
    public sealed class CastlePoolMathTests
    {
        [Fact]
        public void DynamicCapacityUsesProsperityAndWallLevelWhileStaticModeUsesTheFlatCap()
        {
            FakeSettings settings = CastleSettings();

            Assert.Equal(18, B1071_CastlePoolMath.PoolCapacity(1_500f, 2, settings));

            settings.EnableDynamicPoolCapacity = false;
            Assert.Equal(20, B1071_CastlePoolMath.PoolCapacity(1_500f, 2, settings));
        }

        [Fact]
        public void DailyRegenInterpolatesAndDoesNotExceedPoolCapacity()
        {
            FakeSettings settings = CastleSettings();
            settings.ProsperityNormalizer = 1_000f;
            settings.CastleEliteRegenMin = 1;
            settings.CastleEliteRegenMax = 3;

            Assert.Equal(1, B1071_CastlePoolMath.DailyRegenCount(0f, 20, 0, settings));
            Assert.Equal(3, B1071_CastlePoolMath.DailyRegenCount(1_000f, 20, 0, settings));
            Assert.Equal(1, B1071_CastlePoolMath.DailyRegenCount(1_000f, 20, 19, settings));
        }

        [Fact]
        public void WeightedBucketSelectionUsesRelativeWeightsAndInjectedRolls()
        {
            var weights = new List<int> { 45, 35, 15, 5 };

            Assert.Equal(1, B1071_CastlePoolMath.ChooseWeightedBucketIndex(weights, new FakeRandom(integers: new[] { 45 })));
            Assert.Equal(3, B1071_CastlePoolMath.ChooseWeightedBucketIndex(weights, new FakeRandom(integers: new[] { 95 })));
            Assert.Equal(-1, B1071_CastlePoolMath.ChooseWeightedBucketIndex(new List<int> { 0, 0 }, new FakeRandom()));
        }

        [Fact]
        public void AiBufferKeepsTheConfiguredTreasuryReserve()
        {
            FakeSettings settings = CastleSettings();
            settings.CastleAiGoldBufferMultiplier = 3;

            Assert.Equal(3, B1071_CastlePoolMath.AiBufferedAffordableCount(100, 10, 5, settings));
            Assert.Equal(2, B1071_CastlePoolMath.AiBufferedAffordableCount(90, 10, 5, settings));
            Assert.Equal(5, B1071_CastlePoolMath.AiBufferedAffordableCount(0, 0, 5, settings));
        }

        [Fact]
        public void TierTablesApplyTheExpectedPrisonerDaysAndGoldCosts()
        {
            FakeSettings settings = CastleSettings();
            settings.CastlePrisonerAutoEnslaveTierMax = 3;
            settings.CastleRecruitT4Days = 10;
            settings.CastleRecruitT5Days = 21;
            settings.CastleRecruitT6Days = 35;
            settings.CastleRecruitGoldT2 = 150;
            settings.CastleRecruitGoldT3 = 400;
            settings.CastleRecruitGoldT4 = 1_200;
            settings.CastleRecruitGoldT5 = 2_500;
            settings.CastleRecruitGoldT6 = 5_000;

            Assert.Equal(0, B1071_CastlePoolMath.RequiredPrisonerDays(3, settings));
            Assert.Equal(21, B1071_CastlePoolMath.RequiredPrisonerDays(5, settings));
            Assert.Equal(150, B1071_CastlePoolMath.GoldCostForTier(1, settings));
            Assert.Equal(5_000, B1071_CastlePoolMath.GoldCostForTier(7, settings));
        }

        [Fact]
        public void RecruitmentFeeSplitAppliesClanWaiversWithoutChangingTheBaseShares()
        {
            CastleFeeSplit ordinary = B1071_CastlePoolMath.RecruitmentFeeSplit(1_200, 1, 30, true, false, false);
            Assert.Equal(1_200, ordinary.RecruiterCost);
            Assert.Equal(360, ordinary.OwnerPayment);
            Assert.Equal(840, ordinary.DepositorPayment);

            CastleFeeSplit ownerWaived = B1071_CastlePoolMath.RecruitmentFeeSplit(1_200, 1, 30, true, true, false);
            Assert.Equal(840, ownerWaived.RecruiterCost);
            Assert.Equal(0, ownerWaived.OwnerPayment);
            Assert.Equal(840, ownerWaived.DepositorPayment);

            CastleFeeSplit untracked = B1071_CastlePoolMath.RecruitmentFeeSplit(1_200, 1, 30, false, false, false);
            Assert.Equal(1_200, untracked.RecruiterCost);
            Assert.Equal(1_200, untracked.OwnerPayment);
            Assert.Equal(0, untracked.DepositorPayment);
        }

        [Property(MaxTest = 1000)]
        public bool UnwaivedHoldingFeeAlwaysConservesGold(int rawTotal, int rawFee)
        {
            int total = NonNegative(rawTotal) % 1_000_000;
            int fee = NonNegative(rawFee) % 101;
            CastleFeeSplit split = B1071_CastlePoolMath.SplitHoldingFee(total, fee);

            return split.OwnerPayment >= 0
                && split.DepositorPayment >= 0
                && split.OwnerPayment + split.DepositorPayment == total;
        }

        private static FakeSettings CastleSettings() =>
            new()
            {
                EnableDynamicPoolCapacity = true,
                CastleElitePoolBaseCapDynamic = 5,
                CastleElitePoolProsperityScaling = 0.005f,
                CastleElitePoolWallBonus = 3,
                CastleElitePoolMax = 20,
                ProsperityNormalizer = 1_000f,
                CastleEliteRegenMin = 1,
                CastleEliteRegenMax = 2,
                CastleAiGoldBufferMultiplier = 3
            };

        private static int NonNegative(int value) =>
            value == int.MinValue ? 0 : System.Math.Abs(value);
    }
}
