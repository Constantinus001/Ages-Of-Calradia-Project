using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;
namespace Aoc.BorderEditPrototype
{
    // One reviewed saved-layout repair. Reversible row replacement; native calls stay on the map thread.
    internal sealed class NativeFillRepair
    {
        private sealed class Row { internal RepairRow Data;internal GameEntity Original,Candidate;internal bool Visible,Touched;internal int Index;internal Mesh Mesh; }
        private readonly BorderGraph _graph;private readonly EditSnapshot _baseline;private readonly FillRepairPlan _plan;
        private readonly List<GameEntity> _live;private readonly Scene _scene;
        private readonly Func<Dictionary<int,RepairCapturedRow>> _capture;private readonly Func<Point2,float> _height;private readonly Func<Point2,uint> _color;
        private readonly List<Row> _rows=new List<Row>();private readonly Dictionary<Point2,float> _heights=new Dictionary<Point2,float>();
        private int _row,_triangle;private bool _stopped;
        internal bool Applied {get;private set;}
        internal NativeFillRepair(BorderGraph graph,FillRepairPlan plan,List<GameEntity> live,Scene scene,Func<Dictionary<int,RepairCapturedRow>> capture,Func<Point2,float> height,Func<Point2,uint> color)
        { _graph=graph;_baseline=graph.Current.Copy();_plan=plan;_live=live;_scene=scene;_capture=capture;_height=height;_color=color; }
        internal void Tick(float alpha)
        {
            if(_stopped)return;
            try
            {
                if(!BorderGraph.Equal(_baseline,_graph.Current))
                {Clear();PrototypeLog.Write("Fill repair invalidated by later edits; original fill restored. Repair must be refreshed for the new outline.");return;}
                if(Applied)return;
                if(_rows.Count==0)
                {
                    var captured=_capture();if(captured==null)return;
                    foreach(RepairRow data in _plan.Rows)
                    {
                        RepairCapturedRow original;
                        if(!captured.TryGetValue(data.Id,out original)||original.Fingerprint!=data.Fingerprint||original.Faces!=data.Faces)
                            throw new InvalidOperationException("Fill repair geometry identity mismatch for row "+data.Id);
                        int index=_live.IndexOf(original.Entity);if(index<0)throw new InvalidOperationException("Fill repair row is not owned by this map.");
                        _rows.Add(new Row{Data=data,Original=original.Entity,Index=index,Visible=original.Entity.GetVisibilityExcludeParents()});
                    }
                }
                var clock=Stopwatch.StartNew();int budget=64;
                while(_row<_rows.Count&&budget>0&&clock.Elapsed.TotalMilliseconds<4)
                {
                    Row row=_rows[_row];
                    if(row.Mesh==null){row.Mesh=Mesh.CreateMesh(true);row.Mesh.SetMaterial(row.Original.GetFirstMesh().GetMaterial());row.Mesh.SetMeshRenderOrder(100);}
                    UIntPtr handle=row.Mesh.LockEditDataWrite();
                    try
                    {
                        while(_triangle<row.Data.Triangles.Count&&budget>0&&clock.Elapsed.TotalMilliseconds<4)
                        {
                            RepairTriangle t=row.Data.Triangles[_triangle++];Vec3 a=Surface(t.A),b=Surface(t.B),c=Surface(t.C);
                            uint color=t.Color==0?_color((t.A+t.B+t.C)*(1f/3)):t.Color;
                            row.Mesh.AddTriangle(a,b,c,Vec2.Zero,Vec2.Zero,Vec2.Zero,color,handle);row.Mesh.AddTriangle(a,c,b,Vec2.Zero,Vec2.Zero,Vec2.Zero,color,handle);budget--;
                        }
                    }
                    finally{row.Mesh.UnlockEditDataWrite(handle);}
                    if(_triangle<row.Data.Triangles.Count)break;
                    row.Mesh.ComputeNormals();row.Mesh.RecomputeBoundingBox();row.Candidate=GameEntity.CreateEmpty(_scene,false,true,true);
                    row.Candidate.SetVisibilityExcludeParents(false);row.Candidate.SetGlobalFrame(row.Original.GetGlobalFrame(),true);
                    row.Candidate.AddMesh(row.Mesh,true);row.Candidate.SetForceDecalsToRender(false);row.Candidate.SetReadyToRender(true);
                    _row++;_triangle=0;
                }
                if(_row<_rows.Count)return;
                foreach(Row row in _rows)if(!ReferenceEquals(_live[row.Index],row.Original))throw new InvalidOperationException("Fill ownership changed during repair preparation.");
                foreach(Row row in _rows)
                {
                    row.Visible=row.Original.GetVisibilityExcludeParents();row.Touched=true;row.Original.SetVisibilityExcludeParents(false);
                    _live[row.Index]=row.Candidate;row.Candidate.SetAlpha(alpha);row.Candidate.SetVisibilityExcludeParents(row.Visible);
                }
                Applied=true;_heights.Clear();PrototypeLog.Write("FILL REPAIR APPLIED; regions="+_plan.Regions+"; rows="+_rows.Count+"; native row fingerprints verified; saved borders unchanged.");
            }
            catch(Exception error){PrototypeLog.Write("Fill repair failed; restoring original fill: "+error);Clear();}
        }
        // Loading is already on the map thread. Finish this reviewed, hash-pinned
        // replacement before the first map frame, or roll back every staged row.
        // Normal map-frame ticking remains the safe path for non-published layouts.
        internal bool ApplyBeforeFirstMapFrame(float alpha,double maximumMilliseconds,Action advanceCapture=null)
        {
            PrototypeRuntime.RequireNativeThread();
            Stopwatch timer=Stopwatch.StartNew();
            try
            {
                while(!Applied&&!_stopped)
                {
                    if(timer.Elapsed.TotalMilliseconds>=maximumMilliseconds)
                        throw new TimeoutException("Fill repair exceeded its loading budget.");
                    if(_rows.Count==0&&_capture()==null)
                    {
                        if(advanceCapture==null)throw new InvalidOperationException("Fill repair capture is unavailable and has no loading dependency driver.");
                        advanceCapture();
                    }
                    Tick(alpha);
                }
                return Applied;
            }
            catch(Exception error)
            {
                PrototypeLog.Write("Published fill repair failed before first frame; original fill restored: "+error);
                Clear();
                return false;
            }
        }
        private Vec3 Surface(Point2 point)
        {
            float z;if(!_heights.TryGetValue(point,out z)){z=_height(point)+3f;if(float.IsNaN(z)||float.IsInfinity(z))throw new InvalidOperationException("Repair surface unavailable.");_heights.Add(point,z);}
            return new Vec3(point.X,point.Y,z);
        }
        internal void Clear()
        {
            foreach(Row row in _rows)
            {
                NativeCleanup.Try(()=>
                {
                    if(row.Index<_live.Count&&ReferenceEquals(_live[row.Index],row.Candidate))_live[row.Index]=row.Original;
                    if(row.Touched&&_live.Contains(row.Original))row.Original.SetVisibilityExcludeParents(row.Visible);
                },"Fill repair original row restoration failed");
                NativeCleanup.Release(row.Candidate);row.Candidate=null;
            }
            _rows.Clear();_heights.Clear();Applied=false;_stopped=true;
        }
    }
}
