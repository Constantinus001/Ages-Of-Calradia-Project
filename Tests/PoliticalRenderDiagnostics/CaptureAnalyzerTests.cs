using System;
using System.Globalization;
using System.IO;

namespace AgesOfCalradia.PoliticalRenderDiagnostics
{
    internal static class CaptureAnalyzerTests
    {
        private static int _checks;
        private const string Header = "triangle,ax,ay,az,bx,by,bz,cx,cy,cz\n";
        private static void Require(bool condition, string message)
        { _checks++; if (!condition) throw new InvalidOperationException(message); }
        private static void Main(string[] args)
        {
            if (args.Length != 1) throw new ArgumentException("Supply a fixture output directory.");
            string root = Path.GetFullPath(args[0]);
            Directory.CreateDirectory(root);
            string samples = "segment,status,supportRejected,heightRejected,geometryKind,waterStatus,closeTerrainClearance,fullTerrainClearance,closeWaterClearance,fullWaterClearance\n"
                + "1,accepted,False,False,fill-triangle,finite-candidate-validity-unknown,-0.5,-0.5,-9,-9\n"
                + "1,accepted,False,False,fill-triangle,nonfinite-unknown,4,4,unknown,unknown\n"
                + "1,rejected,True,False,frontier-segment,finite-candidate-validity-unknown,-1,3,-2,2\n";
            string seam = "1,-1,0,4,0,0,4,0,1,4\n"
                + "2,0,0,4,1,0,4,0,0.5,9.333333333333\n"
                + "3,0,0.5,9.333333333333,1,1,4,0,1,4\n";
            CaptureAnalysis bad = Run(root, "seam", samples, seam);
            Require(bad.CoarseFinePairs == 2 && bad.CoarseFineMismatches == 2, "Both halves of a coarse/fine seam must be measured.");
            Require(Math.Abs(bad.MaximumHeightGap - 16.0 / 3) < 0.000001, "Known seam gap must be 5.333333 world units.");
            Require(bad.NegativeWaterCandidateSamples == 2 && bad.WaterCoverageUnknownSamples == 3, "Finite negative water values remain coverage-unknown.");
            Require(bad.Markdown.Contains("| fill-triangle | 1 | 0 |") && bad.Markdown.Contains("| frontier-segment | 0 | 1 | 1 |"), "Distinct geometry IDs must not become sample counts or cross-kind collisions.");
            Require(bad.Markdown.Contains("fill-triangle / accepted | 1 | 1 |"), "Report measured negative terrain samples.");
            Require(bad.Markdown.Contains("truncation status: **true**"), "Runtime truncation must be surfaced.");
            CaptureAnalysis good = Run(root, "conforming", samples, seam.Replace("9.333333333333", "4"));
            Require(good.CoarseFinePairs == 2 && good.MismatchedPairs == 0, "Conforming coarse/fine geometry must not report a crack.");
            CaptureAnalysis disjoint = Run(root, "disjoint", samples,
                "1,-1,0,4,0,0,4,0,1,4\n2,0,2,9,1,2,9,0,3,9\n");
            Require(disjoint.SharedEdgePairs == 0, "Disjoint collinear intervals must not be bridged.");
            CaptureAnalysis touch = Run(root, "touch", samples,
                "1,-1,0,4,0,0,4,0,1,4\n2,0,1,9,1,1,9,0,2,9\n");
            Require(touch.SharedEdgePairs == 0, "Endpoint-only contact is not a shared interval.");
            CaptureAnalysis sameSide = Run(root, "same-side", samples,
                "1,-1,0,4,0,0,4,0,1,4\n2,-1,0,9,0,0,9,0,1,9\n");
            Require(sameSide.SharedEdgePairs == 0, "Same-side overlays must not be classified as neighboring fill cells.");
            CaptureAnalysis tolerance = Run(root, "tolerance", samples,
                "1,-1,0,4,0.00009,0,4,0.00009,1,4\n2,0.00011,0,9,1,0,9,0.00011,0.5,9\n");
            Require(tolerance.CoarseFinePairs == 1 && Math.Abs(tolerance.MaximumHeightGap - 5) < 0.000001, "Matching must work across neighboring XY buckets.");
            CaptureAnalysis divergent = Run(root, "divergent-near-axis", samples,
                "1,-1,0,4,0,0,4,0.00009,1,4\n2,-0.00004,0.5,9,1,0.5,9,-0.00004,1,9\n");
            Require(divergent.SharedEdgePairs == 0, "Average fixed coordinates must not hide endpoint XY errors beyond tolerance.");
            CaptureAnalysis overlapTolerance = Run(root, "overlap-tolerance", samples,
                "1,-1,0,4,0,0,4,0.00009,100,4\n2,-0.00009,0,9,1,0,9,-0.00009,1,9\n");
            Require(overlapTolerance.CoarseFinePairs == 1, "XY tolerance applies to actual overlap, not unrelated distant coarse-edge midpoint.");
            // Realistic maximum-size distinct rows ensure the index stays linear-memory
            // and does not create pair matches between disjoint geometry.
            string large = Path.Combine(root, "cap"); Directory.CreateDirectory(large);
            File.WriteAllText(Path.Combine(large, "segments.csv"), samples);
            using (var writer = new StreamWriter(Path.Combine(large, "fill-triangles.csv")))
            {
                writer.Write(Header);
                for (int i = 0; i < 262144; i++)
                {
                    string x = (i * 2).ToString(CultureInfo.InvariantCulture);
                    string next = (i * 2 + 1).ToString(CultureInfo.InvariantCulture);
                    writer.WriteLine(i + "," + x + ",0,4," + next + ",0,4," + x + ",1,4");
                }
            }
            CaptureAnalysis capped = CaptureAnalyzer.Analyze(large);
            Require(capped.TriangleRows == 262144 && !capped.PairScanLimited && capped.SharedEdgePairs == 0, "Full-cap disjoint fixture must finish without false matches.");
            Require(capped.Markdown.Contains("CSV cap reached: **true**") && capped.Markdown.Contains("truncation status: **unknown**"), "Cap reached without a timeline is uncertain, not complete.");
            Console.WriteLine("Capture analyzer: " + _checks + " behavioral checks passed. Fixtures: " + root);
        }
        private static CaptureAnalysis Run(string root, string name, string samples, string geometry)
        {
            string folder = Path.Combine(root, name); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "segments.csv"), samples);
            File.WriteAllText(Path.Combine(folder, "fill-triangles.csv"), Header + geometry);
            File.WriteAllText(Path.Combine(folder, "identity-timeline.log"), "fillGeometryCaptured=3;fillGeometrySeen=4;fillGeometryCap=262144;fillGeometryTruncated=true\n");
            CaptureAnalysis result = CaptureAnalyzer.Analyze(folder);
            File.WriteAllText(Path.Combine(folder, "capture-analysis.md"), result.Markdown);
            return result;
        }
    }
}
