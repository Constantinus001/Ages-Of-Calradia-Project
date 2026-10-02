using System;
using System.Runtime.CompilerServices;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Session-local identity prevents recycled native StringIds sharing balances.
    // Weak keys do not keep removed campaign parties alive.
    internal sealed class EconomyObjectIdentity
    {
        private sealed class Entry { internal long Value; }
        private readonly ConditionalWeakTable<object, Entry> _entries = new ConditionalWeakTable<object, Entry>();
        private long _next;
        internal long Get(object value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return _entries.GetValue(value, _ => new Entry { Value = ++_next }).Value;
        }
    }
}
