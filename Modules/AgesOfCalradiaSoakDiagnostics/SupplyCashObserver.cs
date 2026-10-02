using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Core;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Native 1.4.8 money endpoints, not a claim that every transfer conserves gold.
    // Prefix/finalizer observe actual values, requested change and caller chain.
    // No ref/out native arguments, result modification, finance reevaluation or RNG.
    // Snapshot residuals expose bypassed/inlined mutations; new objects are marked.
    // v7: canonical lord/hero ownership; raw direct-party backing field avoids a
    // stale wallet retargeting when a leader changes. Wider boundaries net out
    // nested observations. Verify-SupplyCash.ps1 covers aliases, native targets,
    // serialized nested deltas and role restoration; live residuals remain required.
    internal static class SupplyCashObserver
    {
        private sealed class Wallet
        {
            internal object Owner;
            internal PropertyInfo Property;
            internal string Id;
            internal double Start, Observed;
            internal double Read() { return Convert.ToDouble(Owner is MobileParty ? PartyCash.GetValue(Owner) : Property.GetValue(Owner)); }
        }
        internal sealed class Call
        {
            internal object Wallet;
            internal double Before;
            internal double ObservedBefore;
            internal string Source;
            internal int Requested;
            internal bool TransferEndpoint;
            internal string BoundaryCategory;
            internal CashPurposeContext.Frame Purpose;
            internal bool IsBoundary;
        }
        private static readonly Dictionary<MethodBase, PropertyInfo> Properties = new Dictionary<MethodBase, PropertyInfo>();
        // Native MBObjectBase.GetHashCode changes when registration assigns Id.
        // Both indexes must use immutable session-local reference identities.
        private static readonly Dictionary<Tuple<long, string>, Wallet> Wallets = new Dictionary<Tuple<long, string>, Wallet>();
        private static readonly Dictionary<long, string> Aliases = new Dictionary<long, string>();
        private static readonly FieldInfo PartyCash = AccessTools.Field(typeof(MobileParty), "_partyTradeGold");
        private static EconomyObjectIdentity _identity = new EconomyObjectIdentity();
        private static long _transferSequence;
        internal static IEnumerable<MethodInfo> Targets()
        {
            yield return SupplyChainObserver.Require(typeof(Hero), "set_Gold", typeof(int));
            yield return SupplyChainObserver.Require(typeof(MobileParty), "set_PartyTradeGold", typeof(int));
            yield return SupplyChainObserver.Require(typeof(SettlementComponent), "ChangeGold", typeof(int));
            yield return SupplyChainObserver.Require(typeof(Workshop), "ChangeGold", typeof(int));
            yield return SupplyChainObserver.Require(typeof(Clan), "set_TributeWallet", typeof(int));
            yield return SupplyChainObserver.Require(typeof(Clan), "set_DebtToKingdom", typeof(int));
            yield return SupplyChainObserver.Require(typeof(Kingdom), "set_TributeWallet", typeof(int));
            yield return SupplyChainObserver.Require(typeof(Kingdom), "set_KingdomBudgetWallet", typeof(int));
        }
        internal static void Reset() { Wallets.Clear(); Aliases.Clear(); Properties.Clear(); _transferSequence = 0; _identity = new EconomyObjectIdentity(); CashPurposeContext.Reset(); }
        internal static void ResetPeriod()
        {
            Wallets.Clear(); Aliases.Clear(); _transferSequence = 0;
            _identity = new EconomyObjectIdentity();
            // Preserve installed endpoint-to-property bindings across log rollover.
        }
        internal static string WorkshopWalletId(Workshop shop)
        {
            return Get(shop, typeof(Workshop).GetProperty("Capital"), "workshop_identity_snapshot").Id;
        }
        internal static string RewardWalletId(Hero hero,MobileParty party)
        {
            if(hero!=null)return Get(hero,typeof(Hero).GetProperty("Gold"),"reward_boundary").Id;
            return party==null?"none":Get(party,typeof(MobileParty).GetProperty("PartyTradeGold"),"reward_boundary").Id;
        }
        internal static IEnumerable<MethodInfo> BoundaryTargets()
        {
            yield return SupplyChainObserver.Require(typeof(Hero), "ChangeHeroGold", typeof(int));
            yield return SupplyChainObserver.Require(typeof(ClanVariablesCampaignBehavior), "DailyTickClan", typeof(Clan));
            yield return SupplyChainObserver.Require(typeof(ClanVariablesCampaignBehavior), "DailyTickHero", typeof(Hero));
            yield return SupplyChainObserver.Require(typeof(WorkshopsCampaignBehavior), "ProduceAnOutputToTown", typeof(EquipmentElement), typeof(Workshop), typeof(bool));
            yield return SupplyChainObserver.Require(typeof(WorkshopsCampaignBehavior), "ConsumeInputFromTownMarket", typeof(ItemCategory), typeof(int), typeof(Town), typeof(Workshop), typeof(bool));
            yield return SupplyChainObserver.Require(typeof(GiveGoldAction), "ApplyInternal", typeof(Hero), typeof(PartyBase), typeof(Hero), typeof(PartyBase), typeof(int), typeof(bool), typeof(string));
            yield return SupplyChainObserver.Require(typeof(WorkshopsCampaignBehavior), "DailyTickTown", typeof(Town));
            foreach (string name in new[] { "HandleDailyExpense", "HandleNotableWorkshopExpense", "HandlePlayerWorkshopExpense" })
                yield return SupplyChainObserver.Require(typeof(WorkshopsCampaignBehavior), name, typeof(Workshop));
            yield return SupplyChainObserver.Require(typeof(Workshop), "ChangeOwnerOfWorkshop", typeof(Hero), typeof(WorkshopType), typeof(int));
            yield return SupplyChainObserver.Require(typeof(Workshop), "InitializeWorkshop", typeof(Hero), typeof(WorkshopType));
            // Wider non-ref model boundaries catch direct/inlined finance writes.
            // Observe actual wallets even for previews, but never re-run a model.
            foreach (string name in new[] { "CalculateClanIncome", "CalculateClanExpenses" })
                yield return SupplyChainObserver.Require(typeof(DefaultClanFinanceModel), name,
                    typeof(Clan), typeof(bool), typeof(bool), typeof(bool));
        }
        internal static MethodInfo WithdrawalTarget()
        {
            return SupplyChainObserver.Require(typeof(DefaultClanFinanceModel), "CalculateHeroIncomeFromWorkshops",
                typeof(Hero), typeof(ExplainedNumber).MakeByRefType(), typeof(bool));
        }
        internal static void Install(Harmony harmony)
        {
            Reset();
            if (PartyCash == null || PartyCash.FieldType != typeof(int)) throw new MissingFieldException("Native party cash backing field changed");
            foreach (var m in Targets())
            {
                string name = m.Name.StartsWith("set_", StringComparison.Ordinal) ? m.Name.Substring(4) : m.DeclaringType == typeof(Workshop) ? "Capital" : "Gold";
                var property = m.DeclaringType.GetProperty(name);
                if (property == null || property.GetGetMethod() == null) throw new MissingMemberException(m.DeclaringType.FullName, name);
                Properties.Add(m, property);
                harmony.Patch(m, new HarmonyMethod(typeof(SupplyCashObserver), "Before"), null, null, new HarmonyMethod(typeof(SupplyCashObserver), "After"));
            }
            // Observe callers of tiny endpoints that can be inlined. Argument arrays
            // are permitted ONLY on these validated non-ref signatures. Void
            // finalizers preserve native exceptions; failures disable capture.
            foreach (var m in BoundaryTargets())
            {
                if (m.GetParameters().Any(p => p.ParameterType.IsByRef)) throw new InvalidOperationException("Cash boundary cannot observe ref arguments");
                harmony.Patch(m, new HarmonyMethod(typeof(SupplyCashObserver), "BoundaryBefore"), null, null,
                    new HarmonyMethod(typeof(SupplyCashObserver), "BoundaryAfter"));
            }
            // Native 1.4.8 withdrawal boundary: typed read-only arguments deliberately
            // omit ref ExplainedNumber. Never marshal __args here or re-evaluate finance.
            // Captures only owned-shop capital; nested changes are subtracted. Missing
            // targets fail capture setup. Verified by Verify-WorkshopCashBoundaries.ps1.
            harmony.Patch(WithdrawalTarget(), new HarmonyMethod(typeof(SupplyCashObserver), "WithdrawalBefore"),
                null, null, new HarmonyMethod(typeof(SupplyCashObserver), "BoundaryAfter"));
            MoneyPurposeObserver.Install(harmony);
        }
        private static Wallet Get(object owner, PropertyInfo property, string origin)
        {
            var party = owner as MobileParty;
            if (party != null && party.IsLordParty && party.LeaderHero != null)
            {
                var canonical = Get(party.LeaderHero, typeof(Hero).GetProperty("Gold"), origin);
                Alias(party, canonical.Id);
                return canonical;
            }
            var key = Tuple.Create(_identity.Get(owner), property.Name);
            Wallet w;
            if (Wallets.TryGetValue(key, out w)) { if (party != null) Alias(party, w.Id); return w; }
            w = new Wallet { Owner = owner, Property = property, Id = owner.GetType().Name + "/instance:" + _identity.Get(owner) + "/" + property.Name };
            w.Start = w.Read(); Wallets.Add(key, w);
            var name = owner.GetType().GetProperty("StringId");
            SupplyCapture.Write("WALLET_BASELINE", 0, 0, w.Id, property.Name, w.Start, w.Start, "origin=" + origin + "; stringId=" + name?.GetValue(owner)
                + "; settlement=" + (owner as SettlementComponent)?.Settlement?.StringId);
            if (party != null) Alias(party, w.Id);
            return w;
        }
        private static void Alias(MobileParty party, string canonical)
        {
            string old;
            long identity = _identity.Get(party);
            if (Aliases.TryGetValue(identity, out old) && old == canonical) return;
            Aliases[identity] = canonical;
            SupplyCapture.Write("WALLET_ALIAS", 0, 0, "MobileParty/instance:" + _identity.Get(party) + "/PartyTradeGold", "canonical", 0, 0,
                "target=" + canonical + "; previous=" + old + "; party=" + party.StringId);
        }
        internal static double UnobservedDelta(double before, double after, double observedBefore, double observedAfter)
        {
            return (after - before) - (observedAfter - observedBefore);
        }
        private static Call Capture(object owner, string property, string source)
        {
            var w = Get(owner, owner.GetType().GetProperty(property), "first_boundary_not_session_start");
            return new Call { Wallet = w, Before = w.Read(), ObservedBefore = w.Observed, Source = source, IsBoundary = true };
        }
        private static void CaptureShop(List<Call> calls, Workshop shop, string source, string category)
        {
            var call = Capture(shop, "Capital", source + "; cashCategory=" + category);
            call.BoundaryCategory = category;
            calls.Add(call);
        }
        private static void CaptureOwnedShops(List<Call> calls, Hero hero, string source, string category)
        {
            if (hero == null) return;
            foreach (var shop in hero.OwnedWorkshops) CaptureShop(calls, shop, source, category);
        }
        private static void WithdrawalBefore(Hero __0, bool __2, out List<Call> __state)
        {
            __state = null;
            if (!SupplyCapture.Active || !__2) return;
            try
            {
                __state = new List<Call>();
                CaptureOwnedShops(__state, __0, "DefaultClanFinanceModel.CalculateHeroIncomeFromWorkshops", "owner_payout");
                if (__state.Count > 0) __state[0].Purpose = CashPurposeContext.Begin("owner_payout");
            }
            catch (Exception ex) { SupplyCapture.Fail("workshop withdrawal before", ex); }
        }
        private static void BoundaryBefore(object __instance, object[] __args, MethodBase __originalMethod, out List<Call> __state)
        {
            __state = null;
            if (!SupplyCapture.Active) return;
            try
            {
                var calls = new List<Call>();
                string source = __originalMethod.DeclaringType.FullName + "." + __originalMethod.Name;
                string category = __originalMethod.DeclaringType == typeof(Workshop) ? "capital_reset"
                    : __originalMethod.Name == "DailyTickTown" ? "daily_town_unclassified"
                    : __originalMethod.Name.Contains("Expense") ? "operating_expense" : null;
                bool financeFallback = __originalMethod.DeclaringType == typeof(ClanVariablesCampaignBehavior);
                bool financeModel = __originalMethod.DeclaringType == typeof(DefaultClanFinanceModel);
                // Preserve the caller of grants/transfers, not just the common
                // GiveGold endpoint. Do not label a null-giver payment inflation:
                // native daily finance also settles through that path.
                if (__originalMethod.DeclaringType == typeof(GiveGoldAction))
                    source += "; transfer=" + (++_transferSequence) + "; requestedTransfer=" + __args[4] + "; transaction=" + __args[6]
                        + "; giverHero=" + ((__args[0] as Hero)?.StringId ?? "none")
                        + "; recipientHero=" + ((__args[2] as Hero)?.StringId ?? "none")
                        + "; callers=" + string.Join("|", new StackTrace(1, false).GetFrames().Take(16)
                            .Select(f => f.GetMethod()).Select(m => m.DeclaringType?.FullName + "." + m.Name));
                foreach (var value in new[] { __instance }.Concat(__args))
                {
                    var hero = value as Hero;
                    var party = value as PartyBase;
                    var shop = value as Workshop;
                    var clan = value as Clan;
                    if (clan?.Leader != null) calls.Add(Capture(clan.Leader, "Gold", source));
                    if (clan != null && (financeFallback || financeModel))
                    {
                        calls.Add(Capture(clan, "TributeWallet", source));
                        calls.Add(Capture(clan, "DebtToKingdom", source));
                        foreach (var member in clan.Heroes) calls.Add(Capture(member, "Gold", source));
                        if (clan.Kingdom != null)
                        {
                            calls.Add(Capture(clan.Kingdom, "TributeWallet", source));
                            calls.Add(Capture(clan.Kingdom, "KingdomBudgetWallet", source));
                        }
                    }
                    if (hero != null) calls.Add(Capture(hero, "Gold", source));
                    if (financeFallback)
                    {
                        CaptureOwnedShops(calls, hero, source, "finance_unclassified");
                        CaptureOwnedShops(calls, clan?.Leader, source, "finance_unclassified");
                    }
                    var town = value as Town;
                    if (town != null && category == "daily_town_unclassified")
                        foreach (var workshop in town.Workshops) CaptureShop(calls, workshop, source, category);
                    if (party?.MobileParty != null) calls.Add(Capture(party.MobileParty, "PartyTradeGold", source));
                    if (party?.Settlement?.SettlementComponent != null) calls.Add(Capture(party.Settlement.SettlementComponent, "Gold", source));
                    if (shop != null)
                    {
                        if (category == null) calls.Add(Capture(shop, "Capital", source));
                        else CaptureShop(calls, shop, source, category);
                        if (category == null) calls.Add(Capture(shop.Settlement.Town, "Gold", source));
                    }
                }
                __state = calls.GroupBy(c => c.Wallet).Select(g => g.First()).ToList();
                foreach (var call in __state)
                {
                    call.TransferEndpoint = __originalMethod.DeclaringType == typeof(GiveGoldAction);
                    if (call.TransferEndpoint)
                        call.Source += "; expectedEndpoints=" + __state.Count
                            + "; giverPresent=" + (__args[0] != null || __args[1] != null)
                            + "; recipientPresent=" + (__args[2] != null || __args[3] != null);
                }
                if (__state.Count > 0 && (category != null || financeFallback || financeModel || __originalMethod.DeclaringType == typeof(GiveGoldAction)))
                    __state[0].Purpose = CashPurposeContext.Begin(category ?? (financeFallback || financeModel ? "finance_unclassified" : "gold_transfer_unclassified"));
            }
            catch (Exception ex) { SupplyCapture.Fail("cash boundary before", ex); }
        }
        private static void BoundaryAfter(List<Call> __state, Exception __exception, bool __runOriginal)
        {
            if (__state == null) return;
            var purpose = __state.Count == 0 ? null : __state[0].Purpose;
            try
            {
                // Commit execution outcome before nested net remainder receipts.
                CashPurposeContext.End(purpose, __runOriginal, __exception);
                foreach (var call in __state)
                {
                    // Patch-side cash is observed, not attributed to a skipped original.
                    if (!__runOriginal)
                        call.Source += "; cashCategory=skipped_boundary_unclassified";
                    After(call, __exception);
                    if (__exception == null && SupplyCapture.Active && call.BoundaryCategory != null)
                    {
                        try
                        {
                            var wallet = (Wallet)call.Wallet;
                            SupplyCapture.Write("WORKSHOP_CASH_BOUNDARY", 0, 0, wallet.Id, call.BoundaryCategory,
                                call.Before, wallet.Read(), "originalRan=" + __runOriginal + "; source=" + call.Source
                                + "; gross_context_not_additive_cash_flow");
                        }
                        catch (Exception ex) { SupplyCapture.Fail("workshop cash boundary receipt", ex); }
                    }
                }
            }
            finally { CashPurposeContext.Restore(purpose); }
        }
        private static void Before(object __instance, MethodBase __originalMethod, int __0, out Call __state)
        {
            __state = null;
            if (!SupplyCapture.Active) return;
            try
            {
                var w = Get(__instance, Properties[__originalMethod], "first_mutation_not_session_start");
                // Common no-op requests omit only the expensive stack. Still
                // measure after: another patch can change even a no-op request.
                double current = w.Read();
                bool noOp = (__originalMethod.Name.StartsWith("set_", StringComparison.Ordinal) && current == __0)
                    || (!__originalMethod.Name.StartsWith("set_", StringComparison.Ordinal) && __0 == 0);
                __state = new Call { Wallet = w, Before = w.Read(), ObservedBefore = w.Observed, Requested = __0,
                    Source = __originalMethod.Name + "; callers=" + (noOp ? "no_op_request_stack_omitted"
                        : string.Join("|", new StackTrace(1, false).GetFrames().Take(12).Select(f => f.GetMethod()).Select(m => m.DeclaringType?.FullName + "." + m.Name))) };
            }
            catch (Exception ex) { SupplyCapture.Fail("wallet before", ex); }
        }
        private static void After(Call __state, Exception __exception)
        {
            if (__state == null) return;
            try
            {
                if (__exception != null) { SupplyCapture.Fail("native wallet mutation", __exception); return; }
                if (!SupplyCapture.Active) return;
                var w = (Wallet)__state.Wallet;
                double after = w.Read();
                // Independent transfer receipt survives complete nested observation.
                // Gross endpoint deltas are context, never additive ledger entries.
                if (__state.TransferEndpoint)
                    SupplyCapture.Write("GOLD_TRANSFER_ENDPOINT", 0, 0, w.Id, w.Property.Name, __state.Before, after,
                        "source=" + __state.Source + SupplyRewardObserver.Context + "; gross_context_not_additive_cash_flow");
                double delta = UnobservedDelta(__state.Before, after, __state.ObservedBefore, w.Observed);
                if (__state.IsBoundary)
                    SupplyCapture.Write("OBSERVER_COVERAGE", 0, 0, w.Id,
                        delta != 0 ? "outer_boundary_recovered_activity" : after != __state.Before ? "nested_activity_reconciled" : "no_net_activity",
                        after - __state.Before, w.Observed - __state.ObservedBefore,
                        "source=" + __state.Source + "; recoveredNet=" + SupplyCapture.N(delta)
                        + "; independent_wallet_endpoints_vs_nested_receipts; not_proof_of_inlining; zero_net_does_not_exclude_offsetting_activity");
                w.Observed += delta;
                if (delta != 0)
                    SupplyCapture.Write("WALLET_CHANGE", 0, 0, w.Id, w.Property.Name, after - delta, after,
                        "requested=" + __state.Requested + "; source=" + __state.Source + SupplyRewardObserver.Context + ProcurementObservation.CashContext + NavalSaleObserver.Context
                        + SupplyMarketObserver.Context + SupplyWorkshopObserver.Context + SupplyTownCashObserver.Context
                        + LogisticsLifecycleObserver.Context
                        + CashPurposeContext.Context
                        + "; grossBefore=" + SupplyCapture.N(__state.Before) + "; grossAfter=" + SupplyCapture.N(after)
                        + "; net_of_nested_observations; endpoint_not_transfer_conservation");
            }
            catch (Exception ex) { SupplyCapture.Fail("wallet after", ex); }
        }
        private static void Seed(object owner, string property)
        {
            Get(owner, owner.GetType().GetProperty(property), "snapshot");
        }
        internal static void Snapshot()
        {
            if (!SupplyCapture.Active) return;
            try
            {
                foreach (var h in Hero.AllAliveHeroes)
                {
                    var wallet = Get(h, typeof(Hero).GetProperty("Gold"), "snapshot");
                    SupplyCapture.Write("WALLET_IDENTITY", 0, 0, wallet.Id, "hero", 0, 0,
                        "hero=" + h.StringId + "; clan=" + h.Clan?.StringId + "; kingdom=" + h.Clan?.Kingdom?.StringId
                        + "; clanLeader=" + (h.Clan?.Leader == h) + "; noble=" + h.IsLord
                        + "; identity_at_snapshot_not_historical_membership");
                }
                foreach (var p in MobileParty.All) Seed(p, "PartyTradeGold");
                foreach (var c in Clan.All) { Seed(c, "TributeWallet"); Seed(c, "DebtToKingdom"); }
                foreach (var k in Kingdom.All) { Seed(k, "TributeWallet"); Seed(k, "KingdomBudgetWallet"); }
                foreach (var t in Town.AllTowns)
                {
                    Seed(t, "Gold");
                    foreach (var w in t.Workshops) Seed(w, "Capital");
                }
                foreach (var w in Wallets.Values)
                {
                    var party = w.Owner as MobileParty;
                    if (party != null && party.IsLordParty && party.LeaderHero != null)
                    {
                        SupplyCapture.Write("WALLET_DORMANT", 0, 0, w.Id, w.Property.Name, 0, 0, "party_now_aliases_hero; not_an_independent_active_wallet");
                        continue;
                    }
                    double current = w.Read();
                    SupplyCapture.Write("WALLET_CHECK", 0, 0, w.Id, w.Property.Name, w.Start + w.Observed, current,
                        "baseline=" + SupplyCapture.N(w.Start) + "; observedNet=" + SupplyCapture.N(w.Observed) + "; residual_not_automatically_inflation");
                }
            }
            catch (Exception ex) { SupplyCapture.Fail("wallet snapshot", ex); }
        }
    }
}
