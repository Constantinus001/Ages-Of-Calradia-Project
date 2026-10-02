using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace AgesOfCalradia.CoastSurfaceFix
{
    internal static class CoastCapClipper
    {
        // Each approved cap has radius .8. Incident strips have half-width .8
        // and extend farther than that radius. Inside the cap, dot(d,p)>=0 is
        // therefore exactly the portion already covered by an incident strip.
        // Keep only the intersection of all uncovered half-planes.
        internal static List<Vec3> Clip(Vec3 center, Vec3 first, Vec3 second, IList<Vec2> outward)
        {
            var polygon = new List<Vec3> { center, first, second };
            foreach (Vec2 direction in outward)
            {
                if (polygon.Count < 3) break;
                var result = new List<Vec3>();
                Vec3 previous = polygon[polygon.Count - 1];
                double previousSide = Side(center, previous, direction);
                foreach (Vec3 current in polygon)
                {
                    double currentSide = Side(center, current, direction);
                    bool previousIn = previousSide <= 0, currentIn = currentSide <= 0;
                    if (previousIn != currentIn)
                    {
                        double t = previousSide / (previousSide - currentSide);
                        result.Add(new Vec3((float)(previous.x + ((double)current.x - previous.x) * t),
                            (float)(previous.y + ((double)current.y - previous.y) * t),
                            (float)(previous.z + ((double)current.z - previous.z) * t)));
                    }
                    if (currentIn) result.Add(current);
                    previous = current; previousSide = currentSide;
                }
                polygon = result;
            }
            return polygon;
        }
        private static double Side(Vec3 center, Vec3 point, Vec2 direction)
        {
            return ((double)point.x - center.x) * direction.x + ((double)point.y - center.y) * direction.y;
        }
        internal static double AreaTwice(Vec3 a, Vec3 b, Vec3 c)
        {
            return ((double)b.x-a.x)*((double)c.y-a.y)-((double)b.y-a.y)*((double)c.x-a.x);
        }
    }
}
