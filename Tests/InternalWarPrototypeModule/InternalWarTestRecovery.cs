using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace AgesOfCalradiaInternalWarsTest
{
    // Native lifecycle adapter. Only the application map tick may drain queued cleanup.
    // Callbacks can arrive before native teardown; a reentrancy barrier prevents nested drains.
    internal sealed partial class InternalWarTestBehavior
    {
        private string _conflictPayload = string.Empty;
        private string _historyPayload = string.Empty;
        private InternalWarHistory _history = new InternalWarHistory();
        internal string GetWarHistory() { return _history.DescribeRecent(20); }
        private Campaign _owningCampaign = Campaign.Current;
        private bool _draining;
        private float _drainDelay;
        internal InternalConflictRecord Conflict { get; private set; }
        private readonly InternalWarRecoveryState _recoveryState = new InternalWarRecoveryState();
        internal string RecoveryBlocker { get { return _root._recoveryState.Reason; } }

        private void SyncConflict(IDataStore dataStore)
        {
            if (dataStore.IsSaving && string.IsNullOrEmpty(RecoveryBlocker))
                _conflictPayload = Conflict == null ? string.Empty : Conflict.Serialize();
            dataStore.SyncData("AOC_InternalConflict_v1", ref _conflictPayload);
            if (_root == this)
            {
                if (dataStore.IsSaving && string.IsNullOrEmpty(RecoveryBlocker)) _historyPayload = _history.Serialize();
                dataStore.SyncData("AOC_InternalConflict_History_v1", ref _historyPayload);
            }
            _ai.SyncData(dataStore);
            if (!dataStore.IsLoading) return;
            InternalWarHistory restoredHistory = _root == this ? InternalWarHistory.Deserialize(_historyPayload) : _root._history;
            if (restoredHistory == null) BlockRecovery("War history is invalid; original payload retained.");
            else _history = restoredHistory;
            Conflict = InternalConflictRecord.Deserialize(_conflictPayload);
            if (!string.IsNullOrWhiteSpace(_conflictPayload) && Conflict == null)
                BlockRecovery("The saved political conflict is invalid; original payload retained. New operations are disabled.");
            // No ledger in v1/v2 saves means legacy one-shot semantics, never an implicit new war.
        }

        internal void BlockRecovery(string reason)
        {
            if (!_root._recoveryState.TryBlock(reason)) return;
            InternalWarTestDiagnostics.Info("TEST RECOVERY BLOCKED: " + RecoveryBlocker);
        }

        internal void DrainCleanup(float dt)
        {
            if (_root == this)
                foreach (InternalWarTestBehavior controller in Controllers.Where(c => c != this && c.HasPendingWork).ToArray()) controller.DrainCleanup(dt);
            if (_root == this && Campaign.Current != null && Campaign.Current == _owningCampaign) _monitor.Tick(dt);
            if (Campaign.Current == _owningCampaign && !_draining)
            {
                try { _raids.AdvanceRaid(); }
                catch (Exception exception) { BlockRecovery("Raid recovery failed: " + exception.Message); }
            }
            if (_draining || Campaign.Current == null || Campaign.Current != _owningCampaign
                || !string.IsNullOrEmpty(_uncertainPaymentConflictId)
                || !string.IsNullOrEmpty(RecoveryBlocker)
                || (CurrentRecord == null ? Conflict == null || Conflict.Phase != InternalConflictPhase.PeacePending
                    : CurrentRecord.State != InternalWarTestState.RoyalPeacePending)) return;
            _drainDelay -= Math.Max(0, dt);
            if (_drainDelay > 0) return;
            _drainDelay = 1;
            // Do not finish missions, post-battle results or another active map event from a callback.
            if (Mission.Current != null || Game.Current == null || !(Game.Current.GameStateManager.ActiveState is MapState)
                || MapEvent.PlayerMapEvent != null || PlayerEncounter.Battle != null) return;
            _draining = true;
            try
            {
                if (CurrentRecord == null)
                {
                    if (HasActiveRaid || HasConflictFieldBattles(Conflict)) return;
                    EndDiplomacy(FindClan(Conflict.AttackerClanId), FindClan(Conflict.DefenderClanId));
                    Conflict.Phase = InternalConflictPhase.Closed;
                    _history.Archive(Conflict);
                    InternalWarTestDiagnostics.Info("CONFLICT CLOSED: " + Conflict.Id);
                }
                else _cleanup.ContinuePendingCleanup();
            }
            catch (Exception exception)
            {
                FailOperationalConflict("Deferred map cleanup failed.", exception);
                _drainDelay = 30; // Wall-clock backoff also works while campaign time is paused.
            }
            finally { _draining = false; }
        }

        private bool ValidateOpenConflict()
        {
            Clan attacker = FindClan(Conflict.AttackerClanId);
            Clan defender = FindClan(Conflict.DefenderClanId);
            if (Conflict.Phase == InternalConflictPhase.PeacePending) return true;
            if (attacker == null || defender == null || attacker.Kingdom == null
                || attacker.Kingdom != defender.Kingdom || attacker.Kingdom.StringId != Conflict.KingdomId
                || !FactionManager.IsAtWarAgainstFaction(attacker, defender))
            {
                InvalidateConflict("The ongoing conflict lost its original clan, kingdom or hostility contract.");
                return false;
            }
            if (CampaignTime.Now.ToDays - Conflict.StartDay > MaximumConflictDays)
            {
                EndConflict(InternalWarTestState.TimedOut, "The continuing conflict reached its 60-day safety limit.");
                return false;
            }
            return true;
        }

        private static bool HasOwnedResidue(InternalWarTestRecord record)
        {
            Settlement target = FindSettlement(record.SettlementId);
            MobileParty leader = FindParty(record.LeaderPartyId);
            SiegeEvent siege = target == null ? null : target.SiegeEvent;
            return (siege != null && siege.BesiegerCamp != null && siege.BesiegerCamp.LeaderParty != null
                    && siege.BesiegerCamp.LeaderParty.StringId == record.LeaderPartyId)
                || (leader != null && leader.BesiegerCamp != null && leader.BesiegerCamp.SiegeEvent != null
                    && leader.BesiegerCamp.SiegeEvent.BesiegedSettlement != null
                    && leader.BesiegerCamp.SiegeEvent.BesiegedSettlement.StringId == record.SettlementId)
                || (leader != null && leader.Party.MapEvent != null && leader.Party.MapEvent.MapEventSettlement != null
                    && leader.Party.MapEvent.MapEventSettlement.StringId == record.SettlementId)
                || (PlayerSiege.PlayerSiegeEvent != null && PlayerSiege.PlayerSiegeEvent.BesiegedSettlement != null
                    && PlayerSiege.PlayerSiegeEvent.BesiegedSettlement.StringId == record.SettlementId);
        }

        internal bool CleanupPostconditions(Settlement target, MobileParty leader, MenuContext menuContext, GameMenu menu)
        {
            if (HasActiveRaid || HasConflictFieldBattles(Conflict)) return false;
            if (HasOwnedResidue(CurrentRecord)) return false;
            if (menuContext != null && Campaign.Current.CurrentMenuContext == menuContext && menuContext.GameMenu == menu)
                return false;
            MenuContext current = Campaign.Current.CurrentMenuContext;
            if (leader == MobileParty.MainParty && current != null && current.GameMenu != null
                && current.GameMenu.StringId == "menu_siege_strategies") return false;
            // Observe, but never forcibly dismiss, native aftermath/encounters whose ownership is uncertain.
            if (leader == MobileParty.MainParty && PlayerEncounter.Current != null
                && PlayerEncounter.EncounterSettlement == target) return false;
            if (Conflict == null || Conflict.Phase != InternalConflictPhase.Active)
            {
                Clan attacker = FindClan(CurrentRecord.AttackerClanId);
                Clan defender = FindClan(CurrentRecord.DefenderClanId);
                if (attacker != null && defender != null && FactionManager.IsAtWarAgainstFaction(attacker, defender)) return false;
            }
            return true;
        }

        internal static bool HasConflictFieldBattles(InternalConflictRecord conflict)
        {
            foreach (MobileParty party in MobileParty.All)
                if (InternalWarCombatService.IsBattleOfConflict(party.Party.MapEvent, conflict)) return true;
            return false;
        }

        internal void CompleteCleanup(InternalWarTestRecord record, InternalWarTestState finalState)
        {
            if (record != CurrentRecord || record.State != InternalWarTestState.RoyalPeacePending) return;
            record.State = record.CaptureApplied ? InternalWarTestState.Captured : finalState;
            record.CleanupComplete = true;
            if (Conflict != null) Conflict.CompleteOperation(record);
            if (Conflict != null && !Conflict.IsOpen) _history.Archive(Conflict);
            _nextCleanupAttemptHour = 0;
            _lastCleanupFailure = string.Empty;
            _ai.Defer();
            InternalWarTestDiagnostics.Info("OPERATION CLEAN: id=" + record.ConflictId + ", outcome=" + record.State
                + ", conflict=" + (Conflict == null ? "legacy closed" : Conflict.Phase.ToString()));
        }
    }
}
