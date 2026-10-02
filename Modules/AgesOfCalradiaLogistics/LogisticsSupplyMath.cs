using System;

namespace AgesOfCalradiaLogistics
{
    internal enum LogisticsSupplyCondition
    {
        Empty,
        Critical,
        Strained,
        Supported
    }

    /// <summary>
    /// Bannerlord-free operational-supply rules. Keeping these calculations
    /// pure makes balance changes deterministic and independently verifiable.
    /// Native food remains the authority for hunger and starvation.
    /// </summary>
    internal static class LogisticsSupplyMath
    {
        internal const float MinimumDailyUse = 0.5f;
        internal const float MaximumDailyUse = 6f;
        internal const int TroopsPerDailySupplyPoint = 40;
        internal const int MaximumCatchUpDays = 30;
        internal const int MaximumReserve = 100;
        internal const int ReservePerSupplyCrate = 20;

        internal static float CalculateDailyUse(int healthyTroops)
        {
            if (healthyTroops <= 0)
            {
                return 0f;
            }

            float demand = healthyTroops / (float)TroopsPerDailySupplyPoint;
            return Math.Max(MinimumDailyUse, Math.Min(MaximumDailyUse, demand));
        }

        internal static void AdvanceReserve(
            int reserve,
            float fractionalDebt,
            float dailyUse,
            int elapsedDays,
            out int remainingReserve,
            out float remainingDebt)
        {
            remainingReserve = ClampReserve(reserve);
            remainingDebt = ClampDebt(fractionalDebt);
            if (elapsedDays <= 0 || dailyUse <= 0f || remainingReserve <= 0)
            {
                return;
            }

            int boundedDays = Math.Min(MaximumCatchUpDays, elapsedDays);
            float totalDemand = remainingDebt + dailyUse * boundedDays;
            int pointsToConsume = Math.Min(remainingReserve, (int)Math.Floor(totalDemand));
            remainingReserve -= pointsToConsume;
            remainingDebt = remainingReserve == 0
                ? 0f
                : ClampDebt(totalDemand - pointsToConsume);
        }

        internal static int CalculateElapsedDays(int lastProcessedDay, int currentDay)
        {
            if (lastProcessedDay < 0 || currentDay <= lastProcessedDay)
            {
                return 0;
            }

            return Math.Min(MaximumCatchUpDays, currentDay - lastProcessedDay);
        }

        internal static float CalculateDaysRemaining(int reserve, float dailyUse)
        {
            return dailyUse <= 0f ? float.PositiveInfinity : ClampReserve(reserve) / dailyUse;
        }

        internal static LogisticsSupplyCondition GetCondition(int reserve, float dailyUse)
        {
            int clamped = ClampReserve(reserve);
            if (clamped == 0) return LogisticsSupplyCondition.Empty;
            if (dailyUse <= 0f) return LogisticsSupplyCondition.Supported;

            float daysRemaining = CalculateDaysRemaining(clamped, dailyUse);
            if (daysRemaining < 3f) return LogisticsSupplyCondition.Critical;
            if (daysRemaining < 7f) return LogisticsSupplyCondition.Strained;
            return LogisticsSupplyCondition.Supported;
        }

        internal static float GetSpeedFactor(int reserve, float dailyUse)
        {
            switch (GetCondition(reserve, dailyUse))
            {
                case LogisticsSupplyCondition.Empty: return 0.80f;
                case LogisticsSupplyCondition.Critical: return 0.88f;
                case LogisticsSupplyCondition.Strained: return 0.95f;
                default: return 1f;
            }
        }

        internal static int CalculateAiPurchaseCrates(
            int reserve,
            int targetReserve,
            int townStock,
            int availableGold,
            int goldFloor,
            int cratePrice,
            int reservePerCrate)
        {
            if (reserve >= targetReserve || townStock <= 0 || cratePrice <= 0 || reservePerCrate <= 0)
            {
                return 0;
            }

            int affordable = Math.Max(0, availableGold - goldFloor) / cratePrice;
            int required = (targetReserve - reserve + reservePerCrate - 1) / reservePerCrate;
            return Math.Max(0, Math.Min(townStock, Math.Min(affordable, required)));
        }

        internal static int CalculateProvisionTarget(float dailyUse, int provisionDays)
        {
            if (dailyUse <= 0f || provisionDays <= 0)
            {
                return 0;
            }

            int target = (int)Math.Ceiling(dailyUse * provisionDays);
            return Math.Max(ReservePerSupplyCrate, Math.Min(MaximumReserve, target));
        }

        internal static bool TryConsumeFromPool(int[] reserves, int amount)
        {
            if (reserves == null || amount <= 0)
            {
                return false;
            }

            int available = 0;
            for (int index = 0; index < reserves.Length && available < amount; index++)
            {
                available += Math.Max(0, reserves[index]);
            }

            if (available < amount)
            {
                return false;
            }

            int remaining = amount;
            for (int index = 0; index < reserves.Length && remaining > 0; index++)
            {
                int consumed = Math.Min(Math.Max(0, reserves[index]), remaining);
                reserves[index] -= consumed;
                remaining -= consumed;
            }

            return true;
        }

        internal static int ClampReserve(int reserve)
        {
            return Math.Max(0, Math.Min(MaximumReserve, reserve));
        }

        internal static float ClampDebt(float debt)
        {
            if (float.IsNaN(debt) || float.IsInfinity(debt) || debt <= 0f)
            {
                return 0f;
            }

            return debt >= 1f ? debt - (float)Math.Floor(debt) : debt;
        }
    }
}
