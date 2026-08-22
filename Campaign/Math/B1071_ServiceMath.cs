using System;
using System.Collections.Generic;
using Byzantium1071.Campaign.Settings;

namespace Byzantium1071.Campaign
{
    internal readonly struct PendingRecallBalance
    {
        internal PendingRecallBalance(int goldPaid, int manpowerDrawn, int playerOwnedCount)
        {
            GoldPaid = goldPaid;
            ManpowerDrawn = manpowerDrawn;
            PlayerOwnedCount = playerOwnedCount;
        }

        internal int GoldPaid { get; }
        internal int ManpowerDrawn { get; }
        internal int PlayerOwnedCount { get; }
    }

    internal readonly struct ServiceCohortSaveRow
    {
        internal ServiceCohortSaveRow(
            string partyId,
            string troopId,
            int joinDay,
            int count,
            int extensionCount,
            string homeId)
        {
            PartyId = partyId;
            TroopId = troopId;
            JoinDay = joinDay;
            Count = count;
            ExtensionCount = extensionCount;
            HomeId = homeId;
        }

        internal string PartyId { get; }
        internal string TroopId { get; }
        internal int JoinDay { get; }
        internal int Count { get; }
        internal int ExtensionCount { get; }
        internal string HomeId { get; }
    }

    internal readonly struct TransferReserveSaveRow
    {
        internal TransferReserveSaveRow(string troopId, int joinDay, int storedDay, int count, int extensionCount, string homeId)
        {
            TroopId = troopId;
            JoinDay = joinDay;
            StoredDay = storedDay;
            Count = count;
            ExtensionCount = extensionCount;
            HomeId = homeId;
        }

        internal string TroopId { get; }
        internal int JoinDay { get; }
        internal int StoredDay { get; }
        internal int Count { get; }
        internal int ExtensionCount { get; }
        internal string HomeId { get; }
    }

    internal readonly struct VeteranSaveRow
    {
        internal VeteranSaveRow(string settlementId, string troopId, int dischargeDay, int count, bool fromPlayer)
        {
            SettlementId = settlementId;
            TroopId = troopId;
            DischargeDay = dischargeDay;
            Count = count;
            FromPlayer = fromPlayer;
        }

        internal string SettlementId { get; }
        internal string TroopId { get; }
        internal int DischargeDay { get; }
        internal int Count { get; }
        internal bool FromPlayer { get; }
    }

    internal readonly struct PendingRecallSaveRow
    {
        internal PendingRecallSaveRow(
            int orderId,
            string settlementId,
            string troopId,
            int count,
            int orderDay,
            int goldPaid,
            int manpowerDrawn,
            int playerOwnedCount,
            float courierRemaining,
            float posX,
            float posY)
        {
            OrderId = orderId;
            SettlementId = settlementId;
            TroopId = troopId;
            Count = count;
            OrderDay = orderDay;
            GoldPaid = goldPaid;
            ManpowerDrawn = manpowerDrawn;
            PlayerOwnedCount = playerOwnedCount;
            CourierRemaining = courierRemaining;
            PosX = posX;
            PosY = posY;
        }

        internal int OrderId { get; }
        internal string SettlementId { get; }
        internal string TroopId { get; }
        internal int Count { get; }
        internal int OrderDay { get; }
        internal int GoldPaid { get; }
        internal int ManpowerDrawn { get; }
        internal int PlayerOwnedCount { get; }
        internal float CourierRemaining { get; }
        internal float PosX { get; }
        internal float PosY { get; }
    }

    internal static class B1071_ServiceMath
    {
        internal static int ServiceThresholdDays(
            int tier,
            B1071Season season,
            bool isInCrisis,
            IB1071Settings settings)
        {
            int baseDays = BaseServiceDays(tier, settings);
            int percent = 100;

            if (settings.EnableDemobilizationSeasonality)
            {
                if (season == B1071Season.Spring || season == B1071Season.Summer)
                {
                    percent = percent * Math.Max(25, settings.DemobilizationSpringSummerThresholdPercent) / 100;
                }
                else if (season == B1071Season.Winter)
                {
                    percent = percent * Math.Max(25, settings.DemobilizationWinterThresholdPercent) / 100;
                }
            }

            if (settings.EnableDemobilizationCrisisCompression && isInCrisis)
            {
                percent = percent * Math.Max(25, settings.DemobilizationCrisisThresholdPercent) / 100;
            }

            return Math.Max(1, baseDays * percent / 100);
        }

        internal static int BaseServiceDays(int tier, IB1071Settings settings)
        {
            switch (settings.DemobilizationIntensityPreset)
            {
                case 0:
                    return TierValue(tier, 63, 84, 126, 168, 252, 336);
                case 2:
                    return TierValue(tier, 28, 42, 56, 84, 112, 168);
                case 3:
                    return TierValue(
                        tier,
                        settings.DemobilizationT1ServiceDays,
                        settings.DemobilizationT2ServiceDays,
                        settings.DemobilizationT3ServiceDays,
                        settings.DemobilizationT4ServiceDays,
                        settings.DemobilizationT5ServiceDays,
                        settings.DemobilizationT6ServiceDays);
                default:
                    return TierValue(tier, 42, 63, 84, 126, 168, 252);
            }
        }

        internal static int MaxExtensions(IB1071Settings settings) =>
            Math.Max(1, settings.DemobilizationMaxExtensions);

        internal static int ExtensionCost(int tier, int count, int alreadyExtended, IB1071Settings settings)
        {
            int clampedTier = ClampTier(tier);
            int days = Math.Max(1, settings.DemobilizationExtensionDays);
            int costPerTierDay = Math.Max(0, settings.DemobilizationExtensionGoldPerTierDay);
            decimal baseCost = (decimal)count * clampedTier * days * costPerTierDay;
            decimal multiplier = 2m + Math.Max(0, alreadyExtended);
            decimal scaled = baseCost * multiplier / 2m;
            if (scaled <= 0m)
            {
                return 0;
            }

            return scaled >= int.MaxValue ? int.MaxValue : (int)scaled;
        }

        internal static int DailyRetirementCap(int overdueCount, int dailyCapPercent)
        {
            return Math.Max(1, overdueCount * Math.Max(1, dailyCapPercent) / 100);
        }

        internal static int RecallGoldCost(int tier, int count, IB1071Settings settings)
        {
            int clampedTier = ClampTier(tier);
            int perTier = Math.Max(0, settings.DemobilizationRecallGoldPerTier);
            return Math.Max(0, count * clampedTier * perTier);
        }

        internal static float CourierSpeedPerDay(IB1071Settings settings) =>
            Math.Max(1, settings.DemobilizationCourierSpeed);

        internal static float MarchSpeedPerDay(IB1071Settings settings) =>
            Math.Max(1, settings.DemobilizationMarchSpeed);

        internal static int EstimateRecallDays(float distance, IB1071Settings settings)
        {
            float days = distance / CourierSpeedPerDay(settings) + distance / MarchSpeedPerDay(settings);
            return Math.Max(1, (int)Math.Ceiling(days));
        }

        internal static int EstimateArrivalDays(float courierRemaining, float marchingDistance, IB1071Settings settings)
        {
            float days = courierRemaining > 0f ? courierRemaining / CourierSpeedPerDay(settings) : 0f;
            days += marchingDistance / MarchSpeedPerDay(settings);
            return Math.Max(0, (int)Math.Ceiling(days));
        }

        internal static int VeteranReturnCount(int count, int returnPercent, IB1071Random random)
        {
            int clampedPercent = ClampPercent(returnPercent);
            int arrived = 0;

            for (int index = 0; index < count; index++)
            {
                if (clampedPercent >= 100 || random.Next(100) < clampedPercent)
                {
                    arrived++;
                }
            }

            return arrived;
        }

        internal static int ScatterCount(int count, int scatterPercent, IB1071Random random)
        {
            int clampedPercent = ClampPercent(scatterPercent);
            if (count <= 0 || clampedPercent <= 0)
            {
                return 0;
            }

            int lost = count * clampedPercent / 100;
            if (lost <= 0 && random.Next(100) < clampedPercent)
            {
                lost = 1;
            }

            return Math.Min(lost, count);
        }

        internal static PendingRecallBalance ProrateAfterDeparture(
            int orderedCount,
            int departedCount,
            int remainingCount,
            int goldPaid,
            int manpowerDrawn,
            int playerOwnedCount)
        {
            if (orderedCount <= 0 || remainingCount <= 0)
            {
                return new PendingRecallBalance(0, 0, 0);
            }

            int remainingGold = goldPaid - (goldPaid * departedCount / orderedCount);
            int remainingManpower = manpowerDrawn - (manpowerDrawn * departedCount / orderedCount);
            int remainingPlayerOwned = Math.Min(
                remainingCount,
                playerOwnedCount - (playerOwnedCount * departedCount / orderedCount));

            return new PendingRecallBalance(remainingGold, remainingManpower, remainingPlayerOwned);
        }

        internal static void AppendServiceCohortRows(
            ICollection<string> partyIds,
            ICollection<string> troopIds,
            ICollection<int> joinDays,
            ICollection<int> counts,
            ICollection<bool> extendedFlags,
            ICollection<int> extensionCounts,
            ICollection<string> homeIds,
            string partyId,
            string troopId,
            int joinDay,
            int count,
            int extensionCount,
            string homeId)
        {
            int soldiers = Math.Max(0, count);
            for (int index = 0; index < soldiers; index++)
            {
                partyIds.Add(partyId);
                troopIds.Add(troopId);
                joinDays.Add(joinDay);
                counts.Add(1);
                extendedFlags.Add(extensionCount > 0);
                extensionCounts.Add(extensionCount);
                homeIds.Add(homeId ?? string.Empty);
            }
        }

        internal static List<ServiceCohortSaveRow> ReadServiceCohortRows(
            IReadOnlyList<string>? partyIds,
            IReadOnlyList<string>? troopIds,
            IReadOnlyList<int>? joinDays,
            IReadOnlyList<int>? counts,
            IReadOnlyList<bool>? extendedFlags,
            IReadOnlyList<int>? extensionCounts,
            IReadOnlyList<string>? homeIds)
        {
            partyIds ??= Array.Empty<string>();
            troopIds ??= Array.Empty<string>();
            joinDays ??= Array.Empty<int>();
            counts ??= Array.Empty<int>();
            extendedFlags ??= Array.Empty<bool>();
            extensionCounts ??= Array.Empty<int>();
            homeIds ??= Array.Empty<string>();

            int rowCount = Math.Min(partyIds.Count,
                Math.Min(troopIds.Count, Math.Min(joinDays.Count, counts.Count)));
            var rows = new List<ServiceCohortSaveRow>(rowCount);

            for (int index = 0; index < rowCount; index++)
            {
                string partyId = partyIds[index];
                string troopId = troopIds[index];
                int count = counts[index];
                int extensionCount = index < extensionCounts.Count
                    ? Math.Max(0, extensionCounts[index])
                    : (index < extendedFlags.Count && extendedFlags[index] ? 1 : 0);
                string homeId = index < homeIds.Count ? homeIds[index] ?? string.Empty : string.Empty;

                if (string.IsNullOrEmpty(partyId) || string.IsNullOrEmpty(troopId) || count <= 0)
                {
                    continue;
                }

                rows.Add(new ServiceCohortSaveRow(
                    partyId,
                    troopId,
                    joinDays[index],
                    count,
                    extensionCount,
                    homeId));
            }

            return rows;
        }

        internal static void AppendTransferReserveRows(
            ICollection<string> troopIds,
            ICollection<int> joinDays,
            ICollection<int> storedDays,
            ICollection<int> counts,
            ICollection<bool> extendedFlags,
            ICollection<int> extensionCounts,
            ICollection<string> homeIds,
            string troopId,
            int joinDay,
            int storedDay,
            int count,
            int extensionCount,
            string homeId)
        {
            int soldiers = Math.Max(0, count);
            for (int index = 0; index < soldiers; index++)
            {
                troopIds.Add(troopId);
                joinDays.Add(joinDay);
                storedDays.Add(storedDay);
                counts.Add(1);
                extendedFlags.Add(extensionCount > 0);
                extensionCounts.Add(extensionCount);
                homeIds.Add(homeId ?? string.Empty);
            }
        }

        internal static List<TransferReserveSaveRow> ReadTransferReserveRows(
            IReadOnlyList<string>? troopIds,
            IReadOnlyList<int>? joinDays,
            IReadOnlyList<int>? storedDays,
            IReadOnlyList<int>? counts,
            IReadOnlyList<bool>? extendedFlags,
            IReadOnlyList<int>? extensionCounts,
            IReadOnlyList<string>? homeIds)
        {
            troopIds ??= Array.Empty<string>();
            joinDays ??= Array.Empty<int>();
            storedDays ??= Array.Empty<int>();
            counts ??= Array.Empty<int>();
            extendedFlags ??= Array.Empty<bool>();
            extensionCounts ??= Array.Empty<int>();
            homeIds ??= Array.Empty<string>();

            int rowCount = Math.Min(troopIds.Count,
                Math.Min(joinDays.Count, Math.Min(storedDays.Count, counts.Count)));
            var rows = new List<TransferReserveSaveRow>(rowCount);
            for (int index = 0; index < rowCount; index++)
            {
                string troopId = troopIds[index];
                int count = counts[index];
                int extensionCount = index < extensionCounts.Count
                    ? Math.Max(0, extensionCounts[index])
                    : (index < extendedFlags.Count && extendedFlags[index] ? 1 : 0);
                string homeId = index < homeIds.Count ? homeIds[index] ?? string.Empty : string.Empty;
                if (string.IsNullOrEmpty(troopId) || count <= 0)
                {
                    continue;
                }

                rows.Add(new TransferReserveSaveRow(
                    troopId,
                    joinDays[index],
                    storedDays[index],
                    count,
                    extensionCount,
                    homeId));
            }

            return rows;
        }

        internal static void AppendVeteranRow(
            ICollection<string> settlementIds,
            ICollection<string> troopIds,
            ICollection<int> dischargeDays,
            ICollection<int> counts,
            ICollection<bool> fromPlayer,
            string settlementId,
            string troopId,
            int dischargeDay,
            int count,
            bool wasFromPlayer)
        {
            if (count <= 0)
            {
                return;
            }

            settlementIds.Add(settlementId);
            troopIds.Add(troopId);
            dischargeDays.Add(dischargeDay);
            counts.Add(count);
            fromPlayer.Add(wasFromPlayer);
        }

        internal static List<VeteranSaveRow> ReadVeteranRows(
            IReadOnlyList<string>? settlementIds,
            IReadOnlyList<string>? troopIds,
            IReadOnlyList<int>? dischargeDays,
            IReadOnlyList<int>? counts,
            IReadOnlyList<bool>? fromPlayer)
        {
            settlementIds ??= Array.Empty<string>();
            troopIds ??= Array.Empty<string>();
            dischargeDays ??= Array.Empty<int>();
            counts ??= Array.Empty<int>();
            fromPlayer ??= Array.Empty<bool>();

            int rowCount = Math.Min(settlementIds.Count,
                Math.Min(troopIds.Count, Math.Min(dischargeDays.Count, counts.Count)));
            var rows = new List<VeteranSaveRow>(rowCount);
            for (int index = 0; index < rowCount; index++)
            {
                string settlementId = settlementIds[index];
                string troopId = troopIds[index];
                int count = counts[index];
                if (string.IsNullOrEmpty(settlementId) || string.IsNullOrEmpty(troopId) || count <= 0)
                {
                    continue;
                }

                rows.Add(new VeteranSaveRow(
                    settlementId,
                    troopId,
                    dischargeDays[index],
                    count,
                    index < fromPlayer.Count && fromPlayer[index]));
            }

            return rows;
        }

        internal static void AppendPendingRecallRow(
            ICollection<int> orderIds,
            ICollection<string> settlementIds,
            ICollection<string> troopIds,
            ICollection<int> counts,
            ICollection<int> orderDays,
            ICollection<int> goldPaid,
            ICollection<int> manpowerDrawn,
            ICollection<int> playerOwnedCounts,
            ICollection<float> courierRemaining,
            ICollection<float> posX,
            ICollection<float> posY,
            int orderId,
            string settlementId,
            string troopId,
            int count,
            int orderDay,
            int paidGold,
            int drawnManpower,
            int playerOwnedCount,
            float courierDistance,
            float positionX,
            float positionY)
        {
            if (count <= 0)
            {
                return;
            }

            orderIds.Add(orderId);
            settlementIds.Add(settlementId);
            troopIds.Add(troopId);
            counts.Add(count);
            orderDays.Add(orderDay);
            goldPaid.Add(paidGold);
            manpowerDrawn.Add(drawnManpower);
            playerOwnedCounts.Add(playerOwnedCount);
            courierRemaining.Add(courierDistance);
            posX.Add(positionX);
            posY.Add(positionY);
        }

        internal static List<PendingRecallSaveRow> ReadPendingRecallRows(
            IReadOnlyList<int>? orderIds,
            IReadOnlyList<string>? settlementIds,
            IReadOnlyList<string>? troopIds,
            IReadOnlyList<int>? counts,
            IReadOnlyList<int>? orderDays,
            IReadOnlyList<int>? goldPaid,
            IReadOnlyList<int>? manpowerDrawn,
            IReadOnlyList<int>? playerOwnedCounts,
            IReadOnlyList<float>? courierRemaining,
            IReadOnlyList<float>? posX,
            IReadOnlyList<float>? posY,
            int fallbackOrderDay)
        {
            orderIds ??= Array.Empty<int>();
            settlementIds ??= Array.Empty<string>();
            troopIds ??= Array.Empty<string>();
            counts ??= Array.Empty<int>();
            orderDays ??= Array.Empty<int>();
            goldPaid ??= Array.Empty<int>();
            manpowerDrawn ??= Array.Empty<int>();
            playerOwnedCounts ??= Array.Empty<int>();
            courierRemaining ??= Array.Empty<float>();
            posX ??= Array.Empty<float>();
            posY ??= Array.Empty<float>();

            int rowCount = Math.Min(settlementIds.Count, Math.Min(troopIds.Count, counts.Count));
            var rows = new List<PendingRecallSaveRow>(rowCount);
            for (int index = 0; index < rowCount; index++)
            {
                string settlementId = settlementIds[index];
                string troopId = troopIds[index];
                int count = counts[index];
                if (string.IsNullOrEmpty(settlementId) || string.IsNullOrEmpty(troopId) || count <= 0)
                {
                    continue;
                }

                bool hasPosition = index < posX.Count && index < posY.Count;
                rows.Add(new PendingRecallSaveRow(
                    index < orderIds.Count ? orderIds[index] : 0,
                    settlementId,
                    troopId,
                    count,
                    index < orderDays.Count ? orderDays[index] : fallbackOrderDay,
                    index < goldPaid.Count ? goldPaid[index] : 0,
                    index < manpowerDrawn.Count ? manpowerDrawn[index] : 0,
                    index < playerOwnedCounts.Count ? Math.Max(0, Math.Min(count, playerOwnedCounts[index])) : 0,
                    index < courierRemaining.Count ? courierRemaining[index] : 0f,
                    hasPosition ? posX[index] : float.NaN,
                    hasPosition ? posY[index] : float.NaN));
            }

            return rows;
        }

        private static int TierValue(int tier, int tierOne, int tierTwo, int tierThree, int tierFour, int tierFive, int tierSix)
        {
            switch (ClampTier(tier))
            {
                case 1: return tierOne;
                case 2: return tierTwo;
                case 3: return tierThree;
                case 4: return tierFour;
                case 5: return tierFive;
                default: return tierSix;
            }
        }

        private static int ClampTier(int tier) => Math.Max(1, Math.Min(6, tier));

        private static int ClampPercent(int value) => Math.Max(0, Math.Min(100, value));
    }
}
