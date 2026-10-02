using System;
using System.Globalization;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace AgesOfCalradia.SoakDiagnostics
{
    // GiveGoldAction.ApplyInternal (1.4.8): observe both endpoints independently
    // of setter hooks. Read-only snapshots, no inferred requested-amount debit.
    internal static class EconomyFlowDiagnostics
    {
        internal sealed class Transfer
        {
            internal Func<double> Giver, Receiver;
            internal double GiverBefore, ReceiverBefore;
            internal string GiverId, ReceiverId;
            internal int Requested;
            internal bool RecipientSettlement;
        }
        internal static Transfer Begin(object[] args)
        {
            var transfer = new Transfer { Requested = (int)args[4] };
            transfer.RecipientSettlement = args[2] == null && args[3] is PartyBase && ((PartyBase)args[3]).IsSettlement;
            Endpoint(args[0] as Hero, args[1] as PartyBase, out transfer.Giver, out transfer.GiverId);
            Endpoint(args[2] as Hero, args[3] as PartyBase, out transfer.Receiver, out transfer.ReceiverId);
            if (transfer.Giver != null) transfer.GiverBefore = transfer.Giver();
            if (transfer.Receiver != null) transfer.ReceiverBefore = transfer.Receiver();
            return transfer;
        }
        private static void Endpoint(Hero hero, PartyBase party, out Func<double> read, out string id)
        {
            read = null; id = "external";
            if (hero != null) { read = () => hero.Gold; id = EconomyTransactionDiagnostics.Id(hero); }
            else if (party != null && party.IsMobile)
            {
                MobileParty mobile = party.MobileParty;
                // Lord-party wallet aliases leader gold, not a second account.
                if (mobile.IsLordParty && mobile.LeaderHero != null) { Hero leader = mobile.LeaderHero; read = () => leader.Gold; id = EconomyTransactionDiagnostics.Id(leader); }
                else { read = () => mobile.PartyTradeGold; id = EconomyTransactionDiagnostics.Id(mobile); }
            }
            else if (party != null && party.IsSettlement)
            {
                var component = party.Settlement.SettlementComponent;
                read = () => component.Gold; id = EconomyTransactionDiagnostics.Id(component);
            }
        }
        internal static double Residual(double giverDelta, double receiverDelta, bool aliased)
        {
            return aliased ? giverDelta : giverDelta + receiverDelta;
        }
        internal static bool MatchesSettlementClamp(int requested, double giverDelta, double receiverDelta, double receiverBefore)
        {
            // Native ApplyForSettlementToCharacter uses a negative request;
            // SettlementComponent.ChangeGold floors its balance at zero.
            return requested < 0 && receiverBefore >= 0 && receiverBefore < -(double)requested
                && giverDelta == -(double)requested && receiverDelta == -receiverBefore;
        }
        internal static void End(EconomyTransactionDiagnostics.Call call)
        {
            Transfer t = call.Transfer;
            if (t == null) return;
            double giver = t.Giver == null ? 0 : t.Giver() - t.GiverBefore;
            double receiver = t.Receiver == null ? 0 : t.Receiver() - t.ReceiverBefore;
            bool internalTransfer = t.Giver != null && t.Receiver != null;
            double net = Residual(giver, receiver, t.GiverId == t.ReceiverId);
            string kind = internalTransfer ? "TRANSFER_RECONCILIATION" : "EXTERNAL_CASH_FLOW";
            bool clamp = t.RecipientSettlement && t.GiverId != t.ReceiverId && MatchesSettlementClamp(t.Requested, giver, receiver, t.ReceiverBefore);
            EconomyTransactionDiagnostics.Open(call);
            EconomyTrace.Write(kind, call.Id, call.Parent, t.GiverId + " -> " + t.ReceiverId, "endpoint_net_not_additive", 0, net,
                call.Previous == null ? call.Source : call.Previous.Source,
                "requested=" + t.Requested + "; giverDelta=" + N(giver) + "; receiverDelta=" + N(receiver)
                + "; giverBefore=" + N(t.GiverBefore) + "; receiverBefore=" + N(t.ReceiverBefore)
                + "; matchesNativeSettlementClamp=" + clamp
                + "; aliased=" + (t.GiverId == t.ReceiverId) + "; external_is_not_automatically_bug; nested_event_effects_require_review");
            if (clamp) EconomyTrace.Write("NATIVE_SETTLEMENT_CLAMP", call.Id, call.Parent, t.ReceiverId, "credited_without_matching_debit", 0, net,
                call.Source, "matches_native_negative_payout_and_zero_floor; overlapping_transfer_evidence_not_extra_money; not_balance_acceptance");
            var daily = call.Previous;
            while (daily != null && daily.FinanceHero == null) daily = daily.Previous;
            if (daily != null && t.Giver == null && t.ReceiverId == EconomyTransactionDiagnostics.Id(daily.FinanceHero))
                EconomyTrace.Write("CLAN_CREDIT_CHECK", call.Id, call.Parent, t.ReceiverId, "actual_minus_requested", 0, receiver - t.Requested,
                    "daily-finance-credit", "requested=" + t.Requested + "; actual=" + N(receiver) + "; finalModel=" + (daily.HasFinalFinance ? N(daily.FinalFinance) : "unavailable")
                    + "; clamping_and_other_patches_require_review; not_an_extra_cash_flow");
        }
        private static string N(double value) { return value.ToString("R", CultureInfo.InvariantCulture); }
    }
}
