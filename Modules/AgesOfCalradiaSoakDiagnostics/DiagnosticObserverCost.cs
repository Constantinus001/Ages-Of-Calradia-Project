using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Selected diagnostic-only boundaries. Excludes nested measured boundaries
    // from each parent's exclusive cost. Uninstrumented callbacks remain unknown;
    // writer timing can overlap and MUST NOT be added to these totals.
    internal static class DiagnosticObserverCost
    {
        private sealed class Cost { internal long Calls, Ticks, Maximum; }
        private static readonly Dictionary<string, Cost> Costs = new Dictionary<string, Cost>();
        [ThreadStatic] private static Measurement _current;
        internal sealed class Measurement : IDisposable
        {
            private readonly string _name;
            private readonly long _start;
            private readonly Measurement _parent;
            private long _children;
            private bool _closed;
            internal Measurement(string name) { _name = name; _start = Stopwatch.GetTimestamp(); _parent = _current; _current = this; }
            public void Dispose()
            {
                if (_closed) return;
                _closed = true;
                long elapsed = Stopwatch.GetTimestamp() - _start;
                _current = _parent;
                if (_parent != null) _parent._children += elapsed;
                Cost cost;
                if (!Costs.TryGetValue(_name, out cost)) Costs.Add(_name, cost = new Cost());
                long exclusive = Math.Max(0, elapsed - _children);
                cost.Calls++; cost.Ticks += exclusive; cost.Maximum = Math.Max(cost.Maximum, exclusive);
            }
        }
        internal static Measurement Measure(string name) { return new Measurement(name); }
        internal static void Reset() { Costs.Clear(); _current = null; }
        internal static string Json()
        {
            return "{" + string.Join(",", Costs.OrderBy(x => x.Key).Select(x => CaptureReadiness.Json(x.Key)
                + ":{\"calls\":" + x.Value.Calls + ",\"exclusiveSeconds\":" + SupplyCapture.N(x.Value.Ticks / (double)Stopwatch.Frequency)
                + ",\"maximumExclusiveSeconds\":" + SupplyCapture.N(x.Value.Maximum / (double)Stopwatch.Frequency) + "}")) + "}";
        }
    }
}
