using System;
using System.Collections.Generic;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Session-only history. A sampled zero-stock span is not continuous proof.
    internal sealed class EconomyStateHistory
    {
        private sealed class Entry { internal double Start, Last; internal bool Zero; }
        private readonly Dictionary<string, Entry> _stock = new Dictionary<string, Entry>();
        internal double Observe(string key, double day, double stock)
        {
            if (!EconomyLedger.Finite(day) || !EconomyLedger.Finite(stock) || stock < 0) throw new ArgumentException("Invalid stock history");
            Entry entry;
            if (!_stock.TryGetValue(key, out entry))
            {
                if (_stock.Count >= 50000) throw new InvalidOperationException("Stock-history budget exceeded");
                _stock.Add(key, entry = new Entry { Last = day, Start = day });
            }
            if (day < entry.Last) throw new InvalidOperationException("Stock history time moved backward without a session reset");
            bool zero = stock == 0;
            if (!zero || !entry.Zero) entry.Start = day;
            entry.Last = day; entry.Zero = zero;
            return zero ? day - entry.Start : 0;
        }
    }
}
