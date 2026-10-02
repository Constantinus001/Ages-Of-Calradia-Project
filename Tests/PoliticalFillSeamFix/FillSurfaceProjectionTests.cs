using System;
using AgesOfCalradia.PoliticalFillSeamFix;

internal static class FillSurfaceProjectionTests
{
    private static int checks;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    private static SeamVertex V(float x, float y) { return new SeamVertex(x, y, 4, x / 8, y / 8); }
    private static bool Height(float x, float y, out float z) { z = x + 2 * y; return true; }
    private static void Main()
    {
        // Two separated patches; empty interval is an excluded region.
        var source = new[] {
            new SeamTriangle(3, 1, 0xff123456, V(0,0),V(1,0),V(0,1)),
            new SeamTriangle(4, 1, 0xffabcdef, V(1,0),V(1,1),V(0,1)),
            new SeamTriangle(7, 2, 0xff987654, V(10,0),V(11,0),V(10,1)) };
        var p = new FillSurfaceProjection(source, Height);
        p.Advance(256, () => true);
        Check(p.QueryCount == 0 && p.CompletedTriangles == 0, "Yield must precede native queries.");
        p.Advance(1, () => false);
        Check(p.CompletedTriangles == 1 && !p.Complete, "Slice limit must stop work.");
        p.Advance(256, () => false);
        Check(p.Complete && p.Output.Length == source.Length, "No accepted triangle added or removed.");
        Check(p.QueryCount == 7, "Shared XY vertices queried exactly once.");
        for (int i = 0; i < source.Length; i++)
        {
            SeamTriangle a = source[i], b = p.Output[i];
            Check(a.ParentId == b.ParentId && a.RowId == b.RowId && a.Color == b.Color, "Ownership metadata preserved.");
            var before = new[] { a.A, a.B, a.C }; var after = new[] { b.A, b.B, b.C };
            for (int j = 0; j < 3; j++)
                Check(before[j].X == after[j].X && before[j].Y == after[j].Y && before[j].U == after[j].U && before[j].V == after[j].V
                    && after[j].Z == after[j].X + 2 * after[j].Y + 3 && before[j].Z == 4, "Only output Z may change; excluded XY remains empty.");
        }
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            var bad = new FillSurfaceProjection(source, (float x, float y, out float z) => { z = invalid; return true; });
            bool rejected = false; try { bad.Advance(256, () => false); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && !bad.Complete && source[0].A.Z == 4, "Nonfinite sample must not publish or mutate original.");
        }
        var failure = new FillSurfaceProjection(source, (float x, float y, out float z) => { z = 0; return false; });
        bool failed = false; try { failure.Advance(256, () => false); } catch (InvalidOperationException) { failed = true; }
        Check(failed && failure.CompletedTriangles == 0, "Failed height query must not silently use zero.");
        SeamBuildResult result; string error;
        Check(FillSeamConformer.TryBuild(p.Output, out result, out error), "Projected geometry must pass conformer: " + error);
        Check(result.Triangles.Count == 3 && result.ChangedParentCount == 0, "Separated patches remain separated even without seams.");
        p.Advance(256, () => false);
        Check(p.QueryCount == 7, "Completed projection cannot repeat native queries.");
        Console.WriteLine("Surface projection: " + checks + " behavioral checks passed.");
    }
}
