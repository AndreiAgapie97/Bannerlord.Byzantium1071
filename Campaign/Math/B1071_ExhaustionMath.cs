using System;
using Byzantium1071.Campaign.Settings;

namespace Byzantium1071.Campaign
{
    /// <summary>
    /// WP5: Graded diplomacy pressure levels that replace hard exhaustion threshold cliffs.
    /// </summary>
    public enum DiplomacyPressureBand
    {
        Low,     // Normal operations
        Rising,  // Elevated peace bias, softer war penalties
        Crisis   // War declarations blocked, strong peace pressure, forced peace eligible
    }

    internal static class B1071_ExhaustionMath
    {
        internal static DiplomacyPressureBand EvaluatePressureBand(
            float exhaustion,
            DiplomacyPressureBand current,
            IB1071Settings settings)
        {
            float risingStart = Math.Max(1f, settings.PressureBandRisingStart);
            float crisisStart = Math.Max(risingStart + 1f, settings.PressureBandCrisisStart);
            float hysteresis = Math.Max(0f, settings.PressureBandHysteresis);

            DiplomacyPressureBand next = exhaustion >= crisisStart
                ? DiplomacyPressureBand.Crisis
                : exhaustion >= risingStart
                    ? DiplomacyPressureBand.Rising
                    : DiplomacyPressureBand.Low;

            if (next < current)
            {
                if (current == DiplomacyPressureBand.Crisis && exhaustion >= crisisStart - hysteresis)
                {
                    return DiplomacyPressureBand.Crisis;
                }

                if (current == DiplomacyPressureBand.Rising && exhaustion >= risingStart - hysteresis)
                {
                    return DiplomacyPressureBand.Rising;
                }
            }

            return next;
        }

        internal static DiplomacyPressureBand MapExhaustionToLegacyBand(float exhaustion, IB1071Settings settings)
        {
            if (exhaustion >= settings.DiplomacyNoNewWarThreshold) return DiplomacyPressureBand.Crisis;
            if (exhaustion >= settings.DiplomacyPeacePressureThreshold) return DiplomacyPressureBand.Rising;
            return DiplomacyPressureBand.Low;
        }

        internal static float BandPeaceBias(DiplomacyPressureBand band, IB1071Settings settings)
        {
            return band switch
            {
                DiplomacyPressureBand.Low => settings.PeaceBiasBandLow,
                DiplomacyPressureBand.Rising => settings.PeaceBiasBandHigh,
                DiplomacyPressureBand.Crisis => settings.PeaceBiasBandHigh * 1.5f,
                _ => settings.PeaceBiasBandLow
            };
        }

        internal static float ForcedPeaceThreshold(int activeMajorWars, IB1071Settings settings)
        {
            float baseThreshold = Math.Max(1f, settings.DiplomacyForcedPeaceThreshold);
            int pressureStartWars = Math.Max(1, settings.DiplomacyMajorWarPressureStartCount);
            float reductionPerWar = Math.Max(0f, settings.DiplomacyForcedPeaceThresholdReductionPerMajorWar);
            int extraWars = Math.Max(0, activeMajorWars - pressureStartWars + 1);
            return Math.Max(1f, baseThreshold - (extraWars * reductionPerWar));
        }

        internal static float MajorWarPressureBias(int activeMajorWars, IB1071Settings settings)
        {
            int pressureStart = Math.Max(1, settings.DiplomacyMajorWarPressureStartCount);
            int extraWars = Math.Max(0, activeMajorWars - pressureStart + 1);
            return extraWars * Math.Max(0f, settings.DiplomacyExtraPeaceBiasPerMajorWar);
        }

        internal static float ManpowerDiplomacyPeaceBias(float averageRatio, IB1071Settings settings)
        {
            if (!settings.EnableManpowerDiplomacyPressure) return 0f;

            float threshold = Math.Max(0.001f, settings.ManpowerDiplomacyThresholdPercent / 100f);
            if (averageRatio >= threshold) return 0f;

            float depletion = (threshold - averageRatio) / threshold;
            return depletion * Math.Max(0f, settings.ManpowerDiplomacyPressureStrength);
        }

        internal static float EarlyWarPeacePenalty(float elapsedDays, bool isMultiFrontCrisis, IB1071Settings settings)
        {
            int minimumDays = Math.Max(0, settings.MinWarDurationDaysBeforeForcedPeace);
            if (minimumDays <= 0) return 0f;

            int effectiveMinimumDays = minimumDays;
            if (settings.EnableMultiFrontWarRelief)
            {
                int emergencyMinimum = Math.Max(1, settings.EmergencyMinWarDays);
                if (emergencyMinimum < minimumDays && isMultiFrontCrisis)
                {
                    effectiveMinimumDays = emergencyMinimum;
                }
            }

            if (elapsedDays >= effectiveMinimumDays) return 0f;
            return (1f - (elapsedDays / effectiveMinimumDays)) * settings.EarlyWarPeacePenaltyStrength;
        }

        internal static float WarSupportPenalty(
            DiplomacyPressureBand band,
            float exhaustion,
            float majorWarBias,
            IB1071Settings settings)
        {
            float penaltyCap = Math.Abs(settings.WarSupportPenaltyCap);
            return band switch
            {
                DiplomacyPressureBand.Crisis => Math.Min(
                    penaltyCap,
                    exhaustion * settings.DiplomacyWarSupportPenaltyPerPoint + majorWarBias),
                DiplomacyPressureBand.Rising => Math.Min(
                    penaltyCap * 0.6f,
                    exhaustion * settings.DiplomacyWarSupportPenaltyPerPoint * 0.7f + majorWarBias),
                _ => exhaustion * settings.DiplomacyWarSupportPenaltyPerPoint * 0.3f + majorWarBias
            };
        }

        internal static float PeaceSupportBonus(
            DiplomacyPressureBand band,
            float exhaustion,
            float majorWarBias,
            IB1071Settings settings)
        {
            float bonusCap = Math.Max(0f, settings.PeaceSupportBonusCap);
            return Math.Min(bonusCap, exhaustion * BandPeaceBias(band, settings) + majorWarBias);
        }

        internal static float LegacyWarSupportPenalty(float exhaustion, float majorWarBias, IB1071Settings settings) =>
            exhaustion * settings.DiplomacyWarSupportPenaltyPerPoint + majorWarBias;

        internal static float LegacyPeaceSupportBonus(float exhaustion, float majorWarBias, IB1071Settings settings) =>
            exhaustion * settings.DiplomacyPeaceSupportBonusPerPoint + majorWarBias;
    }
}
