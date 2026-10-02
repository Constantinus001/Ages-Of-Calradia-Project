using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using Path = System.IO.Path;

namespace AgesOfCalradia.PoliticalRenderDiagnostics
{
    internal static class NativeRenderProbe
    {
        internal const string ApprovedHash = "560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E";
        private const string Owner = "aoc.tests.political-render-probe.v1";
        private const int SegmentLimit = 8192;
        private const int FillTriangleLimit = 4096;
        private const int FillTriangleStride = 128;
        private sealed class Segment
        {
            internal int Id;
            internal Vec2 First, Second;
            internal bool Accepted, SupportRejected, HeightRejected;
            internal bool IsFill;
            internal Vec3 GeometricNormal;
            internal readonly List<Vec3> Samples = new List<Vec3>();
        }
        private static readonly Queue<Segment> Pending = new Queue<Segment>();
        private static readonly HashSet<string> AlphaBuckets = new HashSet<string>();
        private static readonly HashSet<int> MaterialOrders = new HashSet<int>();
        private static Harmony _harmony;
        private static StreamWriter _log, _csv;
        private static NativeFillGeometryExport _fillExport;
        private static NativeCameraTimeline _cameraTimeline;
        private static CoastlineEvidenceProbe _coastlineEvidence;
        private static bool _enabled, _complete, _inBuilder;
        private static int _fillCount;
        private static readonly RenderThreadGate ThreadGate = new RenderThreadGate();
        private static bool _loggedBeforeTick;
        private static long _fillSeen;
        private static int _count, _sampleIndex, _written, _fillWritten, _frontierWritten;
        private static Segment _active, _sampling;
        private static Scene _scene;
        private static object _builder;
        private static PropertyInfo _isComplete;
        private static FieldInfo _sceneField;
        private static double _peakTickMs, _totalTickMs;

        internal static void Initialize()
        {
            try
            {
                string folder = Path.Combine(Path.GetDirectoryName(typeof(NativeRenderProbe).Assembly.Location), "PoliticalRenderDiagnostics", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture));
                Directory.CreateDirectory(folder);
                _log = new StreamWriter(Path.Combine(folder, "identity-timeline.log")) { AutoFlush = true };
                _csv = new StreamWriter(Path.Combine(folder, "segments.csv"));
                _fillExport = new NativeFillGeometryExport(Path.Combine(folder, "fill-triangles.csv"));
                _cameraTimeline = new NativeCameraTimeline(Path.Combine(folder, "camera-timeline.csv"));
                _coastlineEvidence = new CoastlineEvidenceProbe(folder);
                _csv.WriteLine("segment,status,supportRejected,heightRejected,firstX,firstY,secondX,secondY,sample,x,y,rawZ,terrain,water,waterStatus,closeTerrainClearance,fullTerrainClearance,closeWaterClearance,fullWaterClearance,geometryKind,sampleRole,terrainNormalX,terrainNormalY,terrainNormalZ,fillGeometricNormalX,fillGeometricNormalY,fillGeometricNormalZ");
                Assembly core = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "AgesOfCalradia");
                string hash;
                using (var sha = SHA256.Create()) using (var stream = File.OpenRead(core.Location)) hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                Log("renderer=" + core.Location + ";sha256=" + hash + ";mvid=" + core.ManifestModule.ModuleVersionId);
                if (hash != ApprovedHash) throw new InvalidOperationException("Unsupported renderer: probes disabled.");
                Assembly engine = typeof(Scene).Assembly;
                Log("engine=" + engine.FullName + ";mvid=" + engine.ManifestModule.ModuleVersionId + ";native mutation=false; segmentLimit=" + SegmentLimit + "; budgetMs=3; single native call may exceed budget");
                foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic).Take(256))
                    Log("loadedAssembly=" + loaded.FullName + ";mvid=" + loaded.ManifestModule.ModuleVersionId);
                Type fill = core.GetType("TwelveMonthCalendar.CampaignPoliticalTerritoryFill", true);
                Type builder = fill.GetNestedType("Builder", BindingFlags.NonPublic);
                Type grid = core.GetType("TwelveMonthCalendar.CampaignMapTerrainGridCache", true);
                _sceneField = AccessTools.Field(grid, "_scene") ?? throw new MissingFieldException(grid.FullName, "_scene");
                _isComplete = AccessTools.Property(builder, "IsComplete") ?? throw new MissingMemberException("Builder.IsComplete");
                _harmony = new Harmony(Owner);
                Patch(builder, "AddFrontierSegment", "SegmentStart", "SegmentEnd");
                Patch(builder, "HasFrontierLandSupport", null, "SupportEnd");
                Patch(builder, "TryGetFrontierPoint", null, "HeightEnd");
                Patch(builder, "AddDoubleSidedQuad", "QuadStart", null);
                Patch(builder, "Advance", "AdvanceStart", "AdvanceEnd");
                Patch(builder, "CreateRowMesh", null, "MeshEnd");
                Patch(fill, "AddTriangle", null, "FillTriangleEnd");
                Patch(core.GetType("TwelveMonthCalendar.CampaignKingdomBorderBehavior", true), "SetPoliticalOverlayAlpha", null, "AlphaEnd");
                Patch(core.GetType("TwelveMonthCalendar.CampaignKingdomBorderBehavior", true), "ApplyPoliticalEntityVisibility", "AssignmentStart", "AssignmentEnd");
                Patch(core.GetType("TwelveMonthCalendar.CampaignKingdomBorderBehavior", true), "OnMapFrame", null, "MapFrameEnd");
                _enabled = true;
                Log("probes active; read-only observation; first builder only; rain and flora sampled once at first builder start");
                Log("fill stride=" + FillTriangleStride + ";fill triangle cap=" + FillTriangleLimit + ";fill identity world frame; rawZ includes approved terrain+4; no frontier offset applied; terrain probe excludes flora and cannot prove shader behavior;loaderThread=" + Thread.CurrentThread.ManagedThreadId + ";sceneThread=awaiting first OnApplicationTick");
                Log("cameraTimeline=post-OnMapFrame;rate<=1Hz;cap=300;scene TOD/rain/fog/sun sampled with each camera record;exposure=unavailable:no verified getter;camera native reads are separate bounded observations from geometry tick budget");
            }
            catch (Exception ex) { Fail(ex); }
        }

        private static void Patch(Type type, string name, string prefix, string postfix)
        {
            var targets = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance).Where(m => m.Name == name).ToArray();
            if (targets.Length != 1) throw new MissingMethodException(type.FullName, name + " unique target required");
            MethodInfo target = targets[0];
            var info = Harmony.GetPatchInfo(target);
            Log("target=" + target + ";owners=" + (info == null ? "none" : string.Join("|", info.Owners)));
            _harmony.Patch(target, prefix == null ? null : new HarmonyMethod(typeof(NativeRenderProbe), prefix), postfix == null ? null : new HarmonyMethod(typeof(NativeRenderProbe), postfix));
        }
        private static void AdvanceStart(object __instance)
        {
            if (!_enabled || _complete || !OnMainThread()) return;
            try
            {
                if (_builder == null)
                {
                    _builder = __instance; _scene = (Scene)_sceneField.GetValue(null);
                    Log(NativeFillStateProbe.CaptureScene(_scene));
                }
                _inBuilder = ReferenceEquals(_builder, __instance);
            }
            catch (Exception ex) { Fail(ex); }
        }
        private static void AdvanceEnd(object __instance)
        {
            _inBuilder = false;
            if (!_enabled || _complete || !ReferenceEquals(_builder, __instance)) return;
            try
            {
                if ((bool)_isComplete.GetValue(__instance, null)) { _complete = true; Log("first build complete; captured=" + _count + ";capReached=" + (_count >= SegmentLimit) + ";fillSeen=" + _fillSeen + ";fillCaptured=" + _fillCount); }
            }
            catch (Exception ex) { Fail(ex); }
        }
        private static void SegmentStart(object __instance, Vec2 first, Vec2 second)
        {
            if (!_enabled || _complete || !ReferenceEquals(_builder, __instance) || _count >= SegmentLimit) return;
            if (_active != null) { Fail(new InvalidOperationException("Reentrant segment capture; disable rather than misattribute.")); return; }
            _active = new Segment { Id = ++_count, First = first, Second = second };
        }
        private static void SegmentEnd(bool __result)
        {
            if (!_enabled || _active == null) return;
            _active.Accepted = __result;
            Pending.Enqueue(_active);
            _active = null;
        }
        private static void SupportEnd(bool __result) { if (_enabled && _active != null && !__result) _active.SupportRejected = true; }
        private static void HeightEnd(bool __result) { if (_enabled && _active != null && !__result) _active.HeightRejected = true; }
        private static void QuadStart(Vec3 firstOuter, Vec3 firstInner, Vec3 secondInner, Vec3 secondOuter)
        {
            if (!_enabled || _active == null || _active.Samples.Count >= 12) return;
            _active.Samples.AddRange(new[] { firstOuter, firstInner, secondInner, secondOuter,
                (firstOuter + secondInner + secondOuter) * (1f / 3f), (firstOuter + firstInner + secondInner) * (1f / 3f) });
        }
        // Exact approved AddTriangle emits two opposite-winding faces on success.
        // Observe once per logical triangle; no replacement or geometry writes.
        private static void FillTriangleEnd(Vec3 first, Vec3 second, Vec3 third, bool __result)
        {
            if (!_enabled || !_inBuilder || _complete || !__result || !OnMainThread()) return;
            _fillExport.Capture(first, second, third);
            long ordinal = _fillSeen++;
            if (_fillCount >= FillTriangleLimit || ordinal % FillTriangleStride != 0) return;
            var sample = new Segment { Id = ++_fillCount, Accepted = true, IsFill = true,
                First = new Vec2(first.x, first.y), Second = new Vec2(second.x, second.y) };
            Vec3 a = second - first, b = third - first;
            float nx = a.y * b.z - a.z * b.y, ny = a.z * b.x - a.x * b.z, nz = a.x * b.y - a.y * b.x;
            double length = Math.Sqrt((double)nx * nx + (double)ny * ny + (double)nz * nz);
            sample.GeometricNormal = length > 0 ? new Vec3((float)(nx / length), (float)(ny / length), (float)(nz / length)) : new Vec3(float.NaN, float.NaN, float.NaN);
            sample.Samples.AddRange(new[] { first, second, third,
                (first + second) * 0.5f, (second + third) * 0.5f, (third + first) * 0.5f,
                (first + second + third) * (1f / 3f) });
            Pending.Enqueue(sample);
        }
        private static void MeshEnd(Mesh __result, int renderOrder)
        {
            if (!_enabled || !OnMainThread() || !MaterialOrders.Add(renderOrder)) return;
            try
            {
                Material material = __result.GetMaterial();
                Log("material order=" + renderOrder + ";name=" + material.Name + ";flags=" + material.Flags
                    + ";shaderFlags=" + material.GetShaderFlags() + ";alphaBlend=" + material.GetAlphaBlendMode()
                    + ";alphaTest=" + F(material.GetAlphaTestValue()) + ";shader=" + material.GetShader().Name
                    + ";sunLight=" + material.UsingSunLight + ";dynamicLight=" + material.UsingDynamicLight
                    + ";sunShadowReceiver=" + material.IsSunShadowReceiver + ";dynamicShadowReceiver=" + material.IsDynamicShadowReceiver);
            }
            catch (Exception ex) { Fail(ex); }
        }
        private static void AlphaEnd(object __instance, float alpha)
        {
            if (!_enabled || AlphaBuckets.Count >= 64 || !RenderProbeMath.IsFinite(alpha) || !OnMainThread()) return;
            int bucket = (int)(Math.Max(0f, Math.Min(1f, alpha)) * 10f);
            try
            {
                double hours = CampaignTime.Now.ToHours;
                string key = Math.Floor(hours).ToString(CultureInfo.InvariantCulture) + ":" + bucket
                    + ":published=" + NativeFillStateProbe.IsPublished(__instance);
                if (AlphaBuckets.Add(key))
                {
                    Log("requestedAlpha=" + F(alpha) + ";intendedFillAlpha=" + F(Math.Max(0f, Math.Min(1f, alpha))) + ";approvedFillWorldOffset=0;approvedFrontierOffset=" + F(RenderProbeMath.FrontierWorldZ(0f, alpha)) + ";campaignHours=" + hours.ToString("R", CultureInfo.InvariantCulture));
                    Log(NativeFillStateProbe.Capture(__instance));
                }
            }
            catch (Exception ex) { Fail(ex); }
        }
        private static void AssignmentStart(object __instance, out NativeAlphaAssignmentProbe.Snapshot __state)
        {
            __state = _enabled && OnMainThread() ? NativeAlphaAssignmentProbe.Before(__instance) : null;
        }
        private static void MapFrameEnd(object __instance, Camera camera)
        {
            if (!_enabled || !OnMainThread()) return;
            _cameraTimeline.Capture(__instance, camera, _sceneField);
        }
        private static void AssignmentEnd(object __instance, bool forceAlpha, bool __runOriginal, NativeAlphaAssignmentProbe.Snapshot __state)
        {
            if (_enabled && OnMainThread()) NativeAlphaAssignmentProbe.After(__instance, forceAlpha, __runOriginal, __state);
        }
        internal static void RecordOptional(string message)
        {
            if (!_enabled) return;
            try { Log(message); }
            catch (Exception ex) { Fail(ex); }
        }
        internal static void Tick()
        {
            if (!_enabled) return;
            int observedThread = Thread.CurrentThread.ManagedThreadId;
            bool wasUnbound = ThreadGate.BoundThreadId == 0;
            if (ThreadGate.BindApplicationTick(observedThread) != ThreadObservation.Allowed)
            {
                Fail(new InvalidOperationException("Unexpected thread;callback=OnApplicationTick;expected=" + ThreadGate.BoundThreadId + ";observed=" + observedThread + ";no native diagnostic probe executed."));
                return;
            }
            if (wasUnbound) RecordOptional("sceneThread bound;callback=OnApplicationTick;expected=" + ThreadGate.BoundThreadId + ";observed=" + observedThread);
            if (_builder == null) return;
            var timer = Stopwatch.StartNew();
            try
            {
                // Never carry probes into a different scene or load.
                if (!ReferenceEquals(_scene, _sceneField.GetValue(null))) throw new InvalidOperationException("Scene changed; stale probes disabled.");
                // Reserve the first part of the SAME budget for raw geometry export
                // so ongoing native samples cannot starve the offline evidence.
                _fillExport.Drain(timer, 0.75);
                while (timer.Elapsed.TotalMilliseconds < 3)
                {
                    if (_sampling == null)
                    {
                        if (Pending.Count == 0) break;
                        _sampling = Pending.Dequeue(); _sampleIndex = 0;
                    }
                    Sample(_sampling, _sampleIndex++);
                    if (_sampleIndex >= Math.Max(1, _sampling.Samples.Count))
                    {
                        if (_sampling.IsFill) _fillWritten++; else _frontierWritten++;
                        _sampling = null; _written++;
                    }
                }
                _peakTickMs = Math.Max(_peakTickMs, timer.Elapsed.TotalMilliseconds);
                _totalTickMs += timer.Elapsed.TotalMilliseconds;
                if (_complete && Pending.Count == 0 && _sampling == null && !_fillExport.HasPending)
                {
                    Log(_fillExport.Complete());
                    _csv.Flush(); _coastlineEvidence?.Flush(); Log("sampling complete;geometryRecords=" + _written + ";fillTriangleRecords=" + _fillWritten
                        + ";frontierSegmentRecords=" + _frontierWritten + ";peakTickMs=" + _peakTickMs.ToString("F3", CultureInfo.InvariantCulture) + ";totalTickMs=" + _totalTickMs.ToString("F3", CultureInfo.InvariantCulture));
                    _builder = null;
                }
            }
            catch (Exception ex) { Fail(ex); }
        }
        private static void Sample(Segment segment, int index)
        {
            bool hasGeometry = segment.Samples.Count != 0;
            Vec3 p = hasGeometry ? segment.Samples[index] : new Vec3((segment.First.x + segment.Second.x) / 2f, (segment.First.y + segment.Second.y) / 2f, float.NaN);
            Vec2 xy = new Vec2(p.x, p.y);
            if (!segment.IsFill)
            {
                if (index == 0) _coastlineEvidence?.Path(_builder, segment.Id, segment.Accepted, segment.First, segment.Second);
                _coastlineEvidence?.Surface(segment.Id, index, p);
            }
            float height = 0f; Vec3 normal = Vec3.Zero;
            _scene.GetTerrainHeightAndNormal(xy, out height, out normal);
            float water = _scene.GetWaterLevelAtPosition(xy, true, true);
            bool finiteWater = RenderProbeMath.IsFinite(water);
            bool usableWater = finiteWater; // Numerical candidate only: native API has no validity result.
            float closeZ = segment.IsFill ? p.z : RenderProbeMath.FrontierWorldZ(p.z, 0f);
            string role = segment.IsFill ? (index < 3 ? "vertex" : index < 6 ? "edge-midpoint" : "centroid") : (hasGeometry ? "quad-corner-or-centroid" : "candidate-midpoint");
            _csv.WriteLine(string.Join(",", new[] { segment.Id.ToString(CultureInfo.InvariantCulture), segment.Accepted ? "accepted" : "rejected", segment.SupportRejected.ToString(), segment.HeightRejected.ToString(), F(segment.First.x), F(segment.First.y), F(segment.Second.x), F(segment.Second.y), hasGeometry ? index.ToString(CultureInfo.InvariantCulture) : "candidate-midpoint", F(p.x), F(p.y), F(p.z), F(height), F(water), !finiteWater ? "nonfinite-unknown" : "finite-candidate-validity-unknown", F(closeZ - height), F(p.z - height), usableWater ? F(closeZ - water) : "unknown", usableWater ? F(p.z - water) : "unknown", segment.IsFill ? "fill-triangle" : "frontier-segment", role, F(normal.x), F(normal.y), F(normal.z), segment.IsFill ? F(segment.GeometricNormal.x) : "unknown", segment.IsFill ? F(segment.GeometricNormal.y) : "unknown", segment.IsFill ? F(segment.GeometricNormal.z) : "unknown" }));
        }
        private static bool OnMainThread([CallerMemberName] string callback = null)
        {
            int observed = Thread.CurrentThread.ManagedThreadId;
            ThreadObservation result = ThreadGate.Observe(observed);
            if (result == ThreadObservation.Allowed) return true;
            if (result == ThreadObservation.AwaitingApplicationTick)
            {
                if (!_loggedBeforeTick)
                {
                    _loggedBeforeTick = true;
                    RecordOptional("scene probe skipped before first OnApplicationTick;callback=" + callback + ";expected=unbound;observed=" + observed);
                }
                return false;
            }
            Fail(new InvalidOperationException("Unexpected thread;callback=" + callback + ";expected=" + ThreadGate.BoundThreadId + ";observed=" + observed + ";no native diagnostic probe executed."));
            return false;
        }
        private static string F(float value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        private static void Log(string value) { if (_log != null) _log.WriteLine(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + " " + value); }
        private static void Fail(Exception ex)
        {
            _enabled = false; Pending.Clear(); _active = null; _sampling = null;
            try { Log("DISABLED; originals unchanged; " + ex); _csv?.Flush(); }
            catch (Exception logError) { Trace.TraceError("Political diagnostics sink failed: " + logError + "; original=" + ex); }
            Trace.TraceError("Political diagnostics disabled: " + ex);
        }
        internal static void Stop()
        {
            _enabled = false;
            try { _harmony?.UnpatchAll(Owner); _cameraTimeline?.Dispose(); _coastlineEvidence?.Dispose(); _fillExport?.Dispose(); _csv?.Dispose(); _log?.Dispose(); }
            catch (Exception ex) { Trace.TraceError("Political diagnostics shutdown failed: " + ex); }
        }
    }
}

