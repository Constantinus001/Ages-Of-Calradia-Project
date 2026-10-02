using System;

namespace AgesOfCalradiaLogistics
{
    internal static class LogisticsSupplyMathVerifier
    {
        private static int Main()
        {
            AssertNear(0f, LogisticsSupplyMath.CalculateDailyUse(0), "zero troops");
            AssertNear(0.5f, LogisticsSupplyMath.CalculateDailyUse(1), "minimum demand");
            AssertNear(0.975f, LogisticsSupplyMath.CalculateDailyUse(39), "39 troops");
            AssertNear(1f, LogisticsSupplyMath.CalculateDailyUse(40), "40 troops");
            AssertNear(1.025f, LogisticsSupplyMath.CalculateDailyUse(41), "41 troops");
            AssertNear(2.5f, LogisticsSupplyMath.CalculateDailyUse(100), "100 troops");
            AssertNear(6f, LogisticsSupplyMath.CalculateDailyUse(240), "240 troops");
            AssertNear(6f, LogisticsSupplyMath.CalculateDailyUse(300), "demand cap");

            int reserve;
            float debt;
            LogisticsSupplyMath.AdvanceReserve(100, 0f, 2.5f, 30, out reserve, out debt);
            AssertEqual(25, reserve, "30-day reserve consumption");
            AssertNear(0f, debt, "30-day fractional debt");
            LogisticsSupplyMath.AdvanceReserve(100, 0f, 1.025f, 30, out reserve, out debt);
            AssertEqual(70, reserve, "fractional reserve consumption");
            AssertNear(0.75f, debt, "fractional debt retained");

            AssertEqual(0, LogisticsSupplyMath.CalculateElapsedDays(10, 10), "same-day idempotence");
            AssertEqual(0, LogisticsSupplyMath.CalculateElapsedDays(11, 10), "future-day corruption");
            AssertEqual(30, LogisticsSupplyMath.CalculateElapsedDays(0, 100), "catch-up cap");

            AssertEqual(LogisticsSupplyCondition.Empty, LogisticsSupplyMath.GetCondition(0, 2.5f), "empty tier");
            AssertEqual(LogisticsSupplyCondition.Critical, LogisticsSupplyMath.GetCondition(7, 2.5f), "critical upper boundary");
            AssertEqual(LogisticsSupplyCondition.Strained, LogisticsSupplyMath.GetCondition(8, 2.5f), "strained lower boundary");
            AssertEqual(LogisticsSupplyCondition.Strained, LogisticsSupplyMath.GetCondition(17, 2.5f), "strained upper boundary");
            AssertEqual(LogisticsSupplyCondition.Supported, LogisticsSupplyMath.GetCondition(18, 2.5f), "supported boundary");
            AssertEqual(LogisticsSupplyCondition.Supported, LogisticsSupplyMath.GetCondition(19, 0.5f), "small-party reserve remains supported");
            AssertEqual(LogisticsSupplyCondition.Supported, LogisticsSupplyMath.GetCondition(100, 6f), "supported maximum");
            AssertNear(0.80f, LogisticsSupplyMath.GetSpeedFactor(0, 2.5f), "empty speed");
            AssertNear(0.88f, LogisticsSupplyMath.GetSpeedFactor(7, 2.5f), "critical speed");
            AssertNear(0.95f, LogisticsSupplyMath.GetSpeedFactor(8, 2.5f), "strained speed");
            AssertNear(1f, LogisticsSupplyMath.GetSpeedFactor(18, 2.5f), "supported speed");

            AssertEqual(0, LogisticsSupplyMath.ClampReserve(-5), "reserve lower clamp");
            AssertEqual(100, LogisticsSupplyMath.ClampReserve(105), "reserve upper clamp");
            AssertNear(0f, LogisticsSupplyMath.ClampDebt(float.NaN), "NaN debt clamp");
            AssertNear(0.25f, LogisticsSupplyMath.ClampDebt(3.25f), "oversized debt clamp");

            AssertEqual(20, LogisticsSupplyMath.CalculateProvisionTarget(1f, 10), "minimum ten-day target");
            AssertEqual(25, LogisticsSupplyMath.CalculateProvisionTarget(2.5f, 10), "100-troop target");
            AssertEqual(60, LogisticsSupplyMath.CalculateProvisionTarget(6f, 10), "large-party target");
            AssertEqual(0, LogisticsSupplyMath.CalculateAiPurchaseCrates(0, 60, 5, 5000, 5000, 250, 20), "AI gold floor");
            AssertEqual(1, LogisticsSupplyMath.CalculateAiPurchaseCrates(0, 60, 5, 5250, 5000, 250, 20), "AI affordability boundary");
            AssertEqual(2, LogisticsSupplyMath.CalculateAiPurchaseCrates(0, 60, 2, 10000, 5000, 250, 20), "AI stock boundary");
            AssertEqual(1, LogisticsSupplyMath.CalculateAiPurchaseCrates(59, 60, 5, 10000, 5000, 250, 20), "AI target rounding");

            int[] pool = { 2, 5, 10 };
            AssertTrue(LogisticsSupplyMath.TryConsumeFromPool(pool, 6), "coalition debit succeeds");
            AssertEqual(0, pool[0], "first contributor exhausted");
            AssertEqual(1, pool[1], "second contributor debited");
            AssertEqual(10, pool[2], "later contributor untouched");
            int[] insufficient = { 1, 2 };
            AssertTrue(!LogisticsSupplyMath.TryConsumeFromPool(insufficient, 4), "insufficient coalition rejected");
            AssertEqual(1, insufficient[0], "failed debit is atomic");
            AssertEqual(2, insufficient[1], "failed debit preserves pool");

            Console.WriteLine("PASS: logistics supply math, provisioning, tiers, and coalition debit verified.");
            return 0;
        }

        private static void AssertEqual<T>(T expected, T actual, string name)
        {
            if (!Equals(expected, actual))
            {
                throw new InvalidOperationException(name + ": expected " + expected + ", got " + actual + ".");
            }
        }

        private static void AssertNear(float expected, float actual, string name)
        {
            if (Math.Abs(expected - actual) > 0.0001f)
            {
                throw new InvalidOperationException(name + ": expected " + expected + ", got " + actual + ".");
            }
        }

        private static void AssertTrue(bool value, string name)
        {
            if (!value)
            {
                throw new InvalidOperationException(name + ".");
            }
        }
    }
}
