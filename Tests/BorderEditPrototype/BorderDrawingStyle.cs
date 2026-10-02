using System;
namespace Aoc.BorderEditPrototype
{
    internal static class BorderDrawingStyle
    {
        internal static void Resolve(Point2 a,Point2 b,Func<Point2,bool> land,Func<Point2,uint> color,out uint left,out uint right)
        {
            Point2 d=b-a;if(d.Length<.03f)throw new ArgumentException("Place the next point farther away.");
            Point2 n=new Point2(-d.Y,d.X)*(2.75f/d.Length),mid=(a+b)*.5f;
            bool l=land(mid+n),r=land(mid-n);
            if(!l&&!r)
            {
                // Narrow headlands can have water at both wide side samples.
                // Check the stroke itself and closer supports before rejecting it.
                Point2[] support={mid,a,b,mid+n*(.8f/2.75f),mid-n*(.8f/2.75f)};
                foreach(Point2 p in support)if(land(p)){left=right=color(p);return;}
                // Political fill eligibility is not proof of visible water. Manual
                // drawing can intentionally cross either; surface validation stays native.
                left=right=color(mid);return;
            }
            left=color(l?mid+n:mid-n);right=color(r?mid-n:mid+n);
        }
    }
}
