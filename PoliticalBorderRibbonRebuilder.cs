using System;
using System.Collections.Generic;
using System.Globalization;

namespace AgesOfCalradia.PoliticalBorderOptimizer
{
    /// <summary>
    /// Orchestrates the sidecar-only ribbon replacement. The protected renderer
    /// remains authoritative for accepted centerlines, colors, and exclusions;
    /// this class changes only how those captured segments are tessellated.
    /// </summary>
    internal static class PoliticalBorderRibbonRebuilder
    {
        private const int SourceQuadTrianglesPerSegment = 8;
        private const int RemovedBackfaceTrianglesPerSegment = 4;
        private const int MaximumTrianglesPerEntity = 12288;

        internal static bool TryRebuild(
            IList<PoliticalBorderGeometryCache.RawSegment> segments,
            List<PoliticalBorderGeometryCache.EntityGeometry> frontier,
            PoliticalGeometryDiagnosticsRecord diagnostics)
        {
            if (segments == null || segments.Count == 0) return false;
            List<PoliticalBorderChainBuilder.Chain> chains =
                PoliticalBorderChainBuilder.Build(segments);
            if (chains.Count == 0) return false;

            List<PoliticalBorderGeometryCache.EntityGeometry> rebuilt =
                new List<PoliticalBorderGeometryCache.EntityGeometry>();
            PoliticalBorderStrokeTessellator.Result result =
                new PoliticalBorderStrokeTessellator.Result();
            int rebuiltSegments = 0;
            int closedChains = 0;
            int emptyChains = 0;
            PoliticalBorderGeometryCache.EntityGeometry entity = null;
            foreach (PoliticalBorderChainBuilder.Chain chain in chains)
            {
                if (chain.Closed) closedChains++;
                PoliticalBorderGeometryCache.EntityGeometry chainGeometry =
                    new PoliticalBorderGeometryCache.EntityGeometry();
                if (!PoliticalBorderStrokeTessellator.TryBuild(
                    chain, segments, chainGeometry, result)) return false;
                rebuiltSegments += chain.Segments.Count;
                if (chainGeometry.Triangles.Count == 0)
                {
                    emptyChains++;
                    continue;
                }
                if (entity == null
                    || entity.Triangles.Count + chainGeometry.Triangles.Count
                        > MaximumTrianglesPerEntity)
                {
                    entity = new PoliticalBorderGeometryCache.EntityGeometry();
                    rebuilt.Add(entity);
                }
                entity.Triangles.AddRange(chainGeometry.Triangles);
            }

            if (rebuiltSegments != segments.Count || rebuilt.Count == 0) return false;
            int sourceTriangles = CountTriangles(frontier);
            frontier.Clear();
            frontier.AddRange(rebuilt);

            diagnostics.RibbonSourceTriangleCount = sourceTriangles;
            diagnostics.RibbonGeneratedTriangleCount = CountTriangles(rebuilt);
            diagnostics.RibbonRemovedCapTriangleCount = Math.Max(
                0, sourceTriangles - segments.Count * SourceQuadTrianglesPerSegment);
            diagnostics.RibbonRemovedBackfaceTriangleCount =
                segments.Count * RemovedBackfaceTrianglesPerSegment;
            diagnostics.RibbonCurvedSegmentCount = result.CurvedSegments;
            diagnostics.RibbonCurvedCoastalSegmentCount = result.CurvedCoastalSegments;
            diagnostics.RibbonSubdivisionCount =
                PoliticalBorderStrokeTessellator.MaximumCurveSubdivisions;
            diagnostics.RibbonMiteredEndpointCount = 0;
            diagnostics.RibbonWidth = PoliticalBorderStrokeTessellator.EffectiveRibbonWidth;
            diagnostics.CoastalLandLeftSegmentCount = result.LeftLandChains;
            diagnostics.CoastalLandRightSegmentCount = result.RightLandChains;
            diagnostics.CoastalAmbiguousSegmentCount = result.AmbiguousCoastalChains;
            diagnostics.RibbonUnsafeCurveFallbackSegmentCount =
                result.SuppressedUnsafeSections;
            diagnostics.RibbonRejectedInvertedTriangleCount =
                result.WindingConstrainedSections;
            diagnostics.RibbonPreparedHeightFallbackVertexCount =
                result.PreparedHeightFallbackVertices;
            diagnostics.RibbonChainCount = chains.Count;
            diagnostics.RibbonClosedChainCount = closedChains;
            diagnostics.RibbonEmptyChainCount = emptyChains;
            diagnostics.RibbonSharedCrossSectionCount = result.SharedCrossSections;
            diagnostics.RibbonWidthConstrainedSampleCount = result.WidthConstrainedSamples;
            diagnostics.RibbonSuppressedUnsafeSectionCount = result.SuppressedUnsafeSections;
            diagnostics.RibbonUnsafeCoastalSampleCount = result.UnsafeCoastalSamples;

            BorderOptimizerDiagnostics.Info(
                "Shared-chain political ribbon rebuilt: acceptedSegments=" + segments.Count
                + "; chains=" + chains.Count
                + "; closedChains=" + closedChains
                + "; emptyExcludedChains=" + emptyChains
                + "; sharedCrossSections=" + result.SharedCrossSections
                + "; curvedSegments=" + result.CurvedSegments
                + "; curvedCoastalSegments=" + result.CurvedCoastalSegments
                + "; adaptiveCurveSubdivisions="
                + PoliticalBorderStrokeTessellator.MinimumCurveSubdivisions
                + "-" + PoliticalBorderStrokeTessellator.MaximumCurveSubdivisions
                + "; coastalMaskCoverWidth="
                + PoliticalBorderStrokeTessellator.CoastalMaskCoverWidth
                    .ToString("F2", CultureInfo.InvariantCulture)
                + "; coastalSamples=" + result.CoastalSamples
                + "; coastalLandHeightSamples="
                + result.CoastalLandHeightSamples
                + "; coastalLandHeightRejectedSamples="
                + result.CoastalLandHeightRejectedSamples
                + "; coastalLandHeightExpandedSearchSamples="
                + result.CoastalLandHeightExpandedSearchSamples
                + "; coastalLandHeightInterpolatedSamples="
                + result.CoastalLandHeightInterpolatedSamples
                + "; coastalLandHeightUnresolvedSamples="
                + result.CoastalLandHeightUnresolvedSamples
                + "; coastalLandHeightLiftedVertices="
                + result.CoastalLandHeightLiftedVertices
                + "; roundedEndpointCaps=" + result.RoundedEndpointCaps
                + "; roundedEndpointCapTriangles="
                + result.RoundedEndpointCapTriangles
                + "; emittedCoastalSections="
                + result.EmittedCoastalSections
                + "; sourceTriangles=" + sourceTriangles
                + "; generatedTriangles=" + diagnostics.RibbonGeneratedTriangleCount
                + "; widthConstrainedSamples=" + result.WidthConstrainedSamples
                + "; windingConstrainedSections=" + result.WindingConstrainedSections
                + "; unsafeCoastalSamples=" + result.UnsafeCoastalSamples
                + "; suppressedUnsafeSections=" + result.SuppressedUnsafeSections
                + "; alternativeDiagonalSections=" + result.AlternativeDiagonalSections
                + "; preparedHeightFallbackVertices=" + result.PreparedHeightFallbackVertices
                + "; width=" + PoliticalBorderStrokeTessellator.RibbonWidth
                    .ToString("F1", CultureInfo.InvariantCulture)
                + "; widthScale=" + PoliticalBorderStrokeTessellator.BorderWidthScale
                    .ToString("F2", CultureInfo.InvariantCulture)
                + "; effectiveWidth="
                + PoliticalBorderStrokeTessellator.EffectiveRibbonWidth
                    .ToString("F2", CultureInfo.InvariantCulture)
                + "; coastalWidth="
                + PoliticalBorderStrokeTessellator.CoastalRibbonWidth
                    .ToString("F1", CultureInfo.InvariantCulture)
                + "; coastalLandWidth="
                + PoliticalBorderStrokeTessellator.CoastalLandWidth
                    .ToString("F1", CultureInfo.InvariantCulture)
                + "; coastalOuterWidth="
                + PoliticalBorderStrokeTessellator.CoastalOuterWidth
                    .ToString("F1", CultureInfo.InvariantCulture)
                + "; coastalVisibilityClearance="
                + PoliticalBorderStrokeTessellator.CoastalVisibilityClearance
                    .ToString("F1", CultureInfo.InvariantCulture)
                + "; roundedCapStepsPerHalf="
                + PoliticalBorderStrokeTessellator.RoundedCapStepsPerHalf
                + "; validatedCenterlinesAuthoritative=true"
                + "; islandExclusionPreserved=true.");
            result.DiagnosticSamples.Log();
            return true;
        }

        private static int CountTriangles(
            IEnumerable<PoliticalBorderGeometryCache.EntityGeometry> entities)
        {
            int count = 0;
            foreach (PoliticalBorderGeometryCache.EntityGeometry entity in entities)
                count += entity.Triangles.Count;
            return count;
        }
    }
}
