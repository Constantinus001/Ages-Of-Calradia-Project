using System;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace AgesOfCalradia.PoliticalBorderEditor
{
    /// <summary>
    /// Owns the small dynamic production mesh used for crosshairs and staged
    /// plans. Shipping Client debug primitives are not a rendering contract,
    /// so editor-critical marks use a late no-depth scene entity instead.
    /// Scene/reflection failure disables only the visual preview and is logged.
    /// </summary>
    internal static class PoliticalBorderEditorScenePreview
    {
        // The engine accepts an integer here, but very high values are not a
        // reliable Shipping Client render bucket. Keep the live plan safely
        // above the political fill (100) without using the 255 boundary.
        private const int RenderOrder = 120;
        private const string MaterialName = "vertex_color_mat";
        private const int DiscSteps = 8;
        // Bannerlord mesh face indices are safest below the 16-bit vertex ceiling.
        // Stop accepting preview primitives before one dynamic/persistent mesh can
        // cross that native boundary; the next frame can publish a fresh mesh.
        private const int MaximumCorners = 60000;
        private const MaterialFlags OverlayFlags =
            MaterialFlags.NoDepthTest
            | MaterialFlags.NoModifyDepthBuffer
            | MaterialFlags.DontDrawToDepthRenderTarget
            | MaterialFlags.DontCastShadow
            | MaterialFlags.RequiresForwardRendering
            | MaterialFlags.RenderOrderPlus_7;

        private static MeshBuilder _builder;
        private static Scene _scene;
        private static GameEntity _entity;
        private static GameEntity _retiredEntity;
        private static Material _material;
        private static int _cornerCount;
        private static bool _disabledForSession;
        private static bool _loggedFirstDynamicMesh;
        private static bool _loggedPlanningGeometry;
        private static bool _loggedSceneAuthority;

        internal static Scene CurrentScene { get { return _scene; } }

        internal static void BeginSession()
        {
            _disabledForSession = false;
            _loggedFirstDynamicMesh = false;
            _loggedPlanningGeometry = false;
            _loggedSceneAuthority = false;
        }

        internal static void BeginFrame(object mapScreen)
        {
            _builder = null;
            _cornerCount = 0;
            if (_disabledForSession) return;
            try
            {
                Scene scene = ResolveScene(mapScreen);
                if (scene == null)
                    throw new InvalidOperationException(
                        "The campaign scene is unavailable for editor previews.");
                if (_scene != null && !ReferenceEquals(_scene, scene)) ClearEntity();
                _scene = scene;
                _builder = new MeshBuilder();
            }
            catch (Exception exception)
            {
                Disable(exception);
            }
        }

        internal static void AddLine(Vec3 first, Vec3 second, uint color, float width)
        {
            AddLine(_builder, ref _cornerCount, first, second, color, width);
        }

        internal static void AddPersistentLine(
            MeshBuilder builder,
            ref int cornerCount,
            Vec3 first,
            Vec3 second,
            uint color,
            float width)
        {
            AddLine(builder, ref cornerCount, first, second, color, width);
        }

        private static void AddLine(
            MeshBuilder builder,
            ref int cornerCount,
            Vec3 first,
            Vec3 second,
            uint color,
            float width)
        {
            if (builder == null || cornerCount + 4 > MaximumCorners) return;
            Vec2 direction = second.AsVec2 - first.AsVec2;
            if (direction.Normalize() <= 0.0001f) return;
            Vec2 normal = new Vec2(-direction.y, direction.x) * (width * 0.5f);
            Vec3 firstLeft = first + new Vec3(normal.x, normal.y, 0f, -1f);
            Vec3 firstRight = first - new Vec3(normal.x, normal.y, 0f, -1f);
            Vec3 secondLeft = second + new Vec3(normal.x, normal.y, 0f, -1f);
            Vec3 secondRight = second - new Vec3(normal.x, normal.y, 0f, -1f);
            Vec3 up = Vec3.Up;
            Vec2 uv = Vec2.Zero;
            int a = builder.AddFaceCorner(firstLeft, up, uv, color);
            int b = builder.AddFaceCorner(firstRight, up, uv, color);
            int c = builder.AddFaceCorner(secondRight, up, uv, color);
            int d = builder.AddFaceCorner(secondLeft, up, uv, color);
            builder.AddFace(a, b, c);
            builder.AddFace(a, c, d);
            cornerCount += 4;
        }

        internal static void AddDisc(Vec3 center, float radius, uint color)
        {
            if (_builder == null || radius <= 0f
                || _cornerCount + DiscSteps + 1 > MaximumCorners) return;
            Vec3 up = Vec3.Up;
            Vec2 uv = Vec2.Zero;
            int centerIndex = _builder.AddFaceCorner(center, up, uv, color);
            int first = -1;
            int previous = -1;
            for (int step = 0; step < DiscSteps; step++)
            {
                float angle = (float)(Math.PI * 2d * step / DiscSteps);
                Vec3 point = center + new Vec3(
                    (float)Math.Cos(angle) * radius,
                    (float)Math.Sin(angle) * radius, 0f, -1f);
                int current = _builder.AddFaceCorner(point, up, uv, color);
                if (first < 0) first = current;
                if (previous >= 0) _builder.AddFace(centerIndex, previous, current);
                previous = current;
            }
            if (previous >= 0 && first >= 0)
                _builder.AddFace(centerIndex, previous, first);
            _cornerCount += DiscSteps + 1;
        }

        internal static void EndFrame()
        {
            if (_builder == null) return;
            try
            {
                // MapScreen.OnFrameTick can run before the scene presents its
                // render list. Removing last tick's entity here can therefore
                // make a one-frame dynamic mesh live for zero rendered frames.
                // Retain the previous publication for one complete additional
                // frame; the current and previous previews may briefly overlap,
                // but crosshairs and plans can no longer disappear between the
                // map tick and Bannerlord's renderer.
                RemoveEntity(ref _retiredEntity);
                _retiredEntity = _entity;
                _entity = null;
                if (_cornerCount > 0)
                {
                    Mesh mesh = _builder.Finalize();
                    if (mesh == null)
                        throw new InvalidOperationException(
                            "Bannerlord could not finalize the editor preview mesh.");
                    mesh.SetMaterial(GetMaterial());
                    mesh.SetMeshRenderOrder(RenderOrder);
                    mesh.RecomputeBoundingBox();
                    _entity = GameEntity.CreateEmpty(_scene, false, true, true);
                    if (_entity == null)
                        throw new InvalidOperationException(
                            "Bannerlord could not create the editor preview entity.");
                    _entity.SetGlobalFrame(MatrixFrame.Identity, true);
                    _entity.AddMesh(mesh, true);
                    _entity.SetForceDecalsToRender(false);
                    _entity.SetEnforcedMaximumLodLevel(0);
                    _entity.SetVisibilityExcludeParents(true);
                    _entity.SetReadyToRender(true);
                    _entity.SetAlpha(1f);
                    _entity.UpdateVisibilityMask();
                }
                if (!_loggedFirstDynamicMesh)
                {
                    _loggedFirstDynamicMesh = true;
                    PoliticalBorderEditorDiagnostics.Info(
                        "Production dynamic planning mesh published: corners="
                        + _cornerCount + "; renderOrder=" + RenderOrder + ".");
                }
                if (!_loggedPlanningGeometry && _cornerCount > 17)
                {
                    _loggedPlanningGeometry = true;
                    PoliticalBorderEditorDiagnostics.Info(
                        "Live border/anchor geometry published above the cursor baseline: corners="
                        + _cornerCount + ".");
                }
            }
            catch (Exception exception)
            {
                Disable(exception);
            }
            finally
            {
                _builder = null;
            }
        }

        internal static void Clear()
        {
            ClearEntity();
            _builder = null;
            _scene = null;
            _cornerCount = 0;
        }

        internal static GameEntity CreatePersistentEntity(
            MeshBuilder builder,
            int cornerCount,
            int renderOrder)
        {
            if (_scene == null || builder == null || cornerCount == 0) return null;
            Mesh mesh = builder.Finalize();
            if (mesh == null)
                throw new InvalidOperationException(
                    "Bannerlord could not finalize persistent editor guide geometry.");
            mesh.SetMaterial(GetMaterial());
            mesh.SetMeshRenderOrder(renderOrder);
            mesh.RecomputeBoundingBox();
            GameEntity entity = GameEntity.CreateEmpty(_scene, false, true, true);
            if (entity == null)
                throw new InvalidOperationException(
                    "Bannerlord could not create a persistent editor guide entity.");
            entity.SetGlobalFrame(MatrixFrame.Identity, true);
            entity.AddMesh(mesh, true);
            entity.SetForceDecalsToRender(false);
            entity.SetEnforcedMaximumLodLevel(0);
            entity.SetVisibilityExcludeParents(true);
            entity.SetReadyToRender(true);
            entity.SetAlpha(1f);
            entity.UpdateVisibilityMask();
            return entity;
        }

        private static Material GetMaterial()
        {
            if (_material != null) return _material;
            Material source = Material.GetFromResource(MaterialName);
            if (source == null)
                throw new InvalidOperationException(
                    "The editor preview source material is unavailable.");
            _material = source.CreateCopy();
            if (_material == null)
                throw new InvalidOperationException(
                    "Bannerlord could not copy the editor preview material.");
            _material.Flags = _material.Flags | OverlayFlags;
            return _material;
        }

        private static Scene ResolveScene(object mapScreen)
        {
            Campaign campaign = Campaign.Current;
            object wrapper = campaign == null ? null : campaign.MapSceneWrapper;
            Scene mapScene = GetProperty(wrapper, "Scene") as Scene;
            if (mapScene != null)
            {
                if (!_loggedSceneAuthority)
                {
                    _loggedSceneAuthority = true;
                    PoliticalBorderEditorDiagnostics.Info(
                        "Editor preview scene resolved from Campaign.MapSceneWrapper.Scene.");
                }
                return mapScene;
            }
            object view = GetProperty(mapScreen, "MapCameraView");
            Camera camera = view == null ? null : GetProperty(view, "Camera") as Camera;
            Scene cameraScene = camera != null && camera.Entity != null
                ? camera.Entity.Scene : null;
            if (cameraScene != null && !_loggedSceneAuthority)
            {
                _loggedSceneAuthority = true;
                PoliticalBorderEditorDiagnostics.Info(
                    "Editor preview scene used the camera-entity fallback.");
            }
            return cameraScene;
        }

        private static object GetProperty(object instance, string name)
        {
            if (instance == null) return null;
            for (Type type = instance.GetType(); type != null; type = type.BaseType)
            {
                PropertyInfo property = type.GetProperty(name,
                    BindingFlags.Instance | BindingFlags.Public
                    | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (property != null) return property.GetValue(instance, null);
            }
            return null;
        }

        private static void ClearEntity()
        {
            RemoveEntity(ref _entity);
            RemoveEntity(ref _retiredEntity);
        }

        private static void RemoveEntity(ref GameEntity entity)
        {
            if (entity == null) return;
            entity.Remove(0);
            entity = null;
        }

        private static void Disable(Exception exception)
        {
            Clear();
            _disabledForSession = true;
            PoliticalBorderEditorDiagnostics.Error(
                "Production editor preview mesh failed; planning data and controls remain active.",
                exception);
        }
    }
}
