using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace Aoc.BorderEditPrototype
{
    // Owns replacement entities only. The original hidden rows remain intact for exact restoration.
    // Native work is main-thread-only; a complete batch is prepared before visible/list changes.
    internal sealed class NativePreview
    {
        private readonly object _behavior;
        private readonly NativeCapture _source;
        private readonly NativeCoastCompatibility _coast;
        private readonly List<GameEntity> _live;
        private readonly Dictionary<int,GameEntity> _replacements = new Dictionary<int,GameEntity>();
        private readonly Dictionary<int,GameEntity> _staged = new Dictionary<int,GameEntity>();
        private bool _editorOnly;
        private readonly Dictionary<GameEntity,bool> _normalVisibility=new Dictionary<GameEntity,bool>();
        internal void SetEditorOnly(bool enabled)
        {
            if(enabled==_editorOnly)return;
            // Capture every flag before hiding anything, so a partial native failure is reversible.
            if(enabled)foreach(GameEntity entity in _live)_normalVisibility[entity]=entity.GetVisibilityExcludeParents();
            _editorOnly=enabled;
            foreach(GameEntity entity in _live)
            {
                if(enabled)
                { entity.SetVisibilityExcludeParents(false); }
                else
                { bool visible;bool restore=!_normalVisibility.TryGetValue(entity,out visible)||visible;NativeCleanup.Try(()=>entity.SetVisibilityExcludeParents(restore),"Normal border row visibility restoration failed"); }
            }
        }
        private const int BridgeBatchSize=32;
        private Queue<int> _queue;
        private EditSnapshot _target;
        internal EditSnapshot Published { get; private set; }
        internal bool Busy => _target != null;
        internal double PeakBatchMilliseconds { get; private set; }
        internal long PublishedBatches { get; private set; }

        internal NativePreview(object behavior,NativeCapture source,NativeCoastCompatibility coast=null)
        {
            _behavior=behavior; _source=source;
            _coast=coast??NativeCoastCompatibility.Create(source);
            _live=(List<GameEntity>)NativeBindings.LiveEntities.GetValue(behavior);
            Published=source.Graph.Original.Copy();
        }
        internal bool CaughtUp => !Busy && BorderGraph.Equal(Published,_source.Graph.Current);
        // Published geometry is loaded before the first map frame. Complete its
        // existing bounded batches here so the player never sees the generated
        // borders flash before the reviewed Core layout replaces them.
        internal bool ApplyPublishedBeforeFirstMapFrame(double maximumMilliseconds)
        {
            Stopwatch timer=Stopwatch.StartNew();
            try
            {
                while(!CaughtUp)
                {
                    Tick();
                    if(timer.Elapsed.TotalMilliseconds>maximumMilliseconds)
                        throw new TimeoutException("Published border application exceeded its loading budget.");
                }
                return true;
            }
            catch(Exception error)
            {
                AbortPending();
                _source.Graph.Restore(Published);
                PrototypeLog.Write("Published border load application failed; last published rows restored: "+error);
                return false;
            }
        }
        internal void Tick()
        {
            PrototypeRuntime.RequireNativeThread();
            if(BorderGraph.Equal(_source.Graph.Current,_source.Graph.Original) && !BorderGraph.Equal(Published,_source.Graph.Original))
            { RestoreOriginals(); return; }
            if(_target==null && !BorderGraph.Equal(Published,_source.Graph.Current)) Start(_source.Graph.Current);
            if(_target==null) return;
            var clock=Stopwatch.StartNew();
            try
            {
                // One row can exceed this budget: measured timing is exposed, not claimed as a hard guarantee.
                while(_queue.Count>0 && clock.Elapsed.TotalMilliseconds<4d)
                {
                    int row=_queue.Dequeue();
                    _staged.Add(row,BuildRow(row,_target));
                }
                if(_queue.Count==0) Publish();
            }
            catch
            {
                AbortPending();
                _source.Graph.Restore(Published);
                throw;
            }
            finally { PeakBatchMilliseconds=Math.Max(PeakBatchMilliseconds,clock.Elapsed.TotalMilliseconds); }
        }
        private void Start(EditSnapshot target)
        {
            _target=target.Copy(); _coast.Prepare(_target); var rows=new HashSet<int>(); var nodes=new HashSet<int>();
            BorderGraph graph=_source.Graph;
            for(int i=0;i<graph.Edges.Length;i++)
            {
                BorderEdge edge=graph.Edges[i];
                if(Published.Deleted[i]!=target.Deleted[i] || !Point2.Same(Published.Points[edge.A],target.Points[edge.A]) || !Point2.Same(Published.Points[edge.B],target.Points[edge.B]))
                { rows.Add(edge.Row); nodes.Add(edge.A); nodes.Add(edge.B); }
            }
            bool bridgeChanged=Published.Bridges.Length!=target.Bridges.Length || !Published.Bridges.Zip(target.Bridges,(a,b)=>a.A==b.A && a.B==b.B && a.Left==b.Left && a.Right==b.Right && a.Authored==b.Authored).All(x=>x);
            foreach(BorderEdge edge in Published.Bridges.Concat(target.Bridges))
            {
                if(bridgeChanged || !Point2.Same(Published.Points[edge.A],target.Points[edge.A]) || !Point2.Same(Published.Points[edge.B],target.Points[edge.B]))
                { rows.Add(-1); nodes.Add(edge.A); nodes.Add(edge.B); }
            }
            if(rows.Remove(-1))
            {
                int count=Math.Max((target.Bridges.Length+BridgeBatchSize-1)/BridgeBatchSize,_replacements.Keys.Count(k=>k<0));
                for(int batch=0;batch<count;batch++)rows.Add(-1-batch);
            }
            foreach(int node in nodes)
            {
                CapturedCap cap; if(_source.CapByNode.TryGetValue(node,out cap)) rows.Add(cap.Row);
                foreach(int id in node<graph.Incident.Length?graph.Incident[node]:new List<int>()) rows.Add(graph.Edges[id].Row);
            }
            _queue=new Queue<int>(rows.OrderBy(x=>x));
        }
        private GameEntity BuildRow(int row,EditSnapshot state)
        {
            var mesh=Mesh.CreateMesh(true);
            if(mesh==null) throw new InvalidOperationException("Cannot allocate border mesh.");
            GameEntity entity=null;
            try
            {
                mesh.SetMaterial(_source.Rows[row<0?0:row].Material); mesh.SetMeshRenderOrder(108);
                UIntPtr handle=mesh.LockEditDataWrite();
                try
                {
                    if(row<0)
                    {
                        foreach(BorderEdge edge in state.Bridges.Skip((-row-1)*BridgeBatchSize).Take(BridgeBatchSize)) Ribbon(mesh,handle,edge,state);
                    }
                    else
                    {
                        foreach(CapturedEdge capture in _source.Rows[row].Edges)
                        {
                            if(state.Deleted[capture.Id]) continue;
                            BorderEdge edge=_source.Graph.Edges[capture.Id];
                            if(Unmoved(edge.A,state) && Unmoved(edge.B,state)) foreach(Face f in capture.Ribbon) f.Emit(mesh,handle);
                            else Ribbon(mesh,handle,edge,state);
                        }
                        foreach(CapturedCap cap in _source.Rows[row].Caps) DrawCap(mesh,handle,cap,state);
                    }
                }
                finally { mesh.UnlockEditDataWrite(handle); }
                mesh.ComputeNormals(); mesh.RecomputeBoundingBox();
                entity=GameEntity.CreateEmpty(_source.Scene,false,true,true);
                if(entity==null) throw new InvalidOperationException("Cannot allocate border entity.");
                entity.SetVisibilityExcludeParents(false); entity.SetGlobalFrame(Frame(),true);
                entity.AddMesh(mesh,true); entity.SetForceDecalsToRender(false); entity.SetReadyToRender(true); entity.SetAlpha(1f);
                return entity;
            }
            catch { NativeCleanup.Release(entity); throw; }
        }
        private bool Unmoved(int node,EditSnapshot state) => Point2.Same(state.Points[node],_source.Graph.Original.Points[node]);
        private void DrawCap(Mesh mesh,UIntPtr handle,CapturedCap cap,EditSnapshot state)
        {
            var surviving=_source.Graph.Incident[cap.Node].Where(i=>!state.Deleted[i]).ToArray();
            if(surviving.Length==0 && !state.Bridges.Any(e=>e.A==cap.Node || e.B==cap.Node)) return;
            bool unchanged=Unmoved(cap.Node,state) && _source.Graph.Incident[cap.Node].All(i=>!state.Deleted[i] && Unmoved(_source.Graph.Edges[i].A,state) && Unmoved(_source.Graph.Edges[i].B,state))
                && !state.Bridges.Any(e=>e.A==cap.Node || e.B==cap.Node);
            if(unchanged) { foreach(Face face in cap.Faces) face.Emit(mesh,handle); return; }
            BorderEdge edge=surviving.Length>0 ? _source.Graph.Edges[surviving.Contains(cap.Edge)?cap.Edge:surviving[0]] : state.Bridges.First(e=>e.A==cap.Node || e.B==cap.Node);
            if(_coast.EmitCap(mesh,handle,cap.Node,edge,state)) return;
            Vec2 a=V(state.Points[edge.A]),b=V(state.Points[edge.B]); Vec2 direction=b-a; direction.Normalize();
            Vec2 normal=new Vec2(-direction.y,direction.x), center=V(state.Points[cap.Node]);
            Vec3 p=_coast.Point(center,edge);
            // Validate every cap sample first because the native helper returns silently on missing terrain.
            for(int side=-1;side<=1;side+=2) for(int step=0;step<=4;step++)
            {
                float angle=(float)Math.PI*step/4;
                _coast.Point(center+direction*((float)Math.Cos(angle)*0.8f)+normal*((float)Math.Sin(angle)*0.8f*side),edge);
            }
            NativeBindings.EmitCap(mesh,center,p,direction,normal,edge.Left,edge.Right,handle);
        }
        private void Ribbon(Mesh mesh,UIntPtr handle,BorderEdge edge,EditSnapshot state)
        {
            Vec2 a=V(state.Points[edge.A]),b=V(state.Points[edge.B]),direction=b-a;
            float length=direction.Normalize(); if(length<0.03f) throw new InvalidOperationException("Collapsed ribbon.");
            Vec2 offset=new Vec2(-direction.y,direction.x)*0.8f;
            Vec2 startOffset=V(RibbonJoin.Offset(state,edge,edge.A,.8f)),endOffset=V(RibbonJoin.Offset(state,edge,edge.B,.8f));
            // Subdivide newly connected/lengthened runs for terrain following, without changing width or colour.
            int steps=Math.Max(1,(int)Math.Ceiling(length/1.5f));
            for(int i=0;i<steps;i++)
            {
                Vec2 p=a+(b-a)*(i/(float)steps),q=a+(b-a)*((i+1)/(float)steps);
                Vec2 pn=i==0?startOffset:offset,qn=i==steps-1?endOffset:offset;
                Vec3 pc=_coast.Point(p,edge),qc=_coast.Point(q,edge),pl=_coast.Point(p+pn,edge),pr=_coast.Point(p-pn,edge),ql=_coast.Point(q+qn,edge),qr=_coast.Point(q-qn,edge);
                NativeBindings.EmitQuad(mesh,pl,pc,qc,ql,edge.Left,handle);
                NativeBindings.EmitQuad(mesh,pc,pr,qr,qc,edge.Right,handle);
            }
        }
        private static Vec2 V(Point2 p) => new Vec2(p.X,p.Y);
        private MatrixFrame Frame()
        { MatrixFrame frame=MatrixFrame.Identity; frame.origin.z=-4.65f*(1f-(float)NativeBindings.Alpha.GetValue(_behavior)); return frame; }
        private void Publish()
        {
            if(_live.Count!=_source.Rows.Count+_replacements.Keys.Count(k=>k<0)) throw new InvalidOperationException("Native frontier ownership changed during drag.");
            foreach(var pair in _staged.Where(p=>p.Key>=0))
            {
                GameEntity expected; if(!_replacements.TryGetValue(pair.Key,out expected)) expected=_source.Rows[pair.Key].Original;
                if(!ReferenceEquals(_live[pair.Key],expected)) throw new InvalidOperationException("Another module replaced a border row.");
            }
            // Keep old objects until every new object has entered the list. On native publication failure, restore baseline.
            var retired=new List<GameEntity>();
            try
            {
                foreach(var pair in _staged.OrderBy(p=>p.Key<0?_source.Rows.Count-p.Key-1:p.Key))
                {
                    int index=pair.Key<0?_source.Rows.Count-pair.Key-1:pair.Key;
                    bool restoreVisible=true,priorVisible;
                    if(_editorOnly && index<_live.Count && _normalVisibility.TryGetValue(_live[index],out priorVisible))restoreVisible=priorVisible;
                    if(index<_live.Count) _live[index].SetVisibilityExcludeParents(false);
                    GameEntity prior; if(_replacements.TryGetValue(pair.Key,out prior)) retired.Add(prior);
                    if(index<_live.Count) _live[index]=pair.Value; else _live.Add(pair.Value);
                    _replacements[pair.Key]=pair.Value;
                    pair.Value.SetGlobalFrame(Frame(),true); pair.Value.SetVisibilityExcludeParents(!_editorOnly);
                    if(_editorOnly)_normalVisibility[pair.Value]=restoreVisible;
                }
                Published=_target; _target=null; _queue=null; _staged.Clear(); PublishedBatches++;
            }
            catch
            {
                RestoreOriginals();
                throw;
            }
            finally { foreach(GameEntity old in retired){_normalVisibility.Remove(old);NativeCleanup.Release(old);} }
        }
        internal void AbortPending()
        {
            var discard=_staged.Values.Where(entity=>!_replacements.ContainsValue(entity)).ToArray();
            _staged.Clear(); _queue=null; _target=null;
            foreach(GameEntity entity in discard) NativeCleanup.Release(entity);
        }
        internal void RestoreOriginals()
        {
            var discard=_replacements.Values.Concat(_staged.Values).Distinct().ToArray();
            var originals=new List<GameEntity>();
            foreach(var pair in _replacements)
            {
                if(pair.Key>=0 && pair.Key<_live.Count && ReferenceEquals(_live[pair.Key],pair.Value))
                { _live[pair.Key]=_source.Rows[pair.Key].Original; originals.Add(_live[pair.Key]); }
                if(pair.Key<0) _live.Remove(pair.Value);
            }
            // Managed list ownership is restored in full before making any native cleanup calls.
            _replacements.Clear(); _staged.Clear(); _queue=null; _target=null; Published=_source.Graph.Original.Copy();
            foreach(GameEntity original in originals)
            {
                NativeCleanup.Try(()=>original.SetGlobalFrame(Frame(),true),"Original border frame restoration failed");
                bool priorVisible;bool restoreVisible=!_editorOnly && (!_normalVisibility.TryGetValue(original,out priorVisible)||priorVisible);
                NativeCleanup.Try(()=>original.SetVisibilityExcludeParents(restoreVisible),"Original border visibility restoration failed");
            }
            foreach(GameEntity entity in discard){_normalVisibility.Remove(entity);NativeCleanup.Release(entity);}
        }
    }
}
