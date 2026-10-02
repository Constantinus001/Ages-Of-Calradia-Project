using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Native 1.4.8 read-only purpose witnesses. Finance component hooks bind no
    // ref arguments and never re-evaluate a model. Callsite replacements retain
    // the original arguments/call once; no RNG, return, amount or wallet edits.
    // Exact IL/signature preflight fails capture on mismatch. Inlining remains
    // possible for component methods: boundary coverage reports that gap, never
    // fabricates attribution. Verify-DiagnosticHardening exercises these contracts.
    internal static class MoneyPurposeObserver
    {
        internal static readonly MethodInfo GrantSetter = SupplyChainObserver.Require(typeof(Kingdom), "set_KingdomBudgetWallet", typeof(int));
        internal static readonly MethodInfo Payment = SupplyChainObserver.Require(typeof(GiveGoldAction), "ApplyBetweenCharacters", typeof(Hero), typeof(Hero), typeof(int), typeof(bool));
        internal static IEnumerable<MethodInfo> Targets()
        {
            yield return SupplyChainObserver.Require(typeof(ClanVariablesCampaignBehavior), "DailyTickClan", typeof(Clan));
            yield return SupplyChainObserver.Require(typeof(ClanVariablesCampaignBehavior), "DailyTickHero", typeof(Hero));
            var model = typeof(DefaultClanFinanceModel);
            yield return SupplyChainObserver.Require(model, "AddIncomeFromTribute", typeof(Clan), typeof(ExplainedNumber).MakeByRefType(), typeof(bool), typeof(bool));
            foreach (string name in new[] { "AddExpensesForTributes", "AddIncomeFromKingdomBudget" })
                yield return SupplyChainObserver.Require(model, name, typeof(Clan), typeof(ExplainedNumber).MakeByRefType(), typeof(bool));
            yield return SupplyChainObserver.Require(model, "AddPartyExpense", typeof(MobileParty), typeof(Clan), typeof(ExplainedNumber), typeof(bool));
            yield return SupplyChainObserver.Require(model, "CalculatePartyWage", typeof(MobileParty), typeof(int), typeof(bool));
        }
        internal static void Install(Harmony harmony)
        {
            var targets = Targets().ToArray();
            foreach (var target in targets.Where(t => t.DeclaringType == typeof(ClanVariablesCampaignBehavior)))
                Tap(HarmonyLib.PatchProcessor.GetOriginalInstructions(target), target).ToArray();
            var expense = targets.Single(t => t.Name == "AddPartyExpense");
            WageTap(HarmonyLib.PatchProcessor.GetOriginalInstructions(expense)).ToArray();
            foreach (var target in targets)
            {
                if (target.DeclaringType == typeof(ClanVariablesCampaignBehavior))
                    harmony.Patch(target, transpiler: new HarmonyMethod(typeof(MoneyPurposeObserver), nameof(Tap)));
                else if (target.Name == "CalculatePartyWage")
                    harmony.Patch(target, finalizer: new HarmonyMethod(typeof(MoneyPurposeObserver), nameof(WageAfter)));
                else
                    harmony.Patch(target, prefix: new HarmonyMethod(typeof(MoneyPurposeObserver),
                        target.Name == "AddPartyExpense" ? nameof(PartyBefore) : nameof(ComponentBefore)),
                        transpiler: target.Name == "AddPartyExpense" ? new HarmonyMethod(typeof(MoneyPurposeObserver), nameof(WageTap)) : null,
                        finalizer: new HarmonyMethod(typeof(MoneyPurposeObserver), nameof(After)));
            }
        }
        internal static IEnumerable<CodeInstruction> Tap(IEnumerable<CodeInstruction> input, MethodBase __originalMethod)
        {
            var code = input.Select(c => new CodeInstruction(c)).ToList();
            bool clan = __originalMethod.Name == "DailyTickClan";
            int payments = code.Count(i => Equals(i.operand, Payment));
            int grants = code.Count(i => Equals(i.operand, GrantSetter));
            if (payments != 1 || grants != (clan ? 1 : 0))
                throw new InvalidOperationException("Native daily finance callsite count changed");
            for (int n = 0; n < code.Count; n++)
            {
                if (Equals(code[n].operand, GrantSetter))
                {
                    if (n < 4 || code[n-1].opcode != OpCodes.Add || !code[n-2].opcode.Name.StartsWith("ldloc", StringComparison.Ordinal)
                        || !(code[n-3].operand is MethodInfo getter) || getter.Name != "get_KingdomBudgetWallet"
                        || code[n-4].opcode != OpCodes.Dup)
                        throw new InvalidOperationException("Native additive budget grant pattern changed");
                    code[n].opcode = OpCodes.Call; code[n].operand = AccessTools.Method(typeof(MoneyPurposeObserver), nameof(Grant));
                }
                else if (Equals(code[n].operand, Payment))
                {
                    code[n].opcode = OpCodes.Call; code[n].operand = AccessTools.Method(typeof(MoneyPurposeObserver), nameof(Settle));
                }
            }
            return code;
        }
        private static CashPurposeContext.Frame Begin(string purpose)
        {
            if (!SupplyCapture.Active) return null;
            try { return CashPurposeContext.Begin(purpose); }
            catch (Exception ex) { SupplyCapture.Fail("money purpose before", ex); return null; }
        }
        internal static IEnumerable<CodeInstruction> WageTap(IEnumerable<CodeInstruction> input)
        {
            var code = input.Select(c => new CodeInstruction(c)).ToList();
            var hero = SupplyChainObserver.Require(typeof(Hero), "set_Gold", typeof(int));
            var party = SupplyChainObserver.Require(typeof(MobileParty), "set_PartyTradeGold", typeof(int));
            if (code.Count(c => Equals(c.operand, hero)) != 1 || code.Count(c => Equals(c.operand, party)) != 2)
                throw new InvalidOperationException("Native wage/funding setter count changed");
            int deductions = 0, funding = 0;
            for (int n = 0; n < code.Count; n++)
            {
                bool isHero = Equals(code[n].operand, hero);
                if (!isHero && !Equals(code[n].operand, party)) continue;
                if (n < 4 || code[n-4].opcode != OpCodes.Dup || !(code[n-3].operand is MethodInfo getter)
                    || getter.Name != (isHero ? "get_Gold" : "get_PartyTradeGold")
                    || !code[n-2].opcode.Name.StartsWith("ldloc", StringComparison.Ordinal)
                    || (code[n-1].opcode != OpCodes.Sub && code[n-1].opcode != OpCodes.Add))
                    throw new InvalidOperationException("Native wage/funding arithmetic changed");
                bool debit = code[n-1].opcode == OpCodes.Sub;
                if (isHero && !debit) throw new InvalidOperationException("Unexpected hero funding branch");
                if (debit) deductions++; else funding++;
                code[n].opcode = OpCodes.Call;
                code[n].operand = AccessTools.Method(typeof(MoneyPurposeObserver), isHero ? nameof(WageHero) : debit ? nameof(WageParty) : nameof(FundParty));
            }
            if (deductions != 2 || funding != 1) throw new InvalidOperationException("Native wage/funding branch count changed");
            return code;
        }
        private static void ComponentBefore(bool __2, MethodBase __originalMethod, out CashPurposeContext.Frame __state)
        {
            __state = __2 ? Begin(__originalMethod.Name == "AddIncomeFromKingdomBudget" ? "kingdom_budget_distribution" : "tribute_wallet_settlement") : null;
        }
        private static void PartyBefore(bool __3, out CashPurposeContext.Frame __state)
        { __state = __3 ? Begin("party_wages_and_funding") : null; }
        private static void After(CashPurposeContext.Frame __state, bool __runOriginal, Exception __exception)
        {
            try { CashPurposeContext.End(__state, __runOriginal, __exception); }
            catch (Exception ex) { SupplyCapture.Fail("money purpose after", ex); }
            finally { CashPurposeContext.Restore(__state); }
        }
        private static void WageAfter(MobileParty __0, int __1, bool __2, int __result, bool __runOriginal, Exception __exception)
        {
            if (!SupplyCapture.Active) return;
            try
            {
                SupplyCapture.Write("WAGE_ASSESSMENT", 0, 0, __0?.StringId ?? "none", "native_return_not_cash_payment", __1, __result,
                    "applyWithdrawals=" + __2 + "; originalRan=" + __runOriginal + "; error=" + (__exception == null ? "none" : __exception.GetType().FullName)
                    + CashPurposeContext.Context + "; do_not_add_to_wallet_flows");
            }
            catch (Exception ex) { SupplyCapture.Fail("wage witness", ex); }
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Grant(Kingdom kingdom, int value)
        {
            var state = Begin("native_kingdom_budget_grant");
            try { kingdom.KingdomBudgetWallet = value; }
            catch (Exception ex) { After(state, true, ex); throw; }
            After(state, true, null);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WageHero(Hero hero, int value)
        {
            var state = Begin("native_party_wage_payment");
            try { hero.Gold = value; }
            catch (Exception ex) { After(state, true, ex); throw; }
            After(state, true, null);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WageParty(MobileParty party, int value)
        {
            var state = Begin("native_party_wage_payment");
            try { party.PartyTradeGold = value; }
            catch (Exception ex) { After(state, true, ex); throw; }
            After(state, true, null);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void FundParty(MobileParty party, int value)
        {
            var state = Begin("native_party_funding");
            try { party.PartyTradeGold = value; }
            catch (Exception ex) { After(state, true, ex); throw; }
            After(state, true, null);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Settle(Hero giver, Hero recipient, int amount, bool notify)
        {
            var state = Begin("native_daily_finance_settlement");
            try { GiveGoldAction.ApplyBetweenCharacters(giver, recipient, amount, notify); }
            catch (Exception ex) { After(state, true, ex); throw; }
            After(state, true, null);
        }
    }
}
