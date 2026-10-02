using System;
using System.Collections.Generic;
namespace Aoc.BorderEditPrototype
{
    // Retain bends, endpoints and local colour transitions; limit new chord length.
    internal static class StrokeSimplifier
    {
        internal static void Simplify(ref Point2[] points,ref uint[] left,ref uint[] right)
        {
            var output=new List<Point2>{points[0]};var l=new List<uint>();var r=new List<uint>();
            int start=0;
            while(start<points.Length-1)
            {
                int end=start+1;
                while(end+1<points.Length && Fits(points,left,right,start,end+1))end++;
                output.Add(points[end]);l.Add(left[start]);r.Add(right[start]);start=end;
            }
            points=output.ToArray();left=l.ToArray();right=r.ToArray();
        }
        private static bool Fits(Point2[] points,uint[] left,uint[] right,int start,int end)
        {
            Point2 chord=points[end]-points[start];float length=chord.Length;
            if(length<.03f||length>4f)return false;
            for(int i=start;i<end;i++)if(left[i]!=left[start]||right[i]!=right[start])return false;
            for(int i=start+1;i<end;i++)
            {
                Point2 d=points[i]-points[start];float t=Point2.Dot(d,chord)/(length*length);
                if(t<0||t>1||(d-chord*t).Length>.12f)return false;
            }
            return true;
        }
    }
}
