using System;
using System.Collections.Generic;

namespace AgesOfCalradia.PoliticalRenderDiagnostics
{
    internal struct Point3
    {
        internal readonly float X, Y, Z;
        internal Point3(float x, float y, float z) { X = x; Y = y; Z = z; }
    }

    internal enum ClearanceState { Unknown, Below, Clear }

    internal struct SurfaceSample
    {
        internal readonly bool Known;
        internal readonly float Height;
        private SurfaceSample(bool known, float height) { Known = known; Height = height; }
        internal static SurfaceSample FromQuery(bool succeeded, float height,
            float minimum, float maximum, float? noSurfaceSentinel = null)
        {
            if (!RenderProbeMath.IsFinite(minimum) || !RenderProbeMath.IsFinite(maximum)
                || minimum > maximum) throw new ArgumentOutOfRangeException("minimum");
            if (noSurfaceSentinel.HasValue && !RenderProbeMath.IsFinite(noSurfaceSentinel.Value))
                throw new ArgumentOutOfRangeException("noSurfaceSentinel");
            bool known = succeeded && RenderProbeMath.IsFinite(height)
                && height >= minimum && height <= maximum
                && (!noSurfaceSentinel.HasValue || height != noSurfaceSentinel.Value);
            return new SurfaceSample(known, known ? height : float.NaN);
        }
    }

    internal static class RenderProbeMath
    {
        internal static bool IsFinite(float value)
        { return !float.IsNaN(value) && !float.IsInfinity(value); }
        internal static Point3 Midpoint(Point3 first, Point3 second)
        { return new Point3((float)(((double)first.X + second.X) / 2),
            (float)(((double)first.Y + second.Y) / 2), (float)(((double)first.Z + second.Z) / 2)); }
        internal static Point3 Barycenter(Point3 first, Point3 second, Point3 third)
        { return new Point3((float)(((double)first.X + second.X + third.X) / 3),
            (float)(((double)first.Y + second.Y + third.Y) / 3),
            (float)(((double)first.Z + second.Z + third.Z) / 3)); }
        internal static float FrontierWorldZ(float rawZ, float alpha)
        { return IsFinite(rawZ) && IsFinite(alpha)
            ? rawZ - 4.65f * (1f - Math.Max(0f, Math.Min(1f, alpha))) : float.NaN; }
        internal static ClearanceState ClassifyClearance(float worldZ, SurfaceSample surface,
            float tolerance = 0.001f)
        {
            if (!IsFinite(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException("tolerance");
            if (!IsFinite(worldZ) || !surface.Known) return ClearanceState.Unknown;
            return (double)worldZ - surface.Height < -tolerance
                ? ClearanceState.Below : ClearanceState.Clear;
        }
    }

    // Quantization is diagnostic bucketing, not a geometric weld. Coordinates and
    // segments are never modified. A cell center represents at most Step/2 error
    // per axis; adjacent buckets are not merged, even when their points are close.
    internal sealed class EndpointGraph
    {
        private readonly Dictionary<NodeKey, int> _degrees = new Dictionary<NodeKey, int>();
        internal readonly float Step;
        internal int SegmentCount { get; private set; }
        internal int CollapsedBucketSegmentCount { get; private set; }
        internal int NodeCount { get { return _degrees.Count; } }
        internal EndpointGraph(float quantizationStep = 0.001f)
        {
            if (!RenderProbeMath.IsFinite(quantizationStep) || quantizationStep <= 0)
                throw new ArgumentOutOfRangeException("quantizationStep");
            Step = quantizationStep;
        }
        internal NodeKey Key(Point3 point)
        { return new NodeKey(Quantize(point.X), Quantize(point.Y)); }
        private long Quantize(float value)
        {
            double bucket = Math.Round((double)value / Step, MidpointRounding.AwayFromZero);
            if (!RenderProbeMath.IsFinite(value) || bucket <= long.MinValue || bucket >= long.MaxValue)
                throw new ArgumentOutOfRangeException("value");
            return (long)bucket;
        }
        internal Point3 CellCenter(NodeKey key)
        { return new Point3((float)(key.X * (double)Step), (float)(key.Y * (double)Step), float.NaN); }
        internal void AddSegment(Point3 first, Point3 second)
        {
            NodeKey a = Key(first), b = Key(second);
            if (a.Equals(b)) CollapsedBucketSegmentCount++;
            AddIncident(a); AddIncident(b); SegmentCount++;
        }
        private void AddIncident(NodeKey key)
        { int value; _degrees.TryGetValue(key, out value); _degrees[key] = value + 1; }
        internal int CountDegree(int degree)
        { int count = 0; foreach (int value in _degrees.Values) if (value == degree) count++; return count; }
        internal int CountUnexpectedEndpoints(IEnumerable<Point3> intentionalEndpoints)
        {
            var expected = new HashSet<NodeKey>();
            if (intentionalEndpoints != null)
                foreach (Point3 point in intentionalEndpoints) expected.Add(Key(point));
            int count = 0;
            foreach (KeyValuePair<NodeKey, int> entry in _degrees)
                if (entry.Value == 1 && !expected.Contains(entry.Key)) count++;
            return count;
        }
        internal struct NodeKey : IEquatable<NodeKey>
        {
            internal readonly long X, Y;
            internal NodeKey(long x, long y) { X = x; Y = y; }
            public bool Equals(NodeKey other) { return X == other.X && Y == other.Y; }
            public override bool Equals(object obj) { return obj is NodeKey && Equals((NodeKey)obj); }
            public override int GetHashCode() { unchecked { return X.GetHashCode() * 397 ^ Y.GetHashCode(); } }
        }
    }
}
