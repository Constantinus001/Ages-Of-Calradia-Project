using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Native 1.4.8 observation boundaries only. No original is skipped and no
    // model/RNG is queried again. Prefix/finalizer snapshots preserve exceptions.
    // Version-sensitive private targets are preflighted; failure closes capture.
    // Shares supply owner/unpatch lifecycle. Tests verify exact hook installation.
    internal static class SupplyMarketObserver
    {
        private static string[] Items { get { return SupplyCategories.All; } }
        private static long _next;
        [ThreadStatic] private static Call _current;
        internal static string Context { get { return "; market=" + (_current == null ? "none" : _current.Id.ToString())
            + "; parentMarket=" + (_current?.Previous == null ? "none" : _current.Previous.Id.ToString()); } }
        internal sealed class Call
        {
            internal Call Previous;
            internal long Id;
            internal Town Town;
            internal MobileParty Party;
            internal string Kind;
            internal string Category;
            internal Dictionary<string, double> Before;
            internal CashPurposeContext.Frame Purpose;
        }
        internal static IEnumerable<MethodInfo> Targets()
        {
            yield return SupplyChainObserver.Require(typeof(ItemConsumptionBehavior), "MakeConsumption", typeof(Town), typeof(Dictionary<ItemCategory, float>), typeof(Dictionary<ItemCategory, int>));
            yield return SupplyChainObserver.Require(typeof(ItemConsumptionBehavior), "DeleteOverproducedItems", typeof(Town));
            yield return SupplyChainObserver.Require(typeof(ItemConsumptionBehavior), "GetFoodFromMarketInternal", typeof(Town), typeof(int), typeof(Dictionary<ItemCategory, int>));
            yield return SupplyChainObserver.Require(typeof(SellItemsAction), "ApplyInternal", typeof(PartyBase), typeof(PartyBase), typeof(ItemRosterElement), typeof(int), typeof(Settlement));
        }
        internal static void Reset() { _next = 0; _current = null; }
        internal static void Install(Harmony harmony)
        {
            var targets = Targets().ToArray();
            Reset();
            harmony.Patch(SupplyChainObserver.Require(typeof(SettlementComponent), "ChangeGold", typeof(int)),
                prefix: new HarmonyMethod(typeof(SupplyMarketObserver), "Gold"));
            harmony.Patch(SupplyChainObserver.Require(typeof(Town), "GetItemPrice", typeof(EquipmentElement), typeof(MobileParty), typeof(bool)),
                postfix: new HarmonyMethod(typeof(SupplyMarketObserver), "Price"));
            harmony.Patch(SupplyChainObserver.Require(typeof(TownMarketData), "GetPrice", typeof(EquipmentElement), typeof(MobileParty), typeof(bool), typeof(PartyBase)),
                postfix: new HarmonyMethod(typeof(SupplyMarketObserver), "MarketPrice"));
            foreach (var method in targets)
                harmony.Patch(method, new HarmonyMethod(typeof(SupplyMarketObserver), "Before"), null, null,
                    new HarmonyMethod(typeof(SupplyMarketObserver), "After") { priority = Priority.Last });
        }
        private static void Gold(SettlementComponent __instance, int __0)
        {
            try
            {
                if (SupplyCapture.Active && _current != null && ReferenceEquals(__instance, _current.Town))
                    Emit(_current, "MARKET_GOLD_CALL", "native_ChangeGold_argument", 0, __0);
            }
            catch (Exception ex) { SupplyCapture.Fail("market gold tap", ex); }
        }
        private static void Price(Town __instance, EquipmentElement __0, int __result)
        {
            SupplyWoolSaleObserver.Price(__instance, __0, __result);
            if (_current != null && ReferenceEquals(__instance, _current.Town)) Quote(__0, __result, "town");
        }
        private static void MarketPrice(TownMarketData __instance, EquipmentElement __0, int __result)
        {
            if (_current != null && ReferenceEquals(__instance, _current.Town.MarketData)) Quote(__0, __result, "market");
        }
        private static void Quote(EquipmentElement item, int price, string layer)
        {
            try
            {
                if (SupplyCapture.Active && _current != null && Items.Contains(item.Item.ItemCategory.StringId))
                    Emit(_current, "MARKET_QUOTE", layer + "/" + item.Item.StringId, 0, price);
            }
            catch (Exception ex) { SupplyCapture.Fail("market quote", ex); }
        }
        internal static void Provenance()
        {
            SupplyRewardObserver.Provenance();
            var models = Campaign.Current.Models;
            var modelMethods = models.SettlementEconomyModel.GetType().GetMethods()
                .Where(m => m.Name == "GetTownGoldChange" || m.Name == "GetSupplyDemandForCategory" || m.Name == "GetDailyDemandForCategory" || m.Name == "CalculateDailySettlementBudgetForItemCategory")
                .Concat(models.TradeItemPriceFactorModel.GetType().GetMethods().Where(m => m.Name == "GetBasePriceFactor"))
                .Concat(models.CaravanModel.GetType().GetMethods().Where(m => m.Name == "GetMaxGoldToSpendOnOneItemCategory"));
            foreach (var method in Targets().Concat(modelMethods).Concat(SupplyCaravanObserver.Targets()).Concat(SupplyCashObserver.Targets()).Concat(SupplyWorkshopObserver.Targets()).Concat(new[] { SupplyWoolSaleObserver.SellTarget(), SupplyWoolSaleObserver.LookupTarget() }).Distinct())
            {
                var info = Harmony.GetPatchInfo(method);
                var patches = info == null ? Enumerable.Empty<Patch>() : info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers);
                SupplyCapture.Write("MARKET_HOOK", 0, 0, method.DeclaringType.FullName, method.Name, 0, 0,
                    "mvid=" + method.Module.ModuleVersionId + "; patches=" + string.Join("|", patches.Select(p => p.owner + ":" + p.priority + ":" + p.PatchMethod.Name)));
            }
            SupplyCapture.Write("MARKET_MODELS", 0, 0, "campaign", "effective_types", 0, 0,
                "economy=" + models.SettlementEconomyModel.GetType().AssemblyQualifiedName
                + "; price=" + models.TradeItemPriceFactorModel.GetType().AssemblyQualifiedName
                + "; caravan=" + models.CaravanModel.GetType().AssemblyQualifiedName);
        }
        private static Dictionary<string, double> Values(Town town, MobileParty party)
        {
            var values = new Dictionary<string, double>();
            foreach (string item in Items) { values[item] = 0; values["party/" + item] = 0; }
            foreach (var e in town.Owner.ItemRoster)
                if (values.ContainsKey(e.EquipmentElement.Item.ItemCategory.StringId)) values[e.EquipmentElement.Item.ItemCategory.StringId] += e.Amount;
            if (party != null)
                foreach (var e in party.ItemRoster)
                {
                    string key = "party/" + e.EquipmentElement.Item.ItemCategory.StringId;
                    if (values.ContainsKey(key)) values[key] += e.Amount;
                }
            values["town_gold"] = town.Gold;
            values["trade_tax_accrued"] = town.TradeTaxAccumulated;
            if (party != null && party.IsCaravan) values["party_gold"] = party.PartyTradeGold;
            return values;
        }
        private static void Before(MethodBase __originalMethod, object[] __args, out Call __state)
        {
            __state = null;
            if (!SupplyCapture.Active) return;
            try
            {
                Town town = __args.OfType<Town>().FirstOrDefault();
                MobileParty party = null;
                string kind = __originalMethod.Name;
                string category = null;
                if (kind == "ApplyInternal")
                {
                    var seller = (PartyBase)__args[0];
                    var buyer = (PartyBase)__args[1];
                    // Only actual town endpoints. Village price reference towns
                    // are not inventory endpoints and must not be mislabeled.
                    town = seller?.Settlement?.Town ?? buyer?.Settlement?.Town;
                    party = buyer?.MobileParty ?? seller?.MobileParty;
                    if (town == null || party == null) return;
                    var item = ((ItemRosterElement)__args[2]).EquipmentElement.Item;
                    if (!Items.Contains(item.ItemCategory.StringId)) return;
                    category = item.ItemCategory.StringId;
                    kind = (party.IsCaravan ? "caravan_" : "other_party_") + (seller?.Settlement?.Town == town ? "export" : "import");
                }
                if (town == null) return;
                var call = new Call { Previous = _current, Id = ++_next, Town = town, Party = party, Kind = kind, Category = category, Before = Values(town, party) };
                __state = call;
                _current = call;
                if (category != null) call.Purpose = CashPurposeContext.Begin("market_trade");
                Emit(call, "MARKET_BEGIN", kind, 0, 0);
                State(call, "before");
            }
            catch (Exception ex) { SupplyCapture.Fail("market begin", ex); }
        }
        private static void After(Call __state, Exception __exception, bool __runOriginal)
        {
            if (__state == null) return;
            try
            {
                CashPurposeContext.End(__state.Purpose, __runOriginal, __exception);
                if (__exception != null) { SupplyCapture.Fail("native market operation", __exception); return; }
                if (!SupplyCapture.Active) return;
                var after = Values(__state.Town, __state.Party);
                foreach (var pair in __state.Before)
                    if (after[pair.Key] != pair.Value) Emit(__state, "MARKET_DELTA", pair.Key, pair.Value, after[pair.Key]);
                State(__state, "after");
                Emit(__state, "MARKET_END", __state.Kind, 0, 0);
            }
            catch (Exception ex) { SupplyCapture.Fail("market end", ex); }
            finally { CashPurposeContext.Restore(__state.Purpose); _current = __state.Previous; }
        }
        private static void State(Call call, string phase)
        {
            foreach (var category in ItemCategories.All)
            {
                if (!Items.Contains(category.StringId)) continue;
                if (call.Category != null && category.StringId != call.Category) continue;
                var data = call.Town.MarketData.GetCategoryData(category);
                SupplyCapture.Write("MARKET_STATE", 0, 0, call.Town.StringId, category.StringId, data.Supply, data.Demand,
                    Detail(call) + "; phase=" + phase + "; stockValue=" + SupplyCapture.N(data.InStoreValue) + "; stock=" + data.InStore);
            }
        }
        private static string Detail(Call call)
        {
            return "market=" + call.Id + "; parentMarket=" + (call.Previous == null ? 0 : call.Previous.Id)
                + "; settlement=" + call.Town.Settlement.StringId
                + "; operation=" + call.Kind + "; party=" + (call.Party == null ? "none" : call.Party.StringId)
                + "; partyIdentity=" + SupplyCaravanObserver.PartyKey(call.Party)
                + "; cash_is_endpoint_delta_not_tax_adjusted_conservation";
        }
        private static void Emit(Call call, string kind, string metric, double before, double after)
        { SupplyCapture.Write(kind, 0, 0, call.Town.StringId, metric, before, after, Detail(call)); }
    }
}
