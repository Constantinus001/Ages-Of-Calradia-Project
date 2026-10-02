using System;
using System.Collections.Generic;
using AgesOfCalradia.PoliticalBorderOverrides;
using TaleWorlds.Library;

namespace AgesOfCalradia.PoliticalBorderOptimizer
{
    /// <summary>
    /// Applies bounded user-authored overrides after protected coastal contour
    /// correction and before ribbon tessellation. It only recolors triangles
    /// that already exist, so water and island exclusions cannot gain fill.
    /// Authored border paths replace nearby generated coastal segments and are
    /// emitted as exact connected chains with shared endpoints.
    /// </summary>
    internal static class PoliticalBorderAuthoredOverrides
    {
        private const float ReplacementRadius = 3f;
        private const float MaximumSegmentLength = 1.5f;
        private const float ConnectionTolerance = 0.01f;
        private const float MaximumCoastAnchorDistance = 8f;
        private const bool SuppressGeneratedCoasts = true;
        private const uint NeutralAuthoredFillColor = 0xFFA8A096u;

        internal static bool TryApply(
            IList<PoliticalBorderGeometryCache.EntityGeometry> fill,
            List<PoliticalBorderGeometryCache.RawSegment> segments,
            PoliticalGeometryDiagnosticsRecord diagnostics)
        {
            PoliticalBorderEditorDocument document;
            List<PoliticalBorderGeometryCache.RawSegment> originalSegments =
                new List<PoliticalBorderGeometryCache.RawSegment>(segments);
            try
            {
                document = PoliticalBorderEditorDocument.Load();
                PoliticalBorderStrokeTessellator.ConfigureWidthScale(
                    document.BorderWidthScale);
                ValidateBorderConnectivity(document.Borders);
                if (document.Borders.Count == 0 && document.Fills.Count == 0
                    && document.Brushes.Count == 0
                    && !SuppressGeneratedCoasts) return false;
                int removed;
                int terrainSamples;
                int terrainFallbacks;
                int authored = ApplyBorderPaths(segments, document.Borders,
                    SuppressGeneratedCoasts, out removed, out terrainSamples,
                    out terrainFallbacks);
                int recolored = ApplyFillOverrides(
                    fill, document.Fills, document.Brushes);
                diagnostics.AuthoredBorderPathCount = document.Borders.Count;
                diagnostics.AuthoredBorderSegmentCount = authored;
                diagnostics.AuthoredReplacedCoastalSegmentCount = removed;
                diagnostics.AuthoredFillPolygonCount = document.Fills.Count;
                diagnostics.AuthoredFillBrushCount = document.Brushes.Count;
                diagnostics.AuthoredFillRecoloredTriangleCount = recolored;
                BorderOptimizerDiagnostics.Info(
                    "Authored political overrides applied: borderPaths="
                    + document.Borders.Count + "; connectedSegments=" + authored
                    + "; replacedCoastalSegments=" + removed
                    + "; localTerrainSamples=" + terrainSamples
                    + "; localTerrainFallbacks=" + terrainFallbacks
                    + "; generatedCoastsSuppressed="
                    + SuppressGeneratedCoasts + "; fillPolygons="
                    + document.Fills.Count + "; recoloredExistingFillTriangles="
                    + recolored + "; fillBrushes=" + document.Brushes.Count
                    + "; exclusionFillCreated=false.");
                return true;
            }
            catch (Exception exception)
            {
                PoliticalBorderStrokeTessellator.ConfigureWidthScale(1f);
                // XML and route resolution are file/reflection-independent
                // boundaries. Restore the source graph so an invalid route
                // cannot leave partially removed coastal geometry.
                segments.Clear();
                segments.AddRange(originalSegments);
                BorderOptimizerDiagnostics.Error(
                    "Authored political overrides were rejected; generated geometry remains active.",
                    exception);
                return false;
            }
        }

        private static void ValidateBorderConnectivity(IList<BorderPath> paths)
        {
            List<Vec2> network = new List<Vec2>();
            foreach (BorderPath path in paths)
            {
                if (path.Points.Count < 2)
                    throw new InvalidOperationException("An authored border path is empty.");
                for (int index = 1; index < path.Points.Count; index++)
                    if ((path.Points[index] - path.Points[index - 1]).Length < ConnectionTolerance)
                        throw new InvalidOperationException("An authored border contains a zero-length edge.");

                if (network.Count > 0 && !path.Closed
                    && !TouchesNetwork(path.Points[0], network)
                    && !TouchesNetwork(path.Points[path.Points.Count - 1], network))
                    throw new InvalidOperationException(
                        "Every open authored border path must connect to the existing border network.");
                network.AddRange(path.Points);
            }
        }

        private static bool TouchesNetwork(Vec2 point, IList<Vec2> network)
        {
            foreach (Vec2 candidate in network)
                if ((candidate - point).Length <= ConnectionTolerance) return true;
            return false;
        }

        private static int ApplyFillOverrides(
            IList<PoliticalBorderGeometryCache.EntityGeometry> entities,
            IList<FillPolygon> polygons,
            IList<FillBrush> brushes)
        {
            int recolored = 0;
            foreach (PoliticalBorderGeometryCache.EntityGeometry entity in entities)
            for (int triangleIndex = 0; triangleIndex < entity.Triangles.Count; triangleIndex++)
            {
                PoliticalBorderGeometryCache.Triangle triangle = entity.Triangles[triangleIndex];
                Vec2 centroid = new Vec2(
                    (triangle.First.x + triangle.Second.x + triangle.Third.x) / 3f,
                    (triangle.First.y + triangle.Second.y + triangle.Third.y) / 3f);
                uint color;
                if (TryGetBrushColor(brushes, centroid, out color))
                {
                    if (triangle.Color != color)
                    {
                        triangle.Color = color;
                        entity.Triangles[triangleIndex] = triangle;
                        recolored++;
                    }
                    continue;
                }
                for (int polygonIndex = polygons.Count - 1; polygonIndex >= 0; polygonIndex--)
                {
                    FillPolygon polygon = polygons[polygonIndex];
                    if (!Contains(polygon.Points, centroid)) continue;
                    color = polygon.Color == 0u
                        ? NeutralAuthoredFillColor : polygon.Color;
                    if (triangle.Color != color)
                    {
                        triangle.Color = color;
                        entity.Triangles[triangleIndex] = triangle;
                        recolored++;
                    }
                    break;
                }
            }
            return recolored;
        }

        private static bool TryGetBrushColor(
            IList<FillBrush> brushes,
            Vec2 point,
            out uint color)
        {
            for (int brushIndex = brushes.Count - 1; brushIndex >= 0; brushIndex--)
            {
                FillBrush brush = brushes[brushIndex];
                float radiusSquared = brush.Radius * brush.Radius;
                for (int index = 0; index < brush.Points.Count; index++)
                {
                    if ((point - brush.Points[index]).LengthSquared <= radiusSquared)
                    { color = brush.Color == 0u
                        ? NeutralAuthoredFillColor : brush.Color; return true; }
                    if (index > 0 && DistanceSquared(point,
                        brush.Points[index - 1], brush.Points[index]) <= radiusSquared)
                    { color = brush.Color == 0u
                        ? NeutralAuthoredFillColor : brush.Color; return true; }
                }
            }
            color = 0u;
            return false;
        }

        private static int ApplyBorderPaths(
            List<PoliticalBorderGeometryCache.RawSegment> segments,
            IList<BorderPath> paths,
            bool suppressGeneratedCoasts,
            out int removed,
            out int terrainSamples,
            out int terrainFallbacks)
        {
            terrainSamples = 0;
            terrainFallbacks = 0;
            List<PoliticalBorderGeometryCache.RawSegment> source =
                new List<PoliticalBorderGeometryCache.RawSegment>(segments);
            List<Edge> authoredEdges = BuildEdges(paths, source);
            List<Edge> replacementEdges = authoredEdges.FindAll(edge => edge.ReplacesGenerated);
            removed = segments.RemoveAll(segment =>
                IsNearAlignedEdge(segment, replacementEdges)
                || (PoliticalBorderChainBuilder.IsCoastal(segment)
                && (suppressGeneratedCoasts
                    || IsNearAnyEdge(Midpoint(segment.First, segment.Second), authoredEdges))));

            int added = 0;
            foreach (Edge edge in authoredEdges)
            {
                float length = (edge.Second - edge.First).Length;
                int parts = Math.Max(1, (int)Math.Ceiling(length / MaximumSegmentLength));
                for (int part = 0; part < parts; part++)
                {
                    Vec2 first = Lerp(edge.First, edge.Second, part / (float)parts);
                    Vec2 second = Lerp(edge.First, edge.Second, (part + 1) / (float)parts);
                    PoliticalBorderGeometryCache.RawSegment nearest = FindNearestCoast(
                        Midpoint(first, second), source);
                    bool firstPrepared;
                    bool secondPrepared;
                    Vec3 firstTerrain = SampleTerrain(first, nearest, out firstPrepared);
                    Vec3 secondTerrain = SampleTerrain(second, nearest, out secondPrepared);
                    terrainSamples += (firstPrepared ? 1 : 0)
                        + (secondPrepared ? 1 : 0);
                    terrainFallbacks += (firstPrepared ? 0 : 1)
                        + (secondPrepared ? 0 : 1);
                    uint color = edge.Color == 0u ? nearest.LeftColor : edge.Color;
                    segments.Add(new PoliticalBorderGeometryCache.RawSegment(
                        first, second, firstTerrain, secondTerrain, color, color,
                        edge.Style));
                    added++;
                }
            }
            return added;
        }

        private static List<Edge> BuildEdges(
            IList<BorderPath> paths,
            IList<PoliticalBorderGeometryCache.RawSegment> source)
        {
            List<Edge> result = new List<Edge>();
            foreach (BorderPath path in paths)
            {
                if (path.FollowCoast)
                    AddCoastFollowingEdges(result, path, source);
                else
                {
                    for (int index = 1; index < path.Points.Count; index++)
                        result.Add(new Edge(path.Points[index - 1], path.Points[index],
                            path.Color, ParseStyle(path.Style), path.ReplacesGenerated));
                    if (path.Closed && path.Points.Count > 2)
                        result.Add(new Edge(path.Points[path.Points.Count - 1], path.Points[0],
                            path.Color, ParseStyle(path.Style), path.ReplacesGenerated));
                }
            }
            return result;
        }

        private static void AddCoastFollowingEdges(
            ICollection<Edge> output,
            BorderPath path,
            IList<PoliticalBorderGeometryCache.RawSegment> source)
        {
            Dictionary<PoliticalBorderGeometryCache.NodeKey, Vec2> positions =
                new Dictionary<PoliticalBorderGeometryCache.NodeKey, Vec2>();
            Dictionary<PoliticalBorderGeometryCache.NodeKey,
                List<PoliticalBorderGeometryCache.NodeKey>> graph =
                new Dictionary<PoliticalBorderGeometryCache.NodeKey,
                    List<PoliticalBorderGeometryCache.NodeKey>>();
            foreach (PoliticalBorderGeometryCache.RawSegment segment in source)
            {
                if (!PoliticalBorderChainBuilder.IsCoastal(segment)) continue;
                PoliticalBorderGeometryCache.NodeKey first =
                    new PoliticalBorderGeometryCache.NodeKey(segment.First);
                PoliticalBorderGeometryCache.NodeKey second =
                    new PoliticalBorderGeometryCache.NodeKey(segment.Second);
                positions[first] = segment.First;
                positions[second] = segment.Second;
                AddNeighbor(graph, first, second);
                AddNeighbor(graph, second, first);
            }
            if (positions.Count == 0)
                throw new InvalidOperationException("The captured coastal graph is empty.");

            int pairCount = path.Closed ? path.Points.Count : path.Points.Count - 1;
            for (int index = 0; index < pairCount; index++)
            {
                Vec2 firstAnchor = path.Points[index];
                Vec2 secondAnchor = path.Points[(index + 1) % path.Points.Count];
                PoliticalBorderGeometryCache.NodeKey first = FindNearestNode(
                    firstAnchor, positions);
                PoliticalBorderGeometryCache.NodeKey second = FindNearestNode(
                    secondAnchor, positions);
                List<PoliticalBorderGeometryCache.NodeKey> route = FindRoute(
                    first, second, graph);
                for (int routeIndex = 1; routeIndex < route.Count; routeIndex++)
                    output.Add(new Edge(positions[route[routeIndex - 1]],
                        positions[route[routeIndex]], path.Color,
                        ParseStyle(path.Style), path.ReplacesGenerated));
            }
        }

        private static void AddNeighbor(
            IDictionary<PoliticalBorderGeometryCache.NodeKey,
                List<PoliticalBorderGeometryCache.NodeKey>> graph,
            PoliticalBorderGeometryCache.NodeKey first,
            PoliticalBorderGeometryCache.NodeKey second)
        {
            List<PoliticalBorderGeometryCache.NodeKey> values;
            if (!graph.TryGetValue(first, out values))
            {
                values = new List<PoliticalBorderGeometryCache.NodeKey>();
                graph.Add(first, values);
            }
            if (!values.Contains(second)) values.Add(second);
        }

        private static PoliticalBorderGeometryCache.NodeKey FindNearestNode(
            Vec2 anchor,
            IDictionary<PoliticalBorderGeometryCache.NodeKey, Vec2> positions)
        {
            bool found = false;
            float best = MaximumCoastAnchorDistance * MaximumCoastAnchorDistance;
            PoliticalBorderGeometryCache.NodeKey result =
                default(PoliticalBorderGeometryCache.NodeKey);
            foreach (KeyValuePair<PoliticalBorderGeometryCache.NodeKey, Vec2> node in positions)
            {
                float distance = (node.Value - anchor).LengthSquared;
                if (distance >= best) continue;
                best = distance;
                result = node.Key;
                found = true;
            }
            if (!found) throw new InvalidOperationException(
                "A Follow Coast anchor is more than eight map units from a captured coast.");
            return result;
        }

        private static List<PoliticalBorderGeometryCache.NodeKey> FindRoute(
            PoliticalBorderGeometryCache.NodeKey start,
            PoliticalBorderGeometryCache.NodeKey end,
            IDictionary<PoliticalBorderGeometryCache.NodeKey,
                List<PoliticalBorderGeometryCache.NodeKey>> graph)
        {
            Queue<PoliticalBorderGeometryCache.NodeKey> pending =
                new Queue<PoliticalBorderGeometryCache.NodeKey>();
            Dictionary<PoliticalBorderGeometryCache.NodeKey,
                PoliticalBorderGeometryCache.NodeKey> previous =
                new Dictionary<PoliticalBorderGeometryCache.NodeKey,
                    PoliticalBorderGeometryCache.NodeKey>();
            HashSet<PoliticalBorderGeometryCache.NodeKey> visited =
                new HashSet<PoliticalBorderGeometryCache.NodeKey>();
            pending.Enqueue(start);
            visited.Add(start);
            while (pending.Count > 0)
            {
                PoliticalBorderGeometryCache.NodeKey current = pending.Dequeue();
                if (current.Equals(end)) break;
                List<PoliticalBorderGeometryCache.NodeKey> neighbors;
                if (!graph.TryGetValue(current, out neighbors)) continue;
                foreach (PoliticalBorderGeometryCache.NodeKey neighbor in neighbors)
                {
                    if (!visited.Add(neighbor)) continue;
                    previous[neighbor] = current;
                    pending.Enqueue(neighbor);
                }
            }
            if (!visited.Contains(end)) throw new InvalidOperationException(
                "Follow Coast anchors belong to disconnected coast components.");
            List<PoliticalBorderGeometryCache.NodeKey> result =
                new List<PoliticalBorderGeometryCache.NodeKey> { end };
            PoliticalBorderGeometryCache.NodeKey step = end;
            while (!step.Equals(start))
            {
                step = previous[step];
                result.Add(step);
            }
            result.Reverse();
            return result;
        }

        private static bool IsNearAnyEdge(Vec2 point, IList<Edge> edges)
        {
            float maximumSquared = ReplacementRadius * ReplacementRadius;
            foreach (Edge edge in edges)
                if (DistanceSquared(point, edge.First, edge.Second) <= maximumSquared) return true;
            return false;
        }

        private static bool IsNearAlignedEdge(
            PoliticalBorderGeometryCache.RawSegment segment,
            IList<Edge> edges)
        {
            Vec2 sourceDirection = segment.Second - segment.First;
            if (sourceDirection.Normalize() < 0.0001f) return false;
            Vec2 midpoint = Midpoint(segment.First, segment.Second);
            float maximumSquared = ReplacementRadius * ReplacementRadius;
            foreach (Edge edge in edges)
            {
                if (DistanceSquared(midpoint, edge.First, edge.Second) > maximumSquared) continue;
                Vec2 editDirection = edge.Second - edge.First;
                if (editDirection.Normalize() < 0.0001f) continue;
                if (Math.Abs(Vec2.DotProduct(sourceDirection, editDirection)) >= 0.70f)
                    return true;
            }
            return false;
        }

        private static PoliticalBorderGeometryCache.RawSegment FindNearestCoast(
            Vec2 point,
            IList<PoliticalBorderGeometryCache.RawSegment> source)
        {
            bool found = false;
            float best = float.MaxValue;
            PoliticalBorderGeometryCache.RawSegment nearest = default(PoliticalBorderGeometryCache.RawSegment);
            foreach (PoliticalBorderGeometryCache.RawSegment segment in source)
            {
                if (!PoliticalBorderChainBuilder.IsCoastal(segment)) continue;
                float distance = DistanceSquared(point, segment.First, segment.Second);
                if (distance >= best) continue;
                best = distance;
                nearest = segment;
                found = true;
            }
            if (!found) throw new InvalidOperationException(
                "Authored borders require at least one generated coastal segment.");
            return nearest;
        }

        private static Vec3 SampleTerrain(
            Vec2 point,
            PoliticalBorderGeometryCache.RawSegment source,
            out bool prepared)
        {
            float height;
            prepared = BorderOptimizerRuntime.TrySamplePreparedHeight(point, out height);
            if (prepared) return new Vec3(point.x, point.y, height, -1f);

            Vec2 delta = source.Second - source.First;
            float denominator = delta.LengthSquared;
            float amount = denominator <= 0.000001f ? 0f : Math.Max(0f, Math.Min(1f,
                Vec2.DotProduct(point - source.First, delta) / denominator));
            height = source.FirstTerrainPoint.z
                + ((source.SecondTerrainPoint.z - source.FirstTerrainPoint.z) * amount);
            return new Vec3(point.x, point.y, height, -1f);
        }

        private static bool Contains(IList<Vec2> polygon, Vec2 point)
        {
            bool inside = false;
            for (int current = 0, previous = polygon.Count - 1;
                current < polygon.Count; previous = current++)
            {
                Vec2 first = polygon[current];
                Vec2 second = polygon[previous];
                if ((first.y > point.y) == (second.y > point.y)) continue;
                float x = (second.x - first.x) * (point.y - first.y)
                    / (second.y - first.y) + first.x;
                if (point.x < x) inside = !inside;
            }
            return inside;
        }

        private static float DistanceSquared(Vec2 point, Vec2 first, Vec2 second)
        {
            Vec2 delta = second - first;
            float denominator = delta.LengthSquared;
            float amount = denominator <= 0.000001f ? 0f : Math.Max(0f, Math.Min(1f,
                Vec2.DotProduct(point - first, delta) / denominator));
            return (point - (first + delta * amount)).LengthSquared;
        }

        private static Vec2 Lerp(Vec2 first, Vec2 second, float amount)
        { return first + ((second - first) * amount); }

        private static Vec2 Midpoint(Vec2 first, Vec2 second)
        { return (first + second) * 0.5f; }

        private static PoliticalBorderGeometryCache.BorderLineStyle ParseStyle(
            string value)
        {
            if (string.Equals(value, "Dashed", StringComparison.OrdinalIgnoreCase))
                return PoliticalBorderGeometryCache.BorderLineStyle.Dashed;
            if (string.Equals(value, "Double", StringComparison.OrdinalIgnoreCase))
                return PoliticalBorderGeometryCache.BorderLineStyle.Double;
            return PoliticalBorderGeometryCache.BorderLineStyle.Solid;
        }

        private struct Edge
        {
            internal Edge(Vec2 first, Vec2 second, uint color,
                PoliticalBorderGeometryCache.BorderLineStyle style, bool replacesGenerated)
            { First = first; Second = second; Color = color; Style = style; ReplacesGenerated = replacesGenerated; }
            internal readonly Vec2 First, Second;
            internal readonly uint Color;
            internal readonly PoliticalBorderGeometryCache.BorderLineStyle Style;
            internal readonly bool ReplacesGenerated;
        }
    }
}
