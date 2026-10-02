using System.Collections.Generic;
using System.Linq;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Counts only committed rows. No game access or inference from hook installation.
    // Reset on every session, including one-shot fixtures and monthly rotation.
    internal static class DiagnosticEventCoverage
    {
        private sealed class Entry { internal long Count, First, Last; }
        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>();
        private static long _unlisted;
        internal static void Reset() { Entries.Clear(); _unlisted = 0; }
        internal static void Observe(string kind, long sequence)
        {
            Entry entry;
            if (!Entries.TryGetValue(kind, out entry))
            {
                if (Entries.Count >= 256) { _unlisted++; return; }
                Entries.Add(kind, entry = new Entry { First = sequence });
            }
            entry.Count++; entry.Last = sequence;
        }
        internal static string Json()
        {
            return "{\"unlistedEventRows\":" + _unlisted + ",\"families\":{" + string.Join(",", Entries.OrderBy(e => e.Key)
                .Select(e => CaptureReadiness.Json(e.Key) + ":{\"count\":" + e.Value.Count
                    + ",\"firstSequence\":" + e.Value.First + ",\"lastSequence\":" + e.Value.Last + "}"))
                + "},\"limit\":\"Execution evidence only, not accounting or balance certification\"}";
        }
    }
}
