using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace Aoc.BorderEditPrototype
{
    internal static class NativeBindings
    {
        internal const string RendererHash="560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E";
        internal const string Owner="Aoc.BorderEditPrototype.v01";
        internal static FieldInfo BuilderEntities, LiveEntities, Alpha, SceneField;
        internal static MethodInfo Intersection;
        private static FieldInfo[] _cameraLeftFields;
        internal delegate bool SamplePoint(Vec2 position, out Vec3 point);
        internal delegate void Quad(Mesh mesh, Vec3 a, Vec3 b, Vec3 c, Vec3 d, uint color, UIntPtr handle);
        internal delegate void Cap(Mesh mesh, Vec2 position, Vec3 center, Vec2 direction, Vec2 normal, uint left, uint right, UIntPtr handle);
        internal static SamplePoint Sample;
        internal static Quad EmitQuad;
        internal static Cap EmitCap;
        internal static Harmony Harmony;
        internal static Type BuilderType, BehaviorType, MapType;
        internal static bool Ready;
        private static string _waitingFor;

        internal static bool TryBind()
        {
            Assembly core = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetName().Name=="AgesOfCalradia");
            MapType = AccessTools.TypeByName("SandBox.View.Map.MapScreen");
            if (core == null || MapType == null)
            {
                string waiting="Waiting for startup dependencies: core="+(core!=null)+"; map="+(MapType!=null);
                if(waiting!=_waitingFor) { _waitingFor=waiting; PrototypeLog.Write(waiting); }
                return false;
            }
            using (var sha=SHA256.Create()) using (var stream=File.OpenRead(core.Location))
                if (BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","") != RendererHash) throw new InvalidOperationException("Unsupported political renderer; editor disabled.");
            BuilderType=core.GetType("TwelveMonthCalendar.CampaignPoliticalTerritoryFill+Builder",true);
            BehaviorType=core.GetType("TwelveMonthCalendar.CampaignKingdomBorderBehavior",true);
            BuilderEntities=RequiredField(BuilderType,"_frontierEntities");
            LiveEntities=RequiredField(BehaviorType,"_politicalFrontierEntities");
            Alpha=RequiredField(BehaviorType,"_politicalLayerAlpha"); SceneField=RequiredField(BehaviorType,"_mapScene");
            Sample=(SamplePoint)Delegate.CreateDelegate(typeof(SamplePoint),Required(BuilderType,"TryGetFrontierPoint"));
            EmitQuad=(Quad)Delegate.CreateDelegate(typeof(Quad),Required(BuilderType,"AddDoubleSidedQuad"));
            EmitCap=(Cap)Delegate.CreateDelegate(typeof(Cap),Required(BuilderType,"AddSplitRoundCap"));
            Intersection=Required(MapType,"GetCursorIntersectionPoint");
            if (Intersection.GetParameters().Length != 7) throw new InvalidOperationException("Unsupported map picking signature.");
            Harmony=new Harmony(Owner);
            try
            {
                // Install inner observers before Harmony recompiles their callers;
                // otherwise small native wrappers can be inlined without capture.
                NativeTriangleCapture.Install();
                NativeRepairCapture.Install();
                Patch(BehaviorType,"ClearPoliticalFillEntities","RepairBeforeFillClear",null,null);
                Patch(BuilderType,"AddHalfRoundCap","CapStart","CapEnd",null);
                Patch(BuilderType,"AddFrontierSegment","SegmentStart","SegmentEnd",null);
                Patch(BuilderType,"Advance","AdvanceStart","AdvanceEnd","AdvanceFailure");
                Patch(BuilderType,"AddFrontierEntity",null,"RowEnd",null);
                Patch(BuilderType,"TakeFrontierEntities",null,"TakeEnd",null);
                Patch(BehaviorType,"ReplacePoliticalFrontierEntities",null,"Published",null);
                Patch(BehaviorType,"ClearPoliticalFrontierEntities","BeforeClear",null,null);
                // Hash-pinned approved renderer: this routine unconditionally reveals
                // frontier rows. Skip only the attached editor's presentation while active;
                // NativePreview restores visibility on close. Unknown owners fail binding.
                // Verify-NativeBindings checks target/ownership and active-instance scoping.
                Patch(BehaviorType,"ApplyFrontierZoomPresentation","FrontierPresentation",null,null);
                Patch(MapType,"HandleLeftMouseButtonClick","MapClick",null,null);
                Patch(MapType,"OnFrameTick",null,"MapTick",null);
                Patch(MapType,"OnFinalize","MapClosing",null,null);
                Patch(MapType,"OpenEscapeMenu","EscapeMenu",null,null);
                // Bannerlord 1.4.8: camera panning is consumed here, not by
                // TickNavigationInput (which opens campaign navigation screens).
                // Mask only left-drag input throughout editing, including the
                // initial press before the editor's map-frame postfix runs.
                Type cameraType=AccessTools.TypeByName("SandBox.View.Map.MapCameraView")
                    ?? throw new MissingMemberException("MapCameraView");
                MethodInfo cameraTick=Required(cameraType,"OnBeforeTick");
                ParameterInfo[] cameraParameters=cameraTick.GetParameters();
                if(cameraParameters.Length!=1 || !cameraParameters[0].ParameterType.IsByRef)
                    throw new InvalidOperationException("Unsupported camera input signature.");
                Type cameraInput=cameraParameters[0].ParameterType.GetElementType();
                _cameraLeftFields=new[]{"LeftMouseButtonPressed","LeftMouseButtonDown","LeftMouseButtonReleased","LeftButtonDraggingMode"}
                    .Select(name=>RequiredField(cameraInput,name)).ToArray();
                if(_cameraLeftFields.Any(field=>field.FieldType!=typeof(bool)))
                    throw new InvalidOperationException("Unsupported camera left-drag fields.");
                Patch(cameraType,"OnBeforeTick","CameraInput",null,null);
                // Bannerlord 1.4.8 ComputeMapCamera: only its local elevation argument
                // changes while 2D editing. Native target, bearing, zoom, clipping and
                // stored elevation remain intact; close resumes ordinary calculation.
                // Unknown owners/signatures fail binding. Actual Harmony argument flow
                // and inactive restoration are covered by Verify-NativeBindings.
                MethodInfo compute=Required(cameraType,"ComputeMapCamera");
                ParameterInfo[] cameraArgs=compute.GetParameters();
                if(cameraArgs.Length!=5 || cameraArgs[2].ParameterType!=typeof(float) || cameraArgs[2].Name!="cameraElevation" || compute.ReturnType!=typeof(MatrixFrame))
                    throw new InvalidOperationException("Unsupported top-down camera target.");
                Patch(cameraType,"ComputeMapCamera","CameraTopDown",null,null);
                Ready=true; PrototypeLog.Write("Bound approved renderer; map and scene targets resolved."); return true;
            }
            catch { Harmony.UnpatchAll(Owner); throw; }
        }
        internal static MethodInfo Required(Type type,string name) => AccessTools.Method(type,name) ?? throw new MissingMethodException(type.FullName,name);
        internal static void FilterCameraInput(object input,bool editing)
        {
            if(!editing) return;
            foreach(FieldInfo field in _cameraLeftFields) field.SetValue(input,false);
        }
        private static FieldInfo RequiredField(Type type,string name) => AccessTools.Field(type,name) ?? throw new MissingFieldException(type.FullName,name);
        private static HarmonyMethod Hook(string name) => name==null ? null : new HarmonyMethod(typeof(NativeHooks),name);
        private static void Patch(Type type,string name,string prefix,string postfix,string finalizer)
        {
            MethodInfo method=Required(type,name);
            ValidateExisting(method);
            Harmony.Patch(method,prefix:Hook(prefix),postfix:Hook(postfix),finalizer:Hook(finalizer));
        }
        internal static void ValidateExisting(MethodInfo method)
        {
            Patches existing=HarmonyLib.Harmony.GetPatchInfo(method);
            if(existing!=null)
            {
                PrototypeLog.Write("Binding "+method.DeclaringType.Name+"."+method.Name+"; existing owners="+string.Join(",",existing.Owners));
                bool allowed=existing.Prefixes.All(p=>NativePatchCompatibility.Allowed(method,p,"prefix"))
                    && existing.Postfixes.All(p=>NativePatchCompatibility.Allowed(method,p,"postfix"))
                    && existing.Finalizers.All(p=>NativePatchCompatibility.Allowed(method,p,"finalizer"))
                    && existing.Transpilers.All(p=>p.owner==Owner);
                if(!allowed) throw new InvalidOperationException("Unsupported patch on "+method.DeclaringType.Name+"."+method.Name+": "+string.Join(",",existing.Owners));
            }
        }
    }

    // Hook failure is an integration boundary: disable the editor, retain native game execution, log the failure.
    internal static class NativeHooks
    {
        internal static NativeCapture Pending;
        internal static void AdvanceStart(object __instance)
        {
            Safe(()=>
            {
                PrototypeRuntime.OnNativeThread();
                if (Pending == null || !ReferenceEquals(Pending.Builder,__instance)) Pending=new NativeCapture{Builder=__instance};
                Pending.InAdvance=true;
            });
        }
        internal static void AdvanceEnd() { if(Pending!=null) Pending.InAdvance=false; }
        internal static Exception AdvanceFailure(Exception __exception)
        { AdvanceEnd(); if(__exception!=null) PrototypeRuntime.Fail(__exception); return __exception; }
        internal static void SegmentStart(Mesh __0,Vec2 __1,Vec2 __2)
        { Safe(()=>Pending?.BeginSegment(__0,__1,__2)); }
        internal static void SegmentEnd(bool __result) { Safe(()=>Pending?.EndSegment(__result)); }
        internal static void CapStart(Vec2 __1) { Safe(()=>Pending?.BeginCap(__1)); }
        internal static void CapEnd() { Safe(()=>Pending?.EndCap()); }
        internal static void Triangle(Mesh __instance,Vec3 __0,Vec3 __1,Vec3 __2,Vec2 __3,Vec2 __4,Vec2 __5,uint __6)
        { if (Pending==null || !Pending.CapturingSegment || !PrototypeRuntime.IsNativeThread) return; Safe(()=>Pending.Triangle(__instance,__0,__1,__2,__3,__4,__5,__6)); }
        internal static void RowEnd(object __instance,Scene __0,Mesh __1) { Safe(()=>Pending?.AddRow(__instance,__0,__1)); }
        internal static void TakeEnd(object __instance,List<GameEntity> __result)
        { if(Pending!=null && ReferenceEquals(Pending.Builder,__instance)) Pending.TakenEntities=__result; }
        internal static void Published(object __instance,List<GameEntity> __0)
        {
            Safe(()=>
            {
                if(Pending==null || !ReferenceEquals(Pending.TakenEntities,__0)) return;
                Pending.Complete(); PrototypeRuntime.Attach(__instance,Pending); Pending=null;
            });
        }
        internal static void RepairBeforeFillClear(object __instance) { Safe(()=>PrototypeRuntime.BeforeFillClear(__instance)); }
        internal static void BeforeClear(object __instance) { Safe(()=>PrototypeRuntime.BeforeNativeClear(__instance)); }
        internal static bool MapClick() => !PrototypeRuntime.Active;
        internal static void CameraTopDown(ref float __2)
        { if(PrototypeRuntime.Active && PrototypeRuntime.TopDown)__2=(float)Math.PI*.5f; }
        internal static bool FrontierPresentation(object __instance) => !PrototypeRuntime.SuppressNormalFrontiers(__instance);
        internal static void MapTick(object __instance,float __0) { Safe(()=>PrototypeRuntime.Tick(__instance,__0)); }
        internal static void MapClosing() { Safe(()=>PrototypeRuntime.CloseMap()); }
        internal static bool EscapeMenu()
        { if(!PrototypeRuntime.Active) return true; PrototypeRuntime.RequestEscape(); return false; }
        // Harmony writes the modified boxed input back to the native by-ref
        // argument. Keep the rest of the camera update and input unchanged.
        internal static void CameraInput(object[] __args)
        { Safe(()=>NativeBindings.FilterCameraInput(__args[0],PrototypeRuntime.Active)); }
        private static void Safe(Action action)
        {
            if(PrototypeRuntime.Failed) return;
            try { action(); } catch(Exception error) { PrototypeRuntime.Fail(error); }
        }
    }
}
