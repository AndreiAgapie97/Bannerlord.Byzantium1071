using System;
using System.Linq;
using Byzantium1071.Campaign;
using Xunit;

namespace Byzantium1071.Tests
{
    public sealed class CastleConformityMathTests
    {
        [Theory]
        [InlineData(0, 240)]
        [InlineData(100, 360)]
        [InlineData(250, 540)]
        public void DailyBudgetUsesGovernorLeadershipWithoutPartyPerks(int leadership, int expected)
        {
            int remainder = 0;
            Assert.Equal(expected, B1071_CastleConformityMath.DailyBudget(leadership, ref remainder));
        }

        [Fact]
        public void FractionalPointsCarryAcrossDays()
        {
            int remainder = 0, total = 0;
            for (int day = 0; day < 5; day++) total += B1071_CastleConformityMath.DailyBudget(1, ref remainder);
            Assert.Equal(1206, total);
            Assert.Equal(0, remainder);
        }

        [Fact]
        public void MoreTypesOrPrisonersNeverMultiplyTheBudget()
        {
            foreach (int types in new[] { 1, 4, 20 })
            {
                int cursor = 0;
                int[] result = B1071_CastleConformityMath.Allocate(Enumerable.Repeat(100000, types).ToArray(), 241, ref cursor);
                Assert.Equal(241, result.Sum());
                Assert.InRange(result.Max() - result.Min(), 0, 1);
            }
        }

        [Fact]
        public void FinishedTypesLeaveTheRotationAndExcessBudgetIsDiscarded()
        {
            int cursor = 0;
            Assert.Equal(new[] { 0, 2, 238 }, B1071_CastleConformityMath.Allocate(new[] { 0, 2, 1000 }, 240, ref cursor));
            Assert.Equal(new[] { 0, 2, 3 }, B1071_CastleConformityMath.Allocate(new[] { 0, 2, 3 }, 240, ref cursor));
        }

        [Fact]
        public void AllocationMatchesAnIndependentPointByPointRotation()
        {
            var random = new Random(217);
            for (int trial = 0; trial < 500; trial++)
            {
                int[] needs = Enumerable.Range(0, random.Next(1, 20)).Select(_ => random.Next(0, 500)).ToArray();
                int budget = random.Next(0, 1500), cursor = random.Next(needs.Length), expectedCursor = cursor;
                var expected = new int[needs.Length];
                for (int point = 0; point < budget && expected.Sum() < needs.Sum(); point++)
                {
                    while (expected[expectedCursor] == needs[expectedCursor]) expectedCursor = (expectedCursor + 1) % needs.Length;
                    expected[expectedCursor]++;
                    expectedCursor = (expectedCursor + 1) % needs.Length;
                }
                Assert.Equal(expected, B1071_CastleConformityMath.Allocate(needs, budget, ref cursor));
                Assert.Equal(expectedCursor, cursor);
            }
        }

        [Fact]
        public void HugeInputsDoNotOverflowOrRequireBillionsOfIterations()
        {
            int cursor = 0;
            int[] grants = B1071_CastleConformityMath.Allocate(new[] { int.MaxValue, int.MaxValue }, int.MaxValue, ref cursor);
            Assert.Equal(int.MaxValue, grants.Sum());
            Assert.Equal(int.MaxValue, B1071_CastleConformityMath.Capacity(int.MaxValue, int.MaxValue));
        }

        [Theory]
        [InlineData(40, 1000, 0, 35, 0)]
        [InlineData(40, 1000, 7, 35, 200)]
        [InlineData(40, 1000, 34, 35, 971)]
        [InlineData(40, 1000, 35, 35, 40000)]
        [InlineData(40, 1000, 90, 35, 40000)]
        public void LegacyMigrationPreservesReadyStacksButOnlyOnePartialRecruit(int count, int cost, int held, int required, int expected)
        {
            Assert.Equal(expected, B1071_CastleConformityMath.LegacyProgress(count, cost, held, required));
        }

        [Fact]
        public void ArrivalsDoNotCreateReadinessAndRecruitmentSpendsIt()
        {
            Assert.Equal(3, B1071_CastleConformityMath.ReadyCount(3, 3250, 1000));
            Assert.Equal(3, B1071_CastleConformityMath.ReadyCount(100, 3250, 1000));
            Assert.Equal(2, B1071_CastleConformityMath.ReadyCount(99, 2250, 1000));
            Assert.Equal(0, B1071_CastleConformityMath.ReadyCount(97, 250, 1000));
        }
    }
}
