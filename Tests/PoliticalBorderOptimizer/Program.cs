using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using AgesOfCalradia.PoliticalBorderOptimizer;

internal static class Program
{
    private static int _checks;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    private static int Main()
    {
        try
        {
            LoadingGateTests();
            DismissalIlTests();
            PublishedExactProbeMemoization<int> probes =
                new PublishedExactProbeMemoization<int>();
            int value;
            bool success;

            Check(!PublishedExactProbeMemoization<int>.ShouldUsePreparedProbe(true, false)
                && !PublishedExactProbeMemoization<int>.ShouldUsePreparedProbe(true, true)
                && !PublishedExactProbeMemoization<int>.ShouldUsePreparedProbe(false, true)
                && PublishedExactProbeMemoization<int>.ShouldUsePreparedProbe(false, false),
                "Only a non-published exact-cache miss may use prepared interpolation.");
            Check(!probes.TryGet(10f, 20f, out value, out success),
                "An uncached exact coordinate must fall through to native probing.");
            probes.Record(10f, 20f, 17, true);
            Check(probes.TryGet(10f, 20f, out value, out success)
                && value == 17 && success,
                "A successful native result must round-trip unchanged.");
            probes.Record(11f, 20f, 91, false);
            Check(probes.TryGet(11f, 20f, out value, out success)
                && value == 91 && !success,
                "An invalid native result and its out value must round-trip unchanged.");
            Check(!probes.TryGet(10.0001f, 20f, out value, out success),
                "Near coordinates must not be interpolated or quantized into a published-layout cache hit.");
            float positiveZero = new PublishedExactProbeFloatBits { Bits = 0 }.Value;
            float negativeZero = new PublishedExactProbeFloatBits { Bits = unchecked((int)0x80000000) }.Value;
            probes.Record(positiveZero, 4f, 31, true);
            Check(!probes.TryGet(negativeZero, 4f, out value, out success),
                "Signed zero coordinates must retain their exact native identity.");
            float firstNan = new PublishedExactProbeFloatBits { Bits = unchecked((int)0x7FC00001) }.Value;
            float secondNan = new PublishedExactProbeFloatBits { Bits = unchecked((int)0x7FC00002) }.Value;
            probes.Record(firstNan, 8f, 44, true);
            Check(!probes.TryGet(secondNan, 8f, out value, out success),
                "Distinct NaN payload coordinates must not share a native result.");
            for (int index = 0; index < PublishedExactProbeMemoization<int>.MaximumEntries; index++)
                probes.Record(index + 100f, 30f, index, true);
            Check(!probes.Record(999999f, 30f, 7, true)
                && !probes.TryGet(999999f, 30f, out value, out success),
                "A full memo must fall through to native probing instead of evicting or approximating.");
            probes.Clear();
            Check(!probes.TryGet(10f, 20f, out value, out success),
                "Scene invalidation must discard every exact probe result.");
            Console.WriteLine("PASS " + _checks + " published-layout probe and loading dismissal assertions");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void LoadingGateTests()
    {
        var gate=new PublishedLoadingGate();var behavior=new object();var screen=new object();int calls=0;
        gate.Begin(behavior);
        Check(!gate.TryDismiss(screen,null,0,()=>calls++)&&ReferenceEquals(gate.Screen,screen),"First dismissal cannot bypass an unbound pending load.");
        gate.Bind(screen,behavior);
        Check(!gate.TryDismiss(screen,null,0,()=>calls++)&&calls==0,"Pending publication retains loading.");
        gate.Complete(new object());Check(gate.State==PublishedLoadingState.Pending,"Unrelated completion cannot unlock loading.");
        gate.Complete(behavior);
        Check(!gate.TryDismiss(screen,new object(),1,()=>calls++)&&calls==0,"Stale completed behavior cannot unlock loading.");
        Check(gate.TryDismiss(screen,behavior,2,()=>calls++)&&calls==1,"Verified same-behavior completion dismisses exactly once.");
        var next=new object();gate.Begin(next);gate.Bind(screen,next);
        Check(!gate.TryDismiss(screen,behavior,3,()=>calls++),"New campaign does not inherit prior readiness.");
        Check(gate.TryDismiss(new object(),null,4,()=>calls++),"Unrelated map is never blocked.");
        gate.Fail(next,"fixture failure");Check(gate.TryDismiss(screen,null,5,()=>calls++)&&gate.Failure=="fixture failure","Failure is terminal and explicitly reported instead of deadlocking.");
        gate.Begin(new object());gate.Bind(screen,gate.Behavior);gate.Cancel(screen);
        Check(gate.State==PublishedLoadingState.Idle&&gate.TryDismiss(screen,null,6,()=>calls++),"Closing the map cancels its loading hold.");
        gate.Begin(new object());gate.Bind(screen,gate.Behavior);
        Check(!gate.TryDismiss(screen,null,10,()=>calls++)&&gate.TryDismiss(screen,null,130,()=>calls++)&&gate.State==PublishedLoadingState.Failed,"Readiness timeout is bounded and marked failed.");
    }
    private sealed class MapFixture {public int _sceneReadyFrameCounter=3;}
    private static PublishedLoadingGate _gate;
    private static object _completed;
    private static int _dismissals,_ticks;
    private static void NativeDismiss(){_dismissals++;}
    private static void MapTick(){_ticks++;}
    private static void GuardedDismiss(object screen){_gate.TryDismiss(screen,_completed,0,NativeDismiss);}
    private static void DismissalIlTests()
    {
        var method=new DynamicMethod("MapReadinessFixture",typeof(void),new[]{typeof(MapFixture)},typeof(Program),true);
        ILGenerator emit=method.GetILGenerator();Label skip=emit.DefineLabel();
        var field=typeof(MapFixture).GetField("_sceneReadyFrameCounter");
        var native=typeof(Program).GetMethod(nameof(NativeDismiss),BindingFlags.Static|BindingFlags.NonPublic);
        var guard=typeof(Program).GetMethod(nameof(GuardedDismiss),BindingFlags.Static|BindingFlags.NonPublic);
        var code=new List<CodeInstruction>{new CodeInstruction(OpCodes.Ldarg_0),new CodeInstruction(OpCodes.Ldfld,field),new CodeInstruction(OpCodes.Ldc_I4_3),new CodeInstruction(OpCodes.Bne_Un_S,skip),new CodeInstruction(OpCodes.Call,native),new CodeInstruction(OpCodes.Ldarg_0),new CodeInstruction(OpCodes.Ldc_I4_0),new CodeInstruction(OpCodes.Stfld,field),new CodeInstruction(OpCodes.Nop),new CodeInstruction(OpCodes.Call,typeof(Program).GetMethod(nameof(MapTick),BindingFlags.NonPublic|BindingFlags.Static)),new CodeInstruction(OpCodes.Ret)};
        code[8].labels.Add(skip);
        foreach(var instruction in PublishedLoadingDismissalPatch.Rewrite(code,native,guard))
        {
            foreach(Label label in instruction.labels)emit.MarkLabel(label);
            if(instruction.operand is Label)emit.Emit(instruction.opcode,(Label)instruction.operand);
            else if(instruction.operand is FieldInfo)emit.Emit(instruction.opcode,(FieldInfo)instruction.operand);
            else if(instruction.operand is MethodInfo)emit.Emit(instruction.opcode,(MethodInfo)instruction.operand);
            else emit.Emit(instruction.opcode);
        }
        var run=(Action<MapFixture>)method.CreateDelegate(typeof(Action<MapFixture>));var map=new MapFixture();var behavior=new object();
        _gate=new PublishedLoadingGate();_gate.Begin(behavior);_gate.Bind(map,behavior);_completed=null;_ticks=0;_dismissals=0;
        run(map);Check(_ticks==1&&_dismissals==0&&map._sceneReadyFrameCounter==0,"Patched native map loop and counter continue while dismissal is held.");
        map._sceneReadyFrameCounter=3;_gate.Complete(behavior);_completed=behavior;run(map);
        Check(_ticks==2&&_dismissals==1,"Patched native dismissal occurs once after exact readiness.");
        foreach(int mutation in new[]{0,1,2})
        {
            var bad=code.Select(c=>new CodeInstruction(c)).ToList();
            if(mutation==0)bad.RemoveAt(4);else if(mutation==1)bad.Add(new CodeInstruction(OpCodes.Call,native));else bad[2]=new CodeInstruction(OpCodes.Ldc_I4_2);
            bool rejected=false;try{PublishedLoadingDismissalPatch.Rewrite(bad,native,guard).ToList();}catch(InvalidOperationException){rejected=true;}
            Check(rejected,"Missing, duplicate or changed native dismissal IL is rejected.");
        }
    }
}
