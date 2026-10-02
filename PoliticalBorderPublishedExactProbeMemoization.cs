using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AgesOfCalradia.PoliticalBorderOptimizer
{
    [StructLayout(LayoutKind.Explicit)]
    internal struct PublishedExactProbeFloatBits
    {
        [FieldOffset(0)] internal float Value;
        [FieldOffset(0)] internal int Bits;
    }

    // Published layouts must see the same native probe result for each exact
    // generated coordinate. Interpolating a nearby prepared cell can change
    // which native rows exist, invalidating a topology-bound layout.
    internal sealed class PublishedExactProbeMemoization<T>
    {
        internal const int MaximumEntries = 262144;
        private struct PointKey : IEquatable<PointKey>
        {
            private readonly int _x;
            private readonly int _y;

            internal PointKey(float x, float y)
            {
                _x = new PublishedExactProbeFloatBits { Value = x }.Bits;
                _y = new PublishedExactProbeFloatBits { Value = y }.Bits;
            }

            public bool Equals(PointKey other)
            {
                return _x == other._x && _y == other._y;
            }

            public override bool Equals(object value)
            {
                return value is PointKey && Equals((PointKey)value);
            }

            public override int GetHashCode()
            {
                unchecked { return (_x * 397) ^ _y; }
            }
        }

        private struct ProbeResult
        {
            internal T Value;
            internal bool Success;
        }

        private readonly Dictionary<PointKey, ProbeResult> _results =
            new Dictionary<PointKey, ProbeResult>();

        internal static bool ShouldUsePreparedProbe(bool publishedLayout, bool exactMemoHit)
        {
            return !publishedLayout && !exactMemoHit;
        }

        internal bool TryGet(float x, float y, out T value, out bool success)
        {
            ProbeResult result;
            if (!_results.TryGetValue(new PointKey(x, y), out result))
            {
                value = default(T);
                success = false;
                return false;
            }

            value = result.Value;
            success = result.Success;
            return true;
        }

        internal bool Record(float x, float y, T value, bool success)
        {
            PointKey key = new PointKey(x, y);
            if (!_results.ContainsKey(key) && _results.Count >= MaximumEntries)
                return false;
            _results[key] = new ProbeResult
            {
                Value = value,
                Success = success
            };
            return true;
        }

        internal void Clear()
        {
            _results.Clear();
        }
    }
}
