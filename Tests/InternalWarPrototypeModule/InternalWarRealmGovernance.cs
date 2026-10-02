using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace AgesOfCalradiaInternalWarsTest
{
    internal sealed partial class InternalWarTestBehavior
    {
        private InternalWarRealmRules _realmRules = new InternalWarRealmRules();
        private string _realmRulesPayload = string.Empty;
        private bool _issuingRealmPeace;

        private void SyncRealmGovernance(IDataStore store)
        {
            if (store.IsSaving && string.IsNullOrEmpty(RecoveryBlocker)) _realmRulesPayload = _realmRules.Serialize();
            store.SyncData("AOC_InternalConflict_RealmRules_v1", ref _realmRulesPayload);
            if (!store.IsLoading) return;
            InternalWarRealmRules restored = InternalWarRealmRules.Deserialize(_realmRulesPayload);
            if (restored == null) BlockRecovery("Kingdom private-war policy is invalid; original payload retained.");
            else _realmRules = restored;
        }

        internal bool RealmDeclarationsBanned(string kingdomId) { return _root._realmRules.IsBanned(kingdomId); }
        internal string GetRealmDeclarationBlocker(Kingdom kingdom)
        {
            if (!string.IsNullOrEmpty(RecoveryBlocker)) return RecoveryBlocker;
            if (kingdom == null) return "A kingdom is required for a private war.";
            return RealmDeclarationsBanned(kingdom.StringId) ? "The monarch has forbidden new private wars in this kingdom." : string.Empty;
        }

        internal string GetRealmAuthorityBlocker(string expectedKingdomId)
        {
            if (!string.IsNullOrEmpty(RecoveryBlocker)) return RecoveryBlocker;
            Clan player = Clan.PlayerClan;
            if (player == null || player.Kingdom == null || player.Kingdom.StringId != expectedKingdomId)
                return "Your kingdom changed; reopen the kingdom controls.";
            if (player.IsEliminated || player.Leader == null || !player.Leader.IsAlive || player.Kingdom.RulingClan != player)
                return "Only the current ruling clan may issue a kingdom order.";
            return string.Empty;
        }

        internal void SetRealmDeclarationBan(string expectedKingdomId, bool expectedBanned, bool banned, out string result)
        {
            result = GetRealmAuthorityBlocker(expectedKingdomId);
            if (!string.IsNullOrEmpty(result)) return;
            if (RealmDeclarationsBanned(expectedKingdomId) != expectedBanned)
            { result = "The kingdom policy changed; reopen the controls."; return; }
            _root._realmRules.SetBanned(expectedKingdomId, banned);
            result = banned ? "New private wars are forbidden. Existing wars continue until peace is ordered or agreed."
                : "New private wars are permitted again. Normal declaration requirements still apply.";
            InternalWarTestDiagnostics.Info("ROYAL POLICY: realm=" + expectedKingdomId + "; banned=" + banned);
        }

        internal void RequestRealmPeace(string expectedKingdomId, out string result)
        {
            result = GetRealmAuthorityBlocker(expectedKingdomId);
            if (!string.IsNullOrEmpty(result)) return;
            if (_root._issuingRealmPeace) { result = "A kingdom peace order is already being queued."; return; }
            int queued = 0;
            _root._issuingRealmPeace = true;
            try
            {
                // Snapshot only this realm. Each controller retains native operation/cleanup ownership.
                foreach (InternalConflictRecord war in _root.Conflicts.Where(w => w.KingdomId == expectedKingdomId
                    && w.Phase == InternalConflictPhase.Active).ToArray())
                {
                    if (war.Phase != InternalConflictPhase.Active) continue;
                    string blocker = GetRealmAuthorityBlocker(expectedKingdomId);
                    if (!string.IsNullOrEmpty(blocker))
                    { result = queued + " war(s) queued; remaining orders stopped: " + blocker; return; }
                    string detail;
                    _root.ControllerFor(war).RequestRoyalPeace(true, out detail);
                    if (war.Phase == InternalConflictPhase.PeacePending) queued++;
                    if (!string.IsNullOrEmpty(RecoveryBlocker) || war.Phase != InternalConflictPhase.PeacePending)
                    { result = queued + " war(s) queued; remaining orders stopped: "
                        + (string.IsNullOrEmpty(RecoveryBlocker) ? detail : RecoveryBlocker); return; }
                }
                result = queued + " private war(s) queued for peace. Battles must finish before cleanup; current fief ownership is retained."
                    + " This order does not itself ban later declarations.";
                InternalWarTestDiagnostics.Info("ROYAL REALM PEACE: realm=" + expectedKingdomId + "; queued=" + queued);
            }
            finally { _root._issuingRealmPeace = false; }
        }

        internal string GetRealmOverview()
        {
            Kingdom kingdom = Clan.PlayerClan == null ? null : Clan.PlayerClan.Kingdom;
            if (kingdom == null) return "Your clan does not belong to a kingdom.";
            var rows = new List<string> { "Kingdom: " + kingdom.Name,
                "New private wars: " + (RealmDeclarationsBanned(kingdom.StringId) ? "forbidden by the crown" : "permitted"),
                "NPC declarations: " + (PoliticalAiEnabled ? "enabled" : "disabled"),
                "Recovery: " + (string.IsNullOrEmpty(RecoveryBlocker) ? "no recorded blocker" : RecoveryBlocker) };
            InternalConflictRecord[] wars = _root.Conflicts.Where(w => w.KingdomId == kingdom.StringId && w.IsOpen)
                .OrderBy(w => w.StartDay).ThenBy(w => w.Id).ToArray();
            rows.Add("Open wars: " + wars.Length);
            foreach (InternalConflictRecord war in wars)
            {
                Clan attacker = FindClan(war.AttackerClanId), defender = FindClan(war.DefenderClanId);
                rows.Add((attacker == null ? war.AttackerClanId : attacker.Name.ToString()) + " vs "
                    + (defender == null ? war.DefenderClanId : defender.Name.ToString()) + " — " + war.Phase
                    + "; " + war.DescribeGoal() + "; completed operations=" + war.CompletedOperations);
            }
            return string.Join("\n", rows);
        }
    }
}
