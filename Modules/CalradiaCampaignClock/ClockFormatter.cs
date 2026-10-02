using System;

namespace CalradiaCampaignClock
{
    /// <summary>
    /// Pure formatting boundary shared by the runtime clock and deterministic
    /// tests. Minutes are floored so the label never displays a future minute.
    /// </summary>
    internal static class ClockFormatter
    {
        private const int HoursPerDay = 24;
        private const int MinutesPerHour = 60;
        private const int MinutesPerDay = HoursPerDay * MinutesPerHour;

        internal static string FormatHour(
            double hourInDay,
            bool use24HourClock,
            bool showMeridiemOnSecondLine)
        {
            int minuteOfDay = GetMinuteOfDay(hourInDay);
            if (minuteOfDay < 0)
            {
                return string.Empty;
            }

            int hour = minuteOfDay / MinutesPerHour;
            int minute = minuteOfDay % MinutesPerHour;
            if (use24HourClock)
            {
                return string.Format("{0:00}:{1:00}", hour, minute);
            }

            int twelveHour = hour % 12;
            string clock = string.Format(
                "{0}:{1:00}",
                twelveHour == 0 ? 12 : twelveHour,
                minute);
            string meridiem = hour < 12 ? "AM" : "PM";
            return showMeridiemOnSecondLine
                ? clock + "\n" + meridiem
                : clock + " " + meridiem;
        }

        internal static int GetMinuteOfDay(double hourInDay)
        {
            if (double.IsNaN(hourInDay) || double.IsInfinity(hourInDay))
            {
                return -1;
            }

            double normalized = hourInDay % HoursPerDay;
            if (normalized < 0d)
            {
                normalized += HoursPerDay;
            }

            int minuteOfDay = (int)Math.Floor(normalized * MinutesPerHour);
            return Math.Max(0, Math.Min(MinutesPerDay - 1, minuteOfDay));
        }
    }
}
