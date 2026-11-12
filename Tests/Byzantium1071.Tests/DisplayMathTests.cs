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

        [Fact]
        public void OverlayDisplayFactsCoverTheExistingBoundaryRules()
        {
            ClanStatusCode neutralPoor = B1071_DisplayMath.BuildClanStatusCode(true, 3, 39_999);
            ClanStatusCode vassalRich = B1071_DisplayMath.BuildClanStatusCode(false, 4, 40_000);
            Assert.True(neutralPoor.IsNeutral);
            Assert.False(neutralPoor.IsRich);
            Assert.Equal(3, neutralPoor.FiefCount);
            Assert.False(vassalRich.IsNeutral);
            Assert.True(vassalRich.IsRich);

            Assert.Equal(FoodTrendDisplayKind.Unknown, B1071_DisplayMath.FormatFoodTrend(float.NaN).Kind);
            Assert.Equal(FoodTrendDisplayKind.Flat, B1071_DisplayMath.FormatFoodTrend(0.10f).Kind);
            Assert.Equal(FoodTrendDisplayKind.Flat, B1071_DisplayMath.FormatFoodTrend(-0.10f).Kind);
            Assert.Equal(FoodTrendDisplayKind.Rising, B1071_DisplayMath.FormatFoodTrend(0.11f).Kind);
            Assert.Equal(FoodTrendDisplayKind.Falling, B1071_DisplayMath.FormatFoodTrend(-0.11f).Kind);

            Assert.True(B1071_DisplayMath.FormatWarDuration(0).IsNew);
            Assert.False(B1071_DisplayMath.FormatWarDuration(1).IsNew);
            Assert.Equal(87, B1071_DisplayMath.FormatWarDuration(87).Days);
        }

        [Fact]
        public void CompactExhaustionAndPeacePressureFactsPreserveUiSelectionRules()
        {
            ExhaustionCompactDisplay fresh = B1071_DisplayMath.GetExhaustionCompact(
                float.NaN, false, DiplomacyPressureBand.Low);
            ExhaustionCompactDisplay banded = B1071_DisplayMath.GetExhaustionCompact(
                24.9f, true, DiplomacyPressureBand.Rising);
            Assert.Equal(ExhaustionDisplayTag.Fresh, fresh.Tag);
            Assert.False(fresh.IncludeValue);
            Assert.Equal(ExhaustionDisplayTag.Rising, banded.Tag);
            Assert.True(banded.IncludeValue);
            Assert.Equal(24, banded.RoundedValue);

            PeacePressureDisplay neutral = B1071_DisplayMath.GetPeacePressureBand(float.PositiveInfinity, true);
            PeacePressureDisplay peace = B1071_DisplayMath.GetPeacePressureBand(800f, true);
            PeacePressureDisplay war = B1071_DisplayMath.GetPeacePressureBand(-1_600f, true);
            Assert.Equal(PeacePressureDisplayDirection.Neutral, neutral.Direction);
            Assert.Equal(PeacePressureDisplayDirection.Peace, peace.Direction);
            Assert.Equal("High", peace.Level);
            Assert.Equal(PeacePressureDisplayDirection.War, war.Direction);
            Assert.Equal("Extreme", war.Level);
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
