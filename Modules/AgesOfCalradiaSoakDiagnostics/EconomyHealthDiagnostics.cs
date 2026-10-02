using System;
using System.Diagnostics;

namespace AgesOfCalradia.SoakDiagnostics
{
    internal static class EconomyHealthDiagnostics
    {
        private static long _ticks, _calls;
        private static int _gc0;
        internal static void Reset() { _ticks = 0; _calls = 0; _gc0 = GC.CollectionCount(0); }
        internal static void Record(long start) { _ticks += Stopwatch.GetTimestamp() - start; _calls++; }
        internal static void Snapshot()
        {
            EconomyTrace.Write("OBSERVER_HEALTH", 0, 0, "diagnostics", "callback_milliseconds", 0, _ticks * 1000d / Stopwatch.Frequency,
                "observer-only", "callbacks=" + _calls + "; managedBytes=" + GC.GetTotalMemory(false) + "; gen0Collections=" + (GC.CollectionCount(0) - _gc0)
                + "; excludes_disabled_fastpath; totals_since_session_start; not_native_scope_time");
        }
    }
}
