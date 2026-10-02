using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace AgesOfCalradia.PoliticalBorderOptimizer
{
    /// <summary>
    /// Refines only protected fill triangles that straddle the approved land and
    /// exclusion classifier. Fully inland triangles retain their exact captured
    /// vertices, colors, UVs, and grouping; classifier failure preserves all
    /// source fill unchanged.
    /// </summary>
    internal static class PoliticalBorderCoastalFillClipper
    {
        private const int MaximumSubdivisionDepth = 4;
        private const int BoundarySearchIterations = 10;
        private const int MaximumGeneratedTriangles = 500000;
        private const float MinimumSignedArea = 0.0001f;

        internal static bool TryApply(
            List<PoliticalBorderGeometryCache.EntityGeometry> fill,
            PoliticalGeometryDiagnosticsRecord diagnostics)
        {
            if (fill == null || fill.Count == 0) return false;
            Result result = new Result();
            List<PoliticalBorderGeometryCache.EntityGeometry> rebuilt =
                new List<PoliticalBorderGeometryCache.EntityGeometry>(fill.Count);
            foreach (PoliticalBorderGeometryCache.EntityGeometry sourceEntity in fill)
            {
                PoliticalBorderGeometryCache.EntityGeometry target =
                    new PoliticalBorderGeometryCache.EntityGeometry();
                foreach (PoliticalBorderGeometryCache.Triangle triangle
                    in sourceEntity.Triangles)
                {
                    result.SourceTriangles++;
                    if (!TryRefine(triangle, 0, target.Triangles, result))
                    {
                        BorderOptimizerDiagnostics.Info(
                            "Coastal fill refinement retained protected source fill:"
                            + " reason=classifier-unavailable-or-output-bound"
                            + "; sourceTriangles=" + result.SourceTriangles + ".");
                        return false;
                    }
                }
                if (target.Triangles.Count > 0) rebuilt.Add(target);
            }
            if (rebuilt.Count == 0 || result.GeneratedTriangles > MaximumGeneratedTriangles)
                return false;

            fill.Clear();
            fill.AddRange(rebuilt);
            diagnostics.CoastalFillClipSourceTriangleCount = result.SourceTriangles;
            diagnostics.CoastalFillClipGeneratedTriangleCount = result.GeneratedTriangles;
            diagnostics.CoastalFillClipUnchangedTriangleCount = result.UnchangedTriangles;
            diagnostics.CoastalFillClipSubdividedTriangleCount = result.SubdividedTriangles;
            diagnostics.CoastalFillClipDiscardedTriangleCount = result.DiscardedTriangles;
            diagnostics.CoastalFillClipBoundaryVertexCount = result.BoundaryVertices;
            diagnostics.CoastalFillClipMaximumDepth = result.MaximumDepth;
            BorderOptimizerDiagnostics.Info(
                "Protected-land coastal fill refinement applied: sourceTriangles="
                + result.SourceTriangles
                + "; generatedTriangles=" + result.GeneratedTriangles
                + "; unchangedTriangles=" + result.UnchangedTriangles
                + "; subdividedTriangles=" + result.SubdividedTriangles
                + "; discardedOutsideTriangles=" + result.DiscardedTriangles
                + "; boundaryVertices=" + result.BoundaryVertices
                + "; maximumDepth=" + result.MaximumDepth
                + "; inlandGeometryPreserved=true"
                + "; islandExclusionPreserved=true.");
            return true;
        }

        private static bool TryRefine(
            PoliticalBorderGeometryCache.Triangle triangle,
            int depth,
            List<PoliticalBorderGeometryCache.Triangle> output,
            Result result)
        {
            SampleSet samples;
            if (!TryClassify(triangle, out samples)) return false;
            if (samples.AllLand)
            {
                output.Add(triangle);
                result.GeneratedTriangles++;
                if (depth == 0) result.UnchangedTriangles++;
                return result.GeneratedTriangles <= MaximumGeneratedTriangles;
            }
            if (samples.AllOutside)
            {
                result.DiscardedTriangles++;
                return true;
            }
            if (depth >= MaximumSubdivisionDepth)
                return TryClipTerminal(triangle, samples, output, result);

            result.SubdividedTriangles++;
            result.MaximumDepth = Math.Max(result.MaximumDepth, depth + 1);
            Vertex first = new Vertex(triangle.First, triangle.FirstUv);
            Vertex second = new Vertex(triangle.Second, triangle.SecondUv);
            Vertex third = new Vertex(triangle.Third, triangle.ThirdUv);
            Vertex firstSecond = Vertex.Lerp(first, second, 0.5f);
            Vertex secondThird = Vertex.Lerp(second, third, 0.5f);
            Vertex thirdFirst = Vertex.Lerp(third, first, 0.5f);
            uint color = triangle.Color;
            return TryRefine(Make(first, firstSecond, thirdFirst, color), depth + 1, output, result)
                && TryRefine(Make(firstSecond, second, secondThird, color), depth + 1, output, result)
                && TryRefine(Make(thirdFirst, secondThird, third, color), depth + 1, output, result)
                && TryRefine(Make(firstSecond, secondThird, thirdFirst, color), depth + 1, output, result);
        }

        private static bool TryClassify(
            PoliticalBorderGeometryCache.Triangle triangle,
            out SampleSet samples)
        {
            samples = new SampleSet();
            Vec3 firstSecond = (triangle.First + triangle.Second) * 0.5f;
            Vec3 secondThird = (triangle.Second + triangle.Third) * 0.5f;
            Vec3 thirdFirst = (triangle.Third + triangle.First) * 0.5f;
            Vec3 center = (triangle.First + triangle.Second + triangle.Third) / 3f;
            bool first, second, third, midpointOne, midpointTwo, midpointThree, centroid;
            if (!TryLand(triangle.First, out first)
                || !TryLand(triangle.Second, out second)
                || !TryLand(triangle.Third, out third)
                || !TryLand(firstSecond, out midpointOne)
                || !TryLand(secondThird, out midpointTwo)
                || !TryLand(thirdFirst, out midpointThree)
                || !TryLand(center, out centroid)) return false;
            samples = new SampleSet(
                first, second, third,
                midpointOne, midpointTwo, midpointThree, centroid);
            return true;
        }

        private static bool TryClipTerminal(
            PoliticalBorderGeometryCache.Triangle triangle,
            SampleSet samples,
            List<PoliticalBorderGeometryCache.Triangle> output,
            Result result)
        {
            List<Vertex> input = new List<Vertex>(3)
            {
                new Vertex(triangle.First, triangle.FirstUv),
                new Vertex(triangle.Second, triangle.SecondUv),
                new Vertex(triangle.Third, triangle.ThirdUv)
            };
            bool[] land = { samples.First, samples.Second, samples.Third };
            List<Vertex> polygon = new List<Vertex>(5);
            Vertex previous = input[2];
            bool previousLand = land[2];
            for (int index = 0; index < input.Count; index++)
            {
                Vertex current = input[index];
                bool currentLand = land[index];
                if (currentLand != previousLand)
                {
                    Vertex boundary;
                    if (!TryFindBoundary(previous, current, previousLand, out boundary))
                        return false;
                    polygon.Add(boundary);
                    result.BoundaryVertices++;
                }
                if (currentLand) polygon.Add(current);
                previous = current;
                previousLand = currentLand;
            }

            if (polygon.Count < 3)
            {
                result.DiscardedTriangles++;
                return true;
            }
            Vertex anchor = polygon[0];
            for (int index = 1; index + 1 < polygon.Count; index++)
            {
                PoliticalBorderGeometryCache.Triangle candidate =
                    Make(anchor, polygon[index], polygon[index + 1], triangle.Color);
                if (SignedArea(candidate) <= MinimumSignedArea
                    || !TryValidateLand(candidate))
                {
                    result.DiscardedTriangles++;
                    continue;
                }
                output.Add(candidate);
                result.GeneratedTriangles++;
                if (result.GeneratedTriangles > MaximumGeneratedTriangles) return false;
            }
            return true;
        }

        private static bool TryFindBoundary(
            Vertex first,
            Vertex second,
            bool firstLand,
            out Vertex boundary)
        {
            float low = 0f;
            float high = 1f;
            for (int iteration = 0; iteration < BoundarySearchIterations; iteration++)
            {
                float amount = (low + high) * 0.5f;
                bool land;
                if (!TryLand(Vertex.Lerp(first, second, amount).Position, out land))
                {
                    boundary = first;
                    return false;
                }
                if (land == firstLand) low = amount; else high = amount;
            }
            boundary = Vertex.Lerp(first, second, (low + high) * 0.5f);
            return true;
        }

        private static bool TryValidateLand(
            PoliticalBorderGeometryCache.Triangle triangle)
        {
            Vec3 firstSecond = (triangle.First + triangle.Second) * 0.5f;
            Vec3 secondThird = (triangle.Second + triangle.Third) * 0.5f;
            Vec3 thirdFirst = (triangle.Third + triangle.First) * 0.5f;
            Vec3 center = (triangle.First + triangle.Second + triangle.Third) / 3f;
            bool first, second, third, fourth;
            return TryLand(firstSecond, out first) && first
                && TryLand(secondThird, out second) && second
                && TryLand(thirdFirst, out third) && third
                && TryLand(center, out fourth) && fourth;
        }

        private static bool TryLand(Vec3 point, out bool land)
        { return BorderOptimizerRuntime.TryClassifyFrontierLand(new Vec2(point.x, point.y), out land); }

        private static PoliticalBorderGeometryCache.Triangle Make(
            Vertex first,
            Vertex second,
            Vertex third,
            uint color)
        {
            return new PoliticalBorderGeometryCache.Triangle(
                first.Position, second.Position, third.Position,
                first.Uv, second.Uv, third.Uv, color);
        }

        private static float SignedArea(PoliticalBorderGeometryCache.Triangle triangle)
        {
            return (triangle.Second.x - triangle.First.x)
                    * (triangle.Third.y - triangle.First.y)
                - (triangle.Second.y - triangle.First.y)
                    * (triangle.Third.x - triangle.First.x);
        }

        private struct Vertex
        {
            internal Vertex(Vec3 position, Vec2 uv) { Position = position; Uv = uv; }
            internal Vec3 Position;
            internal Vec2 Uv;
            internal static Vertex Lerp(Vertex first, Vertex second, float amount)
            {
                return new Vertex(
                    first.Position + (second.Position - first.Position) * amount,
                    first.Uv + (second.Uv - first.Uv) * amount);
            }
        }

        private struct SampleSet
        {
            internal SampleSet(
                bool first, bool second, bool third,
                bool midpointOne, bool midpointTwo, bool midpointThree, bool centroid)
            {
                First = first; Second = second; Third = third;
                MidpointOne = midpointOne; MidpointTwo = midpointTwo;
                MidpointThree = midpointThree; Centroid = centroid;
            }
            internal bool First, Second, Third;
            internal bool MidpointOne, MidpointTwo, MidpointThree, Centroid;
            internal bool AllLand
            {
                get
                {
                    return First && Second && Third && MidpointOne
                        && MidpointTwo && MidpointThree && Centroid;
                }
            }
            internal bool AllOutside
            {
                get
                {
                    return !First && !Second && !Third && !MidpointOne
                        && !MidpointTwo && !MidpointThree && !Centroid;
                }
            }
        }

        private sealed class Result
        {
            internal int SourceTriangles, GeneratedTriangles, UnchangedTriangles;
            internal int SubdividedTriangles, DiscardedTriangles, BoundaryVertices;
            internal int MaximumDepth;
        }
    }
}
