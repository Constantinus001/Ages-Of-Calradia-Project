using System;
using System.Collections.Generic;

namespace AgesOfCalradia.PoliticalFillSeamFix
{
    internal sealed class SeamValidationException : Exception
    { internal SeamValidationException(string message) : base(message) { } }

    // Pure CPU correction. Only existing vertices on an opposite-side shared
    // edge can be inserted; no new territory, ownership lookup or height query.
    internal static class FillSeamConformer
    {
        internal const int MaximumSourceTriangles = 262144;
        internal const int MaximumOutputTriangles = 1048576;
        private const int MaximumInsertionsPerTriangle = 48;
        private const double CollinearityTolerance = 0.00000001;

        internal static bool TryBuild(IList<SeamTriangle> source, out SeamBuildResult result, out string error)
        {
            result = null; error = null;
            try
            {
                ValidateInput(source);
                var insertions = new Dictionary<int, List<SeamVertex>>();
                var index = new SeamEdgeIndex(source);
                index.VisitOppositeOverlaps((a, b, low, high) =>
                {
                    AddInsertion(a, b.First, insertions); AddInsertion(a, b.Last, insertions);
                    AddInsertion(b, a.First, insertions); AddInsertion(b, a.Last, insertions);
                });
                var built = new SeamBuildResult { SourceCount = source.Count };
                for (int i = 0; i < source.Count; i++) Emit(source[i], i, insertions, built);
                built.ValidatedSharedEdgePairs = new SeamEdgeIndex(built.Triangles).VisitOppositeOverlaps((a, b, low, high) =>
                {
                    if (Math.Abs(a.HeightAt(low) - b.HeightAt(low)) > SeamEdgeIndex.HeightTolerance
                        || Math.Abs(a.HeightAt(high) - b.HeightAt(high)) > SeamEdgeIndex.HeightTolerance)
                        throw new SeamValidationException("Output contains a mismatched shared axis edge.");
                });
                result = built; return true;
            }
            catch (SeamValidationException ex) { error = ex.Message; return false; }
        }

        private static void ValidateInput(IList<SeamTriangle> triangles)
        {
            if (triangles == null || triangles.Count == 0 || triangles.Count > MaximumSourceTriangles)
                throw new SeamValidationException("Missing or oversized logical fill capture.");
            var ids = new HashSet<int>(); var heights = new Dictionary<XyKey, float>();
            foreach (SeamTriangle t in triangles)
            {
                if (!ids.Add(t.ParentId)) throw new SeamValidationException("Duplicate original triangle identity.");
                ValidateVertex(t.A, heights); ValidateVertex(t.B, heights); ValidateVertex(t.C, heights);
                if (Math.Abs(Area(t.A, t.B, t.C)) <= 0.00000001)
                    throw new SeamValidationException("Degenerate source triangle.");
            }
        }

        private static void ValidateVertex(SeamVertex point, Dictionary<XyKey, float> heights)
        {
            if (!Finite(point.X) || !Finite(point.Y) || !Finite(point.Z) || !Finite(point.U) || !Finite(point.V)
                || Math.Abs(point.X) > 100000 || Math.Abs(point.Y) > 100000 || Math.Abs(point.Z) > 100000)
                throw new SeamValidationException("Invalid source coordinate or UV.");
            var key = new XyKey(point); float previous;
            if (heights.TryGetValue(key, out previous))
            {
                if (Math.Abs((double)previous - point.Z) > SeamEdgeIndex.HeightTolerance)
                    throw new SeamValidationException("Existing vertices disagree in height at identical XY.");
            }
            else heights.Add(key, point.Z);
        }

        private static void AddInsertion(SeamEdgeIndex.Edge edge, SeamVertex candidate,
            Dictionary<int, List<SeamVertex>> insertions)
        {
            double position = edge.Position(candidate);
            if (position - edge.Start <= SeamEdgeIndex.XyTolerance || edge.End - position <= SeamEdgeIndex.XyTolerance) return;
            // Candidate tolerance finds near-axis relationships; exact-footprint
            // correction rejects ambiguously offset lines rather than snapping XY.
            if (Math.Abs(edge.CrossAt(position) - edge.Cross(candidate)) > CollinearityTolerance)
                throw new SeamValidationException("Near-axis candidate would move the original XY footprint.");
            double amount = edge.Fraction(position);
            var point = new SeamVertex(candidate.X, candidate.Y, candidate.Z,
                (float)(edge.First.U + amount * ((double)edge.Last.U - edge.First.U)),
                (float)(edge.First.V + amount * ((double)edge.Last.V - edge.First.V)));
            int key = edge.Triangle * 3 + edge.Number; List<SeamVertex> points;
            if (!insertions.TryGetValue(key, out points)) { points = new List<SeamVertex>(); insertions.Add(key, points); }
            foreach (SeamVertex existing in points)
            {
                if (!new XyKey(existing).Equals(new XyKey(point))) continue;
                if (Math.Abs((double)existing.Z - point.Z) > SeamEdgeIndex.HeightTolerance)
                    throw new SeamValidationException("Conflicting fine vertices on an edge.");
                return;
            }
            if (points.Count >= MaximumInsertionsPerTriangle)
                throw new SeamValidationException("Unexpectedly dense edge subdivisions.");
            points.Add(point);
        }

        private static void Emit(SeamTriangle source, int index, Dictionary<int, List<SeamVertex>> insertions,
            SeamBuildResult result)
        {
            if (!insertions.ContainsKey(index * 3) && !insertions.ContainsKey(index * 3 + 1)
                && !insertions.ContainsKey(index * 3 + 2))
            { AddOutput(source, result); return; }
            var boundary = new List<SeamVertex>();
            AppendEdge(source.A, source.B, index * 3, insertions, boundary, result);
            AppendEdge(source.B, source.C, index * 3 + 1, insertions, boundary, result);
            AppendEdge(source.C, source.A, index * 3 + 2, insertions, boundary, result);
            if (boundary.Count - 3 > MaximumInsertionsPerTriangle)
                throw new SeamValidationException("Unexpectedly dense triangle subdivisions.");
            SeamVertex center = Mean(source.A, source.B, source.C);
            double expectedArea = Area(source.A, source.B, source.C), actualArea = 0;
            for (int i = 0; i < boundary.Count; i++)
            {
                SeamVertex a = boundary[i], b = boundary[(i + 1) % boundary.Count];
                double area = Area(center, a, b);
                if (area * expectedArea <= 0 || Math.Abs(area) <= 0.00000001)
                    throw new SeamValidationException("Retriangulation would invert or collapse a face.");
                actualArea += area;
                AddOutput(new SeamTriangle(source.ParentId, source.RowId, source.Color, center, a, b), result);
            }
            if (Math.Abs(actualArea - expectedArea) > Math.Max(0.000001, Math.Abs(expectedArea) * 0.000001))
                throw new SeamValidationException("Retriangulation changed the original XY footprint area.");
            result.ChangedParentCount++; result.InsertedBoundaryVertices += boundary.Count - 3;
        }

        private static void AppendEdge(SeamVertex a, SeamVertex b, int key,
            Dictionary<int, List<SeamVertex>> insertions, List<SeamVertex> boundary, SeamBuildResult result)
        {
            boundary.Add(a); List<SeamVertex> points;
            if (!insertions.TryGetValue(key, out points)) return;
            double dx = (double)b.X - a.X, dy = (double)b.Y - a.Y, length = dx * dx + dy * dy;
            points.Sort((p, q) => Projection(a, p, dx, dy).CompareTo(Projection(a, q, dx, dy)));
            foreach (SeamVertex p in points)
            {
                double fraction = Projection(a, p, dx, dy) / length;
                result.MaximumHeightCorrection = Math.Max(result.MaximumHeightCorrection,
                    (float)Math.Abs(p.Z - (a.Z + fraction * ((double)b.Z - a.Z))));
                boundary.Add(p);
            }
        }

        private static double Projection(SeamVertex origin, SeamVertex p, double dx, double dy)
        { return ((double)p.X - origin.X) * dx + ((double)p.Y - origin.Y) * dy; }
        private static void AddOutput(SeamTriangle triangle, SeamBuildResult result)
        {
            if (result.Triangles.Count >= MaximumOutputTriangles)
                throw new SeamValidationException("Corrected fill exceeds triangle budget.");
            result.Triangles.Add(triangle);
        }
        private static SeamVertex Mean(SeamVertex a, SeamVertex b, SeamVertex c)
        { return new SeamVertex((float)(((double)a.X + b.X + c.X) / 3), (float)(((double)a.Y + b.Y + c.Y) / 3),
            (float)(((double)a.Z + b.Z + c.Z) / 3), (float)(((double)a.U + b.U + c.U) / 3),
            (float)(((double)a.V + b.V + c.V) / 3)); }
        internal static double Area(SeamVertex a, SeamVertex b, SeamVertex c)
        { return (((double)b.X - a.X) * ((double)c.Y - a.Y) - ((double)b.Y - a.Y) * ((double)c.X - a.X)) / 2; }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }

        private readonly struct XyKey : IEquatable<XyKey>
        {
            private readonly float _x, _y;
            internal XyKey(SeamVertex p) { _x = p.X; _y = p.Y; }
            public bool Equals(XyKey other) { return _x == other._x && _y == other._y; }
            public override bool Equals(object obj) { return obj is XyKey && Equals((XyKey)obj); }
            public override int GetHashCode() { unchecked { return _x.GetHashCode() * 397 ^ _y.GetHashCode(); } }
        }
    }
}
