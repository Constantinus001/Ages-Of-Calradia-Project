using System;

namespace AgesOfCalradia.PoliticalRenderDiagnostics
{
    internal static class FillSubdivisionContractTests
    {
        private static int _checks;
        private static void Check(bool condition, string reason)
        { if (!condition) throw new InvalidOperationException(reason); _checks++; }
        // Continuous terrain witness. u spans two adjacent root cells; v one row.
        // This is a counterexample fixture, not a claimed captured campaign height.
        private static float Terrain(float u, float v)
        { return 8f * Math.Max(0f, 1f - Math.Abs(u - 0.75f) / 0.75f)
            * Math.Max(0f, 1f - Math.Abs(v - 0.5f) / 0.5f); }
        private static void Main()
        {
            foreach (float u in new[] { 0f, 1f, 2f })
            foreach (float v in new[] { 0f, 1f })
                Check(Terrain(u, v) == 0f, "Both root cells have flat coarse corners.");
            // Approved CrossesVisualBoundary compares center height with four
            // corners' mean, threshold .75; same land/owner region is assumed.
            Check(Terrain(0.5f, 0.5f) > 0.75f && Terrain(1.5f, 0.5f) == 0f,
                "Left root refines by relief; right root does not.");
            var low = new Point3(1, 0, 4); var high = new Point3(1, 1, 4);
            var split = new Point3(1, 0.5f, Terrain(1, 0.5f) + 4f);
            float gap;
            Check(FillEdgeContinuityProbe.TryMeasureInteriorJoin(low, high, split, 0.00001f, out gap)
                && Math.Abs(gap - 16f / 3f) < 0.00001f,
                "Refined midpoint is 16/3 units above the unrefined submitted edge.");
            Check(FillEdgeContinuityProbe.TryMeasureInteriorJoin(high, low, split, 0.00001f, out gap)
                && Math.Abs(gap - 16f / 3f) < 0.00001f, "Edge direction does not change discrepancy.");
            Check(RenderProbeMath.ClassifyClearance(4f,
                SurfaceSample.FromQuery(true, Terrain(1, 0.5f), -1000, 10000)) == ClearanceState.Below,
                "The coarse fill edge can lie below terrain despite every coarse vertex having +4 clearance.");
            Check(FillEdgeContinuityProbe.TryMeasureInteriorJoin(low, high,
                new Point3(1, 0.5f, 4), 0.00001f, out gap) && gap == 0,
                "Coplanar T-junction has no height discrepancy.");
            Check(!FillEdgeContinuityProbe.TryMeasureInteriorJoin(low, high,
                new Point3(1.1f, 0.5f, 5), 0.001f, out gap) && float.IsNaN(gap),
                "Nearby unrelated edge is not treated as a join.");
            Check(!FillEdgeContinuityProbe.TryMeasureInteriorJoin(low, high, low, 0.001f, out gap),
                "Endpoint equality is not an interior split.");
            Check(!FillEdgeContinuityProbe.TryMeasureInteriorJoin(low, low, split, 0.001f, out gap),
                "Zero-length XY edge is rejected.");
            Console.WriteLine("Fill subdivision diagnostic witness: " + _checks + " behavioral checks passed.");
        }
    }
}
