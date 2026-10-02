using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Bannerlord 1.4.8 observer-only prefix/finalizer boundary; no result/argument
    // replacements, no skipped originals, no transpilers. Exact overloads are
    // required. Other patches and inlining can hide mutations: reconciliation
    // reports these as UNEXPLAINED_DELTA, never as successful coverage.
    internal static class EconomyDiagnosticPatches
    {
        private const string Owner = "aoc.soak.economy.observer.v1";
        private static Harmony _harmony;
        internal static bool Ready { get; private set; }

        internal static List<MethodInfo> Targets()
        {
            var result = new List<MethodInfo>();
            result.Add(Require(typeof(Hero), "set_Gold", typeof(int)));
            result.Add(Require(typeof(MobileParty), "set_PartyTradeGold", typeof(int)));
            result.Add(Require(typeof(SettlementComponent), "ChangeGold", typeof(int)));
            result.Add(Require(typeof(Workshop), "ChangeGold", typeof(int)));
            result.Add(Require(typeof(Fief), "set_FoodStocks", typeof(float)));
            result.Add(Require(typeof(Kingdom), "set_TributeWallet", typeof(int)));
            result.Add(Require(typeof(Clan), "set_TributeWallet", typeof(int)));
            result.Add(Require(typeof(Kingdom), "set_KingdomBudgetWallet", typeof(int)));
            result.Add(Require(typeof(Clan), "set_DebtToKingdom", typeof(int)));
            result.Add(Require(typeof(ItemRoster), "AddToCounts", typeof(EquipmentElement), typeof(int)));
            result.Add(Require(typeof(ItemRoster), "Clear"));
            foreach (string name in new[] { "ApplyBetweenCharacters", "ApplyForCharacterToSettlement", "ApplyForSettlementToCharacter",
                "ApplyForSettlementToParty", "ApplyForPartyToSettlement", "ApplyForPartyToCharacter", "ApplyForCharacterToParty", "ApplyForPartyToParty" })
                result.Add(Unique(typeof(GiveGoldAction), name));
            result.Add(Unique(typeof(SellItemsAction), "ApplyInternal"));
            result.Add(Unique(typeof(GiveGoldAction), "ApplyInternal"));
            foreach (string name in new[] { "InitializeWorkshop", "ChangeOwnerOfWorkshop", "ChangeWorkshopProduction" }) result.Add(Unique(typeof(Workshop), name));
            result.Add(Require(typeof(ClanVariablesCampaignBehavior), "DailyTickClan", typeof(Clan)));
            result.Add(Require(typeof(ClanVariablesCampaignBehavior), "DailyTickHero", typeof(Hero)));
            foreach (string name in new[] { "CalculateClanGoldChange", "CalculateClanIncomeInternal", "CalculateClanExpensesInternal",
                "AddExpensesForTributes", "AddIncomeFromTribute", "AddSettlementIncome", "AddExpensesFromPartiesAndGarrisons",
                "AddPartyExpense", "AddExpenseFromLeaderParty", "CalculatePartyWage", "CalculateHeroIncomeFromWorkshops" })
                result.Add(Unique(typeof(DefaultClanFinanceModel), name));
            foreach (string name in new[] { "DailyTickTown", "RunTownWorkshop", "ConsumeInputFromTownMarket", "ProduceAnOutputToTown",
                "ConsumeInputFromWarehouse", "ProduceAnOutputToWarehouse", "HandleDailyExpense",
                "HandleNotableWorkshopExpense", "HandlePlayerWorkshopExpense", "TickOneProductionCycleForPlayerWorkshop",
                "TickOneProductionCycleForNotableWorkshop", "CanPlayerWorkshopProduceThisCycle", "CanNotableWorkshopProduceThisCycle",
                "DetermineItemRosterHasSufficientInputs", "ChangeWorkshopOwnerByBankruptcy" })
                result.Add(Unique(typeof(WorkshopsCampaignBehavior), name));
            result.Add(Unique(typeof(DefaultWorkshopModel), "GetEffectiveConversionSpeedOfProduction"));
            result.Add(Unique(typeof(DefaultVillageProductionCalculatorModel), "CalculateDailyProductionAmount"));
            result.Add(Unique(typeof(DefaultSettlementEconomyModel), "GetDailyDemandForCategory"));
            foreach (var method in typeof(WorkshopsCampaignBehavior).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
                if (method.Name.EndsWith(".GetInputDailyChange", StringComparison.Ordinal) || method.Name.EndsWith(".GetOutputDailyChange", StringComparison.Ordinal)) result.Add(method);
            if (Campaign.Current != null)
            {
                var model = Campaign.Current.Models.ClanFinanceModel;
                MethodInfo final = model.GetType().GetMethod("CalculateClanGoldChange", new[] { typeof(Clan), typeof(bool), typeof(bool), typeof(bool) });
                if (final == null || final.ReturnType != typeof(ExplainedNumber)) throw new MissingMethodException("Active finance result boundary unavailable");
                if (!result.Contains(final)) result.Add(final);
            }
            return result;
        }
        private static MethodInfo Require(Type type, string name, params Type[] args)
        {
            MethodInfo method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, args, null);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            return method;
        }
        private static MethodInfo Unique(Type type, string name)
        {
            MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(method => method.Name == name).ToArray();
            if (methods.Length != 1) throw new MissingMethodException("Expected one audited overload: " + type.FullName + "." + name);
            return methods[0];
        }
        internal static void Install()
        {
            if (_harmony != null) return;
            try
            {
                _harmony = new Harmony(Owner);
                foreach (MethodInfo target in Targets())
                {
                    var prefix = new HarmonyMethod(typeof(EconomyTransactionDiagnostics), "Before") { priority = Priority.First };
                    // Never inject __args into a finalizer for ref structs: stale
                    // boxing can write back over native finance results in Harmony.
                    bool financeRef = target.GetParameters().Any(parameter => parameter.Name == "goldChange" && parameter.ParameterType == typeof(ExplainedNumber).MakeByRefType());
                    var finalizer = new HarmonyMethod(typeof(EconomyTransactionDiagnostics), financeRef ? "FinanceAfter" : "After") { priority = Priority.Last };
                    HarmonyMethod postfix = target.Name == "CalculatePartyWage" ? new HarmonyMethod(typeof(EconomyTransactionDiagnostics), "WageResult") : null;
                    if (target.ReturnType == typeof(bool)) postfix = new HarmonyMethod(typeof(EconomyTransactionDiagnostics), "BooleanResult");
                    if (target.ReturnType == typeof(ExplainedNumber)) postfix = new HarmonyMethod(typeof(EconomyTransactionDiagnostics), "SpeedResult") { priority = Priority.Last };
                    if (target.ReturnType == typeof(float)) postfix = new HarmonyMethod(typeof(EconomyTransactionDiagnostics), "DemandResult") { priority = Priority.Last };
                    _harmony.Patch(target, prefix, postfix, null, finalizer);
                    SoakLog.Write("ECONOMY_HOOK", "target=" + target.DeclaringType.FullName + "." + target + "; mvid=" + target.Module.ModuleVersionId
                        + "; otherOwners=" + string.Join(",", Harmony.GetPatchInfo(target).Owners.Where(owner => owner != Owner)));
                }
                Ready = true;
            }
            catch (Exception ex)
            {
                SoakLog.Write("ECONOMY_HOOK_FAILURE", ex.ToString());
                Uninstall(); // Fail open for gameplay, fail closed for coverage.
            }
        }
        internal static void Uninstall()
        {
            Ready = false;
            if (_harmony == null) return;
            try { _harmony.UnpatchAll(Owner); }
            catch (Exception ex) { SoakLog.Write("ECONOMY_UNPATCH_FAILURE", ex.ToString()); }
            finally { _harmony = null; }
        }
    }
}
