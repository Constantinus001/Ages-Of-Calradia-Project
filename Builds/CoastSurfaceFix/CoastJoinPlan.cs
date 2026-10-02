using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace AgesOfCalradia.CoastSurfaceFix
{
    internal struct CoastSpan : IEquatable<CoastSpan>
    {
        private readonly Vec2 _first, _second;
        internal CoastSpan(Vec2 first, Vec2 second) { _first = first; _second = second; }
        public bool Equals(CoastSpan other) { return _first.Equals(other._first) && _second.Equals(other._second); }
        public override bool Equals(object value) { return value is CoastSpan && Equals((CoastSpan)value); }
        public override int GetHashCode() { return _first.GetHashCode() * 397 ^ _second.GetHashCode(); }
    }

    // Built before the first frontier mesh, from this generation's own regions.
    // Thus an inland/unknown segment drawn first still knows its coastal join.
    internal sealed class CoastJoinPlan
    {
        internal readonly HashSet<CoastSpan> Coast = new HashSet<CoastSpan>();
        internal readonly HashSet<CoastSpan> Restored = new HashSet<CoastSpan>();
        private readonly Dictionary<Tuple<int, int>, List<Vec2>> _joins = new Dictionary<Tuple<int, int>, List<Vec2>>();
        private readonly Dictionary<Tuple<int, int>, List<Tuple<Vec2, Vec2>>> _incident = new Dictionary<Tuple<int, int>, List<Tuple<Vec2, Vec2>>>();
        internal void Add(Vec2 first, Vec2 second, bool restored)
        {
            var span = new CoastSpan(first, second);
            Coast.Add(span);
            if (restored) Restored.Add(span);
            AddJoin(first); AddJoin(second);
            AddIncident(first, second);
        }
        internal void AddIncident(Vec2 first, Vec2 second)
        {
            Vec2 direction = second - first;
            // Refuse cap clipping for geometry shorter than the cap radius.
            // Approved cell spans are longer; tests verify captured coverage.
            float length = direction.Normalize();
            if (length <= .8002f) return;
            IncidentAt(first, direction); IncidentAt(second, direction * -1f);
        }
        private void IncidentAt(Vec2 center, Vec2 outward)
        {
            var key = Tuple.Create((int)Math.Floor(center.x), (int)Math.Floor(center.y));
            List<Tuple<Vec2, Vec2>> list;
            if (!_incident.TryGetValue(key, out list)) _incident.Add(key, list = new List<Tuple<Vec2, Vec2>>());
            list.Add(Tuple.Create(center, outward));
        }
        internal List<Vec2> DirectionsAt(Vec2 point)
        {
            var result = new List<Vec2>();
            int x = (int)Math.Floor(point.x), y = (int)Math.Floor(point.y);
            for (int dx=-1; dx<=1; dx++) for (int dy=-1; dy<=1; dy++)
            {
                List<Tuple<Vec2,Vec2>> list;
                if (!_incident.TryGetValue(Tuple.Create(x+dx,y+dy),out list)) continue;
                foreach (var item in list)
                    if ((item.Item1-point).LengthSquared <= .00015f*.00015f) result.Add(item.Item2);
            }
            return result;
        }
        private void AddJoin(Vec2 point)
        {
            var key = Tuple.Create((int)Math.Floor(point.x), (int)Math.Floor(point.y));
            List<Vec2> list;
            if (!_joins.TryGetValue(key, out list)) _joins.Add(key, list = new List<Vec2>());
            list.Add(point);
        }
        internal bool AtJoin(Vec2 point)
        {
            int x = (int)Math.Floor(point.x), y = (int)Math.Floor(point.y);
            // Approved half-width is .8; tolerance covers captured float jitter.
            const double radius = .8002;
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
            {
                List<Vec2> list;
                if (!_joins.TryGetValue(Tuple.Create(x + dx, y + dy), out list)) continue;
                foreach (Vec2 p in list)
                    if ((double)(p.x - point.x) * (p.x - point.x) + (double)(p.y - point.y) * (p.y - point.y) <= radius * radius) return true;
            }
            return false;
        }
    }

    internal sealed class CoastHeightCache
    {
        private const double Tolerance = .00015;
        private readonly Dictionary<Tuple<int, int>, List<Tuple<Vec2, float>>> _values = new Dictionary<Tuple<int, int>, List<Tuple<Vec2, float>>>();
        private static Tuple<int, int> Key(Vec2 p) { return Tuple.Create((int)Math.Floor(p.x / Tolerance), (int)Math.Floor(p.y / Tolerance)); }
        internal bool TryGetValue(Vec2 p, out float value)
        {
            var key = Key(p);
            double nearest = Tolerance * Tolerance;
            bool found = false; value = 0;
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
            {
                List<Tuple<Vec2, float>> list;
                if (!_values.TryGetValue(Tuple.Create(key.Item1 + dx, key.Item2 + dy), out list)) continue;
                foreach (var item in list)
                {
                    double x = (double)p.x - item.Item1.x, y = (double)p.y - item.Item1.y, distance = x * x + y * y;
                    if (distance <= nearest) { nearest = distance; value = item.Item2; found = true; }
                }
            }
            return found;
        }
        internal void Add(Vec2 point, float value)
        {
            var key = Key(point);
            List<Tuple<Vec2, float>> list;
            if (!_values.TryGetValue(key, out list)) _values.Add(key, list = new List<Tuple<Vec2, float>>());
            list.Add(Tuple.Create(point, value));
        }
    }
}
