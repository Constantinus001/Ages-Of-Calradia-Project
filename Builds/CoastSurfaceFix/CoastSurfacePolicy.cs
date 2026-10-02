using System;

namespace AgesOfCalradia.CoastSurfaceFix
{
    internal static class CoastSurfacePolicy
    {
        // Keep the approved +5 mesh placement and -4.65 close-zoom frame.
        // Correct the surface source, not the global height or zoom behavior.
        internal const float MeshOffset = 5f;
        internal static bool IsCoast(bool leftLand, bool rightLand, string nonLandTerrain)
        {
            return leftLand != rightLand && nonLandTerrain == "CoastalSea";
        }
        internal static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        internal static bool Project(bool validSurface, float surface, bool confirmedWater, float water, out float z)
        {
            z = 0;
            if (!validSurface || !Finite(surface) || (confirmedWater && !Finite(water))) return false;
            z = (confirmedWater ? Math.Max(surface, water) : surface) + MeshOffset;
            return Finite(z);
        }
    }
}
