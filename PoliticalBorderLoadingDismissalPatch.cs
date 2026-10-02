using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace AgesOfCalradia.PoliticalBorderOptimizer
{
    internal static class PublishedLoadingDismissalPatch
    {
        // Reviewed SandBox.View 1.4.8 pattern: counter != 3 branches past the
        // single static dismissal call; following instructions reset counter.
        // Fail patch installation on any drift rather than partially rewrite IL.
        internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> source,
            MethodInfo nativeDismiss,MethodInfo guardedDismiss)
        {
            var code=source.ToList();
            int[] hits=Enumerable.Range(0,code.Count).Where(i=>code[i].opcode==OpCodes.Call&&Equals(code[i].operand,nativeDismiss)).ToArray();
            if(hits.Length!=1)throw new InvalidOperationException("Expected one native map loading dismissal call.");
            int at=hits[0];
            if(at<4||at+3>=code.Count||code[at-4].opcode!=OpCodes.Ldarg_0
                ||code[at-3].opcode!=OpCodes.Ldfld||code[at-2].opcode!=OpCodes.Ldc_I4_3
                ||(code[at-1].opcode!=OpCodes.Bne_Un&&code[at-1].opcode!=OpCodes.Bne_Un_S)
                ||code[at+1].opcode!=OpCodes.Ldarg_0||code[at+2].opcode!=OpCodes.Ldc_I4_0
                ||code[at+3].opcode!=OpCodes.Stfld
                ||!(code[at-3].operand is FieldInfo)||((FieldInfo)code[at-3].operand).Name!="_sceneReadyFrameCounter"
                ||!Equals(code[at-3].operand,code[at+3].operand)||code[at].blocks.Count!=0)
                throw new InvalidOperationException("Native map loading dismissal IL contract changed.");
            var receiver=new CodeInstruction(OpCodes.Ldarg_0);
            receiver.labels.AddRange(code[at].labels);
            code[at]=receiver;
            code.Insert(at+1,new CodeInstruction(OpCodes.Call,guardedDismiss));
            return code;
        }
    }
}
