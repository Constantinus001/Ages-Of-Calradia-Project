using System;
using System.Linq;
using System.Reflection;

namespace CalradiaCampaignClock
{
    internal static class ClockOwnerCompatibility
    {
        private const string AgesAssemblyName = "AgesOfCalradia";
        private const string AgesClockTypeName =
            "TwelveMonthCalendar.CalendarMapTimeControlVM";

        internal static bool HasAgesOfCalradiaClock()
        {
            try
            {
                Assembly agesAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(assembly => string.Equals(
                        assembly.GetName().Name,
                        AgesAssemblyName,
                        StringComparison.Ordinal));
                return agesAssembly != null
                    && agesAssembly.GetType(AgesClockTypeName, false) != null;
            }
            catch (Exception exception)
            {
                CampaignClockDiagnostics.Error(
                    "Existing clock-owner detection failed; the standalone clock was disabled safely.",
                    exception);
                return true;
            }
        }
    }
}
