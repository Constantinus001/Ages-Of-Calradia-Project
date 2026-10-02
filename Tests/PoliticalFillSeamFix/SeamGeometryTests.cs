using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using AgesOfCalradia.PoliticalFillSeamFix;

internal static class SeamGeometryTests
{
    private static int _checks;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static void Require(bool pass, string description)
    { if (!pass) throw new InvalidOperationException(description); _checks++; }
    private static SeamVertex P(float x, float y, float z, float u = 0, float v = 0)
    { return new SeamVertex(x, y, z, u, v); }
    private static SeamTriangle T(int id, SeamVertex a, SeamVertex b, SeamVertex c)
    { return new SeamTriangle(id, id % 177, 0xFF000000u | ((uint)id * 2654435761u & 0xFFFFFFu), a, b, c); }
    private static List<SeamTriangle> Seam(float tilt = 0)
    { return new List<SeamTriangle> {
        T(1, P(0,0,4,0,0),P(2,tilt,4,1,0),P(0,2,4,0,1)),
        T(2, P(1,tilt/2,9,99,99),P(0,0,4),P(0,-1,4)),
        T(3, P(2,tilt,4),P(1,tilt/2,9,99,99),P(2,-1,4)) }; }
    private static SeamBuildResult Build(List<SeamTriangle> source)
    {
        SeamBuildResult output; string error;
        Require(FillSeamConformer.TryBuild(source, out output, out error), "Conforming build failed: " + error);
        Verify(source, output); return output;
    }
    private static double SignedArea(SeamVertex a, SeamVertex b, SeamVertex c)
    { return (((double)b.X-a.X)*((double)c.Y-a.Y)-((double)b.Y-a.Y)*((double)c.X-a.X))/2; }
    private static void Verify(List<SeamTriangle> source, SeamBuildResult result)
    {
        var originals = new Dictionary<int, SeamTriangle>(); var areas = new Dictionary<int, double>();
        var counts = new Dictionary<int, int>(); var corners = new Dictionary<int, int>();
        var onlyFace = new Dictionary<int, SeamTriangle>();
        foreach (SeamTriangle t in source) originals.Add(t.ParentId, t);
        foreach (SeamTriangle t in result.Triangles)
        {
            SeamTriangle original;
            if (!originals.TryGetValue(t.ParentId, out original)) throw new InvalidOperationException("Invented parent.");
            if (t.RowId != original.RowId || t.Color != original.Color) throw new InvalidOperationException("Owner color or row changed.");
            int count; counts.TryGetValue(t.ParentId,out count); counts[t.ParentId]=count+1; onlyFace[t.ParentId]=t;
            double area = SignedArea(t.A,t.B,t.C), parentArea = SignedArea(original.A,original.B,original.C);
            if (area * parentArea <= 0) throw new InvalidOperationException("Output winding changed.");
            foreach (SeamVertex p in new[] {t.A,t.B,t.C})
            {
                int mask; corners.TryGetValue(t.ParentId,out mask);
                if(Same(p,original.A))mask|=1;if(Same(p,original.B))mask|=2;if(Same(p,original.C))mask|=4;
                corners[t.ParentId]=mask;
                double wa=SignedArea(p,original.B,original.C)/parentArea;
                double wb=SignedArea(original.A,p,original.C)/parentArea;
                double wc=1-wa-wb;
                if (wa < -0.00001 || wb < -0.00001 || wc < -0.00001)
                    throw new InvalidOperationException("Output crosses original territory footprint.");
                double u=wa*original.A.U+wb*original.B.U+wc*original.C.U;
                double v=wa*original.A.V+wb*original.B.V+wc*original.C.V;
                if (Math.Abs(p.U-u)>.0001 || Math.Abs(p.V-v)>.0001)
                    throw new InvalidOperationException("Parent UV interpolation changed.");
            }
            double sum; areas.TryGetValue(t.ParentId,out sum); areas[t.ParentId]=sum+area;
        }
        foreach (SeamTriangle t in source)
        {
            double expected=SignedArea(t.A,t.B,t.C),actual;
            if (!areas.TryGetValue(t.ParentId,out actual) || Math.Abs(expected-actual)>Math.Max(.000001,Math.Abs(expected)*.000001))
                throw new InvalidOperationException("Lost or altered parent area.");
            if(corners[t.ParentId]!=7)throw new InvalidOperationException("An original vertex or UV was changed or lost.");
            SeamTriangle only=onlyFace[t.ParentId];
            if(counts[t.ParentId]==1 && (!Same(t.A,only.A)||!Same(t.B,only.B)||!Same(t.C,only.C)))
                throw new InvalidOperationException("An unchanged face did not replay exactly.");
        }
        _checks++;
    }
    private static void Main(string[] args)
    {
        var source=Seam(); SeamBuildResult fixedSeam=Build(source);
        Require(fixedSeam.ChangedParentCount==1 && fixedSeam.InsertedBoundaryVertices==1 && fixedSeam.Triangles.Count==6,
            "Only coarse parent subdivides into four fan faces.");
        Require(fixedSeam.MaximumHeightCorrection==5 && fixedSeam.ValidatedSharedEdgePairs>0,"Measured seam is repaired and verified.");
        Require(fixedSeam.Triangles[4].A.U==99 && fixedSeam.Triangles[4].B.Z==4,"Unchanged fine face keeps exact captured attributes.");
        bool found=false;
        foreach(SeamTriangle t in fixedSeam.Triangles) if(t.ParentId==1)
        foreach(SeamVertex p in new[]{t.A,t.B,t.C}) if(p.X==1 && p.Y==0 && p.Z==9 && p.U==.5f && p.V==0) found=true;
        Require(found,"Inserted fine XYZ uses coarse parent's UV, not neighbor UV.");
        Build(Seam(.00002f));
        var reverse=Seam(); for(int i=0;i<reverse.Count;i++){var t=reverse[i];reverse[i]=new SeamTriangle(t.ParentId,t.RowId,t.Color,t.A,t.C,t.B);} Build(reverse);
        var disconnected=new List<SeamTriangle>{T(10,P(0,0,4),P(1,0,4),P(0,1,4)),T(11,P(2,0,9),P(3,0,9),P(2,-1,9))};
        Require(Build(disconnected).ChangedParentCount==0,"Disjoint shore/water gap is not bridged.");
        var pointTouch=new List<SeamTriangle>{T(10,P(0,0,4),P(1,0,4),P(0,1,4)),T(11,P(1,0,4),P(2,0,9),P(2,-1,9))};
        Require(Build(pointTouch).ChangedParentCount==0,"Point-only contact is not an edge.");
        var conflict=Seam(); conflict.Add(T(4,P(1,0,10),P(5,0,4),P(5,1,4)));
        SeamBuildResult result; string error;
        Require(!FillSeamConformer.TryBuild(conflict,out result,out error) && result==null,"Conflicting explicit heights preserve original fallback.");
        var invalid=Seam(); invalid[0]=T(1,P(float.NaN,0,4),P(2,0,4),P(0,2,4));
        Require(!FillSeamConformer.TryBuild(invalid,out result,out error) && result==null,"Invalid capture rejected.");
        var duplicate=Seam(); duplicate.Add(duplicate[0]);
        Require(!FillSeamConformer.TryBuild(duplicate,out result,out error),"Duplicate parent identity rejected.");
        var offset=Seam();offset[1]=T(2,P(1,-.00005f,9),P(0,-.00005f,4),P(0,-1,4));
        Require(!FillSeamConformer.TryBuild(offset,out result,out error),"Nearby offset edge is rejected instead of moving XY footprint.");
        var sameSide=new List<SeamTriangle>{T(1,P(0,0,4),P(2,0,4),P(0,2,4)),T(2,P(0,0,4),P(1,0,9),P(0,1,4))};
        Require(Build(sameSide).ChangedParentCount==0,"Same-side overlap does not manufacture neighbor joins.");
        Require(!FillSeamConformer.TryBuild(new List<SeamTriangle>(),out result,out error),"Empty source rejected.");
        if(args.Length>0)
        {
            var captured=new List<SeamTriangle>(); using(var reader=new StreamReader(args[0]))
            { reader.ReadLine();string line;while((line=reader.ReadLine())!=null){string[] fields=line.Split(',');var p=new SeamVertex[3];for(int i=0;i<3;i++){
                int j=1+i*3;float x=float.Parse(fields[j],Invariant),y=float.Parse(fields[j+1],Invariant),z=float.Parse(fields[j+2],Invariant);p[i]=P(x,y,z,x/1024f,y/1024f);}
                captured.Add(T(int.Parse(fields[0],Invariant),p[0],p[1],p[2]));}}
            var timer=Stopwatch.StartNew(); SeamBuildResult full=Build(captured);timer.Stop();
            Console.WriteLine("Full capture: source="+full.SourceCount+";output="+full.Triangles.Count+";changedParents="+full.ChangedParentCount
                +";insertions="+full.InsertedBoundaryVertices+";maxHeightCorrection="+full.MaximumHeightCorrection.ToString("R",Invariant)
                +";validatedOverlapPairs="+full.ValidatedSharedEdgePairs+";milliseconds="+timer.ElapsedMilliseconds);
            Require(full.SourceCount==83058 && full.ChangedParentCount==9902 && full.InsertedBoundaryVertices==15392
                && full.Triangles.Count==118254,"Complete September 9 capture matches independently computed correction scope.");
            if(args.Length>1) WriteGeometry(args[1],full.Triangles);
        }
        Console.WriteLine("Conforming political fill: "+_checks+" behavioral checks passed.");
    }
    private static bool Same(SeamVertex a,SeamVertex b)
    {return a.X==b.X&&a.Y==b.Y&&a.Z==b.Z&&a.U==b.U&&a.V==b.V;}
    private static void WriteGeometry(string path,List<SeamTriangle> triangles)
    {
        using(var writer=new StreamWriter(path))
        { writer.WriteLine("triangle,ax,ay,az,bx,by,bz,cx,cy,cz");int id=0;
          foreach(SeamTriangle t in triangles){writer.Write(++id);foreach(SeamVertex p in new[]{t.A,t.B,t.C})
          {writer.Write(","+p.X.ToString("R",Invariant)+","+p.Y.ToString("R",Invariant)+","+p.Z.ToString("R",Invariant));}writer.WriteLine();}}
    }
}
