using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AgesOfCalradia.CoastSurfaceFix
{
    public sealed class CoastSurfaceSubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad() { base.OnSubModuleLoad(); CoastSurfacePatch.Initialize(); }
        protected override void OnApplicationTick(float dt) { base.OnApplicationTick(dt); CoastSurfacePatch.BindThread(); }
        protected override void OnSubModuleUnloaded() { CoastSurfacePatch.Stop(); base.OnSubModuleUnloaded(); }
    }

    internal static class CoastSurfacePatch
    {
        private const string Owner = "aoc.coast-surface-fix.v1";
        private const string CoreHash = "560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E";
        private sealed class Cache
        {
            public Cache() { }
            internal readonly CoastHeightCache Heights = new CoastHeightCache();
            internal readonly CoastJoinPlan Plan = new CoastJoinPlan();
            internal CoastTopologyScanner Scanner;
            internal int CoastSegments, JoinSegments, RestoredSegments, Fallbacks, Queries, WaterPoints, Candidates;
            internal bool Reported, PlanFailed;
            internal double QueryMs, PlanMs;
            internal int CapInputTriangles, CapOutputTriangles;
        }
        private sealed class SegmentState
        {
            internal Dictionary<Vec2, float> Points;
            internal bool Restore;
            internal Cache CapCache;
        }
        private static ConditionalWeakTable<object, Cache> _caches = new ConditionalWeakTable<object, Cache>();
        private static Harmony _harmony;
        private static MethodInfo _region, _terrain, _support, _gridPoint;
        private static PropertyInfo _land, _complete, _rows;
        private static bool _enabled;
        private static int _thread;
        private static StreamWriter _log;
        [ThreadStatic] private static Scene _scene;
        [ThreadStatic] private static Dictionary<Vec2, float> _points;
        [ThreadStatic] private static bool _restoreSupport;
        [ThreadStatic] private static Cache _capCache;

        internal static void Initialize()
        {
            try
            {
                string folder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(CoastSurfacePatch).Assembly.Location), "Logs");
                Directory.CreateDirectory(folder);
                _log = new StreamWriter(System.IO.Path.Combine(folder, "CoastSurfaceFix.log"), true) { AutoFlush = true };
                Assembly core = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "AgesOfCalradia");
                using (var sha = SHA256.Create()) using (var file = File.OpenRead(core.Location))
                    if (BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "") != CoreHash)
                        throw new InvalidOperationException("Unsupported political renderer; original borders retained.");
                Type builder = core.GetType("TwelveMonthCalendar.CampaignPoliticalTerritoryFill+Builder", true);
                _region = Required(builder, "GetFrontierRegion", typeof(Vec2));
                _land = AccessTools.Property(_region.ReturnType, "Land") ?? throw new MissingMemberException("FrontierRegion.Land");
                _complete = AccessTools.Property(builder, "IsComplete") ?? throw new MissingMemberException("Builder.IsComplete");
                _support = Required(builder, "HasFrontierLandSupport", typeof(Vec2), typeof(Vec2), typeof(Vec2));
                Type grid = core.GetType("TwelveMonthCalendar.CampaignMapTerrainGridCache", true);
                _rows = AccessTools.Property(grid, "Rows") ?? throw new MissingMemberException("Grid.Rows");
                _gridPoint = Required(grid, "GetGridPoint", typeof(int), typeof(int), typeof(float));
                _terrain = grid.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).Single(m => m.Name == "TryGetNativeTerrain");
                if (_terrain.ReturnType != typeof(bool) || _terrain.GetParameters().Length != 2 || !_terrain.GetParameters()[1].IsOut)
                    throw new MissingMethodException("Native terrain query contract changed.");
                Required(typeof(Scene), "GetWaterLevelAtPosition", typeof(Vec2), typeof(bool), typeof(bool));
                _harmony = new Harmony(Owner);
                Patch(Required(builder, "Advance", typeof(Scene)), "AdvanceStart", null, "AdvanceEnd");
                Patch(Required(builder, "AddFrontierSegment", typeof(Mesh), typeof(Vec2), typeof(Vec2), typeof(UIntPtr)), "SegmentStart", null, "SegmentEnd");
                Patch(Required(builder, "TryGetFrontierPoint", typeof(Vec2), typeof(Vec3).MakeByRefType()), null, "PointEnd", null);
                Patch(Required(builder, "BuildNextFrontierRow", typeof(Scene)), "FrontierRowStart", null, null);
                Patch(_support, null, "SupportEnd", null);
                Patch(Required(builder, "AddDoubleSidedFanTriangle", typeof(Mesh), typeof(Vec3), typeof(Vec3), typeof(Vec3), typeof(uint), typeof(UIntPtr)), "CapStart", null, null);
                _enabled = true;
                Log("enabled v3; exposed coastal caps only; whole-map join prepass and footprint rescues retained; campaign surface/water floor, exclusions/depth/zoom retained; awaiting application tick");
            }
            catch (Exception ex) { Log("disabled during integration binding: " + ex); _harmony?.UnpatchAll(Owner); }
        }
        private static MethodInfo Required(Type type, string name, params Type[] args)
        {
            return AccessTools.Method(type, name, args) ?? throw new MissingMethodException(type.FullName, name);
        }
        private static void Patch(MethodInfo target, string prefix, string postfix, string finalizer)
        {
            var existing = Harmony.GetPatchInfo(target);
            // Known read-only diagnostics and accepted fill scope are compatible.
            if (existing != null && existing.Owners.Any(o => o != Owner && o != "aoc.tests.political-render-probe.v1" && o != "aoc.political-fill-seam-fix.v1"))
                throw new InvalidOperationException("Competing patch on " + target);
            _harmony.Patch(target,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(CoastSurfacePatch), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(CoastSurfacePatch), postfix) { priority = Priority.First },
                finalizer: finalizer == null ? null : new HarmonyMethod(typeof(CoastSurfacePatch), finalizer));
            Log("bound " + target);
        }
        internal static void BindThread() { if (_enabled) Interlocked.CompareExchange(ref _thread, Thread.CurrentThread.ManagedThreadId, 0); }
        private static bool OnThread() { return _enabled && _thread != 0 && _thread == Thread.CurrentThread.ManagedThreadId; }
        private static void AdvanceStart(Scene scene, out Scene __state)
        {
            __state = _scene;
            if (OnThread()) _scene = scene;
        }
        private static void AdvanceEnd(object __instance, Scene __state)
        {
            _scene = __state;
            if (!OnThread()) return;
            try
            {
                Cache cache;
                if (_caches.TryGetValue(__instance, out cache) && !cache.Reported && (bool)_complete.GetValue(__instance, null))
                {
                    cache.Reported = true;
                    Log("complete;coastSegments=" + cache.CoastSegments + ";joinSegments=" + cache.JoinSegments + ";restoredSegments=" + cache.RestoredSegments + ";fallbacks=" + cache.Fallbacks + ";uniqueQueries=" + cache.Queries + ";waterPoints=" + cache.WaterPoints + ";queryMs=" + cache.QueryMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
                    Log("cap clipping;inputTriangles=" + cache.CapInputTriangles + ";outputTriangles=" + cache.CapOutputTriangles);
                }
            }
            catch (Exception ex) { Log("completion reflection failed: " + ex); }
        }
        private static string Terrain(Vec2 p)
        {
            object[] args = { p, Activator.CreateInstance(_terrain.GetParameters()[1].ParameterType.GetElementType()) };
            return (bool)_terrain.Invoke(null, args) ? args[1].ToString() : "unknown";
        }
        private static object Region(object builder, Vec2 point) { return _region.Invoke(builder, new object[] { point }); }
        private static bool Land(object builder, Vec2 point) { return (bool)_land.GetValue(Region(builder, point), null); }
        private static bool IsCoast(object builder, Vec2 first, Vec2 second, Vec2 direction)
        {
            Vec2 normal = new Vec2(-direction.y, direction.x), middle = (first + second) * .5f;
            Vec2 l = middle + normal * 2.75f, r = middle - normal * 2.75f;
            bool left = Land(builder, l), right = Land(builder, r);
            return left != right && CoastSurfacePolicy.IsCoast(left, right, Terrain(left ? r : l));
        }
        private static bool FrontierRowStart(object __instance)
        {
            if (!OnThread() || _scene == null) return true;
            Cache cache = _caches.GetOrCreateValue(__instance);
            if (cache.PlanFailed) return true;
            try
            {
                if (cache.Scanner == null)
                {
                    int rows = (int)_rows.GetValue(null, null);
                    var minimum = (Vec3)_gridPoint.Invoke(null, new object[] { 0, 0, 0f });
                    var maximum = (Vec3)_gridPoint.Invoke(null, new object[] { rows, 96, 0f });
                    cache.Scanner = new CoastTopologyScanner(new Vec2(minimum.x, minimum.y), new Vec2(maximum.x, maximum.y), rows * 4, 384,
                        p => Region(__instance, p), (a, b) => PlanCandidate(__instance, cache, a, b));
                }
                if (cache.Scanner.Complete) return true;
                var timer = Stopwatch.StartNew();
                cache.Scanner.AdvanceRow();
                cache.PlanMs += timer.Elapsed.TotalMilliseconds;
                if (cache.Scanner.Complete)
                    Log("join plan complete;candidates=" + cache.Candidates + ";coast=" + cache.Plan.Coast.Count + ";footprintRescues=" + cache.Plan.Restored.Count + ";planMs=" + cache.PlanMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
                // Do not change _nextFrontierRow. The original builder's budgeted
                // loop retries until this generation's complete plan is ready.
                return cache.Scanner.Complete;
            }
            catch (Exception ex)
            {
                cache.PlanFailed = true;
                Log("join planning failed; retain v1 surface policy and original support: " + ex);
                return true;
            }
        }
        private static void PlanCandidate(object builder, Cache cache, Vec2 first, Vec2 second)
        {
            cache.Candidates++;
            Vec2 direction = second - first;
            if (direction.Normalize() < .001f) return;
            bool supported = (bool)_support.Invoke(builder, new object[] { first, second, direction });
            if (!IsCoast(builder, first, second, direction))
            {
                if (supported) cache.Plan.AddIncident(first, second);
                return;
            }
            if (!supported && !CoastSupportPolicy.HasFootprintSupport(first, second, direction, p => Land(builder, p))) return;
            cache.Plan.Add(first, second, !supported);
        }
        private static void SegmentStart(object __instance, Vec2 first, Vec2 second, out SegmentState __state)
        {
            __state = new SegmentState { Points = _points, Restore = _restoreSupport, CapCache = _capCache };
            _points = null; _restoreSupport = false; _capCache = null;
            if (!OnThread() || _scene == null) return;
            Cache cache = null;
            try
            {
                Vec2 direction = second - first;
                if (direction.Normalize() < .001f) return;
                Vec2 normal = new Vec2(-direction.y, direction.x);
                cache = _caches.GetOrCreateValue(__instance);
                bool planned = !cache.PlanFailed && cache.Scanner != null && cache.Scanner.Complete;
                var span = new CoastSpan(first, second);
                bool coast = planned ? cache.Plan.Coast.Contains(span) : IsCoast(__instance, first, second, direction);
                bool restore = planned && cache.Plan.Restored.Contains(span);
                bool joins = planned && (cache.Plan.AtJoin(first) || cache.Plan.AtJoin(second));
                if (!coast && !joins) return;
                if (!restore && !(bool)_support.Invoke(__instance, new object[] { first, second, direction })) return;
                var prepared = new Dictionary<Vec2, float>();
                // Preflight every point used by the approved two quads and caps.
                // A failed query leaves the entire segment on the original path.
                foreach (Vec2 p in SamplePoints(first, second, direction, normal))
                {
                    if (!coast && !cache.Plan.AtJoin(p)) continue;
                    float z;
                    if (!cache.Heights.TryGetValue(p, out z))
                    {
                        var timer = Stopwatch.StartNew();
                        try
                        {
                            float surface = 0;
                            var position = new CampaignVec2(p, false);
                            bool valid = Campaign.Current?.MapSceneWrapper != null && Campaign.Current.MapSceneWrapper.GetHeightAtPoint(in position, ref surface);
                            bool water = Terrain(p) == "CoastalSea";
                            float level = water ? _scene.GetWaterLevelAtPosition(p, true, true) : 0;
                            cache.Queries++;
                            if (water) cache.WaterPoints++;
                            if (!CoastSurfacePolicy.Project(valid, surface, water, level, out z))
                                throw new InvalidOperationException("Invalid surface query at " + p);
                            cache.Heights.Add(p, z);
                        }
                        finally { cache.QueryMs += timer.Elapsed.TotalMilliseconds; }
                    }
                    prepared[p] = z;
                }
                _points = prepared;
                _restoreSupport = restore;
                _capCache = planned ? cache : null;
                if (coast) cache.CoastSegments++; else cache.JoinSegments++;
                if (restore) cache.RestoredSegments++;
            }
            catch (Exception ex)
            {
                if (cache != null) { cache.Fallbacks++; if (cache.Fallbacks > 3) return; }
                Log("segment retained on original height;preflight failed: " + ex);
            }
        }
        internal static IEnumerable<Vec2> SamplePoints(Vec2 first, Vec2 second, Vec2 direction, Vec2 normal)
        {
            foreach (Vec2 p in new[] { first, second })
            {
                yield return p; yield return p + normal * .8f; yield return p - normal * .8f;
                yield return p + direction * .8f;
                foreach (float sign in new[] { 1f, -1f })
                    for (int i = 1; i <= 4; i++)
                    {
                        float angle = (float)Math.PI * i / 4f;
                        yield return p + (direction * ((float)Math.Cos(angle) * .8f) + normal * ((float)Math.Sin(angle) * .8f * sign));
                    }
            }
        }
        private static void SegmentEnd(SegmentState __state)
        {
            _points = __state == null ? null : __state.Points;
            _restoreSupport = __state != null && __state.Restore;
            _capCache = __state == null ? null : __state.CapCache;
        }
        private static bool CapStart(Mesh mesh, Vec3 center, Vec3 first, Vec3 second, uint color, UIntPtr lockHandle)
        {
            if (!OnThread() || _capCache == null || !_capCache.Plan.AtJoin(new Vec2(center.x, center.y))) return true;
            List<Vec3> polygon;
            try
            {
                var directions = _capCache.Plan.DirectionsAt(new Vec2(center.x, center.y));
                if (directions.Count == 0) return true;
                polygon = CoastCapClipper.Clip(center, first, second, directions);
            }
            catch (Exception ex) { Log("cap preparation failed; original cap retained: " + ex); return true; }
            _capCache.CapInputTriangles++;
            // Interpolated points stay on the original cap triangle. Do not
            // raise caps or change the accepted surface policy/material.
            for (int i=1; i+1<polygon.Count; i++)
            {
                Vec3 a=polygon[0], b=polygon[i], c=polygon[i+1];
                if (Math.Abs(CoastCapClipper.AreaTwice(a,b,c)) < 1e-8) continue;
                try
                {
                    mesh.AddTriangle(a,b,c,Vec2.Zero,Vec2.Zero,Vec2.Zero,color,lockHandle);
                    mesh.AddTriangle(a,c,b,Vec2.Zero,Vec2.Zero,Vec2.Zero,color,lockHandle);
                }
                catch (Exception ex) { Log("native cap upload failed; do not append overlapping fallback: " + ex); throw; }
                _capCache.CapOutputTriangles++;
            }
            return false;
        }
        private static void SupportEnd(ref bool __result)
        {
            if (!__result && OnThread() && _restoreSupport && _points != null) __result = true;
        }
        private static void PointEnd(Vec2 point, ref Vec3 terrainPoint, bool __result)
        {
            float z;
            if (OnThread() && __result && _points != null && _points.TryGetValue(point, out z)) terrainPoint.z = z;
        }
        private static void Log(string message)
        {
            try { _log?.WriteLine(DateTime.UtcNow.ToString("O") + " " + message); }
            catch (Exception ex) { Trace.TraceError(ex.ToString()); }
        }
        internal static void Stop()
        {
            _enabled = false;
            try { _harmony?.UnpatchAll(Owner); _log?.Dispose(); }
            catch (Exception ex) { Trace.TraceError(ex.ToString()); }
            _points = null; _scene = null; _restoreSupport = false; _capCache = null; _caches = new ConditionalWeakTable<object, Cache>(); _thread = 0;
        }
    }
}
