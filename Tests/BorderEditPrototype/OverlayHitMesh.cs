using System;
using System.Collections.Generic;
using TaleWorlds.Library;
namespace Aoc.BorderEditPrototype
{
    // CPU-only geometry from displayed overlay vertices. Bounds reject distant batches.
    internal sealed class OverlayHitMesh
    {
        private struct Triangle { internal Vec3 A,B,C;internal int Edge; }
        private readonly List<Triangle> _triangles=new List<Triangle>();
        private Vec3 _min=new Vec3(float.MaxValue,float.MaxValue,float.MaxValue),_max=new Vec3(float.MinValue,float.MinValue,float.MinValue);
        internal void AddQuad(int edge,Vec3 a,Vec3 b,Vec3 c,Vec3 d)
        {
            _triangles.Add(new Triangle{Edge=edge,A=a,B=b,C=c});_triangles.Add(new Triangle{Edge=edge,A=a,B=c,C=d});
            foreach(Vec3 p in new[]{a,b,c,d})
            { _min=new Vec3(Math.Min(_min.x,p.x),Math.Min(_min.y,p.y),Math.Min(_min.z,p.z));_max=new Vec3(Math.Max(_max.x,p.x),Math.Max(_max.y,p.y),Math.Max(_max.z,p.z)); }
        }
        internal int Pick(Vec3 near,Vec3 far,float height,ref float nearest)
        {
            Vec3 shift=new Vec3(0,0,height);near-=shift;far-=shift;
            Vec3 direction=far-near;float lo=0,hi=1;
            if(!Slab(near.x,direction.x,_min.x,_max.x,ref lo,ref hi)||!Slab(near.y,direction.y,_min.y,_max.y,ref lo,ref hi)||!Slab(near.z,direction.z,_min.z,_max.z,ref lo,ref hi))return -1;
            int id=-1;
            foreach(Triangle t in _triangles)
            { float distance=NativeSelection.RayTriangle(near,far,t.A,t.B,t.C);if(distance>=0&&distance<nearest){nearest=distance;id=t.Edge;} }
            return id;
        }
        private static bool Slab(float origin,float direction,float min,float max,ref float lo,ref float hi)
        {
            if(Math.Abs(direction)<.000001f)return origin>=min&&origin<=max;
            float a=(min-origin)/direction,b=(max-origin)/direction;
            lo=Math.Max(lo,Math.Min(a,b));hi=Math.Min(hi,Math.Max(a,b));return lo<=hi;
        }
        internal static bool ProjectDrag(Vec3 near,Vec3 far,float height,out Point2 point)
        {
            point=new Point2();float dz=far.z-near.z;
            if(!near.IsValid||!far.IsValid||Math.Abs(dz)<.000001f)return false;
            float t=(height-near.z)/dz;if(t<0||t>1)return false;
            Vec3 hit=near+(far-near)*t;point=new Point2(hit.x,hit.y);return point.Finite;
        }
    }
}
