using System;
using Byzantium1071.Campaign.Settings;

namespace Byzantium1071.Campaign
{
    internal static class B1071_ManpowerMath
    {
        internal static int MaxPool(PoolFacts facts, IB1071Settings settings)
        {
            int baseMax =
                facts.IsTown ? settings.TownPoolMax :
                facts.IsCastle ? settings.CastlePoolMax :
                settings.OtherPoolMax;

            baseMax = Math.Max(1, baseMax);

            float prosperityScale = 1.0f;
            float securityBonus = 1.0f;

            if (facts.HasTown)
            {
                float prosperityNormalized = Clamp01(facts.Prosperity / Math.Max(1f, settings.ProsperityNormalizer));
                float prosperityMinimum = Math.Max(0.01f, settings.MaxPoolProsperityMinScale / 100f);
                float prosperityMaximum = Math.Max(prosperityMinimum, settings.MaxPoolProsperityMaxScale / 100f);
                prosperityScale = prosperityMinimum + ((prosperityMaximum - prosperityMinimum) * prosperityNormalized);

                float securityNormalized = Clamp01(facts.Security / 100f);
                float securityMinimum = Math.Max(0.01f, settings.SecurityBonusMinScale / 100f);
                float securityMaximum = Math.Max(securityMinimum, settings.SecurityBonusMaxScale / 100f);
                securityBonus = securityMinimum + ((securityMaximum - securityMinimum) * securityNormalized);
            }

            int hearthBonus = 0;
            if (facts.HasTown)
            {
                float multiplier = Math.Max(0f, settings.MaxPoolHearthMultiplier);
                foreach (float hearth in facts.VillageHearths)
                {
                    hearthBonus += (int)(hearth * multiplier);
                }
            }

            int value = (int)(baseMax * prosperityScale * securityBonus) + hearthBonus;

            if (settings.EnableGovernorBonus)
            {
                float governorDivisor = Math.Max(1f, settings.GovernorLeadershipPoolDivisor);
                float leadershipBonus = Math.Min(1.0f, facts.GovernorLeadership / governorDivisor);
                value += (int)(value * leadershipBonus);
            }

            value = Math.Max(1, value);

            if (settings.UseTinyPoolsForTesting)
            {
                int divisor = Math.Max(1, settings.TinyPoolDivisor);
                int minimumScaledPool = Math.Max(1, settings.TinyPoolMinimum);
                value = Math.Max(minimumScaledPool, value / divisor);
            }

            return value;
        }

        internal static DailyRegenResult DailyRegen(
            PoolFacts facts,
            int max,
            IB1071Settings settings,
            IB1071Random random)
        {
            float prosperityNormalizer = Math.Max(1f, settings.ProsperityNormalizer);
            float basePercent;

            if (facts.IsTown)
            {
                float prosperityNormalized = Clamp01(facts.Prosperity / prosperityNormalizer);
                float minimumPercent = Math.Max(0f, settings.TownRegenMinPercent) / 100f;
                float maximumPercent = Math.Max(minimumPercent, settings.TownRegenMaxPercent / 100f);
                basePercent = minimumPercent + ((maximumPercent - minimumPercent) * prosperityNormalized);
            }
            else if (facts.IsCastle)
            {
                float prosperityNormalized = Clamp01(facts.Prosperity / prosperityNormalizer);
                float minimumPercent = Math.Max(0f, settings.CastleRegenMinPercent) / 100f;
                float maximumPercent = Math.Max(minimumPercent, settings.CastleRegenMaxPercent / 100f);
                basePercent = minimumPercent + ((maximumPercent - minimumPercent) * prosperityNormalized);
            }
            else
            {
                basePercent = Math.Max(0f, settings.OtherRegenPercent) / 100f;
            }

            float percent = basePercent;
            float hearthSum = 0f;
            foreach (float hearth in facts.VillageHearths)
            {
                hearthSum += hearth;
            }

            float hearthNormalized = Clamp01(hearthSum / Math.Max(1f, settings.HearthNormalizer));
            percent += (Math.Max(0f, settings.HearthBonusMaxPercent) / 100f) * hearthNormalized;

            float securityMultiplier = 1f;
            float foodMultiplier = 1f;
            float loyaltyMultiplier = 1f;
            float siegeMultiplier = 1f;
            float seasonalMultiplier = 1f;
            float peaceMultiplier = 1f;
            float governorAdd = 0f;
            float exhaustionMultiplier = 1f;
            float recoveryMultiplier = 1f;
            float softCapMultiplier = 1f;

            if (facts.HasTown)
            {
                float securityNormalized = Clamp01(facts.Security / 100f);
                float securityMinimum = Math.Max(0f, settings.SecurityRegenMinScale) / 100f;
                float securityMaximum = Math.Max(securityMinimum, settings.SecurityRegenMaxScale / 100f);
                securityMultiplier = securityMinimum + ((securityMaximum - securityMinimum) * securityNormalized);
                percent *= securityMultiplier;

                float foodNormalized = Clamp01(facts.FoodStocks / Math.Max(1f, settings.FoodStocksNormalizer));
                float foodMinimum = Math.Max(0f, settings.FoodRegenMinScale) / 100f;
                float foodMaximum = Math.Max(foodMinimum, settings.FoodRegenMaxScale / 100f);
                foodMultiplier = foodMinimum + ((foodMaximum - foodMinimum) * foodNormalized);
                percent *= foodMultiplier;

                float loyaltyNormalized = Clamp01(facts.Loyalty / 100f);
                float loyaltyMinimum = Math.Max(0f, settings.LoyaltyRegenMinScale) / 100f;
                float loyaltyMaximum = Math.Max(loyaltyMinimum, settings.LoyaltyRegenMaxScale / 100f);
                loyaltyMultiplier = loyaltyMinimum + ((loyaltyMaximum - loyaltyMinimum) * loyaltyNormalized);
                percent *= loyaltyMultiplier;
            }

            if (facts.IsUnderSiege)
            {
                siegeMultiplier = Math.Max(0f, settings.SiegeRegenMultiplierPercent) / 100f;
                percent *= siegeMultiplier;
            }

            if (settings.EnableSeasonalRegen)
            {
                if (facts.Season == B1071Season.Spring || facts.Season == B1071Season.Summer)
                {
                    seasonalMultiplier = Math.Max(0f, settings.SpringSummerRegenMultiplier) / 100f;
                    percent *= seasonalMultiplier;
                }
                else if (facts.Season == B1071Season.Winter)
                {
                    seasonalMultiplier = Math.Max(0f, settings.WinterRegenMultiplier) / 100f;
                    percent *= seasonalMultiplier;
                }
            }

            if (settings.EnablePeaceDividend && facts.OwnerAtPeace)
            {
                peaceMultiplier = Math.Max(1f, settings.PeaceDividendMultiplier) / 100f;
                percent *= peaceMultiplier;
            }

            if (settings.EnableGovernorBonus)
            {
                governorAdd = facts.GovernorSteward / Math.Max(1f, settings.GovernorStewardRegenDivisor);
                percent += governorAdd;
            }

            if (settings.EnableWarExhaustion && facts.Exhaustion > 0f)
            {
                exhaustionMultiplier = 1f - (facts.Exhaustion / Math.Max(1f, settings.ExhaustionRegenDivisor));
                if (exhaustionMultiplier < 0.1f)
                {
                    exhaustionMultiplier = 0.1f;
                }

                percent *= exhaustionMultiplier;
            }

            if (settings.EnableDelayedRecovery && facts.RecoveryPenalty > 0f)
            {
                recoveryMultiplier = Math.Max(0.1f, 1f - facts.RecoveryPenalty);
                percent *= recoveryMultiplier;
            }

            if (settings.EnableRegenSoftCap && max > 0)
            {
                float startRatio = Clamp01(settings.RegenSoftCapStartRatio);
                float strength = Math.Max(0f, settings.RegenSoftCapStrength);
                if (startRatio < 0.999f && strength > 0f)
                {
                    float fillRatio = Clamp01((float)facts.CurrentPool / max);
                    if (fillRatio > startRatio)
                    {
                        float interpolation = Clamp01((fillRatio - startRatio) / Math.Max(0.001f, 1f - startRatio));
                        float slowdown = 1f - (strength * interpolation * interpolation);
                        softCapMultiplier = Math.Max(0.1f, slowdown);
                        percent *= softCapMultiplier;
                    }
                }
            }

            float varianceMultiplier = 1f;
            if (settings.EnableRecruitmentVariance && settings.RecoveryVariancePercent > 0)
            {
                float spread = Math.Min(settings.RecoveryVariancePercent, 100f) / 100f;
                varianceMultiplier = random.RangeFloat(1f - spread, 1f + spread);
                percent *= varianceMultiplier;
            }

            float stressFloor = Math.Max(0f, settings.RegenStressFloorPercent) / 100f;
            if (percent < stressFloor)
            {
                percent = stressFloor;
            }

            int regen = (int)(max * percent);
            int cap = (int)(max * Math.Max(0.001f, settings.RegenCapPercent) / 100f);
            if (regen > cap)
            {
                regen = cap;
            }

            int minimumDailyRegen = facts.IsCastle
                ? Math.Max(0, settings.CastleMinimumDailyRegen)
                : Math.Max(0, settings.MinimumDailyRegen);
            int result = Math.Min(cap, Math.Max(minimumDailyRegen, regen));

            int depletedBonus = 0;
            if (settings.EnableDepletedEmergencyRegen && max > 0)
            {
                float depletedThreshold = Clamp01(Math.Max(0f, settings.DepletedRegenThresholdPercent) / 100f);
                int maximumBonus = Math.Max(0, settings.DepletedRegenBonusAtZero);
                if (depletedThreshold > 0f && maximumBonus > 0)
                {
                    float fillRatio = Clamp01((float)facts.CurrentPool / max);
                    if (fillRatio < depletedThreshold)
                    {
                        float interpolation = 1f - (fillRatio / depletedThreshold);
                        depletedBonus = Math.Max(0, (int)(maximumBonus * interpolation));
                        result += depletedBonus;
                    }
                }
            }

            return new DailyRegenResult(
                result,
                basePercent,
                percent,
                securityMultiplier,
                foodMultiplier,
                loyaltyMultiplier,
                siegeMultiplier,
                seasonalMultiplier,
                peaceMultiplier,
                governorAdd,
                exhaustionMultiplier,
                recoveryMultiplier,
                softCapMultiplier,
                varianceMultiplier,
                depletedBonus);
        }

        internal static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        internal static int PoolBand(int current, int max)
        {
            if (max <= 0) return 0;

            float ratio = (float)current / max;
            if (current <= 0) return 0;
            if (ratio < 0.25f) return 1;
            if (ratio < 0.50f) return 2;
            if (ratio < 0.75f) return 3;
            return 4;
        }
    }
}
