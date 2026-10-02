using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using AgesOfCalradia.PoliticalBorderOverrides;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace AgesOfCalradia.PoliticalBorderEditor
{
    /// <summary>
    /// Projects the registered 1672x941 province-line PNG onto campaign terrain
    /// and exposes the same bounded pixel graph to the TRACE editor tool.
    /// Calibration is obtained from the approved renderer's settlement-fitted
    /// campaign-to-reference transform; reflection or image failure disables
    /// only this optional guide and is recorded in editor diagnostics.
    /// </summary>
    internal static class PoliticalBorderReferenceOverlay
    {
        private const string AssetName =
            "strategic_map_game_overlay_darkred_borders_1672x941.png";
        private const int ExpectedWidth = 1672;
        private const int ExpectedHeight = 941;
        private const int CellPixels = 6;
        private const int TerrainSamplesPerFrame = 160;
        private const int MaximumSegments = 12000;
        private const float RegisteredLeft = 411f;
        private const float RegisteredTop = 67f;
        private const float RegisteredWidth = 702f;
        private const float RegisteredHeight = 702f;
        private const float StrategicSourceWidth = 1730f;
        private const float StrategicSourceHeight = 1720f;
        private const float StrategicCropLeft = 80f;
        private const float StrategicCropTop = 90f;
        private const float OverlayLift = 0.10f;
        private const int OverlayRenderOrder = 91;
        private const float MaximumSnapDistance = 14f;
        private const uint OverlayColor = 0xFF650B12u;

        private static readonly List<Node> Nodes = new List<Node>();
        private static readonly List<Edge> Edges = new List<Edge>();
        private static bool _visible;
        private static bool _initializationAttempted;
        private static bool _ready;
        private static string _failure = string.Empty;
        private static int _nextTerrainNode;
        private static int _previewStart = -1;
        private static int _previewEnd = -1;
        private static IList<Vec2> _previewRoute;
        private static GameEntity _entity;
        private static Scene _entityScene;
        private static bool _sceneBuildFailed;

        internal static string StatusText
        {
            get
            {
                if (!_visible) return "REFERENCE OVERLAY: HIDDEN";
                if (!_ready && _initializationAttempted)
                    return "REFERENCE OVERLAY: UNAVAILABLE";
                return _nextTerrainNode < Nodes.Count
                    ? "REFERENCE OVERLAY: PREPARING" : "REFERENCE OVERLAY: VISIBLE";
            }
        }

        internal static void Toggle()
        {
            SetVisible(!_visible);
        }

        internal static void SetVisible(bool visible)
        {
            _visible = visible;
            if (_visible)
            {
                _sceneBuildFailed = false;
                EnsureInitialized();
            }
            else ClearSceneEntity();
        }

        internal static void Render()
        {
            if (!_visible || _sceneBuildFailed || !EnsureInitialized()) return;
            try
            {
            AdvanceTerrainPreparation();
            if (_nextTerrainNode < Nodes.Count) return;
            Scene scene = PoliticalBorderEditorScenePreview.CurrentScene;
            if (scene == null) return;
            if (_entity != null && ReferenceEquals(_entityScene, scene)) return;
            ClearSceneEntity();
            MeshBuilder builder = new MeshBuilder();
            int corners = 0;
            int renderedEdges = 0;
            foreach (Edge edge in Edges)
            {
                if (!edge.Render) continue;
                Node first = Nodes[edge.First];
                Node second = Nodes[edge.Second];
                if (!first.TerrainReady || !second.TerrainReady) continue;
                PoliticalBorderEditorScenePreview.AddPersistentLine(
                    builder, ref corners, first.TerrainPoint,
                    second.TerrainPoint, OverlayColor, 0.38f);
                renderedEdges++;
            }
            _entity = PoliticalBorderEditorScenePreview.CreatePersistentEntity(
                builder, corners, OverlayRenderOrder);
            _entityScene = scene;
            PoliticalBorderEditorDiagnostics.Info(
                "Production reference-overlay mesh published: segments="
                + renderedEdges + "; routingEdges=" + Edges.Count
                + "; corners=" + corners + "; renderOrder="
                + OverlayRenderOrder + ".");
            }
            catch (Exception exception)
            {
                ClearSceneEntity();
                _sceneBuildFailed = true;
                PoliticalBorderEditorDiagnostics.Error(
                    "Production reference-overlay mesh failed; TRACE graph data remains available.",
                    exception);
            }
        }

        internal static void ClearSceneEntity()
        {
            if (_entity != null) _entity.Remove(0);
            _entity = null;
            _entityScene = null;
        }

        internal static bool TrySnap(Vec2 point, out Vec2 snapped)
        {
            snapped = point;
            if (!EnsureInitialized()) return false;
            int node = FindNearestNode(point);
            if (node < 0) return false;
            snapped = Nodes[node].Position;
            return true;
        }

        internal static bool TryBuildRoute(
            Vec2 first,
            Vec2 second,
            out List<Vec2> route,
            out string failure)
        {
            route = null;
            failure = string.Empty;
            if (!EnsureInitialized())
            {
                failure = string.IsNullOrEmpty(_failure)
                    ? "reference overlay is unavailable" : _failure;
                return false;
            }
            int start = FindNearestNode(first);
            int end = FindNearestNode(second);
            if (start < 0 || end < 0)
            {
                failure = "an anchor is too far from the red reference line";
                return false;
            }
            route = BuildRoute(start, end);
            if (route == null)
            {
                failure = "the selected red lines belong to disconnected components";
                return false;
            }
            return true;
        }

        internal static bool TryGetPreviewRoute(
            Vec2 first,
            Vec2 second,
            out IList<Vec2> route)
        {
            route = null;
            if (!EnsureInitialized()) return false;
            int start = FindNearestNode(first);
            int end = FindNearestNode(second);
            if (start < 0 || end < 0) return false;
            if (start != _previewStart || end != _previewEnd)
            {
                _previewStart = start;
                _previewEnd = end;
                _previewRoute = BuildRoute(start, end);
            }
            route = _previewRoute;
            return route != null;
        }

        private static bool EnsureInitialized()
        {
            if (_ready) return true;
            if (_initializationAttempted) return false;
            _initializationAttempted = true;
            try
            {
                double[] projectionX;
                double[] projectionY;
                ResolveProjection(out projectionX, out projectionY);
                string path = System.IO.Path.Combine(
                    PoliticalBorderEditorDocument.ResolveModuleRoot(), "GUI",
                    "SpriteParts", "ui_world_calendar", AssetName);
                BuildGraph(path, projectionX, projectionY);
                if (Nodes.Count < 2 || Edges.Count < 1)
                    throw new InvalidDataException(
                        "The registered reference overlay contains no usable red-line graph.");
                _ready = true;
                PoliticalBorderEditorDiagnostics.Info(
                    "Terrain reference overlay prepared: asset=" + AssetName
                    + "; nodes=" + Nodes.Count + "; segments=" + Edges.Count
                    + "; registeredAperture=411,67+702x702; terrainReady=false.");
                return true;
            }
            catch (Exception exception)
            {
                _failure = exception.Message;
                Nodes.Clear();
                Edges.Clear();
                PoliticalBorderEditorDiagnostics.Error(
                    "Terrain reference overlay initialization failed; ordinary editor tools remain active.",
                    exception);
                return false;
            }
        }

        private static void ResolveProjection(
            out double[] projectionX,
            out double[] projectionY)
        {
            Type ledger = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                ledger = assembly.GetType(
                    "TwelveMonthCalendar.CalendarWorldLedgerVM", false);
                if (ledger != null) break;
            }
            if (ledger == null)
                throw new TypeLoadException(
                    "The approved campaign-to-reference calibration owner is unavailable.");
            MethodInfo method = ledger.GetMethod(
                "TryGetCampaignToReferenceProjection",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null)
                throw new MissingMethodException(
                    ledger.FullName, "TryGetCampaignToReferenceProjection");
            object[] arguments = { null, null };
            if (!(bool)method.Invoke(null, arguments))
                throw new InvalidOperationException(
                    "The settlement-fitted campaign-map calibration is not ready.");
            projectionX = arguments[0] as double[];
            projectionY = arguments[1] as double[];
            if (projectionX == null || projectionX.Length != 3
                || projectionY == null || projectionY.Length != 3)
                throw new InvalidDataException(
                    "The campaign-map calibration coefficients are invalid.");
        }

        internal static bool TryResolveProjection(
            out double[] projectionX,
            out double[] projectionY)
        {
            try
            {
                ResolveProjection(out projectionX, out projectionY);
                return true;
            }
            catch (Exception exception)
            {
                projectionX = null;
                projectionY = null;
                PoliticalBorderEditorDiagnostics.Error(
                    "Optional external-editor terrain calibration is not ready.",
                    exception);
                return false;
            }
        }

        private static void BuildGraph(
            string path,
            double[] projectionX,
            double[] projectionY)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    "The registered reference overlay PNG is missing.", path);
            using (Bitmap bitmap = new Bitmap(path))
            {
                if (bitmap.Width != ExpectedWidth || bitmap.Height != ExpectedHeight)
                    throw new InvalidDataException(
                        "The registered reference overlay must be 1672x941 pixels.");
                Rectangle bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
                BitmapData data = bitmap.LockBits(
                    bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                byte[] pixels = new byte[Math.Abs(data.Stride) * data.Height];
                try { Marshal.Copy(data.Scan0, pixels, 0, pixels.Length); }
                finally { bitmap.UnlockBits(data); }
                BuildGraphFromPixels(pixels, data.Stride, bitmap.Width, bitmap.Height,
                    projectionX, projectionY);
            }
        }

        private static void BuildGraphFromPixels(
            byte[] pixels,
            int stride,
            int width,
            int height,
            double[] projectionX,
            double[] projectionY)
        {
            int columns = (width + CellPixels - 1) / CellPixels;
            int rows = (height + CellPixels - 1) / CellPixels;
            int[] cells = new int[columns * rows];
            for (int index = 0; index < cells.Length; index++) cells[index] = -1;
            for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
            {
                int totalX = 0, totalY = 0, count = 0;
                int endX = Math.Min(width, (column + 1) * CellPixels);
                int endY = Math.Min(height, (row + 1) * CellPixels);
                for (int y = row * CellPixels; y < endY; y++)
                for (int x = column * CellPixels; x < endX; x++)
                {
                    int rowStart = stride >= 0 ? y * stride
                        : (height - 1 - y) * -stride;
                    int pixel = rowStart + x * 4;
                    byte blue = pixels[pixel];
                    byte green = pixels[pixel + 1];
                    byte red = pixels[pixel + 2];
                    byte alpha = pixels[pixel + 3];
                    if (alpha <= 16 || red <= green + 10 || red <= blue + 10)
                        continue;
                    totalX += x;
                    totalY += y;
                    count++;
                }
                if (count == 0) continue;
                float pixelX = totalX / (float)count;
                float pixelY = totalY / (float)count;
                Vec2 campaign = Unproject(pixelX, pixelY, projectionX, projectionY);
                cells[row * columns + column] = Nodes.Count;
                Nodes.Add(new Node(campaign, new Vec2(pixelX, pixelY)));
            }

            int[,] directions = { { 1, 0 }, { 0, 1 }, { 1, 1 }, { -1, 1 } };
            for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
            {
                int first = cells[row * columns + column];
                if (first < 0) continue;
                for (int direction = 0; direction < directions.GetLength(0); direction++)
                {
                    int nextX = column + directions[direction, 0];
                    int nextY = row + directions[direction, 1];
                    if (nextX < 0 || nextY < 0 || nextX >= columns || nextY >= rows)
                        continue;
                    int second = cells[nextY * columns + nextX];
                    if (second < 0 || Edges.Count >= MaximumSegments) continue;
                    bool diagonal = directions[direction, 0] != 0
                        && directions[direction, 1] != 0;
                    bool hasOrthogonalContinuation = diagonal
                        && (cells[row * columns + nextX] >= 0
                            || cells[nextY * columns + column] >= 0);
                    bool followsSourceLine = HasSourceLineSupport(
                        pixels, stride, width, height,
                        Nodes[first].PixelPosition,
                        Nodes[second].PixelPosition);
                    Edges.Add(new Edge(
                        first, second,
                        !hasOrthogonalContinuation && followsSourceLine));
                    Nodes[first].Neighbors.Add(second);
                    Nodes[second].Neighbors.Add(first);
                }
            }
        }

        private static bool HasSourceLineSupport(
            byte[] pixels,
            int stride,
            int width,
            int height,
            Vec2 first,
            Vec2 second)
        {
            float length = (second - first).Length;
            int samples = Math.Max(3, (int)Math.Ceiling(length));
            int supported = 0;
            for (int sample = 0; sample <= samples; sample++)
            {
                float amount = sample / (float)samples;
                Vec2 point = first + (second - first) * amount;
                bool found = false;
                int centerX = (int)Math.Round(point.x);
                int centerY = (int)Math.Round(point.y);
                for (int y = centerY - 1; y <= centerY + 1 && !found; y++)
                for (int x = centerX - 1; x <= centerX + 1; x++)
                {
                    if (IsReferencePixel(pixels, stride, width, height, x, y))
                    {
                        found = true;
                        break;
                    }
                }
                if (found) supported++;
            }
            return supported >= (int)Math.Ceiling((samples + 1) * 0.70f);
        }

        private static bool IsReferencePixel(
            byte[] pixels,
            int stride,
            int width,
            int height,
            int x,
            int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return false;
            int rowStart = stride >= 0 ? y * stride
                : (height - 1 - y) * -stride;
            int pixel = rowStart + x * 4;
            byte blue = pixels[pixel];
            byte green = pixels[pixel + 1];
            byte red = pixels[pixel + 2];
            byte alpha = pixels[pixel + 3];
            return alpha > 16 && red > green + 10 && red > blue + 10;
        }

        private static Vec2 Unproject(
            float pixelX,
            float pixelY,
            double[] projectionX,
            double[] projectionY)
        {
            double referenceX = (pixelX - RegisteredLeft)
                * StrategicSourceWidth / RegisteredWidth + StrategicCropLeft;
            double referenceY = (pixelY - RegisteredTop)
                * StrategicSourceHeight / RegisteredHeight + StrategicCropTop;
            double x = referenceX - projectionX[2];
            double y = referenceY - projectionY[2];
            double determinant = projectionX[0] * projectionY[1]
                - projectionX[1] * projectionY[0];
            if (Math.Abs(determinant) < 0.0000001d)
                throw new InvalidDataException(
                    "The campaign-map calibration transform is singular.");
            return new Vec2(
                (float)((x * projectionY[1] - projectionX[1] * y) / determinant),
                (float)((projectionX[0] * y - x * projectionY[0]) / determinant));
        }

        private static void AdvanceTerrainPreparation()
        {
            int end = Math.Min(Nodes.Count,
                _nextTerrainNode + TerrainSamplesPerFrame);
            for (; _nextTerrainNode < end; _nextTerrainNode++)
            {
                Node node = Nodes[_nextTerrainNode];
                float height = 0f;
                Campaign campaign = Campaign.Current;
                if (campaign != null && campaign.MapSceneWrapper != null)
                {
                    CampaignVec2 point = new CampaignVec2(node.Position, false);
                    campaign.MapSceneWrapper.GetHeightAtPoint(point, ref height);
                }
                node.TerrainPoint = new Vec3(
                    node.Position.x, node.Position.y, height + OverlayLift, -1f);
                node.TerrainReady = true;
            }
        }

        private static int FindNearestNode(Vec2 point)
        {
            float best = MaximumSnapDistance * MaximumSnapDistance;
            int result = -1;
            for (int index = 0; index < Nodes.Count; index++)
            {
                float distance = (Nodes[index].Position - point).LengthSquared;
                if (distance >= best) continue;
                best = distance;
                result = index;
            }
            return result;
        }

        private static List<Vec2> BuildRoute(int start, int end)
        {
            int[] previous = new int[Nodes.Count];
            for (int index = 0; index < previous.Length; index++) previous[index] = -1;
            Queue<int> pending = new Queue<int>();
            pending.Enqueue(start);
            previous[start] = start;
            while (pending.Count > 0)
            {
                int current = pending.Dequeue();
                if (current == end) break;
                foreach (int neighbor in Nodes[current].Neighbors)
                {
                    if (previous[neighbor] >= 0) continue;
                    previous[neighbor] = current;
                    pending.Enqueue(neighbor);
                }
            }
            if (previous[end] < 0) return null;
            List<Vec2> reversed = new List<Vec2>();
            for (int current = end; current != start; current = previous[current])
                reversed.Add(Nodes[current].Position);
            reversed.Add(Nodes[start].Position);
            reversed.Reverse();
            return reversed;
        }

        private sealed class Node
        {
            internal Node(Vec2 position, Vec2 pixelPosition)
            {
                Position = position;
                PixelPosition = pixelPosition;
            }
            internal readonly Vec2 Position;
            internal readonly Vec2 PixelPosition;
            internal readonly List<int> Neighbors = new List<int>(8);
            internal Vec3 TerrainPoint;
            internal bool TerrainReady;
        }

        private struct Edge
        {
            internal Edge(int first, int second, bool render)
            {
                First = first;
                Second = second;
                Render = render;
            }
            internal readonly int First, Second;
            internal readonly bool Render;
        }
    }
}
