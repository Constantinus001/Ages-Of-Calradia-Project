using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Bounded, lossy daily evidence, deliberately NOT a transaction ledger.
    // No per-call disk IO. Net, positive and negative totals retain offsetting flows.
    internal sealed class EconomyDailySummary : IDisposable
    {
        // Full 365-day observer runs need multi-gigabyte bounded summaries.
        // This is an evidence cap, not a rolling deletion threshold.
        internal const long SessionByteLimit = 5L * 1024L * 1024L * 1024L;
        internal sealed class Total
        {
            internal long Count;
            internal double Net, Positive, Negative, First, Last;
            internal string Detail;
            internal void Add(double before, double after, string detail)
            {
                if (!EconomyLedger.Finite(before) || !EconomyLedger.Finite(after)) throw new ArgumentException("Nonfinite summary");
                double delta = after - before;
                if (Count == 0) First = before;
                Last = after; Count++; Net += delta;
                if (delta > 0) Positive += delta; else Negative += delta;
                Detail = detail;
            }
        }
        private readonly Dictionary<string, Total> _totals = new Dictionary<string, Total>();
        private readonly StreamWriter _writer;
        private double _day = double.NaN;
        internal EconomyDailySummary(string path)
        {
            _writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), System.Text.Encoding.UTF8, 65536);
            _writer.WriteLine("day\tkind\towner\tmetric\tsource\tcount\tnet\tpositive\tnegative\tfirst\tlast\tlastDetail");
        }
        internal void Add(double day, string kind, string owner, string metric, string source, double before, double after, string detail)
        {
            if (!EconomyLedger.Finite(day)) throw new ArgumentException("Nonfinite summary day");
            double bucket = Math.Floor(day);
            if (_day != bucket) { Flush(); _day = bucket; }
            // Scopes are only meaningful in raw mode. Inventory owners/modifiers
            // stay in the opt-in ledger; the daily view groups these by caller/item.
            if (kind == "BEGIN" || kind == "END" || kind == "ROSTER_BIND") return;
            if (kind == "INVENTORY") { owner = "all-observed-rosters"; int i = metric.IndexOf("/modifier:", StringComparison.Ordinal); if (i >= 0) metric = metric.Substring(0, i); }
            if (kind == "RESOURCE_FLOW")
            {
                if (owner.StartsWith("party:", StringComparison.Ordinal)) owner = "observed-mobile-parties";
                int i = metric.IndexOf("/modifier:", StringComparison.Ordinal); if (i >= 0) metric = metric.Substring(0, i);
            }
            string key = Clean(kind) + "\t" + Clean(owner) + "\t" + Clean(metric) + "\t" + Clean(source);
            Total total;
            if (!_totals.TryGetValue(key, out total))
            {
                if (_totals.Count >= 50000) throw new InvalidOperationException("Daily economy summary key limit reached; coverage invalid");
                _totals.Add(key, total = new Total());
            }
            total.Add(before, after, detail);
        }
        internal void Flush()
        {
            foreach (var pair in _totals)
            {
                Total t = pair.Value;
                _writer.WriteLine(N(_day) + "\t" + pair.Key + "\t" + t.Count + "\t" + N(t.Net) + "\t" + N(t.Positive) + "\t" + N(t.Negative)
                    + "\t" + N(t.First) + "\t" + N(t.Last) + "\t" + Clean(t.Detail));
            }
            _totals.Clear(); _writer.Flush();
            if (_writer.BaseStream.Position > SessionByteLimit) throw new IOException("Daily economy summaries exceeded 5 GiB; coverage invalid");
        }
        public void Dispose() { try { Flush(); } finally { _writer.Dispose(); } }
        private static string N(double value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        private static string Clean(string text) { return (text ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }
    }
}
