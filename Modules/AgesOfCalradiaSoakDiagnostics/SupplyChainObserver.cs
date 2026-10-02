using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Diagnostics-only native 1.4.8 boundaries. Prefix/postfix/finalizer reads
    // and audited value taps; no argument/result writes or skipped originals.
    // All targets preflight before install. Failure invalidates capture, leaves
    // gameplay running, and logs errors. No repeated model or RNG invocation.
    internal static class SupplyChainObserver
    {
        private const string Owner = "aoc.soak.supply.observer.v1";
        private static Harmony _harmony;
        internal static MethodBase[] InstalledTargets { get; private set; } = new MethodBase[0];
        private static EconomyObjectIdentity _identity = new EconomyObjectIdentity();
        private static ConditionalWeakTable<MobileParty, Journey> _journeys = new ConditionalWeakTable<MobileParty, Journey>();
        private static long _sequence, _trip;
        [ThreadStatic] private static Call _current;
        private static string[] Items { get { return SupplyCategories.All; } }
        private sealed class Journey { internal long Id; internal double Loaded; }
        internal sealed class Call
        {
            internal Call Previous;
            internal long Id, Parent;
            internal string Name, Context;
            internal Village Village;
            internal MobileParty Party;
            internal Settlement Settlement;
            internal Dictionary<string, double> Before;
            internal int Stock, Threshold = -1;
            internal float Roll = float.NaN;
            internal bool LoadCalled;
        }

        internal static MethodInfo Require(Type type, string name, params Type[] arguments)
        {
            MethodInfo result = AccessTools.Method(type, name, arguments);
            if (result == null || result.GetMethodBody() == null) throw new MissingMethodException(type.FullName, name);
            return result;
        }
        internal static IEnumerable<MethodInfo> ScopeTargets()
        {
            var villagers = typeof(VillagerCampaignBehavior);
            yield return Require(villagers, "ThinkAboutSendingItemToTown", typeof(Village));
            yield return Require(villagers, "MoveItemsToVillagerParty", typeof(Village), typeof(MobileParty));
            yield return Require(villagers, "SendVillagerPartyToTradeBoundTown", typeof(MobileParty));
            yield return Require(villagers, "OnSettlementEntered", typeof(MobileParty), typeof(Settlement), typeof(Hero));
            yield return Require(villagers, "OnMobilePartyDestroyed", typeof(MobileParty), typeof(PartyBase));
            yield return Require(typeof(VillageGoodProductionCampaignBehavior), "TickProductions", typeof(Settlement), typeof(bool));
            yield return Require(typeof(VillageGoodProductionCampaignBehavior), "TickGoodProduction", typeof(Village), typeof(bool));
            // The public wrapper can already be inlined when a save loads. Observe
            // the actual transaction body, with its version-sensitive private enum.
            Type detail = typeof(SellGoodsForTradeAction).GetNestedType("SellGoodsForTradeActionDetail", BindingFlags.NonPublic);
            if (detail == null || !detail.IsEnum) throw new MissingMemberException("Native villager sale detail enum");
            if (Convert.ToInt32(Enum.Parse(detail, "VillagerTrade")) != 0)
                throw new InvalidOperationException("Native villager sale detail changed");
            yield return Require(typeof(SellGoodsForTradeAction), "ApplyInternal", typeof(Settlement), typeof(MobileParty), detail);
        }
        internal static void Install()
        {
            _identity = new EconomyObjectIdentity();
            _journeys = new ConditionalWeakTable<MobileParty, Journey>();
            _sequence = _trip = 0;
            _current = null;
            if (_harmony != null) return;
            var scopes = ScopeTargets().ToArray();
            MethodInfo capacity = Require(typeof(Village), "GetWarehouseCapacity");
            MethodInfo price = Require(typeof(Town), "GetItemPrice", typeof(EquipmentElement), typeof(MobileParty), typeof(bool));
            MethodInfo produced = Require(typeof(Campaign).Assembly.GetType("TaleWorlds.CampaignSystem.CampaignEventDispatcher", true),
                "OnItemProduced", typeof(ItemObject), typeof(Settlement), typeof(int));
            SupplyValueTaps.Validate();
            try
            {
                _harmony = new Harmony(Owner);
                SupplyWorkshopObserver.Install(_harmony);
                SupplyMarketObserver.Install(_harmony);
                SupplyTownCashObserver.Install(_harmony);
                SupplyWoolSaleObserver.Install(_harmony);
                SupplyCaravanObserver.Install(_harmony);
                SupplyCashObserver.Install(_harmony);
                SupplyRewardObserver.Install(_harmony);
                SupplyBattleAllocationObserver.Install(_harmony);
                SupplyShipLifecycleObserver.Install(_harmony);
                QuestLifecycleObserver.Install(_harmony);
                LogisticsLifecycleObserver.Install(_harmony);
                NavalSaleObserver.Install(_harmony);
                // Patch evaluated-value callees before rebuilding their callers.
                _harmony.Patch(capacity, postfix: new HarmonyMethod(typeof(SupplyChainObserver), "Capacity") { priority = Priority.Last });
                _harmony.Patch(price, postfix: new HarmonyMethod(typeof(SupplyChainObserver), "Price") { priority = Priority.Last });
                _harmony.Patch(produced, prefix: new HarmonyMethod(typeof(SupplyChainObserver), "Produced"));
                foreach (MethodInfo method in scopes)
                {
                    HarmonyMethod tap = null;
                    if (method.Name == "ThinkAboutSendingItemToTown") tap = new HarmonyMethod(typeof(SupplyValueTaps), "Dispatch");
                    if (method.Name == "MoveItemsToVillagerParty") tap = new HarmonyMethod(typeof(SupplyValueTaps), "Load");
                    _harmony.Patch(method, new HarmonyMethod(typeof(SupplyChainObserver), "Before"), null, tap,
                        new HarmonyMethod(typeof(SupplyChainObserver), "After") { priority = Priority.Last });
                }
                foreach (MethodInfo method in scopes.Concat(new[] { capacity, price, produced }))
                    SoakLog.Write("SUPPLY_HOOK", method.DeclaringType.FullName + "." + method + "; mvid=" + method.Module.ModuleVersionId
                        + "; otherOwners=" + string.Join(",", Harmony.GetPatchInfo(method).Owners.Where(x => x != Owner)));
                InstalledTargets = Harmony.GetAllPatchedMethods()
                    .Where(m => Harmony.GetPatchInfo(m).Owners.Contains(Owner)).ToArray();
            }
            catch { Uninstall(); throw; } // Caller owns logged reflection/Harmony boundary.
        }
        internal static void Uninstall()
        {
            NavalSaleObserver.Reset();
            SupplyTownCashObserver.Reset();
            QuestLifecycleObserver.Reset();
            LogisticsLifecycleObserver.Reset();
            try { if (_harmony != null) _harmony.UnpatchAll(Owner); }
            catch (Exception ex) { SoakLog.Write("SUPPLY_CAPTURE_FAILURE", "unpatch: " + ex); }
            finally { _harmony = null; InstalledTargets = new MethodBase[0]; _current = null; SupplyWorkshopObserver.Reset(); SupplyMarketObserver.Reset(); SupplyWoolSaleObserver.Reset(); SupplyCaravanObserver.Reset(); SupplyCashObserver.Reset(); SupplyRewardObserver.Reset(); SupplyBattleAllocationObserver.Reset(); }
        }

        private static void Before(MethodBase __originalMethod, object[] __args, out Call __state)
        {
            __state = null;
            if (!SupplyCapture.Active) return;
            Call call = null;
            try
            {
                var party = __args.OfType<MobileParty>().FirstOrDefault();
                var settlement = __args.OfType<Settlement>().FirstOrDefault();
                var village = __args.OfType<Village>().FirstOrDefault() ?? (settlement == null ? null : settlement.Village);
                if (party != null && !party.IsVillager) return;
                if (__originalMethod.Name == "ApplyInternal" && Convert.ToInt32(__args[2]) != 0) return;
                if (village == null && party != null) village = party.HomeSettlement == null ? null : party.HomeSettlement.Village;
                if (village == null) return;
                party = party ?? village.VillagerPartyComponent?.MobileParty;
                call = new Call { Previous = _current, Parent = _current == null ? 0 : _current.Id, Id = ++_sequence,
                    Name = __originalMethod.Name, Village = village, Party = party, Settlement = settlement };
                call.Stock = Total(village.Settlement.ItemRoster);
                call.Context = Context(call);
                if (call.Name != "ThinkAboutSendingItemToTown") call.Before = Values(call);
                _current = call;
                __state = call;
                if (call.Name == "MoveItemsToVillagerParty")
                {
                    for (Call parent = call.Previous; parent != null; parent = parent.Previous)
                        if (parent.Name == "ThinkAboutSendingItemToTown") parent.LoadCalled = true;
                    if (party != null) { _journeys.Remove(party); _journeys.Add(party, new Journey { Id = ++_trip, Loaded = CampaignTime.Now.ToDays }); }
                }
                Emit(call, "BEGIN", call.Name, 0, 0, call.Name == "ThinkAboutSendingItemToTown" ? "" : call.Context + "; " + Trip(party));
            }
            catch (Exception ex)
            {
                if (call != null) _current = call.Previous;
                __state = null;
                SupplyCapture.Fail("scope begin", ex);
            }
        }

        private static void After(Call __state, Exception __exception)
        {
            if (__state == null) return;
            Call call = __state;
            try
            {
                if (__exception != null) { SupplyCapture.Fail("native " + call.Name, __exception); return; }
                if (!SupplyCapture.Active) return;
                if (call.Before != null)
                {
                    var after = Values(call);
                    foreach (var pair in call.Before)
                        if (pair.Value != after[pair.Key])
                            Emit(call, "SCOPE_BALANCE", pair.Key, pair.Value, after[pair.Key], "delta; nested scopes may overlap");
                    if (call.Name == "MoveItemsToVillagerParty" || call.Name == "ApplyInternal")
                    {
                        string from = call.Name == "MoveItemsToVillagerParty" ? "village/" : "town/";
                        foreach (string item in Items)
                        {
                            double residual = (after[from + item] - call.Before[from + item]) + (after["party/" + item] - call.Before["party/" + item]);
                            Emit(call, "TRANSFER_CHECK", item, 0, residual, "observed endpoint sum; nonzero invalidates transfer attribution");
                        }
                        if (call.Name == "ApplyInternal")
                            Emit(call, "CASH_CHECK", "town_plus_party", 0,
                                after["town/gold"] - call.Before["town/gold"] + after["party/gold"] - call.Before["party/gold"], Trip(call.Party));
                    }
                }
                if (call.Name == "ThinkAboutSendingItemToTown")
                    Emit(call, "DISPATCH_DECISION", Decision(call.Roll, call.Stock, call.Threshold, call.LoadCalled), call.Stock, call.Threshold,
                        "roll=" + SupplyCapture.N(call.Roll) + (call.Roll >= 0.15f ? "" : "; at_entry=" + call.Context));
                Emit(call, "END", call.Name, 0, 0, call.Name == "ThinkAboutSendingItemToTown" ? "" : Context(call) + "; " + Trip(call.Party));
            }
            catch (Exception ex) { SupplyCapture.Fail("scope end", ex); }
            finally { _current = call.Previous; }
        }
        internal static string Decision(float roll, int stock, int threshold, bool loaded)
        {
            if (loaded) return "load_called_not_arrival";
            if (float.IsNaN(roll)) return "unobserved_gate";
            if (roll >= 0.15f) return "random_gate";
            if (threshold < 0) return "prethreshold_party_or_battle_gate";
            if (stock < threshold) return "below_dispatch_threshold";
            return "threshold_met_no_load_inspect_party_state";
        }
        internal static void Roll(float value) { if (_current != null && SupplyCapture.Active) _current.Roll = value; }
        internal static void LoadCapacity(int value) { Tap("native_inventory_capacity", value); }
        internal static void LoadWeight(float value) { Tap("native_weight_carried", value); }
        private static void Tap(string name, double value)
        {
            try { if (_current != null) Emit(_current, "NATIVE_VALUE", name, 0, value, "already evaluated by native; not queried again"); }
            catch (Exception ex) { SupplyCapture.Fail("value tap", ex); }
        }
        private static void Capacity(Village __instance, int __result)
        {
            try
            {
                if (!SupplyCapture.Active || _current == null || _current.Village != __instance) return;
                if (_current.Name == "ThinkAboutSendingItemToTown") _current.Threshold = __result;
                Emit(_current, "WAREHOUSE_THRESHOLD", _current.Name, _current.Stock, __result, "native result; production ceiling is 1.5 times capacity");
            }
            catch (Exception ex) { SupplyCapture.Fail("capacity", ex); }
        }
        private static void Price(EquipmentElement __0, int __result, Town __instance)
        {
            try
            {
                if (!SupplyCapture.Active || _current == null || _current.Name != "ApplyInternal") return;
                int available = _current.Party == null ? 0 : _current.Party.ItemRoster.Where(x => x.EquipmentElement.Equals(__0)).Sum(x => x.Amount);
                Emit(_current, "SALE_QUOTE", __0.Item.StringId, 0, __result, "townGoldAtQuote=" + __instance.Gold
                    + "; cargoAtQuote=" + available + "; category=" + __0.Item.ItemCategory.StringId
                    + "; actual native quote; reserved pack animals may be withheld; no new price query");
            }
            catch (Exception ex) { SupplyCapture.Fail("sale quote", ex); }
        }
        private static void Produced(ItemObject __0, Settlement __1, int __2)
        {
            try
            {
                if (!SupplyCapture.Active || __1 == null || !__1.IsVillage || !Items.Contains(__0.ItemCategory.StringId)) return;
                SupplyCapture.Write("ITEM_PRODUCED", _current == null ? 0 : _current.Id, _current == null ? 0 : _current.Parent,
                    __1.StringId, __0.ItemCategory.StringId, 0, __2, "native production event after roster addition; item=" + __0.StringId);
            }
            catch (Exception ex) { SupplyCapture.Fail("production event", ex); }
        }

        private static string PartyId(MobileParty party) { return party == null ? "none" : party.StringId + "/instance:" + _identity.Get(party); }
        private static string Trip(MobileParty party)
        {
            Journey trip;
            return party != null && _journeys.TryGetValue(party, out trip)
                ? "trip=" + trip.Id + "; sinceLoadDays=" + SupplyCapture.N(CampaignTime.Now.ToDays - trip.Loaded)
                : "trip=unknown_preexisting_or_unobserved";
        }
        private static string Context(Call call)
        {
            MobileParty p = call.Party;
            return "village=" + call.Village.Settlement.StringId + "; villageState=" + call.Village.VillageState
                + "; deserted=" + call.Village.IsDeserted
                + "; villageBattle=" + (call.Village.Owner.MapEvent != null) + "; hearth=" + SupplyCapture.N(call.Village.Hearth)
                + "; tradeBound=" + (call.Village.TradeBound == null ? "none" : call.Village.TradeBound.StringId)
                + "; tradeBoundSiege=" + (call.Village.TradeBound != null && call.Village.TradeBound.IsUnderSiege)
                + "; party=" + PartyId(p) + (p == null ? "" : "; current=" + (p.CurrentSettlement == null ? "outside" : p.CurrentSettlement.StringId)
                + "; target=" + (p.TargetSettlement == null ? "none" : p.TargetSettlement.StringId) + "; behavior=" + p.DefaultBehavior
                + "; partyBattle=" + (p.MapEvent != null) + "; raft=" + p.IsInRaftState)
                + "; entered=" + (call.Settlement == null ? "none" : call.Settlement.StringId);
        }
        private static void Emit(Call call, string kind, string metric, double before, double after, string detail)
        {
            SupplyCapture.Write(kind, call.Id, call.Parent, call.Village.Settlement.StringId, metric, before, after, detail);
        }
        private static int Total(ItemRoster roster) { return roster.Sum(x => x.Amount); }
        private static void Inventory(Dictionary<string, double> result, string prefix, ItemRoster roster)
        {
            foreach (string item in Items) result[prefix + item] = 0;
            result[prefix + "total_all"] = 0;
            if (roster == null) return;
            foreach (ItemRosterElement entry in roster)
            {
                string key = prefix + entry.EquipmentElement.Item.ItemCategory.StringId;
                if (result.ContainsKey(key)) result[key] += entry.Amount;
                result[prefix + "total_all"] += entry.Amount;
            }
        }
        private static Dictionary<string, double> Values(Call call)
        {
            var result = new Dictionary<string, double>();
            Inventory(result, "village/", call.Village.Settlement.ItemRoster);
            Inventory(result, "party/", call.Party == null ? null : call.Party.ItemRoster);
            Town town = call.Settlement == null ? null : call.Settlement.Town;
            Inventory(result, "town/", town == null ? null : town.Settlement.ItemRoster);
            result["town/gold"] = town == null ? 0 : town.Gold;
            result["party/gold"] = call.Party == null ? 0 : call.Party.PartyTradeGold;
            return result;
        }
        internal static void Snapshot()
        {
            if (!SupplyCapture.Active) return;
            using (DiagnosticObserverCost.Measure("combined_stock_cash_workshop_snapshot"))
                SnapshotMeasured();
        }
        private static void SnapshotMeasured()
        {
            SupplyWorkshopObserver.Snapshot();
            SupplyCashObserver.Snapshot();
            SupplyCaravanObserver.Snapshot();
            try
            {
                foreach (Settlement settlement in Settlement.All)
                {
                    if (!settlement.IsVillage && !settlement.IsTown) continue;
                    var values = new Dictionary<string, double>();
                    Inventory(values, "", settlement.ItemRoster);
                    foreach (var pair in values) SupplyCapture.Write("STOCK_SNAPSHOT", 0, 0, settlement.StringId, pair.Key, 0, pair.Value,
                        "settlementKind=" + (settlement.IsTown ? "town" : "village") + "; sampled stock not production");
                }
                foreach (MobileParty party in MobileParty.All)
                {
                    if (!party.IsVillager || party.HomeSettlement == null || party.HomeSettlement.Village == null) continue;
                    var call = new Call { Party = party, Village = party.HomeSettlement.Village };
                    var cargo = new Dictionary<string, double>();
                    Inventory(cargo, "", party.ItemRoster);
                    SupplyCapture.Write("TRANSIT_SNAPSHOT", 0, 0, PartyId(party), "cargo", 0, cargo["total_all"],
                        Context(call) + "; " + Trip(party) + "; " + string.Join("; ", cargo.Select(x => x.Key + "=" + SupplyCapture.N(x.Value))));
                }
            }
            catch (Exception ex) { SupplyCapture.Fail("snapshot", ex); }
        }
    }
}
