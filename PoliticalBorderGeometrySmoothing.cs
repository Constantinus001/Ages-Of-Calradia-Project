using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace AgesOfCalradia.PoliticalBorderOptimizer
{
    /// <summary>
    /// Applies one bounded displacement field to the protected renderer's
    /// coloured fill and frontier ribbon. Sharing the field is essential:
    /// moving only the ribbon exposes the original triangular fill boundary.
    /// Endpoints and junctions remain fixed to preserve political topology.
    /// </summary>
    internal static class PoliticalBorderGeometrySmoothing
    {
        private const int SmoothingPasses = 6;
        private const float PositiveFactor = 0.55f;
        private const float NegativeFactor = -0.56f;
        private const float MaximumNodeDisplacement = 1.4f;
        private const float InfluenceRadius = 2.75f;
        private const float BucketSize = 2f;

        internal static void Apply(
            List<PoliticalBorderGeometryCache.EntityGeometry> fill,
            List<PoliticalBorderGeometryCache.EntityGeometry> frontier,
            List<PoliticalBorderGeometryCache.RawSegment> segments,
            PoliticalGeometryDiagnosticsRecord diagnostics)
        {
            if (segments.Count == 0) return;

            diagnostics.RemovedReverseFillTriangleCount = RemoveReverseFillFaces(fill);

            Dictionary<PoliticalBorderGeometryCache.NodeKey, Vec2> original =
                new Dictionary<PoliticalBorderGeometryCache.NodeKey, Vec2>();
            Dictionary<PoliticalBorderGeometryCache.NodeKey,
                HashSet<PoliticalBorderGeometryCache.NodeKey>> graph =
                new Dictionary<PoliticalBorderGeometryCache.NodeKey,
                    HashSet<PoliticalBorderGeometryCache.NodeKey>>();
            foreach (PoliticalBorderGeometryCache.RawSegment segment in segments)
            {
                PoliticalBorderGeometryCache.NodeKey first =
                    new PoliticalBorderGeometryCache.NodeKey(segment.First);
                PoliticalBorderGeometryCache.NodeKey second =
                    new PoliticalBorderGeometryCache.NodeKey(segment.Second);
                original[first] = segment.First;
                original[second] = segment.Second;
                AddNeighbor(graph, first, second);
                AddNeighbor(graph, second, first);
            }

            Dictionary<PoliticalBorderGeometryCache.NodeKey, Vec2> current =
                new Dictionary<PoliticalBorderGeometryCache.NodeKey, Vec2>(original);
            for (int pass = 0; pass < SmoothingPasses; pass++)
            {
                float factor = (pass & 1) == 0 ? PositiveFactor : NegativeFactor;
                Dictionary<PoliticalBorderGeometryCache.NodeKey, Vec2> next =
                    new Dictionary<PoliticalBorderGeometryCache.NodeKey, Vec2>(current);
                foreach (KeyValuePair<PoliticalBorderGeometryCache.NodeKey, Vec2> node in current)
                {
                    HashSet<PoliticalBorderGeometryCache.NodeKey> adjacent;
                    if (!graph.TryGetValue(node.Key, out adjacent) || adjacent.Count != 2) continue;
                    PoliticalBorderGeometryCache.NodeKey[] pair =
                        new PoliticalBorderGeometryCache.NodeKey[2];
                    adjacent.CopyTo(pair);
                    Vec2 average = (current[pair[0]] + current[pair[1]]) * 0.5f;
                    Vec2 candidate = node.Value + (average - node.Value) * factor;
                    next[node.Key] = ClampDisplacement(original[node.Key], candidate);
                }
                current = next;
            }

            Dictionary<long, List<NodeShift>> buckets = new Dictionary<long, List<NodeShift>>();
            foreach (KeyValuePair<PoliticalBorderGeometryCache.NodeKey, Vec2> node in original)
            {
                Vec2 displacement = current[node.Key] - node.Value;
                float distance = displacement.Length;
                if (distance > 0.0001f) diagnostics.SmoothedNodeCount++;
                diagnostics.TotalSmoothingDisplacement += distance;
                diagnostics.MaximumSmoothingDisplacement = Math.Max(
                    diagnostics.MaximumSmoothingDisplacement, distance);
                AddShift(buckets, new NodeShift(node.Value, displacement));
            }

            diagnostics.SmoothingPassCount = SmoothingPasses;
            diagnostics.FinalSharpFrontierTurnCount = CountSharpTurns(current, graph);
            diagnostics.FillAdjustedVertexCount = ApplyToGeometry(fill, buckets);
            diagnostics.FrontierAdjustedVertexCount = ApplyToGeometry(frontier, buckets);
            BorderOptimizerDiagnostics.Info("Shared political contour smoothing applied: passes="
                + SmoothingPasses + "; segments=" + segments.Count
                + "; nodes=" + original.Count
                + "; sourceSharpTurns=" + diagnostics.SharpFrontierTurnCount
                + "; finalSharpTurns=" + diagnostics.FinalSharpFrontierTurnCount
                + "; removedReverseFillTriangles=" + diagnostics.RemovedReverseFillTriangleCount
                + "; fillAdjustedVertices=" + diagnostics.FillAdjustedVertexCount
                + "; frontierAdjustedVertices=" + diagnostics.FrontierAdjustedVertexCount + ".");
        }

        private static int RemoveReverseFillFaces(
            IList<PoliticalBorderGeometryCache.EntityGeometry> entities)
        {
            int removed = 0;
            foreach (PoliticalBorderGeometryCache.EntityGeometry entity in entities)
            {
                List<PoliticalBorderGeometryCache.Triangle> triangles = entity.Triangles;
                int write = 0;
                for (int read = 0; read < triangles.Count; read++)
                {
                    PoliticalBorderGeometryCache.Triangle first = triangles[read];
                    if (read + 1 < triangles.Count && IsReversePair(first, triangles[read + 1]))
                    {
                        triangles[write++] = first;
                        read++;
                        removed++;
                    }
                    else
                    {
                        triangles[write++] = first;
                    }
                }
                if (write < triangles.Count) triangles.RemoveRange(write, triangles.Count - write);
            }
            return removed;
        }

        private static bool IsReversePair(
            PoliticalBorderGeometryCache.Triangle first,
            PoliticalBorderGeometryCache.Triangle second)
        {
            return first.Color == second.Color
                && Same(first.First, second.First)
                && Same(first.Second, second.Third)
                && Same(first.Third, second.Second)
                && Same(first.FirstUv, second.FirstUv)
                && Same(first.SecondUv, second.ThirdUv)
                && Same(first.ThirdUv, second.SecondUv);
        }

        private static bool Same(Vec3 first, Vec3 second)
        {
            return first.x == second.x && first.y == second.y && first.z == second.z;
        }

        private static bool Same(Vec2 first, Vec2 second)
        {
            return first.x == second.x && first.y == second.y;
        }

        private static Vec2 ClampDisplacement(Vec2 original, Vec2 candidate)
        {
            Vec2 displacement = candidate - original;
            float length = displacement.Normalize();
            return length <= MaximumNodeDisplacement
                ? candidate
                : original + displacement * MaximumNodeDisplacement;
        }

        private static int CountSharpTurns(
            Dictionary<PoliticalBorderGeometryCache.NodeKey, Vec2> positions,
            Dictionary<PoliticalBorderGeometryCache.NodeKey,
                HashSet<PoliticalBorderGeometryCache.NodeKey>> graph)
        {
            int count = 0;
            foreach (KeyValuePair<PoliticalBorderGeometryCache.NodeKey, Vec2> node in positions)
            {
                HashSet<PoliticalBorderGeometryCache.NodeKey> adjacent;
                if (!graph.TryGetValue(node.Key, out adjacent) || adjacent.Count != 2) continue;
                PoliticalBorderGeometryCache.NodeKey[] pair =
                    new PoliticalBorderGeometryCache.NodeKey[2];
                adjacent.CopyTo(pair);
                Vec2 first = positions[pair[0]] - node.Value;
                Vec2 second = positions[pair[1]] - node.Value;
                if (first.Normalize() <= 0.0001f || second.Normalize() <= 0.0001f) continue;
                float straightness = Math.Max(-1f, Math.Min(1f, Vec2.DotProduct(first, second)));
                if (straightness > -0.819152f) count++;
            }
            return count;
        }

        private static int ApplyToGeometry(
            IList<PoliticalBorderGeometryCache.EntityGeometry> entities,
            Dictionary<long, List<NodeShift>> buckets)
        {
            int adjusted = 0;
            foreach (PoliticalBorderGeometryCache.EntityGeometry entity in entities)
            {
                for (int index = 0; index < entity.Triangles.Count; index++)
                {
                    PoliticalBorderGeometryCache.Triangle triangle = entity.Triangles[index];
                    triangle.First = ApplyNearestDisplacement(triangle.First, buckets, ref adjusted);
                    triangle.Second = ApplyNearestDisplacement(triangle.Second, buckets, ref adjusted);
                    triangle.Third = ApplyNearestDisplacement(triangle.Third, buckets, ref adjusted);
                    entity.Triangles[index] = triangle;
                }
            }
            return adjusted;
        }

        private static Vec3 ApplyNearestDisplacement(
            Vec3 point,
            Dictionary<long, List<NodeShift>> buckets,
            ref int adjusted)
        {
            float bestDistanceSquared = InfluenceRadius * InfluenceRadius;
            Vec2 best = Vec2.Zero;
            Vec2 point2 = new Vec2(point.x, point.y);
            int bucketX = (int)Math.Floor(point.x / BucketSize);
            int bucketY = (int)Math.Floor(point.y / BucketSize);
            for (int offsetY = -2; offsetY <= 2; offsetY++)
            for (int offsetX = -2; offsetX <= 2; offsetX++)
            {
                List<NodeShift> values;
                if (!buckets.TryGetValue(BucketKey(bucketX + offsetX, bucketY + offsetY), out values)) continue;
                foreach (NodeShift shift in values)
                {
                    float distanceSquared = (point2 - shift.Position).LengthSquared;
                    if (distanceSquared >= bestDistanceSquared) continue;
                    bestDistanceSquared = distanceSquared;
                    best = shift.Displacement;
                }
            }
            if (best.LengthSquared > 0.00000001f)
            {
                point.x += best.x;
                point.y += best.y;
                adjusted++;
            }
            return point;
        }

        private static void AddShift(Dictionary<long, List<NodeShift>> buckets, NodeShift shift)
        {
            long key = BucketKey(
                (int)Math.Floor(shift.Position.x / BucketSize),
                (int)Math.Floor(shift.Position.y / BucketSize));
            List<NodeShift> values;
            if (!buckets.TryGetValue(key, out values))
            {
                values = new List<NodeShift>();
                buckets.Add(key, values);
            }
            values.Add(shift);
        }

        private static void AddNeighbor(
            Dictionary<PoliticalBorderGeometryCache.NodeKey,
                HashSet<PoliticalBorderGeometryCache.NodeKey>> graph,
            PoliticalBorderGeometryCache.NodeKey node,
            PoliticalBorderGeometryCache.NodeKey neighbor)
        {
            HashSet<PoliticalBorderGeometryCache.NodeKey> values;
            if (!graph.TryGetValue(node, out values))
            {
                values = new HashSet<PoliticalBorderGeometryCache.NodeKey>();
                graph.Add(node, values);
            }
            values.Add(neighbor);
        }

        private static long BucketKey(int x, int y)
        {
            return ((long)x << 32) ^ (uint)y;
        }

        private struct NodeShift
        {
            internal NodeShift(Vec2 position, Vec2 displacement)
            {
                Position = position;
                Displacement = displacement;
            }

            internal Vec2 Position;
            internal Vec2 Displacement;
        }
    }
}
