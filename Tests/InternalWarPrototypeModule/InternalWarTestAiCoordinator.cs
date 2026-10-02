using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace AgesOfCalradiaInternalWarsTest
{
    // Strategic diagnostic AI: one siege or raid per war, native travel/camp/battle execution.
    // Deliberately no mixed armies, naval targets or kingdom-level target enumeration.
    internal sealed class InternalWarSiegeAiCoordinator
    {
        private readonly InternalWarTestBehavior _owner;
        internal InternalWarSiegeAiCoordinator(InternalWarTestBehavior owner) { _owner = owner; }
        private InternalWarTestRecord CurrentRecord { get { return _owner.CurrentRecord; } }
        private bool HasActiveRaid { get { return _owner.HasActiveRaid; } }
        private double _nextAiSiegeHour;
        private bool _autonomousAi = true;
        internal bool AutonomousAi { get { return _autonomousAi; } }
        internal void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("AOC_InternalConflict_AutonomousAi", ref _autonomousAi);
            dataStore.SyncData("AOC_InternalConflict_NextStrategicHour", ref _nextAiSiegeHour);
            if (dataStore.IsLoading && (double.IsNaN(_nextAiSiegeHour) || double.IsInfinity(_nextAiSiegeHour) || _nextAiSiegeHour < 0))
                _owner.BlockRecovery("Strategic AI schedule is invalid; new operations are disabled.");
        }
        internal void Defer() { _nextAiSiegeHour = CampaignTime.Now.ToHours + 6; }

        internal void ToggleAutonomousAi()
        {
            _autonomousAi = !_autonomousAi;
            _nextAiSiegeHour = CampaignTime.Now.ToHours + 6;
            InternalWarTestDiagnostics.Info("AI STRATEGY: " + (_autonomousAi ? "enabled" : "disabled") + "; active operations finish normally.");
        }

        internal void AdvanceAiSieges()
        {
            if (!_autonomousAi || HasActiveRaid || !string.IsNullOrEmpty(_owner.RecoveryBlocker)
                || (CurrentRecord != null && (!CurrentRecord.CleanupComplete || CurrentRecord.IsOperational))
                || CampaignTime.Now.ToHours < _nextAiSiegeHour) return;
            _nextAiSiegeHour = CampaignTime.Now.ToHours + 6;
            foreach (InternalConflictRecord war in _owner.Conflicts.Where(c => c == _owner.Conflict && c.Phase == InternalConflictPhase.Active)
                .OrderBy(c => c.StartDay).ThenBy(c => c.Id))
            {
            // The current player-led operation always has priority. Only clean intervals admit an AI operation.
            foreach (MobileParty party in MobileParty.All.Where(p => p != MobileParty.MainParty
                && !InternalWarTestService.PartyAssigned(p.StringId)
                && InternalWarCombatService.IsLandLord(p.Party)).OrderByDescending(p => p.Party.NumberOfHealthyMembers))
            {
                Clan clan = InternalWarTestService.ResolveClan(party);
                if (clan == null || (clan.StringId != war.AttackerClanId && clan.StringId != war.DefenderClanId)) continue;
                Settlement target = Settlement.All.Where(s => s.IsFortification && s.OwnerClan != null
                    && !InternalWarTestService.SettlementAssigned(s.StringId)
                    && InternalWarCombatService.Opposed(clan, s.OwnerClan) && s.SiegeEvent == null && s.Party.MapEvent == null)
                    .Where(s => war.CanTargetSettlement(clan.StringId, s.OwnerClan.StringId, s.StringId))
                    .Where(s => CanOvercome(party, s, 60))
                    .OrderBy(s => party.Position.ToVec2().DistanceSquared(s.GatePosition.ToVec2())).ThenBy(s => s.StringId).FirstOrDefault();
                if (target == null) continue;
                _owner.StartAiSiege(war, party, target);
                return;
            }
            // If no free party can safely besiege a fortification, prefer a nearby rival village.
            foreach (MobileParty party in MobileParty.All.Where(p => p != MobileParty.MainParty
                && !InternalWarTestService.PartyAssigned(p.StringId) && InternalWarCombatService.IsLandLord(p.Party))
                .OrderByDescending(p => p.Party.NumberOfHealthyMembers))
            {
                Clan clan = InternalWarTestService.ResolveClan(party);
                if (clan == null || string.IsNullOrEmpty(war.OpponentOf(clan.StringId))) continue;
                Settlement village = Settlement.All.Where(s => s.IsVillage && s.OwnerClan != null
                    && s.OwnerClan.StringId == war.OpponentOf(clan.StringId)
                    && s.Village.VillageState == Village.VillageStates.Normal && s.Party.MapEvent == null
                    && !InternalWarTestService.SettlementAssigned(s.StringId))
                    .Where(s => CanOvercome(party, s, 30))
                    .OrderBy(s => party.Position.ToVec2().DistanceSquared(s.GatePosition.ToVec2())).ThenBy(s => s.StringId).FirstOrDefault();
                if (village == null) continue;
                if (_owner.TryBeginNpcRaid(party, village)) return;
                if (_owner.HasActiveRaid) return; // A failed native order owns recovery until safely released.
            }
            }
        }

        private static bool CanOvercome(MobileParty party, Settlement target, int minimumStrength)
        {
            // Accumulate outside Int32 so a large modded garrison cannot overflow into a weak target.
            double defenders = Math.Max(0, target.Party.NumberOfHealthyMembers);
            foreach (MobileParty defender in target.Parties)
                if (InternalWarTestService.ResolveClan(defender) == target.OwnerClan)
                    defenders += Math.Max(0, defender.Party.NumberOfHealthyMembers);
            return party.Party.NumberOfHealthyMembers >= Math.Max(minimumStrength, defenders * 1.5);
        }
    }

    internal sealed partial class InternalWarTestBehavior
    {
        internal void StartAiSiege(InternalConflictRecord war, MobileParty party, Settlement target)
        {
            Clan attacker = InternalWarTestService.ResolveClan(party);
            string claimResult = "The conflict, attacker, target, or active settlement claim is unavailable.";
            if (war == null || _conflicts.GetById(war.Id) != war || attacker == null || target == null || target.OwnerClan == null
                || Conflict != war || HasActiveRaid || !string.IsNullOrEmpty(RecoveryBlocker)
                || (CurrentRecord != null && (!CurrentRecord.CleanupComplete || CurrentRecord.IsOperational))
                || InternalWarTestService.PartyAssigned(party.StringId) || InternalWarTestService.SettlementAssigned(target.StringId)
                || !InternalWarCombatService.IsLandLord(party.Party) || party == MobileParty.MainParty
                || target.SiegeEvent != null || target.Party.MapEvent != null || !target.IsFortification
                || !InternalWarCombatService.Opposed(attacker, target.OwnerClan)
                || !war.CanTargetSettlement(attacker.StringId, target.OwnerClan.StringId, target.StringId)
                || (attacker.StringId == war.AttackerClanId && !war.TryAssignSettlementClaim(target.StringId, out claimResult)))
            {
                InternalWarTestDiagnostics.Info("AI SIEGE ORDER REJECTED: " + claimResult);
                return;
            }
            if (Conflict != war) throw new InvalidOperationException("The siege belongs to another war controller.");
            Clan defenderClan = target.OwnerClan;
            CurrentRecord = new InternalWarTestRecord { ConflictId = Guid.NewGuid().ToString("N"),
                KingdomId = Conflict.KingdomId, AttackerClanId = attacker.StringId, DefenderClanId = target.OwnerClan.StringId,
                LeaderPartyId = party.StringId, SettlementId = target.StringId, State = InternalWarTestState.Marching,
                StartDay = (int)Math.Floor(CampaignTime.Now.ToDays) };
            Conflict.ActiveOperationId = CurrentRecord.ConflictId;
            InternalWarTestRecord operation = CurrentRecord;
            _cleanup.Reset();
            // Native MobilePartyAi converts this order into camp preparation and an assault; no direct mission call.
            try
            {
                party.SetMoveBesiegeSettlement(target, MobileParty.NavigationType.Default);
                if (CurrentRecord != operation || Conflict != war || _conflicts.GetById(war.Id) != war
                    || war.ActiveOperationId != operation.ConflictId || war.Phase != InternalConflictPhase.Active
                    || !operation.IsOperational || operation.State == InternalWarTestState.RoyalPeacePending
                    || operation.LeaderPartyId != party.StringId || operation.SettlementId != target.StringId
                    || target.OwnerClan != defenderClan || InternalWarTestService.ResolveClan(party) != attacker
                    || !InternalWarCombatService.Opposed(attacker, defenderClan) || !string.IsNullOrEmpty(RecoveryBlocker))
                    throw new InvalidOperationException("NPC siege order authority changed inside the native movement callback.");
                InternalWarTestDiagnostics.Info("AI SIEGE ORDER: operation=" + operation.ConflictId + ", party="
                    + party.StringId + ", target=" + target.StringId + ". Troop-count heuristic; balance unverified.");
            }
            catch (Exception exception)
            {
                // Native movement/mod callback boundary. Never clean up or rewrite a replacement record;
                // preserve uncertain native membership for inspection and block automatic retries/reload resumption.
                BlockRecovery("NPC siege order was interrupted; inspect diagnostics and restore the pre-failure backup.");
                InternalWarTestDiagnostics.Error(RecoveryBlocker, exception);
            }
        }
    }
}
