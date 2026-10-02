using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Engine;
using TaleWorlds.Library;
namespace Aoc.BorderEditPrototype
{
    // Read-only adapter to the hash-verified renderer's captured site index and land policy.
    internal sealed class NativeLocationStyle
    {
        private readonly object _index;private readonly MethodInfo _nearest,_land;private readonly PropertyInfo _color;
        internal NativeLocationStyle(NativeCapture source)
        {
            Type builder=source.Builder.GetType();_index=AccessTools.Field(builder,"_siteIndex").GetValue(source.Builder);
            _nearest=AccessTools.Method(_index.GetType(),"FindNearest",new[]{typeof(Vec2)});
            _color=AccessTools.Property(_nearest.ReturnType,"Color");
            _land=AccessTools.Method(builder.Assembly.GetType("TwelveMonthCalendar.CampaignMapTerrainGridCache",true),"IsPoliticalLandExact",new[]{typeof(Vec2)});
            if(_nearest==null||_color==null||_land==null)throw new MissingMemberException("Local border colour/land lookup unavailable.");
        }
        internal bool Land(Point2 p)=>(bool)_land.Invoke(null,new object[]{new Vec2(p.X,p.Y)});
        internal uint Color(Point2 p,int brightness)
        {
            object owner=_nearest.Invoke(_index,new object[]{new Vec2(p.X,p.Y)});
            if(owner==null)throw new InvalidOperationException("No local territory colour at this location.");
            uint c=(uint)_color.GetValue(owner,null);
            return 0xFF000000u|(((c>>16)&255)*(uint)brightness/100<<16)|(((c>>8)&255)*(uint)brightness/100<<8)|((c&255)*(uint)brightness/100);
        }
        internal void Border(Point2 a,Point2 b,out uint left,out uint right)
        {
            BorderDrawingStyle.Resolve(a,b,Land,p=>Color(p,85),out left,out right);
        }

        internal static Vec3 Surface(Point2 p,float lift)
        {
            Vec3 v;if(!NativeBindings.Sample(new Vec2(p.X,p.Y),out v)||!v.IsValid)throw new InvalidOperationException("Map surface unavailable at drawing point.");
            v.z+=lift-5f;return v;
        }
    }
}
