using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace AgesOfCalradia.PoliticalBorderEditor
{
    /// <summary>Deterministic curve sampling for editor previews and commits.</summary>
    internal static class PoliticalBorderEditorPathMath
    {
        internal static List<Vec2> BuildCurve(IList<Vec2> controls)
        {
            if (controls.Count < 3) return new List<Vec2>(controls);
            List<Vec2> result = new List<Vec2>();
            const int subdivisions = 8;
            for (int index = 0; index < controls.Count - 1; index++)
            {
                Vec2 p0 = controls[Math.Max(0, index - 1)];
                Vec2 p1 = controls[index];
                Vec2 p2 = controls[index + 1];
                Vec2 p3 = controls[Math.Min(controls.Count - 1, index + 2)];
                for (int step = 0; step < subdivisions; step++)
                {
                    float t = step / (float)subdivisions;
                    float t2 = t * t;
                    float t3 = t2 * t;
                    result.Add(0.5f * ((2f * p1)
                        + ((-p0 + p2) * t)
                        + (((2f * p0) - (5f * p1) + (4f * p2) - p3) * t2)
                        + ((-p0 + (3f * p1) - (3f * p2) + p3) * t3)));
                }
            }
            result.Add(controls[controls.Count - 1]);
            return result;
        }
    }
}
