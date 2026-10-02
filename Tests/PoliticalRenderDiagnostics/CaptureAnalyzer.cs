using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AgesOfCalradia.PoliticalRenderDiagnostics
{
    // Offline analysis only. No game APIs or renderer mutations. The native CSV
    // schema contains unquoted numeric/status fields; unexpected schemas fail.
    public sealed class CaptureAnalysis
    {
        public string Markdown { get; internal set; }
        public long TriangleRows { get; internal set; }
        public long SharedEdgePairs { get; internal set; }
        public long CoarseFinePairs { get; internal set; }
        public long MismatchedPairs { get; internal set; }
        public long CoarseFineMismatches { get; internal set; }
        public double MaximumHeightGap { get; internal set; }
        public long NegativeWaterCandidateSamples { get; internal set; }
        public long WaterCoverageUnknownSamples { get; internal set; }
        public bool PairScanLimited { get; internal set; }
    }

    public static class CaptureAnalyzer
    {
        private const double XyTolerance = 0.0001;
        private const double HeightTolerance = 0.0001;
        private const int TriangleLimit = 262144;
        private const long PairCheckLimit = 10000000;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private sealed class KindStats
        {
            internal readonly HashSet<string> Accepted = new HashSet<string>();
            internal readonly HashSet<string> Rejected = new HashSet<string>();
            internal readonly HashSet<string> Support = new HashSet<string>();
            internal readonly HashSet<string> Height = new HashSet<string>();
            internal readonly long[] Samples = new long[2];
            internal readonly long[] NegativeCloseTerrain = new long[2];
            internal readonly long[] NegativeFullTerrain = new long[2];
            internal readonly long[] UnknownTerrain = new long[2];
        }
        private struct Point { internal double X, Y, Z; }
        private sealed class Edge
        {
            internal long Triangle;
            internal int Axis, Side;
            internal double Line, Start, End, FirstZ, LastZ, FirstCross, LastCross;
            internal double HeightAt(double position)
            { return FirstZ + (position - Start) / (End - Start) * (LastZ - FirstZ); }
            internal double CrossAt(double position)
            { return FirstCross + (position - Start) / (End - Start) * (LastCross - FirstCross); }
        }
        private sealed class Example
        {
            internal Edge First, Second;
            internal double Start, End, GapStart, GapEnd;
            internal double Gap { get { return Math.Max(Math.Abs(GapStart), Math.Abs(GapEnd)); } }
        }
        private sealed class Csv : IDisposable
        {
            private readonly StreamReader _reader;
            private readonly Dictionary<string, int> _columns;
            private readonly string _path;
            internal int LineNumber = 1;
            internal Csv(string path, params string[] required)
            {
                _path = path;
                _reader = new StreamReader(path);
                string header = _reader.ReadLine();
                _columns = new Dictionary<string, int>(StringComparer.Ordinal);
                if (header == null) { _reader.Dispose(); throw new InvalidDataException(path + ": empty CSV."); }
                string[] fields = header.TrimStart('\uFEFF').Split(',');
                for (int i = 0; i < fields.Length; i++)
                {
                    if (_columns.ContainsKey(fields[i])) { _reader.Dispose(); throw new InvalidDataException(path + ": duplicate column."); }
                    _columns.Add(fields[i], i);
                }
                foreach (string column in required)
                    if (!_columns.ContainsKey(column)) { _reader.Dispose(); throw new InvalidDataException(path + ": missing column " + column); }
            }
            internal string[] Read()
            {
                string line = _reader.ReadLine();
                if (line == null) return null;
                LineNumber++;
                string[] fields = line.Split(',');
                if (fields.Length != _columns.Count || line.IndexOf('"') >= 0)
                    throw new InvalidDataException(_path + ": unexpected CSV fields at line " + LineNumber);
                return fields;
            }
            internal string Get(string[] row, string key) { return row[_columns[key]]; }
            internal double Number(string[] row, string key)
            {
                double value;
                if (!Finite(Get(row, key), out value))
                    throw new InvalidDataException(_path + ": nonfinite/invalid " + key + " at line " + LineNumber);
                return value;
            }
            public void Dispose() { _reader.Dispose(); }
        }

        public static CaptureAnalysis Analyze(string directory)
        {
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
            var result = new CaptureAnalysis();
            var kinds = new Dictionary<string, KindStats> {
                { "fill-triangle", new KindStats() }, { "frontier-segment", new KindStats() } };
            var statuses = new SortedDictionary<string, long>(StringComparer.Ordinal);
            ReadSamples(Path.Combine(directory, "segments.csv"), kinds, statuses, result);
            var groups = new Dictionary<long, List<Edge>>[] {
                new Dictionary<long, List<Edge>>(), new Dictionary<long, List<Edge>>() };
            long axisEdges = 0, skippedEdges = 0;
            string triangles = Path.Combine(directory, "fill-triangles.csv");
            bool hasTriangles = File.Exists(triangles);
            if (hasTriangles) ReadTriangles(triangles, groups, result, ref axisEdges, ref skippedEdges);
            var examples = new List<Example>();
            long checks = 0;
            foreach (Dictionary<long, List<Edge>> axis in groups)
            {
                foreach (long key in axis.Keys.OrderBy(k => k))
                {
                    var combined = new List<Edge>(axis[key]);
                    List<Edge> neighbor;
                    // Nearly axis-aligned coarse/fine edges can agree over the
                    // overlap while their full-edge midlines differ by up to 2*tolerance.
                    for (int offset = 1; offset <= 2; offset++)
                        if (key <= long.MaxValue - offset && axis.TryGetValue(key + offset, out neighbor)) combined.AddRange(neighbor);
                    combined.Sort((a, b) => a.Start.CompareTo(b.Start));
                    var active = new List<Edge>();
                    foreach (Edge edge in combined)
                    {
                        active.RemoveAll(a => a.End - edge.Start <= XyTolerance);
                        foreach (Edge previous in active)
                        {
                            // The neighboring bucket's internal pairs belong to its own pass.
                            if (Bucket(previous.Line) != key && Bucket(edge.Line) != key) continue;
                            if (++checks > PairCheckLimit) { result.PairScanLimited = true; break; }
                            Compare(previous, edge, result, examples);
                        }
                        if (result.PairScanLimited) break;
                        active.Add(edge);
                    }
                    if (result.PairScanLimited) break;
                }
                if (result.PairScanLimited) break;
            }
            result.Markdown = Report(directory, result, kinds, statuses, hasTriangles,
                axisEdges, skippedEdges, checks, examples);
            return result;
        }

        private static void ReadSamples(string path, Dictionary<string, KindStats> kinds,
            SortedDictionary<string, long> statuses, CaptureAnalysis result)
        {
            using (var csv = new Csv(path, "segment", "status", "supportRejected", "heightRejected",
                "geometryKind", "waterStatus", "closeTerrainClearance", "fullTerrainClearance",
                "closeWaterClearance", "fullWaterClearance"))
            {
                string[] row;
                while ((row = csv.Read()) != null)
                {
                    string kind = csv.Get(row, "geometryKind"), status = csv.Get(row, "status");
                    KindStats stats;
                    if (!kinds.TryGetValue(kind, out stats) || (status != "accepted" && status != "rejected"))
                        throw new InvalidDataException(path + ": unknown geometry/status at line " + csv.LineNumber);
                    string id = csv.Get(row, "segment");
                    if (id.Length == 0) throw new InvalidDataException(path + ": missing segment ID.");
                    int category = status == "accepted" ? 0 : 1;
                    (category == 0 ? stats.Accepted : stats.Rejected).Add(id);
                    if (Flag(csv.Get(row, "supportRejected"))) stats.Support.Add(id);
                    if (Flag(csv.Get(row, "heightRejected"))) stats.Height.Add(id);
                    stats.Samples[category]++;
                    double close, full;
                    bool closeKnown = Finite(csv.Get(row, "closeTerrainClearance"), out close);
                    bool fullKnown = Finite(csv.Get(row, "fullTerrainClearance"), out full);
                    if (closeKnown && close < 0) stats.NegativeCloseTerrain[category]++;
                    if (fullKnown && full < 0) stats.NegativeFullTerrain[category]++;
                    if (!closeKnown || !fullKnown) stats.UnknownTerrain[category]++;
                    string waterStatus = csv.Get(row, "waterStatus");
                    long count;
                    statuses.TryGetValue(waterStatus, out count); statuses[waterStatus] = count + 1;
                    // Native water query has no verified water-coverage validity result.
                    result.WaterCoverageUnknownSamples++;
                    if ((Finite(csv.Get(row, "closeWaterClearance"), out close) && close < 0)
                        || (Finite(csv.Get(row, "fullWaterClearance"), out full) && full < 0))
                        result.NegativeWaterCandidateSamples++;
                }
            }
        }

        private static void ReadTriangles(string path, Dictionary<long, List<Edge>>[] groups,
            CaptureAnalysis result, ref long axisEdges, ref long skippedEdges)
        {
            var ids = new HashSet<long>();
            using (var csv = new Csv(path, "triangle", "ax", "ay", "az", "bx", "by", "bz", "cx", "cy", "cz"))
            {
                string[] row;
                while ((row = csv.Read()) != null)
                {
                    result.TriangleRows++;
                    if (result.TriangleRows > TriangleLimit) continue;
                    long id;
                    if (!long.TryParse(csv.Get(row, "triangle"), NumberStyles.Integer, Invariant, out id) || !ids.Add(id))
                        throw new InvalidDataException(path + ": invalid/duplicate triangle ID at line " + csv.LineNumber);
                    var a = ReadPoint(csv, row, "a"); var b = ReadPoint(csv, row, "b"); var c = ReadPoint(csv, row, "c");
                    AddEdge(a, b, c, id, groups, ref axisEdges, ref skippedEdges);
                    AddEdge(b, c, a, id, groups, ref axisEdges, ref skippedEdges);
                    AddEdge(c, a, b, id, groups, ref axisEdges, ref skippedEdges);
                }
            }
        }
        private static Point ReadPoint(Csv csv, string[] row, string prefix)
        { return new Point { X = csv.Number(row, prefix + "x"), Y = csv.Number(row, prefix + "y"), Z = csv.Number(row, prefix + "z") }; }
        private static void AddEdge(Point first, Point last, Point third, long id,
            Dictionary<long, List<Edge>>[] groups, ref long kept, ref long skipped)
        {
            bool vertical = Math.Abs(first.X - last.X) <= XyTolerance;
            bool horizontal = Math.Abs(first.Y - last.Y) <= XyTolerance;
            if (vertical == horizontal) { skipped++; return; }
            double line = vertical ? (first.X + last.X) / 2 : (first.Y + last.Y) / 2;
            double start = vertical ? first.Y : first.X, end = vertical ? last.Y : last.X;
            double side = (vertical ? third.X : third.Y) - line;
            if (Math.Abs(side) <= XyTolerance || Math.Abs(end - start) <= XyTolerance) { skipped++; return; }
            double firstCross = vertical ? first.X : first.Y, lastCross = vertical ? last.X : last.Y;
            var edge = new Edge { Triangle = id, Axis = vertical ? 0 : 1, Line = line,
                Start = Math.Min(start, end), End = Math.Max(start, end), Side = Math.Sign(side),
                FirstZ = start < end ? first.Z : last.Z, LastZ = start < end ? last.Z : first.Z,
                FirstCross = start < end ? firstCross : lastCross, LastCross = start < end ? lastCross : firstCross };
            long key = Bucket(line);
            List<Edge> bucket;
            if (!groups[edge.Axis].TryGetValue(key, out bucket)) groups[edge.Axis].Add(key, bucket = new List<Edge>());
            bucket.Add(edge); kept++;
        }
        private static long Bucket(double coordinate)
        {
            double bucket = Math.Floor(coordinate / XyTolerance);
            if (bucket <= long.MinValue || bucket >= long.MaxValue) throw new InvalidDataException("Coordinate exceeds edge-index range.");
            return (long)bucket;
        }
        private static void Compare(Edge a, Edge b, CaptureAnalysis result, List<Example> examples)
        {
            if (a.Triangle == b.Triangle || a.Side == b.Side || Math.Abs(a.Line - b.Line) > 2 * XyTolerance) return;
            double start = Math.Max(a.Start, b.Start), end = Math.Min(a.End, b.End);
            if (end - start <= XyTolerance) return;
            if (Math.Abs(a.CrossAt(start) - b.CrossAt(start)) > XyTolerance
                || Math.Abs(a.CrossAt(end) - b.CrossAt(end)) > XyTolerance) return;
            result.SharedEdgePairs++;
            bool coarseFine = Math.Abs((a.End - a.Start) - (b.End - b.Start)) > XyTolerance;
            if (coarseFine) result.CoarseFinePairs++;
            var example = new Example { First = a, Second = b, Start = start, End = end,
                GapStart = b.HeightAt(start) - a.HeightAt(start), GapEnd = b.HeightAt(end) - a.HeightAt(end) };
            result.MaximumHeightGap = Math.Max(result.MaximumHeightGap, example.Gap);
            if (example.Gap <= HeightTolerance) return;
            result.MismatchedPairs++;
            if (coarseFine) result.CoarseFineMismatches++;
            examples.Add(example);
            examples.Sort((x, y) => y.Gap.CompareTo(x.Gap));
            if (examples.Count > 12) examples.RemoveAt(examples.Count - 1);
        }
        private static bool Finite(string text, out double value)
        { return double.TryParse(text, NumberStyles.Float, Invariant, out value) && !double.IsNaN(value) && !double.IsInfinity(value); }
        private static bool Flag(string value)
        {
            bool parsed;
            if (!bool.TryParse(value, out parsed)) throw new InvalidDataException("Invalid boolean field: " + value);
            return parsed;
        }
        private static string F(double value) { return value.ToString("0.######", Invariant); }
        private static string Safe(string value) { return value.Replace("|", "\\|").Replace("`", "'"); }

        private static string Report(string directory, CaptureAnalysis result, Dictionary<string, KindStats> kinds,
            SortedDictionary<string, long> statuses, bool hasTriangles, long edges, long skipped, long checks, List<Example> examples)
        {
            var text = new StringBuilder();
            text.AppendLine("# Political render capture analysis\n");
            text.AppendLine("Capture: `" + Safe(Path.GetFullPath(directory)) + "`\n");
            text.AppendLine("Offline measurements of captured geometry. These results do not identify the cause of a rendered pixel or establish whole-map continuity.\n");
            text.AppendLine("## Sampled geometry\n");
            text.AppendLine("Counts are distinct IDs within each geometry kind; CSV sample rows are counted separately. Fill sampling is sparse and distinct IDs here need not equal the full triangle dump.\n");
            text.AppendLine("| Kind | Accepted IDs | Rejected IDs | Support-rejected IDs | Height-rejected IDs | Accepted / rejected sample rows |");
            text.AppendLine("|---|---:|---:|---:|---:|---:|");
            foreach (var pair in kinds)
            {
                KindStats s = pair.Value;
                text.AppendLine("| " + pair.Key + " | " + s.Accepted.Count + " | " + s.Rejected.Count + " | " + s.Support.Count + " | " + s.Height.Count + " | " + s.Samples[0] + " / " + s.Samples[1] + " |");
                if (s.Accepted.Overlaps(s.Rejected)) text.AppendLine("\nInconsistent status: some " + pair.Key + " IDs occur as both accepted and rejected.\n");
            }
            text.AppendLine("\n| Kind / status | Negative close terrain samples | Negative full terrain samples | Samples with an unknown terrain clearance |");
            text.AppendLine("|---|---:|---:|---:|");
            foreach (var pair in kinds) for (int i = 0; i < 2; i++)
                text.AppendLine("| " + pair.Key + " / " + (i == 0 ? "accepted" : "rejected candidate") + " | " + pair.Value.NegativeCloseTerrain[i] + " | " + pair.Value.NegativeFullTerrain[i] + " | " + pair.Value.UnknownTerrain[i] + " |");
            text.AppendLine("\nNegative means a finite sampled clearance below zero; it is not an extent measurement. Rejected candidates are not submitted geometry. Terrain queries exclude flora and scene objects.\n");
            text.AppendLine("## Numerical water candidates\n");
            text.AppendLine("Water coverage validity is **unknown** for all " + result.WaterCoverageUnknownSamples + " samples. " + result.NegativeWaterCandidateSamples + " sample rows have a negative finite close or full water-height candidate difference. This is not proof of submersion; match the query to visible water at that position.\n");
            foreach (var pair in statuses) text.AppendLine("- `" + Safe(pair.Key) + "`: " + pair.Value + " sample rows.");
            text.AppendLine("\n## Fill edge measurements\n");
            if (!hasTriangles) text.AppendLine("`fill-triangles.csv` is absent: no edge continuity analysis was possible.\n");
            else
            {
                text.AppendLine("Triangle rows: " + result.TriangleRows + "; analyzed: " + Math.Min(result.TriangleRows, TriangleLimit) + "; analyzer limit: " + TriangleLimit + ".");
                text.AppendLine("Axis-aligned nondegenerate edges: " + edges + "; other/degenerate edges skipped: " + skipped + ". XY tolerance: 0.0001; height mismatch tolerance: 0.0001 world units.\n");
                text.AppendLine("Positive-length, collinear overlap pairs with triangle interiors on opposite sides: **" + result.SharedEdgePairs + "**; coarse/fine pairs: **" + result.CoarseFinePairs + "**.");
                text.AppendLine("Height-mismatched overlap pairs: **" + result.MismatchedPairs + "**; coarse/fine mismatches: **" + result.CoarseFineMismatches + "**; maximum absolute endpoint height gap: **" + F(result.MaximumHeightGap) + "** world units.");
                text.AppendLine("Candidate pair checks: " + Math.Min(checks, PairCheckLimit) + "; safety limit reached: " + result.PairScanLimited.ToString().ToLowerInvariant() + ".\n");
                text.AppendLine("Each gap compares linearly interpolated Z at the two actual overlap endpoints. Point-only contact and disjoint collinear edges are excluded. Opposite sides narrow the candidates but do not prove global mesh adjacency; overlaps, duplicates and non-axis edges need separate inspection. No missing region is bridged.\n");
                if (examples.Count > 0)
                {
                    text.AppendLine("Largest measured mismatch examples (signed Z is second triangle minus first):\n");
                    text.AppendLine("| Triangle IDs | Axis / fixed coordinates | Overlap interval | Start Z difference | End Z difference |");
                    text.AppendLine("|---|---|---|---:|---:|");
                    foreach (Example e in examples) text.AppendLine("| " + e.First.Triangle + " / " + e.Second.Triangle + " | " + (e.First.Axis == 0 ? "Y at X=" : "X at Y=") + F(e.First.Line) + ", " + F(e.Second.Line) + " | " + F(e.Start) + " to " + F(e.End) + " | " + F(e.GapStart) + " | " + F(e.GapEnd) + " |");
                }
            }
            text.AppendLine("\n## Capture limits and timeline evidence\n");
            text.AppendLine("Full-geometry runtime cap expected: 262144 triangles. Rows at or above the cap may be truncated; a row count below the cap does not prove capture completion. Analyzer row/pair limits also restrict conclusions.\n");
            var evidence = new List<string>();
            string timeline = Path.Combine(directory, "identity-timeline.log");
            string truncated = "unknown";
            if (File.Exists(timeline)) foreach (string line in File.ReadLines(timeline))
            {
                if (Regex.IsMatch(line, "cap|truncat|complete|aborted", RegexOptions.IgnoreCase))
                { if (evidence.Count == 30) evidence.RemoveAt(0); evidence.Add(line); }
                Match match = Regex.Match(line, @"fillGeometryTruncated\s*=\s*(true|false)", RegexOptions.IgnoreCase);
                if (match.Success) truncated = match.Groups[1].Value.ToLowerInvariant();
            }
            text.AppendLine("Explicit runtime full-geometry truncation status: **" + truncated + "**.");
            text.AppendLine("CSV cap reached: **" + (result.TriangleRows >= TriangleLimit).ToString().ToLowerInvariant() + "**; analyzer discarded rows: **" + Math.Max(0, result.TriangleRows - TriangleLimit) + "**.\n");
            if (evidence.Count == 0) text.AppendLine("No completion/cap/truncation timeline evidence found.\n");
            else { text.AppendLine("Last relevant timeline lines (up to 30):\n"); foreach (string line in evidence) text.AppendLine("- `" + Safe(line) + "`"); }
            return text.ToString();
        }
    }
}
