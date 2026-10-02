using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Optional AOC logistics ABI, not a hard dependency. Observe market and
    // reserve endpoints without GetReserve (which initializes gameplay state).
    // Exact signatures preflight; a present incompatible module fails capture.
    // Missing module is coverage=absent. Prefix/finalizers never suppress original
    // exceptions or modify arguments/results. No __args binding on ref methods.
    // Wider reserve boundaries catch inlined service calls; nested deltas are
    // context, NOT additive cash/stock entries. Tests: Verify-DiagnosticExtensions.
    internal static class LogisticsLifecycleObserver
    {
        internal sealed class Call
        {
            internal Call Previous;
            internal string Id, Name, Owner, Before;
            internal object Reserve;
            internal MobileParty[] Parties;
            internal Settlement Settlement;
            internal ItemObject Item;
            internal Hero Payer;
        }
        [ThreadStatic] private static Call _current;
        internal static bool HasOpenScope { get { return _current != null; } }
        private static MethodInfo _readReserve;
        internal static string Context { get { return "; logisticsOperation=" + (_current?.Id ?? "none"); } }
        internal static void Reset() { _current = null; _readReserve = null; }
        internal static void Install(Harmony harmony)
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "AgesOfCalradiaLogistics");
            if (assembly == null)
            {
                SupplyCapture.Write("FEATURE_COVERAGE", 0, 0, "logistics_lifecycle", "absent", 0, 0, "optional_module_not_loaded");
                return;
            }
            var reserve = assembly.GetType("AgesOfCalradiaLogistics.LogisticsReserveBehavior", true);
            var market = assembly.GetType("AgesOfCalradiaLogistics.LogisticsSupplyMarketService", true);
            _readReserve = SupplyChainObserver.Require(reserve, "ReadExistingReserve", typeof(string));
            var targets = new List<MethodInfo>();
            foreach (string name in new[] { "ProcessDailySupply", "TryProcureAiSupply" })
                targets.Add(SupplyChainObserver.Require(reserve, name, typeof(MobileParty)));
            foreach (string name in new[] { "LoadSupplyCrates", "AddPurchasedSupplyCrates", "TryConsumeReserve" })
                targets.Add(SupplyChainObserver.Require(reserve, name, typeof(MobileParty), typeof(int)));
            targets.Add(SupplyChainObserver.Require(reserve, "RemovePartyReserve", typeof(PartyBase)));
            var coalition = SupplyChainObserver.Require(reserve, "TryConsumeReserve", typeof(MobileParty[]), typeof(int), typeof(int).MakeByRefType());
            var buy = SupplyChainObserver.Require(market, "TryBuy", typeof(Settlement), typeof(MobileParty), typeof(Hero), typeof(ItemObject), typeof(int), typeof(int));
            var produce = SupplyChainObserver.Require(market, "Produce", typeof(Settlement), typeof(ItemObject), typeof(int));
            foreach (var target in targets) harmony.Patch(target,
                new HarmonyMethod(typeof(LogisticsLifecycleObserver), "BeforeReserve"), null, null,
                new HarmonyMethod(typeof(LogisticsLifecycleObserver), "After"));
            harmony.Patch(coalition, new HarmonyMethod(typeof(LogisticsLifecycleObserver), "BeforeCoalition"), null, null,
                new HarmonyMethod(typeof(LogisticsLifecycleObserver), "After"));
            harmony.Patch(buy, new HarmonyMethod(typeof(LogisticsLifecycleObserver), "BeforeMarket"), null, null,
                new HarmonyMethod(typeof(LogisticsLifecycleObserver), "AfterBuy"));
            harmony.Patch(produce, new HarmonyMethod(typeof(LogisticsLifecycleObserver), "BeforeMarket"), null, null,
                new HarmonyMethod(typeof(LogisticsLifecycleObserver), "After"));
            SupplyCapture.Write("FEATURE_COVERAGE", 0, 0, "logistics_lifecycle", "present", 0, 0,
                "mvid=" + assembly.ManifestModule.ModuleVersionId + "; execution_requires_LOGISTICS_END; snapshots_not_additive");
        }
        private static string State(Call call)
        {
            if (call.Reserve != null)
                return "reserves=" + string.Join(",", call.Parties.Select(p => p.StringId + ":" + _readReserve.Invoke(call.Reserve, new object[] { p.StringId })));
            return "market=" + (call.Settlement == null || call.Item == null ? "unknown" : call.Settlement.ItemRoster.GetItemNumber(call.Item).ToString())
                + ",payerGold=" + (call.Payer == null ? "unknown" : call.Payer.Gold.ToString());
        }
        private static void Begin(Call call)
        {
            call.Id = Guid.NewGuid().ToString("N"); call.Previous = _current; call.Before = State(call);
            _current = call;
            SupplyCapture.Write("LOGISTICS_BEGIN", 0, 0, call.Owner, call.Name, 0, 0,
                "operation=" + call.Id + "; parentOperation=" + (call.Previous?.Id ?? "none") + "; state=" + call.Before);
        }
        private static void BeforeReserve(object __instance, object[] __args, MethodBase __originalMethod, out Call __state)
        {
            __state = null; if (!SupplyCapture.Active) return;
            try
            {
                var party = __args.OfType<MobileParty>().FirstOrDefault() ?? __args.OfType<PartyBase>().FirstOrDefault()?.MobileParty;
                if (party == null) return;
                __state = new Call { Reserve = __instance, Parties = new[] { party }, Owner = "party:" + party.StringId, Name = __originalMethod.Name };
                using (DiagnosticObserverCost.Measure("logisticsLifecycle")) Begin(__state);
            }
            catch (Exception ex) { SupplyCapture.Fail("logistics reserve before", ex); }
        }
        private static void BeforeCoalition(object __instance, MobileParty[] __0, out Call __state)
        {
            __state = null; if (!SupplyCapture.Active || __0 == null) return;
            try
            {
                __state = new Call { Reserve = __instance, Parties = __0.Where(p => p != null).Distinct().ToArray(), Owner = "coalition", Name = "TryConsumeReserveCoalition" };
                using (DiagnosticObserverCost.Measure("logisticsLifecycle")) Begin(__state);
            }
            catch (Exception ex) { SupplyCapture.Fail("logistics coalition before", ex); }
        }
        private static void BeforeMarket(object[] __args, MethodBase __originalMethod, out Call __state)
        {
            __state = null; if (!SupplyCapture.Active) return;
            try
            {
                var settlement = __args.OfType<Settlement>().FirstOrDefault();
                __state = new Call { Settlement = settlement, Item = __args.OfType<ItemObject>().FirstOrDefault(),
                    Payer = __args.OfType<Hero>().FirstOrDefault(), Owner = "settlement:" + settlement?.StringId, Name = __originalMethod.Name };
                using (DiagnosticObserverCost.Measure("logisticsLifecycle")) Begin(__state);
            }
            catch (Exception ex) { SupplyCapture.Fail("logistics market before", ex); }
        }
        private static void AfterBuy(Call __state, bool __result, bool __runOriginal, Exception __exception)
        { Finish(__state, __runOriginal, __exception, __result.ToString()); }
        private static void After(Call __state, bool __runOriginal, Exception __exception)
        { Finish(__state, __runOriginal, __exception, "endpoint_only"); }
        private static void Finish(Call call, bool ran, Exception error, string result)
        {
            if (call == null) return;
            try
            {
                if (!SupplyCapture.Active) return;
                using (DiagnosticObserverCost.Measure("logisticsLifecycle"))
                    SupplyCapture.Write("LOGISTICS_END", 0, 0, call.Owner, call.Name, 0, 0,
                        "operation=" + call.Id + "; parentOperation=" + (call.Previous?.Id ?? "none") + "; beforeState=" + call.Before
                        + "; afterState=" + State(call) + "; originalRan=" + ran + "; result=" + result
                        + "; error=" + (error == null ? "none" : error.GetType().FullName) + "; gross_context_not_additive; reserve_minus_one_unknown");
                if (error != null) SupplyCapture.Fail("logistics native exception", error);
            }
            catch (Exception ex) { SupplyCapture.Fail("logistics lifecycle after", ex); }
            finally { _current = call.Previous; }
        }
    }
}
