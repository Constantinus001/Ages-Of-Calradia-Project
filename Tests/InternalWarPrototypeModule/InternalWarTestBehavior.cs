using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;

namespace AgesOfCalradiaInternalWarsTest
{
    internal sealed partial class InternalWarTestBehavior : CampaignBehaviorBase
    {
        private const string SaveKey = "AOC_InternalWar_Test_v1";
        private const int MaximumMarchDays = 14;
        private const int MaximumConflictDays = 60;
        private const int CleanupRetryHours = 6;
        private string _payload = string.Empty;
        private double _nextCleanupAttemptHour;
        private string _lastCleanupFailure = string.Empty;
        internal InternalWarTestRecord CurrentRecord { get; private set; }
        private readonly InternalWarRaidCoordinator _raids;
        private readonly InternalWarSiegeAiCoordinator _ai;
        private readonly InternalWarSiegeCleanup _cleanup;
        private readonly InternalWarDiagnosticMonitor _monitor;
        internal bool AutonomousAi { get { return _ai.AutonomousAi; } }
        internal void ToggleAutonomousAi() { _ai.ToggleAutonomousAi(); }
        internal InternalWarRaidRecord Raid { get { return _raids.Raid; } }
        internal bool HasActiveRaid { get { return _raids.HasActiveRaid; } }
        internal string GetRaidBlocker(Settlement target) { return _raids.GetRaidBlocker(target); }
        internal bool TryBeginRaid(Settlement target, out string result) { return _raids.TryBeginRaid(target, out result); }
        internal bool IsRaidHostility(PartyBase attacker, PartyBase defender) { return _raids.IsRaidHostility(attacker, defender); }
        internal bool IsRaidBattle(MapEvent battle) { return _raids.IsRaidBattle(battle); }
        internal bool CanStartNpcRaid(MobileParty party, Settlement target) { return _raids.CanStartNpcRaid(party, target); }
        internal bool TryBeginNpcRaid(MobileParty party, Settlement target) { return _raids.TryBeginNpcRaid(party, target); }

        internal InternalWarTestBehavior() : this(null) { }

        private InternalWarTestBehavior(InternalWarTestBehavior root)
        {
            _root = root ?? this;
            _controllers = root == null ? new System.Collections.Generic.List<InternalWarTestBehavior> { this } : root._controllers;
            if (root != null) { _conflicts = root._conflicts; _history = root._history; }
            _raids = new InternalWarRaidCoordinator(this);
            _ai = new InternalWarSiegeAiCoordinator(this);
            _cleanup = new InternalWarSiegeCleanup(this);
            _monitor = new InternalWarDiagnosticMonitor(this);
            if (root == null) InternalWarTestService.Attach(this);
        }

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.OnSiegeEventStartedEvent.AddNonSerializedListener(this, OnSiegeStarted);
            CampaignEvents.AfterSiegeCompletedEvent.AddNonSerializedListener(this, OnAfterSiegeCompleted);
        }

        public override void SyncData(IDataStore dataStore)
        {
            if (_root == this && dataStore.IsLoading)
            { _controllers.RemoveAll(c => c != this); _selectedController = this; }
            if (_root == this && dataStore.IsLoading) _recoveryState.SyncData(dataStore);
            SyncPaymentSafety(dataStore);
            if (dataStore.IsSaving && string.IsNullOrEmpty(RecoveryBlocker)) _payload = CurrentRecord == null ? string.Empty : CurrentRecord.Serialize();
            dataStore.SyncData(SaveKey, ref _payload);
            SyncConflict(dataStore);
            if (_root == this) { SyncRegistry(dataStore); SyncPoliticalAi(dataStore); SyncRealmGovernance(dataStore); }
            _raids.SyncRaid(dataStore);
            if (dataStore.IsLoading)
            {
                CurrentRecord = InternalWarTestRecord.Deserialize(_payload);
                if (!string.IsNullOrWhiteSpace(_payload) && CurrentRecord == null)
                    BlockRecovery("The saved siege record is invalid; retained unchanged for diagnosis.");
                if (Conflict != null && ((CurrentRecord == null && !string.IsNullOrEmpty(Conflict.ActiveOperationId)
                    && (Conflict.CompletedOperations == 0 || Conflict.ActiveOperationId != Conflict.LastCompletedOperationId))
                    || (CurrentRecord != null && (!Conflict.Matches(CurrentRecord) || Conflict.ActiveOperationId != CurrentRecord.ConflictId))))
                    BlockRecovery("The saved conflict and operation identities disagree.");
                if (_root == this) InternalWarTestService.Attach(this);
            }
            SyncControllers(dataStore);
            // Save last so any safety failure encountered while syncing a controller is retained.
            if (_root == this && dataStore.IsSaving) _recoveryState.SyncData(dataStore);
            if (_root == this) InternalWarTestDiagnostics.Info("AUDIT PAYLOAD: " + (dataStore.IsLoading ? "load" : "save")
                + "; controllers=" + Controllers.Count() + "; recovery_blocked=" + !string.IsNullOrEmpty(RecoveryBlocker)
                + "; payload callback only, not native save completion");
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            _owningCampaign = Campaign.Current;
            InternalWarTestMenu.Register(starter, this);
            try { ReconcileAfterLoad(); }
            catch (Exception exception) { FailOperationalConflict("Saved test cleanup failed during session launch.", exception); }
            foreach (InternalWarTestBehavior controller in Controllers.Where(c => c != this).ToArray())
            {
                controller._owningCampaign = Campaign.Current;
                try { controller.ReconcileAfterLoad(); }
                catch (Exception exception) { controller.FailOperationalConflict("Child war recovery failed.", exception); }
            }
        }

        internal bool TryStart(Clan attacker, Settlement target, out string result)
        {
            result = ValidateStart(attacker, target);
            if (!string.IsNullOrEmpty(result)) return false;
            Clan defender = target.OwnerClan;
            MobileParty party = attacker == Clan.PlayerClan ? MobileParty.MainParty : attacker.Leader.PartyBelongedTo;
            int day = (int)Math.Floor(CampaignTime.Now.ToDays);
            bool continuing = Conflict != null && Conflict.Phase == InternalConflictPhase.Active;
            if (!continuing && !TryDeclare(defender, out result)) return false;
            if (!continuing && SelectedController != this) return SelectedController.TryStart(attacker, target, out result);
            if (continuing && !ValidateOpenConflict())
            {
                result = "The existing conflict no longer permits another operation; peace/recovery is queued.";
                return false;
            }
            if (attacker.StringId == Conflict.AttackerClanId && !Conflict.TryAssignSettlementClaim(target.StringId, out result)) return false;
            CurrentRecord = new InternalWarTestRecord
            {
                ConflictId = Guid.NewGuid().ToString("N"),
                KingdomId = attacker.Kingdom.StringId,
                AttackerClanId = attacker.StringId,
                DefenderClanId = defender.StringId,
                SettlementId = target.StringId,
                LeaderPartyId = party.StringId,
                State = InternalWarTestState.Marching,
                StartDay = day
            };
            Conflict.ActiveOperationId = CurrentRecord.ConflictId;
            _nextCleanupAttemptHour = 0;
            _lastCleanupFailure = string.Empty;
            _cleanup.Reset();

            try
            {
                ExitSettlementForTest(party);
                if (party != MobileParty.MainParty || attacker != Clan.PlayerClan
                    || party.Army != null || party.Party.MapEvent != null || party.BesiegerCamp != null
                    || target.OwnerClan != defender || Conflict.Phase != InternalConflictPhase.Active)
                    throw new InvalidOperationException("The main party became unavailable while leaving the settlement.");
                party.SetMoveGoToSettlement(target, MobileParty.NavigationType.Default, false);
                result = "TEST: " + attacker.Name + " is travelling to " + target.Name + ", held by " + defender.Name
                    + ". This is now the war's settlement claim. At the target, choose [TEST] Begin siege of this settlement.";
                InternalWarTestDiagnostics.Info(result);
                return true;
            }
            catch (Exception exception)
            {
                FailOperationalConflict("Native conflict start failed; cleanup remains pending.", exception);
                result = "The internal-war test could not start. Cleanup is pending; use emergency cleanup and inspect the test log.";
                return false;
            }
        }

        internal bool TryBeginSiege(Settlement target, out string result)
        {
            result = InternalWarTestService.GetBeginSiegeBlocker(target);
            if (!string.IsNullOrEmpty(result)) return false;
            InternalWarTestRecord record = CurrentRecord;
            if (record == null || record != InternalWarTestService.Current)
            {
                result = "The test record changed before the siege could begin.";
                return false;
            }
            MobileParty party = MobileParty.MainParty;
            try
            {
                InternalWarTestDiagnostics.Info("TEST PLAYER SIEGE BEGIN: target=" + target.StringId + ", party=" + party.StringId + ".");
                // v1.4.8 EncounterGameMenuBehavior.game_menu_town_town_besiege_on_consequence
                // finishes the encounter before creating the camp and activating the player siege UI.
                // Friendly town menus can lack PlayerEncounter.Current, so finish any remaining settlement exit explicitly.
                ExitSettlementForTest(party);
                string targetBlocker = InternalWarTestService.GetTargetStartBlocker(target);
                if (record != CurrentRecord || record.State != InternalWarTestState.Marching
                    || party != MobileParty.MainParty || party.Army != null || party.Party.MapEvent != null || party.BesiegerCamp != null
                    || Clan.PlayerClan == null || Clan.PlayerClan.StringId != record.AttackerClanId
                    || Clan.PlayerClan.Kingdom == null || Clan.PlayerClan.Kingdom.StringId != record.KingdomId
                    || !string.IsNullOrEmpty(targetBlocker) || target.OwnerClan.StringId != record.DefenderClanId)
                    throw new InvalidOperationException("The registered siege identities or availability changed during settlement exit. " + targetBlocker);
                SiegeEvent siege = Campaign.Current.SiegeEventManager.StartSiegeEvent(target, party);
                EnsurePlayerSiegeIsCurrent(record, target, party, siege);
                PlayerSiege.StartPlayerSiege(BattleSideEnum.Attacker);
                EnsurePlayerSiegeIsCurrent(record, target, party, siege);
                PlayerSiege.StartSiegePreparation();
                EnsurePlayerSiegeIsCurrent(record, target, party, siege);
                result = "TEST: The siege of " + target.Name + " has begun. Prepare the camp, then use the native Lead an assault option.";
                InternalWarTestDiagnostics.Info("TEST PLAYER SIEGE READY: target=" + target.StringId + "; native preparation menu opened.");
                return true;
            }
            catch (Exception exception)
            {
                if (record == CurrentRecord && record.IsOperational)
                {
                    FailOperationalConflict("Native player siege start failed; cleanup remains pending.", exception);
                    result = "The siege could not begin safely. Cleanup is pending; use emergency cleanup or reload the pre-test save.";
                }
                else
                {
                    InternalWarTestDiagnostics.Error("Player siege start was interrupted after its record closed or changed; no new test state was committed.", exception);
                    result = "The siege start was interrupted because the test ended or changed. Check the test status and log.";
                }
                return false;
            }
        }

        private void EnsurePlayerSiegeIsCurrent(InternalWarTestRecord record, Settlement target, MobileParty party, SiegeEvent siege)
        {
            Clan playerClan = Clan.PlayerClan;
            if (record != CurrentRecord || record.State != InternalWarTestState.Preparing
                || playerClan == null || playerClan.StringId != record.AttackerClanId
                || playerClan.Kingdom == null || playerClan.Kingdom.StringId != record.KingdomId
                || target.OwnerClan == null || target.OwnerClan.StringId != record.DefenderClanId
                || target.OwnerClan.Kingdom != playerClan.Kingdom
                || party != MobileParty.MainParty || InternalWarTestService.ResolveClan(party) != playerClan
                || siege == null || target.SiegeEvent != siege || siege.BesiegerCamp == null
                || siege.BesiegerCamp.LeaderParty != party || party.BesiegerCamp != siege.BesiegerCamp)
                throw new InvalidOperationException("The native player siege no longer matches the active recorded test.");
        }

        private static void ExitSettlementForTest(MobileParty party)
        {
            if (party != MobileParty.MainParty) throw new InvalidOperationException("Only the main party may leave for this test.");
            // Finish also closes the friendly town menu when PlayerEncounter.Current is null.
            PlayerEncounter.Finish(true);
            if (party.CurrentSettlement != null) PlayerEncounter.LeaveSettlement();
            if (party != MobileParty.MainParty || party.CurrentSettlement != null || PlayerEncounter.Current != null)
                throw new InvalidOperationException("Native settlement exit did not return the player to the campaign map.");
        }

        internal void RequestRoyalPeace(bool requireMonarch, out string result)
        {
            InternalWarTestRecord record = CurrentRecord;
            if ((record == null || !record.IsOperational) && (Conflict == null || !Conflict.IsOpen))
            {
                result = "No operational internal-war test exists.";
                return;
            }
            Clan attacker = FindClan(Conflict == null ? record.AttackerClanId : Conflict.AttackerClanId);
            if (requireMonarch && (attacker == null || attacker.Kingdom == null || attacker.Kingdom.RulingClan != Clan.PlayerClan
                || attacker.Kingdom.StringId != (Conflict == null ? record.KingdomId : Conflict.KingdomId)))
            {
                result = "Only the kingdom's ruling clan can issue the monarch peace order. Use the TEST emergency abort if needed.";
                return;
            }
            try
            {
                _nextCleanupAttemptHour = 0;
                _lastCleanupFailure = string.Empty;
                if (Conflict != null) Conflict.RequestPeace();
                if (HasActiveRaid) Raid.StopRequested = true;
                if (record != null) record.RequestCompletion(InternalWarTestState.Lifted);
                if (record == null)
                {
                    result = "Royal peace is queued; existing clan battles must finish safely.";
                    return;
                }
                if (record.State == InternalWarTestState.RoyalPeacePending)
                {
                    result = "Peace is queued until the active battle exits safely. Cleanup remains available.";
                    InternalWarTestDiagnostics.Info("TEST PEACE PENDING: " + result);
                    return;
                }
                result = requireMonarch
                    ? "The monarch declared peace and the test siege was lifted."
                    : "Emergency cleanup ended the feud and lifted the test siege.";
            }
            catch (Exception exception)
            {
                FailOperationalConflict("Peace cleanup crossed a failing native boundary.", exception);
                result = "Cleanup is still pending after a native error. Automatic assaults are stopped; retry emergency cleanup or reload the pre-test save.";
            }
        }

        internal string GetStatusText()
        {
            InternalWarTestRecord record = CurrentRecord;
            if (record == null)
            {
                if (Conflict != null) return "Conflict: " + Conflict.Phase + " / " + Conflict.Id
                    + "\nNo siege operation selected. Choose a rival-clan settlement, fight their parties, or raid their villages."
                    + "\nWar goal: " + Conflict.DescribeGoal()
                    + "\nAutonomous siege AI: " + AutonomousAi + "\nRecovery: " + RecoveryBlocker;
                string blocker = InternalWarTestService.GetStartBlocker();
                return "No internal-war test has been started in this campaign.\n"
                    + (string.IsNullOrEmpty(blocker)
                        ? "Ready: your clan can choose an internal siege target."
                        : "Cannot start: " + blocker);
            }
            Clan attacker = FindClan(record.AttackerClanId);
            Clan defender = FindClan(record.DefenderClanId);
            Settlement target = FindSettlement(record.SettlementId);
            bool originalKingdom = attacker != null && defender != null && attacker.Kingdom != null
                && attacker.Kingdom == defender.Kingdom && attacker.Kingdom.StringId == record.KingdomId;
            return "Conflict: " + (Conflict == null ? "legacy single-siege" : Conflict.Phase + " / " + Conflict.Id)
                + "\nCompleted operations: " + (Conflict == null ? 0 : Conflict.CompletedOperations)
                + "\nWar goal: " + (Conflict == null ? "legacy single-siege" : Conflict.DescribeGoal())
                + "\nRecovery: " + (string.IsNullOrEmpty(RecoveryBlocker) ? "no payload error" : RecoveryBlocker)
                + "\nCleanup verified: " + record.CleanupComplete
                + "\nState: " + record.State + "\nAttacker: " + NameOf(attacker) + "\nDefender: " + NameOf(defender)
                + "\nAttacker kingdom: " + KingdomOf(attacker) + "\nDefender kingdom: " + KingdomOf(defender)
                + "\nExpected kingdom ID: " + record.KingdomId + "\nBoth in original kingdom: " + (originalKingdom ? "YES" : "NO")
                + "\nTarget: " + NameOf(target) + "\nStarted on campaign day: " + record.StartDay + "."
                + (record.State == InternalWarTestState.Marching
                    ? "\nNext: enter the target's town or castle menu and choose [TEST] Begin siege of this settlement.\n"
                        + (string.IsNullOrEmpty(InternalWarTestService.GetBeginSiegeBlocker(target))
                            ? "Ready to begin the siege here."
                            : InternalWarTestService.GetBeginSiegeBlocker(target))
                    : string.Empty)
                + (record.State == InternalWarTestState.Preparing
                    ? "\nPrepare the siege camp, then choose the native Lead an assault option when ready."
                    : string.Empty)
                + (record.State == InternalWarTestState.RoyalPeacePending ? "\nPending result: " + record.PendingFinalState + "." : string.Empty);
        }

        internal void MarkCapture(Settlement settlement)
        {
            try
            {
                InternalWarTestRecord record = CurrentRecord;
                if (record == null || !record.IsOperational || settlement == null || record.SettlementId != settlement.StringId) return;
                Clan attacker = FindClan(record.AttackerClanId);
                if (attacker == null || settlement.OwnerClan != attacker) return;
                if (record.CaptureApplied && record.State == InternalWarTestState.RoyalPeacePending
                    && record.PendingFinalState == InternalWarTestState.Captured) return;
                record.CaptureApplied = true;
                if (Conflict != null && Conflict.MarkGoalSatisfied(settlement.StringId, attacker.StringId))
                    InternalWarTestDiagnostics.Info("WAR GOAL ACHIEVED: " + settlement.StringId + "; the settlement claim was captured.");
                record.RequestCompletion(InternalWarTestState.Captured);
                InternalWarTestDiagnostics.Info("TEST CAPTURE: " + settlement.Name + " transferred to " + attacker.Name + ".");
            }
            catch (Exception exception) { FailOperationalConflict("Capture finalization failed.", exception); }
        }

        private void OnHourlyTick()
        {
            if (_root == this)
                foreach (InternalWarTestBehavior controller in Controllers.Where(c => c != this && c.HasPendingWork).ToArray()) controller.OnHourlyTick();
            try
            {
                if (CampaignTime.Now.ToHours >= _nextCleanupAttemptHour) _drainDelay = 0;
                if (_root == this) AdvancePoliticalAi();
                AdvanceConflict();
            }
            catch (Exception exception) { FailOperationalConflict("Hourly conflict coordination failed.", exception); }
        }

        private void AdvanceConflict()
        {
            InternalWarTestRecord record = CurrentRecord;
            if (!string.IsNullOrEmpty(RecoveryBlocker)) return;
            if (Conflict != null && Conflict.IsOpen && !ValidateOpenConflict()) return;
            if (record == null) { _ai.AdvanceAiSieges(); return; }
            if (!record.IsOperational)
            {
                _ai.AdvanceAiSieges();
                return;
            }
            if (record.State == InternalWarTestState.RoyalPeacePending)
            {
                return;
            }
            Settlement target = FindSettlement(record.SettlementId);
            MobileParty leader = FindParty(record.LeaderPartyId);
            Clan attacker = FindClan(record.AttackerClanId);
            Clan defender = FindClan(record.DefenderClanId);
            if (target == null || leader == null || !leader.IsActive || attacker == null || defender == null)
            {
                InvalidateConflict("A saved clan, party, or settlement no longer exists.");
                return;
            }
            if (InternalWarTestService.ResolveClan(leader) != attacker || attacker.Kingdom == null
                || attacker.Kingdom != defender.Kingdom || attacker.Kingdom.StringId != record.KingdomId)
            {
                InvalidateConflict("The player, party, or kingdom identities changed during the test.");
                return;
            }
            if (target.OwnerClan == attacker)
            {
                InvalidateConflict("Target ownership changed without an authorized siege result.");
                return;
            }
            if (target.OwnerClan != defender)
            {
                InvalidateConflict("The target changed to an unrelated owner during the test.");
                return;
            }
            int elapsedDays = (int)Math.Floor(CampaignTime.Now.ToDays) - record.StartDay;
            if (elapsedDays > MaximumConflictDays || (record.State == InternalWarTestState.Marching && elapsedDays > MaximumMarchDays))
            {
                EndConflict(InternalWarTestState.TimedOut, "The test exceeded its safety timeout and was closed.");
                return;
            }
            SiegeEvent siege = target.SiegeEvent;
            if (siege == null)
            {
                if (record.State != InternalWarTestState.Marching)
                {
                    InvalidateConflict("The test siege ended before its result was recorded; it will not be restarted.");
                    return;
                }
                // Player parties need ordinary settlement travel. Native AI does not convert their
                // BesiegeSettlement order into a settlement encounter; the target menu owns the siege start.
                if (leader.CurrentSettlement != null || leader.Army != null || leader.Party.MapEvent != null
                    || leader.BesiegerCamp != null || PlayerEncounter.Current != null) return;
                if (leader != MobileParty.MainParty)
                {
                    if (leader.DefaultBehavior != AiBehavior.BesiegeSettlement || leader.TargetSettlement != target)
                        leader.SetMoveBesiegeSettlement(target, MobileParty.NavigationType.Default);
                    return;
                }
                if (leader.DefaultBehavior != AiBehavior.GoToSettlement || leader.TargetSettlement != target || leader.IsTargetingPort)
                    leader.SetMoveGoToSettlement(target, MobileParty.NavigationType.Default, false);
                return;
            }
            if (siege.BesiegerCamp == null || siege.BesiegerCamp.LeaderParty != leader)
            {
                InvalidateConflict("Another party controls the target's siege camp.");
                return;
            }
            // The native player menu owns encounter creation and assault/mission setup.
            // Only observe its battle here; directly creating an AI assault would skip that player flow.
            MapEvent siegeBattle = target.Party.MapEvent;
            if (siegeBattle != null && siegeBattle.IsSiegeAssault && leader.Party.MapEvent == siegeBattle)
                record.State = InternalWarTestState.Assaulting;
            else if (siegeBattle == null && leader.Party.MapEvent == null)
            {
                record.State = InternalWarTestState.Preparing;
                if (AutonomousAi) InternalWarSallyService.TryAutomatic(target);
            }
        }

        private void OnAfterSiegeCompleted(Settlement settlement, MobileParty attackerParty, bool isWin, MapEvent.BattleTypes battleType)
        {
            if (_root == this)
                foreach (InternalWarTestBehavior controller in Controllers.Where(c => c != this).ToArray())
                    controller.OnAfterSiegeCompleted(settlement, attackerParty, isWin, battleType);
            try
            {
                InternalWarTestRecord record = CurrentRecord;
                if (record == null || !record.IsOperational || settlement == null || settlement.StringId != record.SettlementId) return;
                Clan attackingClan = InternalWarTestService.ResolveClan(attackerParty);
                if (battleType == MapEvent.BattleTypes.SallyOut)
                {
                    if (attackingClan != null && attackingClan.StringId == record.DefenderClanId && record.ApplyNonCapturingSiegeResult(isWin))
                        InternalWarTestDiagnostics.Info("SALLY RESULT: operation=" + record.ConflictId + ", besieger_won=" + isWin);
                    return;
                }
                if (battleType == MapEvent.BattleTypes.SiegeOutside)
                {
                    if (attackingClan != null && attackingClan.StringId == record.AttackerClanId && record.ApplyNonCapturingSiegeResult(isWin))
                        InternalWarTestDiagnostics.Info("RELIEF RESULT: operation=" + record.ConflictId + ", besieger_won=" + isWin);
                    return;
                }
                if (attackingClan == null || attackingClan.StringId != record.AttackerClanId) return;
                if (isWin && record.CaptureApplied) MarkCapture(settlement);
                else if (isWin) InvalidateConflict("Siege victory lacked an authorized ownership transaction.");
                else EndConflict(InternalWarTestState.Defeated, "The player's clan did not win the siege; the test feud was closed.");
            }
            catch (Exception exception) { FailOperationalConflict("Siege completion cleanup failed.", exception); }
        }

        private void OnSiegeStarted(SiegeEvent siege)
        {
            if (_root == this)
                foreach (InternalWarTestBehavior controller in Controllers.Where(c => c != this).ToArray()) controller.OnSiegeStarted(siege);
            try
            {
                InternalWarTestRecord record = CurrentRecord;
                if (record == null || !record.IsOperational || record.State == InternalWarTestState.RoyalPeacePending
                    || siege == null || siege.BesiegedSettlement == null
                    || siege.BesiegedSettlement.StringId != record.SettlementId || siege.BesiegerCamp == null) return;
                MobileParty leader = siege.BesiegerCamp.LeaderParty;
                Clan leaderClan = InternalWarTestService.ResolveClan(leader);
                if (leader != null && leader.StringId == record.LeaderPartyId
                    && leaderClan != null && leaderClan.StringId == record.AttackerClanId)
                {
                    record.LeaderPartyId = leader.StringId;
                    record.State = InternalWarTestState.Preparing;
                    InternalWarTestDiagnostics.Info("TEST NATIVE SIEGE STARTED: " + siege.BesiegedSettlement.Name + ".");
                }
            }
            catch (Exception exception) { FailOperationalConflict("Siege-start processing failed.", exception); }
        }


        private void ReconcileAfterLoad()
        {
            InternalWarTestRecord record = CurrentRecord;
            if (record == null || !string.IsNullOrEmpty(RecoveryBlocker)) return;
            if (!record.IsOperational)
            {
                // Revalidate terminal saves without assuming an unrelated current encounter is ours.
                if (!record.CleanupComplete || HasOwnedResidue(record))
                    record.RequestCompletion(record.CaptureApplied ? InternalWarTestState.Captured : record.State);
                return;
            }
            if (record.State == InternalWarTestState.RoyalPeacePending)
            {
                return;
            }
            Settlement target = FindSettlement(record.SettlementId);
            Clan attacker = FindClan(record.AttackerClanId);
            Clan defender = FindClan(record.DefenderClanId);
            if (target == null || attacker == null || defender == null || attacker.Kingdom == null || attacker.Kingdom != defender.Kingdom)
            {
                InvalidateConflict("The saved internal-war identities are no longer valid.");
                return;
            }
            if (InternalWarTestService.ResolveClan(FindParty(record.LeaderPartyId)) != attacker
                || attacker.Kingdom.StringId != record.KingdomId)
            {
                InvalidateConflict("The saved player, party, or kingdom contract changed.");
                return;
            }
            if (target.OwnerClan != defender)
            {
                InvalidateConflict("The saved target-owner contract changed without a committed capture.");
                return;
            }
            if (!FactionManager.IsAtWarAgainstFaction(attacker, defender))
            {
                InvalidateConflict("The saved clan-hostility stance no longer exists.");
                return;
            }
        }

        private void EndConflict(InternalWarTestState finalState, string reason)
        {
            InternalWarTestRecord record = CurrentRecord;
            if (Conflict != null) Conflict.RequestPeace();
            if (HasActiveRaid) Raid.StopRequested = true;
            if (record == null) return;
            record.RequestCompletion(finalState);
            InternalWarTestDiagnostics.Info("TEST END: " + reason);
        }


        private void InvalidateConflict(string reason)
        {
            try { EndConflict(InternalWarTestState.Invalidated, reason); }
            catch (Exception exception) { FailOperationalConflict(reason, exception); }
        }

        private void FailOperationalConflict(string message, Exception exception)
        {
            InternalWarTestRecord record = CurrentRecord;
            if (record == null)
            {
                InternalWarTestDiagnostics.Error(message, exception);
                return;
            }
            if (Conflict != null) Conflict.RequestPeace();
            record.RequestCompletion(record.State == InternalWarTestState.RoyalPeacePending
                ? record.PendingFinalState : InternalWarTestState.Failed);
            _nextCleanupAttemptHour = CampaignTime.Now.ToHours + CleanupRetryHours;
            string failure = message + " " + exception.GetType().FullName + ": " + exception.Message;
            bool report = failure != _lastCleanupFailure;
            if (report) InternalWarTestDiagnostics.Error(message + " Cleanup remains pending; retry interval is six campaign hours.", exception);
            _lastCleanupFailure = failure;
            try { _cleanup.RestoreLeaderControl(); }
            catch (Exception cleanupException)
            {
                if (report) InternalWarTestDiagnostics.Error("Player-control rollback also failed; do not save over the pre-test backup.", cleanupException);
            }
        }

        internal static void EndDiplomacy(Clan attacker, Clan defender)
        {
            if (attacker == null || defender == null) return;
            if (FactionManager.IsAtWarAgainstFaction(attacker, defender)) MakePeaceAction.Apply(attacker, defender);
            // Do not call the clan-wide hostile-action helper: it detaches parties from foreign camps
            // and updates battles immediately. ContinuePendingCleanup owns the guarded test cleanup.
        }

        private static string NameOf(Clan clan) { return clan == null ? "missing" : clan.Name.ToString(); }
        private static string NameOf(Settlement settlement) { return settlement == null ? "missing" : settlement.Name.ToString(); }
        private static string KingdomOf(Clan clan)
        {
            return clan == null || clan.Kingdom == null ? "none" : clan.Kingdom.Name + " (" + clan.Kingdom.StringId + ")";
        }

        private string ValidateStart(Clan attacker, Settlement target)
        {
            if (attacker != Clan.PlayerClan) return "This test build only permits the player's clan to start an internal siege.";
            string blocker = InternalWarTestService.GetPlayerStartBlocker();
            return string.IsNullOrEmpty(blocker) ? InternalWarTestService.GetTargetStartBlocker(target) : blocker;
        }

        internal static Clan FindClan(string id) { return Clan.All.FirstOrDefault(c => c != null && c.StringId == id); }
        internal static Settlement FindSettlement(string id) { return Settlement.All.FirstOrDefault(s => s != null && s.StringId == id); }
        internal static MobileParty FindParty(string id) { return MobileParty.All.FirstOrDefault(p => p != null && p.StringId == id); }
    }
}
