using System;
using System.Globalization;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements.Workshops;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Native 1.4.8 RunTownWorkshop, cycle/predicate methods and model speed.
    // Read-only prefix/postfix/finalizer observations; never repeat model calls.
    // Native target resolution is mandatory; unexpected failures invalidate evidence.
    internal static class EconomyRecipeDiagnostics
    {
        internal sealed class Run
        {
            internal Workshop Shop;
            internal float[] Progress;
            internal int[] Attempts, Successes;
            internal string Type, Owner;
        }
        internal static void Before(EconomyTransactionDiagnostics.Call call, string name)
        {
            if (name == "DailyTickTown")
            {
                var town = (TaleWorlds.CampaignSystem.Settlements.Town)call.Arguments[0];
                EconomyTransactionDiagnostics.Open(call);
                EconomyTrace.Write("WORKSHOP_ELIGIBILITY", call.Id, call.Parent, EconomyTransactionDiagnostics.Id(town), town.InRebelliousState ? "rebellion_blocks_production" : "town_eligible", 0, 1,
                    call.Source, "native daily boundary; expenses may still run");
            }
            call.Workshop = call.Workshop ?? call.Arguments.OfType<Workshop>().FirstOrDefault();
            if (call.Workshop == null && call.Previous != null) call.Workshop = call.Previous.Workshop;
            if (name == "RunTownWorkshop" && call.Workshop != null)
            {
                Workshop s = call.Workshop;
                int n = s.WorkshopType.Productions.Count;
                call.RecipeRun = new Run { Shop = s, Progress = Enumerable.Range(0, n).Select(s.GetProductionProgress).ToArray(),
                    Attempts = new int[n], Successes = new int[n], Type = s.WorkshopType.StringId, Owner = EconomyTransactionDiagnostics.Id(s.Owner) };
            }
            foreach (object arg in call.Arguments)
            {
                if (!(arg is WorkshopType.Production)) continue;
                var p = (WorkshopType.Production)arg;
                int index = call.Workshop == null ? -1 : call.Workshop.WorkshopType.Productions.IndexOf(p);
                call.Recipe = "recipe=" + index + "; baseSpeed=" + N(p.ConversionSpeed)
                    + "; inputs=" + string.Join(",", p.Inputs.Select(x => x.Item1.StringId + ":" + x.Item2))
                    + "; outputs=" + string.Join(",", p.Outputs.Select(x => x.Item1.StringId + ":" + x.Item2 + ":" + x.Item1.Properties));
                break;
            }
            if (call.Recipe == null && call.Previous != null) call.Recipe = call.Previous.Recipe;
            if (call.Workshop != null) call.Detail += "; workshop=" + EconomyTransactionDiagnostics.Id(call.Workshop) + "; type=" + (call.Workshop.WorkshopType == null ? "uninitialized" : call.Workshop.WorkshopType.StringId)
                + "; owner=" + EconomyTransactionDiagnostics.Id(call.Workshop.Owner) + "; " + call.Recipe;
        }
        internal static void BooleanResult(bool __result, EconomyTransactionDiagnostics.Call __state)
        {
            if (__state == null) return;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                bool cycle = __state.Source.Contains("TickOneProductionCycle");
                string reason = Reason(__state, __result);
                EconomyTransactionDiagnostics.Open(__state);
                EconomyTrace.Write(cycle ? "RECIPE_CYCLE" : "RECIPE_GATE", __state.Id, __state.Parent,
                    EconomyTransactionDiagnostics.Id(__state.Workshop), (__state.Recipe ?? "recipe:unresolved") + (cycle ? "" : "; reason=" + reason), 0, __result ? 1 : 0,
                    __state.Source, __state.Detail + "; result=" + __result + "; " + Context(__state) + "; reason=" + reason);
                if (!cycle) return;
                var runCall = __state.Previous;
                while (runCall != null && runCall.RecipeRun == null) runCall = runCall.Previous;
                if (runCall == null) return;
                var production = __state.Arguments.OfType<WorkshopType.Production>().First();
                int index = runCall.RecipeRun.Shop.WorkshopType.Productions.IndexOf(production);
                if (index < 0 || index >= runCall.RecipeRun.Attempts.Length) throw new InvalidOperationException("Recipe changed during native run");
                runCall.RecipeRun.Attempts[index]++;
                if (__result) runCall.RecipeRun.Successes[index]++;
            }
            catch (Exception ex) { EconomyTrace.Fail("recipe result: " + ex); }
            finally { EconomyHealthDiagnostics.Record(started); }
        }
        internal static void SpeedResult(ExplainedNumber __result, EconomyTransactionDiagnostics.Call __state)
        {
            if (__state == null) return;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                EconomyTransactionDiagnostics.Open(__state);
                string kind = __state.Source.Contains("WorkshopModel") ? "RECIPE_SPEED" : __state.Source.Contains("VillageProduction") ? "VILLAGE_PRODUCTION" : "FINANCE_RESULT";
                if (__state.AuthoritativeFinance) kind = "FINAL_FINANCE_RESULT";
                if (__state.AuthoritativeFinance)
                {
                    var daily = __state.Previous;
                    while (daily != null && daily.FinanceHero == null) daily = daily.Previous;
                    if (daily != null) { daily.HasFinalFinance = true; daily.FinalFinance = __result.ResultNumber; }
                }
                if (__state.Source.Contains("GetInputDailyChange") || __state.Source.Contains("GetOutputDailyChange")) kind = "WAREHOUSE_ESTIMATE";
                string metric = kind == "VILLAGE_PRODUCTION" ? EconomyTransactionDiagnostics.Id(__state.Arguments[1])
                    : kind == "RECIPE_SPEED" ? "base:" + Convert.ToString(__state.Arguments[1], CultureInfo.InvariantCulture) : "observed_model_result";
                EconomyTrace.Write(kind, __state.Id, __state.Parent, __state.Workshop == null ? EconomyTransactionDiagnostics.Id(__state.Arguments.FirstOrDefault()) : EconomyTransactionDiagnostics.Id(__state.Workshop), metric, 0,
                    __result.ResultNumber, __state.Source, __state.Detail + "; recipeIdentity=unavailable_at_model_boundary; use run progress and attempts");
            }
            catch (Exception ex) { EconomyTrace.Fail("recipe speed: " + ex); }
            finally { EconomyHealthDiagnostics.Record(started); }
        }
        internal static void After(EconomyTransactionDiagnostics.Call call, Exception error)
        {
            if ((call.Source.EndsWith(".ChangeWorkshopOwnerByBankruptcy", StringComparison.Ordinal) || call.Source.EndsWith(".ChangeOwnerOfWorkshop", StringComparison.Ordinal)
                || call.Source.EndsWith(".ChangeWorkshopProduction", StringComparison.Ordinal)) && call.Workshop != null)
            {
                EconomyTransactionDiagnostics.Open(call);
                EconomyTrace.Write("WORKSHOP_TRANSITION", call.Id, call.Parent, EconomyTransactionDiagnostics.Id(call.Workshop), error == null ? "completed_handler" : "failed_handler", 0, 0,
                    call.Source, "beforeContext=" + call.Detail + "; afterOwner=" + EconomyTransactionDiagnostics.Id(call.Workshop.Owner)
                    + "; afterType=" + call.Workshop.WorkshopType.StringId + "; afterCapital=" + call.Workshop.Capital);
            }
            Run run = call.RecipeRun;
            if (run == null || error != null) return;
            if (run.Type != run.Shop.WorkshopType.StringId || run.Progress.Length != run.Shop.WorkshopType.Productions.Count)
                throw new InvalidOperationException("Workshop type changed during production; evidence invalid");
            EconomyTransactionDiagnostics.Open(call);
            for (int i = 0; i < run.Progress.Length; i++)
            {
                float after = run.Shop.GetProductionProgress(i);
                // Derivation relies on audited native cap/decrement semantics; not a new model query.
                double increment = after - Math.Min(1f, run.Progress[i]) + run.Attempts[i];
                EconomyTrace.Write("RECIPE_PROGRESS", call.Id, call.Parent, EconomyTransactionDiagnostics.Id(run.Shop), "recipe:" + i,
                    run.Progress[i], after, call.Source, "type=" + run.Type + "; owner=" + run.Owner + "; attempts=" + run.Attempts[i]
                    + "; successes=" + run.Successes[i] + "; derivedIncrement=" + N(increment) + "; " + call.Detail);
                EconomyTrace.Write("RECIPE_CADENCE", call.Id, call.Parent, EconomyTransactionDiagnostics.Id(run.Shop), "recipe:" + i,
                    0, increment, call.Source, "baseSpeed=" + N(run.Shop.WorkshopType.Productions[i].ConversionSpeed)
                    + "; type=" + run.Type + "; derived_from_native_progress; compare_candidate_to_control_not_unmodified_base");
            }
        }
        internal static void DemandResult(float __result, EconomyTransactionDiagnostics.Call __state)
        {
            if (__state == null) return;
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                EconomyTransactionDiagnostics.Open(__state);
                EconomyTrace.Write("MARKET_DEMAND", __state.Id, __state.Parent, EconomyTransactionDiagnostics.Id(__state.Arguments[0]),
                    "observed_demand_model", 0, __result, __state.Source, __state.Detail + "; model_not_actual_consumption");
            }
            catch (Exception ex) { EconomyTrace.Fail("demand result: " + ex); }
            finally { EconomyHealthDiagnostics.Record(start); }
        }
        private static string Context(EconomyTransactionDiagnostics.Call call)
        {
            Workshop s = call.Workshop;
            return s == null ? "workshop=unresolved" : "capital=" + s.Capital + "; townGold=" + s.Settlement.Town.Gold
                + "; rebellion=" + s.Settlement.Town.InRebelliousState
                + "; failedDetermineInputs=missing_inputs; failedCanProduce=inspect_inputMaterialCost_outputIncome_and_capital; no_recomputed_models";
        }
        private static string Reason(EconomyTransactionDiagnostics.Call call, bool result)
        {
            if (result) return "accepted";
            if (call.Source.Contains("DetermineItemRosterHasSufficientInputs")) return "inputs_rejected";
            if (!call.Source.Contains("Can") || call.Workshop == null) return "cycle_failed_see_child_gates";
            var p = (WorkshopType.Production)call.Arguments[0];
            int input = (int)call.Arguments[2], output = (int)call.Arguments[3];
            double hurdle = call.Workshop.WorkshopType.IsHidden ? input : input + 200d / p.ConversionSpeed;
            if (Campaign.Current.GameStarted && output <= hurdle) return "native_candidate_profitability";
            bool player = call.Source.Contains("CanPlayer");
            if (player && call.Workshop.Capital < input) return "native_candidate_workshop_capital";
            if ((bool)call.Arguments[4] && call.Workshop.Settlement.Town.Gold < output)
                return player ? "native_candidate_town_cash_or_warehouse_rule" : "native_candidate_town_cash";
            if (call.Workshop.Capital < input) return "native_candidate_workshop_capital";
            return "unexplained_rejection_check_other_patches";
        }
        private static string N(double n) { return n.ToString("R", CultureInfo.InvariantCulture); }
    }
}
