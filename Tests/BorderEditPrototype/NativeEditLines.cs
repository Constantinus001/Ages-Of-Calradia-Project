using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace Aoc.BorderEditPrototype
{
    // Editor-only center lines. Cache native rows separately so pointer hover never rebuilds them.
    internal sealed class NativeEditLines
    {
        internal const uint Color=0xFF008000u;
        internal const uint ConnectedColor=0xFF008080u;
        private readonly NativeCapture _source;
        private readonly NativeCoastCompatibility _coast;
        private readonly Dictionary<int,int[]> _rows;
        private readonly Dictionary<int,GameEntity> _entities=new Dictionary<int,GameEntity>();
        private EditSnapshot _shown;
        private readonly Queue<int> _pending=new Queue<int>();
        private readonly Dictionary<int,EditSnapshot> _published=new Dictionary<int,EditSnapshot>();
        private readonly Dictionary<int,OverlayHitMesh> _hits=new Dictionary<int,OverlayHitMesh>();
        private readonly Dictionary<int,OverlayHitMesh> _deleteHits=new Dictionary<int,OverlayHitMesh>();
        private readonly HashSet<int> _dirty=new HashSet<int>();
        private readonly Dictionary<BorderEdge,int> _edgeIds;
        internal bool Busy=>_pending.Count>0;
        private float _frameHeight=float.NaN;
        internal NativeEditLines(NativeCapture source,NativeCoastCompatibility coast)
        {
            _source=source; _coast=coast;
            _edgeIds=Enumerable.Range(0,source.Graph.Edges.Length).ToDictionary(i=>source.Graph.Edges[i],i=>i);
            _rows=new Dictionary<int,int[]>();int batch=0;
            foreach(var row in Enumerable.Range(0,source.Graph.Edges.Length).GroupBy(i=>source.Graph.Edges[i].Row))
            {
                int[] ids=row.ToArray();
                for(int offset=0;offset<ids.Length;offset+=8)_rows.Add(batch++,ids.Skip(offset).Take(8).ToArray());
            }
        }
        internal void Refresh(EditSnapshot shown,MatrixFrame frame)
        {
            if(!ReferenceEquals(shown,_shown))
            {
                _pending.Clear();
                // Reconcile actually displayed bridges first. Comparing only the last queued
                // snapshot could strand an old link when edits superseded an unfinished batch.
                int bridgeSlots=Math.Max(shown.Bridges.Length,_entities.Keys.Where(k=>k<0).Select(k=>-k*8).DefaultIfEmpty(0).Max());
                for(int offset=0;offset<bridgeSlots;offset+=8)_pending.Enqueue(-1-offset/8);
                foreach(var row in _rows)
                {
                    EditSnapshot previous;
                    if(!_published.TryGetValue(row.Key,out previous) || row.Value.Any(i=>Changed(i,shown,previous)))_pending.Enqueue(row.Key);
                }
                _shown=shown;
                _dirty.Clear();foreach(int row in _pending)_dirty.Add(row);
            }
            // One bounded batch per frame, including activation. Never upload the entire map here.
            if(_pending.Count>0)
            {
                _coast.Prepare(shown);int row=_pending.Dequeue();
                Replace(row,row>=0?_rows[row].Where(i=>!shown.Deleted[i]).Select(i=>_source.Graph.Edges[i]):shown.Bridges.Skip((-1-row)*8).Take(8),shown,frame);
                _published[row]=shown;
                _dirty.Remove(row);
            }
            if(_frameHeight!=frame.origin.z)
            { foreach(GameEntity entity in _entities.Values)entity.SetGlobalFrame(frame,true);_frameHeight=frame.origin.z; }
        }
        private bool Changed(int id,EditSnapshot shown,EditSnapshot previous)
        {
            BorderEdge edge=_source.Graph.Edges[id];
            return shown.Deleted[id]!=previous.Deleted[id] || Different(shown.Points[edge.A],previous.Points[edge.A]) || Different(shown.Points[edge.B],previous.Points[edge.B]);
        }
        private static bool Different(Point2 a,Point2 b)=>a.X!=b.X || a.Y!=b.Y;
        internal int Pick(Vec3 near,Vec3 far,out Vec3 point,bool deleteMode=false)
        {
            float nearest=float.MaxValue;int picked=-1;
            foreach(var row in deleteMode?_deleteHits:_hits)
            { if(_dirty.Contains(row.Key))continue;int id=row.Value.Pick(near,far,_frameHeight,ref nearest);if(id>=0)picked=id; }
            point=picked<0?Vec3.Zero:near+(far-near)*nearest;return picked;
        }
        private Vec3 Point(Vec2 p,BorderEdge edge,float floor)
        { Vec3 v=_coast.Point(p,edge);v.z=Math.Max(v.z,floor)+.4f;return v; }
        private void Replace(int row,IEnumerable<BorderEdge> edges,EditSnapshot shown,MatrixFrame frame)
        {
            BorderEdge[] active=edges.ToArray(); GameEntity next=null;var hits=new OverlayHitMesh();var deleteHits=new OverlayHitMesh();
            if(active.Length>0)
            {
                Mesh mesh=Mesh.CreateMesh(true); mesh.SetMaterial(_source.Rows[0].Material);mesh.SetMeshRenderOrder(109);
                UIntPtr handle=mesh.LockEditDataWrite();
                try
                {
                    foreach(BorderEdge edge in active)
                    {
                        int edgeId;if(!_edgeIds.TryGetValue(edge,out edgeId))edgeId=_source.Graph.Edges.Length+Array.IndexOf(shown.Bridges,edge);
                        Vec2 a=new Vec2(shown.Points[edge.A].X,shown.Points[edge.A].Y),b=new Vec2(shown.Points[edge.B].X,shown.Points[edge.B].Y),d=b-a;
                        if(d.Normalize()<.03f)continue;
                        Vec2 n=new Vec2(-d.y,d.x);int steps=Math.Min(32,Math.Max(1,(int)Math.Ceiling((b-a).Length/1.5f)));
                        float[] offsets={-.34f,-.22f,.22f,.34f};
                        Point2[] starts=offsets.Select(w=>RibbonJoin.Offset(shown,edge,edge.A,w)).ToArray(),ends=offsets.Select(w=>RibbonJoin.Offset(shown,edge,edge.B,w)).ToArray();
                        for(int i=0;i<steps;i++)
                        {
                            Vec2 p=a+(b-a)*(i/(float)steps),q=a+(b-a)*((i+1)/(float)steps);
                            float pz=Point(p,edge,float.NegativeInfinity).z-.4f,qz=Point(q,edge,float.NegativeInfinity).z-.4f;
                            for(int band=0;band<3;band++)
                            {
                                Point2 start1=starts[band],start2=starts[band+1];
                                Point2 end1=ends[band],end2=ends[band+1];
                                Vec3 a1=Point(p+(i==0?new Vec2(start1.X,start1.Y):n*offsets[band]),edge,pz),a2=Point(p+(i==0?new Vec2(start2.X,start2.Y):n*offsets[band+1]),edge,pz);
                                Vec3 b2=Point(q+(i==steps-1?new Vec2(end2.X,end2.Y):n*offsets[band+1]),edge,qz),b1=Point(q+(i==steps-1?new Vec2(end1.X,end1.Y):n*offsets[band]),edge,qz);
                                NativeBindings.EmitQuad(mesh,a1,a2,b2,b1,band==1?(edge.Row<0?ConnectedColor:Color):NativeSelection.MarkerOutline,handle);
                                hits.AddQuad(edgeId,a1,a2,b2,b1);
                                if(band==1)
                                {
                                    Vec3 pad=new Vec3(n.x,n.y,0);
                                    deleteHits.AddQuad(edgeId,a1-pad,a2+pad,b2+pad,b1-pad);
                                }
                            }
                        }
                    }
                }
                finally { mesh.UnlockEditDataWrite(handle); }
                mesh.ComputeNormals();mesh.RecomputeBoundingBox();
                next=GameEntity.CreateEmpty(_source.Scene,false,true,true);
                try { next.SetGlobalFrame(frame,true);next.AddMesh(mesh,true);next.SetForceDecalsToRender(false);next.SetAlpha(1f);next.SetReadyToRender(true);next.SetVisibilityExcludeParents(true); }
                catch { NativeCleanup.Release(next);throw; }
            }
            GameEntity old;if(_entities.TryGetValue(row,out old))NativeCleanup.Release(old);
            _entities.Remove(row);if(next!=null)_entities.Add(row,next);
            _hits.Remove(row);if(next!=null)_hits.Add(row,hits);
            _deleteHits.Remove(row);if(next!=null)_deleteHits.Add(row,deleteHits);
        }
        internal void Clear()
        { foreach(GameEntity entity in _entities.Values)NativeCleanup.Release(entity);_entities.Clear();_published.Clear();_pending.Clear();_hits.Clear();_deleteHits.Clear();_dirty.Clear();_shown=null; }
    }
}

