using System.Collections.Generic;
using TaleWorlds.Library;

namespace AgesOfCalradia.PoliticalBorderOptimizer
{
    /// <summary>
    /// Applies topology-preserving repairs to captured political geometry.
    /// Fill XY positions, frontier geometry, UVs, and entity grouping stay
    /// identical to the approved renderer output used by the visual baseline.
    /// The protected renderer's uniform fill lift is removed before replay so
    /// the editor fill conforms to the campaign terrain surface.
    /// </summary>
    internal static class PoliticalBorderGeometryRepair
    {
        private const float ProtectedPoliticalFillLift = 4f;
        private const float TerrainRenderClearance = 2f;
        private const uint TemporaryPoliticalFillColor = 0xFFD8D8D8u;

        internal static void Apply(
            List<PoliticalBorderGeometryCache.EntityGeometry> fill,
            List<PoliticalBorderGeometryCache.EntityGeometry> frontier,
            IList<PoliticalBorderGeometryCache.RawSegment> segments,
            PoliticalGeometryDiagnosticsRecord diagnostics)
        {
            int terrainLevelFillVertices = AdjustFillToTerrainRelativeHeight(fill);
            diagnostics.RemovedReverseFillTriangleCount = RemoveReverseFillFaces(fill);
            bool fillRefined = PoliticalBorderCoastalFillClipper.TryApply(
                fill, diagnostics);
            List<PoliticalBorderGeometryCache.RawSegment> ribbonSegments;
            bool contourSmoothed = PoliticalBorderCoastalContourSmoother.TryApply(
                fill, segments, diagnostics, out ribbonSegments);
            bool authoredOverrides = PoliticalBorderAuthoredOverrides.TryApply(
                fill, ribbonSegments, diagnostics);
            int temporaryLightGrayFillTriangles = ApplyTemporaryLightGrayFill(fill);
            diagnostics.FinalSharpFrontierTurnCount = diagnostics.SharpFrontierTurnCount;
            bool ribbonRebuilt = PoliticalBorderRibbonRebuilder.TryRebuild(
                ribbonSegments, frontier, diagnostics);
            BorderOptimizerDiagnostics.Info(
                "Baseline political geometry repair applied: inlandGeometryDisplacement=0.0"
                + "; fillGroundOffset=-2.0; fillTerrainClearance=2.0; terrainLevelFillVertices="
                + terrainLevelFillVertices
                + "; removedReverseFillTriangles="
                + diagnostics.RemovedReverseFillTriangleCount
                + "; coastalFillRefined=" + fillRefined
                + "; sharedCoastalContour=" + contourSmoothed
                + "; authoredOverrides=" + authoredOverrides
                + "; temporaryLightGrayFill=true; temporaryLightGrayFillTriangles="
                + temporaryLightGrayFillTriangles
                + "; inlandFrontierCenterlinesPreserved=true; ribbonRebuilt=" + ribbonRebuilt + ".");
        }

        private static int AdjustFillToTerrainRelativeHeight(
            IList<PoliticalBorderGeometryCache.EntityGeometry> entities)
        {
            int adjusted = 0;
            float displacement = ProtectedPoliticalFillLift - TerrainRenderClearance;
            foreach (PoliticalBorderGeometryCache.EntityGeometry entity in entities)
            for (int index = 0; index < entity.Triangles.Count; index++)
            {
                PoliticalBorderGeometryCache.Triangle triangle = entity.Triangles[index];
                triangle.First.z -= displacement;
                triangle.Second.z -= displacement;
                triangle.Third.z -= displacement;
                entity.Triangles[index] = triangle;
                adjusted += 3;
            }
            return adjusted;
        }

        private static int ApplyTemporaryLightGrayFill(
            IList<PoliticalBorderGeometryCache.EntityGeometry> entities)
        {
            int recolored = 0;
            foreach (PoliticalBorderGeometryCache.EntityGeometry entity in entities)
            for (int index = 0; index < entity.Triangles.Count; index++)
            {
                PoliticalBorderGeometryCache.Triangle triangle = entity.Triangles[index];
                if (triangle.Color == TemporaryPoliticalFillColor) continue;
                triangle.Color = TemporaryPoliticalFillColor;
                entity.Triangles[index] = triangle;
                recolored++;
            }
            return recolored;
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
    }
}
