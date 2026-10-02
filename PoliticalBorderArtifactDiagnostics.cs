using System;
using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.Library;

namespace AgesOfCalradia.PoliticalBorderOptimizer
{
    /// <summary>
    /// Measures coastal ribbon and nearby territory-fill geometry without
    /// changing the protected renderer's classifications or emitted vertices.
    /// Samples are deliberately capped so a malformed map cannot flood the log.
    /// </summary>
    internal static class PoliticalBorderArtifactDiagnostics
    {
        private const float CoastalProbeDistance = 2.25f;
        private const float SpatialBucketSize = 8f;
        private const float TiltedNormalRatio = 0.999f;
        private const float HighReliefDelta = 1f;
        private const float LargeFillEdge = 8f;
        private const int MaximumLocationSamples = 12;
        private const int MaximumSamplesPerLayer = MaximumLocationSamples / 3;

        internal static void AnalyzeSource(
            IList<PoliticalBorderGeometryCache.EntityGeometry> fill,
            IList<PoliticalBorderGeometryCache.EntityGeometry> frontier,
            IList<PoliticalBorderGeometryCache.RawSegment> segments,
            PoliticalGeometryDiagnosticsRecord diagnostics)
        {
            CoastalIndex coast = CoastalIndex.Build(segments, diagnostics);
            diagnostics.SourceFillReversePairCount = CountReversePairs(fill);
            diagnostics.SourceFrontierReversePairCount = CountReversePairs(frontier);
            AnalyzeNearCoastTriangles(fill, coast, true, false, diagnostics);
            AnalyzeNearCoastTriangles(frontier, coast, false, false, diagnostics);
            LogSamples("source", coast);
        }

        internal static void AnalyzeOutput(
            IList<PoliticalBorderGeometryCache.EntityGeometry> fill,
            IList<PoliticalBorderGeometryCache.EntityGeometry> frontier,
            IList<PoliticalBorderGeometryCache.RawSegment> segments,
            PoliticalGeometryDiagnosticsRecord diagnostics)
        {
            CoastalIndex coast = CoastalIndex.Build(segments, null);
            diagnostics.OutputFillReversePairCount = CountReversePairs(fill);
            diagnostics.OutputFrontierReversePairCount = CountReversePairs(frontier);
            AnalyzeNearCoastTriangles(fill, coast, true, true, diagnostics);
            AnalyzeNearCoastTriangles(frontier, coast, false, true, diagnostics);
            LogSamples("output", coast);
        }

        private static void AnalyzeNearCoastTriangles(
            IList<PoliticalBorderGeometryCache.EntityGeometry> entities,
            CoastalIndex coast,
            bool fill,
            bool output,
            PoliticalGeometryDiagnosticsRecord diagnostics)
        {
            foreach (PoliticalBorderGeometryCache.EntityGeometry entity in entities)
            foreach (PoliticalBorderGeometryCache.Triangle triangle in entity.Triangles)
            {
                Vec2 centroid = new Vec2(
                    (triangle.First.x + triangle.Second.x + triangle.Third.x) / 3f,
                    (triangle.First.y + triangle.Second.y + triangle.Third.y) / 3f);
                if (!coast.IsNear(centroid)) continue;

                Vec3 firstEdge = triangle.Second - triangle.First;
                Vec3 secondEdge = triangle.Third - triangle.First;
                Vec3 cross = Vec3.CrossProduct(firstEdge, secondEdge);
                float doubledArea = cross.Length;
                bool degenerate = doubledArea < 0.0001f;
                bool nonUpward = !degenerate && cross.z <= 0f;
                float maximumPlanarEdge = MaximumPlanarEdge(triangle);
                float heightDelta = MaximumHeight(triangle) - MinimumHeight(triangle);
                bool tilted = !degenerate && Math.Abs(cross.z) / doubledArea < TiltedNormalRatio;

                if (!output && fill)
                {
                    diagnostics.SourceNearCoastFillTriangleCount++;
                    if (tilted) diagnostics.SourceNearCoastTiltedFillTriangleCount++;
                    if (heightDelta > HighReliefDelta)
                        diagnostics.SourceNearCoastHighReliefFillTriangleCount++;
                    if (maximumPlanarEdge > LargeFillEdge)
                        diagnostics.SourceNearCoastLargeFillTriangleCount++;
                    if (tilted || heightDelta > HighReliefDelta || maximumPlanarEdge > LargeFillEdge)
                        coast.AddSample("fill", centroid, maximumPlanarEdge, heightDelta);
                }
                else if (!output)
                {
                    diagnostics.SourceNearCoastFrontierTriangleCount++;
                    if (nonUpward) diagnostics.SourceNearCoastNonUpwardFrontierTriangleCount++;
                    if (degenerate) diagnostics.SourceNearCoastDegenerateFrontierTriangleCount++;
                    if (nonUpward || degenerate)
                        coast.AddSample("source-ribbon-face",
                            centroid, maximumPlanarEdge, heightDelta);
                }
                else if (output)
                {
                    if (fill) diagnostics.OutputNearCoastFillTriangleCount++;
                    else diagnostics.OutputNearCoastFrontierTriangleCount++;
                    if (nonUpward) diagnostics.OutputNearCoastNonUpwardTriangleCount++;
                    if (degenerate) diagnostics.OutputNearCoastDegenerateTriangleCount++;
                    if (nonUpward || degenerate)
                        coast.AddSample(fill ? "fill-face" : "ribbon-face",
                            centroid, maximumPlanarEdge, heightDelta);
                }
            }
        }

        private static int CountReversePairs(
            IList<PoliticalBorderGeometryCache.EntityGeometry> entities)
        {
            int result = 0;
            foreach (PoliticalBorderGeometryCache.EntityGeometry entity in entities)
            {
                IList<PoliticalBorderGeometryCache.Triangle> triangles = entity.Triangles;
                for (int index = 0; index + 1 < triangles.Count; index++)
                {
                    if (!IsReversePair(triangles[index], triangles[index + 1])) continue;
                    result++;
                    index++;
                }
            }
            return result;
        }

        private static bool IsReversePair(
            PoliticalBorderGeometryCache.Triangle first,
            PoliticalBorderGeometryCache.Triangle second)
        {
            return first.Color == second.Color
                && Same(first.First, second.First)
                && Same(first.Second, second.Third)
                && Same(first.Third, second.Second);
        }

        private static bool Same(Vec3 first, Vec3 second)
        {
            return first.x == second.x && first.y == second.y && first.z == second.z;
        }

        private static float MaximumPlanarEdge(PoliticalBorderGeometryCache.Triangle triangle)
        {
            return Math.Max(PlanarDistance(triangle.First, triangle.Second),
                Math.Max(PlanarDistance(triangle.First, triangle.Third),
                    PlanarDistance(triangle.Second, triangle.Third)));
        }

        private static float PlanarDistance(Vec3 first, Vec3 second)
        {
            float x = first.x - second.x;
            float y = first.y - second.y;
            return (float)Math.Sqrt(x * x + y * y);
        }

        private static float MaximumHeight(PoliticalBorderGeometryCache.Triangle triangle)
        { return Math.Max(triangle.First.z, Math.Max(triangle.Second.z, triangle.Third.z)); }

        private static float MinimumHeight(PoliticalBorderGeometryCache.Triangle triangle)
        { return Math.Min(triangle.First.z, Math.Min(triangle.Second.z, triangle.Third.z)); }

        private static void LogSamples(string phase, CoastalIndex coast)
        {
            List<string> samples = coast.GetSamples();
            BorderOptimizerDiagnostics.Info(
                "Political coastal artifact samples: phase=" + phase
                + "; sampleCount=" + samples.Count
                + "; samples=" + (samples.Count == 0
                    ? "none" : string.Join(" | ", samples.ToArray())) + ".");
        }

        private sealed class CoastalIndex
        {
            private readonly IList<PoliticalBorderGeometryCache.RawSegment> _segments;
            private readonly Dictionary<BucketKey, List<int>> _buckets =
                new Dictionary<BucketKey, List<int>>();
            private readonly List<string> _topologySamples = new List<string>();
            private readonly List<string> _fillSamples = new List<string>();
            private readonly List<string> _ribbonSamples = new List<string>();

            private CoastalIndex(IList<PoliticalBorderGeometryCache.RawSegment> segments)
            { _segments = segments; }

            internal static CoastalIndex Build(
                IList<PoliticalBorderGeometryCache.RawSegment> segments,
                PoliticalGeometryDiagnosticsRecord diagnostics)
            {
                CoastalIndex result = new CoastalIndex(segments);
                Dictionary<PoliticalBorderGeometryCache.NodeKey, CoastalNode> nodes =
                    new Dictionary<PoliticalBorderGeometryCache.NodeKey, CoastalNode>();
                for (int index = 0; index < segments.Count; index++)
                {
                    PoliticalBorderGeometryCache.RawSegment segment = segments[index];
                    if (segment.LeftColor != segment.RightColor) continue;
                    result.AddSegment(index, segment);
                    if (diagnostics == null) continue;
                    diagnostics.CoastalSegmentCount++;
                    diagnostics.CoastalMaximumSegmentLength = Math.Max(
                        diagnostics.CoastalMaximumSegmentLength,
                        (segment.Second - segment.First).Length);
                    diagnostics.CoastalMaximumHeightDelta = Math.Max(
                        diagnostics.CoastalMaximumHeightDelta,
                        Math.Abs(segment.SecondTerrainPoint.z - segment.FirstTerrainPoint.z));
                    AddNode(nodes, segment.First, segment.Second);
                    AddNode(nodes, segment.Second, segment.First);
                }
                if (diagnostics != null) AnalyzeNodes(nodes, diagnostics, result);
                return result;
            }

            internal bool IsNear(Vec2 point)
            {
                List<int> candidates;
                if (!_buckets.TryGetValue(BucketKey.FromPoint(point), out candidates)) return false;
                float maximumSquared = CoastalProbeDistance * CoastalProbeDistance;
                foreach (int index in candidates)
                {
                    PoliticalBorderGeometryCache.RawSegment segment = _segments[index];
                    if (DistanceSquaredToSegment(point, segment.First, segment.Second) <= maximumSquared)
                        return true;
                }
                return false;
            }

            internal void AddSample(string kind, Vec2 point, float edge, float heightDelta)
            {
                List<string> destination = kind == "coast-turn"
                    ? _topologySamples
                    : (kind.IndexOf("fill", StringComparison.Ordinal) >= 0
                        ? _fillSamples : _ribbonSamples);
                if (destination.Count >= MaximumSamplesPerLayer) return;
                destination.Add(kind + "@(" + Format(point.x) + "," + Format(point.y)
                    + ") edge=" + Format(edge) + " dz=" + Format(heightDelta));
            }

            internal List<string> GetSamples()
            {
                List<string> result = new List<string>(MaximumLocationSamples);
                result.AddRange(_topologySamples);
                result.AddRange(_fillSamples);
                result.AddRange(_ribbonSamples);
                return result;
            }

            private void AddSegment(
                int index,
                PoliticalBorderGeometryCache.RawSegment segment)
            {
                int minimumX = BucketKey.ToBucket(Math.Min(segment.First.x, segment.Second.x)
                    - CoastalProbeDistance);
                int maximumX = BucketKey.ToBucket(Math.Max(segment.First.x, segment.Second.x)
                    + CoastalProbeDistance);
                int minimumY = BucketKey.ToBucket(Math.Min(segment.First.y, segment.Second.y)
                    - CoastalProbeDistance);
                int maximumY = BucketKey.ToBucket(Math.Max(segment.First.y, segment.Second.y)
                    + CoastalProbeDistance);
                for (int x = minimumX; x <= maximumX; x++)
                for (int y = minimumY; y <= maximumY; y++)
                {
                    BucketKey key = new BucketKey(x, y);
                    List<int> values;
                    if (!_buckets.TryGetValue(key, out values))
                    {
                        values = new List<int>();
                        _buckets.Add(key, values);
                    }
                    values.Add(index);
                }
            }

            private static void AddNode(
                IDictionary<PoliticalBorderGeometryCache.NodeKey, CoastalNode> nodes,
                Vec2 point,
                Vec2 opposite)
            {
                PoliticalBorderGeometryCache.NodeKey key =
                    new PoliticalBorderGeometryCache.NodeKey(point);
                CoastalNode node;
                if (!nodes.TryGetValue(key, out node))
                {
                    node = new CoastalNode(point);
                    nodes.Add(key, node);
                }
                node.Opposites.Add(opposite);
            }

            private static void AnalyzeNodes(
                IDictionary<PoliticalBorderGeometryCache.NodeKey, CoastalNode> nodes,
                PoliticalGeometryDiagnosticsRecord diagnostics,
                CoastalIndex index)
            {
                diagnostics.CoastalNodeCount = nodes.Count;
                foreach (CoastalNode node in nodes.Values)
                {
                    if (node.Opposites.Count == 1) diagnostics.CoastalEndpointCount++;
                    else if (node.Opposites.Count > 2) diagnostics.CoastalJunctionCount++;
                    else if (node.Opposites.Count == 2)
                    {
                        Vec2 first = node.Opposites[0] - node.Position;
                        Vec2 second = node.Opposites[1] - node.Position;
                        if (first.Normalize() <= 0.0001f || second.Normalize() <= 0.0001f) continue;
                        float straightness = Math.Max(-1f, Math.Min(1f,
                            Vec2.DotProduct(first, second)));
                        if (straightness <= -0.819152f) continue;
                        diagnostics.CoastalSharpTurnCount++;
                        index.AddSample("coast-turn", node.Position, 0f, 0f);
                    }
                }
            }

            private static float DistanceSquaredToSegment(Vec2 point, Vec2 first, Vec2 second)
            {
                Vec2 edge = second - first;
                float squaredLength = edge.x * edge.x + edge.y * edge.y;
                if (squaredLength <= 0.000001f)
                {
                    Vec2 delta = point - first;
                    return delta.x * delta.x + delta.y * delta.y;
                }
                Vec2 offset = point - first;
                float amount = Math.Max(0f, Math.Min(1f,
                    (offset.x * edge.x + offset.y * edge.y) / squaredLength));
                Vec2 nearest = first + edge * amount;
                Vec2 distance = point - nearest;
                return distance.x * distance.x + distance.y * distance.y;
            }

            private static string Format(float value)
            { return value.ToString("F2", CultureInfo.InvariantCulture); }
        }

        private sealed class CoastalNode
        {
            internal CoastalNode(Vec2 position) { Position = position; }
            internal readonly Vec2 Position;
            internal readonly List<Vec2> Opposites = new List<Vec2>();
        }

        private struct BucketKey : IEquatable<BucketKey>
        {
            internal BucketKey(int x, int y) { X = x; Y = y; }
            private readonly int X, Y;
            internal static BucketKey FromPoint(Vec2 point)
            { return new BucketKey(ToBucket(point.x), ToBucket(point.y)); }
            internal static int ToBucket(float value)
            { return (int)Math.Floor(value / SpatialBucketSize); }
            public bool Equals(BucketKey other) { return X == other.X && Y == other.Y; }
            public override bool Equals(object obj)
            { return obj is BucketKey && Equals((BucketKey)obj); }
            public override int GetHashCode()
            { unchecked { return (X * 397) ^ Y; } }
        }
    }
}
