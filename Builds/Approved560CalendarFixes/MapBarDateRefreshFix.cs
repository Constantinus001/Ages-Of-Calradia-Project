using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapBar;

namespace AgesOfCalradia.Approved560CalendarFixes
{
    // Native target: MapTimeControlVM.Tick(), Bannerlord 1.4.8, postfix.
    // The approved VM refreshes custom date/year lines only in fast-forward.
    // Refresh once per calendar day in all modes, including first display and
    // save reload. No core replacement or campaign writes. Unsupported VMs are
    // ignored; reflection failure is logged once per VM with native UI retained.
    // Verify-MapBarDateRefresh.ps1 exercises the per-day refresh gate.
    [HarmonyPatch(typeof(MapTimeControlVM), nameof(MapTimeControlVM.Tick))]
    internal static class MapBarDateRefreshFix
    {
        private sealed class State
        {
            internal double Day = double.NaN;
            internal bool Failed;
        }

        private static readonly ConditionalWeakTable<MapTimeControlVM, State> States =
            new ConditionalWeakTable<MapTimeControlVM, State>();
        private static MethodInfo _refresh;
        private static MethodInfo _calendarDays;

        internal static bool NeedsRefresh(double previousDay, double currentDay)
        {
            return !double.IsNaN(currentDay) && !double.IsInfinity(currentDay)
                && (double.IsNaN(previousDay) || Math.Floor(previousDay) != Math.Floor(currentDay));
        }

        [HarmonyPostfix]
        private static void Postfix(MapTimeControlVM __instance)
        {
            if (__instance == null || Campaign.Current == null ||
                __instance.GetType().FullName != "TwelveMonthCalendar.CalendarMapTimeControlVM") return;
            State state = States.GetValue(__instance, CreateState);
            if (state.Failed) return;
            try
            {
                if (_refresh == null)
                {
                    Type vmType = __instance.GetType();
                    _refresh = vmType.GetMethod("RefreshCalendarDisplay", BindingFlags.Instance |
                        BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                    Type math = vmType.Assembly.GetType("TwelveMonthCalendar.CalendarTimeMath", true);
                    _calendarDays = math.GetMethod("ToCalendarAbsoluteDays", BindingFlags.Static |
                        BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(CampaignTime) }, null);
                    if (_refresh == null || _calendarDays == null)
                        throw new MissingMethodException("Approved MapBar calendar refresh methods are unavailable.");
                }
                double day = (double)_calendarDays.Invoke(null, new object[] { CampaignTime.Now });
                if (!NeedsRefresh(state.Day, day)) return;
                _refresh.Invoke(__instance, null);
                state.Day = Math.Floor(day);
            }
            catch (Exception exception)
            {
                state.Failed = true;
                Trace.WriteLine("AOC MapBar date refresh failed; existing UI retained: " + exception);
            }
        }

        private static State CreateState(MapTimeControlVM instance) { return new State(); }
    }
}
