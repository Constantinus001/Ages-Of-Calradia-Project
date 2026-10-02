using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace AgesOfCalradia.PoliticalBorderOptimizer
{
    /// <summary>
    /// Subdivides the protected renderer's already land-filtered triangles at
    /// exact nearest-site bisectors. This borrows the clean visual principle
    /// of a Voronoi political overlay without replacing the protected land
    /// classifier: every output point remains inside a captured source face,
    /// so excluded islands, water, rivers, and coastlines remain authoritative.
    /// </summary>
    internal static class PoliticalBorderVoronoiClipper
    {
        private const float GeometryEpsilon = 0.0001f;
        private const float PoliticalSeamHalfGap = 0.55f;
        private const int SpatialColumns = 32;
        private const int SpatialRows = 32;

        internal static void Apply(
            List<PoliticalBorderGeometryCache.EntityGeometry> fill,
            List<PoliticalBorderGeometryCache.EntityGeometry> frontier,
            List<PoliticalBorderGeometryCache.RawSegment> segments,
            PoliticalBorderGeometryCache.CacheIdentity identity,
            PoliticalGeometryDiagnosticsRecord diagnostics)
        {
            if (identity.Sites.Count == 0 || fill.Count == 0) return;

            // The protected fill remains the only substrate. After clipping,
            // PoliticalBorderExactFrontier removes independent ribbon geometry
            // so neither an internal edge nor an excluded coast can protrude.

            List<VoronoiCell> cells = BuildCells(identity);
            if (cells.Count == 0) return;
            ResolveDisplayColors(cells, fill, identity.Sites);
            CellSpatialIndex index = new CellSpatialIndex(identity, cells);

            int sourceTriangles = 0;
            int generatedTriangles = 0;
            int splitSourceTriangles = 0;
            int discardedDegeneratePolygons = 0;
            foreach (PoliticalBorderGeometryCache.EntityGeometry entity in fill)
            {
                List<PoliticalBorderGeometryCache.Triangle> source = entity.Triangles;
                List<PoliticalBorderGeometryCache.Triangle> clipped =
                    new List<PoliticalBorderGeometryCache.Triangle>(source.Count);
                for (int triangleIndex = 0; triangleIndex < source.Count; triangleIndex++)
                {
                    PoliticalBorderGeometryCache.Triangle triangle = source[triangleIndex];
                    if (triangleIndex + 1 < source.Count
                        && IsReversePair(triangle, source[triangleIndex + 1]))
                    {
                        triangleIndex++;
                        diagnostics.RemovedReverseFillTriangleCount++;
                    }
                    sourceTriangles++;
                    int before = clipped.Count;
                    List<int> candidates = index.Query(triangle);
                    foreach (int cellIndex in candidates)
                    {
                        VoronoiCell cell = cells[cellIndex];
                        List<ClipVertex> polygon = ClipTriangle(triangle, cell.Polygon);
                        if (polygon.Count < 3) continue;
                        int emitted = Triangulate(polygon, cell.DisplayColor, clipped);
                        if (emitted == 0) discardedDegeneratePolygons++;
                    }
                    int emittedForSource = clipped.Count - before;
                    generatedTriangles += emittedForSource;
                    if (emittedForSource > 1) splitSourceTriangles++;
                }
                entity.Triangles.Clear();
                entity.Triangles.AddRange(clipped);
            }

            PoliticalBorderExactFrontier.Apply(
                frontier,
                segments,
                cells,
                diagnostics);

            diagnostics.VoronoiCellCount = cells.Count;
            diagnostics.VoronoiSourceTriangleCount = sourceTriangles;
            diagnostics.VoronoiGeneratedTriangleCount = generatedTriangles;
            diagnostics.VoronoiSplitSourceTriangleCount = splitSourceTriangles;
            diagnostics.VoronoiDiscardedDegeneratePolygonCount = discardedDegeneratePolygons;
            diagnostics.VoronoiOutsideLandTriangleCount = 0;
            BorderOptimizerDiagnostics.Info(
                "Land-constrained exact political clipping applied: cells=" + cells.Count
                + "; sourceTriangles=" + sourceTriangles
                + "; generatedTriangles=" + generatedTriangles
                + "; splitSourceTriangles=" + splitSourceTriangles
                + "; discardedDegeneratePolygons=" + discardedDegeneratePolygons
                + "; politicalSeamHalfGap=" + PoliticalSeamHalfGap.ToString("F2")
                + "; politicalSeamWidth=" + (PoliticalSeamHalfGap * 2f).ToString("F2")
                + "; exposedTerrainSeam=true; frontierTriangles=0"
                + "; outsideLandTriangles=0; islandExclusionPreserved=true"
                + "; borderExclusionPreserved=true.");
        }

        private static List<VoronoiCell> BuildCells(
            PoliticalBorderGeometryCache.CacheIdentity identity)
        {
            List<VoronoiCell> result = new List<VoronoiCell>(identity.Sites.Count);
            for (int siteIndex = 0; siteIndex < identity.Sites.Count; siteIndex++)
            {
                PoliticalBorderGeometryCache.TerritorySite site = identity.Sites[siteIndex];
                List<Vec2> polygon = new List<Vec2>
                {
                    new Vec2(identity.MinimumX, identity.MinimumY),
                    new Vec2(identity.MaximumX, identity.MinimumY),
                    new Vec2(identity.MaximumX, identity.MaximumY),
                    new Vec2(identity.MinimumX, identity.MaximumY)
                };
                for (int otherIndex = 0; otherIndex < identity.Sites.Count && polygon.Count >= 3; otherIndex++)
                {
                    if (otherIndex == siteIndex) continue;
                    Vec2 other = identity.Sites[otherIndex].Site;
                    float normalX = 2f * (other.x - site.Site.x);
                    float normalY = 2f * (other.y - site.Site.y);
                    float limit = other.x * other.x + other.y * other.y
                        - site.Site.x * site.Site.x - site.Site.y * site.Site.y;
                    if (!string.Equals(
                        site.OwnerKey,
                        identity.Sites[otherIndex].OwnerKey,
                        StringComparison.Ordinal))
                    {
                        // Offset both opposing half-planes toward their own
                        // sites. The uncovered strip is the visible frontier;
                        // same-owner settlement cells still meet exactly.
                        float normalLength = (float)Math.Sqrt(
                            normalX * normalX + normalY * normalY);
                        limit -= PoliticalSeamHalfGap * normalLength;
                    }
                    polygon = ClipToHalfPlane(polygon, normalX, normalY, limit);
                }
                if (polygon.Count >= 3 && Math.Abs(SignedArea(polygon)) > GeometryEpsilon)
                    result.Add(new VoronoiCell(site, polygon));
            }
            return result;
        }

        private static List<Vec2> ClipToHalfPlane(
            List<Vec2> input,
            float normalX,
            float normalY,
            float limit)
        {
            List<Vec2> output = new List<Vec2>(input.Count + 1);
            Vec2 previous = input[input.Count - 1];
            float previousDistance = normalX * previous.x + normalY * previous.y - limit;
            bool previousInside = previousDistance <= GeometryEpsilon;
            foreach (Vec2 current in input)
            {
                float currentDistance = normalX * current.x + normalY * current.y - limit;
                bool currentInside = currentDistance <= GeometryEpsilon;
                if (currentInside != previousInside)
                {
                    float denominator = previousDistance - currentDistance;
                    float amount = Math.Abs(denominator) < GeometryEpsilon
                        ? 0f : previousDistance / denominator;
                    output.Add(previous + (current - previous) * amount);
                }
                if (currentInside) output.Add(current);
                previous = current;
                previousDistance = currentDistance;
                previousInside = currentInside;
            }
            return output;
        }

        private static void ResolveDisplayColors(
            IList<VoronoiCell> cells,
            IList<PoliticalBorderGeometryCache.EntityGeometry> fill,
            IList<PoliticalBorderGeometryCache.TerritorySite> sites)
        {
            Dictionary<string, Dictionary<uint, int>> observed =
                new Dictionary<string, Dictionary<uint, int>>(StringComparer.Ordinal);
            foreach (PoliticalBorderGeometryCache.EntityGeometry entity in fill)
            foreach (PoliticalBorderGeometryCache.Triangle triangle in entity.Triangles)
            {
                Vec2 center = new Vec2(
                    (triangle.First.x + triangle.Second.x + triangle.Third.x) / 3f,
                    (triangle.First.y + triangle.Second.y + triangle.Third.y) / 3f);
                string owner = FindNearestSite(center, sites).OwnerKey;
                Dictionary<uint, int> colors;
                if (!observed.TryGetValue(owner, out colors))
                {
                    colors = new Dictionary<uint, int>();
                    observed.Add(owner, colors);
                }
                int count;
                colors.TryGetValue(triangle.Color, out count);
                colors[triangle.Color] = count + 1;
            }

            foreach (VoronoiCell cell in cells)
            {
                Dictionary<uint, int> colors;
                uint selected = ScaleOpaqueColor(cell.Site.Color, 50u);
                if (observed.TryGetValue(cell.Site.OwnerKey, out colors))
                {
                    int bestCount = -1;
                    foreach (KeyValuePair<uint, int> color in colors)
                    {
                        if (color.Value > bestCount)
                        {
                            bestCount = color.Value;
                            selected = color.Key;
                        }
                    }
                }
                cell.DisplayColor = selected;
            }
        }

        private static PoliticalBorderGeometryCache.TerritorySite FindNearestSite(
            Vec2 point,
            IList<PoliticalBorderGeometryCache.TerritorySite> sites)
        {
            PoliticalBorderGeometryCache.TerritorySite nearest = sites[0];
            float best = float.MaxValue;
            foreach (PoliticalBorderGeometryCache.TerritorySite site in sites)
            {
                float dx = point.x - site.Site.x;
                float dy = point.y - site.Site.y;
                float distance = dx * dx + dy * dy;
                if (distance < best)
                {
                    best = distance;
                    nearest = site;
                }
            }
            return nearest;
        }

        private static uint ScaleOpaqueColor(uint color, uint percent)
        {
            uint red = ((color >> 16) & 0xFFu) * percent / 100u;
            uint green = ((color >> 8) & 0xFFu) * percent / 100u;
            uint blue = (color & 0xFFu) * percent / 100u;
            return 0xFF000000u | (red << 16) | (green << 8) | blue;
        }

        private static List<ClipVertex> ClipTriangle(
            PoliticalBorderGeometryCache.Triangle triangle,
            IList<Vec2> clipPolygon)
        {
            List<ClipVertex> polygon = new List<ClipVertex>(3)
            {
                new ClipVertex(triangle.First, triangle.FirstUv),
                new ClipVertex(triangle.Second, triangle.SecondUv),
                new ClipVertex(triangle.Third, triangle.ThirdUv)
            };
            for (int edgeIndex = 0; edgeIndex < clipPolygon.Count && polygon.Count >= 3; edgeIndex++)
            {
                Vec2 edgeStart = clipPolygon[edgeIndex];
                Vec2 edgeEnd = clipPolygon[(edgeIndex + 1) % clipPolygon.Count];
                polygon = ClipVerticesToEdge(polygon, edgeStart, edgeEnd);
            }
            return polygon;
        }

        private static List<ClipVertex> ClipVerticesToEdge(
            List<ClipVertex> input,
            Vec2 edgeStart,
            Vec2 edgeEnd)
        {
            List<ClipVertex> output = new List<ClipVertex>(input.Count + 1);
            ClipVertex previous = input[input.Count - 1];
            float previousDistance = EdgeDistance(edgeStart, edgeEnd, previous.Position);
            bool previousInside = previousDistance >= -GeometryEpsilon;
            foreach (ClipVertex current in input)
            {
                float currentDistance = EdgeDistance(edgeStart, edgeEnd, current.Position);
                bool currentInside = currentDistance >= -GeometryEpsilon;
                if (currentInside != previousInside)
                {
                    float denominator = previousDistance - currentDistance;
                    float amount = Math.Abs(denominator) < GeometryEpsilon
                        ? 0f : previousDistance / denominator;
                    output.Add(ClipVertex.Lerp(previous, current, amount));
                }
                if (currentInside) output.Add(current);
                previous = current;
                previousDistance = currentDistance;
                previousInside = currentInside;
            }
            return output;
        }

        private static float EdgeDistance(Vec2 start, Vec2 end, Vec3 point)
        {
            return (end.x - start.x) * (point.y - start.y)
                - (end.y - start.y) * (point.x - start.x);
        }

        private static int Triangulate(
            IList<ClipVertex> polygon,
            uint color,
            List<PoliticalBorderGeometryCache.Triangle> output)
        {
            int emitted = 0;
            ClipVertex first = polygon[0];
            for (int index = 1; index + 1 < polygon.Count; index++)
            {
                ClipVertex second = polygon[index];
                ClipVertex third = polygon[index + 1];
                float area = Math.Abs(
                    (second.Position.x - first.Position.x) * (third.Position.y - first.Position.y)
                    - (second.Position.y - first.Position.y) * (third.Position.x - first.Position.x));
                if (area <= GeometryEpsilon) continue;
                output.Add(new PoliticalBorderGeometryCache.Triangle(
                    first.Position, second.Position, third.Position,
                    first.Uv, second.Uv, third.Uv, color));
                emitted++;
            }
            return emitted;
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

        private static float SignedArea(IList<Vec2> polygon)
        {
            float area = 0f;
            for (int index = 0; index < polygon.Count; index++)
            {
                Vec2 first = polygon[index];
                Vec2 second = polygon[(index + 1) % polygon.Count];
                area += first.x * second.y - second.x * first.y;
            }
            return area * 0.5f;
        }

        internal sealed class VoronoiCell
        {
            internal VoronoiCell(
                PoliticalBorderGeometryCache.TerritorySite site,
                List<Vec2> polygon)
            {
                Site = site;
                Polygon = polygon;
                MinimumX = MaximumX = polygon[0].x;
                MinimumY = MaximumY = polygon[0].y;
                foreach (Vec2 point in polygon)
                {
                    MinimumX = Math.Min(MinimumX, point.x);
                    MinimumY = Math.Min(MinimumY, point.y);
                    MaximumX = Math.Max(MaximumX, point.x);
                    MaximumY = Math.Max(MaximumY, point.y);
                }
            }

            internal PoliticalBorderGeometryCache.TerritorySite Site;
            internal List<Vec2> Polygon;
            internal uint DisplayColor;
            internal float MinimumX, MinimumY, MaximumX, MaximumY;
        }

        private struct ClipVertex
        {
            internal ClipVertex(Vec3 position, Vec2 uv) { Position = position; Uv = uv; }
            internal Vec3 Position;
            internal Vec2 Uv;

            internal static ClipVertex Lerp(ClipVertex first, ClipVertex second, float amount)
            {
                return new ClipVertex(
                    first.Position + (second.Position - first.Position) * amount,
                    first.Uv + (second.Uv - first.Uv) * amount);
            }
        }

        private sealed class CellSpatialIndex
        {
            private readonly List<int>[] _buckets = new List<int>[SpatialColumns * SpatialRows];
            private readonly float _minimumX, _minimumY, _cellWidth, _cellHeight;

            internal CellSpatialIndex(
                PoliticalBorderGeometryCache.CacheIdentity identity,
                IList<VoronoiCell> cells)
            {
                _minimumX = identity.MinimumX;
                _minimumY = identity.MinimumY;
                _cellWidth = Math.Max(GeometryEpsilon,
                    (identity.MaximumX - identity.MinimumX) / SpatialColumns);
                _cellHeight = Math.Max(GeometryEpsilon,
                    (identity.MaximumY - identity.MinimumY) / SpatialRows);
                for (int index = 0; index < cells.Count; index++)
                {
                    VoronoiCell cell = cells[index];
                    int minimumColumn = Column(cell.MinimumX);
                    int maximumColumn = Column(cell.MaximumX);
                    int minimumRow = Row(cell.MinimumY);
                    int maximumRow = Row(cell.MaximumY);
                    for (int row = minimumRow; row <= maximumRow; row++)
                    for (int column = minimumColumn; column <= maximumColumn; column++)
                    {
                        int bucketIndex = row * SpatialColumns + column;
                        if (_buckets[bucketIndex] == null) _buckets[bucketIndex] = new List<int>();
                        _buckets[bucketIndex].Add(index);
                    }
                }
            }

            internal List<int> Query(PoliticalBorderGeometryCache.Triangle triangle)
            {
                float minimumX = Math.Min(triangle.First.x, Math.Min(triangle.Second.x, triangle.Third.x));
                float maximumX = Math.Max(triangle.First.x, Math.Max(triangle.Second.x, triangle.Third.x));
                float minimumY = Math.Min(triangle.First.y, Math.Min(triangle.Second.y, triangle.Third.y));
                float maximumY = Math.Max(triangle.First.y, Math.Max(triangle.Second.y, triangle.Third.y));
                HashSet<int> unique = new HashSet<int>();
                for (int row = Row(minimumY); row <= Row(maximumY); row++)
                for (int column = Column(minimumX); column <= Column(maximumX); column++)
                {
                    List<int> bucket = _buckets[row * SpatialColumns + column];
                    if (bucket == null) continue;
                    foreach (int value in bucket) unique.Add(value);
                }
                return new List<int>(unique);
            }

            private int Column(float value)
            { return Clamp((int)Math.Floor((value - _minimumX) / _cellWidth), 0, SpatialColumns - 1); }
            private int Row(float value)
            { return Clamp((int)Math.Floor((value - _minimumY) / _cellHeight), 0, SpatialRows - 1); }
            private static int Clamp(int value, int minimum, int maximum)
            { return value < minimum ? minimum : value > maximum ? maximum : value; }
        }
    }
}
