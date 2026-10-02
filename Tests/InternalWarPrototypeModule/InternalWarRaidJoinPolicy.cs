using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;

namespace AgesOfCalradiaInternalWarsTest
{
    // Eligibility only; the caller owns the registered raid and native side assignment.
    // No native callbacks or mutations. A village's own party and its owner main party
    // already inside the village may defend; all other reinforcements must be free land lords.
    internal static class InternalWarRaidJoinPolicy
    {
        internal static bool CanJoin(InternalConflictRecord war, MapEvent battle, PartyBase party, BattleSideEnum side)
        {
            if (InternalWarTestService.RecoveryBlocked || war == null || war.Phase != InternalConflictPhase.Active
                || battle == null || !battle.IsRaid || battle.MapEventSettlement == null || !battle.MapEventSettlement.IsVillage
                || battle.AttackerSide == null || battle.DefenderSide == null || party == null
                || (side != BattleSideEnum.Attacker && side != BattleSideEnum.Defender)
                || (party.MapEvent != null && party.MapEvent != battle)) return false;
            PartyBase attacker = battle.AttackerSide.LeaderParty;
            PartyBase defender = battle.DefenderSide.LeaderParty;
            Clan attackerClan = InternalWarTestService.ResolveClan(attacker);
            Clan defenderClan = battle.MapEventSettlement.OwnerClan;
            if (attacker == null || attacker.MobileParty == null || attacker.MapEvent != battle
                || defender == null || defender.MapEvent != battle
                || InternalWarTestService.ResolveClan(defender) != defenderClan
                || attackerClan == null || defenderClan == null || attackerClan == defenderClan
                || attackerClan.Kingdom == null || attackerClan.Kingdom != defenderClan.Kingdom
                || attackerClan.Kingdom.StringId != war.KingdomId
                || war.OpponentOf(attackerClan.StringId) != defenderClan.StringId) return false;
            Clan clan = InternalWarTestService.ResolveClan(party);
            if (clan == null || clan != (side == BattleSideEnum.Attacker ? attackerClan : defenderClan)
                || clan.IsClanTypeMercenary || clan.IsUnderMercenaryService
                || (clan.IsMinorFaction && clan != Clan.PlayerClan)) return false;
            if (party == battle.MapEventSettlement.Party) return side == BattleSideEnum.Defender;
            MobileParty mobile = party.MobileParty;
            if (mobile == null || !mobile.IsActive || (!mobile.IsLordParty && mobile != MobileParty.MainParty)
                || mobile.IsCaravan || mobile.IsVillager || mobile.IsCurrentlyAtSea || mobile.Army != null
                || mobile.AttachedTo != null || mobile.AttachedParties.Count != 0 || mobile.BesiegerCamp != null) return false;
            return mobile.CurrentSettlement == null || (mobile == MobileParty.MainParty && side == BattleSideEnum.Defender
                && clan == defenderClan && mobile.CurrentSettlement == battle.MapEventSettlement);
        }
    }
}
