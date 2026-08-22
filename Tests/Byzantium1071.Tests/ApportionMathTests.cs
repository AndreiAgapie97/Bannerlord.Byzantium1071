using System;
using System.Collections.Generic;
using System.Linq;
using Byzantium1071.Campaign;
using FsCheck.Xunit;
using Xunit;

namespace Byzantium1071.Tests
{
    public sealed class ApportionMathTests
    {
        [Fact]
        public void EmptyWeightsOrNonPositiveTotalsProduceNoAllocation()
        {
            Assert.Empty(B1071_ApportionMath.AllocateByWeights(0, Weights(), key => key));
            Assert.Empty(B1071_ApportionMath.AllocateByWeights(-1, Weights(("a", 1)), key => key));
            Assert.Empty(B1071_ApportionMath.AllocateByWeights(5, Weights(), key => key));
        }

        [Fact]
        public void PositiveWeightsUseLargestRemainderAndPreserveTheTotal()
        {
            Dictionary<string, int> allocation = B1071_ApportionMath.AllocateByWeights(
                10,
                Weights(("a", 1), ("b", 2), ("c", 3)),
                key => key);

            Assert.Equal(2, allocation["a"]);
            Assert.Equal(3, allocation["b"]);
            Assert.Equal(5, allocation["c"]);
            Assert.Equal(10, allocation.Values.Sum());
        }

        [Fact]
        public void NonPositiveWeightsDoNotReceiveWeightedShares()
        {
            Dictionary<string, int> allocation = B1071_ApportionMath.AllocateByWeights(
                7,
                Weights(("negative", -4), ("zero", 0), ("positive", 3)),
                key => key);

            Assert.Equal(0, allocation["negative"]);
            Assert.Equal(0, allocation["zero"]);
            Assert.Equal(7, allocation["positive"]);
        }

        [Fact]
        public void AllNonPositiveWeightsFallBackToAnEvenSplit()
        {
            Dictionary<string, int> allocation = B1071_ApportionMath.AllocateByWeights(
                7,
                Weights(("first", 0), ("second", -1), ("third", 0)),
                key => key);

            Assert.Equal(3, allocation["first"]);
            Assert.Equal(2, allocation["second"]);
            Assert.Equal(2, allocation["third"]);
            Assert.Equal(7, allocation.Values.Sum());
        }

        [Fact]
        public void EqualRemaindersUseTheIdSelectorAsAStableTiebreak()
        {
            Dictionary<string, int> allocation = B1071_ApportionMath.AllocateByWeights(
                1,
                Weights(("zeta", 1), ("alpha", 1)),
                key => key);

            Assert.Equal(1, allocation["alpha"]);
            Assert.Equal(0, allocation["zeta"]);
        }

        [Property(MaxTest = 1000)]
        public bool AllocationAlwaysPreservesTheRequestedTotal(
            int rawTotal,
            int firstWeight,
            int secondWeight,
            int thirdWeight)
        {
            int total = NonNegative(rawTotal);
            Dictionary<string, int> allocation = B1071_ApportionMath.AllocateByWeights(
                total,
                Weights(
                    ("first", BoundedWeight(firstWeight)),
                    ("second", BoundedWeight(secondWeight)),
                    ("third", BoundedWeight(thirdWeight))),
                key => key);

            return allocation.Values.Sum() == total;
        }

        private static Dictionary<string, int> Weights(params (string Key, int Weight)[] entries) =>
            entries.ToDictionary(entry => entry.Key, entry => entry.Weight);

        private static int NonNegative(int value) =>
            value == int.MinValue ? 0 : Math.Abs(value % 10_000);

        private static int BoundedWeight(int value) => value % 100;
    }
}
