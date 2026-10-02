using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using Path=System.IO.Path;

namespace AgesOfCalradia.PoliticalBorderComparison
{
    public sealed class BorderComparisonSubModule:MBSubModuleBase
    {
        protected override void OnSubModuleLoad(){base.OnSubModuleLoad();BorderComparison.Initialize();}
        protected override void OnApplicationTick(float dt){base.OnApplicationTick(dt);BorderComparison.Tick();}
        protected override void OnSubModuleUnloaded(){BorderComparison.Stop();base.OnSubModuleUnloaded();}
    }
    internal static class BorderComparison
    {
        private const string Owner="aoc.tests.border-comparison.v1";
        private const string CoreHash="560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E";
        private const string FillHash="0918C5DBB2AD59BFAB9F768EE781D9F86A6DDDDE342D6D76A2BFA52EB88F063E";
        private sealed class Work
        {
            internal object Generation,Behavior;
            internal Scene Scene;
            internal Task<List<Face>> Task;
            internal List<Face> Faces;
            internal readonly List<GameEntity> Entities=new List<GameEntity>();
            internal readonly List<Mesh> Meshes=new List<Mesh>();
            internal int Uploaded;
            internal bool Committed;
        }
        private static Harmony _harmony;
        private static Type _fill;
        private static FieldInfo _frontiers;
        private static Work _work;
        private static int _thread;
        private static bool _enabled;
        private static Material _normal,_xray;
        private static string _directory,_mode="A";
        private static StreamWriter _log;
        private static readonly Stopwatch Clock=Stopwatch.StartNew();
        private static long _nextMode;
        private static object Field(object o,string name){return AccessTools.Field(o.GetType(),name).GetValue(o);}
        internal static void Initialize()
        {
            try
            {
                _directory=Path.GetDirectoryName(typeof(BorderComparison).Assembly.Location);
                Directory.CreateDirectory(Path.Combine(_directory,"Logs"));
                _log=new StreamWriter(Path.Combine(_directory,"Logs","PoliticalBorderComparison.log"),true){AutoFlush=true};
                Assembly core=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="AgesOfCalradia");
                Assembly fill=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="AgesOfCalradia.PoliticalFillSeamFix");
                VerifyHash(core,CoreHash);VerifyHash(fill,FillHash);
                _fill=fill.GetType("AgesOfCalradia.PoliticalFillSeamFix.NativeFillSeamFix",true);
                Type transaction=fill.GetType("AgesOfCalradia.PoliticalFillSeamFix.NativeMeshTransaction",true);
                Type behavior=core.GetType("TwelveMonthCalendar.CampaignKingdomBorderBehavior",true);
                _frontiers=AccessTools.Field(behavior,"_politicalFrontierEntities");
                if(_frontiers==null)throw new MissingFieldException("Frontier list unavailable.");
                _harmony=new Harmony(Owner);
                Patch(AccessTools.Method(transaction,"Commit"),"FillCommitted");
                Patch(AccessTools.Method(behavior,"ApplyFrontierZoomPresentation"),"ZoomApplied");
                _enabled=true;Log("enabled;working fill hash pinned;default A;B is unrestricted depth-override diagnostic, NOT water-only");
            }
            catch(Exception ex){Log("initialization failed: "+ex);_harmony?.UnpatchAll(Owner);}
        }
        private static void VerifyHash(Assembly assembly,string expected)
        {
            using(var sha=SHA256.Create())using(var stream=File.OpenRead(assembly.Location))
                if(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","")!=expected)throw new InvalidOperationException("Unsupported assembly: "+assembly.FullName);
        }
        private static void Patch(MethodInfo target,string postfix)
        {
            if(target==null)throw new MissingMethodException(postfix);
            var info=Harmony.GetPatchInfo(target);
            if(info!=null && info.Owners.Any(o=>o!=Owner))throw new InvalidOperationException("Competing patch on "+target);
            _harmony.Patch(target,postfix:new HarmonyMethod(typeof(BorderComparison),postfix));
            Log("bound "+target);
        }
        // Postfix observes successful mesh transaction only. No modification to
        // fill geometry or its working assembly. CPU worker reads immutable faces.
        private static void FillCommitted()
        {
            if(!_enabled || !OnThread())return;
            try
            {
                object generation=AccessTools.Field(_fill,"_generation").GetValue(null);
                object result=Field(Field(generation,"Prepared"),"Result");
                var captured=(IEnumerable)Field(result,"Triangles");
                CleanupPending();
                _work=new Work{Generation=generation,Behavior=Field(generation,"Behavior"),Scene=(Scene)Field(generation,"Scene")};
                _work.Task=Task.Run(()=>FillBoundaryBands.Build(ReadFaces(captured)));
                Log("boundary build queued from successfully published fill");
            }
            catch(Exception ex){Log("boundary capture failed; original borders retained: "+ex);}
        }
        private static List<Face> ReadFaces(IEnumerable captured)
        {
            var result=new List<Face>();
            foreach(object triangle in captured)
                result.Add(new Face{A=ReadPoint(Field(triangle,"A")),B=ReadPoint(Field(triangle,"B")),C=ReadPoint(Field(triangle,"C")),
                    Color=(uint)Field(triangle,"Color"),Parent=result.Count});
            return result;
        }
        private static P ReadPoint(object p){return new P((float)Field(p,"X"),(float)Field(p,"Y"),(float)Field(p,"Z"));}
        private static bool OnThread(){return _thread!=0 && Thread.CurrentThread.ManagedThreadId==_thread;}
        internal static void Tick()
        {
            if(!_enabled)return;
            Interlocked.CompareExchange(ref _thread,Thread.CurrentThread.ManagedThreadId,0);
            if(!OnThread()){_enabled=false;Log("thread mismatch; no native calls");return;}
            try
            {
                if(_work==null)return;
                if(!ReferenceEquals(_work.Generation,AccessTools.Field(_fill,"_generation").GetValue(null))){CleanupPending();return;}
                if(!_work.Committed)
                {
                    if(!_work.Task.IsCompleted)return;
                    if(!(bool)Field(_work.Generation,"Committed"))return;
                    if(_work.Faces==null){_work.Faces=_work.Task.GetAwaiter().GetResult();Log("boundary triangles="+_work.Faces.Count);}
                    Upload();return;
                }
                if(Clock.ElapsedMilliseconds>=_nextMode)
                {
                    _nextMode=Clock.ElapsedMilliseconds+1000;
                    string path=Path.Combine(_directory,"PoliticalBorderComparison.mode");
                    string mode=File.Exists(path)?File.ReadAllText(path).Trim().ToUpperInvariant():"A";
                    if(mode!="A" && mode!="B"){Log("invalid mode ignored;expected A or B");return;}
                    if(mode!=_mode){SetMode(mode);Log("mode="+mode+(mode=="B"?";DEPTH OVERRIDE: may show through land/ships/buildings":";normal depth"));}
                }
            }
            catch(Exception ex){Log("trial failed: "+ex);CleanupPending();_enabled=false;}
        }
        private static void Upload()
        {
            object builder=Field(_work.Generation,"Builder");
            if(!(bool)AccessTools.Property(builder.GetType(),"IsComplete").GetValue(builder,null))return;
            if(_normal==null)
            {
                _normal=Material.GetFromResource("vertex_color_mat").CreateCopy();
                _xray=_normal.CreateCopy();
                _xray.Flags|=MaterialFlags.NoDepthTest|MaterialFlags.NoModifyDepthBuffer;
            }
            // One bounded mesh per tick; all candidates stay hidden until complete.
            int end=Math.Min(_work.Uploaded+512,_work.Faces.Count);
            Mesh mesh=Mesh.CreateMesh(true);mesh.SetMaterial(_normal);mesh.SetMeshRenderOrder(100);
            UIntPtr handle=mesh.LockEditDataWrite();
            try
            {
                for(int i=_work.Uploaded;i<end;i++)
                {
                    Face f=_work.Faces[i];Vec3 a=Position(f.A),b=Position(f.B),c=Position(f.C);
                    uint color=Brighten(f.Color);
                    mesh.AddTriangle(a,b,c,Vec2.Zero,Vec2.Zero,Vec2.Zero,color,handle);
                    mesh.AddTriangle(a,c,b,Vec2.Zero,Vec2.Zero,Vec2.Zero,color,handle);
                }
            }
            finally{mesh.UnlockEditDataWrite(handle);}
            if(mesh.GetFaceCount()!=checked((uint)(end-_work.Uploaded)*2))throw new InvalidOperationException("Native border face count mismatch.");
            mesh.ComputeNormals();mesh.RecomputeBoundingBox();
            GameEntity entity=GameEntity.CreateEmpty(_work.Scene,false);_work.Entities.Add(entity);
            entity.SetVisibilityExcludeParents(false);entity.AddMesh(mesh);entity.SetAlpha(1);entity.SetReadyToRender(true);
            _work.Meshes.Add(mesh);_work.Uploaded=end;
            if(end<_work.Faces.Count)return;
            var current=(List<GameEntity>)_frontiers.GetValue(_work.Behavior);var old=current.ToArray();
            try
            {
                foreach(var item in _work.Entities)item.SetVisibilityExcludeParents(true);
                foreach(var item in old)item.SetVisibilityExcludeParents(false);
                current.Clear();current.AddRange(_work.Entities);
            }
            catch
            {
                current.Clear();current.AddRange(old);
                foreach(var item in old)item.SetVisibilityExcludeParents(true);
                throw;
            }
            _work.Committed=true;
            _mode="A";
            foreach(var item in old){try{item.Remove(0);}catch(Exception ex){Log("old border removal failed: "+ex);}}
            Log("COMMITTED A;triangles="+end+";entities="+_work.Entities.Count+";island cutouts retained;fill untouched");
            _work.Faces=null;_work.Task=null;
            InformationManager.DisplayMessage(new InformationMessage("Border comparison A applied: follows political fill."));
        }
        private static uint Brighten(uint color)
        {
            uint r=Math.Min(255,((color>>16)&255)*85/50),g=Math.Min(255,((color>>8)&255)*85/50),b=Math.Min(255,(color&255)*85/50);
            return 0xff000000u|(r<<16)|(g<<8)|b;
        }
        private static Vec3 Position(P p){return new Vec3(p.X,p.Y,p.Z+0.15f);}
        private static void SetMode(string mode)
        {
            Material target=mode=="B"?_xray:_normal;
            try{foreach(var mesh in _work.Meshes)mesh.SetMaterial(target);}
            catch{foreach(var mesh in _work.Meshes)mesh.SetMaterial(_normal);_mode="A";throw;}
            _mode=mode;
            InformationManager.DisplayMessage(new InformationMessage(mode=="B"?"Border B: depth-override diagnostic; may show through objects.":"Border A: normal depth."));
        }
        private static void ZoomApplied(object __instance)
        {
            if(!_enabled || !OnThread() || _work==null || !_work.Committed || !ReferenceEquals(_work.Behavior,__instance))return;
            try{MatrixFrame frame=MatrixFrame.Identity;foreach(var entity in _work.Entities)entity.SetGlobalFrame(in frame);}
            catch(Exception ex){Log("border zoom presentation failed: "+ex);}
        }
        private static void CleanupPending()
        {
            if(_work==null)return;
            if(_work.Task!=null)_work.Task.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
            if(!_work.Committed)foreach(var entity in _work.Entities){try{entity.Remove(0);}catch(Exception ex){Log("candidate cleanup failed: "+ex);}}
            _work=null;
        }
        private static void Log(string message){try{_log?.WriteLine(DateTime.UtcNow.ToString("O")+" "+message);}catch(Exception ex){Trace.TraceError(ex.ToString());}}
        internal static void Stop(){_enabled=false;try{_harmony?.UnpatchAll(Owner);if(OnThread())CleanupPending();_log?.Dispose();}catch(Exception ex){Trace.TraceError(ex.ToString());}}
    }
}
