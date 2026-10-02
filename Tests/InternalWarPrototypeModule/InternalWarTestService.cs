using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;

namespace AgesOfCalradiaInternalWarsTest
{
    internal static class InternalWarTestService
    {
        private static InternalWarTestBehavior _rootBehavior;
        private static InternalWarTestBehavior _behavior { get { return _rootBehavior == null ? null : _rootBehavior.SelectedController; } }

        internal static void Attach(InternalWarTestBehavior behavior) { _rootBehavior = behavior; }
        internal static void Detach() { _rootBehavior = null; }
        internal static InternalWarTestRecord Current { get { return _behavior == null ? null : _behavior.CurrentRecord; } }
        internal static bool HasOperationalConflict { get { return Current != null && Current.IsOperational; } }
        internal static InternalConflictRecord Conflict { get { return _behavior == null ? null : _behavior.Conflict; } }
        internal static InternalConflictRecord FindConflict(Clan first, Clan second)
        { return _behavior == null || first == null || second == null ? null : _behavior.FindConflict(first.StringId, second.StringId); }
        internal static bool HasOpenConflict { get { return HasOperationalConflict || (Conflict != null && Conflict.IsOpen); } }
        internal static void DrainCleanup(float dt) { if (_rootBehavior != null) _rootBehavior.DrainCleanup(dt); }
        internal static bool HasActiveRaid { get { return _behavior != null && _behavior.HasActiveRaid; } }
        internal static bool RecoveryBlocked { get { return _behavior != null && !string.IsNullOrEmpty(_behavior.RecoveryBlocker); } }
        internal static void BlockRecovery(string reason) { if (_rootBehavior != null) _rootBehavior.BlockRecovery(reason); }
        internal static bool IsRaidBattle(MapEvent battle) { return _rootBehavior != null && _rootBehavior.Controllers.Any(c => c.IsRaidBattle(battle)); }
        internal static InternalConflictRecord FindRaidConflict(MapEvent battle)
        { var owner = _rootBehavior == null ? null : _rootBehavior.Controllers.FirstOrDefault(c => c.IsRaidBattle(battle)); return owner == null ? null : owner.Conflict; }
        internal static bool OwnsOperation(InternalWarTestRecord record)
        { return record != null && _rootBehavior != null && _rootBehavior.Controllers.Any(c => c.CurrentRecord == record); }
        internal static bool PartyAssigned(string id) { return _rootBehavior != null && _rootBehavior.Controllers.Any(c => c.OwnsPartyOrder(id)); }
        internal static bool SettlementAssigned(string id) { return _rootBehavior != null && _rootBehavior.Controllers.Any(c => c.OwnsSettlementOrder(id)); }
        internal static bool IsRaidHostility(PartyBase attacker, PartyBase defender)
        { return _rootBehavior != null && _rootBehavior.Controllers.Any(c => c.IsRaidHostility(attacker, defender)); }
        internal static bool CanStartNpcRaid(MobileParty party, Settlement target)
        { return _rootBehavior != null && _rootBehavior.Controllers.Any(c => c.CanStartNpcRaid(party, target)); }
        internal static bool IsPlayerRaidEncounter(PartyBase attacker, PartyBase defender)
        {
            return MobileParty.MainParty != null && attacker == MobileParty.MainParty.Party
                && MapEvent.PlayerMapEvent == null && attacker.MapEvent == null
                && defender != null && defender.Settlement != null
                && MobileParty.MainParty.CurrentSettlement == defender.Settlement && IsRaidHostility(attacker, defender);
        }

        internal static string GetPlayerStartBlocker()
        { return GetPlayerStartBlocker(false); }

        internal static string GetDeclarationBlocker()
        { return GetPlayerStartBlocker(true); }

        private static string GetPlayerStartBlocker(bool declaring)
        {
            if (!declaring && _rootBehavior != null && MobileParty.MainParty != null
                && _rootBehavior.Controllers.Any(c => c != _behavior && c.OwnsPartyOrder(MobileParty.MainParty.StringId)))
                return "Your party already has an operation in another war. Select that war to finish or cancel it.";
            if (_behavior != null && !string.IsNullOrEmpty(_behavior.RecoveryBlocker)) return _behavior.RecoveryBlocker;
            if (HasActiveRaid) return "Finish the active raid and its recovery first.";
            if (!declaring && HasOperationalConflict) return "This war already has an active operation.";
            if (!declaring && Current != null && !Current.CleanupComplete) return "The previous operation has not passed cleanup verification.";
            if (!declaring && Conflict != null && Conflict.Phase == InternalConflictPhase.PeacePending) return "Political peace is still pending.";
            if (PlayerSiege.PlayerSiegeEvent != null) return "The native player siege has not finished.";
            if (!declaring && Conflict != null && Conflict.Phase == InternalConflictPhase.Active
                && (Clan.PlayerClan == null || (Clan.PlayerClan.StringId != Conflict.AttackerClanId && Clan.PlayerClan.StringId != Conflict.DefenderClanId)
                    || Clan.PlayerClan.Kingdom == null || Clan.PlayerClan.Kingdom.StringId != Conflict.KingdomId))
                return "The ongoing conflict no longer matches your clan and kingdom.";
            return GetPlayerPartyBlocker();
        }

        private static string GetPlayerPartyBlocker()
        {
            Clan playerClan = Clan.PlayerClan;
            if (playerClan == null) return "The player clan is unavailable.";
            if (playerClan.Leader == null) return "Your clan needs a living leader before starting the test.";
            if (playerClan.Kingdom == null) return "Your clan must belong to a kingdom as a vassal or ruler.";
            if (playerClan.IsClanTypeMercenary) return "Mercenary-type player clans are excluded from this test.";
            if (playerClan.IsUnderMercenaryService) return "Your clan is under a mercenary contract. Join the kingdom as a vassal or ruler first.";
            // The native player_faction can retain IsMinorFaction after becoming a sworn vassal.
            // Only the actual player clan receives this exception; target clans remain restricted.
            MobileParty party = MobileParty.MainParty;
            if (party == null) return "Your main party is unavailable.";
            if (!party.IsActive) return "Your main party is inactive.";
            if (party.IsCurrentlyAtSea) return "Your party must be on land for this test.";
            if (party.Army != null) return "Your party belongs to an army. Leave or disband it, even if it contains only your party.";
            if (party.AttachedTo != null || party.AttachedParties.Count != 0) return "Detach linked parties before starting a private-war operation.";
            if (party.Party.MapEvent != null) return "Your party is still participating in a battle or other map event.";
            if (party.BesiegerCamp != null) return "Your party is already attached to a siege camp.";
            return string.Empty;
        }

        internal static string GetBeginSiegeBlocker(Settlement target)
        {
            InternalWarTestRecord record = Current;
            if (record == null || record.State != InternalWarTestState.Marching)
                return "No internal-war test is awaiting its siege start.";
            if (target == null || target.StringId != record.SettlementId)
                return "You must begin the siege at the recorded test target.";
            string blocker = GetPlayerPartyBlocker();
            if (!string.IsNullOrEmpty(blocker)) return blocker;
            Clan playerClan = Clan.PlayerClan;
            MobileParty party = MobileParty.MainParty;
            if (playerClan.StringId != record.AttackerClanId || party.StringId != record.LeaderPartyId
                || ResolveClan(party) != playerClan)
                return "The recorded player clan or main party no longer matches this test.";
            if (playerClan.Kingdom.StringId != record.KingdomId)
                return "Your clan no longer belongs to the recorded kingdom.";
            blocker = GetTargetStartBlocker(target);
            if (!string.IsNullOrEmpty(blocker)) return blocker;
            if (target.OwnerClan.StringId != record.DefenderClanId)
                return "The target is no longer owned by the recorded defending clan.";
            if (party.CurrentSettlement != target || Settlement.CurrentSettlement != target)
                return "Enter the recorded target's town or castle menu before beginning the siege.";
            if (party.Party.NumberOfHealthyMembers <= 0)
                return "Your party needs at least one healthy member to begin the siege.";
            return string.Empty;
        }

        internal static bool IsPlayerSiegeEncounter(PartyBase attackerParty, PartyBase defenderParty)
        {
            InternalWarTestRecord record = Find(defenderParty == null ? null : defenderParty.Settlement);
            MobileParty party = MobileParty.MainParty;
            Clan player = Clan.PlayerClan;
            Settlement target = defenderParty == null ? null : defenderParty.Settlement;
            if (record == null || (record.State != InternalWarTestState.Preparing && record.State != InternalWarTestState.Assaulting)
                || party == null || player == null || attackerParty != party.Party || target == null || defenderParty != target.Party
                || party.StringId != record.LeaderPartyId || player.StringId != record.AttackerClanId || ResolveClan(party) != player
                || target.StringId != record.SettlementId || target.OwnerClan == null || target.OwnerClan.StringId != record.DefenderClanId
                || player.Kingdom == null || player.Kingdom.StringId != record.KingdomId || target.OwnerClan.Kingdom != player.Kingdom
                || party.CurrentSettlement != null || party.Party.MapEvent != null || MapEvent.PlayerMapEvent != null)
                return false;
            SiegeEvent siege = target.SiegeEvent;
            return siege != null && siege.BesiegedSettlement == target && siege.BesiegerCamp != null
                && siege.BesiegerCamp.SiegeEvent == siege && siege.BesiegerCamp.LeaderParty == party
                && party.BesiegerCamp == siege.BesiegerCamp && PlayerSiege.PlayerSiegeEvent == siege;
        }

        internal static string GetTargetStartBlocker(Settlement target)
        {
            Clan playerClan = Clan.PlayerClan;
            if (playerClan == null) return "The player clan is unavailable.";
            if (target == null || !target.IsFortification || target.OwnerClan == null) return "The target must be an owned town or castle.";
            if (_rootBehavior != null && _rootBehavior.Controllers.Any(c => c != _behavior && c.OwnsSettlementOrder(target.StringId)))
                return "Another war has reserved this settlement for an operation.";
            if (target.OwnerClan == playerClan) return "Your clan already owns this fortification.";
            if (Conflict != null && Conflict.Phase == InternalConflictPhase.Active
                && !Conflict.CanTargetSettlement(playerClan.StringId, target.OwnerClan.StringId, target.StringId))
                return "Choose another fortification of the existing defending clan, or end that conflict first.";
            if (Conflict != null && playerClan.StringId == Conflict.AttackerClanId && Conflict.HasUnresolvedSettlementClaim
                && target.StringId != Conflict.GoalSettlementId)
                return "The active war goal is another fortification. Capture it or end the conflict before choosing a new claim.";
            if (playerClan.Kingdom == null || target.OwnerClan.Kingdom != playerClan.Kingdom)
                return "The defending clan must belong to your kingdom.";
            if (target.OwnerClan.IsClanTypeMercenary || target.OwnerClan.IsUnderMercenaryService)
                return "Mercenary defenders are excluded from this test.";
            if (target.OwnerClan.IsMinorFaction) return "Minor-faction defenders are excluded from this test.";
            if (target.SiegeEvent != null) return "The target is already under siege.";
            if (target.Party.MapEvent != null) return "The target is already participating in a battle or other map event.";
            return string.Empty;
        }

        internal static IEnumerable<Settlement> GetEligibleTargets()
        {
            Clan playerClan = Clan.PlayerClan;
            if (playerClan == null || playerClan.Kingdom == null) return Enumerable.Empty<Settlement>();
            return Settlement.All.Where(settlement => string.IsNullOrEmpty(GetTargetStartBlocker(settlement))).ToList();
        }

        internal static string GetStartBlocker()
        {
            string blocker = GetPlayerStartBlocker();
            if (!string.IsNullOrEmpty(blocker)) return blocker;
            return GetEligibleTargets().Any() ? string.Empty
                : "No eligible target: another regular clan in your kingdom must own a town or castle outside an existing siege or battle.";
        }

        internal static InternalWarTestRecord Find(Settlement settlement)
        { return RecoveryBlocked ? null : FindOwned(settlement); }

        // Mechanical lookup also remains available to suppression guards while recovery blocks new actions.
        internal static InternalWarTestRecord FindOwned(Settlement settlement)
        {
            InternalWarTestRecord record = _rootBehavior == null || settlement == null ? null : _rootBehavior.Controllers
                .Select(c => c.CurrentRecord).FirstOrDefault(r => r != null && r.IsOperational && r.SettlementId == settlement.StringId);
            if (record == null || settlement == null || record.SettlementId != settlement.StringId || !record.IsOperational)
                return null;
            SiegeEvent siege = settlement.SiegeEvent;
            if (siege != null && (siege.BesiegerCamp == null || siege.BesiegerCamp.LeaderParty == null
                || siege.BesiegerCamp.LeaderParty.StringId != record.LeaderPartyId)) return null;
            if (siege == null && record.State == InternalWarTestState.Marching) return null;
            return record;
        }

        internal static InternalWarTestRecord Find(MapEvent mapEvent)
        {
            if (mapEvent == null || !mapEvent.IsSiegeAssault) return null;
            InternalWarTestRecord record = Find(mapEvent.MapEventSettlement);
            MobileParty party = mapEvent.AttackerSide == null || mapEvent.AttackerSide.LeaderParty == null
                ? null : mapEvent.AttackerSide.LeaderParty.MobileParty;
            if (record == null || record.State == InternalWarTestState.Marching || party == null
                || party.StringId != record.LeaderPartyId || party.Party.MapEvent != mapEvent
                || mapEvent.AttackerSide == null || mapEvent.AttackerSide.LeaderParty != party.Party
                || ResolveClan(party) == null || ResolveClan(party).StringId != record.AttackerClanId) return null;
            return record;
        }

        internal static bool CanAuthorizeCapture(Hero capturer, Settlement settlement)
        {
            InternalWarTestRecord record = settlement == null ? null : Find(settlement.Party.MapEvent);
            return record != null && capturer != null && capturer.Clan != null
                && capturer.Clan.StringId == record.AttackerClanId && settlement.OwnerClan != null
                && settlement.OwnerClan.StringId == record.DefenderClanId
                && capturer.Clan.Kingdom != null && capturer.Clan.Kingdom.StringId == record.KingdomId
                && settlement.OwnerClan.Kingdom == capturer.Clan.Kingdom;
        }

        internal static Clan ResolveClan(PartyBase party)
        {
            if (party == null) return null;
            if (party.Settlement != null) return party.Settlement.OwnerClan;
            return ResolveClan(party.MobileParty);
        }

        internal static Clan ResolveClan(MobileParty party)
        {
            if (party == null) return null;
            if (party.ActualClan != null) return party.ActualClan;
            if (party.LeaderHero != null && party.LeaderHero.Clan != null) return party.LeaderHero.Clan;
            return party.HomeSettlement == null ? null : party.HomeSettlement.OwnerClan;
        }

        internal static BattleSideEnum SideOf(InternalWarTestRecord record, Clan clan)
        {
            if (record == null || clan == null) return BattleSideEnum.None;
            if (clan.StringId == record.AttackerClanId) return BattleSideEnum.Attacker;
            if (clan.StringId == record.DefenderClanId) return BattleSideEnum.Defender;
            return BattleSideEnum.None;
        }

        internal static bool CanJoin(InternalWarTestRecord record, PartyBase party, BattleSideEnum requestedSide)
        {
            Clan clan = ResolveClan(party);
            BattleSideEnum side = SideOf(record, clan);
            if (RecoveryBlocked || party == null || side == BattleSideEnum.None || side != requestedSide
                || clan.Kingdom == null || clan.Kingdom.StringId != record.KingdomId
                || clan.IsClanTypeMercenary || clan.IsUnderMercenaryService || (clan.IsMinorFaction && clan != Clan.PlayerClan)) return false;
            InternalConflictRecord war = _rootBehavior == null ? null : _rootBehavior.Conflicts
                .FirstOrDefault(c => c.Matches(record) && c.ActiveOperationId == record.ConflictId);
            if (war != null && war.Phase != InternalConflictPhase.Active && party.MapEvent == null) return false;
            if (party.Settlement != null)
                return requestedSide == BattleSideEnum.Defender && party.Settlement.StringId == record.SettlementId;
            MobileParty mobile = party.MobileParty;
            return mobile != null && mobile.IsActive && !mobile.IsVillager && !mobile.IsCaravan
                && !mobile.IsCurrentlyAtSea && mobile.Army == null && mobile.AttachedTo == null && mobile.AttachedParties.Count == 0
                && (mobile.CurrentSettlement == null || mobile.CurrentSettlement.StringId == record.SettlementId)
                && (mobile.BesiegerCamp == null || (mobile.BesiegerCamp.SiegeEvent != null
                    && mobile.BesiegerCamp.SiegeEvent.BesiegedSettlement != null
                    && mobile.BesiegerCamp.SiegeEvent.BesiegedSettlement.StringId == record.SettlementId))
                && (party.MapEvent == null || Find(party.MapEvent) == record || InternalWarSallyService.Find(party.MapEvent) == record
                    || InternalWarReliefService.Find(party.MapEvent) == record);
        }

        internal static IEnumerable<PartyBase> DefenderParties(Town town, MapEvent.BattleTypes battleType)
        {
            InternalWarTestRecord record = town == null ? null : Find(town.Settlement);
            if (record == null) return Enumerable.Empty<PartyBase>();
            List<PartyBase> result = new List<PartyBase> { town.Settlement.Party };
            foreach (MobileParty party in town.Settlement.Parties)
            {
                if (party == null || !party.IsActive || party.IsVillager || party.IsCaravan) continue;
                if (party.IsMilitia && (town.InRebelliousState || (int)battleType == 7)) continue;
                Clan clan = ResolveClan(party);
                if (clan != null && clan.StringId == record.DefenderClanId) result.Add(party.Party);
            }
            return result.Distinct().ToList();
        }

        internal static void MarkCapture(Settlement settlement)
        {
            if (_rootBehavior == null || settlement == null) return;
            InternalWarTestBehavior owner = _rootBehavior.Controllers.FirstOrDefault(c => c.CurrentRecord != null
                && c.CurrentRecord.IsOperational && c.CurrentRecord.SettlementId == settlement.StringId);
            if (owner != null) owner.MarkCapture(settlement);
        }
    }
}
