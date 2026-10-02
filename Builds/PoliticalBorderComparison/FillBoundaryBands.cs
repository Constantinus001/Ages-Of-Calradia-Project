using System;
using System.Collections.Generic;
using System.Linq;

namespace AgesOfCalradia.PoliticalBorderComparison
{
    internal readonly struct P
    {
        internal readonly float X,Y,Z;
        internal P(float x,float y,float z){X=x;Y=y;Z=z;}
        internal Tuple<float,float> Key { get { return Tuple.Create(X,Y); } }
    }
    internal sealed class Face
    {
        internal P A,B,C;
        internal uint Color;
        internal int Parent;
        internal P[] Points { get { return new[]{A,B,C}; } }
    }
    internal static class FillBoundaryBands
    {
        private sealed class Edge { internal int Face,Side; internal P A,B; }
        internal static List<Face> Build(IList<Face> input)
        {
            if(input.Count==0 || input.Count>1048576) throw new InvalidOperationException("Invalid source count.");
            var edges=new Dictionary<Tuple<Tuple<float,float>,Tuple<float,float>>,List<Edge>>();
            var widths=new Dictionary<Tuple<int,int>,float>();
            var corners=new Dictionary<Tuple<uint,Tuple<float,float>>,float>();
            for(int i=0;i<input.Count;i++)
            {
                P[] p=input[i].Points;
                if(Math.Abs(Cross(p[0],p[1],p[2]))<1e-8) throw new InvalidOperationException("Degenerate source face.");
                for(int j=0;j<3;j++)
                {
                    P a=p[j],b=p[(j+1)%3];
                    var ka=a.Key; var kb=b.Key;
                    if(Compare(ka,kb)>0){var swap=ka;ka=kb;kb=swap;}
                    var key=Tuple.Create(ka,kb); List<Edge> list;
                    if(!edges.TryGetValue(key,out list))edges.Add(key,list=new List<Edge>());
                    list.Add(new Edge{Face=i,Side=j,A=a,B=b});
                    if(list.Count>2)throw new InvalidOperationException("Nonmanifold fill edge; refuse replacement.");
                }
            }
            foreach(var pair in edges.Values)
            {
                if(pair.Count==2 && input[pair[0].Face].Color==input[pair[1].Face].Color)continue;
                foreach(Edge e in pair)
                {
                    float w=pair.Count==1?1.6f:0.8f;
                    widths.Add(Tuple.Create(e.Face,e.Side),w);
                    foreach(P p in new[]{e.A,e.B})
                    {
                        var key=Tuple.Create(input[e.Face].Color,p.Key);float old;
                        corners.TryGetValue(key,out old);corners[key]=Math.Max(old,w);
                    }
                }
            }
            var output=new List<Face>();
            for(int i=0;i<input.Count;i++)
            {
                Face source=input[i]; P[] p=source.Points;
                for(int j=0;j<3;j++)
                {
                    float width;
                    if(widths.TryGetValue(Tuple.Create(i,j),out width))
                    {
                        P a=p[j],b=p[(j+1)%3],c=p[(j+2)%3];
                        double dx=b.X-a.X,dy=b.Y-a.Y,len=Math.Sqrt(dx*dx+dy*dy);
                        double sign=Math.Sign(Cross(a,b,c));
                        Emit(Clip(p.ToList(), q=>width-sign*(dx*(q.Y-a.Y)-dy*(q.X-a.X))/len),source,output);
                    }
                    // Clip a small polygonal corner disk to THIS source triangle.
                    // Adjacent wedges meet without placing color in excluded holes.
                    if(corners.TryGetValue(Tuple.Create(source.Color,p[j].Key),out width))
                    {
                        P center=p[j]; var polygon=p.ToList();
                        for(int k=0;k<12 && polygon.Count>0;k++)
                        {
                            double angle=k*Math.PI/6, nx=Math.Cos(angle),ny=Math.Sin(angle);
                            polygon=Clip(polygon,q=>width-nx*(q.X-center.X)-ny*(q.Y-center.Y));
                        }
                        Emit(polygon,source,output);
                    }
                }
                if(output.Count>1048576)throw new InvalidOperationException("Border output cap exceeded.");
            }
            if(output.Count==0)throw new InvalidOperationException("No boundary bands found.");
            return output;
        }
        private static List<P> Clip(List<P> input,Func<P,double> distance)
        {
            var result=new List<P>(); if(input.Count==0)return result;
            P previous=input[input.Count-1];double dp=distance(previous);
            foreach(P current in input)
            {
                double dc=distance(current);
                if((dp>=0)!=(dc>=0))
                {
                    double t=dp/(dp-dc);
                    result.Add(new P((float)(previous.X+(current.X-previous.X)*t),
                        (float)(previous.Y+(current.Y-previous.Y)*t),(float)(previous.Z+(current.Z-previous.Z)*t)));
                }
                if(dc>=0)result.Add(current);previous=current;dp=dc;
            }
            return result;
        }
        private static void Emit(List<P> polygon,Face parent,List<Face> output)
        {
            for(int i=1;i+1<polygon.Count;i++)
                if(Math.Abs(Cross(polygon[0],polygon[i],polygon[i+1]))>1e-7)
                    output.Add(new Face{A=polygon[0],B=polygon[i],C=polygon[i+1],Color=parent.Color,Parent=parent.Parent});
        }
        internal static double Cross(P a,P b,P c){return ((double)b.X-a.X)*((double)c.Y-a.Y)-((double)b.Y-a.Y)*((double)c.X-a.X);}
        private static int Compare(Tuple<float,float>a,Tuple<float,float>b){int c=a.Item1.CompareTo(b.Item1);return c==0?a.Item2.CompareTo(b.Item2):c;}
    }
}
