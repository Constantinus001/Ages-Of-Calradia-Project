using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Extra investigative evidence only; never samples or replaces the ledger.
    // Capture-thread owned, reset per session. No game objects retained. Limits
    // apply even to oversized records and recurrent incidents. Never deletes
    // historical packages; exhausted storage is explicitly reported instead.
    internal sealed class IncidentRecorder
    {
        internal const int RingBytes = 131072, RowLimit = 8192, IncidentLimit = 32;
        internal const long DiskLimit = 33554432;
        private const int PostRows = 64, IdentityLimit = 262144;
        private const int CriticalSlots = 4, FamilyLimit = 8;
        private const long CriticalDiskReserve = 1048576;
        private readonly Queue<string> _recent = new Queue<string>();
        private readonly WalletIncidentHistory _walletHistory = new WalletIncidentHistory();
        private readonly Dictionary<string, int> _families = new Dictionary<string, int>();
        private readonly List<string> _identity = new List<string>();
        private readonly Dictionary<string, Incident> _issues = new Dictionary<string, Incident>();
        private readonly string _directory, _session;
        private int _ringBytes, _identityBytes;
        private long _diskBytes, _omitted, _suppressed;
        private long _issueCapOccurrences, _familyCapOccurrences, _diskCapWrites;
        private int _normalIssues, _criticalIssues;
        private bool _failed;
        private sealed class Incident
        {
            internal string Key, Path, First, Last;
            internal bool Critical;
            internal string[] WalletRows = new string[0];
            internal long Count, Size;
            internal int Remaining = PostRows;
            internal readonly List<string> Rows = new List<string>();
            internal bool Dirty;
        }
        internal IncidentRecorder(string directory, string session)
        {
            _directory = Path.Combine(directory, "AocIncidents"); _session = session;
            try
            {
                Directory.CreateDirectory(_directory);
                // Includes interrupted temporary files; never claims their bytes free.
                _diskBytes = Directory.EnumerateFiles(_directory).Sum(p => new FileInfo(p).Length);
            }
            catch (Exception ex) { Failure(ex); }
        }
        private static int Bytes(string line) { return Encoding.UTF8.GetByteCount(line) + 2; }
        internal void Observe(string line, string[] f)
        {
            if (_failed) return;
            using (DiagnosticObserverCost.Measure("incident_recorder"))
            try
            {
                bool fits = Bytes(line) <= RowLimit;
                if (!fits) _omitted++;
                if (fits && (f[4] == "WALLET_CHANGE" || f[4] == "WALLET_BASELINE" || f[4] == "WALLET_ALIAS"))
                    _walletHistory.Observe(f[7], line);
                foreach (var incident in _issues.Values)
                    if (incident.Remaining > 0)
                    {
                        if (fits) incident.Rows.Add(line);
                        incident.Remaining--; incident.Dirty = true;
                        if (incident.Remaining == 0) Save(incident, "post_window_complete");
                    }
                if (f[4] == "SESSION_START" || f[4] == "CORE_SYSTEMS" || f[4] == "CAPTURE_HOOK" || f[4] == "MARKET_MODELS")
                {
                    if (fits && _identityBytes + Bytes(line) <= IdentityLimit)
                    { _identity.Add(line); _identityBytes += Bytes(line); }
                    else _omitted++;
                }
                double before, after;
                bool mismatch = (f[4] == "WALLET_CHECK" || f[4] == "WORKSHOP_FLOW_CHECK")
                    && double.TryParse(f[9], NumberStyles.Float, CultureInfo.InvariantCulture, out before)
                    && double.TryParse(f[10], NumberStyles.Float, CultureInfo.InvariantCulture, out after) && before != after;
                if (mismatch || (f[4] == "OBSERVER_COVERAGE" && f[8] == "outer_boundary_recovered_activity"))
                    Trigger(f[4] + ":" + f[7] + ":" + f[8], line);
                if (fits)
                {
                    _recent.Enqueue(line); _ringBytes += Bytes(line);
                    while (_ringBytes > RingBytes || _recent.Count > 256) _ringBytes -= Bytes(_recent.Dequeue());
                }
            }
            catch (Exception ex) { Failure(ex); }
        }
        internal void Trigger(string key, string evidence)
        {
            if (_failed) return;
            try
            {
                if (Bytes(evidence) > RowLimit)
                { _omitted++; evidence = "Oversized divergence detail omitted; inspect main ledger. " + key; }
                Incident incident;
                if (_issues.TryGetValue(key, out incident))
                { incident.Count++; incident.Last = evidence; incident.Dirty = true; return; }
                bool critical = key.StartsWith("capture_failure:", StringComparison.Ordinal);
                if ((critical && _criticalIssues >= CriticalSlots) || (!critical && _normalIssues >= IncidentLimit - CriticalSlots))
                { _suppressed++; _issueCapOccurrences++; return; }
                string[] fields = evidence.Split('\t');
                string family = fields.Length >= 12 ? fields[4] + ":" + fields[7].Split('/')[0] + ":" + fields[8] : key.Split(':')[0];
                int familyCount;
                _families.TryGetValue(family, out familyCount);
                if (!critical && familyCount >= FamilyLimit)
                { _suppressed++; _familyCapOccurrences++; return; }
                incident = new Incident { Key = key, First = evidence, Last = evidence, Count = 1, Dirty = true,
                    Critical = critical,
                    WalletRows = fields.Length >= 12 ? _walletHistory.Get(fields[7]) : new string[0],
                    Path = Path.Combine(_directory, _session + "-" + Guid.NewGuid().ToString("N") + ".json") };
                incident.Rows.AddRange(_recent);
                if (Bytes(evidence) <= RowLimit) incident.Rows.Add(evidence); else _omitted++;
                _issues.Add(key, incident);
                if (critical) _criticalIssues++; else _normalIssues++;
                _families[family] = familyCount + 1;
                Save(incident, "post_window_pending"); // First divergence is durable immediately.
            }
            catch (Exception ex) { Failure(ex); }
        }
        internal void Flush(string reason)
        {
            if (_failed) return;
            using (DiagnosticObserverCost.Measure("incident_publication"))
            try
            {
                foreach (var incident in _issues.Values)
                    if (incident.Dirty || incident.Remaining > 0) Save(incident, incident.Remaining == 0 ? "post_window_complete" : reason + "_post_window_incomplete");
            }
            catch (Exception ex) { Failure(ex); }
        }
        private void Save(Incident incident, string status)
        {
            string text = "{\"schema\":1,\"session\":" + CaptureReadiness.Json(_session)
                + ",\"issue\":" + CaptureReadiness.Json(incident.Key) + ",\"occurrences\":" + incident.Count
                + ",\"firstDivergence\":" + CaptureReadiness.Json(incident.First)
                + ",\"latestDivergence\":" + CaptureReadiness.Json(incident.Last)
                + ",\"status\":" + CaptureReadiness.Json(status)
                + ",\"diagnosticsMvid\":" + CaptureReadiness.Json(typeof(IncidentRecorder).Module.ModuleVersionId.ToString())
                + ",\"identityRows\":[" + string.Join(",", _identity.Select(CaptureReadiness.Json)) + "]"
                + ",\"evidenceRows\":[" + string.Join(",", incident.Rows.Select(CaptureReadiness.Json)) + "]"
                + ",\"walletHistoryRows\":[" + string.Join(",", incident.WalletRows.Select(CaptureReadiness.Json)) + "]"
                + ",\"critical\":" + (incident.Critical ? "true" : "false")
                + ",\"walletHistoryEvictedRows\":" + _walletHistory.EvictedRows
                + ",\"omittedExtraRows\":" + _omitted
                + ",\"alternatives\":[\"Observer bypass or inlining\",\"Uncovered direct mutation\",\"Aliasing or overlapping scope\"]"
                + ",\"missingProof\":[\"Exact causal mutation and amount\",\"Review complete unsampled ledger\",\"Missing identities are unsupported, not inferred\"]"
                + ",\"limits\":[\"Bounded surrounding rows may omit the true cause\",\"Grouped within this session only\",\"Extra evidence only; not a full accounting capture\"]}";
            long bytes = Encoding.UTF8.GetByteCount(text);
            // Account for old plus temporary new bytes during atomic replacement.
            long allowed = incident.Critical ? DiskLimit : DiskLimit - CriticalDiskReserve;
            if (_diskBytes + bytes > allowed) { _suppressed++; _diskCapWrites++; incident.Dirty = false; return; }
            string temporary = incident.Path + ".new";
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(text);
            if (File.Exists(incident.Path)) File.Replace(temporary, incident.Path, null);
            else File.Move(temporary, incident.Path);
            _diskBytes += bytes - incident.Size; incident.Size = bytes; incident.Dirty = false;
        }
        private void Failure(Exception error)
        {
            _failed = true;
            SoakLog.Write("INCIDENT_RECORDER_FAILURE", error.ToString() + "; main_accounting_capture_unchanged");
        }
        internal string StatusJson()
        {
            return "{\"status\":\"" + (_failed ? "failed" : _suppressed > 0 ? "storage_or_issue_budget_exhausted" : "ready")
                + "\",\"incidents\":" + _issues.Count + ",\"suppressedWritesOrIssues\":" + _suppressed
                + ",\"issueCapSuppressedOccurrences\":" + _issueCapOccurrences
                + ",\"familyCapSuppressedOccurrences\":" + _familyCapOccurrences
                + ",\"diskCapSuppressedWrites\":" + _diskCapWrites
                + ",\"criticalIncidents\":" + _criticalIssues
                + ",\"walletHistoryBytes\":" + _walletHistory.Bytes
                + ",\"walletHistoryEvictedRows\":" + _walletHistory.EvictedRows
                + ",\"omittedExtraRows\":" + _omitted + ",\"ringBytes\":" + _ringBytes + ",\"diskBytes\":" + _diskBytes + "}";
        }
    }
}
