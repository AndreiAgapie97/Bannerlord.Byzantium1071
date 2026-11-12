using System;
using System.Collections.Generic;
using Byzantium1071.Campaign;

namespace Byzantium1071.Tests
{
    internal sealed class FakeRandom : IB1071Random
    {
        private readonly Queue<int> _integers;
        private readonly Queue<float> _floats;

        internal FakeRandom(IEnumerable<int>? integers = null, IEnumerable<float>? floats = null)
        {
            _integers = new Queue<int>(integers ?? Array.Empty<int>());
            _floats = new Queue<float>(floats ?? Array.Empty<float>());
        }

        public int Next(int maxExclusive) => Next(0, maxExclusive);

        public int Next(int minInclusive, int maxExclusive)
        {
            int value = TakeInteger();
            if (value < minInclusive || value >= maxExclusive)
            {
                throw new InvalidOperationException(
                    $"Fixed random value {value} is outside [{minInclusive}, {maxExclusive}).");
            }

            return value;
        }

        public float RangeFloat(float minInclusive, float maxInclusive)
        {
            if (_floats.Count == 0)
            {
                throw new InvalidOperationException("No fixed floating-point random values remain.");
            }

            float value = _floats.Dequeue();
            if (value < minInclusive || value > maxInclusive)
            {
                throw new InvalidOperationException(
                    $"Fixed random value {value} is outside [{minInclusive}, {maxInclusive}].");
            }

            return value;
        }

        public int RoundRandomized(float value) => TakeInteger();

        private int TakeInteger()
        {
            if (_integers.Count == 0)
            {
                throw new InvalidOperationException("No fixed integer random values remain.");
            }

            return _integers.Dequeue();
        }
    }
}
