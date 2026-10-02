using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;

namespace AgesOfCalradiaInternalWarsTest
{
    // Clan identity, never MapFaction identity, is authoritative for internal combat.
    internal static class InternalWarCombatService
    {
        internal static bool Opposed(Clan first, Clan second, bool existingBattle = false)
        {
            InternalConflictRecord conflict = InternalWarTestService.FindConflict(first, second);
            if (InternalWarTestService.RecoveryBlocked || conflict == null || !conflict.IsOpen || (!existingBattle && conflict.Phase != InternalConflictPhase.Active)
                || first == null || second == null || first == second || first.Kingdom == null
                || first.Kingdom != second.Kingdom || first.Kingdom.StringId != conflict.KingdomId) return false;
            return (first.StringId == conflict.AttackerClanId && second.StringId == conflict.DefenderClanId)
                || (second.StringId == conflict.AttackerClanId && first.StringId == conflict.DefenderClanId);
        }

        internal static bool IsLandLord(PartyBase party, bool existingBattle = false)
        {
            MobileParty mobile = party == null ? null : party.MobileParty;
            return mobile != null && mobile.IsActive && (mobile.IsLordParty || mobile == MobileParty.MainParty)
                && !mobile.IsCaravan && !mobile.IsVillager && !mobile.IsCurrentlyAtSea
                && mobile.Army == null && mobile.AttachedTo == null && mobile.AttachedParties.Count == 0
                && mobile.CurrentSettlement == null && mobile.BesiegerCamp == null
                && (existingBattle || party.MapEvent == null);
        }

        internal static bool CanStartFieldBattle(PartyBase first, PartyBase second)
        {
            return IsLandLord(first) && IsLandLord(second)
                && Opposed(InternalWarTestService.ResolveClan(first), InternalWarTestService.ResolveClan(second));
        }

        internal static bool IsConflictFieldBattle(MapEvent battle)
        {
            if (battle == null || !battle.IsFieldBattle || battle.MapEventSettlement != null
                || battle.AttackerSide == null || battle.DefenderSide == null) return false;
            PartyBase attacker = battle.AttackerSide.LeaderParty;
            PartyBase defender = battle.DefenderSide.LeaderParty;
            return IsLandLord(attacker, true) && IsLandLord(defender, true)
                && attacker.MapEvent == battle && defender.MapEvent == battle
                && Opposed(InternalWarTestService.ResolveClan(attacker), InternalWarTestService.ResolveClan(defender), true);
        }

        internal static bool CanJoinFieldBattle(MapEvent battle, PartyBase party, BattleSideEnum side)
        {
            InternalConflictRecord conflict = battle == null || battle.AttackerSide == null || battle.DefenderSide == null ? null
                : InternalWarTestService.FindConflict(InternalWarTestService.ResolveClan(battle.AttackerSide.LeaderParty),
                    InternalWarTestService.ResolveClan(battle.DefenderSide.LeaderParty));
            if (conflict == null || conflict.Phase != InternalConflictPhase.Active || !IsConflictFieldBattle(battle)
                || !IsLandLord(party, true) || (party.MapEvent != null && party.MapEvent != battle)) return false;
            Clan clan = InternalWarTestService.ResolveClan(party);
            PartyBase leader = side == BattleSideEnum.Attacker ? battle.AttackerSide.LeaderParty
                : side == BattleSideEnum.Defender ? battle.DefenderSide.LeaderParty : null;
            return clan != null && clan == InternalWarTestService.ResolveClan(leader);
        }

        internal static bool IsBattleOfConflict(MapEvent battle, InternalConflictRecord conflict)
        {
            if (battle == null || conflict == null || battle.AttackerSide == null || battle.DefenderSide == null) return false;
            Clan first = InternalWarTestService.ResolveClan(battle.AttackerSide.LeaderParty);
            Clan second = InternalWarTestService.ResolveClan(battle.DefenderSide.LeaderParty);
            return first != null && second != null
                && ((first.StringId == conflict.AttackerClanId && second.StringId == conflict.DefenderClanId)
                    || (second.StringId == conflict.AttackerClanId && first.StringId == conflict.DefenderClanId));
        }
    }
}
