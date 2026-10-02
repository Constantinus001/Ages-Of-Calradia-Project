using System;
using System.Reflection;
using System.Linq;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;

namespace AgesOfCalradia.WorkshopProcurement
{
    internal static class ProcurementPatches
    {
        internal sealed class Context
        {
            internal Workshop Shop;
            internal WorkshopType.Production Recipe;
            internal ProcurementOrder Order;
            internal ProcurementBehavior Behavior;
            internal int BeforeBatches;
            internal int BeforeCapital;
            internal int BeforeTownGold;
            internal int BatchCost;
        }
        [ThreadStatic] internal static Context Active;
        internal static MethodInfo Method(string name, params Type[] args)
        {
            return AccessTools.Method(typeof(WorkshopsCampaignBehavior), name, args)
                ?? throw new MissingMethodException("Procurement native target changed: " + name);
        }
        internal static void Validate()
        {
            if (Cycle.TargetMethod().ReturnType != typeof(bool) || Inputs.TargetMethod().ReturnType != typeof(bool)
                || Consume.TargetMethod().ReturnType != typeof(void))
                throw new InvalidOperationException("Procurement native return type changed");
            var code = PatchProcessor.GetOriginalInstructions(Cycle.TargetMethod()).ToArray();
            if (code.Count(i => i.Calls(Inputs.TargetMethod())) != 1 || code.Count(i => i.Calls(Consume.TargetMethod())) != 1)
                throw new InvalidOperationException("Native workshop input call topology changed");
            PrepaidCapital.Transpiler(PatchProcessor.GetOriginalInstructions(PrepaidCapital.TargetMethod())).ToArray();
            WineOperatingMargin.Transpiler(PatchProcessor.GetOriginalInstructions(WineOperatingMargin.TargetMethod())).ToArray();
            var output = ProcurementPatches.Method("ProduceAnOutputToTown", typeof(EquipmentElement), typeof(Workshop), typeof(bool));
            var owners = Harmony.GetPatchInfo(output);
            if (owners == null || !owners.Owners.Contains("AgesOfCalradia.Approved560CalendarFixes.560F1B51"))
                throw new InvalidOperationException("Required cash-conserving calendar sidecar is not loaded");
        }
    }
    // Only the native notable approval gate's capital read. Prepaid stock still
    // contributes full landed cost to profitability, but is not funded twice.
    // Native town-cash, output and margin checks are left byte-for-byte intact.
    [HarmonyPatch]
    internal static class PrepaidCapital
    {
        internal static MethodInfo TargetMethod() { return ProcurementPatches.Method("CanNotableWorkshopProduceThisCycle",
            typeof(WorkshopType.Production), typeof(Workshop), typeof(int), typeof(int), typeof(bool)); }
        private static int Available(Workshop shop)
        {
            var c = ProcurementPatches.Active;
            return c != null && c.Shop == shop ? (int)Math.Min(int.MaxValue, (long)shop.Capital + c.BatchCost) : shop.Capital;
        }
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.Select(i => new CodeInstruction(i)).ToList();
            var getter = AccessTools.PropertyGetter(typeof(Workshop), "Capital");
            var replacement = AccessTools.Method(typeof(PrepaidCapital), "Available");
            if (code.Count(i => i.Calls(getter)) != 1 || code.Any(i => i.Calls(replacement)))
                throw new InvalidOperationException("Notable workshop capital gate changed or already patched");
            var call = code.Single(i => i.Calls(getter));
            call.opcode = OpCodes.Call;
            call.operand = replacement;
            return code;
        }
    }
    // 1.4.8 notable cycle only. Scoped context; nested/player/init calls retain
    // native behavior. Finalizer restores context and persists any native fault.
    [HarmonyPatch]
    internal static class Cycle
    {
        internal static MethodInfo TargetMethod() { return ProcurementPatches.Method("TickOneProductionCycleForNotableWorkshop", typeof(WorkshopType.Production), typeof(Workshop), typeof(bool)); }
        [HarmonyPriority(Priority.First)]
        internal static void Prefix(WorkshopType.Production __0, Workshop __1, bool __2, out ProcurementPatches.Context __state)
        {
            __state = ProcurementPatches.Active;
            ProcurementPatches.Active = null;
            var behavior = ProcurementBehavior.Current;
            if (behavior == null || !__2 || !Campaign.Current.GameStarted) return;
            ProcurementOrder order;
            try { order = behavior.Ready(__1, __0); }
            catch (Exception ex) { behavior.Fault(ex); return; } // Native object-resolution boundary, no mutation yet.
            if (order != null) ProcurementPatches.Active = new ProcurementPatches.Context { Shop = __1, Recipe = __0,
                Order = order, Behavior = behavior, BeforeBatches = order.Quantity, BeforeCapital = __1.Capital,
                BeforeTownGold = __1.Settlement.Town.Gold, BatchCost = order.CostOf(1) };
        }
        internal static void Postfix(bool __result)
        {
            var c = ProcurementPatches.Active;
            if (c == null) return;
            int expected = c.BeforeBatches - (__result ? 1 : 0);
            if (c.Order.Quantity != expected || c.Order.Lines.Exists(l => l.Remaining != c.Order.Quantity * l.UnitsPerBatch))
                c.Behavior.Fault(new InvalidOperationException("Native cycle/private input reconciliation failed"));
            int receipts = c.Shop.Capital - c.BeforeCapital;
            int townDebit = c.BeforeTownGold - c.Shop.Settlement.Town.Gold;
            ProcurementLog.Order("CYCLE", c.Order, "accepted=" + __result + " receipts=" + receipts
                + " townDebit=" + townDebit + " recognizedCost=" + (__result ? c.BatchCost : 0)
                + " contributionBeforeDailyExpense=" + (receipts - (__result ? c.BatchCost : 0)));
            if (receipts != townDebit) c.Behavior.Fault(new InvalidOperationException("Private-stock output payment residual"));
        }
        internal static void Finalizer(Exception __exception, ProcurementPatches.Context __state)
        {
            if (__exception != null && ProcurementPatches.Active != null) ProcurementPatches.Active.Behavior.Fault(__exception);
            ProcurementPatches.Active = __state;
        }
    }
    // Preserve the native output and profitability gates. Recognized landed cost
    // is used for approval, but prepaid private stock is never charged twice.
    [HarmonyPatch]
    internal static class Inputs
    {
        internal static MethodInfo TargetMethod() { return ProcurementPatches.Method("DetermineItemRosterHasSufficientInputs", typeof(WorkshopType.Production), typeof(ItemRoster), typeof(Town), typeof(int).MakeByRefType()); }
        internal static bool Prefix(WorkshopType.Production __0, ItemRoster __1, Town __2, ref int __3, ref bool __result)
        {
            var c = ProcurementPatches.Active;
            if (c == null || !ProcurementBehavior.SameRecipe(c.Recipe, __0) || c.Shop.Settlement.Town != __2
                || !ReferenceEquals(__1, __2.Owner.ItemRoster)) return true;
            __3 = c.Order.CostOf(1);
            __result = true;
            return false;
        }
    }
    [HarmonyPatch]
    internal static class Consume
    {
        internal static MethodInfo TargetMethod() { return ProcurementPatches.Method("ConsumeInputFromTownMarket", typeof(ItemCategory), typeof(int), typeof(Town), typeof(Workshop), typeof(bool)); }
        internal static bool Prefix(ItemCategory __0, int __1, Town __2, Workshop __3, bool __4)
        {
            var c = ProcurementPatches.Active;
            if (c == null || c.Shop != __3 || c.Shop.Settlement.Town != __2 || !__4) return true;
            c.Behavior.Consume(__3, c.Order, __0, __1);
            return false;
        }
    }
}
