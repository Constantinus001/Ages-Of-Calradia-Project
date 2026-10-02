using System;
using System.Collections.Generic;
using System.Linq;
namespace Aoc.BorderEditPrototype
{
    // Pure bounded polygon geometry. Accepted polygons are simple and may be concave.
    internal static class FillGeometry
    {
        internal static float Cross(Point2 a,Point2 b,Point2 c) => (b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
        internal static bool Contains(Point2[] polygon,Point2 p)
        {
            bool inside=false;
            for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)
                if((polygon[i].Y>p.Y)!=(polygon[j].Y>p.Y) && p.X<(polygon[j].X-polygon[i].X)*(p.Y-polygon[i].Y)/(polygon[j].Y-polygon[i].Y)+polygon[i].X)inside=!inside;
            return inside;
        }
        internal static bool ValidatePoints(Point2[] p,out string reason)
        {
            reason="Fill needs 3 to 32 corners.";
            if(p==null || p.Length<3 || p.Length>32)return false;
            reason="Keep each fill area within 48 map units; use another patch for larger areas.";
            if(p.Any(x=>!x.Finite) || p.Max(x=>x.X)-p.Min(x=>x.X)>48 || p.Max(x=>x.Y)-p.Min(x=>x.Y)>48)return false;
            float area=0;
            for(int i=0;i<p.Length;i++)
            {
                int next=(i+1)%p.Length;
                if((p[i]-p[next]).Length<.1f){reason="Fill corners are too close together.";return false;}
                area+=p[i].X*p[next].Y-p[next].X*p[i].Y;
                for(int j=i+1;j<p.Length;j++)
                {
                    int end=(j+1)%p.Length;if(j==next||end==i)continue;
                    if(Intersect(p[i],p[next],p[j],p[end])){reason="Fill outline crosses itself.";return false;}
                }
            }
            if(Math.Abs(area)<.1f){reason="Fill outline has no area.";return false;}
            reason=null;return true;
        }
        private static bool Intersect(Point2 a,Point2 b,Point2 c,Point2 d)
        {
            if(Math.Max(a.X,b.X)<Math.Min(c.X,d.X)||Math.Max(c.X,d.X)<Math.Min(a.X,b.X)||Math.Max(a.Y,b.Y)<Math.Min(c.Y,d.Y)||Math.Max(c.Y,d.Y)<Math.Min(a.Y,b.Y))return false;
            return Cross(a,b,c)*Cross(a,b,d)<=0 && Cross(c,d,a)*Cross(c,d,b)<=0;
        }
        internal static List<Point2[]> Triangulate(Point2[] p)
        {
            string reason;if(!ValidatePoints(p,out reason))throw new ArgumentException(reason);
            var ids=Enumerable.Range(0,p.Length).ToList();float area=0;
            for(int i=0;i<p.Length;i++)area+=p[i].X*p[(i+1)%p.Length].Y-p[(i+1)%p.Length].X*p[i].Y;
            if(area<0)ids.Reverse();var result=new List<Point2[]>();
            while(ids.Count>3)
            {
                bool clipped=false;
                for(int i=0;i<ids.Count;i++)
                {
                    int a=ids[(i+ids.Count-1)%ids.Count],b=ids[i],c=ids[(i+1)%ids.Count];
                    if(Cross(p[a],p[b],p[c])<=.00001f)continue;
                    if(ids.Any(k=>k!=a&&k!=b&&k!=c&&Cross(p[a],p[b],p[k])>=0&&Cross(p[b],p[c],p[k])>=0&&Cross(p[c],p[a],p[k])>=0))continue;
                    result.Add(new[]{p[a],p[b],p[c]});ids.RemoveAt(i);clipped=true;break;
                }
                if(!clipped)throw new ArgumentException("Fill outline has overlapping or redundant corners.");
            }
            result.Add(ids.Select(i=>p[i]).ToArray());return result;
        }
        internal static List<Point2[]> Subdivide(Point2[] polygon)
        {
            var queue=new Queue<Point2[]>(Triangulate(polygon));var result=new List<Point2[]>();
            while(queue.Count>0)
            {
                if(queue.Count+result.Count>8192)throw new ArgumentException("Fill area is too complex; use smaller patches.");
                Point2[] t=queue.Dequeue();int longest=0;
                for(int i=1;i<3;i++)if((t[i]-t[(i+1)%3]).Length>(t[longest]-t[(longest+1)%3]).Length)longest=i;
                Point2 a=t[longest],b=t[(longest+1)%3],c=t[(longest+2)%3];
                if((a-b).Length<=2f){result.Add(t);continue;}
                Point2 mid=(a+b)*.5f;queue.Enqueue(new[]{a,mid,c});queue.Enqueue(new[]{mid,b,c});
            }
            return result;
        }
    }
}
