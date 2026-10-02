using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Bannerlord 1.4.8 private caravan boundaries. Observation only: native scores,
    // prices and quantities are recorded, never recomputed, and RNG is never called.
    // No __args/ref bindings on methods with native out parameters. Finalizers are
    // void, preserving original exceptions. Target/IL mismatch fails supply start.
    // All hooks share the supply owner and reset lifecycle; no save state.
    internal static class SupplyCaravanObserver
    {
        private static readonly Type Behavior = typeof(CaravansCampaignBehavior);
        private static long _next;
        private static EconomyObjectIdentity _identity = new EconomyObjectIdentity();
        [ThreadStatic] private static Call _current;
        [ThreadStatic] private static Candidate _candidate;
        internal sealed class Call
        {
            internal Call Previous;
            internal long Id;
            internal MobileParty Party;
            internal string Kind;
            internal Town Town;
            internal HashSet<string> Evaluated = new HashSet<string>();
        }
        internal sealed class Candidate
        {
            internal Candidate Previous;
            internal Town Town;
            internal readonly List<string> Buy = new List<string>(), Sell = new List<string>();
        }
        private static MethodInfo Require(string name, params Type[] args) { return SupplyChainObserver.Require(Behavior, name, args); }
        internal static IEnumerable<MethodInfo> Targets()
        {
            yield return Require("FindNextDestinationForCaravan", typeof(MobileParty), typeof(bool), typeof(MobileParty.NavigationType).MakeByRefType(), typeof(bool).MakeByRefType(), typeof(bool).MakeByRefType());
            yield return Require("GetTradeScoreForTown", typeof(MobileParty), typeof(Town), typeof(CampaignTime), typeof(float), typeof(bool), typeof(MobileParty.NavigationType).MakeByRefType(), typeof(bool).MakeByRefType());
            yield return Require("CalculateTownBuyScoreForCategory", typeof(TownMarketData), typeof(int), typeof(MobileParty));
            yield return Require("CalculateTownSellScoreForCategory", typeof(MobileParty), typeof(TownMarketData), typeof(int), typeof(float));
            yield return Require("BuyGoods", typeof(MobileParty), typeof(Town));
            yield return Require("BuyCategory", typeof(MobileParty), typeof(Town), typeof(ItemCategory), typeof(float), typeof(float), typeof(List<ValueTuple<EquipmentElement, int>>));
            yield return Require("CalculateBuyValue", typeof(ItemCategory), typeof(Town), typeof(MobileParty), typeof(float), typeof(float));
            yield return Require("CanTradeWith", typeof(IFaction), typeof(IFaction));
            yield return Require("OnSettlementEntered", typeof(MobileParty), typeof(Settlement), typeof(Hero));
            yield return Require("OnMobilePartyDestroyed", typeof(MobileParty), typeof(PartyBase));
            yield return SupplyChainObserver.Require(typeof(Helpers.AiHelper), "GetBestNavigationTypeAndAdjustedDistanceOfSettlementForMobileParty", typeof(MobileParty), typeof(Settlement), typeof(bool), typeof(MobileParty.NavigationType).MakeByRefType(), typeof(float).MakeByRefType(), typeof(bool).MakeByRefType());
        }
        internal static void Reset() { _next = 0; _current = null; _candidate = null; _identity = new EconomyObjectIdentity(); }
        internal static void Install(Harmony harmony)
        {
            Reset();
            foreach (var m in Targets())
            {
                string prefix = null, postfix = null, finalizer = null;
                switch (m.Name)
                {
                    case "FindNextDestinationForCaravan": prefix = "RouteBefore"; finalizer = "RouteAfter"; break;
                    case "GetTradeScoreForTown": prefix = "ScoreBefore"; finalizer = "ScoreAfter"; break;
                    case "CalculateTownBuyScoreForCategory": postfix = "BuyContribution"; break;
                    case "CalculateTownSellScoreForCategory": postfix = "SellContribution"; break;
                    case "BuyGoods": prefix = "BuyBefore"; finalizer = "BuyAfter"; break;
                    case "BuyCategory": prefix = "CategoryBefore"; finalizer = "CategoryAfter"; break;
                    case "CalculateBuyValue": postfix = "BuyValue"; break;
                    case "CanTradeWith": postfix = "TradePermission"; break;
                    case "OnSettlementEntered": prefix = "Arrival"; break;
                    case "OnMobilePartyDestroyed": prefix = "Destroyed"; break;
                    case "GetBestNavigationTypeAndAdjustedDistanceOfSettlementForMobileParty": postfix = "Navigation"; break;
                }
                if (m.Name == "BuyCategory") QuantityTap(PatchProcessor.GetOriginalInstructions(m)).ToArray();
                harmony.Patch(m, H(prefix), H(postfix), m.Name == "BuyCategory" ? H("QuantityTap") : null, H(finalizer));
            }
            harmony.Patch(SupplyChainObserver.Require(typeof(Town), "GetItemPrice", typeof(ItemObject), typeof(MobileParty), typeof(bool)), postfix: H("ItemPrice"));
            Type model = Campaign.Current == null ? typeof(TaleWorlds.CampaignSystem.GameComponents.DefaultCaravanModel) : Campaign.Current.Models.CaravanModel.GetType();
            harmony.Patch(SupplyChainObserver.Require(model, "GetMaxGoldToSpendOnOneItemCategory", typeof(MobileParty), typeof(ItemCategory)), postfix: H("BudgetCap"));
        }
        private static HarmonyMethod H(string name) { return name == null ? null : new HarmonyMethod(typeof(SupplyCaravanObserver), name); }
        internal static string PartyKey(MobileParty p) { return p == null ? "none" : p.StringId + "/instance:" + _identity.Get(p); }
        private static void Arrival(MobileParty __0, Settlement __1)
        {
            if (!SupplyCapture.Active || __0 == null || !__0.IsCaravan) return;
            try { SupplyCapture.Write("CARAVAN_ARRIVAL", 0, 0, PartyKey(__0), __1.StringId, 0, 0, State(__0)); }
            catch (Exception ex) { SupplyCapture.Fail("caravan arrival", ex); }
        }
        private static void Destroyed(MobileParty __0)
        {
            if (!SupplyCapture.Active || __0 == null || !__0.IsCaravan) return;
            try { SupplyCapture.Write("CARAVAN_DESTROYED", 0, 0, PartyKey(__0), "callback_not_quantified_loss", 0, 0, State(__0)); }
            catch (Exception ex) { SupplyCapture.Fail("caravan destruction", ex); }
        }
        private static void TradePermission(IFaction __0, IFaction __1, bool __result)
        {
            if (!SupplyCapture.Active || _current == null || _current.Kind != "route") return;
            try { Emit("ROUTE_PERMISSION", __1.StringId, 0, __result ? 1 : 0, "sourceFaction=" + __0.StringId + "; native_CanTradeWith_result"); }
            catch (Exception ex) { SupplyCapture.Fail("route permission", ex); }
        }
        internal static void ObserveBuyFactor(TownMarketData market, ItemCategory category, float result)
        {
            if (!SupplyCapture.Active || _current == null || _candidate != null
                || !ReferenceEquals(market, _current.Town?.MarketData)
                || !(_current.Kind == "buy" || _current.Kind.StartsWith("category:", StringComparison.Ordinal))) return;
            try { Emit("BUY_CATEGORY_PRICE", category.StringId, 0, result, "native_market_factor; may_repeat_from_nested_price_calls"); }
            catch (Exception ex) { SupplyCapture.Fail("category price", ex); }
        }
        private static void BudgetCap(ItemCategory __1, int __result)
        {
            if (!SupplyCapture.Active || _current == null || _candidate != null) return;
            try { Emit("BUY_BUDGET_CAP", __1.StringId, 0, __result, "effective_model_result"); }
            catch (Exception ex) { SupplyCapture.Fail("category budget", ex); }
        }
        internal static void Snapshot()
        {
            if (!SupplyCapture.Active) return;
            try
            {
                foreach (var p in MobileParty.All.Where(x => x.IsCaravan))
                    SupplyCapture.Write("CARAVAN_SNAPSHOT", 0, 0, PartyKey(p), "state", 0, 0,
                        State(p) + "; active=" + p.IsActive + "; snapshot_not_arrival_or_loss_proof");
            }
            catch (Exception ex) { SupplyCapture.Fail("caravan snapshot", ex); }
        }
        internal static IEnumerable<CodeInstruction> QuantityTap(IEnumerable<CodeInstruction> code)
        {
            return SupplyValueTaps.Tap(code, AccessTools.Method(typeof(MBRandom), "RoundRandomized", new[] { typeof(float) }),
                AccessTools.Method(typeof(SupplyCaravanObserver), "Quantity"));
        }
        internal static void Quantity(int value)
        {
            if (!SupplyCapture.Active || _current == null) return;
            try { Emit("BUY_QUANTITY", "randomized_before_caps", 0, value, State(_current.Party)); }
            catch (Exception ex) { SupplyCapture.Fail("buy quantity", ex); }
        }
        private static void Navigation(MobileParty __0, Settlement __1, MobileParty.NavigationType __3, float __4, bool __5)
        {
            if (!SupplyCapture.Active || _candidate == null || _current == null || __0 != _current.Party || __1 != _candidate.Town.Settlement) return;
            try { Emit("ROUTE_NAVIGATION", __3.ToString(), 0, __4, "fromPort=" + __5 + "; native_adjusted_distance"); }
            catch (Exception ex) { SupplyCapture.Fail("route navigation", ex); }
        }
        internal static void Index(ItemCategory category, double average, double minimum, bool available, int categoryValue)
        {
            if (!SupplyCapture.Active || _current == null || _candidate != null) return;
            try { Emit("BUY_INDEX", category.StringId, average, minimum, "available=" + available + "; categoryValue=" + categoryValue); }
            catch (Exception ex) { SupplyCapture.Fail("buy index", ex); }
        }
        private static string Cargo(MobileParty p)
        {
            return string.Join(",", p.ItemRoster.GroupBy(x => x.EquipmentElement.Item.ItemCategory.StringId).Select(g => g.Key + ":" + g.Sum(x => x.Amount)));
        }
        private static string State(MobileParty p)
        {
            return "gold=" + p.PartyTradeGold + "; weight=" + SupplyCapture.N(p.TotalWeightCarried) + "; capacity=" + p.InventoryCapacity
                + "; members=" + p.Party.NumberOfAllMembers + "; pack=" + p.ItemRoster.NumberOfPackAnimals + "; livestock=" + p.ItemRoster.NumberOfLivestockAnimals
                + "; naval=" + p.HasNavalNavigationCapability + "; land=" + p.HasLandNavigationCapability
                + "; current=" + p.CurrentSettlement?.StringId + "; target=" + p.TargetSettlement?.StringId + "; home=" + p.HomeSettlement?.StringId
                + "; cargo=" + Cargo(p);
        }
        private static Call Begin(MobileParty p, Town t, string kind)
        {
            var c = new Call { Previous = _current, Id = ++_next, Party = p, Town = t, Kind = kind };
            _current = c;
            Emit("CARAVAN_BEGIN", kind, 0, 0, State(p));
            return c;
        }
        private static void Emit(string kind, string metric, double before, double after, string detail)
        {
            var c = _current;
            SupplyCapture.Write(kind, 0, 0, PartyKey(c.Party), metric, before, after,
                "decision=" + c.Id + "; parentDecision=" + (c.Previous == null ? 0 : c.Previous.Id) + "; operation=" + c.Kind
                + "; town=" + (_candidate == null ? c.Town?.Settlement.StringId : _candidate.Town.Settlement.StringId) + "; " + detail);
        }
        private static void RouteBefore(MobileParty __0, bool __1, out Call __state)
        {
            __state = null;
            if (!SupplyCapture.Active) return;
            try
            {
                __state = Begin(__0, null, "route");
                Emit("ROUTE_CONTEXT", "distanceCut", 0, __1 ? 1 : 0, "candidates=" + string.Join(",", Town.AllTowns.Select(t => t.Settlement.StringId
                    + ":siege=" + t.IsUnderSiege + ":port=" + t.Settlement.HasPort + ":faction=" + t.MapFaction.StringId)));
            }
            catch (Exception ex) { SupplyCapture.Fail("route begin", ex); }
        }
        private static void RouteAfter(Call __state, Town __result, Exception __exception)
        {
            Finish(__state, __exception, __result == null ? "selected=none" : "selected=" + __result.Settlement.StringId,
                "evaluated=" + (__state == null ? "" : string.Join(",", __state.Evaluated)) + "; unscored_not_automatically_unreachable");
        }
        private static void BuyBefore(MobileParty __0, Town __1, out Call __state)
        {
            __state = null;
            if (!SupplyCapture.Active) return;
            try { __state = Begin(__0, __1, "buy"); }
            catch (Exception ex) { SupplyCapture.Fail("buy begin", ex); }
        }
        private static void BuyAfter(Call __state, Exception __exception) { Finish(__state, __exception, "buy", ""); }
        private static void Finish(Call state, Exception error, string metric, string detail)
        {
            if (state == null) return;
            try
            {
                if (error != null) { SupplyCapture.Fail("native caravan decision", error); return; }
                if (SupplyCapture.Active) Emit("CARAVAN_END", metric, 0, 0, State(state.Party) + "; " + detail);
            }
            catch (Exception ex) { SupplyCapture.Fail("caravan decision end", ex); }
            finally { _current = state.Previous; }
        }
        private static void ScoreBefore(Town __1, float __3, bool __4, out Candidate __state)
        {
            __state = null;
            if (!SupplyCapture.Active || _current == null) return;
            try
            {
                __state = new Candidate { Previous = _candidate, Town = __1 }; _candidate = __state;
                _current.Evaluated.Add(__1.Settlement.StringId);
                Emit("ROUTE_CANDIDATE_BEGIN", "native", 0, __3, "distanceCut=" + __4 + "; townGold=" + __1.Gold + "; security=" + SupplyCapture.N(__1.Security));
            }
            catch (Exception ex) { SupplyCapture.Fail("route candidate", ex); }
        }
        private static void ScoreAfter(Candidate __state, float __result, Exception __exception)
        {
            if (__state == null) return;
            try
            {
                if (__exception != null) { SupplyCapture.Fail("native route score", __exception); return; }
                if (SupplyCapture.Active) Emit("ROUTE_SCORE", "native_final", 0, __result, "buyContributions=" + string.Join(",", __state.Buy)
                    + "; sellContributions=" + string.Join(",", __state.Sell) + "; contributions_before_native_weights; minus_one_requires_navigation_or_distance_evidence");
            }
            catch (Exception ex) { SupplyCapture.Fail("route score", ex); }
            finally { _candidate = __state.Previous; }
        }
        private static void BuyContribution(int __1, float __result)
        {
            if (!SupplyCapture.Active || _candidate == null) return;
            try { _candidate.Buy.Add(ItemCategories.All[__1].StringId + ":" + SupplyCapture.N(__result)); }
            catch (Exception ex) { SupplyCapture.Fail("route buy contribution", ex); }
        }
        private static void SellContribution(MobileParty __0, int __2, float __result)
        {
            if (!SupplyCapture.Active || _candidate == null) return;
            try { _candidate.Sell.Add(__0.ItemRoster.GetElementCopyAtIndex(__2).EquipmentElement.Item.ItemCategory.StringId + ":" + SupplyCapture.N(__result)); }
            catch (Exception ex) { SupplyCapture.Fail("route sell contribution", ex); }
        }
        private static void BuyValue(ItemCategory __0, Town __1, float __3, float __4, float __result)
        {
            if (!SupplyCapture.Active || _current == null) return;
            try
            {
                var data = __1.MarketData.GetCategoryData(__0);
                Emit("BUY_VALUE", __0.StringId, 0, __result, "budgetFactor=" + SupplyCapture.N(__3) + "; capacityFactor=" + SupplyCapture.N(__4)
                    + "; stock=" + data.InStore + "; demand=" + SupplyCapture.N(data.Demand) + "; categoryAnimal=" + __0.IsAnimal);
            }
            catch (Exception ex) { SupplyCapture.Fail("buy value", ex); }
        }
        private static void CategoryBefore(MobileParty __0, Town __1, ItemCategory __2, out Call __state)
        {
            __state = null;
            if (!SupplyCapture.Active) return;
            try { __state = Begin(__0, __1, "category:" + __2.StringId); }
            catch (Exception ex) { SupplyCapture.Fail("buy category begin", ex); }
        }
        private static void CategoryAfter(Call __state, Exception __exception) { Finish(__state, __exception, "category", ""); }
        private static void ItemPrice(Town __instance, ItemObject __0, bool __2, int __result)
        {
            SupplyWoolSaleObserver.Price(__instance, new EquipmentElement(__0), __result);
            if (!SupplyCapture.Active || _current == null || _candidate != null) return;
            try { Emit("CARAVAN_ITEM_PRICE", __0.StringId, 0, __result, "selling=" + __2 + "; townGold=" + __instance.Gold + "; category=" + __0.ItemCategory.StringId); }
            catch (Exception ex) { SupplyCapture.Fail("caravan item price", ex); }
        }
    }
}
