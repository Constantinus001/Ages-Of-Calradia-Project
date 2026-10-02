using System;
using System.Collections.Generic;

namespace AgesOfCalradia.PoliticalFillSeamFix
{
    // Axis-oriented boundary indexing matches the approved quadtree's cell
    // boundaries. Nearly axis-aligned edges are candidates, never blindly welded.
    internal sealed class SeamEdgeIndex
    {
        internal const double XyTolerance = 0.0001;
        internal const double HeightTolerance = 0.0001;
        private const long MaximumPairWork = 8000000;
        private readonly Dictionary<long, List<Edge>>[] _buckets =
            { new Dictionary<long, List<Edge>>(), new Dictionary<long, List<Edge>>() };

        internal sealed class Edge
        {
            internal int Triangle, Number, Axis, Side;
            internal long Bucket;
            internal SeamVertex First, Last;
            internal double Start, End;
            internal double Position(SeamVertex p) { return Axis == 0 ? p.Y : p.X; }
            internal double Cross(SeamVertex p) { return Axis == 0 ? p.X : p.Y; }
            internal double Fraction(double p) { return (p - Start) / (End - Start); }
            internal double CrossAt(double p) { return Cross(First) + Fraction(p) * (Cross(Last) - Cross(First)); }
            internal double HeightAt(double p) { return First.Z + Fraction(p) * ((double)Last.Z - First.Z); }
        }

        internal SeamEdgeIndex(IList<SeamTriangle> triangles)
        {
            for (int i = 0; i < triangles.Count; i++)
            {
                SeamTriangle t = triangles[i];
                Add(t.A, t.B, t.C, i, 0); Add(t.B, t.C, t.A, i, 1); Add(t.C, t.A, t.B, i, 2);
            }
            foreach (var axis in _buckets)
                foreach (List<Edge> edges in axis.Values) edges.Sort(CompareStart);
        }

        private void Add(SeamVertex a, SeamVertex b, SeamVertex third, int triangle, int number)
        {
            bool vertical = Math.Abs((double)a.X - b.X) <= XyTolerance;
            bool horizontal = Math.Abs((double)a.Y - b.Y) <= XyTolerance;
            if (vertical == horizontal) return;
            double line = vertical ? ((double)a.X + b.X) / 2 : ((double)a.Y + b.Y) / 2;
            double side = (vertical ? third.X : third.Y) - line;
            double first = vertical ? a.Y : a.X, last = vertical ? b.Y : b.X;
            if (Math.Abs(side) <= XyTolerance || Math.Abs(last - first) <= XyTolerance) return;
            var edge = new Edge { Triangle = triangle, Number = number, Axis = vertical ? 0 : 1,
                Side = Math.Sign(side), Bucket = (long)Math.Floor(line / XyTolerance),
                First = first < last ? a : b, Last = first < last ? b : a,
                Start = Math.Min(first, last), End = Math.Max(first, last) };
            List<Edge> edges;
            if (!_buckets[edge.Axis].TryGetValue(edge.Bucket, out edges))
            { edges = new List<Edge>(); _buckets[edge.Axis].Add(edge.Bucket, edges); }
            edges.Add(edge);
        }

        internal int VisitOppositeOverlaps(Action<Edge, Edge, double, double> visit)
        {
            long work = 0; int overlaps = 0;
            foreach (var axis in _buckets)
            foreach (var bucket in axis)
            {
                Scan(bucket.Value, false, visit, ref work, ref overlaps);
                for (int offset = 1; offset <= 2; offset++)
                {
                    List<Edge> neighbor;
                    if (!axis.TryGetValue(bucket.Key + offset, out neighbor)) continue;
                    var merged = new List<Edge>(bucket.Value.Count + neighbor.Count);
                    merged.AddRange(bucket.Value); merged.AddRange(neighbor); merged.Sort(CompareStart);
                    Scan(merged, true, visit, ref work, ref overlaps);
                }
            }
            return overlaps;
        }

        private static void Scan(List<Edge> edges, bool crossBucketsOnly,
            Action<Edge, Edge, double, double> visit, ref long work, ref int overlaps)
        {
            for (int i = 0; i < edges.Count; i++)
            for (int j = i + 1; j < edges.Count && edges[j].Start < edges[i].End; j++)
            {
                if (++work > MaximumPairWork) throw new SeamValidationException("Edge comparison budget exceeded.");
                Edge a = edges[i], b = edges[j];
                if (a.Triangle == b.Triangle || a.Side == b.Side || (crossBucketsOnly && a.Bucket == b.Bucket)) continue;
                double low = Math.Max(a.Start, b.Start), high = Math.Min(a.End, b.End);
                if (high - low <= XyTolerance || Math.Abs(a.CrossAt(low) - b.CrossAt(low)) > XyTolerance
                    || Math.Abs(a.CrossAt(high) - b.CrossAt(high)) > XyTolerance) continue;
                overlaps++; visit(a, b, low, high);
            }
        }
        private static int CompareStart(Edge a, Edge b)
        {
            int comparison = a.Start.CompareTo(b.Start);
            if (comparison != 0) return comparison;
            comparison = a.Triangle.CompareTo(b.Triangle);
            return comparison != 0 ? comparison : a.Number.CompareTo(b.Number);
        }
    }
}
