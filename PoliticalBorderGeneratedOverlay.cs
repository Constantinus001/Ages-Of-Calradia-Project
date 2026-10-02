using System;
using System.Collections.Generic;
using AgesOfCalradia.PoliticalBorderOverrides;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace AgesOfCalradia.PoliticalBorderEditor
{
    internal static class PoliticalBorderGeneratedOverlay
    {
        private const uint GeneratedPlanColor = 0xFFFFA040u;
        private const int RenderOrder = 115;
        private const int MaximumCornersPerEntity = 48000;
        private const float SelectionDistance = 4f;
        private static List<GeneratedBorderChain> _chains;
        private static readonly List<GameEntity> Entities = new List<GameEntity>();
        private static Scene _scene;
        private static bool _visible;
        private static bool _loadFailed;

        internal static string StatusText { get { return _visible ? "GENERATED: EDITING" : "GENERATED: HIDDEN"; } }
        internal static void BeginSession() { _loadFailed = false; _chains = null; ClearEntities(); }
        internal static void SetVisible(bool visible) { _visible = visible; if (!visible) ClearEntities(); }
        internal static void Render()
        {
            if (!_visible || _loadFailed) return;
            EnsureLoaded();
            Scene scene = PoliticalBorderEditorScenePreview.CurrentScene;
            if (scene == null || _chains == null || _chains.Count == 0) return;
            if (Entities.Count > 0 && ReferenceEquals(_scene, scene)) return;
            ClearEntities();
            _scene = scene;
            MeshBuilder builder = new MeshBuilder(); int corners = 0; int segments = 0;
            foreach (GeneratedBorderChain chain in _chains)
            {
                int edgeCount = chain.Closed ? chain.Points.Count : chain.Points.Count - 1;
                for (int index = 0; index < edgeCount; index++)
                {
                    if (corners + 4 > MaximumCornersPerEntity)
                    { Publish(builder, corners); builder = new MeshBuilder(); corners = 0; }
                    PoliticalBorderEditorScenePreview.AddPersistentLine(builder, ref corners,
                        PoliticalBorderEditorPreview.Point(chain.Points[index]),
                        PoliticalBorderEditorPreview.Point(chain.Points[(index + 1) % chain.Points.Count]),
                        GeneratedPlanColor, 0.42f);
                    segments++;
                }
            }
            Publish(builder, corners);
            PoliticalBorderEditorDiagnostics.Info("Generated plan overlay published: chains=" + _chains.Count + "; segments=" + segments + "; entities=" + Entities.Count + ".");
        }
        internal static bool TrySelect(Vec2 cursor, out GeneratedBorderChain selected)
        {
            selected = null; EnsureLoaded();
            if (_chains == null) return false;
            float best = SelectionDistance * SelectionDistance;
            foreach (GeneratedBorderChain chain in _chains)
            {
                int edges = chain.Closed ? chain.Points.Count : chain.Points.Count - 1;
                for (int i = 0; i < edges; i++)
                {
                    float distance = DistanceSquared(cursor, chain.Points[i], chain.Points[(i + 1) % chain.Points.Count]);
                    if (distance >= best) continue; best = distance; selected = chain;
                }
            }
            return selected != null;
        }
        internal static void ClearEntities()
        { foreach (GameEntity entity in Entities) if (entity != null) entity.Remove(0); Entities.Clear(); _scene = null; }
        private static void EnsureLoaded()
        {
            if (_chains != null || _loadFailed) return;
            if (!PoliticalBorderGeneratedSnapshot.Exists) return;
            try { _chains = PoliticalBorderGeneratedSnapshot.Load(); }
            catch (Exception exception) { _loadFailed = true; PoliticalBorderEditorDiagnostics.Error("Generated-border snapshot could not be loaded; ordinary planning remains available.", exception); }
        }
        private static void Publish(MeshBuilder builder, int corners)
        { if (corners > 0) Entities.Add(PoliticalBorderEditorScenePreview.CreatePersistentEntity(builder, corners, RenderOrder)); }
        private static float DistanceSquared(Vec2 point, Vec2 first, Vec2 second)
        { Vec2 delta = second - first; float d = delta.LengthSquared; float t = d < 0.000001f ? 0f : Math.Max(0f, Math.Min(1f, Vec2.DotProduct(point - first, delta) / d)); return (point - (first + delta * t)).LengthSquared; }
    }
}
