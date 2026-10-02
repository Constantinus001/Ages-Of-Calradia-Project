using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace AgesOfCalradiaInternalWarsTest
{
    internal sealed partial class InternalWarTestBehavior
    {
        private double _nextPoliticalDay;
        private bool _politicalAiEnabled = true;
        internal void TogglePoliticalAi() { _root._politicalAiEnabled = !_root._politicalAiEnabled; }
        internal bool PoliticalAiEnabled { get { return _root._politicalAiEnabled; } }

        private void SyncPoliticalAi(IDataStore store)
        {
            store.SyncData("AOC_InternalConflict_PoliticalAi", ref _politicalAiEnabled);
            store.SyncData("AOC_InternalConflict_NextPoliticalDay", ref _nextPoliticalDay);
            if (store.IsLoading && (double.IsNaN(_nextPoliticalDay) || double.IsInfinity(_nextPoliticalDay) || _nextPoliticalDay < 0))
                BlockRecovery("Political AI schedule is invalid.");
        }

        private void AdvancePoliticalAi()
        {
            if (!string.IsNullOrEmpty(RecoveryBlocker) || CampaignTime.Now.ToDays < _nextPoliticalDay) return;
            _nextPoliticalDay = CampaignTime.Now.ToDays + 7;
            Kingdom kingdom = Clan.PlayerClan == null ? null : Clan.PlayerClan.Kingdom;
            if (kingdom == null) return;
            foreach (InternalConflictRecord war in _conflicts.Records.Where(c => c.Phase == InternalConflictPhase.Active
                && c.KingdomId == kingdom.StringId).ToArray())
            {
                Clan ruler = kingdom.RulingClan;
                bool royalOrder = ruler != null && ruler != Clan.PlayerClan && ruler.StringId != war.AttackerClanId
                    && ruler.StringId != war.DefenderClanId && war.KingdomId == kingdom.StringId
                    && CampaignTime.Now.ToDays - war.StartDay >= 30;
                bool npcSettlement = war.AttackerClanId != Clan.PlayerClan.StringId && war.DefenderClanId != Clan.PlayerClan.StringId
                    && war.GetPeaceOfferBlocker(war.AttackerClanId, (int)CampaignTime.Now.ToDays, false).Length == 0;
                if (!royalOrder && !npcSettlement) continue;
                ControllerFor(war).EndConflict(InternalWarTestState.Lifted,
                    royalOrder ? "The reigning monarch ordered peace." : "The rival clans agreed to status-quo peace.");
                InternalWarTestDiagnostics.Info("NPC PEACE: " + war.Id + "; monarch_order=" + royalOrder);
            }
            if (!_politicalAiEnabled || !string.IsNullOrEmpty(GetRealmDeclarationBlocker(kingdom))
                || _conflicts.Records.Count(c => c.IsOpen) >= 4) return;
            Clan[] clans = Clan.All.Where(c => c.Kingdom == kingdom && !c.IsEliminated && c.Leader != null
                && c.Leader.IsAlive && !c.IsUnderMercenaryService && !c.IsClanTypeMercenary
                && (!c.IsMinorFaction || c == Clan.PlayerClan)).OrderBy(c => c.StringId).ToArray();
            foreach (Clan attacker in clans.Where(c => c != Clan.PlayerClan))
            foreach (Clan defender in clans.Where(c => c != attacker))
            {
                if (_conflicts.Find(attacker.StringId, defender.StringId) != null
                    || FactionManager.IsAtWarAgainstFaction(attacker, defender)) continue;
                // Existing crown disputes justify a feud with the ruling clan, never a grant of its fiefs or crown.
                bool crownDispute = InternalWarSuccessionBridge.HasDisputedClaim(kingdom, attacker, defender);
                if (!crownDispute && attacker.Leader.GetRelation(defender.Leader) > -40) continue;
                // Ninety days between declarations for the same pair; with the sixty-day war limit
                // this also leaves at least thirty days after an ordinary timeout peace.
                if (_conflicts.Records.Any(c => ((c.AttackerClanId == attacker.StringId && c.DefenderClanId == defender.StringId)
                    || (c.DefenderClanId == attacker.StringId && c.AttackerClanId == defender.StringId))
                    && CampaignTime.Now.ToDays - c.StartDay < 90)) continue;
                var war = new InternalConflictRecord { Id = Guid.NewGuid().ToString("N"), KingdomId = kingdom.StringId,
                    AttackerClanId = attacker.StringId, DefenderClanId = defender.StringId, StartDay = (int)CampaignTime.Now.ToDays };
                if (!_conflicts.Add(war)) continue;
                ControllerFor(war);
                try
                {
                    DeclareWarAction.ApplyByDefault(attacker, defender);
                    if (war.Phase != InternalConflictPhase.Active || attacker.Kingdom != kingdom || defender.Kingdom != kingdom
                        || !FactionManager.IsAtWarAgainstFaction(attacker, defender))
                        throw new InvalidOperationException("NPC declaration changed realm or did not establish hostility.");
                    InternalWarTestDiagnostics.Info("NPC DECLARATION: " + war.Id + "; " + attacker.Name + " vs " + defender.Name
                        + "; reason=" + (crownDispute ? "existing succession dispute" : "hostile relations"));
                }
                catch (Exception exception)
                {
                    war.RequestPeace();
                    InternalWarTestDiagnostics.Error("NPC declaration interrupted; clan-specific cleanup queued.", exception);
                }
                return;
            }
        }
    }
}
