using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;
namespace Aoc.BorderEditPrototype
{
    // Separate fill entities; never enters the protected renderer's ownership lists.
    // At most 16 small triangles uploaded per tick, with atomic visible replacement.
    internal sealed class NativeFillPreview
    {
        private readonly NativeCapture _source;private readonly NativeLocationStyle _style;
        private FillPatch[] _published=new FillPatch[0],_target;
        private readonly Queue<Tuple<Point2[],bool>> _pending=new Queue<Tuple<Point2[],bool>>();
        private List<GameEntity> _live=new List<GameEntity>(),_staged=new List<GameEntity>();
        private float _lastAlpha=float.NaN;
        internal bool Ready=>_target==null && Same(_published,_source.Graph.Current.Fills);
        private static bool Same(FillPatch[] a,FillPatch[] b)=>a.Length==b.Length&&a.Zip(b,(x,y)=>ReferenceEquals(x,y)).All(x=>x);
        internal NativeFillPreview(NativeCapture source,NativeLocationStyle style){_source=source;_style=style;}
        internal static bool Supported(Point2[] t,NativeLocationStyle style)
        {
            Point2 center=(t[0]+t[1]+t[2])*(1f/3);
            return style.Land(center)&&t.All(style.Land)&&Enumerable.Range(0,3).All(k=>style.Land((t[k]+t[(k+1)%3])*.5f));
        }
        internal void Tick(float alpha)
        {
            FillPatch[] desired=_source.Graph.Current.Fills;
            if(!Same(desired,_target??_published))
            {
                foreach(GameEntity e in _staged)NativeCleanup.Release(e);_staged.Clear();_pending.Clear();
                _target=desired;
                foreach(FillPatch patch in desired)
                {
                    var triangles=FillGeometry.Subdivide(patch.Points);
                    if(patch.ClipToLand&&!triangles.Any(t=>Supported(t,_style)))throw new InvalidOperationException("Fill area contains no renderable land. Draw a smaller outline on land.");
                    foreach(Point2[] tri in triangles)_pending.Enqueue(Tuple.Create(tri,patch.ClipToLand));
                }
            }
            if(_target!=null)
            {
                if(_pending.Count>0)BuildBatch();
                if(_pending.Count==0)
                {
                    foreach(GameEntity e in _staged){e.SetAlpha(alpha);e.SetVisibilityExcludeParents(alpha>.001f);}
                    foreach(GameEntity e in _live)NativeCleanup.Release(e);
                    _live=_staged;_staged=new List<GameEntity>();_published=_target;_target=null;
                    _lastAlpha=alpha;
                }
            }
            if(float.IsNaN(_lastAlpha)||Math.Abs(alpha-_lastAlpha)>.002f)
            {foreach(GameEntity e in _live){e.SetAlpha(alpha);e.SetVisibilityExcludeParents(alpha>.001f);}_lastAlpha=alpha;}
        }
        private void BuildBatch()
        {
            Mesh mesh=Mesh.CreateMesh(true);mesh.SetMaterial(_source.Rows[0].Material);mesh.SetMeshRenderOrder(101);
            UIntPtr handle=mesh.LockEditDataWrite();int faces=0;
            try
            {
                for(int i=0;i<16 && _pending.Count>0;i++)
                {
                    var item=_pending.Dequeue();Point2[] t=item.Item1;Point2 center=(t[0]+t[1]+t[2])*(1f/3);
                    // Reject coastal fringe triangles conservatively. This may leave a narrow
                    // unpainted seam; nearest-owner lookup alone must never colour open ocean.
                    if(item.Item2&&!Supported(t,_style))continue;
                    Vec3 a=NativeLocationStyle.Surface(t[0],4.12f),b=NativeLocationStyle.Surface(t[1],4.12f),c=NativeLocationStyle.Surface(t[2],4.12f);
                    uint color=_style.Color(center,50);
                    mesh.AddTriangle(a,b,c,Vec2.Zero,Vec2.Zero,Vec2.Zero,color,handle);
                    mesh.AddTriangle(a,c,b,Vec2.Zero,Vec2.Zero,Vec2.Zero,color,handle);faces++;
                }
            }
            finally{mesh.UnlockEditDataWrite(handle);}
            if(faces==0)return;
            mesh.ComputeNormals();mesh.RecomputeBoundingBox();GameEntity entity=GameEntity.CreateEmpty(_source.Scene,false,true,true);
            try{entity.SetVisibilityExcludeParents(false);entity.AddMesh(mesh,true);entity.SetForceDecalsToRender(false);entity.SetReadyToRender(true);_staged.Add(entity);}
            catch{NativeCleanup.Release(entity);throw;}
        }
        internal void Clear()
        {foreach(GameEntity e in _live.Concat(_staged))NativeCleanup.Release(e);_live.Clear();_staged.Clear();_pending.Clear();_target=null;_published=new FillPatch[0];_lastAlpha=float.NaN;}
    }
}
