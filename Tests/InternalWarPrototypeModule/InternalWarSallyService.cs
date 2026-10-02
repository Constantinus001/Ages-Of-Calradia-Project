using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.MapEvents;

namespace AgesOfCalradiaInternalWarsTest
{
    // Land-only siege battle adapter. Political defenders ATTACK in a sally; no title transfer.
    internal static class InternalWarSallyService
    {
        internal static InternalWarTestRecord Find(MapEvent battle)
        {
            if (battle == null || !battle.IsSallyOut || battle.AttackerSide == null || battle.DefenderSide == null) return null;
            Settlement target = battle.MapEventSettlement;
            InternalWarTestRecord record = InternalWarTestService.Find(target);
            PartyBase owner = battle.AttackerSide.LeaderParty, besieger = battle.DefenderSide.LeaderParty;
            if (record == null || (record.State != InternalWarTestState.Preparing && record.State != InternalWarTestState.Assaulting
                    && record.State != InternalWarTestState.RoyalPeacePending) || record.CleanupComplete
                || owner == null || besieger == null || owner.MapEvent != battle || besieger.MapEvent != battle
                || besieger.MobileParty == null || besieger.MobileParty.StringId != record.LeaderPartyId
                || InternalWarTestService.ResolveClan(owner) == null || InternalWarTestService.ResolveClan(besieger) == null
                || InternalWarTestService.ResolveClan(owner).StringId != record.DefenderClanId
                || InternalWarTestService.ResolveClan(besieger).StringId != record.AttackerClanId) return null;
            return record;
        }

        internal static bool CanStart(PartyBase owner, PartyBase besieger)
        {
            MobileParty first = owner == null ? null : owner.MobileParty;
            MobileParty second = besieger == null ? null : besieger.MobileParty;
            Settlement target = first == null ? null : first.CurrentSettlement;
            if (!FreeLand(first) || !FreeLand(second) || owner.MapEvent != null || besieger.MapEvent != null
                || owner.NumberOfHealthyMembers <= 0 || besieger.NumberOfHealthyMembers <= 0
                || target == null || !target.IsFortification || target.Town == null || target.Party.MapEvent != null
                || first.BesiegerCamp != null || second.CurrentSettlement != null
                || (first != MobileParty.MainParty && first != target.Town.GarrisonParty && !first.IsLordParty)) return false;
            InternalWarTestRecord record = InternalWarTestService.Find(target);
            Clan ownerClan = InternalWarTestService.ResolveClan(owner), siegeClan = InternalWarTestService.ResolveClan(besieger);
            if (record == null || (record.State != InternalWarTestState.Preparing && record.State != InternalWarTestState.Assaulting)
                || record.CleanupComplete || record.CaptureApplied || target.OwnerClan != ownerClan
                || !RegularClan(ownerClan) || !RegularClan(siegeClan) || record.DefenderClanId != ownerClan.StringId
                || record.AttackerClanId != siegeClan.StringId || record.LeaderPartyId != second.StringId
                || !InternalWarCombatService.Opposed(ownerClan, siegeClan)) return false;
            InternalConflictRecord war = InternalWarTestService.FindConflict(ownerClan, siegeClan);
            return war != null && war.Phase == InternalConflictPhase.Active && war.ActiveOperationId == record.ConflictId
                && target.SiegeEvent != null && target.SiegeEvent.BesiegerCamp != null
                && target.SiegeEvent.BesiegerCamp.LeaderParty == second
                && second.BesiegerCamp == target.SiegeEvent.BesiegerCamp;
        }

        private static bool FreeLand(MobileParty party)
        {
            return party != null && party.IsActive && !party.IsCurrentlyAtSea && !party.IsCaravan && !party.IsVillager
                && party.Army == null && party.AttachedTo == null && party.AttachedParties.Count == 0;
        }

        private static bool RegularClan(Clan clan)
        {
            return clan != null && !clan.IsClanTypeMercenary && !clan.IsUnderMercenaryService
                && (!clan.IsMinorFaction || clan == Clan.PlayerClan);
        }

        internal static bool TryAutomatic(Settlement target)
        {
            if (target == null || target.Town == null || target.SiegeEvent == null || target.SiegeEvent.BesiegerCamp == null
                || (MobileParty.MainParty != null && MobileParty.MainParty.CurrentSettlement == target)) return false;
            MobileParty garrison = target.Town.GarrisonParty, besieger = target.SiegeEvent.BesiegerCamp.LeaderParty;
            if (garrison == null || besieger == null || !CanStart(garrison.Party, besieger.Party)) return false;
            long owners = garrison.Party.NumberOfHealthyMembers;
            foreach (MobileParty party in target.Parties)
                if (party != garrison && party != MobileParty.MainParty && party.IsLordParty && FreeLand(party)
                    && party.Party.MapEvent == null && InternalWarTestService.ResolveClan(party) == target.OwnerClan)
                    owners += party.Party.NumberOfHealthyMembers;
            long attackers = 0;
            foreach (MobileParty party in MobileParty.All)
                if (party.BesiegerCamp == besieger.BesiegerCamp && FreeLand(party)
                    && InternalWarTestService.ResolveClan(party) == InternalWarTestService.ResolveClan(besieger))
                    attackers += party.Party.NumberOfHealthyMembers;
            if (owners <= 0 || owners <= Math.Max(1L, attackers) * 2) return false;
            try
            {
                EncounterManager.StartPartyEncounter(garrison.Party, besieger.Party);
                return Find(besieger.Party.MapEvent) != null;
            }
            catch (Exception exception)
            {
                InternalWarTestService.BlockRecovery("Automatic private sally entry failed; native encounter state needs inspection.");
                InternalWarTestDiagnostics.Error("Private sally entry failed.", exception);
                return false;
            }
        }
    }
}
