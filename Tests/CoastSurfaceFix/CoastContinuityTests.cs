using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AgesOfCalradia.CoastSurfaceFix;
using TaleWorlds.Library;

internal static class CoastContinuityTests
{
    private static int _checks;
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); _checks++; }
    private static float F(string value) { return float.Parse(value, CultureInfo.InvariantCulture); }
    private static bool Contains(List<Vec3> poly, Vec3 point)
    {
        if(poly.Count<3) return false;
        for(int i=1;i+1<poly.Count;i++)
        {
            double a=CoastCapClipper.AreaTwice(poly[0],poly[i],point),b=CoastCapClipper.AreaTwice(poly[i],poly[i+1],point),c=CoastCapClipper.AreaTwice(poly[i+1],poly[0],point);
            if(Math.Abs(CoastCapClipper.AreaTwice(poly[0],poly[i],poly[i+1]))<1e-8)continue;
            if((a>=-1e-7&&b>=-1e-7&&c>=-1e-7)||(a<=1e-7&&b<=1e-7&&c<=1e-7))return true;
        }
        return false;
    }
    private static void CheckCaps()
    {
        foreach(float[] angles in new[]{new[]{0f},new[]{0f,180f},new[]{0f,90f},new[]{0f,45f},new[]{0f,90f,225f}})
        {
            var directions=angles.Select(a=>new Vec2((float)Math.Cos(a*Math.PI/180),(float)Math.Sin(a*Math.PI/180))).ToList();
            var before=new List<List<Vec3>>();var after=new List<List<Vec3>>();
            var center=new Vec3(0,0,7.57f);
            for(int i=0;i<8;i++)
            {
                var a=new Vec3((float)Math.Cos(i*Math.PI/4)*.8f,(float)Math.Sin(i*Math.PI/4)*.8f,7.57f);
                var b=new Vec3((float)Math.Cos((i+1)*Math.PI/4)*.8f,(float)Math.Sin((i+1)*Math.PI/4)*.8f,7.57f);
                before.Add(new List<Vec3>{center,a,b});after.Add(CoastCapClipper.Clip(center,a,b,directions));
            }
            for(int x=-39;x<=39;x++)for(int y=-39;y<=39;y++)
            {
                var p=new Vec3(x*.02f,y*.02f,7.57f);
                if(directions.Any(d=>Math.Abs(d.x*p.x+d.y*p.y)<.0001f))continue;
                bool coveredByStrip=directions.Any(d=>d.x*p.x+d.y*p.y>0);
                bool oldCap=before.Any(poly=>Contains(poly,p)),newCap=after.Any(poly=>Contains(poly,p));
                Check(!(newCap&&coveredByStrip),"Clipped cap still overlaps an incident strip.");
                Check((oldCap||coveredByStrip)==(newCap||coveredByStrip),"Cap clipping opened a gap or extended coverage.");
            }
            Check(after.SelectMany(p=>p).All(v=>Math.Abs(v.z-7.57f)<.0001f),"Cap clipping changed a flat accepted surface height.");
        }
    }
    private static List<Tuple<Vec2, Vec2>> Scan(Func<Vec2, object> region, int size)
    {
        var output = new List<Tuple<Vec2, Vec2>>();
        var scanner = new CoastTopologyScanner(Vec2.Zero, new Vec2(size, size), size, size, region, (a,b) => output.Add(Tuple.Create(a,b)));
        while (!scanner.Complete) scanner.AdvanceRow();
        int count = output.Count; scanner.AdvanceRow();
        Check(count == output.Count && scanner.CompletedRows == size, "Completed scan must not duplicate spans.");
        return output;
    }
    private static int Main(string[] args)
    {
        try
        {
            CheckCaps();
            Check(Scan(p => "sea", 3).Count == 0, "Uniform/excluded region must produce no invented edges.");
            var straight = Scan(p => p.x < 1 ? "land" : "sea", 3);
            Check(straight.Count == 3 && straight.All(e => e.Item1.x == .5f && e.Item2.x == .5f), "Straight coast must remain connected along original crossings.");
            var saddle = Scan(p => (p.x == 0 && p.y == 0) || (p.x == 1 && p.y == 1) || (p.x == .5f && p.y == .5f) ? "A" : "B", 1);
            Check(saddle.Count == 2 && saddle[0].Item1.Equals(new Vec2(.5f,0)) && saddle[0].Item2.Equals(new Vec2(1,.5f)), "Saddle pairing must honor the protected center decider.");
            var junction = Scan(p => p.x == 0 && p.y == 0 ? "A" : p.x == 1 && p.y == 0 ? "B" : "C", 1);
            Check(junction.Count == 3 && junction.All(e => e.Item2.Equals(new Vec2(.5f,.5f))), "Three-owner junction must retain center spokes.");

            var first = new Vec2(0,0); var second = new Vec2(2,0); var direction = new Vec2(1,0);
            Check(CoastSupportPolicy.HasFootprintSupport(first, second, direction, p => Math.Abs(p.y) < .2f), "A thin included peninsula missed by wide probes must survive.");
            Check(!CoastSupportPolicy.HasFootprintSupport(first, second, direction, p => Math.Abs(p.y) < .2f && Math.Abs(p.x-1) > .1f), "A water channel cutting the entire ribbon must remain a gap.");
            Check(!CoastSupportPolicy.HasFootprintSupport(first, second, direction, p => false), "Excluded island/no political land must remain rejected.");
            Check(!CoastSupportPolicy.HasFootprintSupport(first, second, direction, p => Math.Abs(p.y-1.5f) < .1f), "Land outside the ribbon cannot rescue failed original probes.");
            var plan = new CoastJoinPlan(); plan.Add(first, second, false);
            Check(plan.AtJoin(first) && plan.AtJoin(new Vec2(0,.8f)) && !plan.AtJoin(new Vec2(0,1)), "Correction must cover complete join cross sections but not distant inland ribbon.");
            var heights = new CoastHeightCache(); heights.Add(new Vec2(704.133545f,281.326f), 7.57f);
            float z;
            Check(heights.TryGetValue(new Vec2(704.133545f,281.326019f), out z) && z == 7.57f, "Captured float endpoint mismatch must resolve to one height.");
            Check(!heights.TryGetValue(new Vec2(704.133545f,281.327f), out z), "Distinct nearby geometry cannot be welded by height cache.");

            if (args.Length != 1) throw new ArgumentException("Provide the successful v1 capture directory.");
            var rows = File.ReadLines(Path.Combine(args[0], "coastline-paths.csv")).Skip(1).Select(s => s.Split(',')).ToArray();
            var capturedPlan = new CoastJoinPlan(); var expected = new List<Vec2>();
            foreach (var row in rows)
            {
                if (row[1] != "True") continue;
                Check((new Vec2(F(row[2]),F(row[3]))-new Vec2(F(row[4]),F(row[5]))).Length>.8002f,"An accepted span is shorter than the cap clipping proof permits.");
                string sea = (row[6] == "False" ? row[8] : row[9]).Trim('"');
                if (!CoastSurfacePolicy.IsCoast(row[6] == "True", row[7] == "True", sea)) continue;
                var a = new Vec2(F(row[2]),F(row[3])); var b = new Vec2(F(row[4]),F(row[5]));
                capturedPlan.Add(a,b,false); expected.Add(a); expected.Add(b);
            }
            Check(capturedPlan.Coast.Count == 2830, "Replay must include every confirmed accepted coast, not named-location fixtures alone.");
            var replay = new CoastHeightCache();
            int joined = 0;
            foreach (var row in rows.Reverse())
            {
                if (row[1] != "True") continue;
                foreach (var p in new[] { new Vec2(F(row[2]),F(row[3])), new Vec2(F(row[4]),F(row[5])) })
                {
                    bool shared = expected.Any(e => (e-p).LengthSquared <= .00015f*.00015f);
                    if (!shared) continue;
                    Check(capturedPlan.AtJoin(p), "An incident endpoint was missed when drawn before its coast.");
                    if (!replay.TryGetValue(p,out z)) { CoastSurfacePolicy.Project(true,2.57f,false,0,out z); replay.Add(p,z); }
                    Check(Math.Abs(z-7.57f) < .0001f, "Incident segments disagree about shared height.");
                    joined++;
                }
            }
            Check(joined > expected.Count, "Replay must include previously unchanged incident segments, not coast-only samples.");
            Console.WriteLine("PASS: " + _checks + " checks; topology, thin-coast support, excluded/channel guards, shared heights and reversed-order whole-map replay (" + joined + " incident endpoints).");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex.Message); return 1; }
    }
}
