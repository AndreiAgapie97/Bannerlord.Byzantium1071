using TaleWorlds.Core;

namespace Byzantium1071.Campaign
{
    internal sealed class B1071Random : IB1071Random
    {
        internal static readonly B1071Random Instance = new();

        private B1071Random()
        {
        }

        public int Next(int maxExclusive) => MBRandom.RandomInt(maxExclusive);

        public int Next(int minInclusive, int maxExclusive) => MBRandom.RandomInt(minInclusive, maxExclusive);

        public float RangeFloat(float minInclusive, float maxInclusive) =>
            MBRandom.RandomFloatRanged(minInclusive, maxInclusive);

        public int RoundRandomized(float value) => MBRandom.RoundRandomized(value);
    }
}
