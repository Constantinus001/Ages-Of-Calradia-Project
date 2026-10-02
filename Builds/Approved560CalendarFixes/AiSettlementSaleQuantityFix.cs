using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace AgesOfCalradia.Approved560CalendarFixes
{
    // Native 1.4.8 SellItemsAction.ApplyInternal: stop the per-item loop BEFORE
    // moving an unaffordable item. Preserve each native quote, earlier transfers,
    // payment and events. Only funded AI lord-party -> town sales are affected;
    // player, caravan, initialization, purchases and village paths stay native.
    // Transpiler because a prefix cannot know changing per-item prices without
    // reevaluating/mutating the market. Exact loop shape fails closed in preflight
    // via the sidecar rollback/log boundary. No persistence or reservation state.
    // Other transpilers/price callbacks mutating cash remain compatibility risks.
    // Verify-AiSettlementSale.ps1 checks native goods/cash, dynamic prices, zero
    // sales, native bypasses, and changed/duplicate instruction rejection.
    [HarmonyPatch]
    internal static class AiSettlementSaleQuantityFix
    {
        internal static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(SellItemsAction), "ApplyInternal",
                new[] { typeof(PartyBase), typeof(PartyBase), typeof(ItemRosterElement), typeof(int), typeof(Settlement) })
                ?? throw new MissingMethodException("Native sale quantity target changed");
        }
        internal static bool Affordable(int total, int price, int available)
        {
            return price <= 0 || (total >= 0 && total <= Math.Max(0, available)
                && price <= Math.Max(0, available) - total);
        }
        private static bool CanTransfer(PartyBase seller, PartyBase buyer, int total, int price)
        {
            var mobile = seller?.MobileParty;
            var town = buyer?.Settlement?.Town;
            if (Campaign.Current == null || !Campaign.Current.GameStarted || town == null
                || mobile == null || mobile == MobileParty.MainParty || !mobile.IsLordParty
                || mobile.LeaderHero == null) return true;
            return Affordable(total, price, town.Gold);
        }
        internal static void ValidateTargets()
        {
            Transpiler(PatchProcessor.GetOriginalInstructions(TargetMethod()),
                new DynamicMethod("ValidateSale", typeof(void), Type.EmptyTypes).GetILGenerator()).ToArray();
        }
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var code = instructions.Select(x => new CodeInstruction(x)).ToList();
            var quote = AccessTools.Method(typeof(SettlementComponent), "GetItemPrice",
                new[] { typeof(EquipmentElement), typeof(MobileParty), typeof(bool) });
            var guard = AccessTools.Method(typeof(AiSettlementSaleQuantityFix), "CanTransfer");
            var matches = Enumerable.Range(0, code.Count).Where(i => code[i].Calls(quote)).ToArray();
            if (matches.Length != 1 || code.Any(x => x.Calls(guard)))
                throw new InvalidOperationException("Sale quote pattern changed or already guarded");
            int q = matches[0];
            if (q < 5 || q + 35 >= code.Count || !code[q+1].IsStloc()
                || code[q+2].opcode != OpCodes.Ldloc_3 || !code[q+3].IsLdloc()
                || !Equals(code[q+1].operand, code[q+3].operand)
                || code[q+4].opcode != OpCodes.Add || code[q+5].opcode != OpCodes.Stloc_3
                || code.Skip(q+1).Take(5).Any(x => x.labels.Count != 0 || x.blocks.Count != 0))
                throw new InvalidOperationException("Sale quote/total locals changed");
            var loops = Enumerable.Range(q+6, 30).Where(i => code[i].opcode == OpCodes.Blt || code[i].opcode == OpCodes.Blt_S).ToArray();
            if (loops.Length != 1) throw new InvalidOperationException("Sale loop exit changed");
            int loop = loops[0];
            if (code[loop-1].opcode != OpCodes.Ldarg_3 || !code[q-5].labels.Contains((Label)code[loop].operand)
                || code[loop+1].opcode != OpCodes.Ldarg_1 || code.Skip(q-5).Take(loop-q+7).Any(x => x.blocks.Count != 0))
                throw new InvalidOperationException("Sale loop branch contract changed");
            var exit = generator.DefineLabel();
            code[loop+1].labels.Add(exit);
            code.InsertRange(q+2, new[] {new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldloc_3), new CodeInstruction(code[q+3]), new CodeInstruction(OpCodes.Call, guard),
                new CodeInstruction(OpCodes.Brfalse, exit)});
            return code;
        }
    }
}
