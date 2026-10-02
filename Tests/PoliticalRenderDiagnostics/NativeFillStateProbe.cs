using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace AgesOfCalradia.PoliticalRenderDiagnostics
{
    // Optional native read boundary. Missing state is reported as unknown and
    // never invalidates the separate geometry probe or changes a game object.
    internal static class NativeFillStateProbe
    {
        private static FieldInfo _entitiesField;
        internal static bool IsPublished(object behavior)
        {
            // Managed list observation only; do not issue native reads every frame.
            if (_entitiesField == null) _entitiesField = AccessTools.Field(behavior.GetType(), "_territoryFillEntities");
            var entities = _entitiesField == null ? null : _entitiesField.GetValue(behavior) as List<GameEntity>;
            return entities != null && entities.Count > 0;
        }
        internal static string Capture(object behavior)
        {
            try
            {
                var field = AccessTools.Field(behavior.GetType(), "_territoryFillEntities");
                if (field == null) return "actualFill=unknown:field unavailable";
                var entities = field.GetValue(behavior) as List<GameEntity>;
                if (entities == null || entities.Count == 0) return "actualFill=unknown:not published";
                GameEntity entity = entities[0];
                if (entity == null) return "actualFill=unknown:first entity null";
                MatrixFrame frame = entity.GetGlobalFrame();
                Mesh mesh = entity.GetFirstMesh();
                Material material = mesh.GetMaterial();
                return "actualFillEntities=" + entities.Count + ";firstFillOrigin=" + V(frame.origin)
                    + ";firstFillRotationS=" + V(frame.rotation.s) + ";firstFillRotationF=" + V(frame.rotation.f)
                    + ";firstFillRotationU=" + V(frame.rotation.u)
                    + ";actualFillAlpha=unknown:no GetAlpha in installed API"
                    + ";actualFillMaterial=" + material.Name + ";actualFillFlags=" + material.Flags
                    + ";actualFillShader=" + material.GetShader().Name + ";actualFillShaderFlags=" + material.GetShaderFlags()
                    + ";actualFillAlphaBlend=" + material.GetAlphaBlendMode() + ";actualFillAlphaTest=" + F(material.GetAlphaTestValue())
                    + ";actualFillSunLight=" + material.UsingSunLight + ";actualFillDynamicLight=" + material.UsingDynamicLight
                    + ";actualFillSunShadow=" + material.IsSunShadowReceiver + ";actualFillDynamicShadow=" + material.IsDynamicShadowReceiver;
            }
            catch (Exception ex) { return "actualFill=unknown;optionalNativeReadError=" + ex; }
        }
        internal static string CaptureScene(Scene scene)
        {
            try
            {
                return "sceneRainDensity=" + F(scene.GetRainDensity()) + ";sceneFloraInstances=" + scene.GetFloraInstanceCount()
                    + ";floraCount does not identify visible objects at an artifact pixel";
            }
            catch (Exception ex) { return "sceneWeatherFlora=unknown;optionalNativeReadError=" + ex; }
        }
        private static string F(float value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        private static string V(Vec3 value) { return F(value.x) + "/" + F(value.y) + "/" + F(value.z); }
    }
}
