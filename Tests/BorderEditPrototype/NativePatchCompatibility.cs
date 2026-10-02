using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;

namespace Aoc.BorderEditPrototype
{
    // Only the reviewed installed revisions may share builder capture targets.
    // Coast changes geometry; NativeCoastCompatibility supplies its preview policy.
    internal static class NativePatchCompatibility
    {
        internal const string CoastHash="AA4AE248536C732E16E4CF74606A0C42513FFA11F7242C160A4FF4C6C525A807";
        private static readonly Dictionary<Assembly,string> Hashes=new Dictionary<Assembly,string>();
        internal static bool Allowed(MethodInfo target, Patch patch, string kind)
        {
            if(patch.owner==NativeBindings.Owner) return true;
            if(target.DeclaringType!=NativeBindings.BuilderType) return false;
            string expectedType, expectedHash, expectedMethod;
            if(patch.owner=="aoc.tests.political-render-probe.v1")
            {
                expectedType="AgesOfCalradia.PoliticalRenderDiagnostics.NativeRenderProbe";
                expectedHash="1AC12E731B3C7E38EE50C09B60DCF97A5CD90844607F8B36E891F590A0CDC317";
                if(target.Name=="Advance") expectedMethod=kind=="prefix"?"AdvanceStart":kind=="postfix"?"AdvanceEnd":null;
                else if(target.Name=="AddFrontierSegment") expectedMethod=kind=="prefix"?"SegmentStart":kind=="postfix"?"SegmentEnd":null;
                else if(target.Name=="AddDoubleSidedQuad") expectedMethod=kind=="prefix"?"QuadStart":null;
                else return false;
            }
            else if(patch.owner=="aoc.political-fill-seam-fix.v1" && target.Name=="Advance")
            {
                expectedType="AgesOfCalradia.PoliticalFillSeamFix.NativeFillSeamFix";
                expectedHash="0918C5DBB2AD59BFAB9F768EE781D9F86A6DDDDE342D6D76A2BFA52EB88F063E";
                expectedMethod=kind=="prefix"?"AdvanceStart":kind=="finalizer"?"AdvanceFinished":null;
            }
            else if(patch.owner=="aoc.coast-surface-fix.v1")
            {
                expectedType="AgesOfCalradia.CoastSurfaceFix.CoastSurfacePatch"; expectedHash=CoastHash;
                if(target.Name=="Advance") expectedMethod=kind=="prefix"?"AdvanceStart":kind=="finalizer"?"AdvanceEnd":null;
                else if(target.Name=="AddFrontierSegment") expectedMethod=kind=="prefix"?"SegmentStart":kind=="finalizer"?"SegmentEnd":null;
                else if(target.Name=="AddDoubleSidedFanTriangle") expectedMethod=kind=="prefix"?"CapStart":null;
                else return false;
            }
            else return false;
            MethodInfo method=patch.PatchMethod;
            return expectedMethod!=null && method.Name==expectedMethod && method.DeclaringType?.FullName==expectedType
                && Hash(method.DeclaringType.Assembly)==expectedHash;
        }
        internal static string Hash(Assembly assembly)
        {
            string value;
            if(!Hashes.TryGetValue(assembly,out value))
            {
                using(var sha=SHA256.Create()) using(var stream=File.OpenRead(assembly.Location))
                    value=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");
                Hashes.Add(assembly,value);
            }
            return value;
        }
    }
}
