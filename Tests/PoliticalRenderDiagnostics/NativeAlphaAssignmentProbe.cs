using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Engine;

namespace AgesOfCalradia.PoliticalRenderDiagnostics
{
    // Inference from the approved original control flow, not a native alpha getter.
    // Other Harmony owners are logged by target audit and may affect this inference.
    internal static class NativeAlphaAssignmentProbe
    {
        internal sealed class Snapshot
        {
            internal float Alpha;
            internal bool Ready;
            internal int EntityCount;
        }
        private static FieldInfo _alphaField, _readyField, _entitiesField;
        private static object _behavior;
        private static float _inferredApplied = float.NaN;
        private static int _entries;
        private static bool _fullRequestedRecorded;
        private static string _previousKey;

        internal static Snapshot Before(object behavior)
        {
            try
            {
                if (_alphaField == null)
                {
                    Type type = behavior.GetType();
                    _alphaField = AccessTools.Field(type, "_politicalLayerAlpha");
                    _readyField = AccessTools.Field(type, "_politicalEntitiesReady");
                    _entitiesField = AccessTools.Field(type, "_territoryFillEntities");
                }
                var entities = _entitiesField.GetValue(behavior) as List<GameEntity>;
                return new Snapshot { Alpha = (float)_alphaField.GetValue(behavior), Ready = (bool)_readyField.GetValue(behavior), EntityCount = entities == null ? 0 : entities.Count };
            }
            catch (Exception ex) { NativeRenderProbe.RecordOptional("alphaInference=unknown;reflectionError=" + ex); return null; }
        }
        internal static void After(object behavior, bool forceAlpha, bool originalRan, Snapshot before)
        {
            if (before == null) return;
            if (!ReferenceEquals(_behavior, behavior))
            {
                _behavior = behavior; _inferredApplied = float.NaN; _previousKey = null;
            }
            bool shouldRender = before.Alpha > 0.001f;
            bool readinessChanged = shouldRender != before.Ready;
            bool inferredAssignment = originalRan && (forceAlpha || readinessChanged) && shouldRender && before.EntityCount > 0;
            if (inferredAssignment) _inferredApplied = before.Alpha;
            bool full = before.Alpha >= 1f;
            string key = Bucket(before.Alpha) + ":" + Bucket(_inferredApplied) + ":" + inferredAssignment + ":" + originalRan + ":published=" + (before.EntityCount > 0);
            bool reserveFull = full && before.EntityCount > 0 && !_fullRequestedRecorded;
            if ((_entries >= 255 && !reserveFull) || (_previousKey == key && !reserveFull)) return;
            _previousKey = key; _entries++;
            if (reserveFull) _fullRequestedRecorded = true;
            NativeRenderProbe.RecordOptional("alphaInference;requested=" + F(before.Alpha) + ";lastInferredOriginalAssignment=" + F(_inferredApplied)
                + ";assignmentThisCall=" + inferredAssignment + ";runOriginal=" + originalRan + ";forceAlpha=" + forceAlpha
                + ";readyBefore=" + before.Ready + ";entityCountBefore=" + before.EntityCount
                + ";NOT native getter; assumes approved original branch and eligible entities; other patch owners may change assignment");
        }
        private static string Bucket(float v) { return RenderProbeMath.IsFinite(v) ? Math.Floor(v * 100d).ToString(CultureInfo.InvariantCulture) : "unknown"; }
        private static string F(float v) { return RenderProbeMath.IsFinite(v) ? v.ToString("R", CultureInfo.InvariantCulture) : "unknown"; }
    }
}
