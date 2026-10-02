using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;

namespace AgesOfCalradia.SoakDiagnostics
{
    internal static class EconomyTransactionDiagnostics
    {
        internal sealed class Observation
        {
            internal string Owner, Metric, Kind;
            internal double Before;
            internal Func<double> Read;
        }
        internal sealed class Call
        {
            internal long Id, Parent;
            internal Call Previous;
            internal string Source, Detail, Kind;
            internal List<Observation> Observations = new List<Observation>();
            internal int FinanceIndex = -1;
            internal double FinanceBefore;
            internal bool Scope;
            internal bool Written;
            internal object[] Arguments;
            internal double FinanceAfter;
            internal Hero FinanceHero;
            internal double HeroBefore;
            internal EconomyRecipeDiagnostics.Run RecipeRun;
            internal Workshop Workshop;
            internal string Recipe;
            internal EconomyFlowDiagnostics.Transfer Transfer;
            internal bool AuthoritativeFinance;
            internal bool HasFinalFinance;
            internal double FinalFinance;
            internal long Started;
        }
        [ThreadStatic] private static Call _current;
        [ThreadStatic] private static bool _reading;
        private static bool _running;
        private static int _thread;
        private static EconomyLedger _ledger;
        private static EconomyObjectIdentity _partyIdentity = new EconomyObjectIdentity();
        private static readonly Dictionary<MethodBase, ParameterInfo[]> Parameters = new Dictionary<MethodBase, ParameterInfo[]>();
        private static ConditionalWeakTable<ItemRoster, RosterName> RosterNames = new ConditionalWeakTable<ItemRoster, RosterName>();
        private sealed class RosterName { internal string Name, Owner; internal bool Bound; }
        internal static bool Enabled
        {
            get
            {
                if (!_running || !EconomyTrace.Healthy || _reading) return false;
                if (System.Threading.Thread.CurrentThread.ManagedThreadId == _thread) return true;
                EconomyTrace.Fail("Economy mutation on unexpected thread; observer skipped unsafe native reads");
                return false;
            }
        }

        internal static void Start()
        {
            bool raw = System.IO.File.Exists(System.IO.Path.Combine(SoakLog.DirectoryPath, "AocEconomyTransactions.enabled"));
            string path = StartSession(SoakLog.DirectoryPath, () => Campaign.Current == null ? 0d : CampaignTime.Now.ToDays, !raw);
            SoakLog.Write("ECONOMY_SESSION", "schema=2; path=" + path + "; mode=" + (raw ? "opt-in transaction validation" : "bounded daily aggregates; not a transaction proof"));
            Snapshot();
        }
        internal static string BeginSession(string directory, Func<double> day)
        {
            return StartSession(directory, day, false);
        }
        internal static string StartShortCapture()
        {
            string path = StartSession(SoakLog.DirectoryPath, () => CampaignTime.Now.ToDays, false);
            Snapshot();
            return path;
        }
        private static string StartSession(string directory, Func<double> day, bool summary)
        {
            _current = null;
            EconomyStateDiagnostics.Reset();
            EconomyHealthDiagnostics.Reset();
            RosterNames = new ConditionalWeakTable<ItemRoster, RosterName>();
            _ledger = new EconomyLedger();
            _partyIdentity = new EconomyObjectIdentity();
            _thread = System.Threading.Thread.CurrentThread.ManagedThreadId;
            EconomyTrace.Day = day;
            string path = EconomyTrace.Start(directory, summary);
            _running = EconomyTrace.Healthy;
            EconomyTrace.Write("SESSION_START", 0, 0, "diagnostics", "schema2", 0, 0,
                typeof(EconomyTransactionDiagnostics).Assembly.ManifestModule.ModuleVersionId.ToString(), summary ? "aggregates; no transaction completeness claim" : "mutation ledger; not snapshot-only");
            if (Campaign.Current != null)
                EconomyTrace.Write("RUN_IDENTITY", 0, 0, Campaign.Current.UniqueGameId, "campaign", 0, 0,
                    typeof(EconomyTransactionDiagnostics).Assembly.ManifestModule.ModuleVersionId.ToString(), "same campaign identity does not prove same starting save or settings");
            return path;
        }
        internal static void Stop()
        {
            if (!_running) return;
            _running = false;
            EconomyTrace.Coverage();
            EconomyTrace.Write("SESSION_END", 0, 0, "diagnostics", "flush", 0, 0, "normal-unload", "");
            EconomyTrace.Close();
        }

        internal static string Id(object value)
        {
            if (value == null) return "null";
            Hero hero = value as Hero; if (hero != null) return "hero:" + hero.StringId;
            Clan clan = value as Clan; if (clan != null) return "clan:" + clan.StringId;
            Kingdom kingdom = value as Kingdom; if (kingdom != null) return "kingdom:" + kingdom.StringId;
            MobileParty party = value as MobileParty; if (party != null) return "party:" + party.StringId + "/instance:" + _partyIdentity.Get(party);
            PartyBase partyBase = value as PartyBase;
            if (partyBase != null) return partyBase.MobileParty != null ? Id(partyBase.MobileParty) : Id(partyBase.Settlement);
            Settlement settlement = value as Settlement; if (settlement != null) return "settlement:" + settlement.StringId;
            SettlementComponent component = value as SettlementComponent; if (component != null) return Id(component.Settlement);
            Workshop workshop = value as Workshop;
            if (workshop != null) return Id(workshop.Settlement) + "/workshop:" + Array.IndexOf(workshop.Settlement.Town.Workshops, workshop);
            ItemRoster roster = value as ItemRoster;
            if (roster != null)
            {
                RosterName known;
                if (!RosterNames.TryGetValue(roster, out known))
                {
                    known = new RosterName { Name = "roster:" + EconomyTrace.NextId() };
                    RosterNames.Add(roster, known);
                }
                return known.Name;
            }
            ItemObject item = value as ItemObject; if (item != null) return item.StringId;
            ItemCategory category = value as ItemCategory; if (category != null) return category.StringId;
            return value.GetType().FullName;
        }

        private static string ItemKey(EquipmentElement element)
        {
            return "item:" + (element.Item == null ? "null" : element.Item.StringId) + "/modifier:" + (element.ItemModifier == null ? "none" : element.ItemModifier.StringId);
        }
        private static int Count(ItemRoster roster, EquipmentElement item)
        {
            int index = roster.FindIndexOfElement(item);
            return index < 0 ? 0 : roster.GetElementNumber(index);
        }
        private static void Add(Call call, string kind, object owner, string metric, Func<double> read)
        {
            call.Observations.Add(new Observation { Owner = Id(owner), Kind = kind, Metric = metric, Before = read(), Read = read });
        }

        internal static void Before(object __instance, MethodBase __originalMethod, object[] __args, out Call __state)
        {
            __state = null;
            if (!Enabled) return;
            long captureStart = Stopwatch.GetTimestamp();
            try
            {
                ParameterInfo[] parameters;
                if (!Parameters.TryGetValue(__originalMethod, out parameters)) Parameters.Add(__originalMethod, parameters = __originalMethod.GetParameters());
                for (int i = 0; i < parameters.Length; i++)
                    if (parameters[i].Name == "applyWithdrawals" && !((bool)__args[i])) return; // Never call a mutating model to manufacture proof.
                Call call = new Call { Id = EconomyTrace.NextId(), Parent = _current == null ? 0 : _current.Id,
                    Previous = _current, Source = __originalMethod.DeclaringType.FullName + "." + __originalMethod.Name,
                    Detail = Arguments(parameters, __args), Kind = "TRANSACTION", Scope = true };
                call.Arguments = __args;
                string name = __originalMethod.Name;
                if (EconomyCallerDiagnostics.ShouldCapture(EconomyTrace.RawEnabled, call.Previous == null, name))
                    call.Detail += "; rootCallerStack=" + EconomyCallerDiagnostics.Capture() + "; caller_frames_may_be_inlined";
                call.Started = Stopwatch.GetTimestamp();
                if (call.Source == "TaleWorlds.CampaignSystem.Actions.GiveGoldAction.ApplyInternal") call.Transfer = EconomyFlowDiagnostics.Begin(__args);
                call.AuthoritativeFinance = name == "CalculateClanGoldChange" && Campaign.Current != null
                    && ReferenceEquals(__instance, Campaign.Current.Models.ClanFinanceModel);
                if (name == "DailyTickClan" && __args[0] is Clan)
                {
                    call.FinanceHero = ((Clan)__args[0]).Leader;
                    if (call.FinanceHero != null) call.HeroBefore = call.FinanceHero.Gold;
                }
                Hero hero = __instance as Hero;
                MobileParty party = __instance as MobileParty;
                Workshop workshop = __instance as Workshop;
                call.Workshop = workshop;
                SettlementComponent component = __instance as SettlementComponent;
                Fief fief = __instance as Fief;
                ItemRoster roster = __instance as ItemRoster;
                if (name == "set_Gold" && hero != null) { Add(call, "HERO_GOLD", hero, "gold", () => hero.Gold); call.Scope = false; }
                else if (name == "set_PartyTradeGold" && party != null)
                {
                    // Lord party wallet aliases leader gold: only the nested Hero setter is money.
                    if (!(party.IsLordParty && party.LeaderHero != null)) Add(call, "PARTY_GOLD", party, "gold", () => party.PartyTradeGold);
                    call.Scope = false;
                }
                else if (workshop != null && (name == "ChangeGold" || name == "InitializeWorkshop" || name == "ChangeOwnerOfWorkshop"))
                { Add(call, "WORKSHOP_GOLD", workshop, "gold", () => workshop.Capital); call.Scope = name != "ChangeGold"; }
                else if (name == "ChangeGold" && component != null) { Add(call, "SETTLEMENT_GOLD", component, "gold", () => component.Gold); call.Scope = false; }
                else if (name == "set_FoodStocks" && fief != null) { Add(call, "FOOD_STOCK", fief, "food", () => fief.FoodStocks); call.Scope = false; }
                else if (name.StartsWith("set_", StringComparison.Ordinal))
                {
                    PropertyInfo property = __originalMethod.DeclaringType.GetProperty(name.Substring(4));
                    Add(call, "WALLET", __instance, name.Substring(4), () => Convert.ToDouble(property.GetValue(__instance), CultureInfo.InvariantCulture));
                    call.Scope = false;
                }
                else if (roster != null)
                {
                    if (name == "AddToCounts")
                    {
                        EquipmentElement item = (EquipmentElement)__args[0];
                        Add(call, "INVENTORY", roster, ItemKey(item), () => Count(roster, item));
                    }
                    else foreach (ItemRosterElement entry in roster)
                    {
                        EquipmentElement item = entry.EquipmentElement;
                        Add(call, "INVENTORY", roster, ItemKey(item), () => Count(roster, item));
                    }
                    call.Scope = false;
                }
                for (int i = 0; i < __args.Length; i++)
                {
                    if (__args[i] is ExplainedNumber) { call.FinanceIndex = i; call.FinanceBefore = ((ExplainedNumber)__args[i]).ResultNumber; call.FinanceAfter = call.FinanceBefore; }
                }
                if (call.Source.Contains("Tribute")) call.Kind = "TRIBUTE_COMPONENT";
                else if (call.Source.Contains("WorkshopsCampaignBehavior")) call.Kind = "WORKSHOP_FLOW";
                else if (call.FinanceIndex >= 0) call.Kind = "FINANCE_COMPONENT";
                __state = call;
                EconomyRecipeDiagnostics.Before(call, name);
                _current = call;
            }
            catch (Exception ex) { EconomyTrace.Fail("capture: " + ex); }
            finally { EconomyHealthDiagnostics.Record(captureStart); }
        }

        // Harmony keys __state by patch declaring type. Keep all entry points
        // with Before; helpers may live elsewhere, but cannot receive state directly.
        internal static void BooleanResult(bool __result, Call __state) { EconomyRecipeDiagnostics.BooleanResult(__result, __state); }
        internal static void SpeedResult(ExplainedNumber __result, Call __state) { EconomyRecipeDiagnostics.SpeedResult(__result, __state); }
        internal static void DemandResult(float __result, Call __state) { EconomyRecipeDiagnostics.DemandResult(__result, __state); }

        // Finalizer is void: observe exceptions, do not suppress/replace them or original results.
        internal static void FinanceAfter(object __instance, ExplainedNumber goldChange, Call __state, Exception __exception)
        {
            if (__state != null) __state.FinanceAfter = goldChange.ResultNumber;
            After(__instance, __state, __exception);
        }

        internal static void After(object __instance, Call __state, Exception __exception)
        {
            if (__state == null) return;
            long completionStart = Stopwatch.GetTimestamp();
            try
            {
                foreach (Observation observation in __state.Observations)
                {
                    double after = observation.Read();
                    double residual = _ledger.Observe(observation.Owner + "|" + observation.Metric, observation.Before, after);
                    if (Math.Abs(residual) > 0.001) Gap(observation.Owner, observation.Metric, residual);
                    if (Math.Abs(after - observation.Before) > 0.001 || Math.Abs(residual) > 0.001 || __exception != null)
                    {
                        Open(__state);
                        EconomyTrace.Write(observation.Kind, __state.Id, __state.Parent, observation.Owner, observation.Metric,
                            observation.Before, after, Attribution(__state),
                            __state.Detail + "; nativeTarget=" + __state.Source + "; nativeException=" + (__exception == null ? "none" : __exception.GetType().FullName));
                        if (observation.Kind == "INVENTORY")
                        {
                            RosterName rosterName;
                            string owner = __instance is ItemRoster && RosterNames.TryGetValue((ItemRoster)__instance, out rosterName) ? rosterName.Owner : null;
                            EconomyTrace.Write("RESOURCE_FLOW", __state.Id, __state.Parent,
                                __state.Workshop != null ? Id(__state.Workshop) : owner ?? "unbound-roster", observation.Metric, observation.Before, after,
                                Attribution(__state), "roster=" + observation.Owner + "; " + __state.Recipe + "; observed_inventory_delta_not_recipe_prediction");
                        }
                        if (observation.Kind == "WALLET" && observation.Metric == "KingdomBudgetWallet")
                            EconomyTrace.Write("BUDGET_FLOW", __state.Id, __state.Parent, observation.Owner,
                                __state.Previous != null && __state.Previous.Source.EndsWith(".DailyTickClan", StringComparison.Ordinal) ? "outside_finance_topup_candidate" : "nested_wallet_change",
                                observation.Before, after, Attribution(__state), "observed; classify using parent scope, not assumed income");
                    }
                }
                if (__state.Scope && (Math.Abs(__state.FinanceAfter - __state.FinanceBefore) > 0.001 || __exception != null))
                {
                    Open(__state);
                    double after = __state.FinanceAfter;
                    EconomyTrace.Write(__state.Kind, __state.Id, __state.Parent, Id(__state.Arguments.FirstOrDefault()), "component_not_cash",
                        __state.FinanceBefore, after, __state.Source, __state.Detail + "; cashEvidence=child balance rows; nativeException=" + (__exception == null ? "none" : __exception.GetType().FullName));
                }
                if (__state.FinanceHero != null)
                {
                    Open(__state);
                    EconomyTrace.Write("CLAN_SETTLEMENT", __state.Id, __state.Parent, Id(__state.FinanceHero), "actual_daily_net_not_additive",
                        __state.HeroBefore, __state.FinanceHero.Gold, __state.Source, "reconcile child finance components, transfers, and clamping; do not double-count with HERO_GOLD");
                }
                EconomyRecipeDiagnostics.After(__state, __exception);
                EconomyFlowDiagnostics.End(__state);
                if (__state.RecipeRun != null || __state.FinanceHero != null)
                {
                    Open(__state);
                    EconomyTrace.Write("NATIVE_SCOPE_TIMING", __state.Id, __state.Parent, "campaign", "inclusive_wall_ms", 0,
                        (Stopwatch.GetTimestamp() - __state.Started) * 1000d / Stopwatch.Frequency, __state.Source, "includes_native_work_and_observer; not_isolated_overhead");
                }
                if (__state.Written) EconomyTrace.Write("END", __state.Id, __state.Parent, Id(__instance), "scope", 0, 0, __state.Source, "");
                if (__exception != null) EconomyTrace.Fail("native exception in observed call: " + __state.Source + ": " + __exception);
            }
            catch (Exception ex) { EconomyTrace.Fail("completion: " + ex); }
            finally { _current = __state.Previous; EconomyHealthDiagnostics.Record(completionStart); }
        }

        internal static void WageResult(int __result, Call __state)
        {
            if (__state == null || __result == 0) return;
            Open(__state);
            EconomyTrace.Write("WAGE_ASSESSMENT", __state.Id, __state.Parent, Id(__state.Arguments[0]), "authorized_not_cash", 0, __result,
                __state.Source, __state.Detail + "; actual debit must reconcile with parent daily finance transaction");
        }

        private static void Gap(string owner, string metric, double residual)
        {
            EconomyTrace.Write("UNEXPLAINED_DELTA", 0, 0, owner, metric, 0, residual, "reconciliation", "coverage incomplete; do not infer transaction cause");
        }
        private static string Attribution(Call call)
        {
            var names = new List<string>();
            Call cursor = call.Previous;
            while (cursor != null && names.Count < 5) { names.Add(cursor.Source); cursor = cursor.Previous; }
            return names.Count == 0 ? call.Source + "/unattributed_root" : string.Join(" <- ", names);
        }
        private static string Arguments(ParameterInfo[] parameters, object[] args)
        {
            return string.Join("; ", parameters.Select((p, i) => p.Name + "=" +
                (args[i] == null ? "null" : args[i] is bool || args[i] is int || args[i] is float || args[i] is double
                    ? Convert.ToString(args[i], CultureInfo.InvariantCulture) : args[i] is EquipmentElement ? ItemKey((EquipmentElement)args[i])
                    : args[i] is ItemRosterElement ? ItemKey(((ItemRosterElement)args[i]).EquipmentElement) + "/amount:" + ((ItemRosterElement)args[i]).Amount : Id(args[i]))));
        }
        internal static void Open(Call call)
        {
            if (call == null || call.Written) return;
            Open(call.Previous);
            call.Written = true;
            EconomyTrace.Write("BEGIN", call.Id, call.Parent, "diagnostics", call.Kind, 0, 0, call.Source, call.Detail);
        }

        private static void Balance(string owner, string metric, double actual)
        {
            double residual = _ledger.Reconcile(owner + "|" + metric, actual);
            if (Math.Abs(residual) > 0.001) Gap(owner, metric, residual);
            if (Math.Abs(residual) > 0.001)
                EconomyTrace.Write("BALANCE", 0, 0, owner, metric, actual, actual, "snapshot", "reconciliation gap");
        }
        internal static void Roster(ItemRoster roster, string name)
        {
            if (roster == null) return;
            string id = Id(roster);
            RosterName rosterName;
            if (RosterNames.TryGetValue(roster, out rosterName) && !rosterName.Bound)
            {
                rosterName.Bound = true;
                rosterName.Owner = name;
                EconomyTrace.Write("ROSTER_BIND", 0, 0, id, "owner", 0, 0, name, "session-local roster; stable owner given by source");
            }
            var present = new HashSet<string>();
            foreach (ItemRosterElement entry in roster)
            {
                string key = ItemKey(entry.EquipmentElement);
                present.Add(id + "|" + key);
                Balance(id, key, entry.Amount);
            }
            foreach (string key in _ledger.Keys(id + "|item:"))
                if (!present.Contains(key)) Balance(id, key.Substring(id.Length + 1), 0);
        }

        internal static void Snapshot()
        {
            if (!Enabled) return;
            _reading = true;
            try
            {
                foreach (Hero hero in Hero.AllAliveHeroes) Balance(Id(hero), "gold", hero.Gold);
                foreach (MobileParty party in MobileParty.All)
                {
                    if (!(party.IsLordParty && party.LeaderHero != null)) Balance(Id(party), "gold", party.PartyTradeGold);
                    Roster(party.ItemRoster, Id(party) + "/inventory");
                }
                foreach (Settlement settlement in Settlement.All)
                {
                    if (settlement.SettlementComponent != null) Balance(Id(settlement), "gold", settlement.SettlementComponent.Gold);
                    Roster(settlement.ItemRoster, Id(settlement) + "/inventory");
                    Town town = settlement.Town;
                    if (town == null) continue;
                    Balance(Id(town), "food", town.FoodStocks);
                    ExplainedNumber food = Campaign.Current.Models.SettlementFoodModel.CalculateTownFoodStocksChange(town, true, true);
                    EconomyTrace.Write("FOOD_EXPLANATION", 0, 0, Id(town), "model_not_stock_delta", 0, food.ResultNumber,
                        Campaign.Current.Models.SettlementFoodModel.GetType().FullName,
                        string.Join("; ", food.GetLines().Select(line => line.Item1 + "=" + line.Item2.ToString("R", CultureInfo.InvariantCulture)))
                        + "; stock=" + town.FoodStocks.ToString(CultureInfo.InvariantCulture) + "; cap=" + town.FoodStocksUpperLimit());
                    foreach (Workshop shop in town.Workshops) Balance(Id(shop), "gold", shop.Capital);
                }
                foreach (Kingdom kingdom in Kingdom.All)
                {
                    Balance(Id(kingdom), "TributeWallet", kingdom.TributeWallet);
                    Balance(Id(kingdom), "KingdomBudgetWallet", kingdom.KingdomBudgetWallet);
                }
                foreach (Clan clan in Clan.All) { Balance(Id(clan), "TributeWallet", clan.TributeWallet); Balance(Id(clan), "DebtToKingdom", clan.DebtToKingdom); }
                EconomyStateDiagnostics.Snapshot();
                EconomyWarehouseDiagnostics.Snapshot();
                EconomyHealthDiagnostics.Snapshot();
                EconomyTrace.Coverage();
            }
            catch (Exception ex) { EconomyTrace.Fail("snapshot: " + ex); }
            finally { _reading = false; }
        }
    }
}
