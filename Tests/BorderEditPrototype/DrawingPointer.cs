using System;
using TaleWorlds.Library;
namespace Aoc.BorderEditPrototype
{
    internal static class DrawingPointer
    {
        // Intersect the mouse ray with the same elevated surface used by the drawing.
        // Bounded fixed-point refinement follows relief without shifting a ground hit upward.
        internal static bool Project(Vec3 near,Vec3 far,Point2 seed,Func<Point2,float> height,out Point2 point)
        {
            point=seed;Vec3 ray=far-near;
            if(!near.IsValid||!far.IsValid||Math.Abs(ray.z)<.00001f)return false;
            for(int i=0;i<12;i++)
            {
                float z=height(point),t=(z-near.z)/ray.z;
                if(float.IsNaN(t)||float.IsInfinity(t)||t<0||t>1)return false;
                Vec3 hit=near+ray*t;Point2 next=new Point2(hit.x,hit.y);
                if((next-point).Length<.015f){point=next;return true;}
                point=next;
            }
            return false;
        }
    }
}
