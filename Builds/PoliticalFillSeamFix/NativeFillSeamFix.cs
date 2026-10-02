using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using Path = System.IO.Path;

namespace AgesOfCalradia.PoliticalFillSeamFix
{
    internal static class NativeFillSeamFix
    {
        private const string Owner = "aoc.political-fill-seam-fix.v1";
        private const string ApprovedHash = "560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E";
        private const int CaptureLimit = 262144;
        private sealed class Prepared
        {
            internal SeamBuildResult Result;
            internal Dictionary<int, List<SeamTriangle>> Rows;
            internal string GeometryDump, DumpError;
        }
        private sealed class Generation
        {
            internal int Id;
            internal object Builder, Behavior;
            internal Scene Scene;
            internal bool Broken, FillComplete, Published, Committed;
            internal readonly List<SeamTriangle> Source = new List<SeamTriangle>();
            internal readonly List<FillRow> Rows = new List<FillRow>();
            internal readonly Dictionary<UIntPtr, FillRow> MeshRows = new Dictionary<UIntPtr, FillRow>();
            internal Task<Prepared> Preparation;
            internal FillSurfaceProjection SurfaceProjection;
            internal double PeakSurfaceMs, TotalSurfaceMs;
            internal Prepared Prepared;
            internal int UploadRow, UploadTriangle;
            internal double PeakUploadMs, TotalUploadMs;
            internal long VerifiedNativeFaces;
        }
        private static Harmony _harmony;
        private static StreamWriter _log;
        private static string _logDirectory;
        private static bool _enabled;
        private static int _applicationThread, _generationId;
        private static object _advancing;
        private static Generation _generation;
        private static PropertyInfo _fillComplete;
        private static FieldInfo _entities, _scene;
        private static MethodInfo _refreshAlpha;

        internal static void Initialize()
        {
            try
            {
                string directory = Path.Combine(Path.GetDirectoryName(typeof(NativeFillSeamFix).Assembly.Location), "Logs");
                Directory.CreateDirectory(directory);
                _logDirectory = directory;
                _log = new StreamWriter(Path.Combine(directory, "PoliticalFillSeamFix.log"), true) { AutoFlush = true };
                Assembly core = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "AgesOfCalradia");
                string hash;
                using (var sha = SHA256.Create()) using (var input = File.OpenRead(core.Location)) hash = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "");
                if (hash != ApprovedHash) throw new InvalidOperationException("Renderer hash unsupported: " + hash);
                Type fill = core.GetType("TwelveMonthCalendar.CampaignPoliticalTerritoryFill", true);
                Type builder = fill.GetNestedType("Builder", BindingFlags.NonPublic);
                Type behavior = core.GetType("TwelveMonthCalendar.CampaignKingdomBorderBehavior", true);
                _fillComplete = AccessTools.Property(builder, "IsFillComplete") ?? throw new MissingMemberException("IsFillComplete");
                _entities = AccessTools.Field(builder, "_entities") ?? throw new MissingFieldException("Builder._entities");
                _scene = AccessTools.Field(core.GetType("TwelveMonthCalendar.CampaignMapTerrainGridCache", true), "_scene") ?? throw new MissingFieldException("Grid._scene");
                _refreshAlpha = AccessTools.Method(behavior, "ApplyPoliticalEntityVisibility", new[] { typeof(bool) }) ?? throw new MissingMethodException("ApplyPoliticalEntityVisibility");
                _harmony = new Harmony(Owner);
                Install(builder, "Advance", "AdvanceStart", null, "AdvanceFinished", null);
                Install(builder, "AddRowEntity", null, "RowPublished", null, null);
                Install(fill, "AddTriangle", null, null, null, "CaptureFirstFace");
                Install(behavior, "ReplacePoliticalFillEntities", null, "FillPublished", null, null);
                Install(builder, "Cancel", "BuilderCancelled", null, null, null);
                Log("enabled;renderer=" + hash + ";engine=" + typeof(Scene).Assembly.FullName + ";engineMvid=" + typeof(Scene).Assembly.ManifestModule.ModuleVersionId
                    + ";surfacePolicy=campaign-scene-plus3-v1;island exclusion and accepted XY unchanged;captureLimit=" + CaptureLimit + ";uploadBudgetMs=4;individual native finalization/commit can exceed budget;loaderThread=" + Thread.CurrentThread.ManagedThreadId);
                _enabled = true;
            }
            catch (Exception ex)
            {
                Log("disabled during initialization; original renderer unchanged; " + ex);
                try { _harmony?.UnpatchAll(Owner); }
                catch (Exception cleanup) { Log("patch rollback failed; " + cleanup); }
            }
        }
        private static void Install(Type type, string name, string prefix, string postfix, string finalizer, string transpiler)
        {
            MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Where(m => m.Name == name).ToArray();
            if (methods.Length != 1) throw new MissingMethodException(type.FullName, name + " unique target required");
            var patches = Harmony.GetPatchInfo(methods[0]);
            string[] owners = patches == null ? new string[0] : patches.Owners.ToArray();
            Log("target=" + methods[0] + ";existingOwners=" + string.Join("|", owners));
            if (owners.Any(o => o != Owner && o != "aoc.tests.political-render-probe.v1"))
                throw new InvalidOperationException("Unverified geometry integration owns " + name + "; disable seam fix rather than combine pipelines.");
            _harmony.Patch(methods[0], PatchMethod(prefix), PatchMethod(postfix), PatchMethod(transpiler), PatchMethod(finalizer));
        }
        private static HarmonyMethod PatchMethod(string name) { return name == null ? null : new HarmonyMethod(typeof(NativeFillSeamFix), name); }

        // Hash-pinned managed method has exactly two native face submissions. Only
        // the first call is routed through capture; relay ALWAYS executes that same
        // native call. Second (reverse) face remains untouched. No global Mesh patch.
        private static IEnumerable<CodeInstruction> CaptureFirstFace(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            MethodInfo target = AccessTools.Method(typeof(Mesh), "AddTriangle", new[] { typeof(Vec3), typeof(Vec3), typeof(Vec3), typeof(Vec2), typeof(Vec2), typeof(Vec2), typeof(uint), typeof(UIntPtr) });
            var matches = code.Where(c => c.Calls(target)).ToArray();
            if (matches.Length != 2) throw new InvalidOperationException("Approved fill AddTriangle must contain exactly two Mesh.AddTriangle calls.");
            matches[0].opcode = OpCodes.Call;
            matches[0].operand = AccessTools.Method(typeof(NativeFillSeamFix), "SubmitAndCapture");
            return code;
        }
        private static void SubmitAndCapture(Mesh mesh, Vec3 a, Vec3 b, Vec3 c, Vec2 ua, Vec2 ub, Vec2 uc, uint color, UIntPtr handle)
        {
            // Submit first so a rejected native addition is never treated as accepted.
            mesh.AddTriangle(a, b, c, ua, ub, uc, color, handle);
            if (!_enabled || _advancing == null || !OnApplicationThread()) return;
            Generation generation = _generation;
            if (generation == null || generation.Broken || generation.FillComplete || !ReferenceEquals(generation.Builder, _advancing)) return;
            try
            {
                if (generation.Source.Count >= CaptureLimit) throw new InvalidOperationException("Capture limit reached; correction abandoned without truncation.");
                FillRow row;
                if (!generation.MeshRows.TryGetValue(mesh.Pointer, out row))
                {
                    row = new FillRow { Id = generation.Rows.Count, Original = mesh };
                    generation.Rows.Add(row); generation.MeshRows.Add(mesh.Pointer, row);
                }
                generation.Source.Add(new SeamTriangle(generation.Source.Count, row.Id, color,
                    new SeamVertex(a.x, a.y, a.z, ua.x, ua.y), new SeamVertex(b.x, b.y, b.z, ub.x, ub.y), new SeamVertex(c.x, c.y, c.z, uc.x, uc.y)));
                row.CapturedTriangles++;
            }
            catch (Exception ex) { Abandon(generation, "capture", ex); }
        }
        private static void AdvanceStart(object __instance, Scene scene)
        {
            if (!_enabled || !OnApplicationThread()) return;
            try
            {
                if (_advancing != null) throw new InvalidOperationException("Reentrant builder detected.");
                if (_generation == null || !ReferenceEquals(_generation.Builder, __instance))
                {
                    Discard("new builder generation");
                    _generation = new Generation { Id = ++_generationId, Builder = __instance, Scene = scene };
                    Log("capture started;generation=" + _generation.Id);
                }
                _advancing = __instance;
            }
            catch (Exception ex) { Abandon(_generation, "builder start", ex); }
        }
        private static Exception AdvanceFinished(object __instance, Exception __exception)
        {
            if (!_enabled || !ReferenceEquals(_advancing, __instance)) return __exception;
            _advancing = null;
            Generation generation = _generation;
            if (__exception != null) { Abandon(generation, "original builder failed", __exception); return __exception; }
            if (generation == null || generation.Broken || generation.FillComplete) return __exception;
            try
            {
                if (!(bool)_fillComplete.GetValue(__instance, null)) return __exception;
                generation.FillComplete = true;
                if (generation.Source.Count == 0 || generation.Rows.Any(r => r.Entity == null)) throw new InvalidOperationException("Incomplete fill capture or missing row entity.");
                generation.SurfaceProjection = new FillSurfaceProjection(generation.Source, QueryCampaignSurface);
                Log("surface projection queued;generation=" + generation.Id + ";source=" + generation.Source.Count + ";rows=" + generation.Rows.Count);
            }
            catch (Exception ex) { Abandon(generation, "prepare boundary", ex); }
            return __exception;
        }
        private static Prepared Prepare(SeamTriangle[] immutable, string dumpPath)
        {
            SeamBuildResult result; string error;
            if (!FillSeamConformer.TryBuild(immutable, out result, out error)) throw new InvalidOperationException(error);
            var prepared = new Prepared { Result = result, Rows = result.Triangles.GroupBy(t => t.RowId).ToDictionary(g => g.Key, g => g.ToList()) };
            try
            {
                using (var writer = new StreamWriter(dumpPath))
                {
                    writer.WriteLine("triangle,ax,ay,az,bx,by,bz,cx,cy,cz,parent,row,color");
                    int index = 0;
                    foreach (SeamTriangle triangle in result.Triangles)
                        writer.WriteLine(++index + "," + CsvVertex(triangle.A) + "," + CsvVertex(triangle.B) + "," + CsvVertex(triangle.C) + "," + triangle.ParentId + "," + triangle.RowId + "," + triangle.Color);
                }
                prepared.GeometryDump = dumpPath;
            }
            catch (Exception ex) { prepared.DumpError = ex.ToString(); }
            return prepared;
        }
        private static bool QueryCampaignSurface(float x, float y, out float height)
        {
            height = 0f;
            if (Campaign.Current?.MapSceneWrapper == null) return false;
            var point = new CampaignVec2(new Vec2(x, y), false);
            return Campaign.Current.MapSceneWrapper.GetHeightAtPoint(in point, ref height);
        }
        private static string CsvVertex(SeamVertex p) { return p.X.ToString("R", CultureInfo.InvariantCulture) + "," + p.Y.ToString("R", CultureInfo.InvariantCulture) + "," + p.Z.ToString("R", CultureInfo.InvariantCulture); }
        private static void RowPublished(object __instance, Mesh mesh)
        {
            Generation generation = _generation;
            if (!_enabled || generation == null || generation.Broken || !OnApplicationThread() || !ReferenceEquals(generation.Builder, __instance)) return;
            try
            {
                FillRow row;
                if (!generation.MeshRows.TryGetValue(mesh.Pointer, out row)) throw new InvalidOperationException("Published row missing captured geometry.");
                var entities = (List<GameEntity>)_entities.GetValue(__instance);
                if (entities == null || entities.Count == 0) throw new InvalidOperationException("Row publication missing entity.");
                row.Entity = entities[entities.Count - 1];
                if (row.Entity.GetFirstMesh().Pointer != mesh.Pointer) throw new InvalidOperationException("Published row mesh differs from capture.");
            }
            catch (Exception ex) { Abandon(generation, "row publication", ex); }
        }
        private static void FillPublished(object __instance, List<GameEntity> replacement)
        {
            Generation generation = _generation;
            if (!_enabled || generation == null || generation.Broken || !generation.FillComplete || !OnApplicationThread()) return;
            if (replacement.Count != generation.Rows.Count || generation.Rows.Any(r => !replacement.Contains(r.Entity)))
            { Abandon(generation, "publication mismatch", new InvalidOperationException("Published fill differs from captured generation.")); return; }
            generation.Behavior = __instance; generation.Published = true;
        }
        private static void BuilderCancelled(object __instance)
        {
            if (_generation != null && ReferenceEquals(_generation.Builder, __instance)) Discard("builder cancelled");
        }
        internal static void Tick()
        {
            if (!_enabled) return;
            int observed = Thread.CurrentThread.ManagedThreadId;
            Interlocked.CompareExchange(ref _applicationThread, observed, 0);
            if (!OnApplicationThread()) return;
            Generation generation = _generation;
            if (generation == null || generation.Broken || generation.Committed) return;
            try
            {
                if (!ReferenceEquals(generation.Scene, _scene.GetValue(null))) { Discard("scene changed"); return; }
                if (!generation.Published) return;
                if (generation.Preparation == null)
                {
                    var surfaceTimer = Stopwatch.StartNew();
                    generation.SurfaceProjection.Advance(256, () => surfaceTimer.Elapsed.TotalMilliseconds >= 4);
                    generation.PeakSurfaceMs = Math.Max(generation.PeakSurfaceMs, surfaceTimer.Elapsed.TotalMilliseconds);
                    generation.TotalSurfaceMs += surfaceTimer.Elapsed.TotalMilliseconds;
                    if (!generation.SurfaceProjection.Complete) return;
                    SeamTriangle[] immutable = generation.SurfaceProjection.Output;
                    string dumpPath = Path.Combine(_logDirectory, "fill-seam-corrected-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "-g" + generation.Id + ".csv");
                    generation.Preparation = Task.Run(() => Prepare(immutable, dumpPath));
                    Log("surface projection complete;generation=" + generation.Id + ";queries=" + generation.SurfaceProjection.QueryCount
                        + ";minZDelta=" + generation.SurfaceProjection.MinimumDelta + ";maxZDelta=" + generation.SurfaceProjection.MaximumDelta
                        + ";peakMs=" + generation.PeakSurfaceMs + ";totalMs=" + generation.TotalSurfaceMs + ";island exclusion unchanged;CPU preparation queued");
                    return;
                }
                if (!generation.Preparation.IsCompleted) return;
                if (generation.Prepared == null)
                {
                    generation.Prepared = generation.Preparation.GetAwaiter().GetResult();
                    if (generation.Prepared.Rows.Count != generation.Rows.Count) throw new InvalidOperationException("Prepared row coverage changed.");
                    Log("prepared;generation=" + generation.Id + ";changedParents=" + generation.Prepared.Result.ChangedParentCount + ";output=" + generation.Prepared.Result.Triangles.Count
                        + ";validatedSharedEdgePairs=" + generation.Prepared.Result.ValidatedSharedEdgePairs + ";correctedGeometryDump=" + generation.Prepared.GeometryDump + ";dumpError=" + generation.Prepared.DumpError);
                }
                Upload(generation);
            }
            catch (Exception ex) { Abandon(generation, "native upload/commit", ex); }
        }
        private static void Upload(Generation generation)
        {
            var timer = Stopwatch.StartNew();
            while (generation.UploadRow < generation.Rows.Count && timer.Elapsed.TotalMilliseconds < 4)
            {
                FillRow row = generation.Rows[generation.UploadRow];
                List<SeamTriangle> triangles = generation.Prepared.Rows[row.Id];
                if (row.Candidate == null)
                {
                    uint expectedOriginalFaces = checked((uint)row.CapturedTriangles * 2u);
                    uint actualOriginalFaces = row.Original.GetFaceCount();
                    if (!row.Original.IsValid || actualOriginalFaces != expectedOriginalFaces)
                        throw new InvalidOperationException("Original native face count differs from complete relay capture;row=" + row.Id + ";expected=" + expectedOriginalFaces + ";actual=" + actualOriginalFaces);
                    row.Candidate = Mesh.CreateMesh(true);
                    if (row.Candidate == null) throw new InvalidOperationException("Candidate mesh allocation failed.");
                    row.Candidate.SetMaterial(row.Original.GetMaterial());
                    row.Candidate.SetMeshRenderOrder(100);
                }
                UIntPtr handle = row.Candidate.LockEditDataWrite();
                try
                {
                    int emitted = 0;
                    while (generation.UploadTriangle < triangles.Count && emitted < 256 && timer.Elapsed.TotalMilliseconds < 4)
                    { NativeMeshTransaction.Emit(row.Candidate, triangles[generation.UploadTriangle++], handle); emitted++; }
                }
                finally { row.Candidate.UnlockEditDataWrite(handle); }
                uint expectedUploadedFaces = checked((uint)generation.UploadTriangle * 2u);
                uint uploadedFaces = row.Candidate.GetFaceCount();
                if (!row.Candidate.IsValid || uploadedFaces != expectedUploadedFaces)
                    throw new InvalidOperationException("Candidate chunk append count failed;row=" + row.Id + ";expected=" + expectedUploadedFaces + ";actual=" + uploadedFaces);
                if (generation.UploadTriangle < triangles.Count) break;
                row.Candidate.ComputeNormals(); row.Candidate.RecomputeBoundingBox();
                uint expectedFaces = checked((uint)triangles.Count * 2u);
                uint actualFaces = row.Candidate.GetFaceCount();
                if (!row.Candidate.IsValid || actualFaces != expectedFaces)
                    throw new InvalidOperationException("Candidate retained native face count failed;row=" + row.Id + ";expected=" + expectedFaces + ";actual=" + actualFaces);
                generation.VerifiedNativeFaces += actualFaces;
                generation.UploadTriangle = 0; generation.UploadRow++;
            }
            generation.PeakUploadMs = Math.Max(generation.PeakUploadMs, timer.Elapsed.TotalMilliseconds);
            generation.TotalUploadMs += timer.Elapsed.TotalMilliseconds;
            if (generation.UploadRow < generation.Rows.Count) return;
            var commitTimer = Stopwatch.StartNew();
            NativeMeshTransaction.Commit(generation.Rows, () => _refreshAlpha.Invoke(generation.Behavior, new object[] { true }));
            generation.Committed = true;
            Log("COMMITTED;generation=" + generation.Id + ";rows=" + generation.Rows.Count + ";source=" + generation.Source.Count
                + ";output=" + generation.Prepared.Result.Triangles.Count + ";peakUploadMs=" + generation.PeakUploadMs.ToString("F3", CultureInfo.InvariantCulture)
                + ";expectedNativeFaces=" + checked(generation.Prepared.Result.Triangles.Count * 2L) + ";verifiedNativeFaces=" + generation.VerifiedNativeFaces
                + ";validatedSharedEdgePairs=" + generation.Prepared.Result.ValidatedSharedEdgePairs
                + ";totalUploadMs=" + generation.TotalUploadMs.ToString("F3", CultureInfo.InvariantCulture) + ";commitMs=" + commitTimer.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture)
                + ";entity identity/frame preserved;alpha reapplied through approved behavior;materials unchanged");
            // Commit and alpha refresh succeeded: release rollback-only originals
            // and CPU snapshots on the application thread. Entities own candidates.
            foreach (FillRow row in generation.Rows) { row.Original = null; row.Candidate = null; }
            generation.Source.Clear(); generation.Source.TrimExcess();
            generation.MeshRows.Clear(); generation.Rows.Clear();
            generation.Prepared = null; generation.Preparation = null; generation.SurfaceProjection = null;
        }
        private static bool OnApplicationThread()
        {
            int expected = Volatile.Read(ref _applicationThread);
            if (expected == 0) return false;
            if (expected == Thread.CurrentThread.ManagedThreadId) return true;
            _enabled = false; Log("disabled: unexpected native callback thread;expected=" + expected + ";observed=" + Thread.CurrentThread.ManagedThreadId); return false;
        }
        private static void Abandon(Generation generation, string stage, Exception ex)
        {
            if (generation != null) generation.Broken = true;
            Log("generation abandoned;stage=" + stage + ";original renderer continues unless commit reports incomplete rollback; " + ex);
        }
        private static void Discard(string reason)
        {
            if (_generation != null)
            {
                if (_generation.Preparation != null) _generation.Preparation.ContinueWith(t => { var observed = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                Log("discard generation=" + _generation.Id + ";reason=" + reason + ";detached candidates released by native wrapper lifetime");
            }
            _generation = null; _advancing = null;
        }
        internal static void Log(string text)
        {
            try { _log?.WriteLine(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + " " + text); }
            catch (Exception ex) { _enabled = false; Trace.TraceError("Seam fix diagnostic sink failed: " + ex + ";message=" + text); }
        }
        internal static void Stop()
        {
            _enabled = false;
            try { _harmony?.UnpatchAll(Owner); Discard("module unloaded"); _log?.Dispose(); }
            catch (Exception ex) { Trace.TraceError("Seam fix unload failed: " + ex); }
        }
    }
}
