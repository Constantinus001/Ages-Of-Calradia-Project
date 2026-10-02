using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using AgesOfCalradia.CoastSurfaceFix;
#if !BORDER_PREVIEW_TESTS
using System.Reflection;
using System.IO;
using System.Security.Cryptography;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Map;
#endif

namespace Aoc.BorderEditPrototype
{
    // Optional, version-pinned adapter. It reads the accepted sidecar's completed plan;
    // preview never enters or mutates that sidecar's thread-local generation state.
    internal sealed class NativeCoastCompatibility
    {
        internal const string SupportedHash="AA4AE248536C732E16E4CF74606A0C42513FFA11F7242C160A4FF4C6C525A807";
        private readonly BorderGraph _graph;
        private readonly HashSet<BorderEdge> _coasts;
        private readonly bool[] _joins;
        private readonly Func<Vec2,Vec3> _surface;
        private readonly Dictionary<Tuple<int,int>,List<Vec2>> _joinPoints=new Dictionary<Tuple<int,int>,List<Vec2>>();
        private readonly Dictionary<Vec2,Vec3> _heights=new Dictionary<Vec2,Vec3>();
        internal bool Active => _surface!=null;
        internal NativeCoastCompatibility(BorderGraph graph,bool[] coasts,bool[] joins,Func<Vec2,Vec3> surface)
        {
            _graph=graph; _joins=joins; _surface=surface;
            _coasts=new HashSet<BorderEdge>(graph.Edges.Where((e,i)=>coasts[i]));
        }
        internal static NativeCoastCompatibility Create(NativeCapture capture)
        {
            var coasts=new bool[capture.Graph.Edges.Length]; var joins=new bool[capture.Graph.Original.Points.Length];
#if !BORDER_PREVIEW_TESTS
            var assembly=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetType("AgesOfCalradia.CoastSurfaceFix.CoastSurfacePatch")!=null);
            if(assembly!=null)
            {
                using(var sha=SHA256.Create()) using(var stream=File.OpenRead(assembly.Location))
                    if(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","")!=SupportedHash)
                        throw new InvalidOperationException("Unsupported coastline surface adapter revision.");
                const BindingFlags flags=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
                Type patch=assembly.GetType("AgesOfCalradia.CoastSurfaceFix.CoastSurfacePatch",true);
                if((bool)patch.GetField("_enabled",flags).GetValue(null))
                {
                    object caches=patch.GetField("_caches",flags).GetValue(null);
                    object[] args={capture.Builder,null};
                    if(!(bool)caches.GetType().GetMethod("TryGetValue").Invoke(caches,args))
                        throw new InvalidOperationException("Accepted coastline plan unavailable; retain captured borders.");
                    object cache=args[1]; Type cacheType=cache.GetType();
                    if((bool)cacheType.GetField("PlanFailed",flags).GetValue(cache) || (int)cacheType.GetField("Fallbacks",flags).GetValue(cache)>0)
                        throw new InvalidOperationException("Coastline generation used a fallback; editor cannot safely reproduce its mixed surface policy.");
                    object scanner=cacheType.GetField("Scanner",flags).GetValue(cache);
                    if(scanner==null || !(bool)scanner.GetType().GetProperty("Complete",flags).GetValue(scanner,null))
                        throw new InvalidOperationException("Coastline plan has not completed.");
                    object plan=cacheType.GetField("Plan",flags).GetValue(cache);
                    object spans=plan.GetType().GetField("Coast",flags).GetValue(plan);
                    Type spanType=assembly.GetType("AgesOfCalradia.CoastSurfaceFix.CoastSpan",true);
                    var constructor=spanType.GetConstructor(flags,null,new[]{typeof(Vec2),typeof(Vec2)},null);
                    MethodInfo contains=spans.GetType().GetMethod("Contains"), atJoin=plan.GetType().GetMethod("AtJoin",flags);
                    foreach(CapturedEdge edge in capture.Edges)
                        coasts[edge.Id]=(bool)contains.Invoke(spans,new[]{constructor.Invoke(new object[]{edge.A,edge.B})});
                    for(int i=0;i<joins.Length;i++) joins[i]=(bool)atJoin.Invoke(plan,new object[]{V(capture.Graph.Original.Points[i])});
                    MethodInfo terrain=(MethodInfo)patch.GetField("_terrain",flags).GetValue(null);
                    return new NativeCoastCompatibility(capture.Graph,coasts,joins,p=>
                    {
                        PrototypeRuntime.RequireNativeThread();
                        object[] terrainArgs={p,Activator.CreateInstance(terrain.GetParameters()[1].ParameterType.GetElementType())};
                        bool water=(bool)terrain.Invoke(null,terrainArgs) && terrainArgs[1].ToString()=="CoastalSea";
                        float surface=0; var position=new CampaignVec2(p,false);
                        bool valid=Campaign.Current?.MapSceneWrapper!=null && Campaign.Current.MapSceneWrapper.GetHeightAtPoint(in position,ref surface);
                        float level=water?capture.Scene.GetWaterLevelAtPosition(p,true,true):0, z;
                        if(!CoastSurfacePolicy.Project(valid,surface,water,level,out z)) throw new InvalidOperationException("Coastline surface unavailable; retain last published border.");
                        return new Vec3(p.x,p.y,z);
                    });
                }
            }
#endif
            return new NativeCoastCompatibility(capture.Graph,coasts,joins,null);
        }
        internal void Prepare(EditSnapshot state)
        {
            _heights.Clear(); _joinPoints.Clear();
            if(!Active) return;
            for(int i=0;i<_joins.Length;i++) if(_joins[i])
            {
                Vec2 p=V(state.Points[i]); var key=Key(p); List<Vec2> list;
                if(!_joinPoints.TryGetValue(key,out list)) _joinPoints.Add(key,list=new List<Vec2>());
                list.Add(p);
            }
        }
        private static Tuple<int,int> Key(Vec2 p) => Tuple.Create((int)Math.Floor(p.x),(int)Math.Floor(p.y));
        private bool AtJoin(Vec2 p)
        {
            var key=Key(p);
            for(int x=-1;x<=1;x++) for(int y=-1;y<=1;y++)
            {
                List<Vec2> list;
                if(_joinPoints.TryGetValue(Tuple.Create(key.Item1+x,key.Item2+y),out list))
                    foreach(Vec2 center in list) if((center-p).LengthSquared<=.8002f*.8002f) return true;
            }
            return false;
        }
        private bool IsCoast(BorderEdge edge) => _coasts.Contains(edge) || (edge.Row<0 && edge.A<_joins.Length && edge.B<_joins.Length && _joins[edge.A] && _joins[edge.B]);
        internal Vec3 Point(Vec2 p,BorderEdge edge)
        {
            Vec3 original;
            if(!NativeBindings.Sample(p,out original) || !original.IsValid) throw new InvalidOperationException("Terrain height unavailable at dragged border; last published edit retained.");
            if(!Active || (!IsCoast(edge) && !AtJoin(p))) return original;
            Vec3 corrected;
            if(!_heights.TryGetValue(p,out corrected))
            {
                corrected=_surface(p);
                if(!corrected.IsValid) throw new InvalidOperationException("Invalid coastline surface sample.");
                _heights.Add(p,corrected);
            }
            return corrected;
        }
        internal bool EmitCap(Mesh mesh,UIntPtr handle,int node,BorderEdge edge,EditSnapshot state)
        {
            if(!Active) return false;
            Vec2 center=V(state.Points[node]),direction=V(state.Points[edge.B])-V(state.Points[edge.A]); direction.Normalize();
            Vec2 normal=new Vec2(-direction.y,direction.x); Vec3 c=Point(center,edge);
            var outward=new List<Vec2>();
            if(AtJoin(center))
                foreach(BorderEdge incident in (node<_graph.Incident.Length?_graph.Incident[node]:new List<int>()).Where(i=>!state.Deleted[i]).Select(i=>_graph.Edges[i]).Concat(state.Bridges.Where(e=>e.A==node||e.B==node)))
                {
                    Vec2 d=V(state.Points[incident.A==node?incident.B:incident.A])-center;
                    if(d.Normalize()>.8002f) outward.Add(d);
                }
            foreach(int side in new[]{1,-1})
            {
                Vec3 previous=Point(center+direction*.8f,edge);
                for(int step=1;step<=4;step++)
                {
                    float angle=(float)Math.PI*step/4;
                    Vec3 current=Point(center+direction*((float)Math.Cos(angle)*.8f)+normal*((float)Math.Sin(angle)*.8f*side),edge);
                    var polygon=CoastCapClipper.Clip(c,previous,current,outward);
                    uint color=side==1?edge.Left:edge.Right;
                    for(int i=1;i+1<polygon.Count;i++)
                    {
                        Vec3 a=polygon[0],b=polygon[i],d=polygon[i+1];
                        if(Math.Abs(CoastCapClipper.AreaTwice(a,b,d))<1e-8) continue;
                        mesh.AddTriangle(a,b,d,Vec2.Zero,Vec2.Zero,Vec2.Zero,color,handle);
                        mesh.AddTriangle(a,d,b,Vec2.Zero,Vec2.Zero,Vec2.Zero,color,handle);
                    }
                    previous=current;
                }
            }
            return true;
        }
        private static Vec2 V(Point2 p) => new Vec2(p.X,p.Y);
    }
}
