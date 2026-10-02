using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace AgesOfCalradia.PoliticalRenderDiagnostics
{
    // Read-only coastline evidence. A false political-land result is NOT by
    // itself seawater: it may be an intentionally excluded island or channel.
    internal sealed class CoastlineEvidenceProbe : IDisposable
    {
        private readonly StreamWriter _paths, _surfaces;
        private MethodInfo _region, _terrain;
        private bool _failed;
        internal CoastlineEvidenceProbe(string folder)
        {
            _paths = new StreamWriter(System.IO.Path.Combine(folder,"coastline-paths.csv"));
            _surfaces = new StreamWriter(System.IO.Path.Combine(folder,"coastline-surfaces.csv"));
            _paths.WriteLine("segment,accepted,firstX,firstY,secondX,secondY,leftLand,rightLand,leftNativeTerrain,rightNativeTerrain,leftFaction,rightFaction,leftColor,rightColor,interpretation");
            _surfaces.WriteLine("segment,sample,x,y,rawZ,campaignSurface,surfaceQueryValid,closeSurfaceClearance,fullSurfaceClearance");
        }
        internal static string Interpret(bool leftLand, bool rightLand)
        {
            return leftLand && rightLand ? "inland-keep-unchanged" :
                leftLand != rightLand ? "land-nonland-boundary-verify-water-or-exclusion" : "no-political-land-support";
        }
        internal void Path(object builder,int id,bool accepted,Vec2 first,Vec2 second)
        {
            if(_failed)return;
            try
            {
                float dx=second.x-first.x,dy=second.y-first.y;
                double length=Math.Sqrt((double)dx*dx+(double)dy*dy);
                if(length<0.001)return;
                Vec2 middle=(first+second)*0.5f;
                Vec2 normal=new Vec2((float)(-dy/length*2.75),(float)(dx/length*2.75));
                if(_region==null)_region=AccessTools.Method(builder.GetType(),"GetFrontierRegion",new[]{typeof(Vec2)});
                if(_terrain==null)_terrain=AccessTools.Method(builder.GetType().Assembly.GetType("TwelveMonthCalendar.CampaignMapTerrainGridCache",true),"TryGetNativeTerrain");
                object left=_region.Invoke(builder,new object[]{middle+normal}),right=_region.Invoke(builder,new object[]{middle-normal});
                bool leftLand=(bool)RegionValue(left,"Land"),rightLand=(bool)RegionValue(right,"Land");
                object lo=RegionValue(left,"Owner"),ro=RegionValue(right,"Owner");
                _paths.WriteLine(string.Join(",",new[]{id.ToString(),accepted.ToString(),F(first.x),F(first.y),F(second.x),F(second.y),leftLand.ToString(),rightLand.ToString(),
                    Q(Terrain(middle+normal)),Q(Terrain(middle-normal)),Q(Value(lo,"OwnerKey")),Q(Value(ro,"OwnerKey")),Q(Value(lo,"Color")),Q(Value(ro,"Color")),Interpret(leftLand,rightLand)}));
            }
            catch(Exception ex){Fail(ex);}
        }
        internal void Surface(int id,int index,Vec3 point)
        {
            if(_failed)return;
            try
            {
                float height=0;
                var position=new CampaignVec2(new Vec2(point.x,point.y),false);
                bool valid=Campaign.Current?.MapSceneWrapper!=null && Campaign.Current.MapSceneWrapper.GetHeightAtPoint(in position,ref height)
                    && !float.IsNaN(height) && !float.IsInfinity(height);
                _surfaces.WriteLine(string.Join(",",new[]{id.ToString(),index.ToString(),F(point.x),F(point.y),F(point.z),valid?F(height):"unknown",valid.ToString(),
                    valid?F(point.z-4.65f-height):"unknown",valid?F(point.z-height):"unknown"}));
            }
            catch(Exception ex){Fail(ex);}
        }
        private string Terrain(Vec2 point)
        {
            Type terrainType=_terrain.GetParameters()[1].ParameterType.GetElementType();
            object[] args={point,Activator.CreateInstance(terrainType)};
            return (bool)_terrain.Invoke(null,args)?args[1].ToString():"query-failed";
        }
        internal static object RegionValue(object obj,string name){if(obj==null)throw new ArgumentNullException(nameof(obj)); var property=AccessTools.Property(obj.GetType(),name); if(property==null)throw new MissingMemberException(obj.GetType().FullName,name); return property.GetValue(obj,null);}
        private static string Value(object obj,string name){return obj==null?"none":Convert.ToString(AccessTools.Property(obj.GetType(),name).GetValue(obj,null),CultureInfo.InvariantCulture);}
        private static string F(float f){return f.ToString("R",CultureInfo.InvariantCulture);}
        private static string Q(string text){return "\""+text.Replace("\"","\"\"")+"\"";}
        private void Fail(Exception ex){_failed=true;NativeRenderProbe.RecordOptional("coastline evidence disabled; no renderer mutation: "+ex);Flush();}
        internal void Flush(){_paths.Flush();_surfaces.Flush();}
        public void Dispose(){_paths.Dispose();_surfaces.Dispose();}
    }
}
