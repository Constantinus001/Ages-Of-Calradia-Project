using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Engine;

namespace AgesOfCalradia.PoliticalBorderOptimizer
{
    /// <summary>
    /// Moves the approved renderer's existing scene-bound build into the
    /// campaign-load completion callback. It does not replace renderer logic
    /// or retain native scene objects across campaigns.
    /// </summary>
    internal static class EagerPoliticalBorderWarmup
    {
        // This is the optimizer's native build phase only. A verified published
        // layout has a separate sidecar-owned publication phase inside the final
        // RebuildBorders callback, before that callback returns.
        private const int MaximumBuildSteps = 10000;
        private const double MaximumBuildMilliseconds = 30000d;

        private static MethodInfo _tryGetCampaignMapScene;
        private static MethodInfo _rebuildBorders;
        private static MethodInfo _prepareTerritoryCells;
        private static MethodInfo _buildOwnershipSignature;
        private static MethodInfo _replacePoliticalFillEntities;
        private static MethodInfo _replacePoliticalFrontierEntities;
        private static MethodInfo _cancelBuilder;
        private static FieldInfo _mapScene;
        private static FieldInfo _politicalOverlayView;
        private static FieldInfo _dirty;
        private static FieldInfo _loggedFirstBuild;
        private static FieldInfo _pendingTerritoryCells;
        private static FieldInfo _pendingFillBuilder;
        private static FieldInfo _lastOwnershipSignature;
        private static FieldInfo _minimumX;
        private static FieldInfo _minimumY;
        private static FieldInfo _maximumX;
        private static FieldInfo _maximumY;
        private static PropertyInfo _mapScreenScene;
        private static PropertyInfo _sceneLayer;
        private static PropertyInfo _sceneView;
        private static FieldInfo _isSceneViewEnabled;
        private static MethodInfo _readyToRender;
        private static MethodInfo _checkSceneReadyToRender;
        private static Type _overlayType;
        private static object _completedBehavior;
        private static object _pendingPublishedBehavior;
        private static bool _building;
        private static bool _loadingPublication;
        private static bool _mapPublicationAttempted;
        private static readonly PublishedLoadingGate LoadingGate=new PublishedLoadingGate();
        private static bool _dismissBlockedLogged,_dismissFailureLogged;

        internal static void Bind(Type behaviorType, Type overlayType, Type mapScreenType)
        {
            _tryGetCampaignMapScene = RequireMethod(behaviorType, "TryGetCampaignMapScene");
            _rebuildBorders = RequireMethod(behaviorType, "RebuildBorders");
            _prepareTerritoryCells = RequireMethod(behaviorType, "PrepareTerritoryCells");
            _buildOwnershipSignature = RequireMethod(behaviorType, "BuildOwnershipSignature");
            _replacePoliticalFillEntities = RequireMethod(behaviorType, "ReplacePoliticalFillEntities");
            _replacePoliticalFrontierEntities = RequireMethod(behaviorType, "ReplacePoliticalFrontierEntities");
            _mapScene = RequireField(behaviorType, "_mapScene");
            _politicalOverlayView = RequireField(behaviorType, "_politicalOverlayView");
            _dirty = RequireField(behaviorType, "_dirty");
            _loggedFirstBuild = RequireField(behaviorType, "_loggedFirstBuild");
            _pendingTerritoryCells = RequireField(behaviorType, "_pendingTerritoryCells");
            _pendingFillBuilder = RequireField(behaviorType, "_pendingFillBuilder");
            _lastOwnershipSignature = RequireField(behaviorType, "_lastOwnershipSignature");
            _minimumX = RequireField(behaviorType, "_pendingMinX");
            _minimumY = RequireField(behaviorType, "_pendingMinY");
            _maximumX = RequireField(behaviorType, "_pendingMaxX");
            _maximumY = RequireField(behaviorType, "_pendingMaxY");
            Type builderType = AccessTools.TypeByName(
                "TwelveMonthCalendar.CampaignPoliticalTerritoryFill+Builder");
            _cancelBuilder = RequireMethod(builderType, "Cancel");
            _overlayType = overlayType;
            _mapScreenScene = RequireProperty(mapScreenType, "MapScene");
            _sceneLayer = RequireProperty(mapScreenType, "SceneLayer");
            _sceneView = RequireProperty(_sceneLayer.PropertyType, "SceneView");
            _isSceneViewEnabled = RequireField(mapScreenType, "_isSceneViewEnabled");
            if(_isSceneViewEnabled.FieldType!=typeof(bool)) throw new InvalidOperationException("Map view enabled-state contract changed.");
            _readyToRender = RequireMethod(_sceneView.PropertyType, "ReadyToRender");
            _checkSceneReadyToRender = RequireMethod(_sceneView.PropertyType, "CheckSceneReadyToRender");
        }

        // Hash-pinned Core OnSessionLaunched postfix: queues new sessions as
        // well as saves. No native work occurs here; readiness still belongs
        // to MapScreen. Missing/changed target disables optimizer binding.
        internal static void AfterSessionLaunched(object __instance)
        {
            if(__instance!=null && !_building && !ReferenceEquals(_completedBehavior,__instance)
                && PublishedBorderLayoutPresence.IsPresent()) QueuePublished(__instance,"session launch");
        }

        private static void QueuePublished(object behavior,string reason)
        {
            if(ReferenceEquals(_pendingPublishedBehavior,behavior)) return;
            _pendingPublishedBehavior=behavior;
            _mapPublicationAttempted=false;
            LoadingGate.Begin(behavior);_dismissBlockedLogged=false;_dismissFailureLogged=false;
            BorderOptimizerDiagnostics.Info("Published layout warmup queued from "+reason+" for the MapScreen readiness observer.");
        }

        internal static void AfterGameLoadFinished(object __instance)
        {
            if (__instance == null || _building || ReferenceEquals(_completedBehavior, __instance)) return;

            // OnGameLoadFinished occurs before MapScreen has a ready SceneView.
            // Deferring only published layouts prevents their generated source
            // geometry becoming visible before the authored replacement.
            if (PublishedBorderLayoutPresence.IsPresent() && !_loadingPublication)
            {
                QueuePublished(__instance,"save load completion");
                return;
            }

            try
            {
                Scene scene = Invoke(_tryGetCampaignMapScene, __instance) as Scene;
                if (scene == null)
                {
                    BorderOptimizerDiagnostics.Info(
                        "Eager political border warmup deferred: campaign scene was not available during OnGameLoadFinished.");
                    return;
                }
                object existingScene = _mapScene.GetValue(__instance);
                if (existingScene != null && !ReferenceEquals(existingScene, scene))
                {
                    BorderOptimizerDiagnostics.Info(
                        "Eager political border warmup deferred: renderer still referenced a different campaign scene.");
                    return;
                }
                if (_politicalOverlayView.GetValue(__instance) == null)
                {
                    // RebuildBorders only needs a non-null overlay when it
                    // publishes labels. An unattached instance safely ignores
                    // label work until the real MapScreen view is created.
                    object placeholderOverlay = Activator.CreateInstance(_overlayType, true);
                    _politicalOverlayView.SetValue(__instance, placeholderOverlay);
                }

                _mapScene.SetValue(__instance, scene);
                _building = true;
                Stopwatch timer = Stopwatch.StartNew();
                int steps = 0;
                if (_pendingTerritoryCells.GetValue(__instance) == null)
                    Invoke(_prepareTerritoryCells, __instance);
                IEnumerable territoryCells = _pendingTerritoryCells.GetValue(__instance) as IEnumerable;
                string ownershipSignature = Convert.ToString(
                    _buildOwnershipSignature.Invoke(null, null));
                bool publishedLayout = PublishedBorderLayoutPresence.IsPresent();
                PoliticalBorderGeometryCache.CacheIdentity cacheIdentity = null;
                PoliticalBorderGeometryCache.CachedGeometry cachedGeometry;
                if (!publishedLayout)
                {
                    cacheIdentity = PoliticalBorderGeometryCache.CreateIdentity(
                        ownershipSignature, scene, territoryCells,
                        (float)_minimumX.GetValue(__instance), (float)_minimumY.GetValue(__instance),
                        (float)_maximumX.GetValue(__instance), (float)_maximumY.GetValue(__instance));
                    if (PoliticalBorderGeometryCache.TryLoad(cacheIdentity, out cachedGeometry))
                    {
                        PrepareTerrainGridForReplay(__instance, timer, ref steps);
                        List<GameEntity> fillEntities;
                        List<GameEntity> frontierEntities;
                        PoliticalBorderGeometryCache.Replay(cachedGeometry, scene, out fillEntities, out frontierEntities);
                        Invoke(_replacePoliticalFillEntities, __instance, fillEntities);
                        Invoke(_replacePoliticalFrontierEntities, __instance, frontierEntities);
                        CompleteBehaviorState(__instance, ownershipSignature);
                        _completedBehavior = __instance;
                        BorderOptimizerDiagnostics.Info("Eager political border warmup completed from persistent geometry: steps=" + steps + "; wallMilliseconds=" + timer.ElapsedMilliseconds + ". Borders are ready before the first political map frame.");
                        return;
                    }
                }
                else BorderOptimizerDiagnostics.Info("Published Core border layout detected: persistent generated-geometry replay is bypassed so the reviewed layout is applied before the first map frame.");

                if (!publishedLayout) PoliticalBorderGeometryCache.BeginColdCapture(cacheIdentity);
                bool publicationScope = false;
                try
                {
                    if (publishedLayout)
                    {
                        SetPublishedLoadingScope(true);
                        publicationScope = true;
                    }
                    bool complete;
                    BorderOptimizerDiagnostics.Info(
                        "Eager political border warmup started during "
                        + (publishedLayout ? "the MapScreen readiness gate." : "OnGameLoadFinished."));
                    do
                    {
                        complete = (bool)Invoke(_rebuildBorders, __instance);
                        steps++;
                        if (!complete
                            && (steps >= MaximumBuildSteps
                                || timer.Elapsed.TotalMilliseconds >= MaximumBuildMilliseconds))
                        {
                            throw new TimeoutException(
                                "The eager native political-border phase exceeded its 30-second loading budget.");
                        }
                    }
                    while (!complete);

                    if (publishedLayout) EnsurePublishedLayoutReady();
                }
                finally
                {
                    if (publicationScope) SetPublishedLoadingScope(false);
                }
                if (!publishedLayout && PoliticalBorderGeometryCache.HasColdCapture)
                {
                    cachedGeometry = PoliticalBorderGeometryCache.CompleteColdCapture();
                    List<GameEntity> smoothedFillEntities;
                    List<GameEntity> smoothedFrontierEntities;
                    PoliticalBorderGeometryCache.Replay(
                        cachedGeometry, scene, out smoothedFillEntities, out smoothedFrontierEntities);
                    Invoke(_replacePoliticalFillEntities, __instance, smoothedFillEntities);
                    Invoke(_replacePoliticalFrontierEntities, __instance, smoothedFrontierEntities);
                }
                else if (!publishedLayout)
                {
                    BorderOptimizerDiagnostics.Info(
                        "Persistent geometry capture was abandoned; approved cold-build entities remain active.");
                }

                CompleteBehaviorState(__instance, ownershipSignature);
                _completedBehavior = __instance;
                BorderOptimizerDiagnostics.Info(
                    "Eager political border warmup completed: steps=" + steps
                    + "; wallMilliseconds=" + timer.ElapsedMilliseconds
                    + (publishedLayout
                        ? ". Approved native geometry completed; published-layout readiness was verified by its sidecar."
                        : ". Borders are ready before the first political map frame."));
            }
            catch (Exception exception)
            {
                // Reflection, campaign lifecycle, and native scene creation are
                // version-sensitive boundaries. Preserve the protected
                // behavior's dirty state so its normal frame-by-frame retry
                // remains available after any failure.
                try
                {
                    if (__instance != null && _dirty != null) _dirty.SetValue(__instance, true);
                }
                catch (Exception dirtyStateException)
                {
                    BorderOptimizerDiagnostics.Error(
                        "Eager warmup could not restore the renderer dirty state.", dirtyStateException);
                }
                BorderOptimizerDiagnostics.Error(
                    "Eager political border warmup failed; normal frame-by-frame rendering remains active.",
                    Unwrap(exception));
                PoliticalBorderGeometryCache.AbandonColdCapture();
            }
            finally
            {
                _building = false;
            }
        }

        /// <summary>
        /// Runs before SandBox.View publishes MapScene readiness. The private
        /// target is hash-pinned above. It preserves native readiness state and
        /// observes one bounded, renderer-owned attempt. It must never block
        /// MapScreen: the native screen update is already in progress here.
        /// </summary>
        internal static bool BeforeMapSceneVisible(object __instance)
        {
            object behavior = _pendingPublishedBehavior;
            if (behavior == null || _mapPublicationAttempted || _building) return true;
            try
            {
                if (!LoadingWindow.IsLoadingWindowActive) return true;
                Scene screenScene = _mapScreenScene.GetValue(__instance, null) as Scene;
                Scene campaignScene = Invoke(_tryGetCampaignMapScene, behavior) as Scene;
                if (screenScene == null || campaignScene == null
                    || !ReferenceEquals(screenScene, campaignScene)) return true;
                LoadingGate.Bind(__instance,behavior);
                if(!MapSceneIsReady(__instance))return true;

                _mapPublicationAttempted = true;
                _loadingPublication = true;
                bool wasEnabled=(bool)_isSceneViewEnabled.GetValue(__instance);
                BorderOptimizerDiagnostics.Info("Published loading preparation entered: frame="+Utilities.EngineFrameNo+";loading="+LoadingWindow.IsLoadingWindowActive+";viewEnabled="+wasEnabled);
                AfterGameLoadFinished(behavior);
                BorderOptimizerDiagnostics.Info("Published loading preparation finished: frame="+Utilities.EngineFrameNo+";loading="+LoadingWindow.IsLoadingWindowActive+";completed="+ReferenceEquals(_completedBehavior,behavior));
                if (ReferenceEquals(_completedBehavior, behavior))
                {
                    EnsurePublishedLayoutReady();
                    LoadingGate.Complete(behavior);
                    _pendingPublishedBehavior = null;
                    BorderOptimizerDiagnostics.Info("Published layout completed during the one MapScreen observer attempt.");
                }
                else LoadingGate.Fail(behavior,"Published border and fill preparation did not complete.");
            }
            catch (Exception exception)
            {
                LoadingGate.Fail(behavior,"Published loading preparation failed: "+Unwrap(exception).Message);
                BorderOptimizerDiagnostics.Error(
                    "Published layout observer attempt failed; native map rendering remains active.",
                    Unwrap(exception));
            }
            finally
            {
                _loadingPublication = false;
            }
            return true;
        }

        // Rewrites only the hash-pinned MapScreen's single loading-dismissal
        // call. Native readiness checks, counters and frame updates still run.
        internal static IEnumerable<CodeInstruction> GuardLoadingDismissal(IEnumerable<CodeInstruction> instructions)
            => PublishedLoadingDismissalPatch.Rewrite(instructions,
                AccessTools.Method(typeof(LoadingWindow),nameof(LoadingWindow.DisableGlobalLoadingWindow)),
                AccessTools.Method(typeof(EagerPoliticalBorderWarmup),nameof(DismissPublishedLoadingWindow)));

        internal static void DismissPublishedLoadingWindow(object mapScreen)
        {
            bool released=false;
            try
            {
                if(LoadingGate.State==PublishedLoadingState.Ready&&ReferenceEquals(LoadingGate.Screen,mapScreen))
                    EnsurePublishedLayoutReady();
                LoadingGate.TryDismiss(mapScreen,_completedBehavior,
                    (double)Stopwatch.GetTimestamp()/Stopwatch.Frequency,()=>released=true);
                if(!released&&!_dismissBlockedLogged)
                {
                    _dismissBlockedLogged=true;
                    BorderOptimizerDiagnostics.Info("Loading dismissal held: published borders and fill are not ready; native MapScreen continues.");
                }
            }
            catch(Exception error)
            {
                LoadingGate.Fail(LoadingGate.Behavior,"Loading dismissal verification failed: "+error.Message);
                BorderOptimizerDiagnostics.Error("Published loading dismissal failed; releasing native loading to avoid a deadlock.",error);
                released=true;
            }
            if(!released)return;
            // Exactly one native call. Notification/log failures cannot cause
            // duplicate native dismissal or leave the loader held.
            LoadingWindow.DisableGlobalLoadingWindow();
            if(LoadingGate.State==PublishedLoadingState.Failed)ReportLoadingFailure();
            else if(ReferenceEquals(LoadingGate.Screen,mapScreen))
                BorderOptimizerDiagnostics.Info("Loading dismissed after verified published borders and fill: frame="+Utilities.EngineFrameNo);
        }
        private static void ReportLoadingFailure()
        {
            if(_dismissFailureLogged)return;_dismissFailureLogged=true;
            BorderOptimizerDiagnostics.Info("Published loading FAILED; native fallback released: "+LoadingGate.Failure);
            try { TaleWorlds.Library.InformationManager.DisplayMessage(new TaleWorlds.Library.InformationMessage(
                "AOC: borders/fill failed to finish during loading. Native fallback is active; see PoliticalBorderOptimizer.log.")); }
            catch(Exception error){BorderOptimizerDiagnostics.Error("Published loading failure notification unavailable.",error);}
        }
        internal static void MapLoadingClosed(object __instance)
        {
            if(!ReferenceEquals(LoadingGate.Screen,__instance))return;
            LoadingGate.Cancel(__instance);_pendingPublishedBehavior=null;_completedBehavior=null;
            _mapPublicationAttempted=false;_dismissBlockedLogged=false;_dismissFailureLogged=false;
        }

        private static bool MapSceneIsReady(object mapScreen)
        {
            object layer = _sceneLayer.GetValue(mapScreen, null);
            object sceneView = layer == null ? null : _sceneView.GetValue(layer, null);
            if (sceneView == null) return false;
            return (bool)_readyToRender.Invoke(sceneView, null)
                && (bool)_checkSceneReadyToRender.Invoke(sceneView, null);
        }

        private static object Invoke(MethodInfo method, object instance)
        {
            if (method == null) throw new MissingMethodException("Eager warmup method binding is unavailable.");
            return method.Invoke(instance, null);
        }

        private static object Invoke(MethodInfo method, object instance, object argument)
        {
            if (method == null) throw new MissingMethodException("Eager warmup method binding is unavailable.");
            return method.Invoke(instance, new[] { argument });
        }

        private static void EnsurePublishedLayoutReady()
        {
            Type runtime = AccessTools.TypeByName("Aoc.BorderEditPrototype.PrototypeRuntime");
            PropertyInfo ready = runtime == null ? null : runtime.GetProperty(
                "PublishedReady", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (ready == null || ready.PropertyType != typeof(bool)
                || !(bool)ready.GetValue(null, null))
            {
                throw new InvalidOperationException(
                    "Published border sidecar did not finish its verified pre-map-frame application.");
            }
        }

        private static void SetPublishedLoadingScope(bool value)
        {
            Type runtime = AccessTools.TypeByName("Aoc.BorderEditPrototype.PrototypeRuntime");
            PropertyInfo scope = runtime == null ? null : runtime.GetProperty(
                "LoadingPublication", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (scope == null || scope.PropertyType != typeof(bool) || !scope.CanWrite)
                throw new InvalidOperationException("Published border sidecar does not expose its loading scope.");
            scope.SetValue(null, value, null);
        }

        private static void PrepareTerrainGridForReplay(
            object behavior,
            Stopwatch timer,
            ref int steps)
        {
            while (_pendingFillBuilder.GetValue(behavior) == null)
            {
                bool unexpectedlyComplete = (bool)Invoke(_rebuildBorders, behavior);
                steps++;
                if (unexpectedlyComplete) return;
                if (steps >= MaximumBuildSteps
                    || timer.Elapsed.TotalMilliseconds >= MaximumBuildMilliseconds)
                    throw new TimeoutException("Terrain preparation for cached political geometry timed out.");
            }
            object builder = _pendingFillBuilder.GetValue(behavior);
            if (builder != null)
            {
                Invoke(_cancelBuilder, builder);
                _pendingFillBuilder.SetValue(behavior, null);
            }
        }

        private static void CompleteBehaviorState(object behavior, string ownershipSignature)
        {
            _pendingTerritoryCells.SetValue(behavior, null);
            _lastOwnershipSignature.SetValue(behavior, ownershipSignature);
            _dirty.SetValue(behavior, false);
            _loggedFirstBuild.SetValue(behavior, true);
        }

        private static MethodInfo RequireMethod(Type type, string name)
        {
            MethodInfo method = type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Static
                    | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static PropertyInfo RequireProperty(Type type, string name)
        {
            PropertyInfo property = type.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null) throw new MissingMemberException(type.FullName, name);
            return property;
        }

        private static Exception Unwrap(Exception exception)
        {
            TargetInvocationException invocation = exception as TargetInvocationException;
            return invocation != null && invocation.InnerException != null
                ? invocation.InnerException
                : exception;
        }
    }
}
