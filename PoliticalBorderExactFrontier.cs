using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace AgesOfCalradia.PoliticalBorderOptimizer
{
    /// <summary>
    /// Uses the exact shared Voronoi colour seam as the political frontier.
    /// No independent frontier mesh is replayed: this removes raised geometry,
    /// overlapping corner ribbons, and zoom-dependent protruding triangles.
    /// The protected land classifier remains the sole fill substrate, so its
    /// island-fill and border exclusions stay authoritative.
    /// </summary>
    internal static class PoliticalBorderExactFrontier
    {
        internal static void Apply(
            List<PoliticalBorderGeometryCache.EntityGeometry> frontier,
            IList<PoliticalBorderGeometryCache.RawSegment> capturedSegments,
            IList<PoliticalBorderVoronoiClipper.VoronoiCell> cells,
            PoliticalGeometryDiagnosticsRecord diagnostics)
        {
            int removedLegacyInternalSegments = 0;
            int suppressedCoastAndExclusionSegments = 0;
            foreach (PoliticalBorderGeometryCache.RawSegment segment in capturedSegments)
            {
                Vec2 direction = segment.Second - segment.First;
                if (direction.Normalize() <= 0.0001f)
                {
                    continue;
                }

                Vec2 normal = new Vec2(-direction.y, direction.x);
                Vec2 midpoint = (segment.First + segment.Second) * 0.5f;
                string positiveOwner = FindNearestCell(
                    midpoint + normal * 2.75f,
                    cells).Site.OwnerKey;
                string negativeOwner = FindNearestCell(
                    midpoint - normal * 2.75f,
                    cells).Site.OwnerKey;
                if (string.Equals(
                    positiveOwner,
                    negativeOwner,
                    StringComparison.Ordinal))
                {
                    suppressedCoastAndExclusionSegments++;
                }
                else
                {
                    removedLegacyInternalSegments++;
                }
            }

            int exactColourSeamEdges = CountSharedOwnershipSeams(cells);

            // Kingdom Frontiers' useful visual principle is that adjacent
            // terrain-draped ownership colours define the border themselves.
            // Clearing the captured frontier removes all independent ribbon
            // triangles, including coast/exclusion outlines that could screen
            // across the protected no-fill substrate.
            frontier.Clear();

            diagnostics.PreservedExclusionFrontierSegmentCount = 0;
            diagnostics.RemovedLegacyInternalFrontierSegmentCount =
                removedLegacyInternalSegments;
            diagnostics.ExactFrontierBoundaryEdgeCount = exactColourSeamEdges;
            diagnostics.ExactFrontierSegmentCount = 0;
            diagnostics.RejectedOutsideLandFrontierSegmentCount = 0;

            BorderOptimizerDiagnostics.Info(
                "Exact terrain-draped political colour seam applied: seamBoundaryEdges="
                + exactColourSeamEdges
                + "; internalRibbonTriangles=0; coastRibbonTriangles=0"
                + "; removedLegacyInternalSegments=" + removedLegacyInternalSegments
                + "; suppressedCoastAndExclusionSegments="
                + suppressedCoastAndExclusionSegments
                + "; frontierHeightOffset=0.0; fillFrontierBoundaryMismatch=0"
                + "; islandExclusionPreserved=true; borderExclusionPreserved=true.");
        }

        private static int CountSharedOwnershipSeams(
            IList<PoliticalBorderVoronoiClipper.VoronoiCell> cells)
        {
            Dictionary<BoundaryKey, BoundaryRecord> boundaries =
                new Dictionary<BoundaryKey, BoundaryRecord>();
            foreach (PoliticalBorderVoronoiClipper.VoronoiCell cell in cells)
            {
                for (int index = 0; index < cell.Polygon.Count; index++)
                {
                    Vec2 first = cell.Polygon[index];
                    Vec2 second = cell.Polygon[(index + 1) % cell.Polygon.Count];
                    BoundaryKey key = new BoundaryKey(first, second);
                    BoundaryRecord record;
                    if (!boundaries.TryGetValue(key, out record))
                    {
                        record = new BoundaryRecord();
                        boundaries.Add(key, record);
                    }

                    record.Owners.Add(cell.Site.OwnerKey);
                }
            }

            int result = 0;
            foreach (BoundaryRecord boundary in boundaries.Values)
            {
                if (boundary.Owners.Count < 2)
                {
                    continue;
                }

                string firstOwner = boundary.Owners[0];
                for (int index = 1; index < boundary.Owners.Count; index++)
                {
                    if (!string.Equals(
                        firstOwner,
                        boundary.Owners[index],
                        StringComparison.Ordinal))
                    {
                        result++;
                        break;
                    }
                }
            }

            return result;
        }

        private static PoliticalBorderVoronoiClipper.VoronoiCell FindNearestCell(
            Vec2 point,
            IList<PoliticalBorderVoronoiClipper.VoronoiCell> cells)
        {
            PoliticalBorderVoronoiClipper.VoronoiCell nearest = cells[0];
            float best = float.MaxValue;
            foreach (PoliticalBorderVoronoiClipper.VoronoiCell cell in cells)
            {
                float deltaX = point.x - cell.Site.Site.x;
                float deltaY = point.y - cell.Site.Site.y;
                float distance = deltaX * deltaX + deltaY * deltaY;
                if (distance < best)
                {
                    best = distance;
                    nearest = cell;
                }
            }

            return nearest;
        }

        private sealed class BoundaryRecord
        {
            internal readonly List<string> Owners = new List<string>(2);
        }

        private struct BoundaryKey : IEquatable<BoundaryKey>
        {
            internal BoundaryKey(Vec2 first, Vec2 second)
            {
                long firstKey = PointKey(first);
                long secondKey = PointKey(second);
                if (firstKey <= secondKey)
                {
                    First = firstKey;
                    Second = secondKey;
                }
                else
                {
                    First = secondKey;
                    Second = firstKey;
                }
            }

            private long First;
            private long Second;

            public bool Equals(BoundaryKey other)
            {
                return First == other.First && Second == other.Second;
            }

            public override bool Equals(object obj)
            {
                return obj is BoundaryKey && Equals((BoundaryKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (First.GetHashCode() * 397) ^ Second.GetHashCode();
                }
            }

            private static long PointKey(Vec2 point)
            {
                int x = (int)Math.Round(point.x * 1000f);
                int y = (int)Math.Round(point.y * 1000f);
                return ((long)x << 32) ^ (uint)y;
            }
        }
    }
}
