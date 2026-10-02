using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;

namespace AgesOfCalradia.Approved560CalendarFixes
{
    // Bannerlord 1.4.8: only AI/notable, capital-affecting production approval.
    // Replace ONE existing quote call, not item selection/RNG or another model
    // evaluation. Use the output-payment direction and per-item 1000 cap.
    // Player warehouse/estimates and world initialization remain native.
    // Native signature/IL mismatch aborts module preflight and existing loader
    // rolls back sidecar hooks. No save state. Verify-WorkshopApprovalQuote.ps1.
    [HarmonyPatch]
    internal static class WorkshopApprovalQuoteFix
    {
        [ThreadStatic] internal static bool CapitalCycle;
        [ThreadStatic] internal static Batch CurrentBatch;
        [ThreadStatic] private static Batch _collecting;
        internal sealed class Batch
        {
            internal Workshop Shop;
            internal readonly List<EquipmentElement> Items = new List<EquipmentElement>();
            internal readonly List<int> Prices = new List<int>();
            internal int Paid;
            internal bool Ready;
            internal bool Invalid;
            internal bool Collecting;
            internal int Resolve(EquipmentElement item, int nativeQuote)
            {
                if (!Ready || Invalid) return nativeQuote;
                if (Paid >= Items.Count || !Items[Paid].Equals(item))
                {
                    Invalid = true;
                    System.Diagnostics.Trace.WriteLine("AOC workshop batch output changed; using cash-conserving native payment for remaining outputs.");
                    return nativeQuote;
                }
                return Prices[Paid++];
            }
        }
        internal sealed class CollectionState
        {
            internal Batch Previous;
            internal Batch Selected;
        }
        // Collection is scoped to this exact workshop invocation. A player or
        // another shop called reentrantly cannot acquire the AI batch's prices.
        internal static void Prefix(Workshop __1, out CollectionState __state)
        {
            __state = new CollectionState { Previous = _collecting };
            _collecting = CapitalCycle && CurrentBatch != null && ReferenceEquals(CurrentBatch.Shop, __1)
                && !CurrentBatch.Ready && !CurrentBatch.Collecting && CurrentBatch.Items.Count == 0 ? CurrentBatch : null;
            __state.Selected = _collecting;
            if (_collecting != null) _collecting.Collecting = true;
        }
        internal static void Postfix(List<EquipmentElement> __result, CollectionState __state)
        {
            var batch = __state?.Selected;
            if (batch == null) return;
            batch.Ready = __result != null && batch.Items.SequenceEqual(__result) && batch.Prices.Count == __result.Count;
        }
        internal static void Finalizer(CollectionState __state)
        {
            if (__state != null)
            {
                if (__state.Selected != null) __state.Selected.Collecting = false;
                _collecting = __state.Previous;
            }
        }
        internal static int ResolvePayment(int nativeQuote, EquipmentElement item, Workshop shop)
        {
            return CurrentBatch != null && ReferenceEquals(CurrentBatch.Shop, shop)
                ? CurrentBatch.Resolve(item, nativeQuote) : nativeQuote;
        }
        internal static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(WorkshopsCampaignBehavior), "GetItemsToProduce",
                new[] { typeof(WorkshopType.Production), typeof(Workshop), typeof(int).MakeByRefType() })
                ?? throw new MissingMethodException("Workshop approval quote target changed");
        }
        internal static bool UsePaymentQuote(bool capitalCycle, bool gameStarted) { return capitalCycle && gameStarted; }
        internal static int NormalizeQuote(int quote, bool usePayment) { return usePayment ? Math.Min(1000, quote) : quote; }
        private static int Quote(SettlementComponent town, EquipmentElement item, MobileParty party, bool selling)
        {
            bool payment = UsePaymentQuote(_collecting != null, Campaign.Current.GameStarted);
            int price = NormalizeQuote(town.GetItemPrice(item, party, payment ? false : selling), payment);
            if (payment)
            {
                _collecting.Items.Add(item);
                _collecting.Prices.Add(price);
            }
            return price;
        }
        internal static void ValidateTargets()
        {
            WorkshopApprovalCycle.TargetMethod();
            Transpiler(PatchProcessor.GetOriginalInstructions(TargetMethod())).ToArray();
        }
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.Select(x => new CodeInstruction(x)).ToList();
            var original = AccessTools.Method(typeof(SettlementComponent), "GetItemPrice", new[] { typeof(EquipmentElement), typeof(MobileParty), typeof(bool) });
            var replacement = AccessTools.Method(typeof(WorkshopApprovalQuoteFix), "Quote");
            if (code.Count(x => x.Calls(original)) != 1 || code.Any(x => x.Calls(replacement)))
                throw new InvalidOperationException("Workshop approval price call changed or already replaced: "
                    + string.Join(" | ", code.Where(x => x.operand is MethodInfo && ((MethodInfo)x.operand).Name == "GetItemPrice").Select(x => ((MethodInfo)x.operand).DeclaringType.FullName + ":" + x.operand)));
            var call = code.Single(x => x.Calls(original));
            call.opcode = System.Reflection.Emit.OpCodes.Call;
            call.operand = replacement;
            return code;
        }
    }
    [HarmonyPatch]
    internal static class WorkshopApprovalCycle
    {
        internal sealed class State
        {
            internal bool PreviousCapital;
            internal WorkshopApprovalQuoteFix.Batch PreviousBatch;
        }
        internal static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(WorkshopsCampaignBehavior), "TickOneProductionCycleForNotableWorkshop",
                new[] { typeof(WorkshopType.Production), typeof(Workshop), typeof(bool) })
                ?? throw new MissingMethodException("Workshop notable cycle target changed");
        }
        internal static void Prefix(object __1, bool __2, out State __state)
        {
            __state = new State { PreviousCapital = WorkshopApprovalQuoteFix.CapitalCycle,
                PreviousBatch = WorkshopApprovalQuoteFix.CurrentBatch };
            WorkshopApprovalQuoteFix.CapitalCycle = __2;
            WorkshopApprovalQuoteFix.CurrentBatch = __2 ? new WorkshopApprovalQuoteFix.Batch { Shop = __1 as Workshop } : null;
        }
        internal static void Finalizer(State __state)
        {
            if (__state == null) return;
            WorkshopApprovalQuoteFix.CapitalCycle = __state.PreviousCapital;
            WorkshopApprovalQuoteFix.CurrentBatch = __state.PreviousBatch;
        }
    }
}
