using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Path = System.IO.Path;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Aoc.BorderEditPrototype
{
    public sealed class PublishedBorderSubModule : BorderEditSubModule
    {
        protected override void OnSubModuleLoad()
        {
            PublishedBorderLayout.Root=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(GetType().Assembly.Location),"..","..","AuthoredBorders"));
            PrototypeLog.Write("Published AOC Core borders enabled; assets="+PublishedBorderLayout.Root);
            base.OnSubModuleLoad();
        }
    }
    public class BorderEditSubModule : MBSubModuleBase
    {
        private int _retry;
        private bool _loggedTick;
        private bool _reportedFailure;
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            PrototypeLog.Write("Module loaded; version="+GetType().Assembly.GetName().Version+"; assembly="+GetType().Assembly.Location);
            Bind();
        }
        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            Bind();
        }
        private static void Bind()
        {
            if(PrototypeRuntime.Failed || NativeBindings.Ready) return;
            try { NativeBindings.TryBind(); } catch(Exception error) { PrototypeRuntime.Fail(error); }
        }
        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            if(!_loggedTick) { _loggedTick=true; PrototypeLog.Write("First application tick; bound="+NativeBindings.Ready+"; failed="+PrototypeRuntime.Failed); }
            // Remains available even if failed binding removed every map hook.
            if(PrototypeRuntime.Failed && TaleWorlds.ScreenSystem.ScreenManager.TopScreen!=null
                && (!_reportedFailure || Input.IsKeyReleased(InputKey.F9)))
            {
                _reportedFailure=true;
                try { InformationManager.DisplayMessage(new InformationMessage(PrototypeRuntime.Status)); }
                catch(Exception error) { PrototypeLog.Write("Failure notification unavailable: "+error); }
            }
            if(PrototypeRuntime.IsNativeThread) NativeCleanup.Try(()=> { NativeCleanup.Drain(); PrototypePanel.RetryClose(); },"Deferred cleanup failed");
            if(PrototypeRuntime.Failed || NativeBindings.Ready || _retry-- > 0) return;
            _retry=60;
            Bind();
        }
        protected override void OnSubModuleUnloaded()
        {
            try { PrototypeRuntime.CloseMap(); }
            catch(Exception error) { PrototypeLog.Write(error.ToString()); }
            finally
            {
                NativeCleanup.Try(()=>NativeBindings.Harmony?.UnpatchAll(NativeBindings.Owner),"Prototype unpatch failed");
                base.OnSubModuleUnloaded();
            }
        }
    }
    internal static class PrototypeLog
    {
        internal static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AgesOfCalradia","BorderEditPrototype");
        internal static void Write(string text)
        {
            try { Directory.CreateDirectory(Root); File.AppendAllText(Path.Combine(Root,"prototype.log"),DateTime.UtcNow.ToString("O")+" "+text+Environment.NewLine); }
            catch(IOException error) { System.Diagnostics.Trace.WriteLine(error); }
            catch(UnauthorizedAccessException error) { System.Diagnostics.Trace.WriteLine(error); }
        }
    }
    internal static class PrototypeRuntime
    {
        internal static bool Failed { get; private set; }
        internal static bool Active { get; private set; }
        internal static bool ConnectMode { get; private set; }
        internal static bool DeleteMode { get; private set; }
        internal static bool TopDown {get;private set;}=true;
        private static int _connectionStart=-1;
        private static BorderEdge _connectionAwaitingPreview;
        private static int _pointHover=-1,_checkedStart=-1;
        private static bool _targetAllowed;
        private static int _checkedRevision=-1;
        internal static NativeCapture Source { get; private set; }
        internal static NativePreview Preview { get; private set; }
        private static CreationTool _creation;
        private static NativeFillPreview _fills;
        internal static bool Drawing=>_creation!=null && _creation.Active;
        internal static string DrawingMode=>_creation?.Mode;
        internal static bool Filling=>Drawing && (_creation.Mode=="fill"||_creation.Mode=="borderfill");
        internal static bool FillReady=>_fills==null || _fills.Ready;
        internal static string Status="Load a campaign with the prototype enabled, then press F9.";
        private static int _thread;
        private static object _behavior;
        private static EditSnapshot _session;
        private static DraftChangeTracker _draftChanges;
        internal static bool HasUnsavedEdits=>Source!=null && _draftChanges!=null && _draftChanges.IsDirty(Source.Graph);
        internal static bool CanSave=>Active && Source!=null && !Dragging && Preview.CaughtUp && FillReady;
        internal static string SaveState=>HasUnsavedEdits?"Unsaved":Source==null || BorderGraph.Equal(Source.Graph.Current,Source.Graph.Original)?"No edits":"Saved";
        internal static bool CanDelete=>Active && Source!=null && !Dragging && Preview.CaughtUp;
        private static EditSnapshot _replay;
        private static string _replaySignature, _replayCampaign, _campaign;
        private static string _previousTopologySignature;
        private static string[] _previousTopologyTokens;
        private static string _lastPublishedNodeComparison;
        private static string _publishedPath, _publishedTopology;
        private static CampaignTimeControlMode _previousTime;
        private static Campaign _pausedCampaign;
        private static bool _escape;
        private static int _hover=-1;
        private static Point2? _markerFocus;
        private static readonly DragIntent DragIntent=new DragIntent();
        private static float _dragPlaneHeight;
        internal static string ActionHint=>Drawing?_creation.Hint:DeleteMode?"Click a RED section to delete. Esc: finish.":ConnectMode?"Choose an endpoint. Escape cancels the link.":_pointHover>=0?"Click this point to start a connection.":Dragging?"Release to keep the move. Escape cancels it.":_hover>=0?"Drag to move. Click to select. Delete removes it.":"Drag lines to move them. Click points to connect.";
        internal static bool IsNativeThread => _thread!=0 && Thread.CurrentThread.ManagedThreadId==_thread;
        internal static bool Dragging => Active && Source!=null && Source.Graph.Dragging;
        internal static void OnNativeThread()
        { Interlocked.CompareExchange(ref _thread,Thread.CurrentThread.ManagedThreadId,0); RequireNativeThread(); }
        internal static void RequireNativeThread()
        { if(!IsNativeThread) throw new InvalidOperationException("Border editor native call attempted on the wrong thread."); }
        private static NativeFillRepair _repair;
        internal static bool PublishedReady {get;private set;}
        // The optimizer owns this scope only while the map is still loading.
        // Normal campaign rebuilds must retain the bounded per-frame upload path.
        internal static bool LoadingPublication {get;set;}
        internal static void BeforeFillClear(object behavior){if(ReferenceEquals(behavior,_behavior)){PublishedReady=false;_repair?.Clear();_repair=null;}NativeRepairCapture.Clear();}
        private static float RepairHeight(Point2 p)
        {
            var point=new CampaignVec2(new Vec2(p.X,p.Y),false);float height=0;
            if(Campaign.Current?.MapSceneWrapper==null||!Campaign.Current.MapSceneWrapper.GetHeightAtPoint(in point,ref height))throw new InvalidOperationException("Campaign repair surface unavailable.");
            return height;
        }
        internal static void Attach(object behavior,NativeCapture source)
        {
            RequireNativeThread(); PublishedReady=false; _behavior=behavior; Source=source; Preview=new NativePreview(behavior,source);
            RecordTopologyDiagnostic(source);
            RecordPublishedNodeDiagnostic(source);
            var style=new NativeLocationStyle(source);_creation=new CreationTool(source,style);_fills=new NativeFillPreview(source,style);
            _campaign=Campaign.Current?.UniqueGameId;
            _publishedPath=null;_publishedTopology=null;
            _draftChanges=new DraftChangeTracker(source.Graph.Original);
            bool hasPublishedDraft=false;
            bool restoredDraft=false;
            FillRepairPlan acceptedRepair=NativeRepairCapture.Plan;
            try
            {
                EditSnapshot saved=null;
                if(PublishedBorderLayout.Enabled)
                {
                    if(source.Signature==PublishedBorderLayout.ReviewedSourceIdentity)
                    {
                        var binding=PublishedBorderLayout.LoadReviewedBinding(NativeBindings.RendererHash,source.Signature,source.Graph);
                        saved=binding.Draft;_publishedPath=binding.Path;_publishedTopology=binding.AssetIdentity;acceptedRepair=binding.Repair;
                        PrototypeLog.Write("Published binding accepted; source="+binding.SourceIdentity+";exact="+binding.ExactGraph+";asset="+binding.AssetIdentity+";draftHash="+PublishedBorderLayout.ReviewedDraftHash+";repairHash="+PublishedBorderLayout.ReviewedRepairHash);
                    }
                    else
                    {
                        saved=DraftStore.Load(DraftPath(),source.Signature,source.Graph);
                        _publishedTopology=source.Signature;
                    }
                }
                else saved=DraftStore.Load(DraftPath(),source.Signature,source.Graph);
                _draftChanges.MarkSaved(saved??source.Graph.Original);
                EditSnapshot draft = _replay!=null && _replaySignature==source.Signature && _replayCampaign==_campaign
                    ? _replay : saved;
                if(draft!=null)
                {
                    source.Graph.Restore(draft);
                    restoredDraft=true;
                    hasPublishedDraft=PublishedBorderLayout.Enabled;
                    Status=PublishedBorderLayout.Enabled?"Published AOC Core borders restored. F9 opens editing.":"Restoring saved border edits. F9 opens editing.";
                    PrototypeLog.Write(Status);
                }
                else Status="Borders captured. F9: drag, delete and connect.";
                if(_replay!=null && _replaySignature!=source.Signature) Status="Border generation changed; old edits retained as draft but not applied. F9 opens the new borders.";
            }
            catch(Exception error) { PrototypeLog.Write("Draft load rejected: "+error); Status="Draft could not be loaded; original borders retained."; }
            _replay=null;
            try
            {
                FillRepairPlan plan=acceptedRepair;
                if(restoredDraft&&plan!=null&&plan.Topology==(_publishedTopology??source.Signature)&&System.IO.File.Exists(DraftPath()))
                {
                    string hash;using(var sha=System.Security.Cryptography.SHA256.Create())using(var input=System.IO.File.OpenRead(DraftPath()))hash=BitConverter.ToString(sha.ComputeHash(input)).Replace("-","");
                    if(plan.CanApply(restoredDraft,_publishedTopology??source.Signature,hash))
                    {
                        var entities=(List<GameEntity>)AccessTools.Field(behavior.GetType(),"_territoryFillEntities").GetValue(behavior);
                        _repair=new NativeFillRepair(source.Graph,plan,entities,source.Scene,()=>NativeRepairCapture.For(source.Builder),RepairHeight,p=>style.Color(p,50));
                        PrototypeLog.Write("Saved-layout fill repair queued; exact draft hash matched.");
                    }
                    else PrototypeLog.Write("Fill repair skipped: saved draft has changed.");
                }
            }
            catch(Exception error){PrototypeLog.Write("Fill repair unavailable; original fill retained: "+error);}
            if(hasPublishedDraft&&LoadingPublication)
            {
                var loading=System.Diagnostics.Stopwatch.StartNew();
                bool bordersReady=Preview.ApplyPublishedBeforeFirstMapFrame(15000d);
                double remaining=Math.Max(0d,45000d-loading.Elapsed.TotalMilliseconds);
                if(!bordersReady)
                    PrototypeLog.Write("Published layout failed before first frame; native borders retained.");
                else
                {
                    bool repairReady=_repair!=null
                        ? _repair.ApplyBeforeFirstMapFrame((float)NativeBindings.Alpha.GetValue(_behavior),remaining,()=>NativeRepairCapture.Advance(source.Builder))
                        : NativeRepairCapture.Plan==null;
                    PublishedReady=repairReady;
                    PrototypeLog.Write(repairReady?"Published layout and required fill completed inside loading publication scope.":"Published fill unavailable during loading; original fill retained.");
                }
            }
            PrototypeLog.Write("Captured rows="+source.Rows.Count+";edges="+source.Edges.Count+";nodes="+source.Graph.Original.Points.Length+";layoutIdentity="+source.Signature+";topologySignature="+source.TopologySignature+";rendererSignature="+source.RendererSignature);
        }
        // Called only by the hash-pinned ReplacePoliticalFrontierEntities postfix.
        // It records evidence and never changes native visibility or ownership.
        private static void RecordTopologyDiagnostic(NativeCapture source)
        {
            if (string.IsNullOrWhiteSpace(source.TopologySignature) || source.TopologyTokens == null) return;
            try
            {
                var topology = new TopologyFingerprint
                {
                    Signature = source.TopologySignature,
                    Tokens = source.TopologyTokens
                };
                string path = TopologyCaptureStore.Write(
                    Path.Combine(PrototypeLog.Root, "TopologyCaptures"),
                    NativeBindings.RendererHash,
                    source.Signature,
                    topology);
                PrototypeLog.Write("Canonical topology capture saved at ReplacePoliticalFrontierEntities: path="+path+";signature="+source.TopologySignature+";tokens="+source.TopologyTokens.Length+".");
                var exact=TopologyFingerprint.CreateExact(NativeBindings.RendererHash,source.Graph.Original.Points,source.Graph.Edges);
                string exactPath=TopologyCaptureStore.Write(Path.Combine(PrototypeLog.Root,"ExactGraphCaptures"),NativeBindings.RendererHash,source.Signature,exact);
                PrototypeLog.Write("Exact native XY graph captured: path="+exactPath+";signature="+exact.Signature+".");
            }
            catch (Exception error)
            {
                PrototypeLog.Write("Canonical topology capture unavailable; native borders retained: "+error);
            }
            if (_previousTopologySignature == null)
                PrototypeLog.Write("Canonical topology baseline at ReplacePoliticalFrontierEntities: signature="+source.TopologySignature+";tokens="+source.TopologyTokens.Length+".");
            else if (!string.Equals(_previousTopologySignature,source.TopologySignature,StringComparison.Ordinal))
                PrototypeLog.Write("Canonical topology mismatch at ReplacePoliticalFrontierEntities: earlier="+_previousTopologySignature+";later="+source.TopologySignature+";firstDifference="+TopologyFingerprint.FirstDifference(_previousTopologyTokens,source.TopologyTokens)+".");
            _previousTopologySignature=source.TopologySignature;
            _previousTopologyTokens=source.TopologyTokens;
        }
        private static void RecordPublishedNodeDiagnostic(NativeCapture source)
        {
            if (!PublishedBorderLayout.Enabled || source.TopologyTokens == null) return;
            try
            {
                string exactSignature=TopologyFingerprint.CreateExact(NativeBindings.RendererHash,source.Graph.Original.Points,source.Graph.Edges).Signature;
                if(string.Equals(_lastPublishedNodeComparison,exactSignature,StringComparison.Ordinal)) return;
                string difference=PublishedBorderLayout.FirstNativeNodeDifference(source.TopologyTokens);
                PrototypeLog.Write("Saved authored coordinates compared with native nodes (differences may be user edits, not topology drift): topology="+source.TopologySignature+";firstDifference="+difference+".");
                _lastPublishedNodeComparison=exactSignature;
                PrototypeLog.Write("Read-only reviewed geometry replay: "+PublishedBorderLayout.DiagnoseReviewedGeometry(source.Graph));
            }
            catch(Exception error) { PrototypeLog.Write("Read-only reviewed geometry diagnostic rejected or unavailable; no publication authorized: "+error); }
        }
        private static string DraftPath() => _publishedPath??PublishedBorderLayout.DraftPath(Path.Combine(PrototypeLog.Root,"Drafts"),_campaign,Source.Signature);
        internal static void BeforeNativeClear(object behavior)
        {
            if(!ReferenceEquals(_behavior,behavior) || Source==null) return;
            PublishedReady=false;
            EndSession(false);
            _repair?.Clear();_repair=null;_creation?.Clear();_fills?.Clear();
            _replay=Source.Graph.Current.Copy(); _replaySignature=Source.Signature; _replayCampaign=_campaign;
            NativeSelection.Clear(); Preview?.RestoreOriginals(); Source=null; Preview=null; _behavior=null;
            _publishedPath=null;_publishedTopology=null;
        }
        internal static bool SuppressNormalFrontiers(object behavior) => Active && ReferenceEquals(_behavior,behavior);
        internal static void Tick(object mapScreen,float dt)
        {
            OnNativeThread();
            NativeCleanup.Drain(); PrototypePanel.RetryClose();
            bool ownsInput=Input.IsMouseActive && !MapObscured(mapScreen) && !PrototypePanel.OtherLayerFocused;
            if(!ownsInput) _escape=false;
            if(ownsInput && Input.IsKeyReleased(InputKey.F9)) { if(Active) EndSession(false); else BeginSession(); }
            if(Source==null) return;
            if(!ReferenceEquals(NativeBindings.SceneField.GetValue(_behavior),Source.Scene)) { CloseMap(); return; }
            if(Active)
            {
                if(Campaign.Current!=_pausedCampaign) { CloseMap(); return; }
                Campaign.Current.TimeControlMode=CampaignTimeControlMode.Stop;
                if(ownsInput && _escape)
                { _escape=false; if(Source.Graph.Dragging) { Source.Graph.CancelDrag(); Preview.AbortPending(); Status="Drag cancelled."; } else if(ConnectMode || DeleteMode || Drawing) SetConnectMode(false); else EndSession(false); }
                if(!Active) { Preview.Tick(); return; }
                bool control=Input.IsKeyDown(InputKey.LeftControl)||Input.IsKeyDown(InputKey.RightControl);
                if(ownsInput && control && Input.IsKeyReleased(InputKey.Z)) Command("undo");
                if(ownsInput && control && Input.IsKeyReleased(InputKey.Y)) Command("redo");
                if(ownsInput && control && Input.IsKeyReleased(InputKey.S)) Command("save");
                if(ownsInput && Input.IsKeyReleased(InputKey.Delete)) Command("delete");
                if(ownsInput && !control && Input.IsKeyReleased(InputKey.C)) Command("connect");
                if(ownsInput && Input.IsKeyReleased(InputKey.Enter)) { if(Drawing){Status=_creation.Finish();}else{EndSession(false);Preview.Tick();return;} }
                bool blocked=PrototypePanel.PointerOverPanel || !ownsInput;
                if(blocked)_pointHover=-1;
                Point2 pointer=new Point2(); float tolerance=0.5f;
                Vec3 near=Vec3.Zero,far=Vec3.Zero;
                float endpointSnap=0;
                bool hasRay=!blocked && TryPointerRay(mapScreen,out near,out far,out endpointSnap);
                bool usable=Source.Graph.Dragging?hasRay&&OverlayHitMesh.ProjectDrag(near,far,_dragPlaneHeight,out pointer):!blocked && TryPointer(mapScreen,out pointer,out tolerance);
                if(usable)_markerFocus=pointer;
                bool pointClick=false;
                if(!Drawing && !DeleteMode && !blocked && !Source.Graph.Dragging && Preview.CaughtUp)
                {
                    if(hasRay)
                    {
                        int picked=NativeSelection.PickEndpoint(near,far,endpointSnap);
                        if(_pointHover!=picked || _checkedStart!=_connectionStart || _checkedRevision!=Source.Graph.Revision)
                        {
                            _pointHover=picked;_checkedStart=_connectionStart;_checkedRevision=Source.Graph.Revision;_targetAllowed=false;
                            if(_connectionStart>=0 && picked>=0 && picked!=_connectionStart)
                            {
                                string reason;_targetAllowed=Source.Graph.CanConnectEndpoints(_connectionStart,picked,out reason);
                                Status=_targetAllowed?"Click to join these ends. The dashed line shows the connection.":"Cannot connect here: "+reason+" Choose another end.";
                            }
                        }
                        if(Input.IsKeyPressed(InputKey.LeftMouseButton) && (ConnectMode || picked>=0))
                        {
                            if(!ConnectMode)SetConnectMode(true);
                            SelectConnectionPoint(picked);pointClick=true;
                        }
                    }
                }
                if(DrawingMode=="borderfill")
                {
                    _hover=-1;_pointHover=-1;
                    try
                    {
                        if(usable&&hasRay)usable=DrawingPointer.Project(near,far,pointer,p=>NativeLocationStyle.Surface(p,4.12f).z,out pointer);
                        if(usable&&Preview.CaughtUp&&FillReady)
                        {
                            bool apply=Input.IsKeyPressed(InputKey.LeftMouseButton);
                            Status=_creation.BorderFill(pointer,apply);
                            if(apply){PrototypeLog.Write("Fill to border; applied="+_creation.Applied+"; result="+Status);if(!_creation.Applied)Notify(Status);}
                        }
                        else _creation.HidePreview();
                    }
                    catch(Exception error){PrototypeLog.Write("Fill to border rejected: "+error);_creation.Clear();Notify("Fill not applied: "+error.GetBaseException().Message);}
                }
                else if(Drawing)
                {
                    _hover=-1;_pointHover=-1;
                    try
                    {
                        _creation.FrameHeight=-4.65f*(1f-(float)NativeBindings.Alpha.GetValue(_behavior));
                        if(usable && hasRay)
                        {
                            float lift=Filling?4.12f:5.4f,offset=Filling?0:_creation.FrameHeight;
                            usable=DrawingPointer.Project(near,far,pointer,p=>NativeLocationStyle.Surface(p,lift).z+offset,out pointer);
                            if(!usable)
                            {
                                if(_creation.Pending)throw new InvalidOperationException("Stroke cancelled because the drawing surface could not be aligned. Adjust the camera angle.");
                                Status="Cannot align drawing on this steep surface. Adjust the camera angle.";
                            }
                        }
                        if(usable && _creation.Following)
                        {
                            usable=_creation.Guide(pointer,out pointer);
                            if(!usable)
                            {
                                if(_creation.Pending)Status="Tracing paused. Return to the last yellow point, or release to keep this section.";
                                else Status="Move closer to the boundary until the yellow drawing marker appears.";
                            }
                            else Status="Following boundary. Hold left mouse and follow the edge; release to keep.";
                        }
                        if(usable && hasRay && !Filling && Preview.CaughtUp)
                        {
                            _pointHover=NativeSelection.PickEndpoint(near,far,endpointSnap);
                            if(_pointHover>=0)
                            {
                                pointer=Source.Graph.Current.Points[_pointHover];
                                Status=_creation.Pending?"Snapped to endpoint. Release to finish the stroke.":"Endpoint highlighted. Start drawing here to extend this border.";
                            }
                        }
                        if(usable && Input.IsKeyPressed(InputKey.LeftMouseButton))_creation.Down(pointer);
                        if(usable && Input.IsKeyDown(InputKey.LeftMouseButton))_creation.Drag(pointer);
                        if(_creation.Pending && _creation.Paused)
                        {
                            usable=false;_pointHover=-1;
                            Status="Tracing paused. Return to the last yellow point, or release to keep this section.";
                        }
                        if(_creation.Pending && (!ownsInput || Input.IsKeyReleased(InputKey.LeftMouseButton)))
                        {
                            bool snapped=usable && _pointHover>=0;
                            Status=ownsInput?_creation.Release(usable?(Point2?)pointer:null,snapped):_creation.Finish();
                            if(ownsInput && snapped && _creation.Applied)Status="Stroke connected to the endpoint. Ctrl+Z undoes the whole stroke.";
                            if(ownsInput)
                            {
                                PrototypeLog.Write("Freehand release; applied="+_creation.Applied+"; result="+Status);
                                if(!_creation.Applied)Notify("Drawing not applied: "+Status);
                            }
                        }
                        if(usable)_creation.Preview(pointer);else if(_creation.Pending)_creation.Preview(_creation.LastPoint);else _creation.HidePreview();
                    }
                    catch(Exception error){PrototypeLog.Write("Drawing rejected: "+error);_creation.Finish();Notify("Drawing not applied: "+error.GetBaseException().Message);}
                }
                else if(DeleteMode)
                {
                    _pointHover=-1;Vec3 target;
                    _hover=hasRay && Preview.CaughtUp?NativeSelection.PickBorder(near,far,out target,true):-1;
                    if(_hover>=0 && Input.IsKeyPressed(InputKey.LeftMouseButton))
                    {
                        Source.Graph.Selected.Clear();Source.Graph.Selected.Add(_hover);
                        Source.Graph.DeleteSelected();_hover=-1;
                        Status="Section deleted. Click another red section, or Escape to finish. Ctrl+Z undoes.";
                    }
                    if(usable && _hover<0)
                    {
                        _creation.Preview(pointer,true);
                        if(Input.IsKeyPressed(InputKey.LeftMouseButton))
                        {
                            int fill=Array.FindLastIndex(Source.Graph.Current.Fills,f=>FillGeometry.Contains(f.Points,pointer));
                            if(fill>=0){Source.Graph.DeleteFill(fill);_creation.Clear();Status="Fill area deleted. Ctrl+Z restores it.";}
                        }
                    }
                    else _creation.Clear();
                }
                else if(ConnectMode)
                {
                    _hover=-1;
                }
                else if(Source.Graph.Dragging)
                {
                    if(!ownsInput) { Source.Graph.CancelDrag(); Preview.AbortPending(); }
                    else if(!Input.IsKeyDown(InputKey.LeftMouseButton)) Source.Graph.FinishDrag(); // Release also works over UI or off-map.
                    else if(usable && !blocked && DragIntent.Update(pointer))
                    {
                        string reason; Source.Graph.MoveDrag(pointer,out reason);
                        if(reason!=null) Status=reason;
                    }
                }
                else if(!pointClick && hasRay && !blocked && Preview.CaughtUp)
                {
                    Vec3 grabbed;
                    _hover=NativeSelection.PickBorder(near,far,out grabbed);
                    if(Input.IsKeyPressed(InputKey.LeftMouseButton) && _hover>=0)
                    {
                        bool multi=Input.IsKeyDown(InputKey.LeftShift)||Input.IsKeyDown(InputKey.RightShift);
                        if(multi)
                        {
                            if(!Source.Graph.Selected.Remove(_hover) && Source.Graph.Selected.Count<32) Source.Graph.Selected.Add(_hover);
                            Status="Selected "+Source.Graph.Selected.Count+" sections. Delete removes them; C connects two open ends.";
                        }
                        else
                        {
                            Source.Graph.Selected.Clear(); Source.Graph.Selected.Add(_hover);
                            pointer=new Point2(grabbed.x,grabbed.y);_dragPlaneHeight=grabbed.z;
                            DragIntent.Begin(pointer,Math.Max(.03f,tolerance*.25f));
                            Source.Graph.BeginDrag(_hover,pointer,8f); Status="Drag the border; release to keep, Escape to undo this gesture.";
                        }
                    }
                }
                else _hover=-1;
            }
            Preview.Tick();
            if(_repair!=null && Preview.CaughtUp)
            {
                bool repaired=_repair.Applied;_repair.Tick((float)NativeBindings.Alpha.GetValue(_behavior));
                if(!repaired&&_repair.Applied)Notify("BORDER FILLS REPAIRED: 22 gaps filled and 20 spill areas trimmed.");
                else if(repaired&&!_repair.Applied)Notify("Borders changed: the saved fill repair needs refreshing for this new outline.");
            }
            _fills?.Tick(Active?1f:(float)NativeBindings.Alpha.GetValue(_behavior));
            if(Active && _connectionAwaitingPreview!=null && Preview.CaughtUp)
            {
                if(Source.Graph.Current.Bridges.Contains(_connectionAwaitingPreview))
                {
                    Status="BORDER CONNECTED. Yellow link + cyan ends confirm the join. Ctrl+Z to undo.";
                    NativeCleanup.Try(()=>InformationManager.DisplayMessage(new InformationMessage("BORDER CONNECTED - the new link is highlighted. Ctrl+Z to undo.")),"Connection confirmation unavailable");
                }
                _connectionAwaitingPreview=null;
            }
            if(Active)
            {
                if(!NativeSelection.Refresh(Source,_behavior,_hover,Preview.Published,connectMode:ConnectMode,connectionStart:_connectionStart,focus:_markerFocus,connectionTarget:_pointHover,connectionValid:_targetAllowed,deleteMode:DeleteMode||Filling))
                { EndSession(false);Notify("Editor overlay unavailable; normal borders restored.");return; }
                PrototypePanel.Refresh();
            }
        }
        private static void SetConnectMode(bool enabled)
        {
            DeleteMode=false;
            _creation?.Clear();
            Source.Graph.CancelDrag(); Preview.AbortPending(); Source.Graph.Selected.Clear();
            ConnectMode=enabled; _connectionStart=-1; _hover=-1;_pointHover=-1;_checkedStart=-1;_targetAllowed=false;
            Status=enabled?"Step 1: click a GREEN diamond. Step 2: click the other GREEN diamond.":"Drag a border. Shift-click to select sections for deletion.";
            PrototypeLog.Write("Connection point mode="+enabled);
        }
        private static void SelectConnectionPoint(int chosen)
        {
            BorderGraph graph=Source.Graph;
            PrototypeLog.Write("Connection point click; picked="+chosen+"; first="+_connectionStart);
            if(chosen<0) { Status=_connectionStart<0?"Click a green endpoint to start a connection.":"Choose the other endpoint. Escape cancels this connection."; return; }
            if(_connectionStart<0)
            { graph.Selected.Clear();_connectionStart=chosen; Status="Step 2: first point is CYAN. Click a different GREEN diamond to join them."; return; }
            if(chosen==_connectionStart)
            { SetConnectMode(false);Status="Connection cancelled. Drag a line or choose another endpoint."; return; }
            string reason;
            bool connected=graph.ConnectEndpoints(_connectionStart,chosen,out reason);
            Status=connected?"Joining points - preparing the highlighted connection...":reason+" Choose another endpoint, or click cyan to reset.";
            PrototypeLog.Write("Connect endpoints; from="+_connectionStart+"; to="+chosen+"; success="+connected+"; result="+Status);
            if(connected)
            {
                _connectionStart=-1;ConnectMode=false;_pointHover=-1;_targetAllowed=false;graph.Selected.Add(graph.EdgeCount-1);
                _connectionAwaitingPreview=graph.Current.Bridges.Last();
            }
        }
        private static bool MapObscured(object map)
        {
            foreach(string name in new[]{"IsEscapeMenuOpened","IsInMenu","IsInArmyManagement","IsInRecruitment","IsInCampaignOptions","IsHeirSelectionPopupActive","IsInBattleSimulation"})
            { PropertyInfo property=AccessTools.Property(map.GetType(),name); if(property!=null && property.PropertyType==typeof(bool) && (bool)property.GetValue(map,null)) return true; }
            return false;
        }
        private static bool TryPointer(object map,out Point2 point,out float tolerance)
        {
            point=new Point2(); tolerance=0.5f;
            object view=AccessTools.Property(map.GetType(),"MapCameraView").GetValue(map,null);
            Camera camera=(Camera)AccessTools.Property(view.GetType(),"Camera").GetValue(view,null);
            if(camera==null) return false;
            Vec3 near,far;float snap;
            if(!TryPointerRay(map,out near,out far,out snap))return false;
            object[] args={near,far,0f,new Vec3(),new PathFaceRecord(),false,(BodyFlags)0};
            NativeBindings.Intersection.Invoke(map,args); Vec3 hit=(Vec3)args[3];
            if(!hit.IsValid || (hit.x==0f && hit.y==0f && hit.z==0f)) return false;
            point=new Point2(hit.x,hit.y);
            tolerance=Math.Max(0.2f,Math.Min(5f,(camera.Position-hit).Length*(float)Math.Tan(camera.HorizontalFov*0.5f)*20f/Math.Max(1f,Input.Resolution.x)));
            return true;
        }
        private static bool TryPointerRay(object map,out Vec3 near,out Vec3 far,out float endpointSnap)
        {
            near=Vec3.Zero;far=Vec3.Zero;endpointSnap=0;
            object view=AccessTools.Property(map.GetType(),"MapCameraView").GetValue(map,null);
            Camera camera=(Camera)AccessTools.Property(view.GetType(),"Camera").GetValue(view,null);
            if(camera==null)return false;
            // Match Bannerlord 1.4.8 MapScreen.HandleMouse exactly. SceneView owns
            // viewport and mouse conversion; a separate camera projection can disagree.
            object layer=AccessTools.Property(map.GetType(),"SceneLayer").GetValue(map,null);
            SceneView sceneView=(SceneView)AccessTools.Property(layer.GetType(),"SceneView").GetValue(layer,null);
            if(sceneView==null)return false;
            sceneView.TranslateMouse(ref near,ref far,-1f);
            // 18 screen pixels of near-miss tolerance, independent of terrain hits or zoom.
            endpointSnap=2f*(float)Math.Tan(camera.HorizontalFov*.5f)*18f/Math.Max(1f,Input.Resolution.x);
            return near.IsValid&&far.IsValid;
        }
        internal static void BeginSession()
        {
            if(Source==null || Campaign.Current==null) { Notify("Borders are not captured yet. Load/reload a test campaign with the prototype enabled."); return; }
            _session=Source.Graph.Current.Copy(); _pausedCampaign=Campaign.Current; _previousTime=_pausedCampaign.TimeControlMode;
            TopDown=true;
            _pausedCampaign.TimeControlMode=CampaignTimeControlMode.Stop; Active=true; DeleteMode=false; ConnectMode=false; _connectionStart=-1;_pointHover=-1;_markerFocus=null;
            Status="Drag GREEN lines to move borders. Click two GREEN diamonds to connect their ends.";
            PrototypePanel.Open();
            Preview.SetEditorOnly(true);
        }
        internal static void EndSession(bool cancel)
        {
            if(!Active) return;
            Active=false; DeleteMode=false; ConnectMode=false; _connectionStart=-1;_connectionAwaitingPreview=null;
            _creation?.Clear();
            try
            {
                Source?.Graph.CancelDrag(); Preview?.AbortPending();
                if(cancel && _session!=null) Source?.Graph.Restore(_session);
            }
            finally
            {
                NativeCleanup.Try(()=>Preview?.SetEditorOnly(false),"Normal border visibility restoration failed");
                NativeCleanup.Try(NativeSelection.Clear,"Selection cleanup failed");
                NativeCleanup.Try(PrototypePanel.Close,"Editor panel cleanup failed");
                try
                {
                    if(_pausedCampaign!=null && Campaign.Current==_pausedCampaign)
                        _pausedCampaign.TimeControlMode=_previousTime;
                }
                finally { _pausedCampaign=null; _session=null; _escape=false; }
            }
            PrototypeLog.Write("Editor closed;cancel="+cancel+";publishedBatches="+Preview?.PublishedBatches+";peakNativeBatchMs="+Preview?.PeakBatchMilliseconds);
        }
        internal static void Command(string action)
        {
            if(!Active || Source==null) return;
            try
            {
                BorderGraph graph=Source.Graph;
                if(action=="view")
                {
                    graph.CancelDrag();Preview.AbortPending();_creation?.Clear();
                    TopDown=!TopDown;
                    Status=TopDown?"2D view: straight down for precise editing. Scroll to zoom.":"3D view: normal map angle. Edits are unchanged.";
                    return;
                }
                if(action=="save")
                {
                    if(graph.Dragging || !Preview.CaughtUp || !FillReady || (_creation!=null&&_creation.Pending)) { Status="Release the drawing and wait for preview before saving."; return; }
                    if(_connectionStart>=0){Status="Connection unfinished. Click the other endpoint to apply it, then save.";return;}
                    if(!HasUnsavedEdits){Status=SaveState=="No edits"?"Nothing to save yet. Move a line, delete a section, or complete a connection first.":"Your applied border edits are already saved.";return;}
                    DraftStore.Save(DraftPath(),Source.Signature,graph.Current);_draftChanges.MarkSaved(graph.Current); _session=graph.Current.Copy();
                    PrototypeLog.Write("Draft saved; path="+DraftPath()+"; links="+graph.Current.Bridges.Length+"; deleted="+graph.Current.Deleted.Count(x=>x));
                    Status=(PublishedBorderLayout.Enabled?"PUBLISHED LAYOUT SAVED: ":"EDITS SAVED: ")+graph.Current.Bridges.Count(e=>e.Authored)+" drawn segments, "+graph.Current.Deleted.Count(x=>x)+" deleted, "+graph.Current.Fills.Length+" fills."; return;
                }
                if(action=="undo" && !graph.CanUndo){Status="Nothing to undo.";return;}
                if(action=="redo" && !graph.CanRedo){Status="Nothing to redo.";return;}
                if(action=="add" || action=="fill" || action=="coast" || action=="political" || action=="borderfill")
                {
                    bool same=Drawing && _creation.Mode==action;SetConnectMode(false);
                    if(!same)_creation.Begin(action);Status=same?"Drawing finished.":_creation.Hint;return;
                }
                if(action=="deletemode")
                {
                    if(!CanDelete)return;
                    bool enabled=!DeleteMode;SetConnectMode(false);DeleteMode=enabled;
                    Status=enabled?"Hover a border: the RED section will be removed when clicked. Escape finishes.":"Drag a green line to move it.";
                    return;
                }
                if(action=="delete" && (!CanDelete || graph.Selected.Count==0)){Status="Use Delete mode, then click the red section to remove it.";return;}
                Preview.AbortPending();
                switch(action)
                {
                    case "undo": _creation?.Clear();graph.Undo(); ConnectMode=false;_pointHover=-1; _connectionStart=-1; Status="Undo."; break;
                    case "redo": _creation?.Clear();graph.Redo(); ConnectMode=false;_pointHover=-1; _connectionStart=-1; Status="Redo."; break;
                    case "delete": graph.DeleteSelected(); ConnectMode=false;_pointHover=-1; _connectionStart=-1; Status="Selected sections deleted. Ctrl+Z restores them."; break;
                    case "connect":
                        SetConnectMode(!ConnectMode);
                        break;
                    case "finish":
                        bool unchanged=BorderGraph.Equal(graph.Current,graph.Original);
                        EndSession(false);
                        Notify(unchanged?"No border changes were applied. Selecting one endpoint does not create a connection.":"Editor closed. Applied border edits remain on the map.");
                        break;
                    case "cancel": EndSession(true); break;
                }
            }
            catch(Exception error) { PrototypeLog.Write("Command failed: "+error); Status="Command failed; see prototype.log."; }
        }
        internal static void RequestEscape() { _escape=true; }
        internal static void CloseMap()
        {
            try { EndSession(true); }
            finally
            {
                NativeCleanup.Try(NativeSelection.Clear,"Map selection cleanup failed");
                NativeCleanup.Try(PrototypePanel.Close,"Map panel cleanup failed");
                NativeCleanup.Try(()=>Preview?.RestoreOriginals(),"Map border restoration failed");
                NativeCleanup.Try(()=>_repair?.Clear(),"Fill repair cleanup failed");_repair=null;
                NativeCleanup.Try(()=>_creation?.Clear(),"Drawing cleanup failed");NativeCleanup.Try(()=>_fills?.Clear(),"Fill cleanup failed");_creation=null;_fills=null;
                Source=null; Preview=null; _behavior=null; NativeHooks.Pending=null; _replay=null;
            }
        }
        internal static void Fail(Exception error)
        {
            PrototypeLog.Write(error.ToString());
            try { CloseMap(); } catch(Exception cleanup) { PrototypeLog.Write("Cleanup failed: "+cleanup); }
            Failed=true; Active=false; Status="Border editor disabled: "+error.GetBaseException().Message+" (details in prototype.log)";
        }
        private static void Notify(string text) { Status=text; InformationManager.DisplayMessage(new InformationMessage(text)); }
    }
}




