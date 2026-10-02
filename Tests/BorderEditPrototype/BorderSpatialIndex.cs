using System;
using System.Collections.Generic;

namespace Aoc.BorderEditPrototype
{
    // Candidate-space broad phase avoids comparing every moved edge to the entire world.
    internal sealed class BorderSpatialIndex
    {
        private const float CellSize=16f;
        private readonly Dictionary<long,List<int>> _cells=new Dictionary<long,List<int>>();
        internal void Add(int id,Point2 a,Point2 b)
        {
            Visit(a,b,key=>
            {
                List<int> items;
                if(!_cells.TryGetValue(key,out items)) { items=new List<int>(); _cells.Add(key,items); }
                items.Add(id);
            });
        }
        internal HashSet<int> Query(Point2 a,Point2 b)
        {
            var result=new HashSet<int>();
            Visit(a,b,key=> { List<int> items; if(_cells.TryGetValue(key,out items)) foreach(int id in items) result.Add(id); });
            return result;
        }
        private static void Visit(Point2 a,Point2 b,Action<long> visit)
        {
            int x0=(int)Math.Floor(Math.Min(a.X,b.X)/CellSize),x1=(int)Math.Floor(Math.Max(a.X,b.X)/CellSize);
            int y0=(int)Math.Floor(Math.Min(a.Y,b.Y)/CellSize),y1=(int)Math.Floor(Math.Max(a.Y,b.Y)/CellSize);
            if((long)(x1-x0+1)*(y1-y0+1)>8192) throw new ArgumentException("Border section spans too many spatial cells.");
            for(int y=y0;y<=y1;y++) for(int x=x0;x<=x1;x++) visit(((long)x<<32)|(uint)y);
        }
    }
}
