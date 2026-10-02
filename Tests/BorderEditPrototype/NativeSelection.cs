using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace Aoc.BorderEditPrototype
{
    // Disposable overlay; captured borders remain unchanged.
    internal static class NativeSelection
    {
        // Native vertex_color receives campaign pre-exposure. Zero unwanted channels
        // retain green/cyan hue; true black remains black under exposure.
        internal const uint OpenColor=0xFF008000u, AttachedColor=0xFF805000u, StartColor=0xFF008080u;
        internal const uint MarkerOutline=0xFF000000u, MarkerHighlight=0xFF004000u;
        internal const uint InvalidColor=0xFF800000u;
        internal const uint HoverColor=0xFF00C000u;
        private static GameEntity _entity;
        private static string _key;
        private static EditSnapshot _snapshot;
        private static NativeCapture _source;
        private static NativeCoastCompatibility _coast;
        private static NativeEditLines _lines;
        internal static bool PreparingLines=>_lines!=null && _lines.Busy;
        private static bool _sampleFailureLogged;
        private struct HitTriangle { internal int Node; internal Vec3 A,B,C; }
        private static readonly List<HitTriangle> _hits=new List<HitTriangle>();
        private static float _heightOffset;
        internal static bool Refresh(NativeCapture source,object behavior,int hover,EditSnapshot shown,NativeCoastCompatibility adapter=null,bool connectMode=false,int connectionStart=-1,Point2? focus=null,int connectionTarget=-1,bool connectionValid=false,bool deleteMode=false)
        {
            PrototypeRuntime.RequireNativeThread();
            try { RefreshCore(source,behavior,hover,shown,adapter,connectMode,connectionStart,focus,connectionTarget,connectionValid,deleteMode);return true; }
            catch(Exception error)
            {
                // Native/reflection overlay boundary: selection failure must not disable editing.
                Clear(); PrototypeLog.Write("Selection overlay unavailable: "+error);return false;
            }
        }
        private static void RefreshCore(NativeCapture source,object behavior,int hover,EditSnapshot shown,NativeCoastCompatibility adapter,bool connectMode,int connectionStart,Point2? focus,int connectionTarget,bool connectionValid,bool deleteMode)
        {
            var ids=deleteMode?(hover<0?new int[0]:new[]{hover}):connectMode?source.Graph.Selected.Where(i=>i>=source.Graph.Edges.Length).OrderBy(i=>i).ToArray():source.Graph.Selected.Concat(hover<0?new int[0]:new[]{hover}).Where(i=>i>=0).Distinct().OrderBy(i=>i).ToArray();
            string key=deleteMode+":"+string.Join(",",ids)+":"+connectMode+":"+connectionStart+":"+connectionTarget+":"+connectionValid;
            Point2 area=focus??shown.Points[0];
            area=new Point2((float)Math.Floor(area.X/20)*20+10,(float)Math.Floor(area.Y/20)*20+10);
            key+=":"+area.X+":"+area.Y;
            MatrixFrame frame=MatrixFrame.Identity; frame.origin.z=-4.65f*(1f-(float)NativeBindings.Alpha.GetValue(behavior));
            _heightOffset=frame.origin.z;
            if(!ReferenceEquals(_source,source))
            {
                _lines?.Clear(); _source=source; _coast=adapter??NativeCoastCompatibility.Create(source);
                _lines=new NativeEditLines(source,_coast); _key=null;
            }
            _lines.Refresh(shown,frame);
            if(ReferenceEquals(_source,source) && key==_key && ReferenceEquals(_snapshot,shown))
            { if(_entity!=null) _entity.SetGlobalFrame(frame,true); return; }
            ReleaseEntity(); _hits.Clear(); _key=key; _snapshot=shown; _sampleFailureLogged=false;
            // Selection owns this adapter, never NativePreview's pending-batch adapter.
            _coast.Prepare(shown);
            Mesh mesh=Mesh.CreateMesh(true); if(mesh==null) throw new InvalidOperationException("Selection mesh allocation failed.");
            mesh.SetMaterial(source.Rows[0].Material); mesh.SetMeshRenderOrder(110);
            var endpoints=new Dictionary<int,BorderEdge>(); int quads=0;
            // Open endpoints are always actionable; clicking one starts connection directly.
            {
                // One linear pass; show only degree-one endpoints, never whole-map outlines.
                var degree=new int[shown.Points.Length]; var incident=new BorderEdge[shown.Points.Length];
                foreach(BorderEdge edge in source.Graph.Edges.Where((e,i)=>!shown.Deleted[i]).Concat(shown.Bridges))
                { degree[edge.A]++; degree[edge.B]++; incident[edge.A]=incident[edge.B]=edge; }
                foreach(int node in Enumerable.Range(0,degree.Length).Where(n=>degree[n]==1 && (n==connectionStart || (shown.Points[n]-area).Length<=80f))
                    .OrderBy(n=>n==connectionStart?-1f:(shown.Points[n]-area).Length).Take(16))endpoints.Add(node,incident[node]);
                foreach(int node in shown.Bridges.SelectMany(e=>new[]{e.A,e.B}).Distinct().Where(n=>degree[n]>1 && (shown.Points[n]-area).Length<=80f)
                    .OrderBy(n=>(shown.Points[n]-area).Length).Take(16))endpoints[node]=incident[node];
            }
            // Suggest a legal destination immediately; committing still requires its click.
            // Work is bounded by visible markers and cached with the selection snapshot.
            if(connectMode && connectionStart>=0 && connectionTarget<0)
            {
                foreach(int node in endpoints.Keys.Where(n=>n!=connectionStart)
                    .OrderBy(n=>(shown.Points[n]-shown.Points[connectionStart]).Length))
                {
                    string reason;
                    if(!source.Graph.CanConnectEndpoints(connectionStart,node,out reason))continue;
                    connectionTarget=node;connectionValid=true;break;
                }
            }
            UIntPtr handle=mesh.LockEditDataWrite();
            try
            {
                if(connectMode && connectionStart>=0 && connectionTarget>=0 && connectionStart!=connectionTarget
                    && endpoints.ContainsKey(connectionStart) && endpoints.ContainsKey(connectionTarget))
                {
                    Vec3 a,b;
                    if(Sample(V(shown.Points[connectionStart]),endpoints[connectionStart],float.NegativeInfinity,out a)
                        && Sample(V(shown.Points[connectionTarget]),endpoints[connectionTarget],float.NegativeInfinity,out b))
                    {
                        Vec2 d=new Vec2(b.x-a.x,b.y-a.y);
                        if(d.Normalize()>.03f)
                        {
                            Vec3 n=new Vec3(-d.y*.22f,d.x*.22f,0),lift=new Vec3(0,0,.2f);
                            for(int dash=0;dash<8;dash++)
                            {
                                Vec3 p=a+(b-a)*(dash/8f)+lift,q=a+(b-a)*((dash+.65f)/8f)+lift;
                                NativeBindings.EmitQuad(mesh,p-n,p+n,q+n,q-n,connectionValid?OpenColor:InvalidColor,handle);quads++;
                            }
                        }
                    }
                }
                foreach(int id in ids)
                {
                    if(id>=source.Graph.Edges.Length+shown.Bridges.Length || (id<source.Graph.Edges.Length && shown.Deleted[id])) continue;
                    BorderEdge edge=id<source.Graph.Edges.Length?source.Graph.Edges[id]:shown.Bridges[id-source.Graph.Edges.Length];
                    endpoints[edge.A]=edge; endpoints[edge.B]=edge;
                    Vec2 a=V(shown.Points[edge.A]),b=V(shown.Points[edge.B]),d=b-a; float length=d.Normalize();
                    if(length<.03f) continue;
                    Vec2 n=new Vec2(-d.y,d.x); int steps=Math.Max(1,(int)Math.Ceiling(length/1.5f));
                    for(int i=0;i<steps;i++)
                    {
                        Vec2 p=a+(b-a)*(i/(float)steps),q=a+(b-a)*((i+1)/(float)steps); Vec3 pc,qc;
                        if(!Sample(p,edge,float.NegativeInfinity,out pc)||!Sample(q,edge,float.NegativeInfinity,out qc)) continue;
                        for(int side=-1;side<=1;side+=2)
                        {
                            Vec3 p1,p2,q1,q2;
                            if(!Sample(p+n*((deleteMode?0f:.42f)*side),edge,pc.z-.35f,out p1)||!Sample(p+n*((deleteMode?.7f:.62f)*side),edge,pc.z-.35f,out p2)
                                ||!Sample(q+n*((deleteMode?0f:.42f)*side),edge,qc.z-.35f,out q1)||!Sample(q+n*((deleteMode?.7f:.62f)*side),edge,qc.z-.35f,out q2)) continue;
                            NativeBindings.EmitQuad(mesh,p1,p2,q2,q1,deleteMode?InvalidColor:0xFF808000u,handle); quads++;
                        }
                    }
                }
                foreach(var endpoint in endpoints.Where(e=>!deleteMode))
                {
                    Vec2 center=V(shown.Points[endpoint.Key]); Vec3 anchor;
                    if(!Sample(center,endpoint.Value,float.NegativeInfinity,out anchor)) continue;
                    int degree=(endpoint.Key<source.Graph.Incident.Length?source.Graph.Incident[endpoint.Key].Count(i=>!shown.Deleted[i]):0)+shown.Bridges.Count(e=>e.A==endpoint.Key||e.B==endpoint.Key);
                    bool joined=degree>1 && shown.Bridges.Any(e=>e.A==endpoint.Key||e.B==endpoint.Key);
                    uint color=joined || (connectMode && endpoint.Key==connectionStart)?StartColor:degree==1?OpenColor:AttachedColor;
                    bool hovered=endpoint.Key==connectionTarget;
                    if(hovered && degree==1 && !joined && endpoint.Key!=connectionStart)color=HoverColor;
                    if(connectMode && connectionStart>=0 && endpoint.Key==connectionTarget && connectionTarget!=connectionStart && !connectionValid)color=InvalidColor;
                    // Compact filled diamonds mark exact endpoints; the selected start has an extra halo.
                    bool selected=joined || (connectMode && endpoint.Key==connectionStart);
                    float[] radii=selected?new[]{0f,1.25f,1.55f,1.8f,2.1f}:new[]{0f,1.25f,1.55f,1.8f};
                    uint[] colors=selected?new[]{color,MarkerOutline,MarkerHighlight,StartColor}:new[]{color,MarkerOutline,MarkerHighlight};
                    if(hovered)for(int radius=1;radius<radii.Length;radius++)radii[radius]*=1.18f;
                    const int sectors=4;
                    for(int band=0;band<colors.Length;band++)
                    for(int i=0;i<sectors;i++)
                    {
                        float rotation=joined?(float)Math.PI/4:0;
                        float a=(float)(i*Math.PI*2/sectors)+rotation,b=(float)((i+1)*Math.PI*2/sectors)+rotation;
                        Vec2 da=new Vec2((float)Math.Cos(a),(float)Math.Sin(a)),db=new Vec2((float)Math.Cos(b),(float)Math.Sin(b));
                        // An editor handle is a flat marker at its sampled anchor, not terrain geometry.
                        // Sample once per endpoint instead of hundreds of native calls per ring.
                        Vec3 p1=anchor+new Vec3(da.x*radii[band],da.y*radii[band],0),p2=anchor+new Vec3(da.x*radii[band+1],da.y*radii[band+1],0);
                        Vec3 q1=anchor+new Vec3(db.x*radii[band],db.y*radii[band],0),q2=anchor+new Vec3(db.x*radii[band+1],db.y*radii[band+1],0);
                        NativeBindings.EmitQuad(mesh,p1,p2,q2,q1,colors[band],handle); quads++;
                        // Pick the displayed ring and its center, using the same sampled perimeter.
                        if(degree==1 && band==colors.Length-1)
                        {
                            float padding=1f+.75f/radii[radii.Length-1];
                            _hits.Add(new HitTriangle{Node=endpoint.Key,A=anchor,B=anchor+(p2-anchor)*padding,C=anchor+(q2-anchor)*padding});
                        }
                    }
                }
            }
            finally { mesh.UnlockEditDataWrite(handle); }
            if(quads==0) return;
            mesh.ComputeNormals(); mesh.RecomputeBoundingBox();
            _entity=GameEntity.CreateEmpty(source.Scene,false,true,true);
            if(_entity==null) throw new InvalidOperationException("Selection entity allocation failed.");
            _entity.SetGlobalFrame(frame,true); _entity.AddMesh(mesh,true); _entity.SetForceDecalsToRender(false);
            _entity.SetAlpha(1f); _entity.SetReadyToRender(true); _entity.SetVisibilityExcludeParents(true);
        }
        private static Vec2 V(Point2 p) => new Vec2(p.X,p.Y);
        internal static int PickBorder(Vec3 near,Vec3 far,out Vec3 point,bool deleteMode=false)
        { point=Vec3.Zero;return _lines==null?-1:_lines.Pick(near,far,out point,deleteMode); }
        internal static int PickEndpoint(Vec3 near,Vec3 far,float snapSlope=0)
        {
            Vec3 shift=new Vec3(0,0,_heightOffset);float best=float.MaxValue;int node=-1;
            foreach(HitTriangle hit in _hits)
            {
                float distance=RayTriangle(near,far,hit.A+shift,hit.B+shift,hit.C+shift);
                if(distance<0)continue;
                Vec3 offset=near+(far-near)*distance-(hit.A+shift);
                float score=offset.x*offset.x+offset.y*offset.y;
                if(score<best){best=score;node=hit.Node;}
            }
            if(node>=0 || snapSlope<=0)return node;
            Vec3 ray=far-near;float length=ray.Length;
            if(length<.001f)return -1;
            Vec3 direction=ray*(1f/length);best=snapSlope*snapSlope;
            var visited=new HashSet<int>();
            foreach(HitTriangle hit in _hits)
            {
                if(!visited.Add(hit.Node))continue;
                Vec3 delta=hit.A+shift-near;float depth=Vec3.DotProduct(delta,direction);
                if(depth<=0 || depth>length)continue;
                Vec3 perpendicular=delta-direction*depth;
                float score=Vec3.DotProduct(perpendicular,perpendicular)/(depth*depth);
                if(score<best){best=score;node=hit.Node;}
            }
            return node;
        }
        internal static float RayTriangle(Vec3 origin,Vec3 far,Vec3 a,Vec3 b,Vec3 c)
        {
            Vec3 direction=far-origin,ab=b-a,ac=c-a;
            Vec3 p=Vec3.CrossProduct(direction,ac);float determinant=Vec3.DotProduct(ab,p);
            if(Math.Abs(determinant)<.000001f)return -1;
            float inverse=1f/determinant;Vec3 s=origin-a;
            float u=Vec3.DotProduct(s,p)*inverse;if(u<0||u>1)return -1;
            Vec3 q=Vec3.CrossProduct(s,ab);float v=Vec3.DotProduct(direction,q)*inverse;
            if(v<0||u+v>1)return -1;
            float t=Vec3.DotProduct(ac,q)*inverse;return t>=0&&t<=1?t:-1;
        }
        private static bool Sample(Vec2 point,BorderEdge edge,float minimum,out Vec3 result)
        {
            result=Vec3.Zero;
            try
            {
                result=_coast.Point(point,edge); result.z=Math.Max(result.z,minimum)+.35f;
                if(result.IsValid) return true;
                throw new InvalidOperationException("Nonfinite selection surface sample.");
            }
            catch(Exception error)
            {
                if(!_sampleFailureLogged) { _sampleFailureLogged=true; PrototypeLog.Write("Selection sample skipped: "+error); }
                return false;
            }
        }
        private static void ReleaseEntity() { GameEntity old=_entity; _entity=null; NativeCleanup.Release(old); }
        internal static void Clear()
        { ReleaseEntity(); _hits.Clear(); _lines?.Clear(); _lines=null; _key=null; _snapshot=null; _source=null; _coast=null; }
    }
}

