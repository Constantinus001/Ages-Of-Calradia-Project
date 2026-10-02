using System;

namespace AgesOfCalradia.PoliticalRenderDiagnostics
{
    // Reports a submitted split vertex's Z difference from a coarse edge at the
    // same XY. It does not infer adjacency, change vertices, or repair a mesh.
    internal static class FillEdgeContinuityProbe
    {
        internal static bool TryMeasureInteriorJoin(Point3 first, Point3 second,
            Point3 splitPoint, float xyTolerance, out float heightDifference)
        {
            heightDifference = float.NaN;
            if (!RenderProbeMath.IsFinite(xyTolerance) || xyTolerance < 0)
                throw new ArgumentOutOfRangeException("xyTolerance");
            if (!Finite(first) || !Finite(second) || !Finite(splitPoint)) return false;
            double dx = (double)second.X - first.X, dy = (double)second.Y - first.Y;
            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared == 0) return false;
            double px = (double)splitPoint.X - first.X, py = (double)splitPoint.Y - first.Y;
            double parameter = (px * dx + py * dy) / lengthSquared;
            if (parameter <= 0 || parameter >= 1) return false;
            double residualX = px - parameter * dx, residualY = py - parameter * dy;
            if (residualX * residualX + residualY * residualY > (double)xyTolerance * xyTolerance)
                return false;
            double coarseHeight = first.Z + parameter * ((double)second.Z - first.Z);
            heightDifference = (float)(splitPoint.Z - coarseHeight);
            return RenderProbeMath.IsFinite(heightDifference);
        }
        private static bool Finite(Point3 value)
        { return RenderProbeMath.IsFinite(value.X) && RenderProbeMath.IsFinite(value.Y)
            && RenderProbeMath.IsFinite(value.Z); }
    }
}
