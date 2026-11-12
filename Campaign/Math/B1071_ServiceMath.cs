using System;
using System.Collections.Generic;
using Byzantium1071.Campaign.Settings;

namespace Byzantium1071.Campaign
{
    internal readonly struct PendingRecallBalance
    {
        internal PendingRecallBalance(int goldPaid, int manpowerDrawn)
        {
            GoldPaid = goldPaid;
            ManpowerDrawn = manpowerDrawn;
        }

        internal int GoldPaid { get; }
        internal int ManpowerDrawn { get; }
    }

    internal readonly struct ServiceCohortSaveRow
    {
        internal ServiceCohortSaveRow(
            string partyId,
            string troopId,
            int joinDay,
            int count,
            int extensionCount,
            string homeId,
            string originClanId,
            string employerClanId)
        {
            PartyId = partyId;
            TroopId = troopId;
            JoinDay = joinDay;
            Count = count;
            ExtensionCount = extensionCount;
            HomeId = homeId;
            OriginClanId = originClanId;
            EmployerClanId = employerClanId;
        }

        internal string PartyId { get; }
        internal string TroopId { get; }
        internal int JoinDay { get; }
        internal int Count { get; }
        internal int ExtensionCount { get; }
        internal string HomeId { get; }
        internal string OriginClanId { get; }
        internal string EmployerClanId { get; }
    }

    internal readonly struct TransferReserveSaveRow
    {
        internal TransferReserveSaveRow(
            string troopId,
            int joinDay,
            int storedDay,
            int count,
            int extensionCount,
            string homeId,
            string originClanId,
            string employerClanId,
            string sourcePartyId)
        {
            TroopId = troopId;
            JoinDay = joinDay;
            StoredDay = storedDay;
            Count = count;
            ExtensionCount = extensionCount;
            HomeId = homeId;
            OriginClanId = originClanId;
            EmployerClanId = employerClanId;
            SourcePartyId = sourcePartyId;
        }

        internal string TroopId { get; }
        internal int JoinDay { get; }
        internal int StoredDay { get; }
        internal int Count { get; }
        internal int ExtensionCount { get; }
        internal string HomeId { get; }
        internal string OriginClanId { get; }
        internal string EmployerClanId { get; }
        internal string SourcePartyId { get; }
    }

    internal readonly struct VeteranSaveRow
    {
        internal VeteranSaveRow(
            string settlementId,
            string troopId,
            int dischargeDay,
            int count,
            bool fromPlayer,
            string originClanId,
            string employerClanId)
        {
            SettlementId = settlementId;
            TroopId = troopId;
            DischargeDay = dischargeDay;
            Count = count;
            FromPlayer = fromPlayer;
            OriginClanId = originClanId;
            EmployerClanId = employerClanId;
        }

        internal string SettlementId { get; }
        internal string TroopId { get; }
        internal int DischargeDay { get; }
        internal int Count { get; }
        internal bool FromPlayer { get; }
        internal string OriginClanId { get; }
        internal string EmployerClanId { get; }
    }

    internal readonly struct PendingRecallSaveRow
    {
        internal PendingRecallSaveRow(
            int sourceIndex,
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
            SourceIndex = sourceIndex;
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

        /// <summary>Index in the raw parallel save lists that supplied this accepted header.</summary>
        internal int SourceIndex { get; }
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

    internal readonly struct RecallBatchSaveRow
    {
        internal RecallBatchSaveRow(string originClanId, string employerClanId, int count)
        {
            OriginClanId = originClanId;
            EmployerClanId = employerClanId;
            Count = count;
        }

        internal string OriginClanId { get; }
        internal string EmployerClanId { get; }
        internal int Count { get; }
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

        /// <summary>
        /// How many soldiers of one troop type may leave in a single day. This is an upper bound on
        /// a list of genuinely overdue men, not a count of them: the floor of one stops a small
        /// group from rounding down to zero and never rotating out. Callers are expected to have
        /// found at least one overdue soldier before asking.
        /// </summary>
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
            int manpowerDrawn)
        {
            if (orderedCount <= 0 || remainingCount <= 0)
            {
                return new PendingRecallBalance(0, 0);
            }

            int remainingGold = goldPaid - (goldPaid * departedCount / orderedCount);
            int remainingManpower = manpowerDrawn - (manpowerDrawn * departedCount / orderedCount);
            return new PendingRecallBalance(remainingGold, remainingManpower);
        }

        /// <summary>
        /// Splits <paramref name="total"/> across positive weights by largest remainder. Equal
        /// remainders keep the source order, so an ordered list deterministically favours its
        /// oldest batch.
        /// </summary>
        internal static List<int> AllocateLargestRemainder(int total, IReadOnlyList<int> weights)
        {
            var allocation = new List<int>(weights?.Count ?? 0);
            if (weights == null || weights.Count == 0) return allocation;

            long weightTotal = 0;
            for (int index = 0; index < weights.Count; index++)
            {
                int weight = Math.Max(0, weights[index]);
                allocation.Add(0);
                weightTotal += weight;
            }

            int requested = Math.Max(0, Math.Min(total, weightTotal > int.MaxValue ? int.MaxValue : (int)weightTotal));
            if (requested == 0 || weightTotal <= 0) return allocation;

            var remainders = new List<long>(weights.Count);
            int remaining = requested;
            for (int index = 0; index < weights.Count; index++)
            {
                long numerator = (long)Math.Max(0, weights[index]) * requested;
                int floor = (int)(numerator / weightTotal);
                allocation[index] = floor;
                remainders.Add(numerator % weightTotal);
                remaining -= floor;
            }

            while (remaining > 0)
            {
                int selected = -1;
                for (int index = 0; index < weights.Count; index++)
                {
                    if (allocation[index] >= Math.Max(0, weights[index])) continue;
                    if (selected < 0 || remainders[index] > remainders[selected])
                        selected = index;
                }

                if (selected < 0) break;
                allocation[selected]++;
                remainders[selected] = -1;
                remaining--;
            }

            return allocation;
        }

        internal static void AppendServiceCohortRows(
            ICollection<string> partyIds,
            ICollection<string> troopIds,
            ICollection<int> joinDays,
            ICollection<int> counts,
            ICollection<bool> extendedFlags,
            ICollection<int> extensionCounts,
            ICollection<string> homeIds,
            ICollection<string> originClanIds,
            ICollection<string> employerClanIds,
            string partyId,
            string troopId,
            int joinDay,
            int count,
            int extensionCount,
            string homeId,
            string originClanId,
            string employerClanId)
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
                originClanIds.Add(originClanId ?? string.Empty);
                employerClanIds.Add(employerClanId ?? string.Empty);
            }
        }

        internal static List<ServiceCohortSaveRow> ReadServiceCohortRows(
            IReadOnlyList<string>? partyIds,
            IReadOnlyList<string>? troopIds,
            IReadOnlyList<int>? joinDays,
            IReadOnlyList<int>? counts,
            IReadOnlyList<bool>? extendedFlags,
            IReadOnlyList<int>? extensionCounts,
            IReadOnlyList<string>? homeIds,
            IReadOnlyList<string>? originClanIds = null,
            IReadOnlyList<string>? employerClanIds = null)
        {
            partyIds ??= Array.Empty<string>();
            troopIds ??= Array.Empty<string>();
            joinDays ??= Array.Empty<int>();
            counts ??= Array.Empty<int>();
            extendedFlags ??= Array.Empty<bool>();
            extensionCounts ??= Array.Empty<int>();
            homeIds ??= Array.Empty<string>();
            originClanIds ??= Array.Empty<string>();
            employerClanIds ??= Array.Empty<string>();

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
                string originClanId = index < originClanIds.Count ? originClanIds[index] ?? string.Empty : string.Empty;
                string employerClanId = index < employerClanIds.Count ? employerClanIds[index] ?? string.Empty : string.Empty;

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
                    homeId,
                    originClanId,
                    employerClanId));
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
            ICollection<string> originClanIds,
            ICollection<string> employerClanIds,
            ICollection<string> sourcePartyIds,
            string troopId,
            int joinDay,
            int storedDay,
            int count,
            int extensionCount,
            string homeId,
            string originClanId,
            string employerClanId,
            string sourcePartyId)
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
                originClanIds.Add(originClanId ?? string.Empty);
                employerClanIds.Add(employerClanId ?? string.Empty);
                sourcePartyIds.Add(sourcePartyId ?? string.Empty);
            }
        }

        internal static List<TransferReserveSaveRow> ReadTransferReserveRows(
            IReadOnlyList<string>? troopIds,
            IReadOnlyList<int>? joinDays,
            IReadOnlyList<int>? storedDays,
            IReadOnlyList<int>? counts,
            IReadOnlyList<bool>? extendedFlags,
            IReadOnlyList<int>? extensionCounts,
            IReadOnlyList<string>? homeIds,
            IReadOnlyList<string>? originClanIds = null,
            IReadOnlyList<string>? employerClanIds = null,
            IReadOnlyList<string>? sourcePartyIds = null)
        {
            troopIds ??= Array.Empty<string>();
            joinDays ??= Array.Empty<int>();
            storedDays ??= Array.Empty<int>();
            counts ??= Array.Empty<int>();
            extendedFlags ??= Array.Empty<bool>();
            extensionCounts ??= Array.Empty<int>();
            homeIds ??= Array.Empty<string>();
            originClanIds ??= Array.Empty<string>();
            employerClanIds ??= Array.Empty<string>();
            sourcePartyIds ??= Array.Empty<string>();

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
                string originClanId = index < originClanIds.Count ? originClanIds[index] ?? string.Empty : string.Empty;
                string employerClanId = index < employerClanIds.Count ? employerClanIds[index] ?? string.Empty : string.Empty;
                string sourcePartyId = index < sourcePartyIds.Count ? sourcePartyIds[index] ?? string.Empty : string.Empty;
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
                    homeId,
                    originClanId,
                    employerClanId,
                    sourcePartyId));
            }

            return rows;
        }

        internal static void AppendVeteranRow(
            ICollection<string> settlementIds,
            ICollection<string> troopIds,
            ICollection<int> dischargeDays,
            ICollection<int> counts,
            ICollection<bool> fromPlayer,
            ICollection<string> originClanIds,
            ICollection<string> employerClanIds,
            string settlementId,
            string troopId,
            int dischargeDay,
            int count,
            bool wasFromPlayer,
            string originClanId,
            string employerClanId)
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
            originClanIds.Add(originClanId ?? string.Empty);
            employerClanIds.Add(employerClanId ?? string.Empty);
        }

        internal static List<VeteranSaveRow> ReadVeteranRows(
            IReadOnlyList<string>? settlementIds,
            IReadOnlyList<string>? troopIds,
            IReadOnlyList<int>? dischargeDays,
            IReadOnlyList<int>? counts,
            IReadOnlyList<bool>? fromPlayer,
            IReadOnlyList<string>? originClanIds = null,
            IReadOnlyList<string>? employerClanIds = null)
        {
            settlementIds ??= Array.Empty<string>();
            troopIds ??= Array.Empty<string>();
            dischargeDays ??= Array.Empty<int>();
            counts ??= Array.Empty<int>();
            fromPlayer ??= Array.Empty<bool>();
            originClanIds ??= Array.Empty<string>();
            employerClanIds ??= Array.Empty<string>();

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
                    index < fromPlayer.Count && fromPlayer[index],
                    index < originClanIds.Count ? originClanIds[index] ?? string.Empty : string.Empty,
                    index < employerClanIds.Count ? employerClanIds[index] ?? string.Empty : string.Empty));
            }

            return rows;
        }

        internal static bool AppendRecallBatchRow(
            ICollection<string> originClanIds,
            ICollection<string> employerClanIds,
            ICollection<int> counts,
            string originClanId,
            string employerClanId,
            int count)
        {
            if (count <= 0) return false;
            originClanIds.Add(originClanId ?? string.Empty);
            employerClanIds.Add(employerClanId ?? string.Empty);
            counts.Add(count);
            return true;
        }

        /// <summary>
        /// Reads flattened recall batches by header order instead of their mutable order ID.
        /// A truncated group consumes its declared slots, leaving later headers empty so a bad
        /// row can never leak provenance into a different recall order.
        /// </summary>
        internal static List<List<RecallBatchSaveRow>> ReadRecallBatchGroups(
            int headerCount,
            IReadOnlyList<int>? batchesPerOrder,
            IReadOnlyList<string>? originClanIds,
            IReadOnlyList<string>? employerClanIds,
            IReadOnlyList<int>? counts)
        {
            batchesPerOrder ??= Array.Empty<int>();
            originClanIds ??= Array.Empty<string>();
            employerClanIds ??= Array.Empty<string>();
            counts ??= Array.Empty<int>();

            int safeHeaderCount = Math.Max(0, headerCount);
            var groups = new List<List<RecallBatchSaveRow>>(safeHeaderCount);
            int cursor = 0;
            int available = Math.Min(originClanIds.Count, Math.Min(employerClanIds.Count, counts.Count));

            for (int header = 0; header < safeHeaderCount; header++)
            {
                int declared = header < batchesPerOrder.Count ? Math.Max(0, batchesPerOrder[header]) : 0;
                var group = new List<RecallBatchSaveRow>(Math.Min(declared, Math.Max(0, available - cursor)));
                for (int index = 0; index < declared && cursor + index < available; index++)
                {
                    int count = counts[cursor + index];
                    if (count <= 0) continue;
                    group.Add(new RecallBatchSaveRow(
                        originClanIds[cursor + index] ?? string.Empty,
                        employerClanIds[cursor + index] ?? string.Empty,
                        count));
                }

                groups.Add(group);
                cursor = declared > int.MaxValue - cursor ? int.MaxValue : cursor + declared;
            }

            return groups;
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

        /// <summary>
        /// Number of raw recall headers that have all three fields required to identify an
        /// order. This is deliberately shared with <see cref="ReadPendingRecallRows"/> so
        /// flattened batch groups stay indexed to the same unfiltered header span.
        /// </summary>
        internal static int GetPendingRecallHeaderCount(
            IReadOnlyList<string>? settlementIds,
            IReadOnlyList<string>? troopIds,
            IReadOnlyList<int>? counts)
        {
            return Math.Min(
                settlementIds?.Count ?? 0,
                Math.Min(troopIds?.Count ?? 0, counts?.Count ?? 0));
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

            int rowCount = GetPendingRecallHeaderCount(settlementIds, troopIds, counts);
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
                    index,
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

        /// <summary>
        /// Clamps a value into an inclusive range. An inverted range (min above max) does not
        /// throw: values below <paramref name="min"/> return <paramref name="min"/> and everything
        /// else returns <paramref name="max"/>.
        /// </summary>
        internal static int ClampInt(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static int ClampTier(int tier) => Math.Max(1, Math.Min(6, tier));

        private static int ClampPercent(int value) => Math.Max(0, Math.Min(100, value));
    }
}
