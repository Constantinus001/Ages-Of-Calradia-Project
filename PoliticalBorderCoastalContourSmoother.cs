using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace AgesOfCalradia.PoliticalBorderOptimizer
{
    /// <summary>
    /// Extracts the exposed fill boundary near protected coastal segments,
    /// smooths only degree-two boundary nodes within a bounded displacement,
    /// and supplies that same contour to the coastal ribbon consumer.
    /// </summary>
    internal static class PoliticalBorderCoastalContourSmoother
    {
        private const float CoastalSearchDistance = 3f;
        private const float BoundaryProbeDistance = 0.25f;
        private const float MaximumNodeDisplacement = 0.28f;
        private const float RibbonSnapDistance = 1.75f;
        private const float MaximumRibbonCoastCorrection = CoastalSearchDistance;
        private const float MinimumCorrectedSegmentLength = 0.15f;
        private const float MinimumSignedArea = 0.0001f;
        private const int SmoothingPasses = 4;
        private const float PositiveFactor = 0.45f;
        private const float NegativeFactor = -0.47f;

        internal static bool TryApply(
            List<PoliticalBorderGeometryCache.EntityGeometry> fill,
            IList<PoliticalBorderGeometryCache.RawSegment> capturedSegments,
            PoliticalGeometryDiagnosticsRecord diagnostics,
            out List<PoliticalBorderGeometryCache.RawSegment> ribbonSegments)
        {
            ribbonSegments = new List<PoliticalBorderGeometryCache.RawSegment>(capturedSegments);
            CoastalIndex coast = CoastalIndex.Build(capturedSegments);
            Dictionary<EdgeKey, EdgeRecord> records = BuildBoundaryRecords(fill, coast);
            List<Edge> edges;
            Dictionary<PoliticalBorderGeometryCache.NodeKey, Node> nodes;
            BuildGraph(records, out edges, out nodes);
            if (edges.Count == 0 || nodes.Count == 0) return false;

            int moved = Smooth(nodes, edges);
            int rejected = RejectUnsafeMoves(fill, nodes);
            int adjustedVertices = ApplyToFill(fill, nodes);
            int coastCorrectedNodes;
            int collapseRejectedNodes;
            float maximumCoastCorrection;
            float totalCoastCorrection;
            ribbonSegments = BuildRibbonSegments(
                capturedSegments, nodes, edges, out coastCorrectedNodes,
                out collapseRejectedNodes, out maximumCoastCorrection,
                out totalCoastCorrection);

            float totalDisplacement = 0f;
            float maximumDisplacement = 0f;
            foreach (Node node in nodes.Values)
            {
                float displacement = (node.Position - node.Original).Length;
                if (displacement <= 0.0001f) continue;
                totalDisplacement += displacement;
                maximumDisplacement = Math.Max(maximumDisplacement, displacement);
            }
            diagnostics.CoastalContourBoundaryEdgeCount = edges.Count;
            diagnostics.CoastalContourBoundaryNodeCount = nodes.Count;
            diagnostics.CoastalContourMovedNodeCount = moved - rejected;
            diagnostics.CoastalContourRejectedNodeCount = rejected;
            diagnostics.CoastalContourAdjustedFillVertexCount = adjustedVertices;
            diagnostics.CoastalContourRibbonSegmentCount = ribbonSegments.Count;
            diagnostics.CoastalContourMaximumDisplacement = maximumDisplacement;
            diagnostics.CoastalContourTotalDisplacement = totalDisplacement;
            BorderOptimizerDiagnostics.Info(
                "Shared coastal fill/ribbon contour applied: boundaryEdges=" + edges.Count
                + "; boundaryNodes=" + nodes.Count
                + "; movedNodes=" + (moved - rejected)
                + "; rejectedUnsafeNodes=" + rejected
                + "; adjustedFillVertices=" + adjustedVertices
                + "; ribbonInputSegments=" + ribbonSegments.Count
                + "; maximumDisplacement=" + maximumDisplacement.ToString("F3")
                + "; coastCorrectedNodes=" + coastCorrectedNodes
                + "; collapseRejectedNodes=" + collapseRejectedNodes
                + "; maximumCoastCorrection=" + maximumCoastCorrection.ToString("F3")
                + "; totalCoastCorrection=" + totalCoastCorrection.ToString("F3")
                + "; islandExclusionPreserved=true.");
            return adjustedVertices > 0;
        }

        private static Dictionary<EdgeKey, EdgeRecord> BuildBoundaryRecords(
            IList<PoliticalBorderGeometryCache.EntityGeometry> fill,
            CoastalIndex coast)
        {
            Dictionary<EdgeKey, EdgeRecord> result = new Dictionary<EdgeKey, EdgeRecord>();
            foreach (PoliticalBorderGeometryCache.EntityGeometry entity in fill)
            foreach (PoliticalBorderGeometryCache.Triangle triangle in entity.Triangles)
            {
                AddEdge(result, coast, triangle.First, triangle.Second, triangle.Color);
                AddEdge(result, coast, triangle.Second, triangle.Third, triangle.Color);
                AddEdge(result, coast, triangle.Third, triangle.First, triangle.Color);
            }
            return result;
        }

        private static void AddEdge(
            IDictionary<EdgeKey, EdgeRecord> records,
            CoastalIndex coast,
            Vec3 first,
            Vec3 second,
            uint color)
        {
            Vec2 midpoint = new Vec2(
                (first.x + second.x) * 0.5f,
                (first.y + second.y) * 0.5f);
            if (!coast.IsNear(midpoint, CoastalSearchDistance)) return;
            EdgeKey key = new EdgeKey(first, second);
            EdgeRecord record;
            if (!records.TryGetValue(key, out record))
            {
                record = new EdgeRecord(first, second, color);
                records.Add(key, record);
            }
            record.Count++;
        }

        private static void BuildGraph(
            IDictionary<EdgeKey, EdgeRecord> records,
            out List<Edge> edges,
            out Dictionary<PoliticalBorderGeometryCache.NodeKey, Node> nodes)
        {
            edges = new List<Edge>();
            nodes = new Dictionary<PoliticalBorderGeometryCache.NodeKey, Node>();
            foreach (EdgeRecord record in records.Values)
            {
                if (record.Count != 1) continue;
                Node first = GetNode(nodes, record.First);
                Node second = GetNode(nodes, record.Second);
                if (first.Key.Equals(second.Key)) continue;
                int edgeIndex = edges.Count;
                edges.Add(new Edge(first, second, record.Color));
                first.Incidents.Add(edgeIndex);
                second.Incidents.Add(edgeIndex);
            }
        }

        private static Node GetNode(
            IDictionary<PoliticalBorderGeometryCache.NodeKey, Node> nodes,
            Vec3 point)
        {
            PoliticalBorderGeometryCache.NodeKey key = new PoliticalBorderGeometryCache.NodeKey(
                new Vec2(point.x, point.y));
            Node node;
            if (!nodes.TryGetValue(key, out node))
            {
                node = new Node(key, point);
                nodes.Add(key, node);
            }
            else
            {
                node.HeightTotal += point.z;
                node.HeightSamples++;
            }
            return node;
        }

        private static int Smooth(
            IDictionary<PoliticalBorderGeometryCache.NodeKey, Node> nodes,
            IList<Edge> edges)
        {
            for (int pass = 0; pass < SmoothingPasses; pass++)
            {
                float factor = (pass & 1) == 0 ? PositiveFactor : NegativeFactor;
                Dictionary<Node, Vec2> next = new Dictionary<Node, Vec2>();
                foreach (Node node in nodes.Values)
                {
                    if (node.Incidents.Count != 2) continue;
                    Node previous = edges[node.Incidents[0]].Other(node);
                    Node following = edges[node.Incidents[1]].Other(node);
                    Vec2 average = (previous.Position + following.Position) * 0.5f;
                    Vec2 candidate = Clamp(
                        node.Original,
                        node.Position + (average - node.Position) * factor,
                        MaximumNodeDisplacement);
                    Vec2 tangent = following.Position - previous.Position;
                    if (IsProtectedBoundary(candidate, tangent)) next[node] = candidate;
                }
                foreach (KeyValuePair<Node, Vec2> value in next)
                    value.Key.Position = value.Value;
            }

            int moved = 0;
            foreach (Node node in nodes.Values)
                if ((node.Position - node.Original).Length > 0.0001f) moved++;
            return moved;
        }

        private static bool IsProtectedBoundary(Vec2 point, Vec2 tangent)
        {
            if (tangent.Normalize() < 0.001f) return false;
            Vec2 normal = new Vec2(-tangent.y, tangent.x);
            bool left;
            bool right;
            return BorderOptimizerRuntime.TryClassifyFrontierLand(
                    point + normal * BoundaryProbeDistance, out left)
                && BorderOptimizerRuntime.TryClassifyFrontierLand(
                    point - normal * BoundaryProbeDistance, out right)
                && left != right;
        }

        private static Vec2 Clamp(Vec2 origin, Vec2 candidate, float maximum)
        {
            Vec2 displacement = candidate - origin;
            float length = displacement.Normalize();
            return length <= maximum ? candidate : origin + displacement * maximum;
        }

        private static int RejectUnsafeMoves(
            IList<PoliticalBorderGeometryCache.EntityGeometry> fill,
            IDictionary<PoliticalBorderGeometryCache.NodeKey, Node> nodes)
        {
            HashSet<PoliticalBorderGeometryCache.NodeKey> rejected =
                new HashSet<PoliticalBorderGeometryCache.NodeKey>();
            bool changed;
            do
            {
                changed = false;
                foreach (PoliticalBorderGeometryCache.EntityGeometry entity in fill)
                foreach (PoliticalBorderGeometryCache.Triangle triangle in entity.Triangles)
                {
                    Vec2 first = MovedPoint(triangle.First, nodes, rejected);
                    Vec2 second = MovedPoint(triangle.Second, nodes, rejected);
                    Vec2 third = MovedPoint(triangle.Third, nodes, rejected);
                    if (SignedArea(first, second, third) > MinimumSignedArea) continue;
                    changed |= RejectMoved(triangle.First, nodes, rejected);
                    changed |= RejectMoved(triangle.Second, nodes, rejected);
                    changed |= RejectMoved(triangle.Third, nodes, rejected);
                }
            }
            while (changed);

            foreach (PoliticalBorderGeometryCache.NodeKey key in rejected)
            {
                Node node;
                if (nodes.TryGetValue(key, out node)) node.Position = node.Original;
            }
            return rejected.Count;
        }

        private static Vec2 MovedPoint(
            Vec3 point,
            IDictionary<PoliticalBorderGeometryCache.NodeKey, Node> nodes,
            ISet<PoliticalBorderGeometryCache.NodeKey> rejected)
        {
            PoliticalBorderGeometryCache.NodeKey key = Key(point);
            Node node;
            return !rejected.Contains(key) && nodes.TryGetValue(key, out node)
                ? node.Position : new Vec2(point.x, point.y);
        }

        private static bool RejectMoved(
            Vec3 point,
            IDictionary<PoliticalBorderGeometryCache.NodeKey, Node> nodes,
            ISet<PoliticalBorderGeometryCache.NodeKey> rejected)
        {
            PoliticalBorderGeometryCache.NodeKey key = Key(point);
            Node node;
            return nodes.TryGetValue(key, out node)
                && (node.Position - node.Original).Length > 0.0001f
                && rejected.Add(key);
        }

        private static int ApplyToFill(
            IList<PoliticalBorderGeometryCache.EntityGeometry> fill,
            IDictionary<PoliticalBorderGeometryCache.NodeKey, Node> nodes)
        {
            int adjusted = 0;
            foreach (PoliticalBorderGeometryCache.EntityGeometry entity in fill)
            {
                for (int index = 0; index < entity.Triangles.Count; index++)
                {
                    PoliticalBorderGeometryCache.Triangle triangle = entity.Triangles[index];
                    triangle.First = Move(triangle.First, nodes, ref adjusted);
                    triangle.Second = Move(triangle.Second, nodes, ref adjusted);
                    triangle.Third = Move(triangle.Third, nodes, ref adjusted);
                    entity.Triangles[index] = triangle;
                }
            }
            return adjusted;
        }

        private static Vec3 Move(
            Vec3 point,
            IDictionary<PoliticalBorderGeometryCache.NodeKey, Node> nodes,
            ref int adjusted)
        {
            Node node;
            if (!nodes.TryGetValue(Key(point), out node)
                || (node.Position - node.Original).Length <= 0.0001f) return point;
            adjusted++;
            float originalTerrain;
            float movedTerrain;
            float height = node.Height;
            if (BorderOptimizerRuntime.TrySamplePreparedHeight(node.Original, out originalTerrain)
                && BorderOptimizerRuntime.TrySamplePreparedHeight(node.Position, out movedTerrain))
                height = movedTerrain + (node.Height - originalTerrain);
            return new Vec3(node.Position.x, node.Position.y, height, -1f);
        }

        private static List<PoliticalBorderGeometryCache.RawSegment> BuildRibbonSegments(
            IList<PoliticalBorderGeometryCache.RawSegment> captured,
            IDictionary<PoliticalBorderGeometryCache.NodeKey, Node> nodes,
            IList<Edge> edges,
            out int coastCorrectedNodes,
            out int collapseRejectedNodes,
            out float maximumCoastCorrection,
            out float totalCoastCorrection)
        {
            NodeIndex index = new NodeIndex(nodes.Values);
            BoundaryIndex boundary = new BoundaryIndex(edges);
            Dictionary<PoliticalBorderGeometryCache.NodeKey, Vec2> snapped =
                new Dictionary<PoliticalBorderGeometryCache.NodeKey, Vec2>();
            coastCorrectedNodes = 0;
            collapseRejectedNodes = 0;
            maximumCoastCorrection = 0f;
            totalCoastCorrection = 0f;
            foreach (PoliticalBorderGeometryCache.RawSegment segment in captured)
            {
                if (!PoliticalBorderChainBuilder.IsCoastal(segment)) continue;
                Snap(segment.First, index, boundary, snapped,
                    ref coastCorrectedNodes, ref maximumCoastCorrection,
                    ref totalCoastCorrection);
                Snap(segment.Second, index, boundary, snapped,
                    ref coastCorrectedNodes, ref maximumCoastCorrection,
                    ref totalCoastCorrection);
            }
            collapseRejectedNodes = RejectCollapsingCorrections(captured, snapped);
            List<PoliticalBorderGeometryCache.RawSegment> result =
                new List<PoliticalBorderGeometryCache.RawSegment>(captured.Count);
            foreach (PoliticalBorderGeometryCache.RawSegment segment in captured)
            {
                if (!PoliticalBorderChainBuilder.IsCoastal(segment))
                {
                    result.Add(segment);
                    continue;
                }
                Vec2 first = snapped[new PoliticalBorderGeometryCache.NodeKey(segment.First)];
                Vec2 second = snapped[new PoliticalBorderGeometryCache.NodeKey(segment.Second)];
                if ((second - first).Length < 0.001f)
                {
                    result.Add(segment);
                    continue;
                }
                result.Add(new PoliticalBorderGeometryCache.RawSegment(
                    first, second,
                    new Vec3(first.x, first.y, segment.FirstTerrainPoint.z, -1f),
                    new Vec3(second.x, second.y, segment.SecondTerrainPoint.z, -1f),
                    segment.LeftColor, segment.RightColor, segment.Style));
            }
            return result;
        }

        private static int RejectCollapsingCorrections(
            IList<PoliticalBorderGeometryCache.RawSegment> segments,
            IDictionary<PoliticalBorderGeometryCache.NodeKey, Vec2> snapped)
        {
            HashSet<PoliticalBorderGeometryCache.NodeKey> rejected =
                new HashSet<PoliticalBorderGeometryCache.NodeKey>();
            bool changed;
            do
            {
                changed = false;
                foreach (PoliticalBorderGeometryCache.RawSegment segment in segments)
                {
                    if (!PoliticalBorderChainBuilder.IsCoastal(segment)) continue;
                    PoliticalBorderGeometryCache.NodeKey firstKey =
                        new PoliticalBorderGeometryCache.NodeKey(segment.First);
                    PoliticalBorderGeometryCache.NodeKey secondKey =
                        new PoliticalBorderGeometryCache.NodeKey(segment.Second);
                    Vec2 correctedFirst = snapped[firstKey];
                    Vec2 correctedSecond = snapped[secondKey];
                    float sourceLength = (segment.Second - segment.First).Length;
                    float minimumLength = Math.Min(
                        MinimumCorrectedSegmentLength, sourceLength * 0.5f);
                    if ((correctedSecond - correctedFirst).Length >= minimumLength)
                        continue;
                    if ((correctedFirst - segment.First).Length > 0.0001f)
                    {
                        snapped[firstKey] = segment.First;
                        changed |= rejected.Add(firstKey);
                    }
                    if ((correctedSecond - segment.Second).Length > 0.0001f)
                    {
                        snapped[secondKey] = segment.Second;
                        changed |= rejected.Add(secondKey);
                    }
                }
            }
            while (changed);
            return rejected.Count;
        }

        private static Vec2 Snap(
            Vec2 source,
            NodeIndex index,
            BoundaryIndex boundary,
            IDictionary<PoliticalBorderGeometryCache.NodeKey, Vec2> snapped,
            ref int coastCorrectedNodes,
            ref float maximumCoastCorrection,
            ref float totalCoastCorrection)
        {
            PoliticalBorderGeometryCache.NodeKey key =
                new PoliticalBorderGeometryCache.NodeKey(source);
            Vec2 result;
            if (snapped.TryGetValue(key, out result)) return result;
            Vec2 displaced = index.ApplyNearestDisplacement(source, RibbonSnapDistance);
            float correction;
            result = boundary.MoveTowardNearest(
                displaced, CoastalSearchDistance, MaximumRibbonCoastCorrection,
                out correction);
            if (correction > 0.0001f)
            {
                coastCorrectedNodes++;
                totalCoastCorrection += correction;
                maximumCoastCorrection = Math.Max(maximumCoastCorrection, correction);
            }
            snapped.Add(key, result);
            return result;
        }

        private static PoliticalBorderGeometryCache.NodeKey Key(Vec3 point)
        { return new PoliticalBorderGeometryCache.NodeKey(new Vec2(point.x, point.y)); }

        private static float SignedArea(Vec2 first, Vec2 second, Vec2 third)
        {
            return (second.x - first.x) * (third.y - first.y)
                - (second.y - first.y) * (third.x - first.x);
        }

        private sealed class Node
        {
            internal Node(PoliticalBorderGeometryCache.NodeKey key, Vec3 point)
            {
                Key = key;
                Original = new Vec2(point.x, point.y);
                Position = Original;
                HeightTotal = point.z;
                HeightSamples = 1;
            }
            internal readonly PoliticalBorderGeometryCache.NodeKey Key;
            internal readonly Vec2 Original;
            internal Vec2 Position;
            internal float HeightTotal;
            internal int HeightSamples;
            internal float Height { get { return HeightTotal / HeightSamples; } }
            internal readonly List<int> Incidents = new List<int>();
        }

        private sealed class Edge
        {
            internal Edge(Node first, Node second, uint color)
            { First = first; Second = second; Color = color; }
            internal readonly Node First, Second;
            internal readonly uint Color;
            internal Node Other(Node node) { return node == First ? Second : First; }
        }

        private sealed class EdgeRecord
        {
            internal EdgeRecord(Vec3 first, Vec3 second, uint color)
            { First = first; Second = second; Color = color; }
            internal readonly Vec3 First, Second;
            internal readonly uint Color;
            internal int Count;
        }

        private struct EdgeKey : IEquatable<EdgeKey>
        {
            internal EdgeKey(Vec3 first, Vec3 second)
            {
                PoliticalBorderGeometryCache.NodeKey firstKey = Key(first);
                PoliticalBorderGeometryCache.NodeKey secondKey = Key(second);
                if (firstKey.CompareTo(secondKey) <= 0)
                { First = firstKey; Second = secondKey; }
                else
                { First = secondKey; Second = firstKey; }
            }
            private PoliticalBorderGeometryCache.NodeKey First, Second;
            public bool Equals(EdgeKey other)
            { return First.Equals(other.First) && Second.Equals(other.Second); }
            public override bool Equals(object obj)
            { return obj is EdgeKey && Equals((EdgeKey)obj); }
            public override int GetHashCode()
            { unchecked { return (First.GetHashCode() * 397) ^ Second.GetHashCode(); } }
        }

        private sealed class NodeIndex
        {
            private const float BucketSize = 4f;
            private readonly Dictionary<BucketKey, List<Node>> _buckets =
                new Dictionary<BucketKey, List<Node>>();
            internal NodeIndex(IEnumerable<Node> nodes)
            {
                foreach (Node node in nodes)
                {
                    BucketKey key = BucketKey.From(node.Position, BucketSize);
                    List<Node> values;
                    if (!_buckets.TryGetValue(key, out values))
                    { values = new List<Node>(); _buckets.Add(key, values); }
                    values.Add(node);
                }
            }
            internal Vec2 ApplyNearestDisplacement(Vec2 point, float maximum)
            {
                Node best = null;
                float bestSquared = maximum * maximum;
                int radius = (int)Math.Ceiling(maximum / BucketSize);
                BucketKey center = BucketKey.From(point, BucketSize);
                for (int x = -radius; x <= radius; x++)
                for (int y = -radius; y <= radius; y++)
                {
                    List<Node> values;
                    if (!_buckets.TryGetValue(center.Offset(x, y), out values)) continue;
                    foreach (Node node in values)
                    {
                        float squared = (node.Position - point).LengthSquared;
                        if (squared >= bestSquared) continue;
                        best = node;
                        bestSquared = squared;
                    }
                }
                return best == null ? point : point + (best.Position - best.Original);
            }
        }

        /// <summary>
        /// Finds the nearest point on the already validated, smoothed fill
        /// boundary. The protected search radius bounds candidate selection;
        /// an accepted coastal node projects completely onto that boundary.
        /// </summary>
        private sealed class BoundaryIndex
        {
            private const float BucketSize = 4f;
            private readonly Dictionary<BucketKey, List<Edge>> _buckets =
                new Dictionary<BucketKey, List<Edge>>();

            internal BoundaryIndex(IEnumerable<Edge> edges)
            {
                foreach (Edge edge in edges) Add(edge);
            }

            private void Add(Edge edge)
            {
                int minimumX = (int)Math.Floor(
                    Math.Min(edge.First.Position.x, edge.Second.Position.x) / BucketSize);
                int maximumX = (int)Math.Floor(
                    Math.Max(edge.First.Position.x, edge.Second.Position.x) / BucketSize);
                int minimumY = (int)Math.Floor(
                    Math.Min(edge.First.Position.y, edge.Second.Position.y) / BucketSize);
                int maximumY = (int)Math.Floor(
                    Math.Max(edge.First.Position.y, edge.Second.Position.y) / BucketSize);
                for (int x = minimumX; x <= maximumX; x++)
                for (int y = minimumY; y <= maximumY; y++)
                {
                    BucketKey key = new BucketKey(x, y);
                    List<Edge> values;
                    if (!_buckets.TryGetValue(key, out values))
                    {
                        values = new List<Edge>();
                        _buckets.Add(key, values);
                    }
                    values.Add(edge);
                }
            }

            internal Vec2 MoveTowardNearest(
                Vec2 point,
                float searchDistance,
                float maximumCorrection,
                out float correction)
            {
                Vec2 nearest = point;
                float bestSquared = searchDistance * searchDistance;
                int radius = (int)Math.Ceiling(searchDistance / BucketSize);
                BucketKey center = BucketKey.From(point, BucketSize);
                for (int x = -radius; x <= radius; x++)
                for (int y = -radius; y <= radius; y++)
                {
                    List<Edge> values;
                    if (!_buckets.TryGetValue(center.Offset(x, y), out values)) continue;
                    foreach (Edge edge in values)
                    {
                        Vec2 candidate = ClosestPoint(
                            point, edge.First.Position, edge.Second.Position);
                        float squared = (candidate - point).LengthSquared;
                        if (squared >= bestSquared) continue;
                        nearest = candidate;
                        bestSquared = squared;
                    }
                }

                Vec2 displacement = nearest - point;
                float distance = displacement.Normalize();
                if (distance <= 0.0001f)
                {
                    correction = 0f;
                    return point;
                }
                correction = Math.Min(distance, maximumCorrection);
                return point + displacement * correction;
            }

            private static Vec2 ClosestPoint(Vec2 point, Vec2 first, Vec2 second)
            {
                Vec2 edge = second - first;
                float lengthSquared = edge.LengthSquared;
                float amount = lengthSquared <= 0.000001f ? 0f
                    : Math.Max(0f, Math.Min(1f,
                        Vec2.DotProduct(point - first, edge) / lengthSquared));
                return first + edge * amount;
            }
        }

        private sealed class CoastalIndex
        {
            private const float BucketSize = 8f;
            private readonly IList<PoliticalBorderGeometryCache.RawSegment> _segments;
            private readonly Dictionary<BucketKey, List<int>> _buckets =
                new Dictionary<BucketKey, List<int>>();
            private CoastalIndex(IList<PoliticalBorderGeometryCache.RawSegment> segments)
            { _segments = segments; }
            internal static CoastalIndex Build(IList<PoliticalBorderGeometryCache.RawSegment> segments)
            {
                CoastalIndex result = new CoastalIndex(segments);
                for (int index = 0; index < segments.Count; index++)
                    if (PoliticalBorderChainBuilder.IsCoastal(segments[index]))
                        result.Add(index, segments[index]);
                return result;
            }
            private void Add(int index, PoliticalBorderGeometryCache.RawSegment segment)
            {
                int minimumX = (int)Math.Floor((Math.Min(segment.First.x, segment.Second.x)
                    - CoastalSearchDistance) / BucketSize);
                int maximumX = (int)Math.Floor((Math.Max(segment.First.x, segment.Second.x)
                    + CoastalSearchDistance) / BucketSize);
                int minimumY = (int)Math.Floor((Math.Min(segment.First.y, segment.Second.y)
                    - CoastalSearchDistance) / BucketSize);
                int maximumY = (int)Math.Floor((Math.Max(segment.First.y, segment.Second.y)
                    + CoastalSearchDistance) / BucketSize);
                for (int x = minimumX; x <= maximumX; x++)
                for (int y = minimumY; y <= maximumY; y++)
                {
                    BucketKey key = new BucketKey(x, y);
                    List<int> values;
                    if (!_buckets.TryGetValue(key, out values))
                    { values = new List<int>(); _buckets.Add(key, values); }
                    values.Add(index);
                }
            }
            internal bool IsNear(Vec2 point, float maximum)
            {
                List<int> candidates;
                if (!_buckets.TryGetValue(BucketKey.From(point, BucketSize), out candidates))
                    return false;
                float maximumSquared = maximum * maximum;
                foreach (int index in candidates)
                {
                    PoliticalBorderGeometryCache.RawSegment segment = _segments[index];
                    if (DistanceSquared(point, segment.First, segment.Second) <= maximumSquared)
                        return true;
                }
                return false;
            }
            private static float DistanceSquared(Vec2 point, Vec2 first, Vec2 second)
            {
                Vec2 edge = second - first;
                float lengthSquared = edge.LengthSquared;
                float amount = lengthSquared <= 0.000001f ? 0f
                    : Math.Max(0f, Math.Min(1f, Vec2.DotProduct(point - first, edge) / lengthSquared));
                return (point - (first + edge * amount)).LengthSquared;
            }
        }

        private struct BucketKey : IEquatable<BucketKey>
        {
            internal BucketKey(int x, int y) { X = x; Y = y; }
            private int X, Y;
            internal static BucketKey From(Vec2 point, float size)
            { return new BucketKey((int)Math.Floor(point.x / size), (int)Math.Floor(point.y / size)); }
            internal BucketKey Offset(int x, int y) { return new BucketKey(X + x, Y + y); }
            public bool Equals(BucketKey other) { return X == other.X && Y == other.Y; }
            public override bool Equals(object obj)
            { return obj is BucketKey && Equals((BucketKey)obj); }
            public override int GetHashCode() { unchecked { return (X * 397) ^ Y; } }
        }
    }
}
