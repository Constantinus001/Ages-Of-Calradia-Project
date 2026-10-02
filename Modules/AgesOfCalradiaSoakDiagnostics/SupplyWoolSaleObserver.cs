using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Bannerlord 1.4.8 SellGoodsInternal: observation only, including zero sales.
    // Prefix/finalizer preserve native exceptions. Lookup/factor postfixes read
    // existing results; a validated DUP tap records the existing randomized
    // quantity, never calls RNG again. Reflection/IL mismatch fails capture.
    // Session state resets with the supply Harmony owner. No persistence.
    internal static class SupplyWoolSaleObserver
    {
        [ThreadStatic] private static Call _current;
        private static long _next;
        private static readonly Type Behavior = typeof(CaravansCampaignBehavior);
        private static readonly Type Index = Behavior.GetNestedType("PriceIndexData", BindingFlags.NonPublic);
        private static readonly FieldInfo Average = AccessTools.Field(Index, "AverageBuySellPriceIndex");
        private static readonly FieldInfo Minimum = AccessTools.Field(Index, "MinBuySellPriceIndex");
        private static readonly FieldInfo Totals = AccessTools.Field(Behavior, "_totalValueOfItemsAtCategory");
        internal sealed class Call
        {
            internal Call Previous;
            internal MobileParty Party;
            internal Town Town;
            internal long Id;
            internal int Before;
            internal float Limit;
            internal bool LoseWeight, Wool;
            internal bool HorsePass;
            internal string Category;
            internal int Evaluations;
        }
        internal static MethodInfo SellTarget()
        {
            return SupplyChainObserver.Require(Behavior, "SellGoodsInternal", typeof(MobileParty), typeof(Town), typeof(bool),
                typeof(List<ValueTuple<EquipmentElement, int>>), typeof(float), typeof(bool));
        }
        internal static MethodInfo LookupTarget()
        {
            if (Index == null || Average == null || Minimum == null || Totals == null)
                throw new MissingMemberException("Wool price-index contract changed");
            return SupplyChainObserver.Require(Behavior, "GetCategoryPriceData", typeof(ItemCategory), typeof(MobileParty), Index.MakeByRefType());
        }
        internal static void Reset() { _current = null; _next = 0; }
        internal static void Price(Town town, EquipmentElement item, int value)
        {
            try
            {
                if (SupplyCapture.Active && _current != null && ReferenceEquals(town, _current.Town))
                {
                    Emit("SELL_PRICE", item.Item.ItemCategory.StringId, 0, value, "item=" + item.Item.StringId + "; repeated_quotes_not_transactions");
                    if (!_current.HorsePass && item.Item.ItemCategory.StringId == "wool")
                        Emit("WOOL_SELL_PRICE", "native_quote", 0, value, "includes_repeated_native_calls_not_transaction_count");
                }
            }
            catch (Exception ex) { SupplyCapture.Fail("wool price observation", ex); }
        }
        internal static IEnumerable<CodeInstruction> QuantityTap(IEnumerable<CodeInstruction> code)
        {
            return SupplyValueTaps.Tap(code, AccessTools.Method(typeof(MBRandom), "RoundRandomized", new[] { typeof(float) }),
                AccessTools.Method(typeof(SupplyWoolSaleObserver), "Quantity"));
        }
        internal static void Install(Harmony harmony)
        {
            var sell = SellTarget();
            var lookup = LookupTarget();
            QuantityTap(PatchProcessor.GetOriginalInstructions(sell)).ToArray();
            // Harmony 2.4.2 __args on this out-struct postfix writes a stale
            // default box back to the caller. Bind the private struct as a
            // typed value copy: no object[] and no ref/out observer parameter.
            harmony.Patch(lookup, postfix: new HarmonyMethod(LookupObserver()));
            harmony.Patch(SupplyChainObserver.Require(typeof(TownMarketData), "GetPriceFactor", typeof(ItemCategory)),
                postfix: new HarmonyMethod(typeof(SupplyWoolSaleObserver), "Factor"));
            harmony.Patch(sell, new HarmonyMethod(typeof(SupplyWoolSaleObserver), "Before"), null,
                new HarmonyMethod(typeof(SupplyWoolSaleObserver), "QuantityTap"),
                new HarmonyMethod(typeof(SupplyWoolSaleObserver), "After") { priority = Priority.Last });
        }
        private static int Wool(MobileParty party)
        { return party.ItemRoster.Where(e => e.EquipmentElement.Item.ItemCategory.StringId == "wool").Sum(e => e.Amount); }
        private static void Before(MobileParty __0, Town __1, bool __2, float __4, bool __5, out Call __state)
        {
            __state = null;
            if (!SupplyCapture.Active || !__0.IsCaravan) return;
            try
            {
                __state = new Call { Previous = _current, Party = __0, Town = __1, Before = Wool(__0), Id = ++_next, Limit = __4, LoseWeight = __5, HorsePass = __2 };
                _current = __state;
                Emit("SELL_BEGIN", "cargo", 0, 0, Cargo(__0) + "; limit=" + SupplyCapture.N(__4) + "; loseWeight=" + __5);
                if (!__2) Emit("WOOL_SELL_BEGIN", "cargo", __state.Before, __state.Before, "limit=" + SupplyCapture.N(__4) + "; loseWeight=" + __5);
            }
            catch (Exception ex) { SupplyCapture.Fail("wool sale begin", ex); }
        }
        internal static MethodInfo LookupObserver()
        { return AccessTools.Method(typeof(SupplyWoolSaleObserver), "Lookup").MakeGenericMethod(Index); }
        private static void Lookup<T>(object __instance, ItemCategory __0, T __2, bool __result)
        {
            if (!SupplyCapture.Active) return;
            try
            {
                var category = __0;
                var totals = (Dictionary<ItemCategory, int>)Totals.GetValue(__instance);
                int value;
                bool hasValue = totals.TryGetValue(category, out value);
                SupplyCaravanObserver.Index(category, Convert.ToDouble(Average.GetValue(__2)), Convert.ToDouble(Minimum.GetValue(__2)), __result, value);
                if (_current == null) return;
                _current.Category = category.StringId;
                Emit("SELL_INDEX", category.StringId, 0, value,
                    "available=" + __result + "; average=" + SupplyCapture.N(Convert.ToDouble(Average.GetValue(__2)))
                    + "; minimum=" + SupplyCapture.N(Convert.ToDouble(Minimum.GetValue(__2))) + "; limit=" + SupplyCapture.N(_current.Limit));
                _current.Wool = category.StringId == "wool";
                if (!_current.Wool || _current.HorsePass) return;
                _current.Evaluations++;
                Emit("WOOL_SELL_INDEX", __result ? "available" : "unavailable", 0, value,
                    "average=" + SupplyCapture.N(Convert.ToDouble(Average.GetValue(__2)))
                    + "; minimum=" + SupplyCapture.N(Convert.ToDouble(Minimum.GetValue(__2)))
                    + "; categoryValuePresent=" + hasValue + "; limit=" + SupplyCapture.N(_current.Limit)
                    + "; gameStarted=" + Campaign.Current.GameStarted);
            }
            catch (Exception ex) { SupplyCapture.Fail("wool index observation", ex); }
        }
        private static void Factor(TownMarketData __instance, ItemCategory __0, float __result)
        {
            SupplyCaravanObserver.ObserveBuyFactor(__instance, __0, __result);
            try
            {
                if (SupplyCapture.Active && _current != null && ReferenceEquals(__instance, _current.Town.MarketData))
                {
                    Emit("SELL_FACTOR", __0.StringId, 0, __result, "native_result_may_repeat");
                    if (__0.StringId == "wool" && !_current.HorsePass)
                        Emit("WOOL_SELL_FACTOR", "native_result", 0, __result, "may_repeat_from_nested_native_price_calls");
                }
            }
            catch (Exception ex) { SupplyCapture.Fail("wool factor observation", ex); }
        }
        internal static void Quantity(int value)
        {
            try
            {
                if (SupplyCapture.Active && _current != null)
                {
                    Emit("SELL_QUANTITY", _current.Category, 0, value, "native_randomized_before_caps; loseWeight=" + _current.LoseWeight);
                    if (_current.Wool && !_current.HorsePass)
                        Emit("WOOL_SELL_QUANTITY", "native_randomized_before_caps", 0, value, "loseWeight=" + _current.LoseWeight);
                }
            }
            catch (Exception ex) { SupplyCapture.Fail("wool quantity observation", ex); }
        }
        private static void After(Call __state, Exception __exception)
        {
            if (__state == null) return;
            try
            {
                if (__exception != null) { SupplyCapture.Fail("native wool sale", __exception); return; }
                if (SupplyCapture.Active)
                {
                    if (!__state.HorsePass) Emit("WOOL_SELL_END", "cargo", __state.Before, Wool(__state.Party), "evaluations=" + __state.Evaluations);
                    Emit("SELL_END", "cargo", 0, 0, Cargo(__state.Party));
                }
            }
            catch (Exception ex) { SupplyCapture.Fail("wool sale end", ex); }
            finally { _current = __state.Previous; }
        }
        private static void Emit(string kind, string metric, double before, double after, string detail)
        {
            var c = _current;
            SupplyCapture.Write(kind, 0, 0, c.Town.Settlement.StringId, metric, before, after,
                "decision=" + c.Id + "; party=" + SupplyCaravanObserver.PartyKey(c.Party)
                + "; townGold=" + c.Town.Gold + "; horsePass=" + c.HorsePass + "; " + detail);
        }
        private static string Cargo(MobileParty p)
        {
            return "cargo=" + string.Join(",", p.ItemRoster.GroupBy(x => x.EquipmentElement.Item.ItemCategory.StringId).Select(g => g.Key + ":" + g.Sum(x => x.Amount)))
                + "; gold=" + p.PartyTradeGold + "; weight=" + SupplyCapture.N(p.TotalWeightCarried) + "; capacity=" + p.InventoryCapacity
                + "; members=" + p.Party.NumberOfAllMembers + "; livestock=" + p.ItemRoster.NumberOfLivestockAnimals + "; pack=" + p.ItemRoster.NumberOfPackAnimals;
        }
    }
}
