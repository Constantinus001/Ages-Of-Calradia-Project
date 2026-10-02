using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AgesOfCalradia.PoliticalBorderOptimizer
{
    /// <summary>
    /// Fail-open optimization sidecar for the immutable approved political
    /// renderer. It replaces repeated exact native probes only after that
    /// renderer has finished preparing its scene-bound terrain snapshots.
    /// </summary>
    public sealed class PoliticalBorderOptimizerSubModule : MBSubModuleBase
    {
        private const string HarmonyId = "AgesOfCalradia.PoliticalBorderOptimizer";
        private const string ApprovedRendererSha256 =
            "560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E";
        // SandBox.View v1.4.8 owns the final map-scene readiness gate. The
        // published layout is prepared in a prefix here so it cannot briefly
        // present the generated frontier before its reviewed replacement.
        private const string ApprovedMapScreenSha256 =
            "C7360E71DA06799A0CB6C00C4A0CFAB5877621197BA25101433AC28654D169E2";

        private Harmony _harmony;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            BorderOptimizerDiagnostics.Initialize();
                BorderOptimizerDiagnostics.Info("Political border optimizer v0.10.0 traced coastal frontier restoration entering OnSubModuleLoad.");

            try
            {
                Type gridType = AccessTools.TypeByName(
                    "TwelveMonthCalendar.CampaignMapTerrainGridCache");
                Type builderType = AccessTools.TypeByName(
                    "TwelveMonthCalendar.CampaignPoliticalTerritoryFill+Builder");
                Type behaviorType = AccessTools.TypeByName(
                    "TwelveMonthCalendar.CampaignKingdomBorderBehavior");
                Type overlayType = AccessTools.TypeByName(
                    "TwelveMonthCalendar.CampaignPoliticalOverlayView");
                Type mapScreenType = AccessTools.TypeByName("SandBox.View.Map.MapScreen");
                if (gridType == null || builderType == null || behaviorType == null
                    || overlayType == null || mapScreenType == null)
                {
                    BorderOptimizerDiagnostics.Info(
                        "Optimizer inactive: approved renderer targets were not loaded. Legacy rendering remains unchanged.");
                    return;
                }

                string rendererPath = gridType.Assembly.Location;
                string rendererHash = ComputeSha256(rendererPath);
                if (!string.Equals(rendererHash, ApprovedRendererSha256, StringComparison.OrdinalIgnoreCase))
                {
                    BorderOptimizerDiagnostics.Info(
                        "Optimizer inactive: renderer hash was " + rendererHash
                        + ", expected " + ApprovedRendererSha256
                        + ". Legacy rendering remains unchanged.");
                    return;
                }

                string mapScreenHash = ComputeSha256(mapScreenType.Assembly.Location);
                if (!string.Equals(mapScreenHash, ApprovedMapScreenSha256, StringComparison.OrdinalIgnoreCase))
                {
                    BorderOptimizerDiagnostics.Info(
                        "Optimizer inactive: MapScreen hash was " + mapScreenHash
                        + ", expected " + ApprovedMapScreenSha256
                        + ". Legacy rendering remains unchanged.");
                    return;
                }

                BorderOptimizerRuntime.Bind(gridType, builderType);
                EagerPoliticalBorderWarmup.Bind(behaviorType, overlayType, mapScreenType);
                _harmony = new Harmony(HarmonyId);
                PatchTargets(_harmony, gridType, builderType, behaviorType, mapScreenType);
                PoliticalBorderGeometryCache.PatchTargets(_harmony, builderType);
                BorderOptimizerDiagnostics.Info(
                    "Optimizer active for approved renderer " + rendererHash
                    + ". Exact heights and post-topology terrain probes use the bounded high-resolution snapshot."
                    + " Native faction fill colours, inland ownership borders, and faction-name labels are preserved.");
            }
            catch (Exception exception)
            {
                // Reflection and Harmony cross a version-sensitive mod boundary.
                // Remove partial interception and leave the protected renderer
                // fully responsible for the session.
                BorderOptimizerDiagnostics.Error(
                    "Optimizer activation failed; partial patches were removed and legacy rendering remains active.",
                    exception);
                DisablePatches();
            }
        }

        protected override void OnSubModuleUnloaded()
        {
            BorderOptimizerRuntime.LogSessionSummary("submodule-unload");
            DisablePatches();
            base.OnSubModuleUnloaded();
        }

        private static void PatchTargets(
            Harmony harmony,
            Type gridType,
            Type builderType,
            Type behaviorType,
            Type mapScreenType)
        {
            MethodInfo reset = RequireMethod(gridType, "Reset");
            MethodInfo clear = RequireMethod(gridType, "Clear");
            MethodInfo beginOrAdvance = RequireMethod(gridType, "BeginOrAdvance");
            MethodInfo tryExactHeight = RequireMethod(gridType, "TrySampleExactHeight");
            MethodInfo tryNativeTerrain = RequireMethod(gridType, "TryGetNativeTerrain");
            MethodInfo advance = RequireMethod(builderType, "Advance");
            MethodInfo gameLoadFinished = RequireMethod(behaviorType, "OnGameLoadFinished");
            MethodInfo sessionLaunched = RequireMethod(behaviorType, "OnSessionLaunched");
            MethodInfo mapReady = RequireMethod(mapScreenType, "HandleIfBlockerStatesDisabled");

            harmony.Patch(reset,
                postfix: new HarmonyMethod(typeof(BorderOptimizerPatches), nameof(BorderOptimizerPatches.AfterGridReset)));
            harmony.Patch(clear,
                prefix: new HarmonyMethod(typeof(BorderOptimizerPatches), nameof(BorderOptimizerPatches.BeforeGridClear)));
            harmony.Patch(beginOrAdvance,
                postfix: new HarmonyMethod(typeof(BorderOptimizerPatches), nameof(BorderOptimizerPatches.AfterGridAdvance)));
            harmony.Patch(tryExactHeight,
                prefix: new HarmonyMethod(typeof(BorderOptimizerPatches), nameof(BorderOptimizerPatches.BeforeExactHeight)),
                postfix: new HarmonyMethod(typeof(BorderOptimizerPatches), nameof(BorderOptimizerPatches.AfterExactHeight)));
            harmony.Patch(tryNativeTerrain,
                prefix: new HarmonyMethod(typeof(BorderOptimizerPatches), nameof(BorderOptimizerPatches.BeforeNativeTerrain)),
                postfix: new HarmonyMethod(typeof(BorderOptimizerPatches), nameof(BorderOptimizerPatches.AfterNativeTerrain)));
            harmony.Patch(advance,
                prefix: new HarmonyMethod(typeof(BorderOptimizerPatches), nameof(BorderOptimizerPatches.BeforeBuilderAdvance)),
                postfix: new HarmonyMethod(typeof(BorderOptimizerPatches), nameof(BorderOptimizerPatches.AfterBuilderAdvance)));
            harmony.Patch(gameLoadFinished,
                postfix: new HarmonyMethod(typeof(EagerPoliticalBorderWarmup), nameof(EagerPoliticalBorderWarmup.AfterGameLoadFinished)));
            harmony.Patch(sessionLaunched,
                postfix: new HarmonyMethod(typeof(EagerPoliticalBorderWarmup), nameof(EagerPoliticalBorderWarmup.AfterSessionLaunched)));
            // Prefix only: native readiness state and the three-frame loading
            // window delay remain owned by SandBox.View.
            harmony.Patch(mapReady,
                prefix: new HarmonyMethod(typeof(EagerPoliticalBorderWarmup), nameof(EagerPoliticalBorderWarmup.BeforeMapSceneVisible)),
                transpiler: new HarmonyMethod(typeof(EagerPoliticalBorderWarmup), nameof(EagerPoliticalBorderWarmup.GuardLoadingDismissal)));
            harmony.Patch(RequireMethod(mapScreenType,"OnFinalize"),
                prefix:new HarmonyMethod(typeof(EagerPoliticalBorderWarmup),nameof(EagerPoliticalBorderWarmup.MapLoadingClosed)));
        }

        private static MethodInfo RequireMethod(Type type, string name)
        {
            MethodInfo method = AccessTools.Method(type, name);
            if (method == null)
            {
                throw new MissingMethodException(type.FullName, name);
            }
            return method;
        }

        private static string ComputeSha256(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return "<missing>";
            }
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(stream);
                StringBuilder text = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) text.Append(value.ToString("X2"));
                return text.ToString();
            }
        }

        private void DisablePatches()
        {
            if (_harmony == null) return;
            try
            {
                _harmony.UnpatchAll(HarmonyId);
            }
            catch (Exception exception)
            {
                BorderOptimizerDiagnostics.Error("Optimizer patch cleanup failed.", exception);
            }
            _harmony = null;
        }
    }

    internal static class BorderOptimizerPatches
    {
        internal static void AfterGridReset()
        {
            BorderOptimizerRuntime.CaptureAllocatedGrid();
        }

        internal static void BeforeGridClear()
        {
            BorderOptimizerRuntime.InvalidateGrid("scene-clear");
        }

        internal static void AfterGridAdvance(ref bool __result)
        {
            if (__result && !BorderOptimizerRuntime.AdvancePreparedTopology()) __result = false;
        }

        internal static bool BeforeExactHeight(Vec2 point, ref float height, ref bool __result)
        {
            if (BorderOptimizerRuntime.IsBuildingPreparedTopology) return true;
            bool exactMemoHit = BorderOptimizerRuntime.TryGetPublishedExactHeight(
                point, out height, out __result);
            if (exactMemoHit)
                return false;
            if (BorderOptimizerRuntime.IsPublishedLayoutActive)
                BorderOptimizerRuntime.RecordPublishedExactHeightMemoMiss();
            if (!PublishedExactProbeMemoization<float>.ShouldUsePreparedProbe(
                BorderOptimizerRuntime.IsPublishedLayoutActive, exactMemoHit)) return true;
            if (!BorderOptimizerRuntime.TrySamplePreparedHeight(point, out height))
            {
                BorderOptimizerRuntime.RecordExactHeightFallback();
                return true;
            }
            BorderOptimizerRuntime.RecordPreparedHeightHit();
            __result = true;
            return false;
        }

        internal static void AfterExactHeight(Vec2 point, float height, bool __result)
        {
            BorderOptimizerRuntime.RecordPublishedExactHeight(point, height, __result);
        }

        internal static bool BeforeNativeTerrain(Vec2 point, ref TerrainType terrain, ref bool __result)
        {
            if (BorderOptimizerRuntime.IsBuildingPreparedTopology) return true;
            bool exactMemoHit = BorderOptimizerRuntime.TryGetPublishedNativeTerrain(
                point, out terrain, out __result);
            if (exactMemoHit)
                return false;
            if (BorderOptimizerRuntime.IsPublishedLayoutActive)
                BorderOptimizerRuntime.RecordPublishedNativeTerrainMemoMiss();
            if (!PublishedExactProbeMemoization<TerrainType>.ShouldUsePreparedProbe(
                BorderOptimizerRuntime.IsPublishedLayoutActive, exactMemoHit)) return true;
            bool hasTerrain;
            if (!BorderOptimizerRuntime.TryResolvePreparedTerrain(point, out terrain, out hasTerrain))
            {
                BorderOptimizerRuntime.RecordNativeTerrainFallback();
                return true;
            }
            if (hasTerrain) BorderOptimizerRuntime.RecordPreparedTerrainHit();
            else BorderOptimizerRuntime.RecordPreparedTerrainInvalidHit();
            __result = hasTerrain;
            return false;
        }

        internal static void AfterNativeTerrain(Vec2 point, TerrainType terrain, bool __result)
        {
            BorderOptimizerRuntime.RecordPublishedNativeTerrain(point, terrain, __result);
        }

        internal static void BeforeBuilderAdvance(out long __state)
        {
            __state = Stopwatch.GetTimestamp();
            PoliticalBorderGeometryCache.BeginBuilderAdvance();
        }

        internal static void AfterBuilderAdvance(object __instance, long __state)
        {
            PoliticalBorderGeometryCache.EndBuilderAdvance();
            BorderOptimizerRuntime.RecordBuilderAdvance(__instance, Stopwatch.GetTimestamp() - __state);
        }
    }

    internal static class BorderOptimizerRuntime
    {
        private delegate bool TryExactHeightDelegate(Vec2 point, out float height);
        private delegate bool TryNativeTerrainDelegate(Vec2 point, out TerrainType terrain);
        private delegate bool IsFrontierLandDelegate(Vec2 point);

        private const int PreparedTopologyColumns = 384;
        private const double PreparedTopologyWorkBudgetMilliseconds = 3d;

        private static readonly object SyncRoot = new object();
        private static MethodInfo _builderIsCompleteGetter;
        private static TryExactHeightDelegate _tryExactHeight;
        private static TryNativeTerrainDelegate _tryNativeTerrain;
        private static IsFrontierLandDelegate _isFrontierLand;
        private static bool _frontierLandClassifierFailed;
        private static FieldInfo _minimumXField;
        private static FieldInfo _minimumYField;
        private static FieldInfo _maximumXField;
        private static FieldInfo _maximumYField;

        private static TerrainType[] _preparedTerrain;
        private static bool[] _preparedTerrainValid;
        private static float[] _preparedHeights;
        private static bool[] _preparedHeightsValid;
        private static int _preparedRows;
        private static int _nextPreparedHeightSample;
        private static int _nextPreparedTerrainSample;
        private static float _minimumX;
        private static float _minimumY;
        private static float _maximumX;
        private static float _maximumY;
        private static float _preparedStepX;
        private static float _preparedStepY;
        private static bool _topologyReady;
        private static bool _topologyFailed;
        private static bool _buildingTopology;
        private static bool _completionLogged;
        private static bool _publishedLayoutActive;
        private static readonly PublishedExactProbeMemoization<float> PublishedExactHeights =
            new PublishedExactProbeMemoization<float>();
        private static readonly PublishedExactProbeMemoization<TerrainType> PublishedNativeTerrain =
            new PublishedExactProbeMemoization<TerrainType>();

        private static long _preparedHeightHits;
        private static long _exactHeightFallbacks;
        private static long _preparedTerrainHits;
        private static long _preparedTerrainInvalidHits;
        private static long _nativeTerrainFallbacks;
        private static long _publishedExactHeightMemoHits;
        private static long _publishedExactHeightMemoMisses;
        private static long _publishedNativeTerrainMemoHits;
        private static long _publishedNativeTerrainMemoMisses;
        private static long _publishedExactProbeCapacityFallbacks;
        private static long _advanceCount;
        private static long _advanceTicks;
        private static long _maximumAdvanceTicks;
        private static long _topologyWorkTicks;
        private static long _maximumTopologySliceTicks;

        internal static bool IsBuildingPreparedTopology { get { return _buildingTopology; } }
        internal static bool IsPublishedLayoutActive { get { return _publishedLayoutActive; } }

        internal static void Bind(Type gridType, Type builderType)
        {
            MethodInfo sampleExactHeight = AccessTools.Method(gridType, "TrySampleExactHeight");
            MethodInfo sampleNativeTerrain = AccessTools.Method(gridType, "TryGetNativeTerrain");
            MethodInfo isFrontierLand = AccessTools.Method(
                gridType, "IsPoliticalLandExact", new[] { typeof(Vec2) });
            PropertyInfo builderIsComplete = AccessTools.Property(builderType, "IsComplete");
            if (sampleExactHeight == null || sampleNativeTerrain == null
                || isFrontierLand == null || builderIsComplete == null)
            {
                throw new MissingMemberException("Required political renderer cache contracts were not found.");
            }

            _tryExactHeight = (TryExactHeightDelegate)Delegate.CreateDelegate(
                typeof(TryExactHeightDelegate), sampleExactHeight);
            _tryNativeTerrain = (TryNativeTerrainDelegate)Delegate.CreateDelegate(
                typeof(TryNativeTerrainDelegate), sampleNativeTerrain);
            _isFrontierLand = (IsFrontierLandDelegate)Delegate.CreateDelegate(
                typeof(IsFrontierLandDelegate), isFrontierLand);
            _frontierLandClassifierFailed = false;
            _builderIsCompleteGetter = builderIsComplete.GetGetMethod(true);
            _minimumXField = RequireField(gridType, "_minX");
            _minimumYField = RequireField(gridType, "_minY");
            _maximumXField = RequireField(gridType, "_maxX");
            _maximumYField = RequireField(gridType, "_maxY");
        }

        internal static void CaptureAllocatedGrid()
        {
            try
            {
                lock (SyncRoot)
                {
                    LogSessionSummaryLocked("grid-reset");
                    _topologyReady = false;
                    _topologyFailed = false;
                    _buildingTopology = false;
                    _publishedLayoutActive = PublishedBorderLayoutPresence.IsPresent();
                    PublishedExactHeights.Clear();
                    PublishedNativeTerrain.Clear();
                    if (_publishedLayoutActive)
                    {
                        _preparedTerrain = null;
                        _preparedTerrainValid = null;
                        _preparedHeights = null;
                        _preparedHeightsValid = null;
                        _preparedRows = 0;
                        _nextPreparedHeightSample = 0;
                        _nextPreparedTerrainSample = 0;
                        BorderOptimizerDiagnostics.Info(
                            "Published Core layout detected: prepared-grid interpolation is disabled; exact native probe memoization will preserve published topology.");
                        return;
                    }
                    _minimumX = (float)_minimumXField.GetValue(null);
                    _minimumY = (float)_minimumYField.GetValue(null);
                    _maximumX = (float)_maximumXField.GetValue(null);
                    _maximumY = (float)_maximumYField.GetValue(null);
                    float width = _maximumX - _minimumX;
                    float height = _maximumY - _minimumY;
                    _preparedRows = width > 0f && height > 0f
                        ? Math.Max(1, (int)Math.Round(PreparedTopologyColumns * height / width))
                        : 0;
                    _preparedStepX = _preparedRows > 0 ? width / PreparedTopologyColumns : 0f;
                    _preparedStepY = _preparedRows > 0 ? height / _preparedRows : 0f;
                    _preparedTerrain = _preparedRows > 0
                        ? new TerrainType[_preparedRows * PreparedTopologyColumns]
                        : null;
                    _preparedTerrainValid = _preparedRows > 0
                        ? new bool[_preparedRows * PreparedTopologyColumns]
                        : null;
                    int heightColumns = PreparedTopologyColumns + 1;
                    int heightRows = _preparedRows + 1;
                    _preparedHeights = _preparedRows > 0
                        ? new float[heightRows * heightColumns]
                        : null;
                    _preparedHeightsValid = _preparedRows > 0
                        ? new bool[heightRows * heightColumns]
                        : null;
                    _nextPreparedHeightSample = 0;
                    _nextPreparedTerrainSample = 0;
                    ResetCountersLocked();
                    BorderOptimizerDiagnostics.Info(
                        "Prepared height/terrain snapshot allocated: columns=" + PreparedTopologyColumns
                        + "; rows=" + _preparedRows
                        + ". Sampling begins after the approved coarse grid is ready.");
                }
            }
            catch (Exception exception)
            {
                // Reflection reads a version-sensitive renderer boundary. Any
                // mismatch leaves original methods active.
                InvalidateGrid("capture-failure");
                BorderOptimizerDiagnostics.Error(
                    "Terrain snapshot capture failed; original probes remain active.", exception);
            }
        }

        internal static bool AdvancePreparedTopology()
        {
            if (_publishedLayoutActive) return true;
            if (_topologyReady || _topologyFailed) return true;
            if (_preparedTerrain == null || _preparedTerrainValid == null
                || _preparedHeights == null || _preparedHeightsValid == null
                || _tryExactHeight == null || _tryNativeTerrain == null)
            {
                _topologyFailed = true;
                BorderOptimizerDiagnostics.Info(
                    "Prepared height/terrain snapshot unavailable; original native probes remain active.");
                return true;
            }

            Stopwatch budget = Stopwatch.StartNew();
            long started = Stopwatch.GetTimestamp();
            try
            {
                _buildingTopology = true;
                while (_nextPreparedHeightSample < _preparedHeights.Length
                    && budget.Elapsed.TotalMilliseconds < PreparedTopologyWorkBudgetMilliseconds)
                {
                    int index = _nextPreparedHeightSample;
                    int heightColumns = PreparedTopologyColumns + 1;
                    int row = index / heightColumns;
                    int column = index % heightColumns;
                    Vec2 point = new Vec2(
                        _minimumX + column * _preparedStepX,
                        _minimumY + row * _preparedStepY);
                    float height;
                    bool valid = _tryExactHeight(point, out height);
                    _preparedHeightsValid[index] = valid;
                    _preparedHeights[index] = valid ? height : 0f;
                    _nextPreparedHeightSample++;
                }

                while (_nextPreparedHeightSample >= _preparedHeights.Length
                    && _nextPreparedTerrainSample < _preparedTerrain.Length
                    && budget.Elapsed.TotalMilliseconds < PreparedTopologyWorkBudgetMilliseconds)
                {
                    int index = _nextPreparedTerrainSample;
                    int row = index / PreparedTopologyColumns;
                    int column = index % PreparedTopologyColumns;
                    Vec2 point = new Vec2(
                        _minimumX + (column + 0.5f) * _preparedStepX,
                        _minimumY + (row + 0.5f) * _preparedStepY);
                    TerrainType terrain;
                    bool valid = _tryNativeTerrain(point, out terrain);
                    _preparedTerrainValid[index] = valid;
                    _preparedTerrain[index] = valid ? terrain : TerrainType.Water;
                    _nextPreparedTerrainSample++;
                }
            }
            catch (Exception exception)
            {
                // The delegate invokes the approved renderer's native campaign
                // face lookup on the main thread. A changed or invalid scene
                // abandons optimization for this grid and resumes legacy work.
                _topologyFailed = true;
                BorderOptimizerDiagnostics.Error(
                    "Prepared terrain sampling failed; original native probes remain active for this scene.",
                    exception);
                return true;
            }
            finally
            {
                _buildingTopology = false;
                long elapsed = Stopwatch.GetTimestamp() - started;
                Interlocked.Add(ref _topologyWorkTicks, elapsed);
                UpdateMaximum(ref _maximumTopologySliceTicks, elapsed);
            }

            if (_nextPreparedHeightSample < _preparedHeights.Length
                || _nextPreparedTerrainSample < _preparedTerrain.Length) return false;
            _topologyReady = true;
            BorderOptimizerDiagnostics.Info(
                "Prepared height/terrain snapshot ready: heightSamples=" + _nextPreparedHeightSample
                + "; terrainSamples=" + _nextPreparedTerrainSample
                + "; workMilliseconds=" + ToMilliseconds(Interlocked.Read(ref _topologyWorkTicks))
                + "; maximumSliceMilliseconds=" + ToMilliseconds(Interlocked.Read(ref _maximumTopologySliceTicks))
                + ". Political mesh construction now uses cached terrain and prepared heights.");
            return true;
        }

        internal static void InvalidateGrid(string reason)
        {
            lock (SyncRoot)
            {
                LogSessionSummaryLocked(reason);
                _topologyReady = false;
                _topologyFailed = false;
                _buildingTopology = false;
                _publishedLayoutActive = false;
                PublishedExactHeights.Clear();
                PublishedNativeTerrain.Clear();
                _preparedTerrain = null;
                _preparedTerrainValid = null;
                _preparedHeights = null;
                _preparedHeightsValid = null;
                _preparedRows = 0;
                _nextPreparedHeightSample = 0;
                _nextPreparedTerrainSample = 0;
                _minimumX = 0f;
                _minimumY = 0f;
                _maximumX = 0f;
                _maximumY = 0f;
                _preparedStepX = 0f;
                _preparedStepY = 0f;
                ResetCountersLocked();
            }
        }

        internal static bool TrySamplePreparedHeight(Vec2 point, out float height)
        {
            height = 0f;
            if (!_topologyReady
                || _preparedHeights == null
                || _preparedHeightsValid == null
                || _preparedRows <= 0
                || _preparedStepX <= 0f
                || _preparedStepY <= 0f
                || point.x < _minimumX
                || point.y < _minimumY
                || point.x > _maximumX
                || point.y > _maximumY)
            {
                return false;
            }

            float gridX = (point.x - _minimumX) / _preparedStepX;
            float gridY = (point.y - _minimumY) / _preparedStepY;
            int column = Math.Min(PreparedTopologyColumns - 1,
                Math.Max(0, (int)Math.Floor(gridX)));
            int row = Math.Min(_preparedRows - 1,
                Math.Max(0, (int)Math.Floor(gridY)));
            float fractionX = Math.Max(0f, Math.Min(1f, gridX - column));
            float fractionY = Math.Max(0f, Math.Min(1f, gridY - row));
            int heightColumns = PreparedTopologyColumns + 1;
            int lowerLeft = row * heightColumns + column;
            int lowerRight = lowerLeft + 1;
            int upperLeft = lowerLeft + heightColumns;
            int upperRight = upperLeft + 1;
            if (!_preparedHeightsValid[lowerLeft]
                || !_preparedHeightsValid[lowerRight]
                || !_preparedHeightsValid[upperLeft]
                || !_preparedHeightsValid[upperRight])
            {
                return false;
            }

            float lower = _preparedHeights[lowerLeft]
                + (_preparedHeights[lowerRight] - _preparedHeights[lowerLeft]) * fractionX;
            float upper = _preparedHeights[upperLeft]
                + (_preparedHeights[upperRight] - _preparedHeights[upperLeft]) * fractionX;
            height = lower + (upper - lower) * fractionY;
            return true;
        }

        internal static bool TryGetPublishedExactHeight(Vec2 point, out float height, out bool success)
        {
            height = 0f;
            success = false;
            bool found = _publishedLayoutActive
                && PublishedExactHeights.TryGet(point.x, point.y, out height, out success);
            if (found) Interlocked.Increment(ref _publishedExactHeightMemoHits);
            return found;
        }

        internal static void RecordPublishedExactHeight(Vec2 point, float height, bool success)
        {
            if (_publishedLayoutActive && !_buildingTopology)
                if (!PublishedExactHeights.Record(point.x, point.y, height, success))
                    Interlocked.Increment(ref _publishedExactProbeCapacityFallbacks);
        }

        internal static void RecordPublishedExactHeightMemoMiss()
        {
            Interlocked.Increment(ref _publishedExactHeightMemoMisses);
        }

        internal static bool TryClassifyFrontierLand(Vec2 point, out bool land)
        {
            land = false;
            if (!_topologyReady || _isFrontierLand == null || _frontierLandClassifierFailed)
                return false;
            try
            {
                land = _isFrontierLand(point);
                return true;
            }
            catch (Exception exception)
            {
                _frontierLandClassifierFailed = true;
                BorderOptimizerDiagnostics.Error(
                    "Protected frontier land classification failed; coastal ribbon classification will fail safely.",
                    exception);
                return false;
            }
        }

        internal static bool TryResolvePreparedTerrain(
            Vec2 point,
            out TerrainType terrain,
            out bool hasTerrain)
        {
            terrain = TerrainType.Water;
            hasTerrain = false;
            if (_buildingTopology) return false;
            if (!_topologyReady
                || _preparedTerrain == null
                || _preparedTerrainValid == null
                || point.x < _minimumX
                || point.y < _minimumY
                || point.x > _maximumX
                || point.y > _maximumY)
            {
                return false;
            }

            int column = Math.Min(PreparedTopologyColumns - 1,
                Math.Max(0, (int)Math.Floor((point.x - _minimumX) / _preparedStepX)));
            int row = Math.Min(_preparedRows - 1,
                Math.Max(0, (int)Math.Floor((point.y - _minimumY) / _preparedStepY)));
            int index = row * PreparedTopologyColumns + column;
            if (index < 0 || index >= _preparedTerrain.Length) return false;
            hasTerrain = _preparedTerrainValid[index];
            if (hasTerrain) terrain = _preparedTerrain[index];
            return true;
        }

        internal static bool TryGetPublishedNativeTerrain(
            Vec2 point,
            out TerrainType terrain,
            out bool success)
        {
            terrain = TerrainType.Water;
            success = false;
            bool found = _publishedLayoutActive
                && PublishedNativeTerrain.TryGet(point.x, point.y, out terrain, out success);
            if (found) Interlocked.Increment(ref _publishedNativeTerrainMemoHits);
            return found;
        }

        internal static void RecordPublishedNativeTerrain(
            Vec2 point,
            TerrainType terrain,
            bool success)
        {
            if (_publishedLayoutActive && !_buildingTopology)
                if (!PublishedNativeTerrain.Record(point.x, point.y, terrain, success))
                    Interlocked.Increment(ref _publishedExactProbeCapacityFallbacks);
        }

        internal static void RecordPublishedNativeTerrainMemoMiss()
        {
            Interlocked.Increment(ref _publishedNativeTerrainMemoMisses);
        }

        internal static void RecordPreparedHeightHit()
        {
            Interlocked.Increment(ref _preparedHeightHits);
        }

        internal static void RecordExactHeightFallback()
        {
            Interlocked.Increment(ref _exactHeightFallbacks);
        }

        internal static void RecordPreparedTerrainHit()
        {
            Interlocked.Increment(ref _preparedTerrainHits);
        }

        internal static void RecordPreparedTerrainInvalidHit()
        {
            Interlocked.Increment(ref _preparedTerrainInvalidHits);
        }

        internal static void RecordNativeTerrainFallback()
        {
            Interlocked.Increment(ref _nativeTerrainFallbacks);
        }

        internal static void RecordBuilderAdvance(object builder, long elapsedTicks)
        {
            Interlocked.Increment(ref _advanceCount);
            Interlocked.Add(ref _advanceTicks, elapsedTicks);
            UpdateMaximum(ref _maximumAdvanceTicks, elapsedTicks);

            if (_completionLogged || builder == null || _builderIsCompleteGetter == null) return;
            try
            {
                if ((bool)_builderIsCompleteGetter.Invoke(builder, null))
                {
                    lock (SyncRoot)
                    {
                        if (_completionLogged) return;
                        _completionLogged = true;
                        LogSessionSummaryLocked("build-complete");
                    }
                }
            }
            catch (Exception exception)
            {
                // Diagnostic reflection must never affect rendering.
                BorderOptimizerDiagnostics.Error("Builder completion diagnostics failed.", exception);
            }
        }

        internal static void LogSessionSummary(string reason)
        {
            lock (SyncRoot) LogSessionSummaryLocked(reason);
        }

        private static void LogSessionSummaryLocked(string reason)
        {
            long advances = Interlocked.Read(ref _advanceCount);
            long heightHits = Interlocked.Read(ref _preparedHeightHits);
            long heightFallbacks = Interlocked.Read(ref _exactHeightFallbacks);
            long terrainHits = Interlocked.Read(ref _preparedTerrainHits);
            long terrainInvalidHits = Interlocked.Read(ref _preparedTerrainInvalidHits);
            long terrainFallbacks = Interlocked.Read(ref _nativeTerrainFallbacks);
            long publishedHeightMemoHits = Interlocked.Read(ref _publishedExactHeightMemoHits);
            long publishedHeightMemoMisses = Interlocked.Read(ref _publishedExactHeightMemoMisses);
            long publishedTerrainMemoHits = Interlocked.Read(ref _publishedNativeTerrainMemoHits);
            long publishedTerrainMemoMisses = Interlocked.Read(ref _publishedNativeTerrainMemoMisses);
            long publishedCapacityFallbacks = Interlocked.Read(ref _publishedExactProbeCapacityFallbacks);
            if (advances == 0 && heightHits == 0 && heightFallbacks == 0
                && terrainHits == 0 && terrainInvalidHits == 0 && terrainFallbacks == 0
                && publishedHeightMemoHits == 0 && publishedHeightMemoMisses == 0
                && publishedTerrainMemoHits == 0 && publishedTerrainMemoMisses == 0) return;

            BorderOptimizerDiagnostics.Info(
                "Optimizer summary: reason=" + reason
                + "; preparedHeightHits=" + heightHits
                + "; exactHeightFallbacks=" + heightFallbacks
                + "; preparedTerrainHits=" + terrainHits
                + "; preparedTerrainInvalidHits=" + terrainInvalidHits
                + "; nativeTerrainFallbacks=" + terrainFallbacks
                + "; publishedExactHeightMemoHits=" + publishedHeightMemoHits
                + "; publishedExactHeightMemoMisses=" + publishedHeightMemoMisses
                + "; publishedNativeTerrainMemoHits=" + publishedTerrainMemoHits
                + "; publishedNativeTerrainMemoMisses=" + publishedTerrainMemoMisses
                + "; publishedExactProbeCapacityFallbacks=" + publishedCapacityFallbacks
                + "; builderAdvances=" + advances
                + "; builderTotalMilliseconds=" + ToMilliseconds(Interlocked.Read(ref _advanceTicks))
                + "; maximumAdvanceMilliseconds=" + ToMilliseconds(Interlocked.Read(ref _maximumAdvanceTicks)) + ".");
        }

        private static void ResetCountersLocked()
        {
            Interlocked.Exchange(ref _preparedHeightHits, 0);
            Interlocked.Exchange(ref _exactHeightFallbacks, 0);
            Interlocked.Exchange(ref _preparedTerrainHits, 0);
            Interlocked.Exchange(ref _preparedTerrainInvalidHits, 0);
            Interlocked.Exchange(ref _nativeTerrainFallbacks, 0);
            Interlocked.Exchange(ref _publishedExactHeightMemoHits, 0);
            Interlocked.Exchange(ref _publishedExactHeightMemoMisses, 0);
            Interlocked.Exchange(ref _publishedNativeTerrainMemoHits, 0);
            Interlocked.Exchange(ref _publishedNativeTerrainMemoMisses, 0);
            Interlocked.Exchange(ref _publishedExactProbeCapacityFallbacks, 0);
            Interlocked.Exchange(ref _advanceCount, 0);
            Interlocked.Exchange(ref _advanceTicks, 0);
            Interlocked.Exchange(ref _maximumAdvanceTicks, 0);
            Interlocked.Exchange(ref _topologyWorkTicks, 0);
            Interlocked.Exchange(ref _maximumTopologySliceTicks, 0);
            _completionLogged = false;
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = AccessTools.Field(type, name);
            if (field == null) throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static void UpdateMaximum(ref long target, long candidate)
        {
            long current;
            do
            {
                current = Interlocked.Read(ref target);
                if (candidate <= current) return;
            }
            while (Interlocked.CompareExchange(ref target, candidate, current) != current);
        }

        private static long ToMilliseconds(long ticks)
        {
            return ticks <= 0 ? 0 : ticks * 1000L / Stopwatch.Frequency;
        }
    }

    internal static class BorderOptimizerDiagnostics
    {
        private static readonly object SyncRoot = new object();
        private static string _logPath;

        internal static void Initialize()
        {
            try
            {
                string assemblyDirectory = Path.GetDirectoryName(
                    typeof(PoliticalBorderOptimizerSubModule).Assembly.Location);
                DirectoryInfo binaryDirectory = string.IsNullOrWhiteSpace(assemblyDirectory)
                    ? null
                    : Directory.GetParent(assemblyDirectory);
                DirectoryInfo moduleDirectory = binaryDirectory == null ? null : binaryDirectory.Parent;
                string root = moduleDirectory == null ? assemblyDirectory : moduleDirectory.FullName;
                string logDirectory = Path.Combine(root, "Logs");
                Directory.CreateDirectory(logDirectory);
                _logPath = Path.Combine(logDirectory, "PoliticalBorderOptimizer.log");
                File.AppendAllText(_logPath, string.Empty, Encoding.UTF8);
            }
            catch
            {
                // Diagnostics are best-effort and must not prevent module load.
                _logPath = null;
            }
        }

        internal static void Info(string message)
        {
            Write("INFO  " + message);
        }

        internal static void Error(string message, Exception exception)
        {
            Write("ERROR " + message + Environment.NewLine + exception);
        }

        private static void Write(string message)
        {
            if (string.IsNullOrWhiteSpace(_logPath)) return;
            try
            {
                lock (SyncRoot)
                {
                    File.AppendAllText(
                        _logPath,
                        DateTime.Now.ToString("O") + " " + message + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch
            {
                // Logging failure must not affect the renderer.
            }
        }
    }
}
