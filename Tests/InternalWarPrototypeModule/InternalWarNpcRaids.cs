using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace AgesOfCalradiaInternalWarsTest
{
    // Native 1.4.8 strategic raid orders. No direct battle or player encounter creation.
    // StartSettlementEncounter's audited query adapter admits only this exact reserved order.
    internal sealed partial class InternalWarRaidCoordinator
    {
        internal bool TryBeginNpcRaid(MobileParty party, Settlement target)
        {
            Clan clan = party == null ? null : InternalWarTestService.ResolveClan(party.Party);
            if (!string.IsNullOrEmpty(RecoveryBlocker) || Conflict == null || Conflict.Phase != InternalConflictPhase.Active
                || HasActiveRaid || (CurrentRecord != null && (!CurrentRecord.CleanupComplete || CurrentRecord.IsOperational))
                || party == null || party == MobileParty.MainParty || !InternalWarCombatService.IsLandLord(party.Party)
                || party.Party.NumberOfHealthyMembers <= 0 || InternalWarTestService.PartyAssigned(party.StringId)
                || target == null || !target.IsVillage || target.OwnerClan == null
                || target.Village.VillageState != Village.VillageStates.Normal || target.Party.MapEvent != null
                || InternalWarTestService.SettlementAssigned(target.StringId) || clan == null
                || Conflict.OpponentOf(clan.StringId) != target.OwnerClan.StringId
                || !InternalWarCombatService.Opposed(clan, target.OwnerClan)) return false;
            Raid = new InternalWarRaidRecord { Id = Guid.NewGuid().ToString("N"), ConflictId = Conflict.Id,
                SettlementId = target.StringId, LeaderPartyId = party.StringId, DefenderClanId = target.OwnerClan.StringId,
                StartDay = (int)Math.Floor(CampaignTime.Now.ToDays) };
            try
            {
                party.SetMoveRaidSettlement(target, MobileParty.NavigationType.Default, false);
                if (!IsRaidAuthority(party.Party, target.Party))
                    throw new InvalidOperationException("Raid authority changed while issuing the order.");
                InternalWarTestDiagnostics.Info("AI RAID ORDER: " + Raid.Id + ", party=" + party.StringId + ", village=" + target.StringId);
                return true;
            }
            catch (Exception exception)
            {
                Raid.StopRequested = true;
                InternalWarTestDiagnostics.Error("NPC raid order failed; exact owned order queued for cleanup.", exception);
                return false;
            }
        }

        internal bool CanStartNpcRaid(MobileParty party, Settlement target)
        {
            return party != null && party != MobileParty.MainParty && InternalWarCombatService.IsLandLord(party.Party)
                && party.Party.NumberOfHealthyMembers > 0 && target != null && target.Party.MapEvent == null
                && target.IsVillage && target.Village.VillageState == Village.VillageStates.Normal
                && party.ShortTermBehavior == AiBehavior.RaidSettlement && party.ShortTermTargetSettlement == target
                && IsRaidAuthority(party.Party, target.Party);
        }

        private void AdvanceNpcRaid(MobileParty party, Settlement target)
        {
            if (party == null || target == null || !IsRaidAuthority(party.Party, target.Party)
                || CampaignTime.Now.ToDays - Raid.StartDay > 14) Raid.StopRequested = true;
            if (!Raid.StopRequested && !Raid.EventObserved) return;
            // Never replace a foreign battle, army, camp or another system's new order.
            if (party != null && party.Party.MapEvent == null && party.Army == null && party.BesiegerCamp == null
                && party.DefaultBehavior == AiBehavior.RaidSettlement && party.TargetSettlement == target)
                party.SetMoveModeHold();
            Raid.Closed = true;
            InternalWarTestDiagnostics.Info("AI RAID ENDED: " + Raid.Id + "; owned order released.");
        }
    }
}
