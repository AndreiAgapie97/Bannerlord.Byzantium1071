namespace Byzantium1071.Campaign
{
    public interface IB1071Random
    {
        int Next(int maxExclusive);
        int Next(int minInclusive, int maxExclusive);
        float RangeFloat(float minInclusive, float maxInclusive);
        int RoundRandomized(float value);
    }
}
