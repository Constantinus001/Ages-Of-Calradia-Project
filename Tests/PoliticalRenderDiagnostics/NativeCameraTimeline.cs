using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace AgesOfCalradia.PoliticalRenderDiagnostics
{
    // Post-OnMapFrame passive snapshots. Coordinates support manual correlation
    // with video; they do not identify a stripe or its pixels automatically.
    internal sealed class NativeCameraTimeline : IDisposable
    {
        private readonly StreamWriter _writer;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _nextMilliseconds;
        private int _records;
        private bool _failed;
        private FieldInfo _alphaField;
        internal NativeCameraTimeline(string path)
        {
            _writer = new StreamWriter(path) { AutoFlush = true };
            _writer.WriteLine("utc,elapsedSeconds,campaignHours,sceneTimeOfDay,sceneIsDayTime,rainDensity,snowDensity,fog,sunX,sunY,sunZ,cameraX,cameraY,cameraZ,rotationSX,rotationSY,rotationSZ,rotationFX,rotationFY,rotationFZ,rotationUX,rotationUY,rotationUZ,fovVertical,fovHorizontal,aspectRatio,altitudeRequestedAlpha,bookkeepingAlpha");
        }
        internal void Capture(object behavior, Camera camera, FieldInfo sceneField)
        {
            if (_failed || _records >= 300 || _clock.Elapsed.TotalMilliseconds < _nextMilliseconds) return;
            _nextMilliseconds = _clock.Elapsed.TotalMilliseconds + 1000;
            try
            {
                if (camera == null) return;
                Scene scene = (Scene)sceneField.GetValue(null);
                MatrixFrame frame = camera.Frame;
                if (_alphaField == null) _alphaField = AccessTools.Field(behavior.GetType(), "_politicalLayerAlpha");
                string bookkeepingAlpha = _alphaField == null ? "unknown" : F((float)_alphaField.GetValue(behavior));
                float requested = Math.Max(0f, Math.Min(1f, (frame.origin.z - 580f) / 160f));
                _writer.WriteLine(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + ","
                    + _clock.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture) + ","
                    + CampaignTime.Now.ToHours.ToString("R", CultureInfo.InvariantCulture) + ","
                    + SceneValues(scene) + "," + V(frame.origin) + "," + V(frame.rotation.s) + "," + V(frame.rotation.f) + "," + V(frame.rotation.u)
                    + "," + F(camera.GetFovVertical()) + "," + F(camera.GetFovHorizontal()) + "," + F(camera.GetAspectRatio())
                    + "," + F(requested) + "," + bookkeepingAlpha);
                _records++;
                if (_records == 300) NativeRenderProbe.RecordOptional("cameraTimeline complete;records=300;limit reached;no additional camera native reads");
            }
            catch (Exception ex)
            {
                _failed = true;
                NativeRenderProbe.RecordOptional("cameraTimeline disabled;optional native/file/reflection read failed; geometry probes unaffected; " + ex);
            }
        }
        private static string SceneValues(Scene scene)
        {
            if (scene == null) return "unknown,unknown,unknown,unknown,unknown,unknown,unknown,unknown";
            Vec3 sun = scene.GetSunDirection();
            return F(scene.TimeOfDay) + "," + scene.IsDayTime + "," + F(scene.GetRainDensity()) + "," + F(scene.GetSnowDensity()) + "," + F(scene.GetFog()) + "," + V(sun);
        }
        private static string F(float value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        private static string V(Vec3 value) { return F(value.x) + "," + F(value.y) + "," + F(value.z); }
        public void Dispose() { _writer.Dispose(); }
    }
}
