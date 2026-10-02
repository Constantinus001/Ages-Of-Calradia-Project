using System;
using CalradiaCampaignClock;

internal static class ClockFormatterVerifier
{
    private static int Main()
    {
        Verify(0d, false, true, "12:00\nAM", "midnight");
        Verify(11d + 59d / 60d, false, true, "11:59\nAM", "before noon");
        Verify(12d, false, true, "12:00\nPM", "noon");
        Verify(23d + 59d / 60d, false, true, "11:59\nPM", "before midnight");
        Verify(24d, false, true, "12:00\nAM", "positive rollover");
        Verify(-0.5d, false, true, "11:30\nPM", "negative normalization");
        Verify(9.5d, false, false, "9:30 AM", "single-line meridiem");
        Verify(0d, true, true, "00:00", "24-hour midnight");
        Verify(13.5d, true, true, "13:30", "24-hour afternoon");
        Verify(9d + 30.9d / 60d, true, true, "09:30", "minute flooring");
        Verify(double.NaN, false, true, string.Empty, "NaN fallback");
        Verify(double.PositiveInfinity, true, true, string.Empty, "infinity fallback");

        Console.WriteLine(
            "PASS: campaign clock formatting covers noon, midnight, rollover, negative normalization, minute flooring, 12-hour layout, and 24-hour layout.");
        return 0;
    }

    private static void Verify(
        double hour,
        bool use24HourClock,
        bool secondLine,
        string expected,
        string name)
    {
        string actual = ClockFormatter.FormatHour(
            hour,
            use24HourClock,
            secondLine);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                name + " expected '" + Escape(expected)
                + "' but received '" + Escape(actual) + "'.");
        }
    }

    private static string Escape(string value)
    {
        return (value ?? string.Empty).Replace("\n", "\\n");
    }
}
