using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Core;

namespace AgesOfCalradiaInternalWarsTest
{
    // Native player raid adapter. Village damage, militia, loot and mission resolution stay native.
    internal sealed partial class InternalWarRaidCoordinator
    {
        private readonly InternalWarTestBehavior _owner;
        internal InternalWarRaidCoordinator(InternalWarTestBehavior owner) { _owner = owner; }
        private InternalConflictRecord Conflict { get { return _owner.Conflict; } }
        private InternalWarTestRecord CurrentRecord { get { return _owner.CurrentRecord; } }
        private string RecoveryBlocker { get { return _owner.RecoveryBlocker; } }
        private void BlockRecovery(string message) { _owner.BlockRecovery(message); }
        private static Settlement FindSettlement(string id) { return InternalWarTestBehavior.FindSettlement(id); }
        private static MobileParty FindParty(string id) { return InternalWarTestBehavior.FindParty(id); }
        private string _raidPayload = string.Empty;
        private PlayerEncounter _entryEncounter;
        private bool _originalForceRaid;
        private bool _advancing;
        internal InternalWarRaidRecord Raid { get; private set; }
        internal bool HasActiveRaid { get { return Raid != null && !Raid.Closed; } }

        internal void SyncRaid(IDataStore dataStore)
        {
            if (dataStore.IsSaving && string.IsNullOrEmpty(RecoveryBlocker)) _raidPayload = Raid == null ? string.Empty : Raid.Serialize();
            dataStore.SyncData("AOC_InternalConflict_Raid_v1", ref _raidPayload);
            if (!dataStore.IsLoading) return;
            Raid = InternalWarRaidRecord.Deserialize(_raidPayload);
            if ((!string.IsNullOrEmpty(_raidPayload) && Raid == null)
                || (HasActiveRaid && (Conflict == null || Raid.ConflictId != Conflict.Id
                    || string.IsNullOrEmpty(Conflict.OpponentOf(Raid.DefenderClanId)))))
                BlockRecovery("The saved raid identity is invalid; payload retained and new actions disabled.");
        }

        internal string GetRaidBlocker(Settlement target)
        {
            if (!string.IsNullOrEmpty(RecoveryBlocker)) return RecoveryBlocker;
            if (Conflict == null || Conflict.Phase != InternalConflictPhase.Active) return "Declare an internal conflict first.";
            if (HasActiveRaid) return "A raid is already active or awaiting recovery.";
            if (CurrentRecord != null && (!CurrentRecord.CleanupComplete || CurrentRecord.IsOperational)) return "Finish the current siege operation first.";
            MobileParty party = MobileParty.MainParty;
            if (party != null && InternalWarTestService.PartyAssigned(party.StringId)) return "Your party already has an operation in another war.";
            if (target != null && InternalWarTestService.SettlementAssigned(target.StringId)) return "Another war already reserved this village.";
            if (party == null || party.Army != null || party.BesiegerCamp != null || party.Party.MapEvent != null
                || party.IsCurrentlyAtSea || !party.IsActive || party.Party.NumberOfHealthyMembers <= 0) return "Your free land party must be ready.";
            if (target == null || !target.IsVillage || target.OwnerClan == null
                || Clan.PlayerClan == null || target.OwnerClan.StringId != Conflict.OpponentOf(Clan.PlayerClan.StringId)
                || !InternalWarCombatService.Opposed(Clan.PlayerClan, target.OwnerClan)) return "This village must belong to the opposing clan.";
            if (party.CurrentSettlement != target || Settlement.CurrentSettlement != target
                || PlayerEncounter.Current == null || PlayerEncounter.EncounterSettlement != target || PlayerEncounter.Battle != null)
                return "Enter this village normally before starting the raid.";
            if (target.Party.MapEvent != null || target.Village.VillageState != Village.VillageStates.Normal)
                return "This village is already fighting, raided or recovering.";
            return string.Empty;
        }

        internal bool TryBeginRaid(Settlement target, out string result)
        {
            result = GetRaidBlocker(target);
            if (!string.IsNullOrEmpty(result)) return false;
            Raid = new InternalWarRaidRecord { Id = Guid.NewGuid().ToString("N"), ConflictId = Conflict.Id,
                SettlementId = target.StringId, LeaderPartyId = MobileParty.MainParty.StringId,
                DefenderClanId = target.OwnerClan.StringId, StartDay = (int)Math.Floor(CampaignTime.Now.ToDays) };
            InternalWarRaidRecord expected = Raid;
            _entryEncounter = PlayerEncounter.Current;
            _originalForceRaid = _entryEncounter.ForceRaid;
            try
            {
                // The friendly village encounter predates raid intent; refresh its exact guarded side assignment.
                PlayerEncounter.Current.SetupFields(MobileParty.MainParty.Party, target.Party);
                EnsureEntry(expected, target);
                BeHostileAction.ApplyEncounterHostileAction(MobileParty.MainParty.Party, target.Party);
                EnsureEntry(expected, target);
                PlayerEncounter.Current.ForceRaid = true;
                GameMenu.SwitchToMenu("encounter");
                if (Raid == expected && IsRaidBattle(MobileParty.MainParty.Party.MapEvent)) Raid.EventObserved = true;
                result = "Internal raid started. Use native militia battle/raid controls; village damage and loot remain native.";
                InternalWarTestDiagnostics.Info("RAID START: id=" + Raid.Id + ", village=" + target.StringId);
                return true;
            }
            catch (Exception exception)
            {
                if (Raid == expected)
                {
                    Raid.StopRequested = true;
                    try { TryCancelUnstartedRaid(target); }
                    catch (Exception cleanupException)
                    {
                        InternalWarTestDiagnostics.Error("Unstarted raid rollback failed; no unrelated encounter was dismissed.", cleanupException);
                        BlockRecovery("Raid entry rollback failed. Reload the pre-test save and retain diagnostics.");
                    }
                }
                InternalWarTestDiagnostics.Error("Native raid entry failed; recovery remains pending.", exception);
                result = "Raid entry failed. Use diagnostic snapshot and emergency peace; do not overwrite the pre-test save.";
                return false;
            }
        }

        internal bool IsRaidHostility(PartyBase attacker, PartyBase defender)
        {
            return MobileParty.MainParty != null && attacker == MobileParty.MainParty.Party
                && InternalWarTestService.ResolveClan(attacker) == Clan.PlayerClan && IsRaidAuthority(attacker, defender);
        }

        internal bool IsRaidAuthority(PartyBase attacker, PartyBase defender)
        {
            if (!string.IsNullOrEmpty(RecoveryBlocker) || !HasActiveRaid || Raid.StopRequested
                || Conflict == null || Conflict.Phase != InternalConflictPhase.Active || Raid.ConflictId != Conflict.Id
                || attacker == null || attacker.MobileParty == null || defender == null || defender.Settlement == null
                || !defender.Settlement.IsVillage
                || attacker.MobileParty.StringId != Raid.LeaderPartyId || defender.Settlement.StringId != Raid.SettlementId)
                return false;
            Clan clan = InternalWarTestService.ResolveClan(attacker);
            return clan != null && Conflict.OpponentOf(clan.StringId) == Raid.DefenderClanId
                && clan.Kingdom != null && clan.Kingdom.StringId == Conflict.KingdomId
                && defender.Settlement.OwnerClan != null && defender.Settlement.OwnerClan.StringId == Raid.DefenderClanId
                && defender.Settlement.OwnerClan.Kingdom == clan.Kingdom;
        }

        internal bool IsRaidBattle(MapEvent battle)
        {
            // Mechanical ownership survives political invalidation so only this in-flight event can stop safely.
            MobileParty party = HasActiveRaid ? FindParty(Raid.LeaderPartyId) : null;
            return HasActiveRaid && party != null && party.StringId == Raid.LeaderPartyId
                && battle != null && battle.IsRaid && battle.AttackerSide != null
                && battle.AttackerSide.LeaderParty == party.Party && party.Party.MapEvent == battle
                && battle.MapEventSettlement != null && battle.MapEventSettlement.IsVillage
                && battle.MapEventSettlement.StringId == Raid.SettlementId;
        }

        internal void AdvanceRaid()
        {
            if (_advancing) return;
            _advancing = true;
            try { AdvanceRaidCore(); }
            finally { _advancing = false; }
        }

        private void AdvanceRaidCore()
        {
            if (!HasActiveRaid) return;
            Settlement target = FindSettlement(Raid.SettlementId);
            MobileParty party = FindParty(Raid.LeaderPartyId);
            MapEvent battle = party == null ? null : party.Party.MapEvent;
            if (IsRaidBattle(battle))
            {
                Raid.EventObserved = true;
                if (!string.IsNullOrEmpty(RecoveryBlocker) || Conflict == null || Conflict.Phase != InternalConflictPhase.Active
                    || !IsRaidAuthority(party.Party, battle.MapEventSettlement.Party)) Raid.StopRequested = true;
                if (Raid.StopRequested && Mission.Current == null && Game.Current != null
                    && Game.Current.GameStateManager.ActiveState is MapState) battle.DiplomaticallyFinished = true;
                return;
            }
            if (battle != null || Mission.Current != null || PlayerEncounter.Battle != null) return;
            if (party != MobileParty.MainParty)
            {
                AdvanceNpcRaid(party, target);
                return;
            }
            if (Raid.StopRequested && !Raid.EventObserved && TryCancelUnstartedRaid(target)) return;
            if (PlayerEncounter.Current != null && PlayerEncounter.EncounterSettlement == target) return;
            if (Raid.EventObserved || Raid.StopRequested || CampaignTime.Now.ToDays - Raid.StartDay > 2)
            {
                Raid.Closed = true;
                InternalWarTestDiagnostics.Info("RAID ENDED: id=" + Raid.Id + "; native control returned. Outcome remains native.");
            }
        }

        private void EnsureEntry(InternalWarRaidRecord expected, Settlement target)
        {
            if (Raid != expected || _entryEncounter == null || PlayerEncounter.Current != _entryEncounter
                || PlayerEncounter.EncounterSettlement != target || PlayerEncounter.Battle != null
                || MobileParty.MainParty == null || MobileParty.MainParty.Party.MapEvent != null || target.Party.MapEvent != null
                || MobileParty.MainParty.CurrentSettlement != target || !IsRaidHostility(MobileParty.MainParty.Party, target.Party)
                || _entryEncounter.PlayerSide != BattleSideEnum.Attacker)
                throw new InvalidOperationException("The raid, authority or native encounter changed during entry.");
        }

        private bool TryCancelUnstartedRaid(Settlement target)
        {
            if (!HasActiveRaid || Raid.EventObserved || Mission.Current != null || PlayerEncounter.Battle != null
                || MobileParty.MainParty == null || MobileParty.MainParty.Party.MapEvent != null
                || (target != null && target.Party.MapEvent != null)) return false;
            if (PlayerEncounter.Current != null)
            {
                if (PlayerEncounter.Current != _entryEncounter || PlayerEncounter.EncounterSettlement != target) return false;
                // Only our captured, unstarted entry is restored; never finish or replace this menu.
                _entryEncounter.ForceRaid = _originalForceRaid;
                Raid.Closed = true;
                _entryEncounter.SetupFields(MobileParty.MainParty.Party, target.Party);
            }
            else Raid.Closed = true;
            InternalWarTestDiagnostics.Info("RAID ENTRY CANCELLED: " + Raid.Id + "; no native battle was started.");
            return true;
        }
    }
}
