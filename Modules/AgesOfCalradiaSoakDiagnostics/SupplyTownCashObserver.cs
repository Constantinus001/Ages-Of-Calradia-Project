using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Settlements;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Bannerlord 1.4.8 ItemConsumptionBehavior.UpdateTownGold(Town). Observes
    // the actual virtual GetTownGoldChange result using DUP + void tap, not a
    // second model evaluation. Exact single-call IL is required; changed targets
    // block capture, never gameplay. Prefix/finalizer preserve native exceptions.
    // Wallet changes retain net-of-nested accounting; this boundary is context,
    // never another additive cash entry. Verify-CausalDiagnostics covers it.
    internal static class SupplyTownCashObserver
    {
        [ThreadStatic] private static Call _current;
        private static long _next;
        internal sealed class Call
        {
            internal Call Previous;
            internal Town Town;
            internal long Id;
            internal int Before, ModelResult, ModelCalls;
        }
        internal static string Context { get { return "; townCashOperation=" + (_current == null ? "none" : _current.Id.ToString()); } }
        internal static MethodInfo Target()
        { return SupplyChainObserver.Require(typeof(ItemConsumptionBehavior), "UpdateTownGold", typeof(Town)); }
        internal static void Reset() { _current = null; _next = 0; }
        internal static void Install(Harmony harmony)
        {
            Reset();
            Tap(PatchProcessor.GetOriginalInstructions(Target())).ToArray();
            harmony.Patch(Target(), new HarmonyMethod(typeof(SupplyTownCashObserver), "Before"), null,
                new HarmonyMethod(typeof(SupplyTownCashObserver), "Tap"),
                new HarmonyMethod(typeof(SupplyTownCashObserver), "After") { priority = Priority.Last });
        }
        internal static IEnumerable<CodeInstruction> Tap(IEnumerable<CodeInstruction> instructions)
        {
            return SupplyValueTaps.Tap(instructions,
                AccessTools.Method(typeof(SettlementEconomyModel), "GetTownGoldChange", new[] { typeof(Town) }),
                AccessTools.Method(typeof(SupplyTownCashObserver), "ModelResult"));
        }
        private static void ModelResult(int value)
        {
            if (_current == null || !SupplyCapture.Active) return;
            _current.ModelResult = value; _current.ModelCalls++;
        }
        private static void Before(Town __0, out Call __state)
        {
            __state = null;
            if (!SupplyCapture.Active) return;
            try
            {
                __state = new Call { Previous = _current, Town = __0, Before = __0.Gold, Id = ++_next };
                _current = __state;
                SupplyCapture.Write("TOWN_CASH_BEGIN", 0, 0, __0.StringId, "UpdateTownGold", __0.Gold, __0.Gold,
                    Context + "; prosperity=" + SupplyCapture.N(__0.Prosperity));
            }
            catch (Exception ex) { SupplyCapture.Fail("town cash begin", ex); }
        }
        private static void After(Call __state, bool __runOriginal, Exception __exception)
        {
            if (__state == null) return;
            try
            {
                if (!SupplyCapture.Active) return;
                SupplyCapture.Write("TOWN_CASH_END", 0, 0, __state.Town.StringId, "UpdateTownGold", __state.Before, __state.Town.Gold,
                    Context + "; originalRan=" + __runOriginal + "; modelCalls=" + __state.ModelCalls
                    + "; modelResult=" + (__state.ModelCalls == 1 ? __state.ModelResult.ToString() : "unavailable")
                    + "; error=" + (__exception == null ? "none" : __exception.GetType().FullName)
                    + "; gross_context_not_additive_cash_flow; attribution=observed_call_boundary_not_exclusive_cause");
                if (__exception != null) SupplyCapture.Fail("native town cash operation", __exception);
            }
            catch (Exception ex) { SupplyCapture.Fail("town cash end", ex); }
            finally { _current = __state.Previous; }
        }
    }
}
