using System;

namespace Byzantium1071.Campaign
{
    internal enum ExhaustionDisplayTag
    {
        Fresh,
        Low,
        Rising,
        Crisis,
        Strained,
        Tired,
        Exhausted
    }

    internal enum FoodTrendDisplayKind
    {
        Unknown,
        Rising,
        Falling,
        Flat
    }

    internal enum PeacePressureDisplayDirection
    {
        Neutral,
        Peace,
        War
    }

    internal readonly struct ClanStatusCode
    {
        internal ClanStatusCode(bool isNeutral, bool isRich, int fiefCount)
        {
            IsNeutral = isNeutral;
            IsRich = isRich;
            FiefCount = fiefCount;
        }

        internal bool IsNeutral { get; }
        internal bool IsRich { get; }
        internal int FiefCount { get; }
    }

    internal readonly struct FoodTrendDisplay
    {
        internal FoodTrendDisplay(FoodTrendDisplayKind kind, float value)
        {
            Kind = kind;
            Value = value;
        }

        internal FoodTrendDisplayKind Kind { get; }
        internal float Value { get; }
    }

    internal readonly struct WarDurationDisplay
    {
        internal WarDurationDisplay(bool isNew, int days)
        {
            IsNew = isNew;
            Days = days;
        }

        internal bool IsNew { get; }
        internal int Days { get; }
    }

    internal readonly struct ExhaustionCompactDisplay
    {
        internal ExhaustionCompactDisplay(ExhaustionDisplayTag tag, int roundedValue, bool includeValue)
        {
            Tag = tag;
            RoundedValue = roundedValue;
            IncludeValue = includeValue;
        }

        internal ExhaustionDisplayTag Tag { get; }
        internal int RoundedValue { get; }
        internal bool IncludeValue { get; }
    }

    internal readonly struct PeacePressureDisplay
    {
        internal PeacePressureDisplay(PeacePressureDisplayDirection direction, string level)
        {
            Direction = direction;
            Level = level;
        }

        internal PeacePressureDisplayDirection Direction { get; }
        internal string Level { get; }
    }

    internal static class B1071_DisplayMath
    {
        internal static string FormatManpower(int current, int maximum) =>
            current.ToString("N0") + "/" + maximum.ToString("N0");

        internal static string TruncateForColumn(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text;

            if (maxLength <= 1)
                return text.Substring(0, maxLength);

            return text.Substring(0, maxLength - 1) + "…";
        }

        internal static string TruncateForColumn(string text, int maxLength, out string hintText)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            {
                hintText = string.Empty;
                return text;
            }

            hintText = text;
            return TruncateForColumn(text, maxLength);
        }

        internal static int ComputeQueryScore(string query, string[] fields, float[]? weights)
        {
            if (string.IsNullOrEmpty(query) || fields == null || fields.Length == 0)
                return 0;

            string q = query.Trim();
            if (q.Length == 0)
                return 0;

            string qLower = q.ToLowerInvariant();
            int best = 0;
            string[] queryTokens = qLower.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            for (int index = 0; index < fields.Length; index++)
            {
                string source = fields[index] ?? string.Empty;
                if (source.Length == 0)
                    continue;

                float weight = weights != null && index < weights.Length ? weights[index] : 1f;
                string sourceLower = source.ToLowerInvariant();
                int fieldScore = sourceLower == qLower
                    ? 1000
                    : sourceLower.StartsWith(qLower, StringComparison.Ordinal)
                        ? 850
                        : sourceLower.Contains(qLower)
                            ? 650
                            : 0;

                if (fieldScore == 0 && queryTokens.Length > 1)
                {
                    int tokenHits = 0;
                    for (int tokenIndex = 0; tokenIndex < queryTokens.Length; tokenIndex++)
                    {
                        if (sourceLower.Contains(queryTokens[tokenIndex]))
                            tokenHits++;
                    }

                    if (tokenHits > 0)
                        fieldScore = 400 + tokenHits * 60;
                }

                best = Math.Max(best, (int)(fieldScore * weight));
            }

            return best;
        }

        internal static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        internal static int ComputeInstabilityScore(
            bool isDefectionRisk,
            int fiefCount,
            int gold,
            int influence,
            int relationToPlayer)
        {
            float fiefless = fiefCount <= 0 ? 1f : 0f;
            float poor = Clamp01((40000f - gold) / 40000f);
            float lowInfluence = Clamp01((150f - influence) / 150f);
            float relationFactor = isDefectionRisk
                ? Clamp01((20f - relationToPlayer) / 60f)
                : Clamp01((relationToPlayer + 20f) / 80f);
            float weighted = 0.35f * fiefless
                + 0.25f * poor
                + 0.20f * relationFactor
                + 0.20f * lowInfluence;
            return ClampPercent((int)Math.Round(100f * Clamp01(weighted), MidpointRounding.AwayFromZero));
        }

        internal static int EstimateTimeToRebelDays(float loyalty, float loyaltyChange, bool inRebelliousState)
        {
            if (inRebelliousState) return 0;
            if (loyalty <= 25f) return 1;
            if (float.IsNaN(loyaltyChange) || float.IsInfinity(loyaltyChange) || loyaltyChange >= -0.01f)
                return int.MaxValue;

            double rawDays = (loyalty - 25f) / -loyaltyChange;
            if (double.IsNaN(rawDays) || double.IsInfinity(rawDays) || rawDays <= 0d)
                return int.MaxValue;

            return Math.Min(999, Math.Max(1, (int)Math.Ceiling(rawDays)));
        }

        internal static int ComputeRebellionRiskScore(
            float loyalty,
            float security,
            float foodChange,
            bool cultureMismatch,
            bool inRebelliousState)
        {
            float loyaltyRisk = Clamp01((50f - loyalty) / 50f);
            float securityRisk = Clamp01((45f - security) / 45f);
            float foodRisk = foodChange < 0f ? Clamp01(-foodChange / 8f) : 0f;
            float weighted = 0.50f * loyaltyRisk
                + 0.20f * securityRisk
                + 0.15f * foodRisk
                + 0.10f * (cultureMismatch ? 1f : 0f)
                + 0.05f * (inRebelliousState ? 1f : 0f);
            return ClampPercent((int)Math.Round(100f * Clamp01(weighted), MidpointRounding.AwayFromZero));
        }

        internal static string FormatFoodTrendCompact(float foodChange)
        {
            if (float.IsNaN(foodChange) || float.IsInfinity(foodChange)) return "?";

            int rounded = (int)Math.Round(foodChange, MidpointRounding.AwayFromZero);
            if (rounded == 0) return "0";
            if (rounded > 999) return "+999";
            if (rounded < -999) return "-999";
            return rounded > 0 ? "+" + rounded : rounded.ToString();
        }

        internal static ClanStatusCode BuildClanStatusCode(bool isNeutral, int fiefCount, int gold) =>
            new(isNeutral, gold >= 40000, fiefCount);

        internal static FoodTrendDisplay FormatFoodTrend(float foodChange)
        {
            if (float.IsNaN(foodChange) || float.IsInfinity(foodChange))
                return new FoodTrendDisplay(FoodTrendDisplayKind.Unknown, 0f);
            if (foodChange > 0.10f)
                return new FoodTrendDisplay(FoodTrendDisplayKind.Rising, foodChange);
            if (foodChange < -0.10f)
                return new FoodTrendDisplay(FoodTrendDisplayKind.Falling, foodChange);
            return new FoodTrendDisplay(FoodTrendDisplayKind.Flat, foodChange);
        }

        internal static WarDurationDisplay FormatWarDuration(int days) =>
            new(days <= 0, days);

        internal static string FormatTerritoryCount(int countA, int countB) => countA + " vs " + countB;

        internal static string FormatRuler(string rulerName, int rulerAge, string notAvailable) =>
            string.IsNullOrEmpty(rulerName) || rulerAge <= 0
                ? notAvailable
                : rulerName + " (" + rulerAge + ")";

        internal static ExhaustionDisplayTag ExhaustionTag(float exhaustion, bool pressureBands, DiplomacyPressureBand band)
        {
            if (float.IsNaN(exhaustion) || float.IsInfinity(exhaustion)) exhaustion = 0f;
            if (pressureBands)
            {
                return band switch
                {
                    DiplomacyPressureBand.Crisis => ExhaustionDisplayTag.Crisis,
                    DiplomacyPressureBand.Rising => ExhaustionDisplayTag.Rising,
                    _ => exhaustion < 1f ? ExhaustionDisplayTag.Fresh : ExhaustionDisplayTag.Low
                };
            }

            if (exhaustion < 1f) return ExhaustionDisplayTag.Fresh;
            if (exhaustion < 25f) return ExhaustionDisplayTag.Strained;
            if (exhaustion < 50f) return ExhaustionDisplayTag.Tired;
            if (exhaustion < 75f) return ExhaustionDisplayTag.Exhausted;
            return ExhaustionDisplayTag.Crisis;
        }

        internal static ExhaustionCompactDisplay GetExhaustionCompact(
            float exhaustion,
            bool pressureBands,
            DiplomacyPressureBand band)
        {
            if (float.IsNaN(exhaustion) || float.IsInfinity(exhaustion)) exhaustion = 0f;

            ExhaustionDisplayTag tag = ExhaustionTag(exhaustion, pressureBands, band);
            int rounded = (int)exhaustion;
            bool includeValue = pressureBands ? rounded > 0 : tag != ExhaustionDisplayTag.Fresh;
            return new ExhaustionCompactDisplay(tag, rounded, includeValue);
        }

        internal static string PeacePressureLevel(float pressure, bool pressureBands)
        {
            float absolute = Math.Abs(pressure);
            if (pressureBands)
            {
                return absolute >= 1600f ? "Extreme"
                    : absolute >= 800f ? "High"
                    : absolute >= 300f ? "Medium"
                    : absolute >= 80f ? "Low"
                    : "Light";
            }

            return absolute >= 100000f ? "Extreme"
                : absolute >= 25000f ? "High"
                : absolute >= 5000f ? "Medium"
                : absolute >= 1000f ? "Low"
                : "Light";
        }

        internal static PeacePressureDisplay GetPeacePressureBand(float pressure, bool pressureBands)
        {
            if (float.IsNaN(pressure) || float.IsInfinity(pressure) || pressure == 0f)
                return new PeacePressureDisplay(PeacePressureDisplayDirection.Neutral, "Light");

            PeacePressureDisplayDirection direction = pressure > 0f
                ? PeacePressureDisplayDirection.Peace
                : PeacePressureDisplayDirection.War;
            return new PeacePressureDisplay(direction, PeacePressureLevel(pressure, pressureBands));
        }

        private static int ClampPercent(int value) => Math.Max(0, Math.Min(100, value));
    }
}
