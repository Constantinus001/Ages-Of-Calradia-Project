using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;

namespace AgesOfCalradia.Approved560CalendarFixes
{
    // Native 1.4.8 ProduceAnOutputToTown: cap the existing payment local to cash
    // available BEFORE both native gold changes. Keep native item choice, quote,
    // roster writes, RNG, events and effectCapital branching unchanged.
    // IL mismatch throws during module preflight; the existing load boundary logs
    // and rolls back this sidecar. No save state. Other transpilers can conflict;
    // exact signature and shared-local pattern are verified before installation.
    [HarmonyPatch]
    internal static class WorkshopPaymentConservationFix
    {
        internal static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(WorkshopsCampaignBehavior), "ProduceAnOutputToTown",
                new[] { typeof(EquipmentElement), typeof(Workshop), typeof(bool) })
                ?? throw new MissingMethodException("Native workshop output payment target changed");
        }
        internal static int BoundPayment(int requested, int available)
        {
            // Preserve unexpected negative native values rather than changing an
            // unrelated refund contract. Native positive payments are capped.
            return requested <= 0 ? requested : Math.Min(requested, Math.Max(0, available));
        }
        private static int BoundToTown(int requested, EquipmentElement item, Workshop workshop)
        {
            requested = WorkshopApprovalQuoteFix.ResolvePayment(requested, item, workshop);
            return BoundPayment(requested, workshop.Settlement.Town.Gold);
        }
        internal static void ValidateTargets()
        {
            Transpiler(PatchProcessor.GetOriginalInstructions(TargetMethod())).ToArray();
        }
        private static int LocalIndex(CodeInstruction instruction)
        {
            if (instruction.opcode == OpCodes.Stloc_0 || instruction.opcode == OpCodes.Ldloc_0) return 0;
            if (instruction.opcode == OpCodes.Stloc_1 || instruction.opcode == OpCodes.Ldloc_1) return 1;
            if (instruction.opcode == OpCodes.Stloc_2 || instruction.opcode == OpCodes.Ldloc_2) return 2;
            if (instruction.opcode == OpCodes.Stloc_3 || instruction.opcode == OpCodes.Ldloc_3) return 3;
            var local = instruction.operand as LocalVariableInfo;
            return local != null ? local.LocalIndex : Convert.ToInt32(instruction.operand);
        }
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.Select(x => new CodeInstruction(x)).ToList();
            var minimum = AccessTools.Method(typeof(TaleWorlds.Library.MathF), "Min", new[] { typeof(int), typeof(int) });
            var workshopGold = AccessTools.Method(typeof(Workshop), "ChangeGold", new[] { typeof(int) });
            var townGold = AccessTools.Method(typeof(SettlementComponent), "ChangeGold", new[] { typeof(int) });
            var cap = AccessTools.Method(typeof(WorkshopPaymentConservationFix), "BoundToTown");
            int[] matches = Enumerable.Range(0, code.Count).Where(i => code[i].Calls(minimum)).ToArray();
            if (matches.Length != 1 || code.Any(x => x.Calls(cap))
                || code.Count(x => x.Calls(workshopGold)) != 1 || code.Count(x => x.Calls(townGold)) != 1)
                throw new InvalidOperationException("Workshop payment IL contract changed or was already capped");
            int index = matches[0];
            // The bounded value must feed the same local used by both mutations.
            if (index + 8 >= code.Count || !code[index + 1].IsStloc()
                || !code[index + 3].IsLdloc() || !code[index + 4].Calls(workshopGold)
                || !code[index + 6].IsLdloc() || code[index + 7].opcode != OpCodes.Neg
                || !code[index + 8].Calls(townGold)
                || LocalIndex(code[index + 1]) != LocalIndex(code[index + 3])
                || LocalIndex(code[index + 1]) != LocalIndex(code[index + 6])
                || code.Skip(index + 1).Take(8).Any(x => x.labels.Count != 0 || x.blocks.Count != 0))
                throw new InvalidOperationException("Workshop gold changes no longer share the audited payment local");
            code.InsertRange(index + 1, new[] { new CodeInstruction(OpCodes.Ldarg_1), new CodeInstruction(OpCodes.Ldarg_2), new CodeInstruction(OpCodes.Call, cap) });
            return code;
        }
    }
}
