using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace AgesOfCalradia.CoastSurfaceFix
{
    // Enumerates the hash-pinned renderer's marching-cell candidates only.
    // No geometry allocation or native drawing occurs in this prepass.
    internal sealed class CoastTopologyScanner
    {
        private readonly Vec2 _minimum;
        private readonly float _stepX, _stepY;
        private readonly int _rows, _columns;
        private readonly Func<Vec2, object> _region;
        private readonly Action<Vec2, Vec2> _candidate;
        internal int CompletedRows { get; private set; }
        internal bool Complete { get { return CompletedRows >= _rows; } }
        internal CoastTopologyScanner(Vec2 minimum, Vec2 maximum, int rows, int columns, Func<Vec2, object> region, Action<Vec2, Vec2> candidate)
        {
            if (rows <= 0 || columns <= 0 || maximum.x <= minimum.x || maximum.y <= minimum.y) throw new ArgumentException("Invalid frontier grid.");
            _minimum = minimum; _rows = rows; _columns = columns;
            _stepX = (maximum.x - minimum.x) / columns; _stepY = (maximum.y - minimum.y) / rows;
            _region = region; _candidate = candidate;
        }
        internal void AdvanceRow()
        {
            if (Complete) return;
            float bottom = _minimum.y + CompletedRows * _stepY, top = bottom + _stepY;
            var lower = new object[_columns + 1]; var upper = new object[_columns + 1];
            for (int x = 0; x <= _columns; x++)
            {
                float column = _minimum.x + x * _stepX;
                lower[x] = _region(new Vec2(column, bottom)); upper[x] = _region(new Vec2(column, top));
            }
            for (int x = 0; x < _columns; x++)
            {
                float left = _minimum.x + x * _stepX, right = left + _stepX;
                var a = new Vec2(left, bottom); var b = new Vec2(right, bottom);
                var c = new Vec2(right, top); var d = new Vec2(left, top);
                var crosses = new List<Vec2>(4);
                Crossing(crosses, a, b, lower[x], lower[x + 1]); Crossing(crosses, b, c, lower[x + 1], upper[x + 1]);
                Crossing(crosses, c, d, upper[x + 1], upper[x]); Crossing(crosses, d, a, upper[x], lower[x]);
                if (crosses.Count == 2) _candidate(crosses[0], crosses[1]);
                else if (crosses.Count == 4 && lower[x].Equals(upper[x + 1]) && lower[x + 1].Equals(upper[x]))
                {
                    bool centerMatches = _region(new Vec2((left + right) * .5f, (bottom + top) * .5f)).Equals(lower[x]);
                    _candidate(crosses[centerMatches ? 0 : 3], crosses[centerMatches ? 1 : 0]);
                    _candidate(crosses[centerMatches ? 2 : 1], crosses[centerMatches ? 3 : 2]);
                }
                else if (crosses.Count > 2)
                {
                    var center = new Vec2((left + right) * .5f, (bottom + top) * .5f);
                    foreach (Vec2 p in crosses) _candidate(p, center);
                }
            }
            CompletedRows++;
        }
        private static void Crossing(List<Vec2> points, Vec2 a, Vec2 b, object first, object second)
        {
            if (!first.Equals(second)) points.Add((a + b) * .5f);
        }
    }

    internal static class CoastSupportPolicy
    {
        // A rejection from probes outside a thin peninsula can be rescued only
        // by included political land within the actual ribbon footprint.
        // All five original stations still require a land witness.
        internal static bool HasFootprintSupport(Vec2 first, Vec2 second, Vec2 direction, Func<Vec2, bool> includedLand)
        {
            Vec2 normal = new Vec2(-direction.y, direction.x);
            for (int i = 0; i <= 4; i++)
            {
                Vec2 p = first + (second - first) * (i * .25f);
                bool supported = includedLand(p + normal * 2.75f) || includedLand(p - normal * 2.75f);
                if (!supported)
                    supported = includedLand(p) || includedLand(p + normal * .4f) || includedLand(p - normal * .4f)
                        || includedLand(p + normal * .8f) || includedLand(p - normal * .8f);
                if (!supported) return false;
            }
            return true;
        }
    }
}
