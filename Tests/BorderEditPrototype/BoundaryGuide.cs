using System;
namespace Aoc.BorderEditPrototype
{
    // Local, bounded boundary search. Reads land/colour labels; never changes ownership.
    internal sealed class BoundaryGuide
    {
        private readonly Func<Point2,uint> _label;
        private Point2? _input;private Point2 _result;private bool _found;
        internal BoundaryGuide(Func<Point2,uint> label){_label=label;}
        internal bool Trace(Point2 from,Point2 to,out Point2[] points)
        {
            points=null;float distance=(to-from).Length;
            if(distance>6f)return false;
            int count=Math.Max(1,(int)Math.Ceiling(distance));
            var path=new Point2[count];Point2 previous=from;
            for(int i=0;i<count;i++)
            {
                Point2 target=from+(to-from)*((i+1f)/count),snapped;
                if(!Snap(target,out snapped)||(snapped-target).Length>.65f||(snapped-previous).Length>1.8f)return false;
                path[i]=snapped;previous=snapped;
            }
            points=path;return true;
        }
        internal bool Snap(Point2 point,out Point2 result)
        {
            if(_input.HasValue && (point-_input.Value).Length<.15f){result=_result;return _found;}
            _input=point;_found=false;float best=3.01f;
            uint center=_label(point);Point2 inside=point,outside=point;
            // At most 97 label samples plus 7 refinement samples per update.
            for(int direction=0;direction<16;direction++)
            {
                double angle=direction*Math.PI/8;
                Point2 step=new Point2((float)Math.Cos(angle),(float)Math.Sin(angle));
                for(float distance=.5f;distance<=3f;distance+=.5f)
                {
                    Point2 candidate=point+step*distance;
                    if(_label(candidate)==center)continue;
                    if(distance<best){best=distance;inside=point+step*(distance-.5f);outside=candidate;_found=true;}
                    break;
                }
            }
            if(_found)
            {
                for(int i=0;i<7;i++)
                {
                    Point2 mid=(inside+outside)*.5f;
                    if(_label(mid)==center)inside=mid;else outside=mid;
                }
                _result=(inside+outside)*.5f;
            }
            result=_result;return _found;
        }
    }
}
