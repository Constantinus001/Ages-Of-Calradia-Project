using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TaleWorlds.Library;
using BinaryReader = System.IO.BinaryReader;
using BinaryWriter = System.IO.BinaryWriter;

namespace AgesOfCalradia.PoliticalBorderOptimizer
{
    internal static class PoliticalBorderGeometryDiagnostics
    {
        private const int MaximumDiagnosticTriangles = 2000000;

        internal static PoliticalGeometryDiagnosticsRecord Analyze(
            IList<PoliticalBorderGeometryCache.EntityGeometry> fill,
            IList<PoliticalBorderGeometryCache.EntityGeometry> frontier,
            IList<PoliticalBorderGeometryCache.RawSegment> segments)
        {
            PoliticalGeometryDiagnosticsRecord result = new PoliticalGeometryDiagnosticsRecord();
            result.FillEntityCount = fill.Count;
            result.FrontierEntityCount = frontier.Count;
            AnalyzeTriangles(fill, result, true);
            AnalyzeTriangles(frontier, result, false);
            result.FrontierSegmentCount = segments.Count;

            Dictionary<PoliticalBorderGeometryCache.NodeKey, Vec2> positions =
                new Dictionary<PoliticalBorderGeometryCache.NodeKey, Vec2>();
            Dictionary<PoliticalBorderGeometryCache.NodeKey,
                HashSet<PoliticalBorderGeometryCache.NodeKey>> graph =
                new Dictionary<PoliticalBorderGeometryCache.NodeKey,
                    HashSet<PoliticalBorderGeometryCache.NodeKey>>();
            HashSet<EdgeKey> uniqueEdges = new HashSet<EdgeKey>();
            foreach (PoliticalBorderGeometryCache.RawSegment segment in segments)
            {
                PoliticalBorderGeometryCache.NodeKey first =
                    new PoliticalBorderGeometryCache.NodeKey(segment.First);
                PoliticalBorderGeometryCache.NodeKey second =
                    new PoliticalBorderGeometryCache.NodeKey(segment.Second);
                positions[first] = segment.First;
                positions[second] = segment.Second;
                if (first.Equals(second)) result.ZeroLengthFrontierSegments++;
                if (!uniqueEdges.Add(new EdgeKey(first, second))) result.DuplicateFrontierSegments++;
                AddNeighbor(graph, first, second);
                AddNeighbor(graph, second, first);
                result.MaximumFrontierSegmentLength = Math.Max(
                    result.MaximumFrontierSegmentLength, (segment.Second - segment.First).Length);
            }
            result.FrontierNodeCount = positions.Count;
            foreach (KeyValuePair<PoliticalBorderGeometryCache.NodeKey, Vec2> node in positions)
            {
                HashSet<PoliticalBorderGeometryCache.NodeKey> adjacent;
                if (!graph.TryGetValue(node.Key, out adjacent)) continue;
                result.MaximumFrontierDegree = Math.Max(result.MaximumFrontierDegree, adjacent.Count);
                if (adjacent.Count == 1) result.FrontierEndpointCount++;
                else if (adjacent.Count > 2) result.FrontierJunctionCount++;
                else if (adjacent.Count == 2)
                {
                    PoliticalBorderGeometryCache.NodeKey[] pair =
                        new PoliticalBorderGeometryCache.NodeKey[2];
                    adjacent.CopyTo(pair);
                    Vec2 firstDirection = positions[pair[0]] - node.Value;
                    Vec2 secondDirection = positions[pair[1]] - node.Value;
                    if (firstDirection.Normalize() > 0.0001f
                        && secondDirection.Normalize() > 0.0001f)
                    {
                        float straightness = Math.Max(-1f, Math.Min(1f,
                            Vec2.DotProduct(firstDirection, secondDirection)));
                        if (straightness > -0.819152f) result.SharpFrontierTurnCount++;
                    }
                }
            }
            return result;
        }

        internal static void UpdateOutputCounts(
            IList<PoliticalBorderGeometryCache.EntityGeometry> fill,
            IList<PoliticalBorderGeometryCache.EntityGeometry> frontier,
            PoliticalGeometryDiagnosticsRecord diagnostics)
        {
            diagnostics.FillEntityCount = 0;
            diagnostics.FrontierEntityCount = 0;
            diagnostics.FillTriangleCount = 0;
            diagnostics.FrontierTriangleCount = 0;

            foreach (PoliticalBorderGeometryCache.EntityGeometry entity in fill)
            {
                if (entity.Triangles.Count == 0)
                {
                    continue;
                }

                diagnostics.FillEntityCount++;
                diagnostics.FillTriangleCount += entity.Triangles.Count;
            }

            foreach (PoliticalBorderGeometryCache.EntityGeometry entity in frontier)
            {
                if (entity.Triangles.Count == 0)
                {
                    continue;
                }

                diagnostics.FrontierEntityCount++;
                diagnostics.FrontierTriangleCount += entity.Triangles.Count;
            }

            BorderOptimizerDiagnostics.Info(
                "Final political geometry output: fillEntities="
                + diagnostics.FillEntityCount
                + "; fillTriangles=" + diagnostics.FillTriangleCount
                + "; frontierEntities=" + diagnostics.FrontierEntityCount
                + "; frontierTriangles=" + diagnostics.FrontierTriangleCount + ".");
        }

        internal static void Write(BinaryWriter writer, PoliticalGeometryDiagnosticsRecord value)
        {
            writer.Write(value.FillEntityCount); writer.Write(value.FrontierEntityCount);
            writer.Write(value.FillTriangleCount); writer.Write(value.FrontierTriangleCount);
            writer.Write(value.DegenerateTriangleCount); writer.Write(value.OversizedTriangleCount);
            writer.Write(value.SourceTiltedFillTriangleCount);
            writer.Write(value.MaximumTriangleEdge); writer.Write(value.MaximumTriangleArea);
            writer.Write(value.FrontierSegmentCount); writer.Write(value.FrontierNodeCount);
            writer.Write(value.FrontierEndpointCount); writer.Write(value.FrontierJunctionCount);
            writer.Write(value.MaximumFrontierDegree); writer.Write(value.SharpFrontierTurnCount);
            writer.Write(value.DuplicateFrontierSegments); writer.Write(value.ZeroLengthFrontierSegments);
            writer.Write(value.MaximumFrontierSegmentLength); writer.Write(value.SmoothedNodeCount);
            writer.Write(value.TotalSmoothingDisplacement); writer.Write(value.MaximumSmoothingDisplacement);
            writer.Write(value.SmoothingPassCount); writer.Write(value.FinalSharpFrontierTurnCount);
            writer.Write(value.FillAdjustedVertexCount); writer.Write(value.FrontierAdjustedVertexCount);
            writer.Write(value.RemovedReverseFillTriangleCount);
            writer.Write(value.VoronoiCellCount); writer.Write(value.VoronoiSourceTriangleCount);
            writer.Write(value.VoronoiGeneratedTriangleCount); writer.Write(value.VoronoiSplitSourceTriangleCount);
            writer.Write(value.VoronoiDiscardedDegeneratePolygonCount);
            writer.Write(value.VoronoiOutsideLandTriangleCount);
            writer.Write(value.PreservedExclusionFrontierSegmentCount);
            writer.Write(value.RemovedLegacyInternalFrontierSegmentCount);
            writer.Write(value.ExactFrontierBoundaryEdgeCount);
            writer.Write(value.ExactFrontierSegmentCount);
            writer.Write(value.RejectedOutsideLandFrontierSegmentCount);
            writer.Write(value.RibbonSourceTriangleCount);
            writer.Write(value.RibbonGeneratedTriangleCount);
            writer.Write(value.RibbonRemovedCapTriangleCount);
            writer.Write(value.RibbonMiteredEndpointCount);
            writer.Write(value.RibbonWidth);
            writer.Write(value.RibbonRemovedBackfaceTriangleCount);
            writer.Write(value.RibbonCurvedSegmentCount);
            writer.Write(value.RibbonCurvedCoastalSegmentCount);
            writer.Write(value.RibbonSubdivisionCount);
            writer.Write(value.CoastalSegmentCount); writer.Write(value.CoastalNodeCount);
            writer.Write(value.CoastalEndpointCount); writer.Write(value.CoastalJunctionCount);
            writer.Write(value.CoastalSharpTurnCount);
            writer.Write(value.CoastalMaximumSegmentLength); writer.Write(value.CoastalMaximumHeightDelta);
            writer.Write(value.SourceFillReversePairCount);
            writer.Write(value.SourceFrontierReversePairCount);
            writer.Write(value.SourceNearCoastFillTriangleCount);
            writer.Write(value.SourceNearCoastTiltedFillTriangleCount);
            writer.Write(value.SourceNearCoastHighReliefFillTriangleCount);
            writer.Write(value.SourceNearCoastLargeFillTriangleCount);
            writer.Write(value.SourceNearCoastFrontierTriangleCount);
            writer.Write(value.SourceNearCoastNonUpwardFrontierTriangleCount);
            writer.Write(value.SourceNearCoastDegenerateFrontierTriangleCount);
            writer.Write(value.OutputFillReversePairCount); writer.Write(value.OutputFrontierReversePairCount);
            writer.Write(value.OutputNearCoastFillTriangleCount);
            writer.Write(value.OutputNearCoastFrontierTriangleCount);
            writer.Write(value.OutputNearCoastNonUpwardTriangleCount);
            writer.Write(value.OutputNearCoastDegenerateTriangleCount);
            writer.Write(value.CoastalLandLeftSegmentCount);
            writer.Write(value.CoastalLandRightSegmentCount);
            writer.Write(value.CoastalAmbiguousSegmentCount);
            writer.Write(value.RibbonUnsafeCurveFallbackSegmentCount);
            writer.Write(value.RibbonRejectedInvertedTriangleCount);
            writer.Write(value.RibbonPreparedHeightFallbackVertexCount);
            writer.Write(value.RibbonChainCount);
            writer.Write(value.RibbonClosedChainCount);
            writer.Write(value.RibbonEmptyChainCount);
            writer.Write(value.RibbonSharedCrossSectionCount);
            writer.Write(value.RibbonWidthConstrainedSampleCount);
            writer.Write(value.RibbonSuppressedUnsafeSectionCount);
            writer.Write(value.RibbonUnsafeCoastalSampleCount);
            writer.Write(value.CoastalFillClipSourceTriangleCount);
            writer.Write(value.CoastalFillClipGeneratedTriangleCount);
            writer.Write(value.CoastalFillClipUnchangedTriangleCount);
            writer.Write(value.CoastalFillClipSubdividedTriangleCount);
            writer.Write(value.CoastalFillClipDiscardedTriangleCount);
            writer.Write(value.CoastalFillClipBoundaryVertexCount);
            writer.Write(value.CoastalFillClipMaximumDepth);
            writer.Write(value.CoastalContourBoundaryEdgeCount);
            writer.Write(value.CoastalContourBoundaryNodeCount);
            writer.Write(value.CoastalContourMovedNodeCount);
            writer.Write(value.CoastalContourRejectedNodeCount);
            writer.Write(value.CoastalContourAdjustedFillVertexCount);
            writer.Write(value.CoastalContourRibbonSegmentCount);
            writer.Write(value.CoastalContourMaximumDisplacement);
            writer.Write(value.CoastalContourTotalDisplacement);
            writer.Write(value.AuthoredBorderPathCount);
            writer.Write(value.AuthoredBorderSegmentCount);
            writer.Write(value.AuthoredReplacedCoastalSegmentCount);
            writer.Write(value.AuthoredFillPolygonCount);
            writer.Write(value.AuthoredFillRecoloredTriangleCount);
            writer.Write(value.AuthoredFillBrushCount);
        }

        internal static PoliticalGeometryDiagnosticsRecord Read(BinaryReader reader)
        {
            PoliticalGeometryDiagnosticsRecord value = new PoliticalGeometryDiagnosticsRecord();
            value.FillEntityCount = reader.ReadInt32(); value.FrontierEntityCount = reader.ReadInt32();
            value.FillTriangleCount = reader.ReadInt32(); value.FrontierTriangleCount = reader.ReadInt32();
            value.DegenerateTriangleCount = reader.ReadInt32(); value.OversizedTriangleCount = reader.ReadInt32();
            value.SourceTiltedFillTriangleCount = reader.ReadInt32();
            value.MaximumTriangleEdge = reader.ReadSingle(); value.MaximumTriangleArea = reader.ReadSingle();
            value.FrontierSegmentCount = reader.ReadInt32(); value.FrontierNodeCount = reader.ReadInt32();
            value.FrontierEndpointCount = reader.ReadInt32(); value.FrontierJunctionCount = reader.ReadInt32();
            value.MaximumFrontierDegree = reader.ReadInt32(); value.SharpFrontierTurnCount = reader.ReadInt32();
            value.DuplicateFrontierSegments = reader.ReadInt32(); value.ZeroLengthFrontierSegments = reader.ReadInt32();
            value.MaximumFrontierSegmentLength = reader.ReadSingle(); value.SmoothedNodeCount = reader.ReadInt32();
            value.TotalSmoothingDisplacement = reader.ReadSingle(); value.MaximumSmoothingDisplacement = reader.ReadSingle();
            value.SmoothingPassCount = reader.ReadInt32(); value.FinalSharpFrontierTurnCount = reader.ReadInt32();
            value.FillAdjustedVertexCount = reader.ReadInt32(); value.FrontierAdjustedVertexCount = reader.ReadInt32();
            value.RemovedReverseFillTriangleCount = reader.ReadInt32();
            value.VoronoiCellCount = reader.ReadInt32(); value.VoronoiSourceTriangleCount = reader.ReadInt32();
            value.VoronoiGeneratedTriangleCount = reader.ReadInt32(); value.VoronoiSplitSourceTriangleCount = reader.ReadInt32();
            value.VoronoiDiscardedDegeneratePolygonCount = reader.ReadInt32();
            value.VoronoiOutsideLandTriangleCount = reader.ReadInt32();
            value.PreservedExclusionFrontierSegmentCount = reader.ReadInt32();
            value.RemovedLegacyInternalFrontierSegmentCount = reader.ReadInt32();
            value.ExactFrontierBoundaryEdgeCount = reader.ReadInt32();
            value.ExactFrontierSegmentCount = reader.ReadInt32();
            value.RejectedOutsideLandFrontierSegmentCount = reader.ReadInt32();
            value.RibbonSourceTriangleCount = reader.ReadInt32();
            value.RibbonGeneratedTriangleCount = reader.ReadInt32();
            value.RibbonRemovedCapTriangleCount = reader.ReadInt32();
            value.RibbonMiteredEndpointCount = reader.ReadInt32();
            value.RibbonWidth = reader.ReadSingle();
            value.RibbonRemovedBackfaceTriangleCount = reader.ReadInt32();
            value.RibbonCurvedSegmentCount = reader.ReadInt32();
            value.RibbonCurvedCoastalSegmentCount = reader.ReadInt32();
            value.RibbonSubdivisionCount = reader.ReadInt32();
            value.CoastalSegmentCount = reader.ReadInt32(); value.CoastalNodeCount = reader.ReadInt32();
            value.CoastalEndpointCount = reader.ReadInt32(); value.CoastalJunctionCount = reader.ReadInt32();
            value.CoastalSharpTurnCount = reader.ReadInt32();
            value.CoastalMaximumSegmentLength = reader.ReadSingle();
            value.CoastalMaximumHeightDelta = reader.ReadSingle();
            value.SourceFillReversePairCount = reader.ReadInt32();
            value.SourceFrontierReversePairCount = reader.ReadInt32();
            value.SourceNearCoastFillTriangleCount = reader.ReadInt32();
            value.SourceNearCoastTiltedFillTriangleCount = reader.ReadInt32();
            value.SourceNearCoastHighReliefFillTriangleCount = reader.ReadInt32();
            value.SourceNearCoastLargeFillTriangleCount = reader.ReadInt32();
            value.SourceNearCoastFrontierTriangleCount = reader.ReadInt32();
            value.SourceNearCoastNonUpwardFrontierTriangleCount = reader.ReadInt32();
            value.SourceNearCoastDegenerateFrontierTriangleCount = reader.ReadInt32();
            value.OutputFillReversePairCount = reader.ReadInt32();
            value.OutputFrontierReversePairCount = reader.ReadInt32();
            value.OutputNearCoastFillTriangleCount = reader.ReadInt32();
            value.OutputNearCoastFrontierTriangleCount = reader.ReadInt32();
            value.OutputNearCoastNonUpwardTriangleCount = reader.ReadInt32();
            value.OutputNearCoastDegenerateTriangleCount = reader.ReadInt32();
            value.CoastalLandLeftSegmentCount = reader.ReadInt32();
            value.CoastalLandRightSegmentCount = reader.ReadInt32();
            value.CoastalAmbiguousSegmentCount = reader.ReadInt32();
            value.RibbonUnsafeCurveFallbackSegmentCount = reader.ReadInt32();
            value.RibbonRejectedInvertedTriangleCount = reader.ReadInt32();
            value.RibbonPreparedHeightFallbackVertexCount = reader.ReadInt32();
            value.RibbonChainCount = reader.ReadInt32();
            value.RibbonClosedChainCount = reader.ReadInt32();
            value.RibbonEmptyChainCount = reader.ReadInt32();
            value.RibbonSharedCrossSectionCount = reader.ReadInt32();
            value.RibbonWidthConstrainedSampleCount = reader.ReadInt32();
            value.RibbonSuppressedUnsafeSectionCount = reader.ReadInt32();
            value.RibbonUnsafeCoastalSampleCount = reader.ReadInt32();
            value.CoastalFillClipSourceTriangleCount = reader.ReadInt32();
            value.CoastalFillClipGeneratedTriangleCount = reader.ReadInt32();
            value.CoastalFillClipUnchangedTriangleCount = reader.ReadInt32();
            value.CoastalFillClipSubdividedTriangleCount = reader.ReadInt32();
            value.CoastalFillClipDiscardedTriangleCount = reader.ReadInt32();
            value.CoastalFillClipBoundaryVertexCount = reader.ReadInt32();
            value.CoastalFillClipMaximumDepth = reader.ReadInt32();
            value.CoastalContourBoundaryEdgeCount = reader.ReadInt32();
            value.CoastalContourBoundaryNodeCount = reader.ReadInt32();
            value.CoastalContourMovedNodeCount = reader.ReadInt32();
            value.CoastalContourRejectedNodeCount = reader.ReadInt32();
            value.CoastalContourAdjustedFillVertexCount = reader.ReadInt32();
            value.CoastalContourRibbonSegmentCount = reader.ReadInt32();
            value.CoastalContourMaximumDisplacement = reader.ReadSingle();
            value.CoastalContourTotalDisplacement = reader.ReadSingle();
            value.AuthoredBorderPathCount = reader.ReadInt32();
            value.AuthoredBorderSegmentCount = reader.ReadInt32();
            value.AuthoredReplacedCoastalSegmentCount = reader.ReadInt32();
            value.AuthoredFillPolygonCount = reader.ReadInt32();
            value.AuthoredFillRecoloredTriangleCount = reader.ReadInt32();
            value.AuthoredFillBrushCount = reader.ReadInt32();
            if (value.FillTriangleCount < 0 || value.FrontierTriangleCount < 0
                || value.FillTriangleCount + value.FrontierTriangleCount > MaximumDiagnosticTriangles)
                throw new InvalidDataException("Cached geometry diagnostics are invalid.");
            if (value.SmoothingPassCount < 0 || value.SmoothingPassCount > 32
                || value.FinalSharpFrontierTurnCount < 0
                || value.FillAdjustedVertexCount < 0 || value.FrontierAdjustedVertexCount < 0
                || value.RemovedReverseFillTriangleCount < 0
                || value.VoronoiCellCount < 0 || value.VoronoiSourceTriangleCount < 0
                || value.VoronoiGeneratedTriangleCount < 0 || value.VoronoiSplitSourceTriangleCount < 0
                || value.VoronoiDiscardedDegeneratePolygonCount < 0
                || value.VoronoiOutsideLandTriangleCount < 0
                || value.PreservedExclusionFrontierSegmentCount < 0
                || value.RemovedLegacyInternalFrontierSegmentCount < 0
                || value.ExactFrontierBoundaryEdgeCount < 0
                || value.ExactFrontierSegmentCount < 0
                || value.RejectedOutsideLandFrontierSegmentCount < 0
                || value.RibbonSourceTriangleCount < 0
                || value.RibbonGeneratedTriangleCount < 0
                || value.RibbonRemovedCapTriangleCount < 0
                || value.RibbonMiteredEndpointCount < 0
                || value.RibbonWidth < 0f || value.RibbonWidth > 10f
                || value.RibbonRemovedBackfaceTriangleCount < 0
                || value.RibbonCurvedSegmentCount < 0
                || value.RibbonCurvedCoastalSegmentCount < 0
                || value.RibbonSubdivisionCount < 0 || value.RibbonSubdivisionCount > 16
                || value.CoastalSegmentCount < 0 || value.CoastalNodeCount < 0
                || value.CoastalEndpointCount < 0 || value.CoastalJunctionCount < 0
                || value.CoastalSharpTurnCount < 0
                || value.CoastalMaximumSegmentLength < 0f || value.CoastalMaximumHeightDelta < 0f
                || value.SourceFillReversePairCount < 0
                || value.SourceFrontierReversePairCount < 0
                || value.SourceNearCoastFillTriangleCount < 0
                || value.SourceNearCoastTiltedFillTriangleCount < 0
                || value.SourceNearCoastHighReliefFillTriangleCount < 0
                || value.SourceNearCoastLargeFillTriangleCount < 0
                || value.SourceNearCoastFrontierTriangleCount < 0
                || value.SourceNearCoastNonUpwardFrontierTriangleCount < 0
                || value.SourceNearCoastDegenerateFrontierTriangleCount < 0
                || value.OutputFillReversePairCount < 0
                || value.OutputFrontierReversePairCount < 0
                || value.OutputNearCoastFillTriangleCount < 0
                || value.OutputNearCoastFrontierTriangleCount < 0
                || value.OutputNearCoastNonUpwardTriangleCount < 0
                || value.OutputNearCoastDegenerateTriangleCount < 0
                || value.CoastalLandLeftSegmentCount < 0
                || value.CoastalLandRightSegmentCount < 0
                || value.CoastalAmbiguousSegmentCount < 0
                || value.RibbonUnsafeCurveFallbackSegmentCount < 0
                || value.RibbonRejectedInvertedTriangleCount < 0
                || value.RibbonPreparedHeightFallbackVertexCount < 0
                || value.RibbonChainCount < 0 || value.RibbonClosedChainCount < 0
                || value.RibbonEmptyChainCount < 0
                || value.RibbonSharedCrossSectionCount < 0
                || value.RibbonWidthConstrainedSampleCount < 0
                || value.RibbonSuppressedUnsafeSectionCount < 0
                || value.RibbonUnsafeCoastalSampleCount < 0
                || value.CoastalFillClipSourceTriangleCount < 0
                || value.CoastalFillClipGeneratedTriangleCount < 0
                || value.CoastalFillClipUnchangedTriangleCount < 0
                || value.CoastalFillClipSubdividedTriangleCount < 0
                || value.CoastalFillClipDiscardedTriangleCount < 0
                || value.CoastalFillClipBoundaryVertexCount < 0
                || value.CoastalFillClipMaximumDepth < 0
                || value.CoastalFillClipMaximumDepth > 8
                || value.CoastalContourBoundaryEdgeCount < 0
                || value.CoastalContourBoundaryNodeCount < 0
                || value.CoastalContourMovedNodeCount < 0
                || value.CoastalContourRejectedNodeCount < 0
                || value.CoastalContourAdjustedFillVertexCount < 0
                || value.CoastalContourRibbonSegmentCount < 0
                || value.CoastalContourMaximumDisplacement < 0f
                || value.CoastalContourMaximumDisplacement > 1f
                || value.CoastalContourTotalDisplacement < 0f
                || value.AuthoredBorderPathCount < 0
                || value.AuthoredBorderSegmentCount < 0
                || value.AuthoredReplacedCoastalSegmentCount < 0
                || value.AuthoredFillPolygonCount < 0
                || value.AuthoredFillRecoloredTriangleCount < 0
                || value.AuthoredFillBrushCount < 0)
                throw new InvalidDataException("Cached smoothing diagnostics are invalid.");
            return value;
        }

        internal static void Log(
            string source,
            PoliticalBorderGeometryCache.CacheIdentity identity,
            PoliticalGeometryDiagnosticsRecord value)
        {
            float averageDisplacement = value.SmoothedNodeCount == 0 ? 0f
                : value.TotalSmoothingDisplacement / value.SmoothedNodeCount;
            BorderOptimizerDiagnostics.Info("Political geometry diagnostics: source=" + source
                + "; key=" + identity.Key.Substring(0, 12)
                + "; fillEntities=" + value.FillEntityCount
                + "; frontierEntities=" + value.FrontierEntityCount
                + "; fillTriangles=" + value.FillTriangleCount
                + "; frontierTriangles=" + value.FrontierTriangleCount
                + "; degenerateTriangles=" + value.DegenerateTriangleCount
                + "; oversizedTriangles=" + value.OversizedTriangleCount
                + "; maximumTriangleEdge=" + Format(value.MaximumTriangleEdge)
                + "; maximumTriangleArea=" + Format(value.MaximumTriangleArea)
                + "; sourceTiltedFillTriangles=" + value.SourceTiltedFillTriangleCount
                + "; replayFlatNormals=true"
                + "; frontierSegments=" + value.FrontierSegmentCount
                + "; frontierNodes=" + value.FrontierNodeCount
                + "; frontierEndpoints=" + value.FrontierEndpointCount
                + "; frontierJunctions=" + value.FrontierJunctionCount
                + "; maximumFrontierDegree=" + value.MaximumFrontierDegree
                + "; sharpFrontierTurns=" + value.SharpFrontierTurnCount
                + "; duplicateFrontierSegments=" + value.DuplicateFrontierSegments
                + "; zeroLengthFrontierSegments=" + value.ZeroLengthFrontierSegments
                + "; maximumFrontierSegmentLength=" + Format(value.MaximumFrontierSegmentLength)
                + "; smoothingPasses=" + value.SmoothingPassCount
                + "; smoothedNodes=" + value.SmoothedNodeCount
                + "; finalSharpFrontierTurns=" + value.FinalSharpFrontierTurnCount
                + "; fillAdjustedVertices=" + value.FillAdjustedVertexCount
                + "; frontierAdjustedVertices=" + value.FrontierAdjustedVertexCount
                + "; removedReverseFillTriangles=" + value.RemovedReverseFillTriangleCount
                + "; voronoiCells=" + value.VoronoiCellCount
                + "; voronoiSourceTriangles=" + value.VoronoiSourceTriangleCount
                + "; voronoiGeneratedTriangles=" + value.VoronoiGeneratedTriangleCount
                + "; voronoiSplitSourceTriangles=" + value.VoronoiSplitSourceTriangleCount
                + "; voronoiDiscardedDegeneratePolygons=" + value.VoronoiDiscardedDegeneratePolygonCount
                + "; voronoiOutsideLandTriangles=" + value.VoronoiOutsideLandTriangleCount
                + "; islandExclusionPreserved=" + (value.VoronoiOutsideLandTriangleCount == 0)
                + "; preservedExclusionFrontierSegments=" + value.PreservedExclusionFrontierSegmentCount
                + "; removedLegacyInternalFrontierSegments=" + value.RemovedLegacyInternalFrontierSegmentCount
                + "; exactFrontierBoundaryEdges=" + value.ExactFrontierBoundaryEdgeCount
                + "; exactFrontierSegments=" + value.ExactFrontierSegmentCount
                + "; rejectedOutsideLandFrontierSegments=" + value.RejectedOutsideLandFrontierSegmentCount
                + "; ribbonSourceTriangles=" + value.RibbonSourceTriangleCount
                + "; ribbonGeneratedTriangles=" + value.RibbonGeneratedTriangleCount
                + "; ribbonRemovedCapTriangles=" + value.RibbonRemovedCapTriangleCount
                + "; ribbonMiteredEndpoints=" + value.RibbonMiteredEndpointCount
                + "; ribbonWidth=" + Format(value.RibbonWidth)
                + "; ribbonRemovedBackfaceTriangles=" + value.RibbonRemovedBackfaceTriangleCount
                + "; ribbonCurvedSegments=" + value.RibbonCurvedSegmentCount
                + "; ribbonCurvedCoastalSegments=" + value.RibbonCurvedCoastalSegmentCount
                + "; ribbonCurveSubdivisions=" + value.RibbonSubdivisionCount
                + "; coastalSegments=" + value.CoastalSegmentCount
                + "; coastalNodes=" + value.CoastalNodeCount
                + "; coastalEndpoints=" + value.CoastalEndpointCount
                + "; coastalJunctions=" + value.CoastalJunctionCount
                + "; coastalSharpTurns=" + value.CoastalSharpTurnCount
                + "; coastalMaximumSegmentLength=" + Format(value.CoastalMaximumSegmentLength)
                + "; coastalMaximumHeightDelta=" + Format(value.CoastalMaximumHeightDelta)
                + "; sourceFillReversePairs=" + value.SourceFillReversePairCount
                + "; sourceFrontierReversePairs=" + value.SourceFrontierReversePairCount
                + "; sourceNearCoastFillTriangles=" + value.SourceNearCoastFillTriangleCount
                + "; sourceNearCoastTiltedFillTriangles=" + value.SourceNearCoastTiltedFillTriangleCount
                + "; sourceNearCoastHighReliefFillTriangles=" + value.SourceNearCoastHighReliefFillTriangleCount
                + "; sourceNearCoastLargeFillTriangles=" + value.SourceNearCoastLargeFillTriangleCount
                + "; sourceNearCoastFrontierTriangles=" + value.SourceNearCoastFrontierTriangleCount
                + "; sourceNearCoastNonUpwardFrontierTriangles=" + value.SourceNearCoastNonUpwardFrontierTriangleCount
                + "; sourceNearCoastDegenerateFrontierTriangles=" + value.SourceNearCoastDegenerateFrontierTriangleCount
                + "; outputFillReversePairs=" + value.OutputFillReversePairCount
                + "; outputFrontierReversePairs=" + value.OutputFrontierReversePairCount
                + "; outputNearCoastFillTriangles=" + value.OutputNearCoastFillTriangleCount
                + "; outputNearCoastFrontierTriangles=" + value.OutputNearCoastFrontierTriangleCount
                + "; outputNearCoastNonUpwardTriangles=" + value.OutputNearCoastNonUpwardTriangleCount
                + "; outputNearCoastDegenerateTriangles=" + value.OutputNearCoastDegenerateTriangleCount
                + "; coastalLandLeftSegments=" + value.CoastalLandLeftSegmentCount
                + "; coastalLandRightSegments=" + value.CoastalLandRightSegmentCount
                + "; coastalAmbiguousSegments=" + value.CoastalAmbiguousSegmentCount
                + "; ribbonUnsafeCurveFallbackSegments=" + value.RibbonUnsafeCurveFallbackSegmentCount
                + "; ribbonRejectedInvertedTriangles=" + value.RibbonRejectedInvertedTriangleCount
                + "; ribbonPreparedHeightFallbackVertices=" + value.RibbonPreparedHeightFallbackVertexCount
                + "; ribbonChains=" + value.RibbonChainCount
                + "; ribbonClosedChains=" + value.RibbonClosedChainCount
                + "; ribbonEmptyChains=" + value.RibbonEmptyChainCount
                + "; ribbonSharedCrossSections=" + value.RibbonSharedCrossSectionCount
                + "; ribbonWidthConstrainedSamples=" + value.RibbonWidthConstrainedSampleCount
                + "; ribbonSuppressedUnsafeSections=" + value.RibbonSuppressedUnsafeSectionCount
                + "; ribbonUnsafeCoastalSamples=" + value.RibbonUnsafeCoastalSampleCount
                + "; coastalFillClipSourceTriangles=" + value.CoastalFillClipSourceTriangleCount
                + "; coastalFillClipGeneratedTriangles=" + value.CoastalFillClipGeneratedTriangleCount
                + "; coastalFillClipUnchangedTriangles=" + value.CoastalFillClipUnchangedTriangleCount
                + "; coastalFillClipSubdividedTriangles=" + value.CoastalFillClipSubdividedTriangleCount
                + "; coastalFillClipDiscardedTriangles=" + value.CoastalFillClipDiscardedTriangleCount
                + "; coastalFillClipBoundaryVertices=" + value.CoastalFillClipBoundaryVertexCount
                + "; coastalFillClipMaximumDepth=" + value.CoastalFillClipMaximumDepth
                + "; coastalContourBoundaryEdges=" + value.CoastalContourBoundaryEdgeCount
                + "; coastalContourBoundaryNodes=" + value.CoastalContourBoundaryNodeCount
                + "; coastalContourMovedNodes=" + value.CoastalContourMovedNodeCount
                + "; coastalContourRejectedNodes=" + value.CoastalContourRejectedNodeCount
                + "; coastalContourAdjustedFillVertices=" + value.CoastalContourAdjustedFillVertexCount
                + "; coastalContourRibbonSegments=" + value.CoastalContourRibbonSegmentCount
                + "; coastalContourMaximumDisplacement=" + Format(value.CoastalContourMaximumDisplacement)
                + "; coastalContourTotalDisplacement=" + Format(value.CoastalContourTotalDisplacement)
                + "; authoredBorderPaths=" + value.AuthoredBorderPathCount
                + "; authoredBorderSegments=" + value.AuthoredBorderSegmentCount
                + "; authoredReplacedCoastalSegments=" + value.AuthoredReplacedCoastalSegmentCount
                + "; authoredFillPolygons=" + value.AuthoredFillPolygonCount
                + "; authoredFillRecoloredTriangles=" + value.AuthoredFillRecoloredTriangleCount
                + "; authoredFillBrushes=" + value.AuthoredFillBrushCount
                + "; fillFrontierBoundaryMismatch=0"
                + "; averageSmoothingDisplacement=" + averageDisplacement.ToString("F4", CultureInfo.InvariantCulture)
                + "; maximumSmoothingDisplacement=" + value.MaximumSmoothingDisplacement.ToString("F4", CultureInfo.InvariantCulture) + ".");
        }

        private static void AnalyzeTriangles(
            IList<PoliticalBorderGeometryCache.EntityGeometry> entities,
            PoliticalGeometryDiagnosticsRecord diagnostics,
            bool fill)
        {
            foreach (PoliticalBorderGeometryCache.EntityGeometry entity in entities)
            foreach (PoliticalBorderGeometryCache.Triangle triangle in entity.Triangles)
            {
                if (fill) diagnostics.FillTriangleCount++;
                else diagnostics.FrontierTriangleCount++;
                Vec3 firstEdge = triangle.Second - triangle.First;
                Vec3 secondEdge = triangle.Third - triangle.First;
                Vec3 cross = Vec3.CrossProduct(firstEdge, secondEdge);
                float doubledArea = cross.Length;
                float maximumEdge = Math.Max(firstEdge.Length,
                    Math.Max(secondEdge.Length, (triangle.Third - triangle.Second).Length));
                diagnostics.MaximumTriangleEdge = Math.Max(diagnostics.MaximumTriangleEdge, maximumEdge);
                diagnostics.MaximumTriangleArea = Math.Max(diagnostics.MaximumTriangleArea, doubledArea * 0.5f);
                if (doubledArea < 0.0001f) diagnostics.DegenerateTriangleCount++;
                if (maximumEdge > 25f) diagnostics.OversizedTriangleCount++;
                if (fill && doubledArea > 0.0001f
                    && Math.Abs(cross.z) / doubledArea < 0.999f)
                    diagnostics.SourceTiltedFillTriangleCount++;
            }
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
                graph[node] = values;
            }
            values.Add(neighbor);
        }

        private static string Format(float value)
        { return value.ToString("F3", CultureInfo.InvariantCulture); }

        private struct EdgeKey : IEquatable<EdgeKey>
        {
            internal EdgeKey(
                PoliticalBorderGeometryCache.NodeKey first,
                PoliticalBorderGeometryCache.NodeKey second)
            {
                if (first.CompareTo(second) <= 0) { First = first; Second = second; }
                else { First = second; Second = first; }
            }
            private PoliticalBorderGeometryCache.NodeKey First, Second;
            public bool Equals(EdgeKey other) { return First.Equals(other.First) && Second.Equals(other.Second); }
            public override bool Equals(object obj) { return obj is EdgeKey && Equals((EdgeKey)obj); }
            public override int GetHashCode()
            { unchecked { return (First.GetHashCode() * 397) ^ Second.GetHashCode(); } }
        }
    }

    internal sealed class PoliticalGeometryDiagnosticsRecord
    {
        internal int FillEntityCount, FrontierEntityCount;
        internal int FillTriangleCount, FrontierTriangleCount;
        internal int DegenerateTriangleCount, OversizedTriangleCount;
        internal int SourceTiltedFillTriangleCount;
        internal float MaximumTriangleEdge, MaximumTriangleArea;
        internal int FrontierSegmentCount, FrontierNodeCount;
        internal int FrontierEndpointCount, FrontierJunctionCount, MaximumFrontierDegree;
        internal int SharpFrontierTurnCount, DuplicateFrontierSegments, ZeroLengthFrontierSegments;
        internal float MaximumFrontierSegmentLength;
        internal int SmoothedNodeCount;
        internal int SmoothingPassCount, FinalSharpFrontierTurnCount;
        internal int FillAdjustedVertexCount, FrontierAdjustedVertexCount;
        internal int RemovedReverseFillTriangleCount;
        internal int VoronoiCellCount, VoronoiSourceTriangleCount;
        internal int VoronoiGeneratedTriangleCount, VoronoiSplitSourceTriangleCount;
        internal int VoronoiDiscardedDegeneratePolygonCount, VoronoiOutsideLandTriangleCount;
        internal int PreservedExclusionFrontierSegmentCount;
        internal int RemovedLegacyInternalFrontierSegmentCount;
        internal int ExactFrontierBoundaryEdgeCount, ExactFrontierSegmentCount;
        internal int RejectedOutsideLandFrontierSegmentCount;
        internal int RibbonSourceTriangleCount, RibbonGeneratedTriangleCount;
        internal int RibbonRemovedCapTriangleCount, RibbonMiteredEndpointCount;
        internal float RibbonWidth;
        internal int RibbonRemovedBackfaceTriangleCount, RibbonCurvedSegmentCount;
        internal int RibbonCurvedCoastalSegmentCount, RibbonSubdivisionCount;
        internal int CoastalSegmentCount, CoastalNodeCount;
        internal int CoastalEndpointCount, CoastalJunctionCount, CoastalSharpTurnCount;
        internal float CoastalMaximumSegmentLength, CoastalMaximumHeightDelta;
        internal int SourceFillReversePairCount, SourceFrontierReversePairCount;
        internal int SourceNearCoastFillTriangleCount;
        internal int SourceNearCoastTiltedFillTriangleCount;
        internal int SourceNearCoastHighReliefFillTriangleCount;
        internal int SourceNearCoastLargeFillTriangleCount;
        internal int SourceNearCoastFrontierTriangleCount;
        internal int SourceNearCoastNonUpwardFrontierTriangleCount;
        internal int SourceNearCoastDegenerateFrontierTriangleCount;
        internal int OutputFillReversePairCount, OutputFrontierReversePairCount;
        internal int OutputNearCoastFillTriangleCount, OutputNearCoastFrontierTriangleCount;
        internal int OutputNearCoastNonUpwardTriangleCount, OutputNearCoastDegenerateTriangleCount;
        internal int CoastalLandLeftSegmentCount, CoastalLandRightSegmentCount;
        internal int CoastalAmbiguousSegmentCount;
        internal int RibbonUnsafeCurveFallbackSegmentCount;
        internal int RibbonRejectedInvertedTriangleCount;
        internal int RibbonPreparedHeightFallbackVertexCount;
        internal int RibbonChainCount, RibbonClosedChainCount, RibbonEmptyChainCount;
        internal int RibbonSharedCrossSectionCount, RibbonWidthConstrainedSampleCount;
        internal int RibbonSuppressedUnsafeSectionCount, RibbonUnsafeCoastalSampleCount;
        internal int CoastalFillClipSourceTriangleCount, CoastalFillClipGeneratedTriangleCount;
        internal int CoastalFillClipUnchangedTriangleCount, CoastalFillClipSubdividedTriangleCount;
        internal int CoastalFillClipDiscardedTriangleCount, CoastalFillClipBoundaryVertexCount;
        internal int CoastalFillClipMaximumDepth;
        internal int CoastalContourBoundaryEdgeCount, CoastalContourBoundaryNodeCount;
        internal int CoastalContourMovedNodeCount, CoastalContourRejectedNodeCount;
        internal int CoastalContourAdjustedFillVertexCount, CoastalContourRibbonSegmentCount;
        internal float CoastalContourMaximumDisplacement, CoastalContourTotalDisplacement;
        internal int AuthoredBorderPathCount, AuthoredBorderSegmentCount;
        internal int AuthoredReplacedCoastalSegmentCount, AuthoredFillPolygonCount;
        internal int AuthoredFillRecoloredTriangleCount;
        internal int AuthoredFillBrushCount;
        internal float TotalSmoothingDisplacement, MaximumSmoothingDisplacement;
    }
}
