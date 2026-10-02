using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace Aoc.BorderEditPrototype
{
    internal static class PreviewTests
    {
        private static int _checks;
        private static void Check(bool condition,string name)
        { if(!condition)throw new Exception(name); _checks++;Console.WriteLine("PASS "+name); }
        private static NativeCapture Fixture(out FakeBehavior behavior)
        {
            var builder=new FakeBuilder(); var capture=new NativeCapture{Builder=builder,InAdvance=true};
            var scene=new Scene(); var caps=new HashSet<int>(); Mesh.Capture=capture;
            for(int row=0;row<3;row++)
            {
                Mesh mesh=Mesh.CreateMesh(true);mesh.SetMaterial(new Material());mesh.SetMeshRenderOrder(108);UIntPtr handle=mesh.LockEditDataWrite();
                for(int id=row*2;id<Math.Min(row*2+2,5);id++)
                {
                    Vec2 a=new Vec2(id*4,0),b=new Vec2((id+1)*4,0),n=new Vec2(0,0.8f);
                    Vec3 ac,bc,al,ar,bl,br;NativeBindings.Sample(a,out ac);NativeBindings.Sample(b,out bc);
                    NativeBindings.Sample(a+n,out al);NativeBindings.Sample(a-n,out ar);NativeBindings.Sample(b+n,out bl);NativeBindings.Sample(b-n,out br);
                    capture.BeginSegment(mesh,a,b);
                    NativeBindings.EmitQuad(mesh,al,ac,bc,bl,10,handle);NativeBindings.EmitQuad(mesh,ac,ar,br,bc,20,handle);
                    foreach(int node in new[]{id,id+1}) if(caps.Add(node))
                    {
                        Vec2 p=new Vec2(node*4,0);Vec3 center;NativeBindings.Sample(p,out center);capture.BeginCap(p);
                        NativeBindings.EmitCap(mesh,p,center,new Vec2(1,0),new Vec2(0,1),10,20,handle);capture.EndCap();
                    }
                    capture.EndSegment(true);
                }
                mesh.UnlockEditDataWrite(handle);var entity=GameEntity.CreateEmpty(scene,false,true,true);entity.AddMesh(mesh,true);entity.SetVisibilityExcludeParents(true);
                builder.Entities.Add(entity);capture.AddRow(builder,scene,mesh);
            }
            capture.InAdvance=false;Mesh.Capture=null;capture.TakenEntities=builder.Entities;capture.Complete();
            behavior=new FakeBehavior();behavior.Entities.AddRange(builder.Entities);return capture;
        }
        private static void Drain(NativePreview preview)
        { for(int i=0;i<200 && !preview.CaughtUp;i++)preview.Tick(); if(!preview.CaughtUp)throw new Exception("Publication did not finish"); }
        private static void RepairRows()
        {
            FakeBehavior behavior;NativeCapture source=Fixture(out behavior);GameEntity original=behavior.Entities[0];
            var plan=new FillRepairPlan{Regions=1};var row=new RepairRow{Id=0,Faces=8,Fingerprint="verified"};
            for(int i=0;i<130;i++)row.Triangles.Add(new RepairTriangle{A=new Point2(0,0),B=new Point2(1,0),C=new Point2(0,1),Color=10});plan.Rows.Add(row);
            var captured=new Dictionary<int,RepairCapturedRow>{{0,new RepairCapturedRow{Entity=original,Faces=8,Fingerprint="verified"}}};
            var repair=new NativeFillRepair(source.Graph,plan,behavior.Entities,source.Scene,()=>captured,p=>2,p=>10);
            repair.Tick(1);Check(!repair.Applied&&ReferenceEquals(behavior.Entities[0],original),"repair keeps original fill visible while bounded upload is pending");
            for(int i=0;i<100&&!repair.Applied;i++)repair.Tick(1);
            Check(repair.Applied&&!ReferenceEquals(behavior.Entities[0],original)&&!original.Visible,"verified repair atomically replaces original fill rows");
            GameEntity candidate=behavior.Entities[0];source.Graph.Selected.Add(0);source.Graph.DeleteSelected();repair.Tick(1);
            Check(ReferenceEquals(behavior.Entities[0],original)&&original.Visible&&candidate.Removed,"later border edits restore original fill and release the stale repair");
            captured[0].Fingerprint="wrong";
            repair=new NativeFillRepair(source.Graph,plan,behavior.Entities,source.Scene,()=>captured,p=>2,p=>10);repair.Tick(1);
            Check(!repair.Applied&&ReferenceEquals(behavior.Entities[0],original),"native triangle fingerprint mismatch refuses the repair");
            captured[0].Fingerprint="verified";
            repair=new NativeFillRepair(source.Graph,plan,behavior.Entities,source.Scene,()=>captured,p=>float.NaN,p=>10);repair.Tick(1);
            Check(!repair.Applied&&ReferenceEquals(behavior.Entities[0],original),"surface failure leaves original fill intact");
            repair.Clear();foreach(GameEntity e in behavior.Entities)e.Remove(0);
            FakeBehavior eagerBehavior;NativeCapture eagerSource=Fixture(out eagerBehavior);GameEntity eagerOriginal=eagerBehavior.Entities[0];
            var eagerCaptured=new Dictionary<int,RepairCapturedRow>{{0,new RepairCapturedRow{Entity=eagerOriginal,Faces=8,Fingerprint="verified"}}};
            var eagerRepair=new NativeFillRepair(eagerSource.Graph,plan,eagerBehavior.Entities,eagerSource.Scene,()=>eagerCaptured,p=>2,p=>10);
            Check(eagerRepair.ApplyBeforeFirstMapFrame(1,5000)&&eagerRepair.Applied,"published repair completes before the first map frame within its loading budget");
            eagerRepair.Clear();
            int advances=0;
            eagerRepair=new NativeFillRepair(eagerSource.Graph,plan,eagerBehavior.Entities,eagerSource.Scene,()=>advances>=3?eagerCaptured:null,p=>2,p=>10);
            Check(eagerRepair.ApplyBeforeFirstMapFrame(1,5000,()=>advances++)&&advances==3,"published loading advances the pending seam dependency before applying repair");
            eagerRepair.Clear();
            eagerRepair=new NativeFillRepair(eagerSource.Graph,plan,eagerBehavior.Entities,eagerSource.Scene,()=>null,p=>2,p=>10);
            Check(!eagerRepair.ApplyBeforeFirstMapFrame(1,5000)&&ReferenceEquals(eagerBehavior.Entities[0],eagerOriginal),"missing capture driver fails safely without spinning through the loading budget");
            advances=0;
            eagerRepair=new NativeFillRepair(eagerSource.Graph,plan,eagerBehavior.Entities,eagerSource.Scene,()=>null,p=>2,p=>10);
            Check(!eagerRepair.ApplyBeforeFirstMapFrame(1,0,()=>advances++)&&advances==0&&ReferenceEquals(eagerBehavior.Entities[0],eagerOriginal),"expired budget performs no dependency or native repair work");
            eagerRepair=new NativeFillRepair(eagerSource.Graph,plan,eagerBehavior.Entities,eagerSource.Scene,()=>null,p=>2,p=>10);
            Check(!eagerRepair.ApplyBeforeFirstMapFrame(1,5000,()=>{throw new InvalidOperationException("broken seam generation");})&&ReferenceEquals(eagerBehavior.Entities[0],eagerOriginal),"failed seam prerequisite keeps original fill and does not claim publication");
            foreach(GameEntity e in eagerBehavior.Entities)e.Remove(0);
        }
        private static void EnclosedMaskFill()
        {
            FakeBehavior behavior;NativeCapture source=Fixture(out behavior);var style=new NativeLocationStyle();var tool=new CreationTool(source,style);string reason;
            Point2[] square={new Point2(12,10),new Point2(16,10),new Point2(16,14),new Point2(12,14),new Point2(12,10)};
            Check(source.Graph.AddStroke(square,Enumerable.Repeat(10u,4).ToArray(),Enumerable.Repeat(20u,4).ToArray(),out reason),"drawn closed border is available for fill selection");
            tool.Begin("borderfill");tool.BorderFill(new Point2(14,12),false);
            Check(source.Graph.Current.Fills.Length==0&&!tool.Pending,"border-fill hover previews without committing or blocking save");
            tool.BorderFill(new Point2(14,12),true);Check(tool.Applied&&source.Graph.Current.Fills.Length==1&&!source.Graph.Current.Fills[0].ClipToLand,"click fills a drawn enclosure despite all land samples being rejected");
            tool.BorderFill(new Point2(14,12),true);Check(!tool.Applied&&source.Graph.Current.Fills.Length==1,"repeated fill click does not duplicate the same region");tool.HidePreview();
            string path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"aoc-borderfill-"+Guid.NewGuid().ToString("N")+".xml");
            try
            {
                DraftStore.Save(path,"fill",source.Graph.Current);Check(!DraftStore.Load(path,"fill",source.Graph).Fills[0].ClipToLand,"manual fill land-mask override survives reload");
                var xml=new System.Xml.XmlDocument();xml.Load(path);((System.Xml.XmlElement)xml.SelectSingleNode("//Fill")).RemoveAttribute("clipToLand");xml.Save(path);
                Check(DraftStore.Load(path,"fill",source.Graph).Fills[0].ClipToLand,"legacy drafts retain land clipping when the new attribute is absent");
            }
            finally{if(System.IO.File.Exists(path))System.IO.File.Delete(path);}
            var fill=new NativeFillPreview(source,style);for(int i=0;i<200&&!fill.Ready;i++)fill.Tick(1);
            var rendered=GameEntity.All.Where(e=>!e.Removed&&!behavior.Entities.Contains(e)).ToArray();
            Check(fill.Ready&&rendered.Sum(e=>e.Mesh.Faces.Count)>0&&rendered.SelectMany(e=>e.Mesh.Faces).All(f=>f.A.x>=12&&f.A.x<=16&&f.A.y>=10&&f.A.y<=14),"mask-rejected fill renders inside the selected border");
            source.Graph.Undo();fill.Tick(1);Check(source.Graph.Current.Fills.Length==0&&source.Graph.Current.Bridges.Length==4,"Undo removes the fill while retaining its border");
            fill.Clear();tool.Clear();foreach(GameEntity e in behavior.Entities)e.Remove(0);
        }
        private static void LargeBorderBatches()
        {
            FakeBehavior behavior;NativeCapture source=Fixture(out behavior);var preview=new NativePreview(behavior,source);
            GameEntity[] original=behavior.Entities.ToArray();var state=source.Graph.Current.Copy();
            var points=state.Points.ToList();var edges=new List<BorderEdge>();
            for(int i=0;i<300;i++)
            {
                int a=points.Count;float x=(i%15)*3,y=10+(i/15)*3;
                points.Add(new Point2(x,y));points.Add(new Point2(x+1,y));
                edges.Add(new BorderEdge{A=a,B=a+1,Authored=true,Row=-1,Left=10,Right=20});
            }
            state.Points=points.ToArray();state.Bridges=edges.ToArray();source.Graph.Restore(state);Drain(preview);
            var drawn=behavior.Entities.Skip(original.Length).ToArray();
            Check(drawn.Length==10&&drawn.All(e=>e.Mesh.Faces.Count<=32*8),"300 drawn segments publish in meshes bounded to 32 segments");
            Check(drawn.Sum(e=>e.Mesh.Faces.Count)==300*8&&drawn.All(e=>e.Visible),"all large-draft segments are published without missing or duplicated geometry");
            var smaller=state.Copy();smaller.Bridges=smaller.Bridges.Take(3).ToArray();source.Graph.Commit(smaller);Drain(preview);
            Check(behavior.Entities.Skip(original.Length).Sum(e=>e.Mesh.Faces.Count)==3*8,"deletion clears unused trailing border batches");
            source.Graph.Undo();Drain(preview);Check(behavior.Entities.Skip(original.Length).Sum(e=>e.Mesh.Faces.Count)==300*8,"Undo repopulates all large border batches");
            preview.SetEditorOnly(true);Check(behavior.Entities.All(e=>!e.Visible),"editing hides every normal-style batch");preview.SetEditorOnly(false);
            preview.RestoreOriginals();Check(behavior.Entities.SequenceEqual(original)&&drawn.All(e=>e.Removed),"large draft cleanup restores native entities and releases replacements");
            foreach(GameEntity entity in original)entity.Remove(0);
        }
        private static void Transactions()
        {
            FakeBehavior behavior;NativeCapture source=Fixture(out behavior);var preview=new NativePreview(behavior,source);
            GameEntity[] original=source.Rows.Select(r=>r.Original).ToArray();
            Check(source.Graph.Edges.Length==5 && source.CapByNode.Count==6,"capture retains all segments and unique shared caps");
            source.Graph.Selected.Add(0);source.Graph.DeleteSelected();Drain(preview);
            Check(!ReferenceEquals(behavior.Entities[0],original[0]) && ReferenceEquals(behavior.Entities[2],original[2]),"deletion replaces affected row and preserves unrelated row ownership");
            Check(!original[0].Visible && behavior.Entities[0].Visible,"old row hidden when replacement becomes visible");
            Check(behavior.Entities[0].Mesh.Faces.Any(f=>f.A.x>=4f && f.B.x>=4f),"neighboring section in same mesh survives deletion");
            Check(ReferenceEquals(behavior.Entities[0].Mesh.Material,original[0].Mesh.Material) && behavior.Entities[0].Mesh.Order==108,"original material and render order preserved");
            source.Graph.Undo();Drain(preview);
            Check(behavior.Entities.SequenceEqual(original) && original.All(e=>e.Visible&&!e.Removed),"Undo to baseline restores exact original entity objects");
            source.Graph.Selected.Add(2);source.Graph.DeleteSelected();Drain(preview);
            source.Graph.Selected.Add(1);source.Graph.Selected.Add(3);string reason;
            Check(source.Graph.ConnectSelected(out reason),"preview fixture connection accepted");Drain(preview);
            Check(behavior.Entities.Count==original.Length+1 && behavior.Entities.Last().Mesh.Faces.Count>0,"connection publishes a separately owned bridge mesh");
            Check(behavior.Entities.Last().Mesh.Faces.All(f=>f.Color==10||f.Color==20),"connector preserves both source side colours");
            behavior.Alpha=0;source.Graph.BeginDrag(5,new Point2(10,0),8);source.Graph.MoveDrag(new Point2(10,1),out reason);source.Graph.FinishDrag();Drain(preview);
            Check(behavior.Entities.Last().Frame.origin.z==-4.65f,"replacement receives native close-zoom height offset");
            Check(behavior.Entities.Last().Mesh.Faces.Count>0 && behavior.Entities.Last().Mesh.Faces.All(f=>f.A.IsValid&&f.B.IsValid&&f.C.IsValid),"dragged bridge geometry remains finite");
            preview.RestoreOriginals();
            Check(behavior.Entities.SequenceEqual(original) && original.All(e=>e.Visible&&!e.Removed),"teardown removes bridge and restores all original rows");
            Check(GameEntity.All.Where(e=>!original.Contains(e)&&e.Visible).Count()==0,"no visible orphan replacements after teardown");
            foreach(GameEntity e in original)e.Remove(0);
        }
        private static void Failures()
        {
            FakeBehavior behavior;NativeCapture source=Fixture(out behavior);var preview=new NativePreview(behavior,source);GameEntity[] original=behavior.Entities.ToArray();
            source.Graph.BeginDrag(2,new Point2(10,0),8);string reason;source.Graph.MoveDrag(new Point2(10,1),out reason);source.Graph.FinishDrag();
            NativeBindings.RejectSamples=true;bool failed=false;
            try{Drain(preview);}catch(InvalidOperationException){failed=true;}finally{NativeBindings.RejectSamples=false;}
            Check(failed && behavior.Entities.SequenceEqual(original) && original.All(e=>e.Visible),"terrain rejection leaves original published rows intact");
            Check(BorderGraph.Equal(source.Graph.Current,source.Graph.Original),"rejected native preparation restores last published edit state");
            source.Graph.Selected.Add(0);source.Graph.DeleteSelected();
            GameEntity.FailFrameCountdown=1;failed=false;
            try{Drain(preview);}catch(InvalidOperationException){failed=true;}finally{GameEntity.FailFrameCountdown=-1;}
            Check(failed && behavior.Entities.SequenceEqual(original) && original.All(e=>e.Visible&&!e.Removed),"publication failure rolls back native row ownership");
            Check(GameEntity.All.Where(e=>!original.Contains(e)&&e.Visible).Count()==0,"failed publication leaves no visible duplicate geometry");
            source.Graph.Selected.Add(0);source.Graph.Selected.Add(3);source.Graph.DeleteSelected();Drain(preview);
            GameEntity[] replacements=behavior.Entities.Where(e=>!original.Contains(e)).ToArray();
            GameEntity.FailRemoveCountdown=1;preview.RestoreOriginals();
            Check(behavior.Entities.SequenceEqual(original) && original.All(e=>e.Visible&&!e.Removed),"failure removing one replacement does not prevent restoring later rows");
            NativeCleanup.Drain();Check(replacements.All(e=>e.Removed),"deferred native cleanup retries failed entity without double-removing others");
            preview.RestoreOriginals();foreach(GameEntity e in original)e.Remove(0);
        }
        private static void CoastCompatibility()
        {
            FakeBehavior behavior; NativeCapture source=Fixture(out behavior);
            var coast=new bool[5]; coast[0]=true;
            var joins=new bool[6]; joins[0]=joins[1]=true;
            int queries=0;
            var adapter=new NativeCoastCompatibility(source.Graph,coast,joins,p=>
            {
                queries++; float z;
                if(!AgesOfCalradia.CoastSurfaceFix.CoastSurfacePolicy.Project(true,20f,true,25f,out z)) throw new Exception("Surface policy rejected valid water.");
                return new Vec3(p.x,p.y,z);
            });
            EditSnapshot state=source.Graph.Current.Copy(); state.Points[1]=new Point2(4,1);
            adapter.Prepare(state);
            Check(adapter.Point(new Vec2(2,.5f),source.Graph.Edges[0]).z==30f,"moved coast uses confirmed water surface plus original five-unit offset");
            Check(adapter.Point(new Vec2(6,0),source.Graph.Edges[1]).z==5.6f,"ordinary inland sample retains native terrain projection");
            Vec2 shared=new Vec2(4,1); int before=queries;
            Check(adapter.Point(shared,source.Graph.Edges[0]).z==adapter.Point(shared,source.Graph.Edges[1]).z && queries==before+1,"coast and adjoining inland edge share one corrected junction sample");
            Mesh cap=Mesh.CreateMesh(true); UIntPtr handle=cap.LockEditDataWrite();
            adapter.EmitCap(cap,handle,1,source.Graph.Edges[0],state); cap.UnlockEditDataWrite(handle);
            Vec2 left=new Vec2(-4,-1),right=new Vec2(4,-1);left.Normalize();right.Normalize();
            Check(cap.Faces.Count>0 && cap.Faces.SelectMany(f=>new[]{f.A,f.B,f.C}).All(p=>
                ((p.x-4)*left.x+(p.y-1)*left.y)<=.00001f && ((p.x-4)*right.x+(p.y-1)*right.y)<=.00001f),"moved coastal cap clips against current adjacent directions");
            Check(cap.Faces.All(f=>Math.Abs(f.A.z-30f)<.0001f&&Math.Abs(f.B.z-30f)<.0001f&&Math.Abs(f.C.z-30f)<.0001f),"clipped coastal cap keeps corrected surface without another lift");
            var preview=new NativePreview(behavior,source,adapter); GameEntity[] original=behavior.Entities.ToArray();
            source.Graph.Commit(state); Drain(preview);
            Check(behavior.Entities[0].Mesh.Faces.Any(f=>f.A.z==30f),"coastal adapter participates in published edited row");
            source.Graph.Undo(); Drain(preview);
            Check(behavior.Entities.SequenceEqual(original),"coastal edit Undo restores captured originals exactly");
            foreach(GameEntity entity in original)entity.Remove(0);
            float rejected;
            Check(!AgesOfCalradia.CoastSurfaceFix.CoastSurfacePolicy.Project(true,20f,true,float.NaN,out rejected),"invalid confirmed water height rejects corrected projection");
        }
        private static void EditLines()
        {
            FakeBehavior behavior;NativeCapture source=Fixture(out behavior);
            GameEntity[] originals=behavior.Entities.ToArray();
            var adapter=new NativeCoastCompatibility(source.Graph,new bool[5],new bool[6],p=>new Vec3(p.x,p.y,30f));
            var lines=new NativeEditLines(source,adapter);MatrixFrame frame=MatrixFrame.Identity;
            lines.Refresh(source.Graph.Current,frame);
            Check(lines.Busy && GameEntity.All.Count(e=>!e.Removed&&!originals.Contains(e))==1,"activation uploads only one bounded green-line batch");
            Vec3 grab;
            Check(lines.Pick(new Vec3(18,0,60),new Vec3(18,0,-20),out grab)==-1,"unprepared green lines have no invisible click targets");
            while(lines.Busy)lines.Refresh(source.Graph.Current,frame);
            GameEntity[] first=GameEntity.All.Where(e=>!e.Removed&&!originals.Contains(e)).ToArray();
            Check(lines.Pick(new Vec3(18,.9f,60),new Vec3(18,.9f,-20),out grab)==-1 && lines.Pick(new Vec3(18,.9f,60),new Vec3(18,.9f,-20),out grab,true)==4,"Delete mode accepts near-line clicks without widening precision dragging");
            Check(first.Length==3 && first.All(e=>e.Mesh.Faces.Any(f=>f.Color==NativeEditLines.Color)),"green editing lines cover all rows without needing selection");
            Check(first.SelectMany(e=>e.Mesh.Faces).Where(f=>f.Color==NativeEditLines.Color).All(f=>Math.Abs(f.A.y)<.23f&&Math.Abs(f.B.y)<.23f&&Math.Abs(f.C.y)<.23f),"precision edit line stays thin instead of covering the coastline");
            int count=GameEntity.All.Count;frame.origin.z=-2;lines.Refresh(source.Graph.Current,frame);
            Check(GameEntity.All.Count==count && first.All(e=>e.Frame.origin.z==-2),"unchanged green lines reuse meshes while following native zoom");
            int pickSamples=NativeBindings.SampleCalls;
            Check(lines.Pick(new Vec3(2,10,13.6f),new Vec3(2,-10,-6.4f),out grab)==0 && Math.Abs(grab.z-3.6f)<.001f,"angled ray picks visible elevated border with zoom offset");
            Check(lines.Pick(new Vec3(50,0,60),new Vec3(50,0,-20),out grab)==-1 && NativeBindings.SampleCalls==pickSamples,"line picking rejects distant batches without native terrain queries");
            Point2 projected;
            Check(OverlayHitMesh.ProjectDrag(new Vec3(3,10,13.6f),new Vec3(3,-10,-6.4f),3.6f,out projected) && Math.Abs(projected.X-3)<.001f && Math.Abs(projected.Y)<.001f,"drag follows the grabbed height plane instead of uneven terrain");
            Check(!OverlayHitMesh.ProjectDrag(new Vec3(0,0,10),new Vec3(10,0,10),3.6f,out projected),"parallel drag ray is rejected without a pointer jump");
            EditSnapshot moved=source.Graph.Current.Copy();moved.Points[0]=new Point2(0,1);
            lines.Refresh(moved,frame);while(lines.Busy)lines.Refresh(moved,frame);
            Check(first.Count(e=>e.Removed)==1 && GameEntity.All.Last().Mesh.Faces.Any(f=>f.A.y>.8f),"dragged green line follows published geometry and rebuilds only affected row");
            EditSnapshot deleted=moved.Copy();deleted.Deleted[2]=true;lines.Refresh(deleted,frame);while(lines.Busy)lines.Refresh(deleted,frame);
            Check(GameEntity.All.Where(e=>!e.Removed&&!originals.Contains(e)).SelectMany(e=>e.Mesh.Faces).Where(f=>f.Color==NativeEditLines.Color).All(f=>!(f.A.x>8.01f&&f.A.x<11.99f)),"deleted section has no selectable-looking green line");
            Check(lines.Pick(new Vec3(10,0,60),new Vec3(10,0,-20),out grab)==-1,"deleted line cannot be selected through a stale hit target");
            Check(lines.Pick(new Vec3(10,.9f,60),new Vec3(10,.9f,-20),out grab,true)==-1,"deleted lines lose their forgiving deletion target too");
            BorderEdge edge=source.Graph.Edges[2];
            EditSnapshot connected=deleted.Copy();connected.Bridges=new[]{new BorderEdge{A=edge.A,B=edge.B,Row=-1,Left=edge.Left,Right=edge.Right}};
            lines.Refresh(connected,frame);while(lines.Busy)lines.Refresh(connected,frame);
            Check(GameEntity.All.Last().Mesh.Faces.Any(f=>f.Color==NativeEditLines.ConnectedColor&&f.A.x>8.01f&&f.A.x<11.99f),"new connection receives a distinct cyan editing line");
            Check(lines.Pick(new Vec3(10,0,60),new Vec3(10,0,-20),out grab)==source.Graph.Edges.Length,"visible new connection is selectable by its bridge identity");
            GameEntity oldLink=GameEntity.All.Last();EditSnapshot replaced=deleted.Copy();replaced.Points[0]=new Point2(0,2);
            lines.Refresh(replaced,frame);
            Check(oldLink.Removed && lines.Busy,"deleted connection overlay disappears before unrelated pending line updates");
            Check(lines.Pick(new Vec3(2,0,60),new Vec3(2,0,-20),out grab)==-1 && lines.Pick(new Vec3(18,0,60),new Vec3(18,0,-20),out grab)==4,"pending changed rows are not pickable while unchanged visible rows remain interactive");
            replaced=replaced.Copy();replaced.Points[0]=new Point2(0,3);lines.Refresh(replaced,frame);
            while(lines.Busy)lines.Refresh(replaced,frame);
            Check(oldLink.Removed,"superseding an unfinished overlay update cannot revive a deleted connection");
            lines.Clear();
            Check(GameEntity.All.All(e=>e.Removed||originals.Contains(e)) && originals.All(e=>!e.Removed),"closing green overlay preserves all original border entities");
            Check(lines.Pick(new Vec3(18,0,60),new Vec3(18,0,-20),out grab)==-1,"closing editor clears all line hit geometry");
            lines.Refresh(source.Graph.Current,frame);lines.Clear();
            Check(!lines.Busy && GameEntity.All.All(e=>e.Removed||originals.Contains(e)),"closing during activation cancels remaining batches and removes partial overlay");
            foreach(GameEntity entity in originals)entity.Remove(0);
        }
        private static void SelectionMarkers()
        {
            FakeBehavior behavior; NativeCapture source=Fixture(out behavior);
            var coasts=Enumerable.Repeat(true,5).ToArray();var joins=Enumerable.Repeat(true,6).ToArray();
            var adapter=new NativeCoastCompatibility(source.Graph,coasts,joins,p=>new Vec3(p.x,p.y,30f));
            GameEntity[] original=behavior.Entities.ToArray();source.Graph.Selected.Add(0);
            int samplesBefore=NativeBindings.SampleCalls;
            NativeSelection.Refresh(source,behavior,-1,source.Graph.Current,adapter);
            Check(NativeBindings.SampleCalls-samplesBefore<250,"activation bounds native terrain calls instead of sampling every ring vertex");
            GameEntity marker=GameEntity.All.Last();
            Check(NativeSelection.PickEndpoint(new Vec3(20,0,60),new Vec3(20,0,-20))==5,"open endpoint is actionable in drag mode without toolbar activation");
            Check(NativeSelection.PickEndpoint(new Vec3(22.3f,0,60),new Vec3(22.3f,0,-20))==5,"small endpoint near-miss snaps to the intended open point");
            Check(NativeSelection.PickEndpoint(new Vec3(24,0,60),new Vec3(24,0,-20),.15f)==5,"screen-angle snapping catches a near miss outside the marker");
            Check(NativeSelection.PickEndpoint(new Vec3(24,0,40),new Vec3(24,0,-20),.15f)==-1,"zooming closer reduces world-space snap reach for precision");
            Check(NativeSelection.PickEndpoint(new Vec3(26,0,100),new Vec3(26,0,-20),.1f)==5,"zooming out keeps small endpoints within a usable screen-space target");
            Check(NativeSelection.PickEndpoint(new Vec3(20,0,20),new Vec3(20,0,-20),.15f)==-1,"snapping cannot pick an endpoint behind the pointer ray");
            Check(marker.Visible && marker.Mesh.Faces.Any(f=>f.Color==NativeSelection.OpenColor) && marker.Mesh.Faces.Any(f=>f.Color==NativeSelection.AttachedColor),"selection shows green open and amber attached endpoint rings");
            Check(marker.Mesh.Faces.All(f=>f.A.z>=30.34f&&f.B.z>=30.34f&&f.C.z>=30.34f),"coastal selection outlines and rings remain above corrected coast surface");
            NativeSelection.Refresh(source,behavior,-1,source.Graph.Current,adapter,connectionTarget:5);
            Check(GameEntity.All.Last().Mesh.Faces.Any(f=>f.Color==NativeSelection.HoverColor) && NativeSelection.PickEndpoint(new Vec3(22,0,60),new Vec3(22,0,-20))==5,"hovered endpoint brightens and enlarges its visible click target");
            NativeSelection.Refresh(source,behavior,-1,source.Graph.Current,adapter);marker=GameEntity.All.Last();
            NativeSelection.Refresh(source,behavior,0,source.Graph.Current,adapter,deleteMode:true);
            Check(GameEntity.All.Last().Mesh.Faces.All(f=>f.Color==NativeSelection.InvalidColor) && NativeSelection.PickEndpoint(new Vec3(20,0,60),new Vec3(20,0,-20))==-1,"Delete mode highlights only the target red and removes competing endpoint markers");
            NativeSelection.Refresh(source,behavior,-1,source.Graph.Current,adapter);marker=GameEntity.All.Last();
            behavior.Alpha=0;NativeSelection.Refresh(source,behavior,-1,source.Graph.Current,adapter);
            Check(!marker.Removed && marker.Frame.origin.z==-4.65f,"cached endpoint overlay follows native zoom without rebuilding");
            NativeSelection.Refresh(source,behavior,-1,source.Graph.Current,adapter,true,0);
            GameEntity connect=GameEntity.All.Last();
            Check(connect.Mesh.Faces.Any(f=>f.Color==NativeSelection.StartColor && f.A.x==0 && f.A.y==0),"selected endpoint is a filled diamond with an exact visible center");
            Check(NativeSelection.PickEndpoint(new Vec3(3,0,60),new Vec3(3,0,-20))==-1,"compact diamond does not cover the old oversized ring area");
            Check(NativeSelection.PickEndpoint(new Vec3(10,0,35.7f),new Vec3(-10,0,15.7f))==0,"angled click targets raised coastal endpoint at displayed zoom height rather than ground below");
            Check(NativeSelection.PickEndpoint(new Vec3(20,0,60),new Vec3(20,0,-20))==5,"disconnected opposite endpoint can be clicked at its visible center");
            Check(NativeSelection.PickEndpoint(new Vec3(10,10,60),new Vec3(10,10,-20))==-1,"click away from displayed endpoint rings is rejected");
            Check(marker.Removed && connect.Mesh.Faces.Count==112 && connect.Mesh.Faces.All(f=>f.Color==NativeSelection.OpenColor||f.Color==NativeSelection.StartColor||f.Color==NativeSelection.MarkerOutline||f.Color==NativeSelection.MarkerHighlight),"Connect mode shows every open endpoint with cyan start and no map-wide outlines");
            Check(connect.Mesh.Faces.Any(f=>f.Color==NativeSelection.MarkerOutline) && connect.Mesh.Faces.Any(f=>f.Color==NativeSelection.MarkerHighlight),"endpoint markers include dark and white contrast bands");
            EditSnapshot deleted=source.Graph.Current.Copy();deleted.Deleted[2]=true;
            NativeSelection.Refresh(source,behavior,-1,deleted,adapter,true,0);
            Check(GameEntity.All.Last().Mesh.Faces.Count==208,"Connect markers use displayed snapshot topology after deletion");
            int from=NativeSelection.PickEndpoint(new Vec3(8,0,60),new Vec3(8,0,-20)),to=NativeSelection.PickEndpoint(new Vec3(12,0,60),new Vec3(12,0,-20));
            source.Graph.Restore(deleted);string connectionReason;
            EditSnapshot beforePreview=source.Graph.Current.Copy();int revisionBefore=source.Graph.Revision;
            bool allowed=source.Graph.CanConnectEndpoints(from,to,out connectionReason);
            NativeSelection.Refresh(source,behavior,-1,source.Graph.Current,adapter,true,from);
            Check(GameEntity.All.Last().Mesh.Faces.Any(f=>f.Color==NativeSelection.OpenColor && f.A.x>9.6f && f.A.x<10.4f) && BorderGraph.Equal(beforePreview,source.Graph.Current),"first endpoint immediately shows a legal destination guide without committing");
            NativeSelection.Refresh(source,behavior,-1,source.Graph.Current,adapter,true,from,connectionTarget:to,connectionValid:allowed);
            Check(allowed && BorderGraph.Equal(beforePreview,source.Graph.Current) && revisionBefore==source.Graph.Revision,"connection validation previews without editing graph or history");
            Check(GameEntity.All.Last().Mesh.Faces.Any(f=>f.Color==NativeSelection.OpenColor && f.A.x>9.6f && f.A.x<10.4f),"allowed connection draws dashed preview across the gap before clicking");
            bool rejected=!source.Graph.CanConnectEndpoints(0,5,out connectionReason);
            NativeSelection.Refresh(source,behavior,-1,source.Graph.Current,adapter,true,0,connectionTarget:5,connectionValid:false);
            Check(rejected && GameEntity.All.Last().Mesh.Faces.Any(f=>f.Color==NativeSelection.InvalidColor) && BorderGraph.Equal(beforePreview,source.Graph.Current),"rejected connection displays red preview without applying a bridge");
            Check(from==2 && to==3 && source.Graph.ConnectEndpoints(from,to,out connectionReason),"clicking two nearby displayed endpoints selects their centers and creates the missing connection");
            source.Graph.Selected.Add(source.Graph.EdgeCount-1);
            NativeSelection.Refresh(source,behavior,-1,source.Graph.Current,adapter,false,-1);
            GameEntity confirmation=GameEntity.All.Last();
            Check(confirmation.Mesh.Faces.Any(f=>f.Color==0xFF808000u) && confirmation.Mesh.Faces.Any(f=>f.Color==NativeSelection.StartColor&&f.A.x==8) && confirmation.Mesh.Faces.Any(f=>f.Color==NativeSelection.StartColor&&f.A.x==12),"successful connection retains yellow link outline and cyan markers at both joined ends");
            NativeSelection.Refresh(source,behavior,-1,source.Graph.Current,adapter,false,-1);
            Check(!confirmation.Removed,"connection highlight persists across frames");
            source.Graph.Selected.Clear();NativeSelection.Refresh(source,behavior,-1,source.Graph.Current,adapter,false,-1);
            Check(GameEntity.All.Last().Mesh.Faces.Any(f=>f.Color==NativeSelection.StartColor) && NativeSelection.PickEndpoint(new Vec3(8,0,60),new Vec3(8,0,-20))==-1,"completed joins retain cyan markers without selection and cannot be mistaken for open ends");
            source.Graph.Undo();NativeSelection.Refresh(source,behavior,-1,source.Graph.Current,adapter,true,-1);
            Check(confirmation.Removed && !GameEntity.All.Last().Mesh.Faces.Any(f=>f.Color==NativeSelection.StartColor),"Undo removes connection confirmation markers");
            NativeSelection.Clear();
            Check(NativeSelection.PickEndpoint(new Vec3(0,0,60),new Vec3(0,0,-20))==-1,"closing overlay clears stale connection hit targets");
            Check(behavior.Entities.SequenceEqual(original) && original.All(e=>e.Visible&&!e.Removed) && GameEntity.All.Where(e=>!original.Contains(e)&&!e.Removed).Count()==0,"selection cleanup removes overlays and preserves original border entities");
            NativeBindings.RejectSamples=true;
            try { NativeSelection.Refresh(source,behavior,0,source.Graph.Current,adapter); }
            finally { NativeBindings.RejectSamples=false; NativeSelection.Clear(); }
            Check(original.All(e=>e.Visible&&!e.Removed),"selection sampling failure leaves border editing geometry intact");
            foreach(GameEntity entity in original)entity.Remove(0);
        }
        private static int Main()
        {
            try{RepairRows();EnclosedMaskFill();LargeBorderBatches();HalfCapCapture();Transactions();Failures();CoastCompatibility();EditorVisibility();KeepEditsOnClose();CreationAndFill();RibbonCorners();EditLines();SelectionMarkers();Console.WriteLine("PASS "+_checks+" native-adapter transaction assertions (fake scene; not visual validation)");return 0;}
            catch(Exception error){Console.Error.WriteLine(error);return 1;}
        }
        private static void EditorVisibility()
        {
            FakeBehavior behavior;NativeCapture source=Fixture(out behavior);var preview=new NativePreview(behavior,source);
            GameEntity[] originals=behavior.Entities.ToArray();originals[1].SetVisibilityExcludeParents(false);
            preview.SetEditorOnly(true);
            Check(behavior.Entities.All(e=>!e.Visible),"edit mode hides normal border ribbons");
            source.Graph.Selected.Add(2);source.Graph.DeleteSelected();Drain(preview);
            Check(behavior.Entities.All(e=>!e.Visible),"publishing edits does not reintroduce normal ribbons");
            preview.SetEditorOnly(false);
            Check(behavior.Entities[0].Visible&&!behavior.Entities[1].Visible&&behavior.Entities[2].Visible,"closing restores the corresponding pre-editor visibility flags");
            preview.SetEditorOnly(true);source.Graph.Undo();Drain(preview);
            Check(behavior.Entities.All(e=>!e.Visible),"Undo retains the single-border editing view");
            preview.SetEditorOnly(false);
            Check(behavior.Entities.SequenceEqual(originals)&&originals[0].Visible&&!originals[1].Visible&&originals[2].Visible,"Undo and close restore original entities and visibility exactly");
            foreach(GameEntity entity in originals)entity.Remove(0);
        }
        private static void KeepEditsOnClose()
        {
            FakeBehavior behavior;NativeCapture source=Fixture(out behavior);var preview=new NativePreview(behavior,source);
            GameEntity[] original=behavior.Entities.ToArray();preview.SetEditorOnly(true);
            source.Graph.BeginDrag(2,new Point2(10,0),8);string reason;
            Check(source.Graph.MoveDrag(new Point2(10,1),out reason),"close-retention fixture moves the border");
            source.Graph.FinishDrag();Drain(preview);EditSnapshot saved=source.Graph.Current.Copy();
            GameEntity[] applied=behavior.Entities.ToArray();
            preview.AbortPending();preview.SetEditorOnly(false);
            Check(behavior.Entities.SequenceEqual(applied) && applied.All(e=>e.Visible) && BorderGraph.Equal(saved,preview.Published),"closing retains visible edited entities and published geometry");
            Check(applied.SelectMany(e=>e.Mesh.Faces).Any(f=>f.A.y>.5f) && original.Where(e=>!applied.Contains(e)).All(e=>!e.Visible),"normal-style moved border remains displaced and old geometry stays hidden");
            preview.SetEditorOnly(true);preview.SetEditorOnly(false);
            Check(behavior.Entities.SequenceEqual(applied)&&applied.All(e=>e.Visible),"reopening and closing cannot erase applied changes");
            preview.RestoreOriginals();foreach(GameEntity entity in original)entity.Remove(0);
        }
        private static void HalfCapCapture()
        {
            var capture=new NativeCapture{InAdvance=true}; var mesh=Mesh.CreateMesh(true);
            capture.BeginSegment(mesh,Vec2.Zero,new Vec2(4,0));
            for(int half=0;half<2;half++)
            {
                capture.BeginCap(Vec2.Zero);
                capture.Triangle(mesh,new Vec3(0,0,5),new Vec3(1,0,5),new Vec3(0,1,5),Vec2.Zero,Vec2.Zero,Vec2.Zero,(uint)(half+1));
                capture.EndCap();
            }
            Check(capture.Caps.Count==1 && capture.Caps[0].Faces.Count==2,"two half-cap hooks merge into one endpoint capture");
            capture.BeginCap(new Vec2(4,0)); capture.EndCap();
            Check(capture.Caps.Count==2 && capture.Caps[1].Faces.Count==0,"fully clipped half-cap retains endpoint identity");
        }
        private static void RibbonCorners()
        {
            FakeBehavior behavior;NativeCapture source=Fixture(out behavior);GameEntity[] original=behavior.Entities.ToArray();string reason;
            Check(source.Graph.AddStroke(new[]{new Point2(0,10),new Point2(3,10),new Point2(3,13)},new uint[]{30,40},new uint[]{50,60},out reason),"joined-ribbon fixture creates a bent authored stroke");
            EditSnapshot state=source.Graph.Current;BorderEdge a=state.Bridges[0],b=state.Bridges[1];
            Point2 left=RibbonJoin.Offset(state,a,a.B,.8f),right=RibbonJoin.Offset(state,b,b.A,.8f);
            Check((left-right).Length<.00001f && Math.Abs(left.X+.8f)<.001f&&Math.Abs(left.Y-.8f)<.001f,"right-angle border sections share the exact miter instead of separated square ends");
            var preview=new NativePreview(behavior,source);Drain(preview);Mesh ribbon=behavior.Entities.Last().Mesh;
            Point2 corner=state.Points[a.B]+left;
            var touching=ribbon.Faces.Where(f=>new[]{f.A,f.B,f.C}.Any(p=>Math.Abs(p.x-corner.X)<.0001f&&Math.Abs(p.y-corner.Y)<.0001f)).ToArray();
            Check(touching.Any(f=>f.Color==30)&&touching.Any(f=>f.Color==40),"published adjacent ribbons meet at one sampled corner with their own local colours");
            state=state.Copy();state.Points[b.B]=new Point2(.1f,10.2f);
            left=RibbonJoin.Offset(state,a,a.B,.8f);right=RibbonJoin.Offset(state,b,b.A,.8f);
            Check((left-right).Length<.0001f&&left.Length<=1.6001f,"sharp turns retain shared corners with a bounded miter");
            state=source.Graph.Current.Copy();state.Bridges=new[]{a};
            left=RibbonJoin.Offset(state,a,a.B,.8f);
            Check(Math.Abs(left.X)<.0001f&&Math.Abs(left.Y-.8f)<.0001f,"deleting the adjoining section restores a clean open end");
            var native=new BorderEdge{A=a.A,B=a.B,Row=0,Authored=false};
            Check(Math.Abs(RibbonJoin.Offset(source.Graph.Current,native,native.B,.8f).X)<.0001f,"captured native ribbons do not inherit custom join geometry");
            preview.RestoreOriginals();foreach(GameEntity e in original)e.Remove(0);
        }
        private static void CreationAndFill()
        {
            FakeBehavior behavior;NativeCapture source=Fixture(out behavior);var style=new NativeLocationStyle();
            GameEntity[] original=behavior.Entities.ToArray();var tool=new CreationTool(source,style);
            tool.Begin("add");tool.Down(new Point2(0,10));tool.Drag(new Point2(3,10));tool.Drag(new Point2(6,12));
            tool.Preview(new Point2(6,12));
            tool.FrameHeight=-4.65f;tool.Preview(new Point2(6,12));
            Check(Math.Abs(GameEntity.All.Last().Frame.origin.z+4.65f)<.001f,"freehand ghost follows native zoom height even with a stationary cursor");
            Point2 cursor;
            MatrixFrame overhead=MatrixFrame.Identity;
            overhead.rotation.RotateAboutSide((float)Math.PI*.5f);overhead.rotation.RotateAboutForward(-.7f);overhead.rotation.RotateAboutSide(-(float)Math.PI*.5f);
            Check(Math.Abs(overhead.rotation.u.x)<.0001f&&Math.Abs(overhead.rotation.u.y)<.0001f&&Math.Abs(overhead.rotation.u.z-1)<.0001f,"native camera rotation formula at 90-degree elevation looks straight down while retaining bearing");
            Check(DrawingPointer.Project(new Vec3(0,-10,10),new Vec3(0,10,-10),new Point2(0,0),p=>5.4f,out cursor)&&Math.Abs(cursor.Y+5.4f)<.02f,"angled mouse ray lands directly on the raised border surface rather than the ground beneath");
            Check(DrawingPointer.Project(new Vec3(0,-10,10),new Vec3(0,10,-10),new Point2(0,0),p=>.75f,out cursor)&&Math.Abs(cursor.Y+.75f)<.02f,"cursor alignment includes the close-zoom border drop");
            Check(DrawingPointer.Project(new Vec3(0,-10,10),new Vec3(0,10,-10),new Point2(0,0),p=>5.4f+.2f*p.Y,out cursor)&&Math.Abs(cursor.Y+4.5f)<.02f,"cursor alignment refines against sloped drawing terrain");
            Check(!DrawingPointer.Project(new Vec3(0,0,10),new Vec3(10,0,10),new Point2(0,0),p=>5.4f,out cursor),"parallel drawing ray is rejected without relocating the stroke");
            Check(source.Graph.Current.Bridges.Length==0,"freehand ghost does not mutate borders before release");
            tool.Release(new Point2(8,12));
            Check(source.Graph.Current.Bridges.Length==3 && source.Graph.Current.Points.Length>source.Graph.Original.Points.Length,"freehand release adds the complete sampled stroke");
            string strokeFile=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"aoc-freehand-"+Guid.NewGuid().ToString("N")+".xml");
            try
            {
                DraftStore.Save(strokeFile,"freehand-test",source.Graph.Current);
                EditSnapshot loaded=DraftStore.Load(strokeFile,"freehand-test",source.Graph);
                Check(BorderGraph.Equal(loaded,source.Graph.Current)&&loaded.Bridges.All(e=>e.Authored),"released freehand stroke roundtrips through the real draft writer and loader");
            }
            finally{if(System.IO.File.Exists(strokeFile))System.IO.File.Delete(strokeFile);}
            var preview=new NativePreview(behavior,source);preview.SetEditorOnly(true);Drain(preview);
            NativeSelection.Refresh(source,behavior,-1,preview.Published);NativeSelection.Clear();
            preview.SetEditorOnly(false);
            Check(behavior.Entities.Count==original.Length+1 && behavior.Entities.Last().Visible,"new freehand border publishes normally after closing");
            source.Graph.Undo();Drain(preview);Check(source.Graph.Current.Bridges.Length==0,"one Undo removes the complete freehand stroke");
            tool.Begin("add");tool.Down(new Point2(20,0));tool.Drag(new Point2(18,4));tool.Drag(new Point2(.2f,.3f));
            tool.Release(new Point2(0,0),true);
            Check(tool.Applied && source.Graph.Current.Bridges.Last().B==0 && Point2.Same(source.Graph.Current.Points[source.Graph.Current.Bridges.Last().B],new Point2(0,0)),"snapped release preserves the exact existing endpoint below the freehand sampling threshold");
            Check(source.Graph.AtNode(0).Count()==2&&source.Graph.AtNode(5).Count()==2,"freehand can start and finish on existing open endpoints without a gap");
            source.Graph.Undo();Check(source.Graph.AtNode(0).Count()==1&&source.Graph.AtNode(5).Count()==1,"Undo snapped stroke reopens both original endpoints");
            foreach(string mode in new[]{"coast","political"})
            {
                tool.Begin(mode);Point2 guided;
                float x=mode=="coast"?9:4;
                Check(tool.Guide(new Point2(x,20),out guided),mode+" drawing finds its boundary");
                tool.Down(guided);
                for(int y=21;y<=24;y++){tool.Guide(new Point2(x,y),out guided);tool.Drag(guided);}
                tool.Release(guided);
                Check(tool.Applied&&source.Graph.Current.Bridges.Length<=2&&source.Graph.Current.Bridges.Length>0,mode+" boundary refinement preserves the stroke while using fewer segments");
                string guideFile=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"aoc-guide-"+Guid.NewGuid().ToString("N")+".xml");
                try{DraftStore.Save(guideFile,"guide",source.Graph.Current);Check(BorderGraph.Equal(DraftStore.Load(guideFile,"guide",source.Graph),source.Graph.Current),mode+" stroke survives save and reload");}
                finally{if(System.IO.File.Exists(guideFile))System.IO.File.Delete(guideFile);}
                source.Graph.Undo();Check(source.Graph.Current.Bridges.Length==0,mode+" stroke undoes as one gesture");
                tool.Down(guided);tool.Drag(guided+new Point2(0,1));tool.Drag(guided+new Point2(0,12));
                Check(tool.Paused&&source.Graph.Current.Bridges.Length==0,mode+" large discontinuity pauses without committing or losing the gesture");
                tool.Release(null);Check(tool.Applied&&source.Graph.Current.Bridges.Length==1,mode+" release while paused keeps the valid section");
                source.Graph.Undo();tool.Down(guided);tool.Drag(guided+new Point2(0,12));tool.Drag(guided+new Point2(0,1));
                Check(!tool.Paused,mode+" tracing resumes after returning near the tip");tool.Clear();
            }
            tool.Begin("fill");tool.Down(new Point2(0,8));tool.Drag(new Point2(12,8));tool.Drag(new Point2(12,16));tool.Drag(new Point2(0,16));tool.Release(new Point2(0,8));
            Check(source.Graph.Current.Fills.Length==1,"freehand closed outline commits one fill patch");
            int fillCount=source.Graph.Current.Fills.Length;
            tool.Down(new Point2(9.9f,20));tool.Drag(new Point2(11.5f,20));tool.Drag(new Point2(11.5f,21.5f));
            string rejectedFill=tool.Release(new Point2(9.9f,20));
            Check(source.Graph.Current.Fills.Length==fillCount+1 && tool.Applied && rejectedFill.Contains("added"),"manual fill accepts an outline rejected by the political land mask");
            source.Graph.Undo();
            source.Graph.Current.Fills[0].ClipToLand=true; // Exercise legacy clipping below.
            tool.Down(new Point2(0,20));tool.Drag(new Point2(3,20));tool.Clear();
            Check(source.Graph.Current.Fills.Length==fillCount,"cancelling an unfinished fill leaves committed areas unchanged");
            var fills=new NativeFillPreview(source,style);fills.Tick(1);
            Check(!fills.Ready,"fill preparation spreads a larger patch across multiple ticks");
            int ticks=0;while(!fills.Ready && ticks++<600)fills.Tick(1);
            Check(fills.Ready,"bounded fill upload completes");
            var rendered=GameEntity.All.Where(e=>!e.Removed&&!original.Contains(e)).ToArray();
            Check(rendered.Length>0 && rendered.All(e=>e.Mesh.Faces.Count<=32),"fill uploads at most sixteen double-sided triangles per mesh");
            Check(rendered.SelectMany(e=>e.Mesh.Faces).All(f=>f.A.x<10&&f.B.x<10&&f.C.x<10),"fill rejects water fringe triangles independently of territory lookup");
            Check(rendered.SelectMany(e=>e.Mesh.Faces).Select(f=>f.Color).Distinct().Count()==2,"fill follows different local territory colours across the area");
            fills.Tick(0);Check(rendered.All(e=>!e.Visible),"custom fills follow native political-layer visibility when zoomed out of that presentation");
            fills.Tick(1);Check(rendered.All(e=>e.Visible),"custom fills reappear with the political layer");
            source.Graph.DeleteFill(0);fills.Tick(1);Check(fills.Ready&&rendered.All(e=>e.Removed),"deleting a fill removes its rendered geometry");
            source.Graph.Undo();fills.Tick(1);fills.Clear();tool.Clear();preview.RestoreOriginals();
            Check(GameEntity.All.All(e=>e.Removed||original.Contains(e)),"closing during fill preparation releases staged and visible entities");
            foreach(GameEntity e in original)e.Remove(0);
            string reason;
            Check(!FillGeometry.ValidatePoints(new[]{new Point2(0,0),new Point2(4,4),new Point2(0,4),new Point2(4,0)},out reason),"self-crossing freehand fill outline is rejected");
            Point2[] concave={new Point2(0,0),new Point2(4,0),new Point2(4,4),new Point2(2,2),new Point2(0,4)};
            Check(FillGeometry.Triangulate(concave).Count==3,"concave freehand outline triangulates without filling its notch");
        }
    }
}





