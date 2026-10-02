using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AgesOfCalradia.PoliticalBorderComparison;
internal static class BoundaryTests
{
    private static int checks;
    private static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    private static double Length(P a,P b){double x=b.X-a.X,y=b.Y-a.Y;return Math.Sqrt(x*x+y*y);}
    // World-distance tolerance covers float rounding (ULP ~0.000122 near X=1038).
    // The previous fixed cross-product tolerance varied with parent edge length.
    private static bool Inside(Face f,P p,double tolerance=0.00015)
    {
        double s=Math.Sign(FillBoundaryBands.Cross(f.A,f.B,f.C));
        return s*FillBoundaryBands.Cross(f.A,f.B,p)>=-tolerance*Length(f.A,f.B) && s*FillBoundaryBands.Cross(f.B,f.C,p)>=-tolerance*Length(f.B,f.C) && s*FillBoundaryBands.Cross(f.C,f.A,p)>=-tolerance*Length(f.C,f.A);
    }
    private static void Validate(List<Face> source,List<Face> bands)
    {
        foreach(Face f in bands)
        {
            Face original=source[f.Parent];
            if(f.Color!=original.Color)throw new Exception("Ownership color changed.");
            foreach(P p in f.Points)
                if(!Inside(original,p))throw new Exception("Parent="+f.Parent+";p="+p.X+","+p.Y+";cross="+FillBoundaryBands.Cross(original.A,original.B,p)+","+FillBoundaryBands.Cross(original.B,original.C,p)+","+FillBoundaryBands.Cross(original.C,original.A,p)+";a="+original.A.X+","+original.A.Y+";b="+original.B.X+","+original.B.Y+";c="+original.C.X+","+original.C.Y);
        }
        checks++;
    }
    private static int Main(string[] args)
    {
        // Test-runner boundary: return a failure without Windows crash dialogs.
        try { Run(args); return 0; }
        catch(Exception ex) { Console.Error.WriteLine("Boundary verification FAILED: "+ex); return 1; }
    }
    private static void Run(string[] args)
    {
        var a=new P(0,0,3);var b=new P(10,0,3);var c=new P(10,10,3);var d=new P(0,10,3);
        var source=new List<Face>{new Face{A=a,B=b,C=c,Color=1,Parent=0},new Face{A=a,B=c,C=d,Color=1,Parent=1}};
        var bands=FillBoundaryBands.Build(source);Validate(source,bands);
        Check(!bands.Any(f=>Inside(f,new P(5,5,3),1e-6)),"Same-color diagonal must not become a border.");
        Check(bands.Any(f=>Inside(f,new P(0.5f,0.5f,3))),"Corner join missing.");
        Check(bands.Any(f=>Inside(f,new P(5,0.5f,3))),"Outer strip missing.");
        source[1].Color=2;bands=FillBoundaryBands.Build(source);Validate(source,bands);
        Check(bands.Any(f=>Inside(f,new P(5,5,3))),"Different-color frontier missing.");
        source.Add(new Face{A=new P(20,0,3),B=new P(30,0,3),C=new P(20,10,3),Color=1,Parent=2});
        bands=FillBoundaryBands.Build(source);Validate(source,bands);
        Check(!bands.Any(f=>Inside(f,new P(15,1,3))),"Disconnected islands were bridged.");
        bool rejected=false;try{FillBoundaryBands.Build(new[]{source[0],source[0],source[0]});}catch(InvalidOperationException){rejected=true;}
        Check(rejected,"Nonmanifold source must be rejected.");
        if(args.Length>0)
        {
            source.Clear();
            using(var reader=new StreamReader(args[0]))
            {
                reader.ReadLine();string line;
                while((line=reader.ReadLine())!=null)
                {
                    string[] f=line.Split(',');var p=new P[3];
                    for(int i=0;i<3;i++){int j=1+i*3;p[i]=new P(float.Parse(f[j],CultureInfo.InvariantCulture),float.Parse(f[j+1],CultureInfo.InvariantCulture),float.Parse(f[j+2],CultureInfo.InvariantCulture));}
                    source.Add(new Face{A=p[0],B=p[1],C=p[2],Color=uint.Parse(f[12],CultureInfo.InvariantCulture),Parent=source.Count});
                }
            }
            bands=FillBoundaryBands.Build(source);Validate(source,bands);
            Check(source.Count==118254,"Wrong runtime capture.");
            Console.WriteLine("Actual corrected fill: "+source.Count+" triangles; boundary bands: "+bands.Count+"; every output vertex remains in its accepted parent.");
        }
        Console.WriteLine(checks+" boundary checks passed.");
    }
}
