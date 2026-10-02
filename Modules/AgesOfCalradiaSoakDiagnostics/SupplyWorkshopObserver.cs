using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Native 1.4.8 diagnostics only: exact targets below, read-only prefixes,
    // postfixes and exception-preserving finalizers. No repeated model/RNG calls.
    // Installed by the supply observer with the same owner and rollback lifecycle.
    // Missing signatures fail capture. Candidate gate reasons are not proof when
    // third-party patches change native semantics. Verify-SupplyCapture covers hooks.
    internal static class SupplyWorkshopObserver
    {
        private static string[] Items { get { return SupplyCategories.All; } }
        private static long _sequence;
        private static EconomyObjectIdentity _identity = new EconomyObjectIdentity();
        [ThreadStatic] private static Run _run;
        [ThreadStatic] private static Cycle _cycle;
        internal static string Context { get { return "; workshopRun=" + (_run == null ? "none" : _run.Id.ToString())
            + "; workshopCycle=" + (_cycle == null ? "none" : _cycle.Id.ToString()); } }
        internal sealed class Run
        {
            internal Run Previous;
            internal Workshop Shop;
            internal long Id;
            internal float[] Progress;
            internal int[] Attempts;
        }
        internal sealed class Cycle
        {
            internal Cycle Previous;
            internal Workshop Shop;
            internal WorkshopType.Production Recipe;
            internal long Id;
            internal Dictionary<string, double> Stock;
            internal ItemRoster Warehouse;
            internal readonly Dictionary<string, double> Events = new Dictionary<string, double>();
        }
        internal static IEnumerable<MethodInfo> Targets()
        {
            Type t = typeof(WorkshopsCampaignBehavior), p = typeof(WorkshopType.Production), w = typeof(Workshop);
            yield return SupplyChainObserver.Require(t, "DetermineItemRosterHasSufficientInputs", p, typeof(ItemRoster), typeof(Town), typeof(int).MakeByRefType());
            yield return SupplyChainObserver.Require(t, "CanPlayerWorkshopProduceThisCycle", p, w, typeof(int), typeof(int), typeof(bool), typeof(bool));
            yield return SupplyChainObserver.Require(t, "CanNotableWorkshopProduceThisCycle", p, w, typeof(int), typeof(int), typeof(bool));
            yield return SupplyChainObserver.Require(t, "TickOneProductionCycleForPlayerWorkshop", p, w, typeof(bool));
            yield return SupplyChainObserver.Require(t, "TickOneProductionCycleForNotableWorkshop", p, w, typeof(bool));
            yield return SupplyChainObserver.Require(t, "RunTownWorkshop", typeof(Town), w);
        }
        private static MethodInfo _warehouse;
        internal static void Install(Harmony harmony)
        {
            Reset();
            var targets = Targets().ToArray();
            _warehouse = SupplyChainObserver.Require(typeof(WorkshopsCampaignBehavior), "GetWarehouseRoster", typeof(Settlement));
            Type dispatcher = typeof(Campaign).Assembly.GetType("TaleWorlds.CampaignSystem.CampaignEventDispatcher", true);
            var events = new[] { "OnItemProduced", "OnItemConsumed" }.Select(n => SupplyChainObserver.Require(dispatcher, n, typeof(ItemObject), typeof(Settlement), typeof(int))).ToArray();
            foreach (var e in events) harmony.Patch(e, prefix: new HarmonyMethod(typeof(SupplyWorkshopObserver), "ItemEvent"));
            foreach (MethodInfo m in targets)
            {
                if (m.Name == "RunTownWorkshop")
                    harmony.Patch(m, prefix: new HarmonyMethod(typeof(SupplyWorkshopObserver), "RunBefore"), finalizer: new HarmonyMethod(typeof(SupplyWorkshopObserver), "RunAfter"));
                else if (m.Name.StartsWith("TickOne", StringComparison.Ordinal))
                    harmony.Patch(m, prefix: new HarmonyMethod(typeof(SupplyWorkshopObserver), "CycleBefore"), finalizer: new HarmonyMethod(typeof(SupplyWorkshopObserver), "CycleAfter"));
                else if (m.Name == "DetermineItemRosterHasSufficientInputs")
                    harmony.Patch(m, postfix: new HarmonyMethod(typeof(SupplyWorkshopObserver), "InputGate") { priority = Priority.Last });
                else harmony.Patch(m, postfix: new HarmonyMethod(typeof(SupplyWorkshopObserver), "Gate") { priority = Priority.Last });
            }
            foreach (MethodInfo m in targets.Concat(events))
                SoakLog.Write("SUPPLY_WORKSHOP_HOOK", m.ToString() + "; mvid=" + m.Module.ModuleVersionId + "; owners=" + string.Join(",", Harmony.GetPatchInfo(m).Owners));
        }
        internal static void Reset() { _sequence = 0; _run = null; _cycle = null; _identity = new EconomyObjectIdentity(); }
        internal static void Snapshot()
        {
            if (!SupplyCapture.Active) return;
            try
            {
                foreach (Town town in Town.AllTowns)
                    foreach (Workshop shop in town.Workshops)
                        if (shop.WorkshopType != null && shop.WorkshopType.Productions.Any(Relevant))
                        {
                            Emit(shop, "WORKSHOP_STATE", "capital", 0, shop.Capital,
                                "townGold=" + town.Gold + "; rebellion=" + town.InRebelliousState + "; owner=" + shop.Owner?.StringId
                                + "; wallet=" + SupplyCashObserver.WorkshopWalletId(shop)
                                + "; workshopTag=" + shop.Tag
                                + "; siege=" + town.Settlement.IsUnderSiege + "; foodStocks=" + SupplyCapture.N(town.FoodStocks)
                                + "; sampled_state_not_cycle_result");
                            foreach (var recipe in shop.WorkshopType.Productions)
                                Emit(shop, "WORKSHOP_RECIPE", "declared", 0, 0, Recipe(shop, recipe));
                        }
            }
            catch (Exception ex) { SupplyCapture.Fail("workshop snapshot", ex); }
        }
        private static bool Relevant(WorkshopType.Production p) { return true; }
        private static string Recipe(Workshop shop, WorkshopType.Production p)
        {
            return "recipe=" + shop.WorkshopType.Productions.IndexOf(p) + "; baseSpeed=" + SupplyCapture.N(p.ConversionSpeed)
                + "; inputs=" + string.Join(",", p.Inputs.Select(x => x.Item1.StringId + ":" + x.Item2))
                + "; outputs=" + string.Join(",", p.Outputs.Select(x => x.Item1.StringId + ":" + x.Item2));
        }
        private static void Emit(Workshop s, string kind, string metric, double before, double after, string detail)
        {
            SupplyCapture.Write(kind, 0, 0, s.Settlement.StringId + "/workshop:" + _identity.Get(s), metric, before, after,
                "type=" + s.WorkshopType.StringId + "; run=" + (_run == null ? 0 : _run.Id) + "; cycle=" + (_cycle == null ? 0 : _cycle.Id) + "; " + detail);
        }
        private static void RunBefore(Workshop __1, out Run __state)
        {
            __state = null;
            if (!SupplyCapture.Active) return;
            try
            {
                if (!__1.WorkshopType.Productions.Any(Relevant)) return;
                __state = new Run { Previous = _run, Shop = __1, Id = ++_sequence,
                    Progress = Enumerable.Range(0, __1.WorkshopType.Productions.Count).Select(__1.GetProductionProgress).ToArray(),
                    Attempts = new int[__1.WorkshopType.Productions.Count] };
                _run = __state;
            }
            catch (Exception ex) { SupplyCapture.Fail("workshop run begin", ex); }
        }
        private static void RunAfter(Run __state, Exception __exception)
        {
            if (__state == null) return;
            try
            {
                if (__exception != null) { SupplyCapture.Fail("native workshop run", __exception); return; }
                if (!SupplyCapture.Active) return;
                for (int i = 0; i < __state.Progress.Length; i++)
                {
                    var p = __state.Shop.WorkshopType.Productions[i];
                    if (!Relevant(p)) continue;
                    float after = __state.Shop.GetProductionProgress(i);
                    double increment = DerivedIncrement(__state.Progress[i], after, __state.Attempts[i]);
                    Emit(__state.Shop, "WORKSHOP_PROGRESS", "recipe:" + i, __state.Progress[i], after,
                        Recipe(__state.Shop, p) + "; attempts=" + __state.Attempts[i] + "; derivedIncrement=" + SupplyCapture.N(increment)
                        + "; derived_native_cap_and_decrement_not_model_query");
                }
            }
            catch (Exception ex) { SupplyCapture.Fail("workshop run end", ex); }
            finally { _run = __state.Previous; }
        }
        internal static double DerivedIncrement(float before, float after, int attempts)
        {
            return after - Math.Min(1f, before) + attempts;
        }
        private static Dictionary<string, double> Stock(Workshop shop, ItemRoster warehouse)
        {
            var values = new Dictionary<string, double>();
            foreach (string item in Items)
            {
                values["market/" + item] = shop.Settlement.ItemRoster.Where(x => x.EquipmentElement.Item.ItemCategory.StringId == item).Sum(x => x.Amount);
                values["warehouse/" + item] = warehouse == null ? 0 : warehouse.Where(x => x.EquipmentElement.Item.ItemCategory.StringId == item).Sum(x => x.Amount);
                values["private/" + item] = ProcurementObservation.Stock(shop, item);
            }
            return values;
        }
        private static void CycleBefore(object __instance, WorkshopType.Production __0, Workshop __1, out Cycle __state)
        {
            __state = null;
            if (!SupplyCapture.Active || !Relevant(__0)) return;
            try
            {
                var warehouse = (ItemRoster)_warehouse.Invoke(__instance, new object[] { __1.Settlement });
                __state = new Cycle { Previous = _cycle, Shop = __1, Recipe = __0, Id = ++_sequence, Warehouse = warehouse, Stock = Stock(__1, warehouse) };
                _cycle = __state;
                if (_run != null && _run.Shop == __1) _run.Attempts[__1.WorkshopType.Productions.IndexOf(__0)]++;
                Emit(__1, "WORKSHOP_ATTEMPT", "begin", 0, 0, Recipe(__1, __0));
            }
            catch (Exception ex) { SupplyCapture.Fail("workshop cycle begin", ex); }
        }
        private static void CycleAfter(Cycle __state, bool __result, Exception __exception)
        {
            if (__state == null) return;
            try
            {
                if (__exception != null) { SupplyCapture.Fail("native workshop cycle", __exception); return; }
                if (!SupplyCapture.Active) return;
                var after = Stock(__state.Shop, __state.Warehouse);
                foreach (var p in __state.Stock)
                    if (p.Value != after[p.Key]) Emit(__state.Shop, "WORKSHOP_STOCK_DELTA", p.Key, p.Value, after[p.Key], Recipe(__state.Shop, __state.Recipe) + "; net_change_not_gross_flow");
                foreach (string item in Items)
                {
                    double expected;
                    __state.Events.TryGetValue(item, out expected);
                    double actual = after["market/" + item] - __state.Stock["market/" + item]
                        + after["warehouse/" + item] - __state.Stock["warehouse/" + item]
                        + after["private/" + item] - __state.Stock["private/" + item];
                    if (expected != 0 || actual != 0)
                        Emit(__state.Shop, "WORKSHOP_FLOW_CHECK", item, expected, actual,
                            "event_net_vs_market_plus_warehouse_plus_private_net; mismatch_requires_investigation");
                }
                Emit(__state.Shop, "WORKSHOP_CYCLE", __result ? "succeeded" : "failed_see_gates", 0, __result ? 1 : 0, Recipe(__state.Shop, __state.Recipe)
                    + "; checkedCategories=" + Items.Length + "; matching_zero_flow_rows_omitted");
            }
            catch (Exception ex) { SupplyCapture.Fail("workshop cycle end", ex); }
            finally { _cycle = __state.Previous; }
        }
        // Never use Harmony __args on the native out-int input-cost method.
        // Typed by-value binding observes the final cost without writeback.
        private static void InputGate(int __3, bool __result)
        {
            if (!SupplyCapture.Active || _cycle == null) return;
            try
            {
                Emit(_cycle.Shop, "WORKSHOP_GATE", "DetermineItemRosterHasSufficientInputs:" + (__result ? "accepted" : "inputs_rejected"),
                    0, __result ? 1 : 0, Recipe(_cycle.Shop, _cycle.Recipe) + "; inputCost=" + __3 + ProcurementObservation.Gate(_cycle.Shop));
                foreach (var input in _cycle.Recipe.Inputs)
                {
                    string category = input.Item1.StringId;
                    int market = _cycle.Shop.Settlement.ItemRoster.Where(e => e.EquipmentElement.Item.ItemCategory == input.Item1).Sum(e => e.Amount);
                    Emit(_cycle.Shop, "WORKSHOP_INPUT_WITNESS", category, input.Item2, market,
                        Recipe(_cycle.Shop, _cycle.Recipe) + "; nativeAccepted=" + __result
                        + "; privateStock=" + ProcurementObservation.Stock(_cycle.Shop, category)
                        + ProcurementObservation.InputEvidence(_cycle.Shop, category)
                        + "; required_before_market_after; private_eligibility_requires_ledger; not_independent_shortage");
                }
            }
            catch (Exception ex) { SupplyCapture.Fail("workshop input gate", ex); }
        }
        private static void Gate(MethodBase __originalMethod, object[] __args, bool __result)
        {
            if (!SupplyCapture.Active || _cycle == null) return;
            try
            {
                string detail = Recipe(_cycle.Shop, _cycle.Recipe) + "; capital=" + _cycle.Shop.Capital + "; townGold=" + _cycle.Shop.Settlement.Town.Gold;
                string reason = __result ? "accepted" : "inputs_rejected";
                if (__originalMethod.Name.StartsWith("Can", StringComparison.Ordinal))
                {
                    int input = (int)__args[2], output = (int)__args[3];
                    reason = __result ? "accepted" : "rejected_inspect_profit_cash_capital";
                    detail += "; inputCost=" + input + "; outputIncome=" + output + "; effectCapital=" + __args[4]
                        + "; nativeProfitHurdle=" + SupplyCapture.N(_cycle.Shop.WorkshopType.IsHidden ? input : input + 200f / _cycle.Recipe.ConversionSpeed)
                        + (__originalMethod.Name == "CanNotableWorkshopProduceThisCycle" ? ProcurementObservation.ApprovalHurdle(_cycle.Shop, input) : "")
                        + "; gameStarted=" + Campaign.Current.GameStarted
                        + (__args.Length > 5 ? "; allOutputsToWarehouse=" + __args[5] : "")
                        + "; rule_context_not_recomputed_verdict" + ProcurementObservation.Gate(_cycle.Shop);
                }
                Emit(_cycle.Shop, "WORKSHOP_GATE", __originalMethod.Name + ":" + reason, 0, __result ? 1 : 0, detail);
            }
            catch (Exception ex) { SupplyCapture.Fail("workshop gate", ex); }
        }
        private static void ItemEvent(MethodBase __originalMethod, ItemObject __0, Settlement __1, int __2)
        {
            if (!SupplyCapture.Active || _cycle == null || __1 != _cycle.Shop.Settlement) return;
            try
            {
                string category = __0.ItemCategory.StringId;
                double previous;
                _cycle.Events.TryGetValue(category, out previous);
                _cycle.Events[category] = previous + (__originalMethod.Name == "OnItemProduced" ? __2 : -__2);
                Emit(_cycle.Shop, __originalMethod.Name == "OnItemProduced" ? "WORKSHOP_PRODUCED" : "WORKSHOP_CONSUMED",
                    __0.ItemCategory.StringId, 0, __2, Recipe(_cycle.Shop, _cycle.Recipe) + "; item=" + __0.StringId + "; native_event_compare_roster_delta");
            }
            catch (Exception ex) { SupplyCapture.Fail("workshop item event", ex); }
        }
    }
}
