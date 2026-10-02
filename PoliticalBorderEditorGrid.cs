using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace AgesOfCalradia.PoliticalBorderEditor
{
    /// <summary>
    /// Terrain-draped planning grid shared by the border and fill tools. The
    /// established editor scale treats one map unit as one planning mile, so a
    /// ten-by-ten cell represents 100 square miles. Rendering is bounded and
    /// diagnostics are emitted only when bounds are prepared.
    /// </summary>
    internal static class PoliticalBorderEditorGrid
    {
        internal const float CellSideMiles = 10f;
        internal const float CellAreaSquareMiles = 100f;
        private const float GridLift = 0.05f;
        private const int GridRenderOrder = 90;
        private const int MaximumRenderedSegments = 12000;
        private const uint GridColor = 0xAA165A32u;
        private static bool _prepared;
        private static float _minimumX, _minimumY, _maximumX, _maximumY;
        private static GameEntity _entity;
        private static Scene _entityScene;
        private static bool _sceneBuildFailed;

        internal static void Render()
        {
            if (_sceneBuildFailed) return;
            try
            {
            if (!_prepared && !TryPrepareBounds()) return;
            Scene scene = PoliticalBorderEditorScenePreview.CurrentScene;
            if (scene == null) return;
            if (_entity != null && ReferenceEquals(_entityScene, scene)) return;
            ClearSceneEntity();
            MeshBuilder builder = new MeshBuilder();
            int corners = 0;
            int rendered = 0;
            for (float x = _minimumX; x <= _maximumX && rendered < MaximumRenderedSegments;
                x += CellSideMiles)
            for (float y = _minimumY; y < _maximumY && rendered < MaximumRenderedSegments;
                y += CellSideMiles)
            {
                RenderSegment(builder, ref corners, new Vec2(x, y),
                    new Vec2(x, Math.Min(_maximumY, y + CellSideMiles)));
                rendered++;
            }
            for (float y = _minimumY; y <= _maximumY && rendered < MaximumRenderedSegments;
                y += CellSideMiles)
            for (float x = _minimumX; x < _maximumX && rendered < MaximumRenderedSegments;
                x += CellSideMiles)
            {
                RenderSegment(builder, ref corners, new Vec2(x, y),
                    new Vec2(Math.Min(_maximumX, x + CellSideMiles), y));
                rendered++;
            }
            _entity = PoliticalBorderEditorScenePreview.CreatePersistentEntity(
                builder, corners, GridRenderOrder);
            _entityScene = scene;
            PoliticalBorderEditorDiagnostics.Info(
                "Production planning-grid mesh published: segments=" + rendered
                + "; corners=" + corners + "; renderOrder="
                + GridRenderOrder + ".");
            }
            catch (Exception exception)
            {
                ClearSceneEntity();
                _sceneBuildFailed = true;
                PoliticalBorderEditorDiagnostics.Error(
                    "Production planning-grid mesh failed; other editor visuals remain active.",
                    exception);
            }
        }

        internal static void SetVisible(bool visible)
        {
            if (!visible) ClearSceneEntity();
            else _sceneBuildFailed = false;
        }

        internal static void ClearSceneEntity()
        {
            if (_entity != null) _entity.Remove(0);
            _entity = null;
            _entityScene = null;
        }

        private static bool TryPrepareBounds()
        {
            if (Campaign.Current == null || Settlement.All == null) return false;
            bool found = false;
            float minimumX = float.MaxValue, minimumY = float.MaxValue;
            float maximumX = float.MinValue, maximumY = float.MinValue;
            foreach (Settlement settlement in Settlement.All)
            {
                if (settlement == null) continue;
                Vec2 point = new Vec2(settlement.Position.X, settlement.Position.Y);
                minimumX = Math.Min(minimumX, point.x);
                minimumY = Math.Min(minimumY, point.y);
                maximumX = Math.Max(maximumX, point.x);
                maximumY = Math.Max(maximumY, point.y);
                found = true;
            }
            if (!found) return false;
            const float margin = 30f;
            _minimumX = (float)Math.Floor((minimumX - margin) / CellSideMiles) * CellSideMiles;
            _minimumY = (float)Math.Floor((minimumY - margin) / CellSideMiles) * CellSideMiles;
            _maximumX = (float)Math.Ceiling((maximumX + margin) / CellSideMiles) * CellSideMiles;
            _maximumY = (float)Math.Ceiling((maximumY + margin) / CellSideMiles) * CellSideMiles;
            _prepared = true;
            PoliticalBorderEditorDiagnostics.Info(
                "100-square-mile editor grid prepared: cellSide=10; bounds="
                + _minimumX + "," + _minimumY + ".." + _maximumX + "," + _maximumY + ".");
            return true;
        }

        private static void RenderSegment(
            MeshBuilder builder,
            ref int corners,
            Vec2 first,
            Vec2 second)
        {
            PoliticalBorderEditorScenePreview.AddPersistentLine(
                builder, ref corners, TerrainPoint(first), TerrainPoint(second),
                GridColor, 0.6f);
        }

        private static Vec3 TerrainPoint(Vec2 point)
        {
            float height = 0f;
            Campaign campaign = Campaign.Current;
            if (campaign != null && campaign.MapSceneWrapper != null)
            {
                CampaignVec2 campaignPoint = new CampaignVec2(point, false);
                campaign.MapSceneWrapper.GetHeightAtPoint(campaignPoint, ref height);
            }
            return new Vec3(point.x, point.y, height + GridLift, -1f);
        }
    }
}
