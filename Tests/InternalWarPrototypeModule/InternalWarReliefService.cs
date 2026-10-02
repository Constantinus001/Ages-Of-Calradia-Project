using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace AgesOfCalradiaInternalWarsTest
{
    // Land relief identity only. Native SiegeOutside initialization must normalize the besieger
    // to argument1 because its camp roster and aftermath assume that orientation. Existing events
    // are resolved from their actual leaders; this service never authorizes settlement capture.
    internal static class InternalWarReliefService
    {
        internal static bool CanStart(PartyBase first, PartyBase second)
        {
            return CanStartOrdered(first, second) || CanStartOrdered(second, first);
        }

        private static bool CanStartOrdered(PartyBase besieger, PartyBase relief)
        {
            MobileParty siegeParty = besieger == null ? null : besieger.MobileParty;
            if (siegeParty == null || relief == null || siegeParty.BesiegerCamp == null
                || siegeParty.BesiegerCamp.SiegeEvent == null || !FreeBesieger(siegeParty)
                || besieger.MapEvent != null || !InternalWarCombatService.IsLandLord(relief)
                || besieger.NumberOfHealthyMembers <= 0 || relief.NumberOfHealthyMembers <= 0) return false;
            Settlement target = siegeParty.BesiegerCamp.SiegeEvent.BesiegedSettlement;
            InternalWarTestRecord record = InternalWarTestService.Find(target);
            Clan attacker = InternalWarTestService.ResolveClan(besieger), defender = InternalWarTestService.ResolveClan(relief);
            if (!UsableRecord(record) || record.State == InternalWarTestState.RoyalPeacePending
                || target == null || !target.IsFortification || target.Party.MapEvent != null
                || target.SiegeEvent != siegeParty.BesiegerCamp.SiegeEvent || target.OwnerClan != defender
                || siegeParty.BesiegerCamp.LeaderParty != siegeParty || record.LeaderPartyId != siegeParty.StringId
                || !RegularClan(attacker) || !RegularClan(defender) || attacker.StringId != record.AttackerClanId
                || defender.StringId != record.DefenderClanId || !InternalWarCombatService.Opposed(attacker, defender)) return false;
            InternalConflictRecord war = InternalWarTestService.FindConflict(attacker, defender);
            return war != null && war.Phase == InternalConflictPhase.Active && war.ActiveOperationId == record.ConflictId;
        }

        internal static InternalWarTestRecord Find(MapEvent battle)
        {
            if (battle == null || !battle.IsSiegeOutside || battle.AttackerSide == null || battle.DefenderSide == null) return null;
            Settlement target = battle.MapEventSettlement;
            InternalWarTestRecord record = InternalWarTestService.Find(target);
            if (!UsableRecord(record)) return null;
            PartyBase first = battle.AttackerSide.LeaderParty, second = battle.DefenderSide.LeaderParty;
            if (first == null || second == null || first.MapEvent != battle || second.MapEvent != battle) return null;
            return Matches(record, target, first, second) || Matches(record, target, second, first) ? record : null;
        }

        private static bool Matches(InternalWarTestRecord record, Settlement target, PartyBase besieger, PartyBase relief)
        {
            MobileParty party = besieger.MobileParty;
            Clan attacker = InternalWarTestService.ResolveClan(besieger), defender = InternalWarTestService.ResolveClan(relief);
            return party != null && party.StringId == record.LeaderPartyId && relief.MobileParty != null
                && attacker != null && defender != null && attacker.StringId == record.AttackerClanId
                && defender.StringId == record.DefenderClanId && target.OwnerClan == defender
                && target.SiegeEvent != null && target.SiegeEvent.BesiegerCamp != null
                && target.SiegeEvent.BesiegerCamp.LeaderParty == party && party.BesiegerCamp == target.SiegeEvent.BesiegerCamp;
        }

        internal static BattleSideEnum BattleSideFor(InternalWarTestRecord record, MapEvent battle, Clan clan)
        {
            if (record == null || clan == null || Find(battle) != record) return BattleSideEnum.None;
            if (InternalWarTestService.ResolveClan(battle.AttackerSide.LeaderParty) == clan) return BattleSideEnum.Attacker;
            return InternalWarTestService.ResolveClan(battle.DefenderSide.LeaderParty) == clan ? BattleSideEnum.Defender : BattleSideEnum.None;
        }

        private static bool UsableRecord(InternalWarTestRecord record)
        {
            return record != null && !record.CleanupComplete && !record.CaptureApplied
                && (record.State == InternalWarTestState.Preparing || record.State == InternalWarTestState.Assaulting
                    || record.State == InternalWarTestState.RoyalPeacePending);
        }
        private static bool FreeBesieger(MobileParty party)
        {
            return party.IsActive && (party.IsLordParty || party == MobileParty.MainParty) && !party.IsCurrentlyAtSea
                && !party.IsCaravan && !party.IsVillager && party.CurrentSettlement == null
                && party.Army == null && party.AttachedTo == null && party.AttachedParties.Count == 0;
        }
        private static bool RegularClan(Clan clan)
        {
            return clan != null && !clan.IsClanTypeMercenary && !clan.IsUnderMercenaryService
                && (!clan.IsMinorFaction || clan == Clan.PlayerClan);
        }
    }
}
