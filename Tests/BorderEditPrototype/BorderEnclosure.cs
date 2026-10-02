using System;
using System.Collections.Generic;
using System.Linq;
namespace Aoc.BorderEditPrototype
{
    // Walk directed edges around planar faces. Cache against graph revision, never screen pixels.
    internal sealed class BorderEnclosure
    {
        private int _revision=-1;
        private readonly List<Point2[]> _faces=new List<Point2[]>();
        internal bool Find(BorderGraph graph,Point2 point,out Point2[] polygon,out string reason)
        {
            if(_revision!=graph.Revision){Build(graph);_revision=graph.Revision;}
            polygon=_faces.Where(p=>FillGeometry.Contains(p,point)).OrderBy(Area).FirstOrDefault();
            reason="No closed border here. Connect the open ends first.";
            if(polygon==null)return false;
            Point2[] selected=polygon;
            if(_faces.Any(p=>!ReferenceEquals(p,selected)&&p.All(v=>FillGeometry.Contains(selected,v))))
            {reason="This outline contains another enclosed border. Fill the inner area or split the outer area first.";return false;}
            return FillGeometry.ValidatePoints(polygon,out reason);
        }
        private void Build(BorderGraph graph)
        {
            _faces.Clear();var state=graph.Current;
            var neighbors=new Dictionary<int,List<int>>();
            for(int i=0;i<graph.EdgeCount;i++)
            {
                if(i<graph.Edges.Length&&state.Deleted[i])continue;
                BorderEdge e=graph.EdgeAt(i);
                if(!neighbors.ContainsKey(e.A))neighbors[e.A]=new List<int>();
                if(!neighbors.ContainsKey(e.B))neighbors[e.B]=new List<int>();
                if(!neighbors[e.A].Contains(e.B))neighbors[e.A].Add(e.B);
                if(!neighbors[e.B].Contains(e.A))neighbors[e.B].Add(e.A);
            }
            foreach(var entry in neighbors)
            {
                Point2 origin=state.Points[entry.Key];
                entry.Value.Sort((a,b)=>Math.Atan2(state.Points[a].Y-origin.Y,state.Points[a].X-origin.X).CompareTo(Math.Atan2(state.Points[b].Y-origin.Y,state.Points[b].X-origin.X)));
            }
            var visited=new HashSet<long>();
            foreach(var entry in neighbors)foreach(int target in entry.Value)
            {
                int a=entry.Key,b=target;long first=Key(a,b);
                if(visited.Contains(first))continue;
                var ids=new List<int>();bool closed=false;
                while(visited.Add(Key(a,b)))
                {
                    ids.Add(a);var next=neighbors[b];int index=next.IndexOf(a);
                    int c=next[(index+next.Count-1)%next.Count];a=b;b=c;
                    if(Key(a,b)==first){closed=true;break;}
                }
                if(!closed||ids.Count<3||ids.Distinct().Count()!=ids.Count)continue;
                var points=ids.Select(id=>state.Points[id]).ToList();
                // Remove only redundant straight vertices, preserving the exact outline.
                for(int i=points.Count-1;i>=0&&points.Count>3;i--)
                {
                    Point2 p=points[(i+points.Count-1)%points.Count],q=points[i],r=points[(i+1)%points.Count];
                    if(Math.Abs(FillGeometry.Cross(p,q,r))<.00001f&&Point2.Dot(q-p,q-r)<=0)points.RemoveAt(i);
                }
                Point2[] face=points.ToArray();if(Area(face)>.05f)_faces.Add(face);
            }
        }
        private static long Key(int a,int b)=>((long)a<<32)|(uint)b;
        private static float Area(Point2[] p)
        {
            float area=0;for(int i=0;i<p.Length;i++)area+=p[i].X*p[(i+1)%p.Length].Y-p[(i+1)%p.Length].X*p[i].Y;
            return area*.5f;
        }
    }
}
