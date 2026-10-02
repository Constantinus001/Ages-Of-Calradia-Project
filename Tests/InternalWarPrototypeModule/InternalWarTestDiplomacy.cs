using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace AgesOfCalradiaInternalWarsTest
{
    internal sealed partial class InternalWarTestBehavior
    {
        private string _uncertainPaymentConflictId = string.Empty;
        private void SyncPaymentSafety(IDataStore store)
        {
            // This marker is saved even when invalid payloads must otherwise be retained.
            store.SyncData("AOC_InternalConflict_UncertainPayment", ref _uncertainPaymentConflictId);
            if (store.IsLoading && !string.IsNullOrEmpty(_uncertainPaymentConflictId))
                BlockRecovery("An interrupted payment for " + _uncertainPaymentConflictId + " requires diagnosis; payment will not be retried.");
        }
        internal const int PeaceCompensationGold = 5000;
        internal void OfferCompensation(string expectedConflictId, out string result)
        {
            if (Conflict == null || Conflict.Id != expectedConflictId)
            { result = "The conflict changed; reopen negotiations."; return; }
            result = GetPeaceOfferBlocker(true);
            if (!string.IsNullOrEmpty(result)) return;
            Clan rival = FindClan(Conflict.OpponentOf(Clan.PlayerClan.StringId));
            Hero payer = Hero.MainHero, recipient = rival == null ? null : rival.Leader;
            if (payer == null || !payer.IsAlive || payer.Clan != Clan.PlayerClan || recipient == null || recipient == payer || !recipient.IsAlive
                || recipient.Clan != rival || (long)recipient.Gold + PeaceCompensationGold > int.MaxValue
                || payer.Gold < PeaceCompensationGold || Conflict.CompensationGold != 0)
            { result = "Compensation requires 5,000 gold, an available rival leader, and no previous payment."; return; }
            InternalConflictRecord war = Conflict;
            Clan payerClan = payer.Clan;
            // Stop further offers before crossing native gold callbacks. Native hero gold is saved by Bannerlord.
            RequestRoyalPeace(false, out result);
            if (!string.IsNullOrEmpty(RecoveryBlocker)) { result = RecoveryBlocker; return; }
            if (Conflict != war || war.Phase != InternalConflictPhase.PeacePending) return;
            int before = payer.Gold;
            int recipientBefore = recipient.Gold;
            _uncertainPaymentConflictId = war.Id;
            try
            {
                GiveGoldAction.ApplyBetweenCharacters(payer, recipient, PeaceCompensationGold);
                if (payer.Gold != before - PeaceCompensationGold
                    || (long)recipient.Gold != (long)recipientBefore + PeaceCompensationGold
                    || payer.Clan != payerClan || recipient.Clan != rival || rival.Leader != recipient
                    || payerClan.Kingdom == null || payerClan.Kingdom != rival.Kingdom || payerClan.Kingdom.StringId != war.KingdomId
                    || Conflict != war || war.Phase != InternalConflictPhase.PeacePending
                    || !string.IsNullOrEmpty(RecoveryBlocker))
                    throw new InvalidOperationException("Native compensation transfer changed its participants, peace state, or agreed debit/credit.");
                war.CompensationGold = PeaceCompensationGold;
                war.CompensationPayerId = payerClan.StringId;
                _uncertainPaymentConflictId = string.Empty;
                result = "5,000 gold paid to the rival leader. Peace cleanup is queued; current fief ownership is retained.";
                InternalWarTestDiagnostics.Info("COMPENSATION: " + war.Id + "; recipient=" + recipient.StringId + ". " + result);
            }
            catch (Exception exception)
            {
                // Never retry an uncertain gold transaction automatically.
                BlockRecovery("Compensation transfer was interrupted. Inspect diagnostics before saving; payment will not be retried.");
                InternalWarTestDiagnostics.Error(RecoveryBlocker, exception);
                result = RecoveryBlocker;
            }
        }

        internal string GetPeaceOfferBlocker(bool concede)
        {
            if (!string.IsNullOrEmpty(RecoveryBlocker)) return RecoveryBlocker;
            if (Conflict == null || Clan.PlayerClan == null) return "No private war is available for negotiation.";
            if (Clan.PlayerClan.Kingdom == null || Clan.PlayerClan.Kingdom.StringId != Conflict.KingdomId)
                return "Your clan no longer belongs to the conflict's kingdom.";
            Clan attacker = FindClan(Conflict.AttackerClanId);
            Clan defender = FindClan(Conflict.DefenderClanId);
            if (attacker == null || defender == null || attacker.Kingdom == null
                || attacker.Kingdom != defender.Kingdom || attacker.Kingdom.StringId != Conflict.KingdomId
                || !FactionManager.IsAtWarAgainstFaction(attacker, defender))
                return "The recorded rival, kingdom membership, or clan hostility changed; peace cannot be negotiated.";
            return Conflict.GetPeaceOfferBlocker(Clan.PlayerClan.StringId, (int)Math.Floor(CampaignTime.Now.ToDays), concede);
        }

        internal void OfferPeace(string expectedConflictId, bool concede, out string result)
        {
            if (Conflict == null || Conflict.Id != expectedConflictId)
            {
                result = "The conflict changed while the peace offer was open. Reopen negotiations.";
                return;
            }
            result = GetPeaceOfferBlocker(concede);
            if (!string.IsNullOrEmpty(result)) return;
            // Reuse the battle-safe peace transition; no gold or fief transfers occur in these terms.
            // Concession abandons the remaining claim; both offers retain current settlement owners.
            string conflictId = Conflict.Id;
            RequestRoyalPeace(false, out result);
            if (Conflict != null && Conflict.Id == conflictId && Conflict.Phase == InternalConflictPhase.PeacePending)
            {
                result = concede ? "Concession accepted. Current fief ownership is retained; peace cleanup is queued."
                    : "Status-quo peace accepted. Current fief ownership is retained; peace cleanup is queued.";
                InternalWarTestDiagnostics.Info("NEGOTIATED PEACE: conflict=" + conflictId + "; concession=" + concede + ". " + result);
            }
        }

        internal bool TryDeclare(Clan defender, out string result)
        {
            result = InternalWarTestService.GetDeclarationBlocker();
            if (!string.IsNullOrEmpty(result)) return false;
            Clan attacker = Clan.PlayerClan;
            result = GetRealmDeclarationBlocker(attacker == null ? null : attacker.Kingdom);
            if (!string.IsNullOrEmpty(result)) return false;
            if (attacker == null || defender == null || defender == attacker || defender.Kingdom != attacker.Kingdom
                || defender.IsEliminated || defender.Leader == null || !defender.Leader.IsAlive
                || defender.IsMinorFaction || defender.IsClanTypeMercenary || defender.IsUnderMercenaryService)
            { result = "Choose another regular clan in your kingdom."; return false; }
            if (FactionManager.IsAtWarAgainstFaction(attacker, defender))
            { result = "An existing native clan hostility is not owned by this system; it will not be adopted or cleaned up."; return false; }
            if (_conflicts.Find(attacker.StringId, defender.StringId) != null)
            { result = "An open war already exists between these clans."; return false; }
            if (Conflict != null && !Conflict.IsOpen) _history.Archive(Conflict);
            var war = new InternalConflictRecord { Id = Guid.NewGuid().ToString("N"), KingdomId = attacker.Kingdom.StringId,
                AttackerClanId = attacker.StringId, DefenderClanId = defender.StringId, StartDay = (int)Math.Floor(CampaignTime.Now.ToDays) };
            if (!_conflicts.Add(war)) { result = "The declaration conflicts with an existing record."; return false; }
            InternalWarTestBehavior controller;
            if (Conflict == null && CurrentRecord == null) { Conflict = war; controller = this; }
            else controller = ControllerFor(war);
            _root._selectedController = controller;
            controller._ai.Defer();
            try
            {
                DeclareWarAction.ApplyByDefault(attacker, defender);
                if (war.Phase != InternalConflictPhase.Active || !FactionManager.IsAtWarAgainstFaction(attacker, defender)
                    || attacker.Kingdom == null || attacker.Kingdom != defender.Kingdom || attacker.Kingdom.StringId != war.KingdomId)
                    throw new InvalidOperationException("Declaration was interrupted or its realm/stance changed.");
                result = "Internal war declared against " + defender.Name + ". Both clans remain in " + attacker.Kingdom.Name
                    + ". Field combat and raids are available; your first selected fortification becomes the settlement claim.";
                InternalWarTestDiagnostics.Info("CONFLICT DECLARED: " + war.Id + "; " + attacker.StringId + " vs " + defender.StringId);
                return true;
            }
            catch (Exception exception)
            {
                war.RequestPeace();
                InternalWarTestDiagnostics.Error("Declaration failed; political cleanup queued.", exception);
                result = "Declaration failed; cleanup is queued. Write a diagnostic snapshot.";
                return false;
            }
        }
    }
}
