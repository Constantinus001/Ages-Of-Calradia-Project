using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using AgesOfCalradia.PoliticalBorderOverrides;
using BinaryReader = System.IO.BinaryReader;
using BinaryWriter = System.IO.BinaryWriter;
using IOPath = System.IO.Path;

namespace AgesOfCalradia.PoliticalBorderOptimizer
{
    internal static class PoliticalBorderGeometryCache
    {
        private const string Magic = "AOCPBG60";
        private const int FormatVersion = 60;
        private const int MaximumEntities = 4096;
        private const int MaximumTriangles = 1000000;
        private const long MaximumCacheBytes = 96L * 1024L * 1024L;
        private const float FillReplayLift = 0f;
        private const float FrontierReplayLift = 0f;
        private const string FillMaterial = "vertex_color_mat";
        private const string FrontierMaterial = "vertex_color_mat";
        private const int FillRenderOrder = 100;
        private const int FrontierRenderOrder = 255;
        private const MaterialFlags FrontierOverlayFlags =
            MaterialFlags.NoDepthTest
            | MaterialFlags.NoModifyDepthBuffer
            | MaterialFlags.DontDrawToDepthRenderTarget
            | MaterialFlags.DontCastShadow
            | MaterialFlags.RequiresForwardRendering
            | MaterialFlags.RenderOrderPlus_7;
        private const string RendererHash =
            "560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E";

        private static CaptureSession _capture;
        [ThreadStatic] private static bool _insideBuilderAdvance;
        [ThreadStatic] private static SegmentCapture _segmentCapture;

        internal static bool HasColdCapture { get { return _capture != null; } }

        internal static void PatchTargets(Harmony harmony, Type builderType)
        {
            MethodInfo meshAddTriangle = AccessTools.Method(typeof(Mesh), "AddTriangle", new[]
            {
                typeof(Vec3), typeof(Vec3), typeof(Vec3),
                typeof(Vec2), typeof(Vec2), typeof(Vec2),
                typeof(uint), typeof(UIntPtr)
            });
            MethodInfo addRowEntity = AccessTools.Method(builderType, "AddRowEntity");
            MethodInfo addFrontierEntity = AccessTools.Method(builderType, "AddFrontierEntity");
            MethodInfo addFrontierSegment = AccessTools.Method(builderType, "AddFrontierSegment");
            MethodInfo tryGetFrontierPoint = AccessTools.Method(builderType, "TryGetFrontierPoint");
            if (meshAddTriangle == null || addRowEntity == null || addFrontierEntity == null
                || addFrontierSegment == null || tryGetFrontierPoint == null)
            {
                throw new MissingMemberException("Persistent political geometry capture targets were not found.");
            }

            harmony.Patch(meshAddTriangle,
                prefix: new HarmonyMethod(typeof(PoliticalBorderGeometryCache), nameof(BeforeMeshAddTriangle)));
            harmony.Patch(addRowEntity,
                prefix: new HarmonyMethod(typeof(PoliticalBorderGeometryCache), nameof(BeforeAddRowEntity)));
            harmony.Patch(addFrontierEntity,
                prefix: new HarmonyMethod(typeof(PoliticalBorderGeometryCache), nameof(BeforeAddFrontierEntity)));
            harmony.Patch(addFrontierSegment,
                prefix: new HarmonyMethod(typeof(PoliticalBorderGeometryCache), nameof(BeforeFrontierSegment)),
                postfix: new HarmonyMethod(typeof(PoliticalBorderGeometryCache), nameof(AfterFrontierSegment)));
            harmony.Patch(tryGetFrontierPoint,
                postfix: new HarmonyMethod(typeof(PoliticalBorderGeometryCache), nameof(AfterFrontierPoint)));
        }

        internal static CacheIdentity CreateIdentity(
            string ownershipSignature,
            object scene,
            IEnumerable cells,
            float minimumX,
            float minimumY,
            float maximumX,
            float maximumY)
        {
            if (cells == null) throw new ArgumentNullException(nameof(cells));
            StringBuilder signature = new StringBuilder(32768);
            signature.Append(Magic).Append('|').Append(RendererHash).Append('|')
                .Append(typeof(Mesh).Assembly.GetName().Version).Append('|')
                .Append(scene == null ? "<no-scene>" : InvokeSceneName(scene)).Append('|')
                .Append(FloatBits(minimumX)).Append('|').Append(FloatBits(minimumY)).Append('|')
                .Append(FloatBits(maximumX)).Append('|').Append(FloatBits(maximumY)).Append('|')
                .Append(ownershipSignature ?? string.Empty).Append('|')
                .Append(ComputeOverrideHash()).Append('|');

            List<string> cellSignatures = new List<string>();
            List<TerritorySite> sites = new List<TerritorySite>();
            foreach (object cell in cells)
            {
                if (cell == null) continue;
                Type type = cell.GetType();
                Vec2 site = (Vec2)RequireProperty(type, "Site").GetValue(cell, null);
                string owner = (string)RequireProperty(type, "OwnerKey").GetValue(cell, null);
                uint color = (uint)RequireProperty(type, "Color").GetValue(cell, null);
                sites.Add(new TerritorySite(site, owner, color));
                cellSignatures.Add((owner ?? string.Empty) + ":" + color.ToString("X8") + ":"
                    + FloatBits(site.x) + ":" + FloatBits(site.y));
            }
            cellSignatures.Sort(StringComparer.Ordinal);
            foreach (string value in cellSignatures) signature.Append(value).Append('|');

            string key;
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(signature.ToString()));
                StringBuilder text = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) text.Append(value.ToString("X2"));
                key = text.ToString();
            }

            string assemblyDirectory = IOPath.GetDirectoryName(typeof(PoliticalBorderGeometryCache).Assembly.Location);
            DirectoryInfo binaryDirectory = string.IsNullOrWhiteSpace(assemblyDirectory)
                ? null : Directory.GetParent(assemblyDirectory);
            DirectoryInfo moduleDirectory = binaryDirectory == null ? null : binaryDirectory.Parent;
            string moduleRoot = moduleDirectory == null ? assemblyDirectory : moduleDirectory.FullName;
            string directory = IOPath.Combine(moduleRoot, "Cache", "PoliticalBorders", "v" + FormatVersion);
            return new CacheIdentity(
                key,
                IOPath.Combine(directory, key + ".bin.gz"),
                sites,
                minimumX,
                minimumY,
                maximumX,
                maximumY);
        }

        private static string ComputeOverrideHash()
        {
            try
            {
                string path = PoliticalBorderEditorDocument.ResolvePath();
                if (!File.Exists(path)) return "<no-authored-overrides>";
                using (FileStream stream = File.OpenRead(path))
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(stream);
                    StringBuilder text = new StringBuilder(hash.Length * 2);
                    foreach (byte value in hash) text.Append(value.ToString("X2"));
                    return text.ToString();
                }
            }
            catch (Exception exception)
            {
                // Override hashing crosses a mod-file boundary. A stable failure
                // token forces safe generated geometry without blocking play.
                BorderOptimizerDiagnostics.Error(
                    "Authored override identity could not be read; generated geometry will be used.",
                    exception);
                return "<authored-overrides-unreadable>";
            }
        }

        internal static bool TryLoad(CacheIdentity identity, out CachedGeometry geometry)
        {
            geometry = null;
            try
            {
                FileInfo file = new FileInfo(identity.Path);
                if (!file.Exists) return false;
                if (file.Length <= 0 || file.Length > MaximumCacheBytes)
                    throw new InvalidDataException("Political geometry cache file size is invalid.");
                using (FileStream stream = File.OpenRead(file.FullName))
                using (GZipStream gzip = new GZipStream(stream, CompressionMode.Decompress))
                using (BinaryReader reader = new BinaryReader(gzip, Encoding.UTF8))
                {
                    if (reader.ReadString() != Magic || reader.ReadInt32() != FormatVersion)
                        throw new InvalidDataException("Political geometry cache header is incompatible.");
                    if (!string.Equals(reader.ReadString(), identity.Key, StringComparison.Ordinal))
                        throw new InvalidDataException("Political geometry cache key does not match this campaign.");
                    geometry = new CachedGeometry(
                        ReadEntities(reader, MaximumEntities),
                        ReadEntities(reader, MaximumEntities),
                        PoliticalBorderGeometryDiagnostics.Read(reader));
                    if (reader.ReadByte() != 0xA5)
                        throw new InvalidDataException("Political geometry cache terminator is missing.");
                }
                BorderOptimizerDiagnostics.Info("Persistent political geometry cache hit: key="
                    + identity.Key.Substring(0, 12) + "; bytes=" + file.Length + ".");
                PoliticalBorderGeometryDiagnostics.Log("warm-cache", identity, geometry.Diagnostics);
                return true;
            }
            catch (Exception exception)
            {
                // Cache I/O is an external file boundary. Ignore corrupt or
                // incompatible data and rebuild through the approved renderer.
                geometry = null;
                BorderOptimizerDiagnostics.Error(
                    "Persistent political geometry cache was rejected; a cold rebuild will be used.", exception);
                return false;
            }
        }

        internal static void BeginColdCapture(CacheIdentity identity)
        {
            _capture = new CaptureSession(identity);
            BorderOptimizerDiagnostics.Info("Persistent political geometry cache miss: key="
                + identity.Key.Substring(0, 12) + ". Capturing approved renderer output.");
        }

        internal static CachedGeometry CompleteColdCapture()
        {
            CaptureSession capture = _capture;
            _capture = null;
            if (capture == null) throw new InvalidOperationException("No political geometry capture is active.");
            PoliticalBorderGeneratedSnapshotExporter.TryWrite(capture.Segments);
            PoliticalGeometryDiagnosticsRecord diagnostics = PoliticalBorderGeometryDiagnostics.Analyze(
                capture.FillEntities, capture.FrontierEntities, capture.Segments);
            PoliticalBorderArtifactDiagnostics.AnalyzeSource(
                capture.FillEntities, capture.FrontierEntities, capture.Segments, diagnostics);
            PoliticalBorderGeometryRepair.Apply(
                capture.FillEntities,
                capture.FrontierEntities,
                capture.Segments,
                diagnostics);
            PoliticalBorderGeometryDiagnostics.UpdateOutputCounts(
                capture.FillEntities,
                capture.FrontierEntities,
                diagnostics);
            PoliticalBorderArtifactDiagnostics.AnalyzeOutput(
                capture.FillEntities, capture.FrontierEntities, capture.Segments, diagnostics);
            CachedGeometry geometry = new CachedGeometry(
                capture.FillEntities,
                capture.FrontierEntities,
                diagnostics);
            WriteCache(capture.Identity, geometry);
            PoliticalBorderGeometryDiagnostics.Log("cold-capture", capture.Identity, diagnostics);
            return geometry;
        }

        internal static void AbandonColdCapture()
        {
            _capture = null;
            _insideBuilderAdvance = false;
            _segmentCapture = null;
        }

        internal static void BeginBuilderAdvance()
        {
            if (_capture != null) _insideBuilderAdvance = true;
        }

        internal static void EndBuilderAdvance()
        {
            _segmentCapture = null;
            _insideBuilderAdvance = false;
        }

        internal static void BeforeMeshAddTriangle(
            Mesh __instance,
            Vec3 p1,
            Vec3 p2,
            Vec3 p3,
            Vec2 uv1,
            Vec2 uv2,
            Vec2 uv3,
            uint color)
        {
            CaptureSession capture = _capture;
            if (!_insideBuilderAdvance || capture == null || __instance == null) return;
            try
            {
                Triangle triangle = new Triangle(p1, p2, p3, uv1, uv2, uv3, color);
                capture.GetMesh(__instance).Triangles.Add(triangle);
                if (_segmentCapture != null) _segmentCapture.Colors.Add(color);
            }
            catch (Exception exception)
            {
                AbandonCaptureAfterError(exception);
            }
        }

        internal static void BeforeAddRowEntity(Mesh mesh)
        {
            CaptureSession capture = _capture;
            if (!_insideBuilderAdvance || capture == null || mesh == null) return;
            try
            {
                EntityGeometry entity = capture.TakeMesh(mesh);
                if (entity != null && entity.Triangles.Count > 0) capture.FillEntities.Add(entity);
            }
            catch (Exception exception) { AbandonCaptureAfterError(exception); }
        }

        internal static void BeforeAddFrontierEntity(Mesh mesh)
        {
            CaptureSession capture = _capture;
            if (!_insideBuilderAdvance || capture == null || mesh == null) return;
            try
            {
                EntityGeometry entity = capture.TakeMesh(mesh);
                if (entity != null && entity.Triangles.Count > 0) capture.FrontierEntities.Add(entity);
            }
            catch (Exception exception) { AbandonCaptureAfterError(exception); }
        }

        internal static void BeforeFrontierSegment(Vec2 first, Vec2 second, out SegmentCapture __state)
        {
            __state = null;
            if (!_insideBuilderAdvance || _capture == null) return;
            __state = new SegmentCapture(first, second);
            _segmentCapture = __state;
        }

        internal static void AfterFrontierPoint(Vec2 point, ref Vec3 terrainPoint, bool __result)
        {
            SegmentCapture segment = _segmentCapture;
            if (segment == null || !__result || segment.CenterPoints.Count >= 2) return;
            segment.CenterPoints.Add(terrainPoint);
        }

        internal static void AfterFrontierSegment(bool __result, SegmentCapture __state)
        {
            if (__state != null && __result && _capture != null)
            {
                uint leftColor = __state.Colors.Count > 0 ? __state.Colors[0] : 0xFFFFFFFFu;
                uint rightColor = __state.Colors.Count > 4 ? __state.Colors[4] : leftColor;
                Vec3 firstTerrainPoint = __state.CenterPoints.Count > 0
                    ? __state.CenterPoints[0] : new Vec3(__state.First.x, __state.First.y, 0f, -1f);
                Vec3 secondTerrainPoint = __state.CenterPoints.Count > 1
                    ? __state.CenterPoints[1] : new Vec3(__state.Second.x, __state.Second.y, firstTerrainPoint.z, -1f);
                _capture.Segments.Add(new RawSegment(
                    __state.First,
                    __state.Second,
                    firstTerrainPoint,
                    secondTerrainPoint,
                    leftColor,
                    rightColor));
            }
            _segmentCapture = null;
        }

        internal static void Replay(
            CachedGeometry geometry,
            object scene,
            out List<GameEntity> fillEntities,
            out List<GameEntity> frontierEntities)
        {
            Scene nativeScene = scene as Scene;
            if (nativeScene == null) throw new InvalidOperationException("Campaign scene is unavailable for cached geometry replay.");
            Stopwatch timer = Stopwatch.StartNew();
            fillEntities = CreateEntities(
                nativeScene, geometry.FillEntities, FillRenderOrder, FillReplayLift,
                FillMaterial, false, false);
            frontierEntities = CreateEntities(
                nativeScene, geometry.FrontierEntities, FrontierRenderOrder,
                FrontierReplayLift, FrontierMaterial, false, true);
            BorderOptimizerDiagnostics.Info("Persistent political geometry replay completed: fillEntities="
                + fillEntities.Count + "; frontierEntities=" + frontierEntities.Count
                + "; triangles=" + geometry.TriangleCount
                + "; fillLift=" + FillReplayLift.ToString("F1", CultureInfo.InvariantCulture)
                + "; frontierLift=" + FrontierReplayLift.ToString("F1", CultureInfo.InvariantCulture)
                + "; frontierMaterial=" + FrontierMaterial
                + "; frontierExcludeParents=false"
                + "; frontierNoDepthOverlay=true"
                + "; frontierRenderOrder=" + FrontierRenderOrder
                + "; wallMilliseconds=" + timer.ElapsedMilliseconds + ".");
        }

        private static List<GameEntity> CreateEntities(
            Scene scene,
            IList<EntityGeometry> records,
            int renderOrder,
            float verticalLift,
            string materialName,
            bool visibilityExcludeParents,
            bool noDepthOverlay)
        {
            List<GameEntity> entities = new List<GameEntity>(records.Count);
            try
            {
                Material overlayMaterial = noDepthOverlay
                    ? CreateFrontierOverlayMaterial(materialName) : null;
                foreach (EntityGeometry record in records)
                {
                    MeshBuilder builder = new MeshBuilder();
                    Vec3 normal = new Vec3(0f, 0f, 1f);
                    foreach (Triangle triangle in record.Triangles)
                    {
                        Vec3 firstPoint = triangle.First;
                        Vec3 secondPoint = triangle.Second;
                        Vec3 thirdPoint = triangle.Third;
                        firstPoint.z += verticalLift;
                        secondPoint.z += verticalLift;
                        thirdPoint.z += verticalLift;
                        int first = builder.AddFaceCorner(firstPoint, normal, triangle.FirstUv, triangle.Color);
                        int second = builder.AddFaceCorner(secondPoint, normal, triangle.SecondUv, triangle.Color);
                        int third = builder.AddFaceCorner(thirdPoint, normal, triangle.ThirdUv, triangle.Color);
                        builder.AddFace(first, second, third);
                    }
                    Mesh mesh = builder.Finalize();
                    if (mesh == null) throw new InvalidOperationException("Bannerlord could not finalize cached political geometry.");
                    if (overlayMaterial == null) mesh.SetMaterial(materialName);
                    else mesh.SetMaterial(overlayMaterial);
                    mesh.SetMeshRenderOrder(renderOrder);
                    mesh.RecomputeBoundingBox();
                    GameEntity entity = GameEntity.CreateEmpty(scene, false, true, true);
                    if (entity == null) throw new InvalidOperationException("Bannerlord could not create a cached political entity.");
                    entity.SetGlobalFrame(MatrixFrame.Identity, true);
                    entity.AddMesh(mesh, true);
                    entity.SetForceDecalsToRender(false);
                    entity.SetVisibilityExcludeParents(visibilityExcludeParents);
                    entity.SetReadyToRender(true);
                    entity.SetAlpha(0f);
                    entities.Add(entity);
                }
                return entities;
            }
            catch
            {
                foreach (GameEntity entity in entities)
                    if (entity != null) entity.Remove(0);
                throw;
            }
        }

        /// <summary>
        /// Creates a sidecar-owned copy of the opaque vertex-colour material.
        /// The native material and protected renderer remain untouched. The
        /// copy renders late without testing or writing scene depth so terrain,
        /// water, objects, and the political fill cannot occlude the ribbon.
        /// </summary>
        private static Material CreateFrontierOverlayMaterial(string materialName)
        {
            Material source = Material.GetFromResource(materialName);
            if (source == null)
                throw new InvalidOperationException(
                    "The frontier overlay source material is unavailable.");
            Material overlay = source.CreateCopy();
            if (overlay == null)
                throw new InvalidOperationException(
                    "Bannerlord could not copy the frontier overlay material.");
            overlay.Flags = overlay.Flags | FrontierOverlayFlags;
            return overlay;
        }

        private static void WriteCache(CacheIdentity identity, CachedGeometry geometry)
        {
            try
            {
                string directory = IOPath.GetDirectoryName(identity.Path);
                Directory.CreateDirectory(directory);
                string temporary = identity.Path + ".tmp-" + Guid.NewGuid().ToString("N");
                try
                {
                    using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    using (GZipStream gzip = new GZipStream(stream, CompressionLevel.Optimal))
                    using (BinaryWriter writer = new BinaryWriter(gzip, Encoding.UTF8))
                    {
                        writer.Write(Magic);
                        writer.Write(FormatVersion);
                        writer.Write(identity.Key);
                        WriteEntities(writer, geometry.FillEntities);
                        WriteEntities(writer, geometry.FrontierEntities);
                        PoliticalBorderGeometryDiagnostics.Write(writer, geometry.Diagnostics);
                        writer.Write((byte)0xA5);
                    }
                    if (File.Exists(identity.Path)) File.Delete(identity.Path);
                    File.Move(temporary, identity.Path);
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
                BorderOptimizerDiagnostics.Info("Persistent political geometry cache written: key="
                    + identity.Key.Substring(0, 12) + "; bytes=" + new FileInfo(identity.Path).Length
                    + "; triangles=" + geometry.TriangleCount + ".");
            }
            catch (Exception exception)
            {
                BorderOptimizerDiagnostics.Error(
                    "Persistent political geometry cache could not be written; rendered geometry remains active.",
                    exception);
            }
        }

        private static void WriteEntities(BinaryWriter writer, IList<EntityGeometry> entities)
        {
            writer.Write(entities.Count);
            foreach (EntityGeometry entity in entities)
            {
                writer.Write(entity.Triangles.Count);
                foreach (Triangle triangle in entity.Triangles) WriteTriangle(writer, triangle);
            }
        }

        private static List<EntityGeometry> ReadEntities(BinaryReader reader, int maximumEntities)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > maximumEntities) throw new InvalidDataException("Cached entity count is invalid.");
            List<EntityGeometry> entities = new List<EntityGeometry>(count);
            int totalTriangles = 0;
            for (int entityIndex = 0; entityIndex < count; entityIndex++)
            {
                int triangleCount = reader.ReadInt32();
                totalTriangles += triangleCount;
                if (triangleCount <= 0 || totalTriangles > MaximumTriangles)
                    throw new InvalidDataException("Cached triangle count is invalid.");
                EntityGeometry entity = new EntityGeometry();
                entity.Triangles.Capacity = triangleCount;
                for (int index = 0; index < triangleCount; index++) entity.Triangles.Add(ReadTriangle(reader));
                entities.Add(entity);
            }
            return entities;
        }

        private static void WriteTriangle(BinaryWriter writer, Triangle value)
        {
            WriteVec3(writer, value.First); WriteVec3(writer, value.Second); WriteVec3(writer, value.Third);
            WriteVec2(writer, value.FirstUv); WriteVec2(writer, value.SecondUv); WriteVec2(writer, value.ThirdUv);
            writer.Write(value.Color);
        }

        private static Triangle ReadTriangle(BinaryReader reader)
        {
            Triangle value = new Triangle(
                ReadVec3(reader), ReadVec3(reader), ReadVec3(reader),
                ReadVec2(reader), ReadVec2(reader), ReadVec2(reader), reader.ReadUInt32());
            if (!IsFinite(value.First) || !IsFinite(value.Second) || !IsFinite(value.Third))
                throw new InvalidDataException("Cached political vertex is not finite.");
            return value;
        }

        private static void WriteVec3(BinaryWriter writer, Vec3 value)
        { writer.Write(value.x); writer.Write(value.y); writer.Write(value.z); }
        private static Vec3 ReadVec3(BinaryReader reader)
        { return new Vec3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()); }
        private static void WriteVec2(BinaryWriter writer, Vec2 value)
        { writer.Write(value.x); writer.Write(value.y); }
        private static Vec2 ReadVec2(BinaryReader reader)
        { return new Vec2(reader.ReadSingle(), reader.ReadSingle()); }
        private static bool IsFinite(Vec3 value)
        { return !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z); }

        private static void AbandonCaptureAfterError(Exception exception)
        {
            AbandonColdCapture();
            BorderOptimizerDiagnostics.Error(
                "Persistent geometry capture failed; approved rendering continues without caching.",
                exception);
        }

        private static string InvokeSceneName(object scene)
        {
            MethodInfo method = scene.GetType().GetMethod("GetName", BindingFlags.Instance | BindingFlags.Public);
            return method == null ? scene.GetType().FullName : Convert.ToString(method.Invoke(scene, null), CultureInfo.InvariantCulture);
        }

        private static PropertyInfo RequireProperty(Type type, string name)
        {
            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null) throw new MissingMemberException(type.FullName, name);
            return property;
        }

        private static int FloatBits(float value) { return BitConverter.ToInt32(BitConverter.GetBytes(value), 0); }

        internal sealed class CacheIdentity
        {
            internal CacheIdentity(
                string key,
                string path,
                List<TerritorySite> sites,
                float minimumX,
                float minimumY,
                float maximumX,
                float maximumY)
            {
                Key = key;
                Path = path;
                Sites = sites;
                MinimumX = minimumX;
                MinimumY = minimumY;
                MaximumX = maximumX;
                MaximumY = maximumY;
            }
            internal string Key { get; private set; }
            internal string Path { get; private set; }
            internal List<TerritorySite> Sites { get; private set; }
            internal float MinimumX { get; private set; }
            internal float MinimumY { get; private set; }
            internal float MaximumX { get; private set; }
            internal float MaximumY { get; private set; }
        }

        internal struct TerritorySite
        {
            internal TerritorySite(Vec2 site, string ownerKey, uint color)
            { Site = site; OwnerKey = ownerKey ?? string.Empty; Color = color; }
            internal Vec2 Site;
            internal string OwnerKey;
            internal uint Color;
        }

        internal sealed class CachedGeometry
        {
            internal CachedGeometry(
                List<EntityGeometry> fill,
                List<EntityGeometry> frontier,
                PoliticalGeometryDiagnosticsRecord diagnostics)
            { FillEntities = fill; FrontierEntities = frontier; Diagnostics = diagnostics; }
            internal List<EntityGeometry> FillEntities { get; private set; }
            internal List<EntityGeometry> FrontierEntities { get; private set; }
            internal PoliticalGeometryDiagnosticsRecord Diagnostics { get; private set; }
            internal int TriangleCount
            {
                get
                {
                    int count = 0;
                    foreach (EntityGeometry entity in FillEntities) count += entity.Triangles.Count;
                    foreach (EntityGeometry entity in FrontierEntities) count += entity.Triangles.Count;
                    return count;
                }
            }
        }

        internal sealed class EntityGeometry
        {
            internal readonly List<Triangle> Triangles = new List<Triangle>();
        }

        internal struct Triangle
        {
            internal Triangle(Vec3 first, Vec3 second, Vec3 third, Vec2 firstUv, Vec2 secondUv, Vec2 thirdUv, uint color)
            { First = first; Second = second; Third = third; FirstUv = firstUv; SecondUv = secondUv; ThirdUv = thirdUv; Color = color; }
            internal Vec3 First, Second, Third;
            internal Vec2 FirstUv, SecondUv, ThirdUv;
            internal uint Color;
        }

        internal sealed class SegmentCapture
        {
            internal SegmentCapture(Vec2 first, Vec2 second) { First = first; Second = second; }
            internal Vec2 First { get; private set; }
            internal Vec2 Second { get; private set; }
            internal readonly List<Vec3> CenterPoints = new List<Vec3>(2);
            internal readonly List<uint> Colors = new List<uint>(32);
        }

        private sealed class CaptureSession
        {
            private readonly Dictionary<Mesh, EntityGeometry> _meshes = new Dictionary<Mesh, EntityGeometry>();
            internal CaptureSession(CacheIdentity identity) { Identity = identity; }
            internal CacheIdentity Identity { get; private set; }
            internal readonly List<EntityGeometry> FillEntities = new List<EntityGeometry>();
            internal readonly List<EntityGeometry> FrontierEntities = new List<EntityGeometry>();
            internal readonly List<RawSegment> Segments = new List<RawSegment>();
            internal EntityGeometry GetMesh(Mesh mesh)
            {
                EntityGeometry value;
                if (!_meshes.TryGetValue(mesh, out value)) { value = new EntityGeometry(); _meshes.Add(mesh, value); }
                return value;
            }
            internal EntityGeometry TakeMesh(Mesh mesh)
            {
                EntityGeometry value;
                if (!_meshes.TryGetValue(mesh, out value)) return null;
                _meshes.Remove(mesh);
                return value;
            }
        }

        internal struct RawSegment
        {
            internal RawSegment(
                Vec2 first,
                Vec2 second,
                Vec3 firstTerrainPoint,
                Vec3 secondTerrainPoint,
                uint leftColor,
                uint rightColor,
                BorderLineStyle style = BorderLineStyle.Solid)
            {
                First = first;
                Second = second;
                FirstTerrainPoint = firstTerrainPoint;
                SecondTerrainPoint = secondTerrainPoint;
                LeftColor = leftColor;
                RightColor = rightColor;
                Style = style;
            }
            internal Vec2 First, Second;
            internal Vec3 FirstTerrainPoint, SecondTerrainPoint;
            internal uint LeftColor, RightColor;
            internal BorderLineStyle Style;
        }

        internal enum BorderLineStyle : byte { Solid, Dashed, Double }

        internal struct NodeKey : IEquatable<NodeKey>
        {
            internal NodeKey(Vec2 point) { X = (int)Math.Round(point.x * 1000f); Y = (int)Math.Round(point.y * 1000f); }
            private int X, Y;
            internal int CompareTo(NodeKey other)
            { int x = X.CompareTo(other.X); return x != 0 ? x : Y.CompareTo(other.Y); }
            public bool Equals(NodeKey other) { return X == other.X && Y == other.Y; }
            public override bool Equals(object obj) { return obj is NodeKey && Equals((NodeKey)obj); }
            public override int GetHashCode() { unchecked { return (X * 397) ^ Y; } }
        }

    }
}
