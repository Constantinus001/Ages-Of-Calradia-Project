using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Version-sensitive observer transpilers. Insert DUP + void tap after exact
    // native getters: originals and their results are untouched, each getter
    // executes exactly once. No RNG/model query in taps. Unknown/duplicate call
    // patterns reject installation. Verify-SupplyCapture tests IL and execution.
    internal static class SupplyValueTaps
    {
        internal static void Validate()
        {
            Dispatch(PatchProcessor.GetOriginalInstructions(SupplyChainObserver.Require(typeof(VillagerCampaignBehavior),
                "ThinkAboutSendingItemToTown", typeof(Village)))).ToArray();
            Load(PatchProcessor.GetOriginalInstructions(SupplyChainObserver.Require(typeof(VillagerCampaignBehavior),
                "MoveItemsToVillagerParty", typeof(Village), typeof(MobileParty)))).ToArray();
        }
        internal static IEnumerable<CodeInstruction> Dispatch(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.Select(x => new CodeInstruction(x)).ToList();
            MethodInfo getter = AccessTools.PropertyGetter(typeof(MBRandom), "RandomFloat");
            int[] indices = Enumerable.Range(0, code.Count).Where(i => code[i].Calls(getter)).ToArray();
            if (indices.Length != 1 || indices[0] + 1 >= code.Count || code[indices[0] + 1].opcode != OpCodes.Ldc_R4
                || !Equals(code[indices[0] + 1].operand, 0.15f))
                throw new InvalidOperationException("Supply observer: native dispatch roll/threshold pattern changed");
            return Tap(code, getter, AccessTools.Method(typeof(SupplyChainObserver), "Roll"));
        }
        internal static IEnumerable<CodeInstruction> Load(IEnumerable<CodeInstruction> instructions)
        {
            var code = Tap(instructions, AccessTools.PropertyGetter(typeof(MobileParty), "InventoryCapacity"),
                AccessTools.Method(typeof(SupplyChainObserver), "LoadCapacity"));
            return Tap(code, AccessTools.PropertyGetter(typeof(MobileParty), "TotalWeightCarried"),
                AccessTools.Method(typeof(SupplyChainObserver), "LoadWeight"));
        }
        internal static IEnumerable<CodeInstruction> Tap(IEnumerable<CodeInstruction> instructions, MethodInfo getter, MethodInfo observer)
        {
            var code = instructions.Select(x => new CodeInstruction(x)).ToList();
            if (getter == null || observer == null || observer.ReturnType != typeof(void)
                || observer.GetParameters().Length != 1 || observer.GetParameters()[0].ParameterType != getter.ReturnType)
                throw new InvalidOperationException("Supply observer: invalid value tap signature");
            int[] indices = Enumerable.Range(0, code.Count).Where(i => code[i].Calls(getter)).ToArray();
            if (indices.Length != 1 || code.Any(x => x.Calls(observer)))
                throw new InvalidOperationException("Supply observer: expected one untapped getter");
            int at = indices[0];
            if (code[at].blocks.Count != 0) throw new InvalidOperationException("Supply observer: getter exception boundary changed");
            code.InsertRange(at + 1, new[] { new CodeInstruction(OpCodes.Dup), new CodeInstruction(OpCodes.Call, observer) });
            return code;
        }
    }
}
