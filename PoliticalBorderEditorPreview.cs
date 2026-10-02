using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace AgesOfCalradia.PoliticalBorderEditor
{
    /// <summary>
    /// Owns terrain-draped transient editor primitives. Document editing,
    /// input, persistence, and UI state remain outside this presentation helper.
    /// </summary>
    internal static class PoliticalBorderEditorPreview
    {
        private const float PreviewLift = 0.15f;
        private const float PlanningDashLength = 0.95f;
        private const float PlanningDashGap = 0.70f;
        private const int PlanningDashThickness = 8;
        private const float AnchorRingRadius = 0.72f;
        private const float AnchorDotRadius = 0.18f;

        internal static void RenderPath(
            IList<Vec2> points,
            bool closed,
            uint color,
            bool renderNodes,
            int thickness)
        {
            for (int index = 0; index < points.Count; index++)
            {
                Vec3 current = Point(points[index]);
                if (renderNodes)
                    PoliticalBorderEditorScenePreview.AddDisc(
                        current, 0.7f, color);
                if (index > 0)
                    RenderLineSegment(Point(points[index - 1]), current,
                        color, thickness);
            }
            if (closed && points.Count > 2)
                RenderLineSegment(Point(points[points.Count - 1]), Point(points[0]),
                    color, thickness);
        }

        internal static void RenderStyledPath(
            IList<Vec2> points,
            bool closed,
            uint color,
            string style,
            int thickness)
        {
            if (string.Equals(style, "Dashed", StringComparison.OrdinalIgnoreCase))
            {
                for (int index = 1; index < points.Count; index++)
                    if ((index - 1) % 5 < 3)
                        RenderPath(new[] { points[index - 1], points[index] },
                            false, color, false, thickness);
                return;
            }
            if (string.Equals(style, "Double", StringComparison.OrdinalIgnoreCase))
            {
                for (int index = 1; index < points.Count; index++)
                {
                    Vec2 direction = points[index] - points[index - 1];
                    if (direction.Normalize() < 0.001f) continue;
                    Vec2 normal = new Vec2(-direction.y, direction.x) * 0.35f;
                    RenderPath(new[] { points[index - 1] + normal,
                        points[index] + normal }, false, color, false, thickness);
                    RenderPath(new[] { points[index - 1] - normal,
                        points[index] - normal }, false, color, false, thickness);
                }
                return;
            }
            RenderPath(points, closed, color, true, thickness);
        }

        internal static void RenderDottedPath(
            IList<Vec2> points,
            bool closed,
            uint color)
        {
            if (points == null || points.Count == 0) return;
            if (points.Count == 1)
            {
                RenderAnchor(points[0], color);
                return;
            }
            int edgeCount = closed && points.Count > 2
                ? points.Count : points.Count - 1;
            for (int edge = 0; edge < edgeCount; edge++)
            {
                Vec2 first = points[edge];
                Vec2 second = points[(edge + 1) % points.Count];
                float length = (second - first).Length;
                if (length <= 0.001f) continue;
                Vec2 direction = (second - first) / length;
                float stride = PlanningDashLength + PlanningDashGap;
                for (float offset = 0f; offset < length; offset += stride)
                {
                    float end = Math.Min(length, offset + PlanningDashLength);
                    RenderLineSegment(Point(first + direction * offset),
                        Point(first + direction * end), color,
                        PlanningDashThickness);
                }
            }
        }

        internal static void RenderTerrainRing(
            Vec2 center,
            float radius,
            uint color,
            int steps)
        {
            Vec2 previous = center + new Vec2(radius, 0f);
            for (int step = 1; step <= steps; step++)
            {
                float angle = (float)(Math.PI * 2d * step / steps);
                Vec2 current = center + new Vec2(
                    (float)Math.Cos(angle) * radius,
                    (float)Math.Sin(angle) * radius);
                RenderLineSegment(Point(previous), Point(current), color, 3);
                previous = current;
            }
        }

        internal static void RenderCrosshair(
            Vec2 center,
            float radius,
            uint color,
            int thickness)
        {
            RenderLineSegment(
                Point(center - new Vec2(radius, 0f)),
                Point(center + new Vec2(radius, 0f)),
                color, thickness);
            RenderLineSegment(
                Point(center - new Vec2(0f, radius)),
                Point(center + new Vec2(0f, radius)),
                color, thickness);
        }

        internal static void RenderLineSegment(
            Vec3 first,
            Vec3 second,
            uint color,
            int thickness)
        {
            // Bannerlord's API names the second argument "direction"; passing
            // an absolute endpoint sends long guide lines away from the map.
            PoliticalBorderEditorScenePreview.AddLine(
                first, second, color, Math.Max(0.18f, thickness * 0.10f));
        }

        internal static void RenderBrush(
            IList<Vec2> points,
            float radius,
            uint color)
        {
            foreach (Vec2 point in points)
                PoliticalBorderEditorScenePreview.AddDisc(
                    Point(point), radius, color);
        }

        internal static void RenderMarker(Vec2 point, float radius, uint color)
        {
            PoliticalBorderEditorScenePreview.AddDisc(Point(point), radius, color);
        }

        internal static void RenderAnchor(Vec2 point, uint color)
        {
            RenderTerrainRing(point, AnchorRingRadius, color, 16);
            PoliticalBorderEditorScenePreview.AddDisc(
                Point(point), AnchorDotRadius, color);
        }

        internal static Vec3 Point(Vec2 point)
        {
            float height = 0f;
            Campaign campaign = Campaign.Current;
            if (campaign != null && campaign.MapSceneWrapper != null)
            {
                CampaignVec2 campaignPoint = new CampaignVec2(point, false);
                campaign.MapSceneWrapper.GetHeightAtPoint(campaignPoint, ref height);
            }
            return new Vec3(point.x, point.y, height + PreviewLift, -1f);
        }
    }
}
