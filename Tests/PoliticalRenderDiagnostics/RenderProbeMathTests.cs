using System;

namespace AgesOfCalradia.PoliticalRenderDiagnostics
{
    internal static class RenderProbeMathTests
    {
        private static int _checks;
        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); _checks++; }
        private static bool Near(float first, float second)
        { return Math.Abs(first - second) < 0.00001f; }
        private static SurfaceSample Surface(float height)
        { return SurfaceSample.FromQuery(true, height, -1000f, 10000f); }

        private static void Main()
        {
            Check(Near(RenderProbeMath.FrontierWorldZ(15f, 0f), 10.35f), "Close zoom uses final frame translation.");
            Check(Near(RenderProbeMath.FrontierWorldZ(15f, 1f), 15f), "Far zoom retains raw vertex height.");
            Check(Near(RenderProbeMath.FrontierWorldZ(15f, 0.5f), 12.675f), "Intermediate zoom interpolates frame.");
            Check(Near(RenderProbeMath.FrontierWorldZ(15f, -5f), 10.35f)
                && Near(RenderProbeMath.FrontierWorldZ(15f, 5f), 15f), "Finite alpha clamps.");
            Check(float.IsNaN(RenderProbeMath.FrontierWorldZ(15f, float.NaN)), "Invalid alpha is unknown.");
            var a = new Point3(0, 0, 2); var b = new Point3(2, 0, 2); var c = new Point3(0, 2, 2);
            Point3 midpoint = RenderProbeMath.Midpoint(a, b), center = RenderProbeMath.Barycenter(a, b, c);
            Check(Near(midpoint.X, 1) && Near(center.X, 2f / 3) && Near(center.Y, 2f / 3), "Interior probe XY interpolation.");
            Check(RenderProbeMath.ClassifyClearance(a.Z, Surface(0)) == ClearanceState.Clear
                && RenderProbeMath.ClassifyClearance(b.Z, Surface(0)) == ClearanceState.Clear
                && RenderProbeMath.ClassifyClearance(midpoint.Z, Surface(3)) == ClearanceState.Below,
                "Clear vertices do not imply clear triangle interior over a terrain ridge.");
            Check(RenderProbeMath.ClassifyClearance(center.Z, Surface(3)) == ClearanceState.Below, "Barycenter also detects ridge.");
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -100f, 10001f })
            {
                SurfaceSample sample = SurfaceSample.FromQuery(true, invalid, -1000, 10000, -100f);
                Check(!sample.Known && float.IsNaN(sample.Height)
                    && RenderProbeMath.ClassifyClearance(2, sample) == ClearanceState.Unknown,
                    "Invalid or explicit no-water sentinel must not become zero water.");
            }
            Check(RenderProbeMath.ClassifyClearance(2, SurfaceSample.FromQuery(false, 0, -1000, 10000))
                == ClearanceState.Unknown, "Failed query is unknown even with finite output.");
            Check(RenderProbeMath.ClassifyClearance(2, default(SurfaceSample)) == ClearanceState.Unknown,
                "Default sample is unknown despite zero-initialized storage.");
            Check(RenderProbeMath.ClassifyClearance(0, SurfaceSample.FromQuery(true, -5, -1000, 10000))
                == ClearanceState.Clear, "Negative valid water is not blindly rejected.");
            Check(RenderProbeMath.ClassifyClearance(1.9995f, Surface(2)) == ClearanceState.Clear,
                "Clearance tolerance avoids numeric noise.");
            var loop = new EndpointGraph(); loop.AddSegment(a, b); loop.AddSegment(b, c); loop.AddSegment(c, a);
            Check(loop.NodeCount == 3 && loop.CountDegree(2) == 3 && loop.CountUnexpectedEndpoints(null) == 0,
                "Closed loop has no diagnostic open endpoints.");
            var split = new EndpointGraph(); var d = new Point3(4, 0, 2); var e = new Point3(6, 0, 2);
            split.AddSegment(a, b); split.AddSegment(d, e);
            Check(split.CountUnexpectedEndpoints(null) == 4 && split.CountUnexpectedEndpoints(new[] { a, b, d, e }) == 0
                && split.NodeCount == 4 && split.SegmentCount == 2, "Intentional splits are reported without bridging.");
            var joined = new EndpointGraph(); joined.AddSegment(a, b); joined.AddSegment(b, d);
            Check(joined.CountDegree(2) == 1 && joined.CountUnexpectedEndpoints(new[] { a, d }) == 0,
                "Shared endpoint has two incident segments.");
            var almost = new Point3(2.0001f, 0, 9);
            Check(joined.Key(b).Equals(joined.Key(almost)) && Near(almost.X, 2.0001f), "Bucketing does not mutate coordinates.");
            Check(joined.Key(joined.CellCenter(joined.Key(almost))).Equals(joined.Key(almost)), "Bucket center round trips.");
            var collapsed = new EndpointGraph(); collapsed.AddSegment(b, almost);
            Check(collapsed.CollapsedBucketSegmentCount == 1, "Bucket collapse is separately exposed, not a valid loop proof.");
            Console.WriteLine("Political render diagnostic math: " + _checks + " behavioral checks passed.");
        }
    }
}
