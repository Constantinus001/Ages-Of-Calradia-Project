using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Path = System.IO.Path;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Engine;
namespace Aoc.BorderEditPrototype
{
    internal sealed class RepairCapturedRow { internal GameEntity Entity;internal string Fingerprint;internal uint Faces; }
    // Read-only observer of the hash-pinned seam fix's successful commit, before it
    // releases CPU geometry. No replacement of that module or protected renderer.
    internal static class NativeRepairCapture
    {
        private static object _builder;private static Dictionary<int,RepairCapturedRow> _rows;
        private static Type _seam;internal static FillRepairPlan Plan;
        internal static void Install()
        {
            string path=PublishedBorderLayout.Enabled?Path.Combine(PublishedBorderLayout.Root,"Repair.xml"):Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(NativeRepairCapture).Assembly.Location),"..","..","Repair.xml"));
            if(!File.Exists(path))return;Plan=FillRepairPlan.Load(path);
            _seam=AccessTools.TypeByName("AgesOfCalradia.PoliticalFillSeamFix.NativeFillSeamFix");
            if(_seam==null||NativePatchCompatibility.Hash(_seam.Assembly)!="0918C5DBB2AD59BFAB9F768EE781D9F86A6DDDDE342D6D76A2BFA52EB88F063E")throw new InvalidOperationException("Fill repair requires the verified seam fix.");
            Type transaction=_seam.Assembly.GetType("AgesOfCalradia.PoliticalFillSeamFix.NativeMeshTransaction",true);
            MethodInfo target=AccessTools.Method(transaction,"Commit");
            if(target==null||!target.IsStatic||target.GetParameters().Length!=2)throw new MissingMethodException("Verified fill commit target missing.");
            NativeBindings.ValidateExisting(target);
            NativeBindings.Harmony.Patch(target,postfix:new HarmonyMethod(typeof(NativeRepairCapture),nameof(Committed)));
        }
        private static object Field(object value,string name)=>AccessTools.Field(value.GetType(),name).GetValue(value);
        private static Point2 Point(object value)=>new Point2((float)Field(value,"X"),(float)Field(value,"Y"));
        internal static void Committed(object __0)
        {
            try
            {
                object generation=AccessTools.Field(_seam,"_generation").GetValue(null);
                var result=Field(Field(generation,"Prepared"),"Result");
                var tokens=Plan.Rows.ToDictionary(r=>r.Id,r=>new List<string>());
                foreach(object t in (IEnumerable)Field(result,"Triangles"))
                {
                    List<string> list;if(!tokens.TryGetValue((int)Field(t,"RowId"),out list))continue;
                    list.Add(FillRepairPlan.TriangleKey(Point(Field(t,"A")),Point(Field(t,"B")),Point(Field(t,"C")),(uint)Field(t,"Color")));
                }
                var captured=new Dictionary<int,RepairCapturedRow>();
                foreach(object row in (IEnumerable)__0)
                {
                    int id=(int)Field(row,"Id");if(!tokens.ContainsKey(id))continue;
                    Mesh mesh=(Mesh)Field(row,"Candidate");if(mesh==null)throw new InvalidOperationException("Repair capture has no committed mesh.");
                    captured.Add(id,new RepairCapturedRow{Entity=(GameEntity)Field(row,"Entity"),Faces=mesh.GetFaceCount(),Fingerprint=FillRepairPlan.Fingerprint(tokens[id])});
                }
                _builder=Field(generation,"Builder");_rows=captured;
                PrototypeLog.Write("Fill repair captured verified native row identities: "+captured.Count);
            }
            catch(Exception error){_rows=null;PrototypeLog.Write("Fill repair capture unavailable; original fill retained: "+error);}
        }
        internal static Dictionary<int,RepairCapturedRow> For(object builder)=>ReferenceEquals(_builder,builder)?_rows:null;
        // Drive the existing hash-pinned seam lifecycle on the native thread while
        // the loading screen is active. Only immutable preparation runs off-thread.
        // No new Harmony target: this calls the same Tick used by the seam module.
        internal static void Advance(object builder)
        {
            PrototypeRuntime.RequireNativeThread();
            if(For(builder)!=null)return;
            if(_seam==null)throw new InvalidOperationException("Verified seam lifecycle is unavailable.");
            object generation=AccessTools.Field(_seam,"_generation").GetValue(null);
            if(generation==null||!ReferenceEquals(Field(generation,"Builder"),builder)
                ||(bool)Field(generation,"Broken")||!(bool)Field(generation,"Published")
                ||(bool)Field(generation,"Committed"))
                throw new InvalidOperationException("Verified seam generation cannot provide this repair capture.");
            if(!(bool)AccessTools.Field(_seam,"_enabled").GetValue(null))
                throw new InvalidOperationException("Verified seam lifecycle is disabled.");
            AccessTools.Method(_seam,"Tick",Type.EmptyTypes).Invoke(null,null);
            var preparation=Field(generation,"Preparation") as System.Threading.Tasks.Task;
            if(preparation!=null&&!preparation.IsCompleted)System.Threading.Thread.Sleep(1);
        }
        internal static void Clear(){_rows=null;_builder=null;}
    }
}

