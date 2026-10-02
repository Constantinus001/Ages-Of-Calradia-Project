using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace Aoc.BorderEditPrototype
{
    // Exact upload-site observation, required because Mesh.AddTriangle can already
    // be inlined before this module loads. No geometry or native result changes.
    // Targets: approved Builder.AddDoubleSidedQuad (4 calls),
    // Builder.AddDoubleSidedFanTriangle (2), and the hash-pinned optional
    // CoastSurfacePatch.CapStart (2). Unknown IL/counts/owners reject activation;
    // NativeBindings removes partial patches. Upload exceptions retain original
    // behavior; observation failures use NativeHooks' logged disable boundary.
    // Verified by actual installed-target Harmony binding and exact call counts.
    internal static class NativeTriangleCapture
    {
        private static readonly Dictionary<MethodBase,int> Expected=new Dictionary<MethodBase,int>();
        private static readonly MethodInfo Triangle=typeof(Mesh).GetMethod("AddTriangle",new[]{typeof(Vec3),typeof(Vec3),typeof(Vec3),typeof(Vec2),typeof(Vec2),typeof(Vec2),typeof(uint),typeof(UIntPtr)});
        private static readonly MethodInfo RelayMethod=typeof(NativeTriangleCapture).GetMethod(nameof(Relay),BindingFlags.Static|BindingFlags.NonPublic);

        internal static void Install()
        {
            if(Triangle==null || Triangle.ReturnType!=typeof(void)) throw new MissingMethodException("Supported Mesh.AddTriangle upload signature");
            if(NativePatchCompatibility.Hash(NativeBindings.BuilderType.Assembly)!=NativeBindings.RendererHash)
                throw new InvalidOperationException("Unsupported renderer upload sites.");
            // Recompile the optional prefix before the core fan caller. The fan
            // patch then generates a fresh wrapper referring to this prefix.
            Type coast=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AgesOfCalradia.CoastSurfaceFix.CoastSurfacePatch")).FirstOrDefault(t=>t!=null);
            if(coast!=null)
            {
                if(NativePatchCompatibility.Hash(coast.Assembly)!=NativePatchCompatibility.CoastHash)
                    throw new InvalidOperationException("Unsupported coastal triangle upload sites.");
                MethodInfo cap=AccessTools.Method(coast,"CapStart",new[]{typeof(Mesh),typeof(Vec3),typeof(Vec3),typeof(Vec3),typeof(uint),typeof(UIntPtr)});
                if(cap==null || !cap.IsStatic || cap.ReturnType!=typeof(bool)) throw new MissingMethodException("CoastSurfacePatch.CapStart");
                InstallTarget(cap,2);
            }
            MethodInfo fan=AccessTools.Method(NativeBindings.BuilderType,"AddDoubleSidedFanTriangle",new[]{typeof(Mesh),typeof(Vec3),typeof(Vec3),typeof(Vec3),typeof(uint),typeof(UIntPtr)});
            MethodInfo quad=AccessTools.Method(NativeBindings.BuilderType,"AddDoubleSidedQuad",new[]{typeof(Mesh),typeof(Vec3),typeof(Vec3),typeof(Vec3),typeof(Vec3),typeof(uint),typeof(UIntPtr)});
            if(fan==null || quad==null || !fan.IsStatic || !quad.IsStatic || fan.ReturnType!=typeof(void) || quad.ReturnType!=typeof(void))
                throw new MissingMethodException("Supported protected frontier upload helpers");
            InstallTarget(fan,2); InstallTarget(quad,4);
        }
        private static void InstallTarget(MethodInfo target,int count)
        {
            NativeBindings.ValidateExisting(target);
            var existing=Harmony.GetPatchInfo(target);
            if(existing!=null && existing.Transpilers.Any(p=>p.owner==NativeBindings.Owner && p.PatchMethod.DeclaringType==typeof(NativeTriangleCapture))) return;
            Expected[target]=count;
            NativeBindings.Harmony.Patch(target,transpiler:new HarmonyMethod(typeof(NativeTriangleCapture),nameof(ObserveUploads)));
        }
        internal static IEnumerable<CodeInstruction> ObserveUploads(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod)
        {
            var code=instructions.ToList(); int expected;
            if(!Expected.TryGetValue(__originalMethod,out expected)) throw new InvalidOperationException("Unregistered border upload target.");
            var uploads=code.Where(i=>(i.opcode==OpCodes.Call || i.opcode==OpCodes.Callvirt) && Equals(i.operand,Triangle)).ToArray();
            if(uploads.Length!=expected) throw new InvalidOperationException("Unsupported triangle upload IL in "+__originalMethod+": expected "+expected+", found "+uploads.Length+".");
            foreach(CodeInstruction instruction in uploads)
            {
                // Keep all labels and exception blocks attached to the call.
                // The instance receiver is the relay's first explicit argument.
                instruction.opcode=OpCodes.Call; instruction.operand=RelayMethod;
            }
            PrototypeLog.Write("Captured triangle upload sites: "+__originalMethod.DeclaringType.FullName+"."+__originalMethod.Name+";calls="+expected);
            return code;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Relay(Mesh mesh,Vec3 a,Vec3 b,Vec3 c,Vec2 u,Vec2 v,Vec2 w,uint color,UIntPtr handle)
        {
            mesh.AddTriangle(a,b,c,u,v,w,color,handle);
            NativeHooks.Triangle(mesh,a,b,c,u,v,w,color);
        }
    }
}
