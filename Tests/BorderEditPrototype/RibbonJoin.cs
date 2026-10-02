using System;
using System.Linq;
namespace Aoc.BorderEditPrototype
{
    // Shared cross sections for authored chains. Native captured ribbons retain their geometry.
    internal static class RibbonJoin
    {
        internal static Point2 Offset(EditSnapshot state,BorderEdge edge,int node,float halfWidth)
        {
            Point2 direction=state.Points[edge.B]-state.Points[edge.A];
            float length=direction.Length;if(length<.0001f)return new Point2();
            direction=direction*(1f/length);Point2 normal=new Point2(-direction.Y,direction.X);
            if(!edge.Authored)return normal*halfWidth;
            BorderEdge[] adjacent=state.Bridges.Where(e=>!ReferenceEquals(e,edge)&&e.Authored&&(e.A==node||e.B==node)).Take(2).ToArray();
            if(adjacent.Length!=1)return normal*halfWidth;
            BorderEdge neighbor=adjacent[0];int other=neighbor.A==node?neighbor.B:neighbor.A;
            Point2 tangent=node==edge.A?state.Points[node]-state.Points[other]:state.Points[other]-state.Points[node];
            float neighborLength=tangent.Length;if(neighborLength<.0001f)return normal*halfWidth;
            tangent=tangent*(1f/neighborLength);
            Point2 bisector=normal+new Point2(-tangent.Y,tangent.X);float magnitude=bisector.Length;
            if(magnitude<.0001f)return normal*halfWidth;
            bisector=bisector*(1f/magnitude);float alignment=Math.Abs(Point2.Dot(bisector,normal));
            // Both incident edges obtain the same shared corner. Limit the miter at
            // hairpin turns and short samples so a sharp cursor movement cannot spike.
            float limit=Math.Min(2f*Math.Abs(halfWidth),Math.Max(Math.Abs(halfWidth),.5f*Math.Min(length,neighborLength)));
            float reach=Math.Min(Math.Abs(halfWidth)/Math.Max(.0001f,alignment),limit);
            return bisector*(halfWidth<0?-reach:reach);
        }
    }
}
