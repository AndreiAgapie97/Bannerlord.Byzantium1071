using Byzantium1071.Campaign;
using FsCheck.Xunit;
using Xunit;

namespace Byzantium1071.Tests
{
    public sealed class DisplayMathTests
    {
        [Fact]
        public void ColumnTruncationHandlesZeroOneAndFullWidths()
        {
            Assert.Equal(string.Empty, B1071_DisplayMath.TruncateForColumn("abcdef", 0));
            Assert.Equal("a", B1071_DisplayMath.TruncateForColumn("abcdef", 1));
            Assert.Equal("ab…", B1071_DisplayMath.TruncateForColumn("abcdef", 3, out string hint));
            Assert.Equal("abcdef", hint);
            Assert.Equal("abc", B1071_DisplayMath.TruncateForColumn("abc", 3, out hint));
            Assert.Equal(string.Empty, hint);
        }

        [Fact]
        public void QueryScoreRanksExactPrefixAndSubstringMatches()
        {
            Assert.Equal(1000, B1071_DisplayMath.ComputeQueryScore("alex", new[] { "Alex" }, null));
            Assert.Equal(850, B1071_DisplayMath.ComputeQueryScore("ale", new[] { "Alex" }, null));
            Assert.Equal(650, B1071_DisplayMath.ComputeQueryScore("lex", new[] { "Alex" }, null));
            Assert.Equal(0, B1071_DisplayMath.ComputeQueryScore("", new[] { "Alex" }, null));
            Assert.Equal(1500, B1071_DisplayMath.ComputeQueryScore("alex", new[] { "Alex" }, new[] { 1.5f }));
        }

        [Fact]
        public void RebellionAndInstabilityScoresStayAtTheirDocumentedEndpoints()
        {
            Assert.Equal(0, B1071_DisplayMath.ComputeRebellionRiskScore(100f, 100f, 10f, false, false));
            Assert.Equal(100, B1071_DisplayMath.ComputeRebellionRiskScore(0f, 0f, -20f, true, true));
            Assert.Equal(1, B1071_DisplayMath.EstimateTimeToRebelDays(20f, -1f, false));
            Assert.Equal(25, B1071_DisplayMath.EstimateTimeToRebelDays(50f, -1f, false));
            Assert.InRange(B1071_DisplayMath.ComputeInstabilityScore(true, 0, 0, 0, -100), 0, 100);
        }

        [Fact]
        public void CompactAndClassificationFormattingUsesExistingThresholds()
        {
            Assert.Equal("+999", B1071_DisplayMath.FormatFoodTrendCompact(1000f));
            Assert.Equal("-999", B1071_DisplayMath.FormatFoodTrendCompact(-1000f));
            Assert.Equal("?", B1071_DisplayMath.FormatFoodTrendCompact(float.NaN));
            Assert.Equal("3 vs 7", B1071_DisplayMath.FormatTerritoryCount(3, 7));
            Assert.Equal("N/A", B1071_DisplayMath.FormatRuler("", 30, "N/A"));
            Assert.Equal(ExhaustionDisplayTag.Rising,
                B1071_DisplayMath.ExhaustionTag(50f, true, DiplomacyPressureBand.Rising));
            Assert.Equal("High", B1071_DisplayMath.PeacePressureLevel(800f, true));
        }

        [Property(MaxTest = 1000)]
        public bool ScoresAlwaysRemainBounded(float loyalty, float security, float foodChange, int gold, int influence)
        {
            if (float.IsNaN(loyalty) || float.IsInfinity(loyalty)
                || float.IsNaN(security) || float.IsInfinity(security)
                || float.IsNaN(foodChange) || float.IsInfinity(foodChange))
            {
                return true;
            }

            int rebellion = B1071_DisplayMath.ComputeRebellionRiskScore(loyalty, security, foodChange, false, false);
            int instability = B1071_DisplayMath.ComputeInstabilityScore(false, 1, gold, influence, 0);
            return rebellion >= 0 && rebellion <= 100 && instability >= 0 && instability <= 100;
        }
    }
}
