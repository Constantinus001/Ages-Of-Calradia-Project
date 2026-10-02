using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Native 1.4.8 MapEvent allocation boundaries BEFORE CommitGoldChanges.
    // Diagnostic-only prefix/finalizer: observe allocations, never change rewards.
    // Actual active ShipCostModel results are tapped, never recomputed. Exact
    // signatures fail installation on API drift. Native exceptions are preserved;
    // observation exceptions close capture. Verify-BattleAllocation.ps1 covers
    // bindings, scopes, weak identity, joins and unchanged evaluated results.
    internal static class SupplyBattleAllocationObserver
    {
        internal sealed class Call
        {
            internal Call Previous;
            internal string Id, Stage;
            internal long Event;
            internal MapEventParty[] Winners, Losers;
            internal Ship[] Ships;
        }
        private sealed class Link { internal long Event; }
        [ThreadStatic] private static Call _current;
        internal static bool HasOpenScope { get { return _current != null; } }
        internal static string Context { get { return _current == null ? "; allocation=none"
            : "; allocation=" + _current.Id + "; mapEvent=" + _current.Event; } }
        private static EconomyObjectIdentity _events = new EconomyObjectIdentity(), _parties = new EconomyObjectIdentity();
        private static ConditionalWeakTable<MapEventParty, Link> _links = new ConditionalWeakTable<MapEventParty, Link>();
        internal static MethodInfo[] ModelTargets { get; private set; } = new MethodInfo[0];
        internal static void Reset()
        {
            _current = null; _events = new EconomyObjectIdentity(); _parties = new EconomyObjectIdentity();
            _links = new ConditionalWeakTable<MapEventParty, Link>(); ModelTargets = new MethodInfo[0];
        }
        internal static string PartyContext(MapEventParty party)
        {
            if (party == null) return string.Empty;
            Link link;
            return "; mapParty=" + _parties.Get(party) + "; mapEvent="
                + (_links.TryGetValue(party, out link) ? link.Event.ToString() : "unobserved");
        }
        internal static MethodInfo[] Targets()
        {
            var args = new[] { typeof(MBReadOnlyList<MapEventParty>), typeof(MBReadOnlyList<MapEventParty>) };
            return new[] {
                SupplyChainObserver.Require(typeof(MapEvent), "CalculatePlunderedAndLostGoldAmounts", args),
                SupplyChainObserver.Require(typeof(MapEvent), "LootDefeatedPartyShips", args) };
        }
        internal static void Install(Harmony harmony)
        {
            Reset();
            // Callees first, then callers, to prevent a newly compiled wrapper
            // retaining an inlined model query without its observation postfix.
            if (Campaign.Current != null && AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "NavalDLC"))
            {
                var model = Campaign.Current.Models.ShipCostModel;
                if (model == null) throw new MissingMemberException("Active ship cost model missing for allocation capture");
                ModelTargets = CostTargets(model.GetType());
                foreach (var method in ModelTargets)
                {
                    harmony.Patch(method, postfix: new HarmonyMethod(typeof(SupplyBattleAllocationObserver),
                        method.Name == "GetShipTradeValue" ? "Value" : "Penalty") { priority = Priority.Last });
                }
            }
            foreach (var method in Targets())
            {
                if (method.ReturnType != typeof(void)) throw new InvalidOperationException("Battle allocation signature changed");
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(SupplyBattleAllocationObserver), "Before"),
                    finalizer: new HarmonyMethod(typeof(SupplyBattleAllocationObserver), "After") { priority = Priority.Last });
                SoakLog.Write("SUPPLY_ALLOCATION_HOOK", method + "; mvid=" + method.Module.ModuleVersionId);
            }
        }
        internal static MethodInfo[] CostTargets(Type type)
        {
            var targets = new[] {
                SupplyChainObserver.Require(type, "GetShipTradeValue", typeof(Ship), typeof(PartyBase), typeof(PartyBase)),
                SupplyChainObserver.Require(type, "GetShipSellingPenalty") };
            if (targets.Any(m => m.ReturnType != typeof(float) || m.IsStatic || m.IsAbstract))
                throw new InvalidOperationException("Concrete ship allocation value signature changed");
            return targets;
        }
        private static void Before(MapEvent __instance, MBReadOnlyList<MapEventParty> __0,
            MBReadOnlyList<MapEventParty> __1, MethodBase __originalMethod, out Call __state)
        {
            __state = null;
            if (!SupplyCapture.Active) return;
            try
            {
                __state = new Call { Id = Guid.NewGuid().ToString("N"), Event = _events.Get(__instance),
                    Stage = __originalMethod.Name, Winners = __0.ToArray(), Losers = __1.ToArray(), Previous = _current };
                _current = __state;
                foreach (var party in __state.Winners.Concat(__state.Losers))
                    _links.GetValue(party, _ => new Link()).Event = __state.Event;
                __state.Ships = __state.Winners.Concat(__state.Losers).SelectMany(p => p.Ships).Distinct().ToArray();
                Write("BATTLE_ALLOCATION_BEGIN", __state, "scope", 0, 0, "allocations_not_cash");
                Snapshot(__state, "before");
            }
            catch (Exception ex) { SupplyCapture.Fail("battle allocation before", ex); }
        }
        private static void Snapshot(Call call, string phase)
        {
            foreach (var party in call.Winners.Concat(call.Losers).Distinct())
            {
                Write("BATTLE_ALLOCATION_PARTY", call, phase, party.GoldLost, party.PlunderedGold,
                    "mapParty=" + _parties.Get(party) + "; party=" + PartyName(party.Party)
                    + "; role=" + (call.Winners.Contains(party) ? "winner" : "loser")
                    + "; before_is_allocated_loss; after_is_allocated_gain; not_cash");
            }
            foreach (var ship in call.Ships)
                Write("BATTLE_ALLOCATION_SHIP", call, phase, 0, 0,
                    "ship=" + SupplyRewardObserver.ShipId(ship) + "; ownerIdentity=" + SupplyShipLifecycleObserver.Owner(ship));
        }
        private static void Value(Ship __0, PartyBase __1, PartyBase __2, float __result, bool __runOriginal)
        {
            if (!SupplyCapture.Active || _current == null || _current.Stage != "LootDefeatedPartyShips") return;
            try { Write("BATTLE_ALLOCATION_VALUE", _current, "evaluated_trade_value", 0, __result,
                "ship=" + SupplyRewardObserver.ShipId(__0) + "; seller=" + PartyName(__1) + "; buyer=" + PartyName(__2)
                + "; originalRan=" + __runOriginal + "; evaluated_once; valuation_not_cash"); }
            catch (Exception ex) { SupplyCapture.Fail("battle ship value observation", ex); }
        }
        private static void Penalty(float __result, bool __runOriginal)
        {
            if (!SupplyCapture.Active || _current == null || _current.Stage != "LootDefeatedPartyShips") return;
            try { Write("BATTLE_ALLOCATION_PENALTY", _current, "evaluated_selling_penalty", 0, __result,
                "originalRan=" + __runOriginal + "; actual_result_not_recomputed"); }
            catch (Exception ex) { SupplyCapture.Fail("battle ship penalty observation", ex); }
        }
        private static void After(Call __state, Exception __exception, bool __runOriginal, MethodBase __originalMethod)
        {
            if (__state == null)
            {
                if (SupplyCapture.Active && !__runOriginal && __exception == null)
                    SupplyCapture.Write("BATTLE_ALLOCATION_SKIPPED", 0, 0, "unobserved_prefix", __originalMethod.Name,
                        0, 0, "originalRan=False; no_allocation_scope_opened");
                return;
            }
            try
            {
                if (__exception != null) { SupplyCapture.Fail("native battle allocation exception", __exception); return; }
                if (!SupplyCapture.Active) return;
                Snapshot(__state, "after");
                Write("BATTLE_ALLOCATION_END", __state, "scope", 0, 0, "originalRan=" + __runOriginal + "; allocation_not_cash");
            }
            catch (Exception ex) { SupplyCapture.Fail("battle allocation after", ex); }
            finally { _current = __state.Previous; }
        }
        private static void Write(string kind, Call call, string metric, double before, double after, string detail)
        {
            SupplyCapture.Write(kind, 0, 0, "mapEvent:" + call.Event, metric, before, after,
                "allocation=" + call.Id + "; mapEvent=" + call.Event + "; stage=" + call.Stage + "; " + detail);
        }
        // PartyBase.Id assumes a live mobile/settlement owner. Cleanup can detach
        // it; the weak mapParty identity above remains the authoritative join.
        private static string PartyName(PartyBase party)
        { return party?.MobileParty?.StringId ?? party?.Settlement?.StringId ?? "none_or_detached"; }
    }
}
