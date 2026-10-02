using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Pure accounting: observed mutations and independent snapshots must reconcile.
    internal sealed class EconomyLedger
    {
        private readonly Dictionary<string, double> _balances = new Dictionary<string, double>();
        private readonly Dictionary<string, HashSet<string>> _inventoryKeys = new Dictionary<string, HashSet<string>>();
        internal static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        internal double Observe(string key, double before, double after)
        {
            if (!Finite(before) || !Finite(after)) throw new ArgumentException("Nonfinite economy balance");
            if (_balances.Count >= 500000 && !_balances.ContainsKey(key)) throw new InvalidOperationException("Economy account budget exceeded; coverage invalid");
            double expected;
            bool known = _balances.TryGetValue(key, out expected);
            double residual = known ? before - expected : 0;
            int separator = key.IndexOf("|item:", StringComparison.Ordinal);
            if (!known && separator >= 0)
            {
                string prefix = key.Substring(0, separator + 6);
                HashSet<string> keys;
                if (!_inventoryKeys.TryGetValue(prefix, out keys)) _inventoryKeys.Add(prefix, keys = new HashSet<string>());
                keys.Add(key);
            }
            _balances[key] = after;
            return residual;
        }

        internal double Reconcile(string key, double actual)
        {
            return Observe(key, actual, actual);
        }
        internal IEnumerable<string> Keys(string prefix)
        {
            HashSet<string> keys;
            return _inventoryKeys.TryGetValue(prefix, out keys) ? new List<string>(keys) : new List<string>();
        }
    }

    // Separate, buffered ledger. IO failure invalidates coverage, never gameplay.
    internal static class EconomyTrace
    {
        private static StreamWriter _writer;
        private static EconomyDailySummary _daily;
        private static readonly Dictionary<string, long> Counts = new Dictionary<string, long>();
        private static long _sequence;
        private static DateTime _lastFlush;
        internal static bool Healthy { get; private set; }
        internal static bool RawEnabled { get { return Healthy && _writer != null; } }
        internal static long RawBytes { get { return _writer == null ? 0 : _writer.BaseStream.Position; } }
        internal static string Session { get; private set; }
        internal static Func<double> Day = () => 0;
        internal static Action<string> Failure = message => SoakLog.Write("ECONOMY_FAILURE", message);

        internal static string Start(string directory, bool summary = false)
        {
            Close();
            Session = Guid.NewGuid().ToString("N");
            Counts.Clear();
            _sequence = 0;
            string path = Path.Combine(directory, (summary ? "AocEconomyDaily-" : "AocEconomy-") + Session + ".tsv");
            try
            {
                if (summary) _daily = new EconomyDailySummary(path);
                else
                {
                    _writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), System.Text.Encoding.UTF8, 65536);
                    _writer.WriteLine("utc\tsession\tsequence\tday\tkind\ttransaction\tparent\towner\tmetric\tbefore\tafter\tdelta\tsource\tdetail");
                }
                Healthy = true;
                _lastFlush = DateTime.UtcNow;
            }
            catch (Exception ex) { Fail("open: " + ex); }
            return path;
        }

        internal static long NextId() { return System.Threading.Interlocked.Increment(ref _sequence); }
        internal static void Write(string kind, long transaction, long parent, string owner, string metric,
            double before, double after, string source, string detail)
        {
            if (!Healthy) return;
            try
            {
                if (!EconomyLedger.Finite(before) || !EconomyLedger.Finite(after)) throw new ArgumentException("Nonfinite ledger row");
                long count; Counts.TryGetValue(kind, out count); Counts[kind] = count + 1;
                if (_daily != null) { _daily.Add(Day(), kind, owner, metric, source, before, after, detail); return; }
                _writer.WriteLine(string.Join("\t", new[] { DateTime.UtcNow.ToString("O"), Session, NextId().ToString(),
                    Day().ToString("R", CultureInfo.InvariantCulture), kind, transaction.ToString(), parent.ToString(), Clean(owner), Clean(metric),
                    before.ToString("R", CultureInfo.InvariantCulture), after.ToString("R", CultureInfo.InvariantCulture),
                    (after - before).ToString("R", CultureInfo.InvariantCulture), Clean(source), Clean(detail) }));
            }
            catch (Exception ex) { Fail("write: " + ex); }
        }

        internal static void Flush(bool force = false)
        {
            if (!Healthy || (!force && DateTime.UtcNow - _lastFlush < TimeSpan.FromSeconds(1))) return;
            try
            {
                if (_daily != null) { if (force) _daily.Flush(); _lastFlush = DateTime.UtcNow; return; }
                _writer.Flush(); _lastFlush = DateTime.UtcNow;
                if (_writer.BaseStream.Position > 1073741824L) throw new IOException("Economy ledger exceeded 1 GiB per session; coverage invalid, shorten validation run");
            }
            catch (Exception ex) { Fail("flush: " + ex); }
        }

        internal static void Coverage()
        {
            foreach (string kind in new[] { "HERO_GOLD", "PARTY_GOLD", "SETTLEMENT_GOLD", "WORKSHOP_GOLD", "FOOD_STOCK",
                "INVENTORY", "WAGE_ASSESSMENT", "TRIBUTE_COMPONENT", "WORKSHOP_FLOW", "FOOD_EXPLANATION", "FINANCE_COMPONENT",
                "CLAN_SETTLEMENT", "RECIPE_CYCLE", "RECIPE_PROGRESS", "RECIPE_GATE", "WALLET", "MARKET_STOCK" })
            {
                long count; Counts.TryGetValue(kind, out count);
                Write("COVERAGE", 0, 0, kind, "observations", count, count, "coverage", count == 0 ? "NOT_EXERCISED" : "OBSERVED");
            }
            Flush(true);
        }

        internal static void Fail(string message) { Healthy = false; Failure(message); }
        internal static void Close()
        {
            if (_daily != null)
            {
                try { _daily.Dispose(); } catch (Exception ex) { Fail("summary close: " + ex); }
                finally { _daily = null; Healthy = false; }
            }
            if (_writer == null) return;
            try { _writer.Dispose(); }
            catch (Exception ex) { Fail("close: " + ex); }
            finally { _writer = null; Healthy = false; }
        }
        private static string Clean(string value) { return (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }
    }
}
