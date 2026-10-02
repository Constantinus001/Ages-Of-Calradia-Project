using System;
using AgesOfCalradiaInternalWarsTest;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;

// Calls the actual combat service with data-only parties. No native battle creation or AI execution.
internal static class CombatPolicyTests
{
    private static int _checks;

    private sealed class Fixture
    {
        internal InternalConflictRecord Conflict { get; set; }
        internal Clan AttackerClan { get; set; }
        internal Clan DefenderClan { get; set; }
        internal Clan NeutralClan { get; set; }
        internal MobileParty Attacker { get; set; }
        internal MobileParty Defender { get; set; }
    }

    internal static int Run()
    {
        _checks = 0;
        try
        {
            VerifyExactClanPair();
            VerifyPartyExclusions();
            VerifyBattleSideMapping(false);
            VerifyBattleSideMapping(true);
            VerifyBattleIdentityExclusions();
            VerifyPeaceAndClosedPhases();
            VerifyJoiningPartyExclusions();
            VerifyConcurrentConflicts();
            VerifySiegeJoinBoundaries();
            return _checks;
        }
        finally
        {
            InternalWarTestService.Detach();
            MobileParty.MainParty = null;
            Clan.PlayerClan = null;
            MapEvent.PlayerMapEvent = null;
        }
    }

    private static Fixture Reset()
    {
        InternalWarTestService.Detach();
        MapEvent.PlayerMapEvent = null;
        Kingdom kingdom = new Kingdom { StringId = "combat_kingdom" };
        Clan attacker = new Clan { StringId = "combat_attacker", Kingdom = kingdom, IsMinorFaction = true };
        Clan defender = new Clan { StringId = "combat_defender", Kingdom = kingdom };
        Clan neutral = new Clan { StringId = "combat_neutral", Kingdom = kingdom };
        InternalConflictRecord conflict = new InternalConflictRecord
        {
            Id = "combat_conflict", KingdomId = kingdom.StringId, AttackerClanId = attacker.StringId,
            DefenderClanId = defender.StringId, ActiveOperationId = "combat_operation", Phase = InternalConflictPhase.Active
        };
        InternalWarTestService.Attach(new InternalWarTestBehavior { Conflict = conflict });
        MobileParty first = NewLord(attacker);
        MobileParty second = NewLord(defender);
        MobileParty.MainParty = first;
        Clan.PlayerClan = attacker;
        return new Fixture { Conflict = conflict, AttackerClan = attacker, DefenderClan = defender,
            NeutralClan = neutral, Attacker = first, Defender = second };
    }

    private static MobileParty NewLord(Clan clan)
    {
        return new MobileParty { StringId = "party_" + clan.StringId, ActualClan = clan, IsActive = true, IsLordParty = true };
    }

    private static MapEvent NewBattle(Fixture fixture, bool reverse)
    {
        MobileParty attacker = reverse ? fixture.Defender : fixture.Attacker;
        MobileParty defender = reverse ? fixture.Attacker : fixture.Defender;
        MapEvent battle = new MapEvent
        {
            IsFieldBattle = true,
            AttackerSide = new MapEventSide { LeaderParty = attacker.Party },
            DefenderSide = new MapEventSide { LeaderParty = defender.Party }
        };
        attacker.Party.MapEvent = battle;
        defender.Party.MapEvent = battle;
        return battle;
    }

    private static void VerifyExactClanPair()
    {
        Fixture fixture = Reset();
        string before = fixture.Conflict.Serialize();
        Assert(InternalWarCombatService.Opposed(fixture.AttackerClan, fixture.DefenderClan), "Registered clans were not opposed.");
        Assert(InternalWarCombatService.Opposed(fixture.DefenderClan, fixture.AttackerClan), "Opposition was not symmetric.");
        Assert(!InternalWarCombatService.Opposed(fixture.AttackerClan, fixture.AttackerClan), "A clan opposed itself.");
        Assert(!InternalWarCombatService.Opposed(fixture.AttackerClan, fixture.NeutralClan), "Neutral realm clan became hostile.");
        Assert(!InternalWarCombatService.Opposed(null, fixture.DefenderClan)
            && !InternalWarCombatService.Opposed(fixture.AttackerClan, null), "Missing clan was treated as hostile.");
        Assert(InternalWarCombatService.CanStartFieldBattle(fixture.Attacker.Party, fixture.Defender.Party)
            && InternalWarCombatService.CanStartFieldBattle(fixture.Defender.Party, fixture.Attacker.Party),
            "Registered independent land lords could not begin a field battle in either orientation.");
        fixture.Attacker.IsLordParty = false;
        Assert(InternalWarCombatService.CanStartFieldBattle(fixture.Attacker.Party, fixture.Defender.Party),
            "Main-party exception to lord-party metadata was lost.");
        fixture.DefenderClan.Kingdom = new Kingdom { StringId = "foreign_kingdom" };
        Assert(!InternalWarCombatService.Opposed(fixture.AttackerClan, fixture.DefenderClan), "Cross-kingdom parties received internal hostility.");
        fixture.AttackerClan.Kingdom = fixture.DefenderClan.Kingdom;
        Assert(!InternalWarCombatService.Opposed(fixture.AttackerClan, fixture.DefenderClan),
            "Moving both clans to another kingdom retained the old conflict override.");
        fixture.AttackerClan.Kingdom = null;
        Assert(!InternalWarCombatService.Opposed(fixture.AttackerClan, fixture.DefenderClan), "Independent clan retained internal hostility.");
        Assert(fixture.Conflict.Serialize() == before && fixture.AttackerClan.IsMinorFaction,
            "Combat eligibility mutated the political record or player metadata.");
        InternalWarTestService.Detach();
        Assert(!InternalWarCombatService.Opposed(fixture.AttackerClan, fixture.DefenderClan, true),
            "Missing conflict enabled an existing-battle override.");
    }

    private static void VerifyPartyExclusions()
    {
        StartBlocked("inactive party", fixture => fixture.Defender.IsActive = false);
        StartBlocked("civilian party", fixture => fixture.Defender.IsLordParty = false);
        StartBlocked("caravan", fixture => fixture.Defender.IsCaravan = true);
        StartBlocked("villager", fixture => fixture.Defender.IsVillager = true);
        StartBlocked("sea party", fixture => fixture.Defender.IsCurrentlyAtSea = true);
        StartBlocked("army membership", fixture => fixture.Defender.Army = new Army());
        StartBlocked("attached to another party", fixture => fixture.Defender.AttachedTo = NewLord(fixture.DefenderClan));
        StartBlocked("attached follower", fixture => fixture.Defender.AttachedParties.Add(NewLord(fixture.DefenderClan)));
        StartBlocked("inside settlement", fixture => fixture.Defender.CurrentSettlement = new Settlement());
        StartBlocked("siege camp", fixture => fixture.Defender.BesiegerCamp = new BesiegerCamp());
        StartBlocked("already in a map event", fixture => fixture.Defender.Party.MapEvent = new MapEvent());
        StartBlocked("neutral clan", fixture => fixture.Defender.ActualClan = fixture.NeutralClan);
        Fixture current = Reset();
        Assert(!InternalWarCombatService.CanStartFieldBattle(null, current.Defender.Party)
            && !InternalWarCombatService.CanStartFieldBattle(current.Attacker.Party, null), "Null field-battle party was accepted.");
        Assert(!InternalWarCombatService.CanStartFieldBattle(current.Attacker.Party, new Settlement().Party),
            "Settlement party was accepted as a mobile land lord.");
    }

    private static void StartBlocked(string name, Action<Fixture> mutate)
    {
        Fixture fixture = Reset();
        mutate(fixture);
        Assert(!InternalWarCombatService.CanStartFieldBattle(fixture.Attacker.Party, fixture.Defender.Party)
            && !InternalWarCombatService.CanStartFieldBattle(fixture.Defender.Party, fixture.Attacker.Party),
            "Field start accepted excluded party: " + name + ".");
    }

    private static void VerifyBattleSideMapping(bool reverse)
    {
        Fixture fixture = Reset();
        MapEvent battle = NewBattle(fixture, reverse);
        Assert(InternalWarCombatService.IsConflictFieldBattle(battle), "Registered field battle was not recognized; reverse=" + reverse + ".");
        PartyBase politicalAttackerJoin = NewLord(fixture.AttackerClan).Party;
        PartyBase politicalDefenderJoin = NewLord(fixture.DefenderClan).Party;
        BattleSideEnum attackerNativeSide = reverse ? BattleSideEnum.Defender : BattleSideEnum.Attacker;
        BattleSideEnum defenderNativeSide = reverse ? BattleSideEnum.Attacker : BattleSideEnum.Defender;
        Assert(InternalWarCombatService.CanJoinFieldBattle(battle, politicalAttackerJoin, attackerNativeSide)
            && !InternalWarCombatService.CanJoinFieldBattle(battle, politicalAttackerJoin, defenderNativeSide),
            "Political attacker was assigned to the wrong native event side; reverse=" + reverse + ".");
        Assert(InternalWarCombatService.CanJoinFieldBattle(battle, politicalDefenderJoin, defenderNativeSide)
            && !InternalWarCombatService.CanJoinFieldBattle(battle, politicalDefenderJoin, attackerNativeSide),
            "Political defender was assigned to the wrong native event side; reverse=" + reverse + ".");
        Assert(!InternalWarCombatService.CanJoinFieldBattle(battle, NewLord(fixture.NeutralClan).Party, BattleSideEnum.Attacker)
            && !InternalWarCombatService.CanJoinFieldBattle(battle, NewLord(fixture.NeutralClan).Party, BattleSideEnum.Defender),
            "Neutral clan joined an internal field battle.");
        Assert(!InternalWarCombatService.CanJoinFieldBattle(battle, politicalAttackerJoin, BattleSideEnum.None)
            && !InternalWarCombatService.CanJoinFieldBattle(battle, politicalAttackerJoin, (BattleSideEnum)999),
            "Missing/undefined native side was accepted.");
    }

    private static void VerifyBattleIdentityExclusions()
    {
        InvalidBattle("non-field event", (fixture, battle) => battle.IsFieldBattle = false);
        InvalidBattle("settlement event", (fixture, battle) => battle.MapEventSettlement = new Settlement());
        InvalidBattle("missing attacker side", (fixture, battle) => battle.AttackerSide = null);
        InvalidBattle("missing defender side", (fixture, battle) => battle.DefenderSide = null);
        InvalidBattle("missing native attacker", (fixture, battle) => battle.AttackerSide.LeaderParty = null);
        InvalidBattle("missing native defender", (fixture, battle) => battle.DefenderSide.LeaderParty = null);
        InvalidBattle("attacker disconnected from event", (fixture, battle) => fixture.Attacker.Party.MapEvent = null);
        InvalidBattle("defender belongs to another event", (fixture, battle) => fixture.Defender.Party.MapEvent = new MapEvent());
        InvalidBattle("neutral event leader", (fixture, battle) => fixture.Defender.ActualClan = fixture.NeutralClan);
        InvalidBattle("foreign kingdom leader", (fixture, battle) => fixture.DefenderClan.Kingdom = new Kingdom { StringId = "foreign" });
        InvalidBattle("army entered event", (fixture, battle) => fixture.Defender.Army = new Army());
        InvalidBattle("naval event leader", (fixture, battle) => fixture.Defender.IsCurrentlyAtSea = true);
        Fixture current = Reset();
        Assert(!InternalWarCombatService.IsConflictFieldBattle(null), "Null event received field-battle override.");
        Assert(!InternalWarCombatService.CanJoinFieldBattle(null, current.Attacker.Party, BattleSideEnum.Attacker),
            "Null event accepted a joining party.");
    }

    private static void InvalidBattle(string name, Action<Fixture, MapEvent> mutate)
    {
        Fixture fixture = Reset();
        MapEvent battle = NewBattle(fixture, false);
        mutate(fixture, battle);
        Assert(!InternalWarCombatService.IsConflictFieldBattle(battle), "Unrelated event received override: " + name + ".");
        Assert(!InternalWarCombatService.CanJoinFieldBattle(battle, NewLord(fixture.AttackerClan).Party, BattleSideEnum.Attacker),
            "An unrelated/invalid event accepted reinforcement: " + name + ".");
    }

    private static void VerifyPeaceAndClosedPhases()
    {
        Fixture fixture = Reset();
        MapEvent battle = NewBattle(fixture, true);
        fixture.Conflict.RequestPeace();
        Assert(!InternalWarCombatService.Opposed(fixture.AttackerClan, fixture.DefenderClan)
            && InternalWarCombatService.Opposed(fixture.AttackerClan, fixture.DefenderClan, true),
            "Peace did not distinguish new hostility from an existing battle's continuity.");
        Assert(!InternalWarCombatService.CanStartFieldBattle(NewLord(fixture.AttackerClan).Party, NewLord(fixture.DefenderClan).Party),
            "A new field battle was authorized after peace was requested.");
        Assert(InternalWarCombatService.IsConflictFieldBattle(battle), "Pending peace dropped an existing battle's scoped continuity.");
        Assert(!InternalWarCombatService.CanJoinFieldBattle(battle, NewLord(fixture.AttackerClan).Party, BattleSideEnum.Defender),
            "Pending peace allowed a reinforcement to join.");
        fixture.Conflict.Phase = InternalConflictPhase.Closed;
        Assert(!InternalWarCombatService.IsConflictFieldBattle(battle)
            && !InternalWarCombatService.Opposed(fixture.AttackerClan, fixture.DefenderClan, true),
            "Closed conflict retained an existing-battle hostility override.");
        Assert(!InternalWarCombatService.CanStartFieldBattle(NewLord(fixture.AttackerClan).Party, NewLord(fixture.DefenderClan).Party)
            && !InternalWarCombatService.CanJoinFieldBattle(battle, NewLord(fixture.AttackerClan).Party, BattleSideEnum.Defender),
            "Closed conflict authorized a new start or join.");
    }

    private static void VerifyJoiningPartyExclusions()
    {
        JoinBlocked("party fighting elsewhere", party => party.Party.MapEvent = new MapEvent());
        JoinBlocked("army party", party => party.Army = new Army());
        JoinBlocked("attached party", party => party.AttachedTo = new MobileParty());
        JoinBlocked("party with attached followers", party => party.AttachedParties.Add(new MobileParty()));
        JoinBlocked("naval party", party => party.IsCurrentlyAtSea = true);
        JoinBlocked("caravan", party => party.IsCaravan = true);
        JoinBlocked("villager", party => party.IsVillager = true);
        JoinBlocked("civilian", party => party.IsLordParty = false);
        Fixture fixture = Reset();
        MapEvent battle = NewBattle(fixture, false);
        Assert(!InternalWarCombatService.CanJoinFieldBattle(battle, null, BattleSideEnum.Attacker), "Null joining party was accepted.");
    }

    private static void JoinBlocked(string name, Action<MobileParty> mutate)
    {
        Fixture fixture = Reset();
        MapEvent battle = NewBattle(fixture, false);
        MobileParty joiner = NewLord(fixture.AttackerClan);
        mutate(joiner);
        Assert(!InternalWarCombatService.CanJoinFieldBattle(battle, joiner.Party, BattleSideEnum.Attacker),
            "Excluded reinforcement was accepted: " + name + ".");
    }

    private static void VerifyConcurrentConflicts()
    {
        Fixture fixture = Reset();
        var secondWar = new InternalConflictRecord
        {
            Id = "second_war", KingdomId = fixture.AttackerClan.Kingdom.StringId,
            AttackerClanId = fixture.AttackerClan.StringId, DefenderClanId = fixture.NeutralClan.StringId
        };
        var registry = new InternalWarConflictRegistry();
        Assert(registry.Add(fixture.Conflict) && registry.Add(secondWar), "Concurrent war fixture was rejected.");
        var owner = new InternalWarTestBehavior { Conflict = fixture.Conflict, Registry = registry };
        InternalWarTestService.Attach(owner);
        Assert(InternalWarCombatService.Opposed(fixture.AttackerClan, fixture.DefenderClan)
            && InternalWarCombatService.Opposed(fixture.AttackerClan, fixture.NeutralClan),
            "Both registered simultaneous enemies were not recognized.");
        Assert(!InternalWarCombatService.Opposed(fixture.DefenderClan, fixture.NeutralClan),
            "Sharing an enemy created an unregistered war.");
        MapEvent firstBattle = NewBattle(fixture, false);
        MobileParty secondAttacker = NewLord(fixture.AttackerClan);
        MobileParty secondDefender = NewLord(fixture.NeutralClan);
        MapEvent secondBattle = new MapEvent { IsFieldBattle = true,
            AttackerSide = new MapEventSide { LeaderParty = secondAttacker.Party },
            DefenderSide = new MapEventSide { LeaderParty = secondDefender.Party } };
        secondAttacker.Party.MapEvent = secondBattle;
        secondDefender.Party.MapEvent = secondBattle;
        owner.Conflict = secondWar;
        Assert(InternalWarCombatService.IsConflictFieldBattle(firstBattle)
            && InternalWarCombatService.IsConflictFieldBattle(secondBattle), "Changing selected war changed battle continuity.");
        Assert(InternalWarCombatService.CanJoinFieldBattle(firstBattle, NewLord(fixture.DefenderClan).Party, BattleSideEnum.Defender)
            && !InternalWarCombatService.CanJoinFieldBattle(firstBattle, NewLord(fixture.NeutralClan).Party, BattleSideEnum.Defender),
            "Another war's participant crossed the battle reinforcement boundary.");
        fixture.Conflict.RequestPeace();
        Assert(!InternalWarCombatService.CanStartFieldBattle(NewLord(fixture.AttackerClan).Party, NewLord(fixture.DefenderClan).Party)
            && InternalWarCombatService.CanStartFieldBattle(NewLord(fixture.AttackerClan).Party, NewLord(fixture.NeutralClan).Party),
            "Peace in A-B affected new A-C battles or permitted new A-B battles.");
        Assert(InternalWarCombatService.IsConflictFieldBattle(firstBattle)
            && InternalWarCombatService.IsConflictFieldBattle(secondBattle), "Pending peace lost either battle's continuity.");
        Assert(!InternalWarCombatService.CanJoinFieldBattle(firstBattle, NewLord(fixture.AttackerClan).Party, BattleSideEnum.Attacker)
            && InternalWarCombatService.CanJoinFieldBattle(secondBattle, NewLord(fixture.AttackerClan).Party, BattleSideEnum.Attacker),
            "Pending peace reinforcement policy leaked between conflicts.");
        fixture.Conflict.Phase = InternalConflictPhase.Closed;
        owner.Conflict = fixture.Conflict;
        Assert(!InternalWarCombatService.IsConflictFieldBattle(firstBattle)
            && InternalWarCombatService.IsConflictFieldBattle(secondBattle), "Closing selected A-B ended unrelated A-C continuity.");
        Assert(InternalWarCombatService.CanStartFieldBattle(NewLord(fixture.AttackerClan).Party, NewLord(fixture.NeutralClan).Party)
            && secondWar.Phase == InternalConflictPhase.Active, "Closed selected war blocked or mutated another active war.");
    }

    private static void VerifySiegeJoinBoundaries()
    {
        foreach (Action<MobileParty> invalid in new Action<MobileParty>[] {
            p => p.IsActive = false, p => p.IsVillager = true, p => p.IsCaravan = true,
            p => p.IsCurrentlyAtSea = true, p => p.Army = new Army(), p => p.AttachedTo = new MobileParty(),
            p => p.AttachedParties.Add(new MobileParty()), p => p.CurrentSettlement = new Settlement { StringId = "foreign" },
            p => p.Party.MapEvent = new MapEvent(), p => p.BesiegerCamp = new BesiegerCamp() })
        {
            Fixture fixture = Reset();
            var record = new InternalWarTestRecord { ConflictId = fixture.Conflict.ActiveOperationId,
                KingdomId = fixture.Conflict.KingdomId, AttackerClanId = fixture.AttackerClan.StringId,
                DefenderClanId = fixture.DefenderClan.StringId, SettlementId = "siege_target", State = InternalWarTestState.Preparing };
            Assert(InternalWarTestService.CanJoin(record, fixture.Attacker.Party, BattleSideEnum.Attacker), "Free siege participant rejected.");
            Assert(!InternalWarTestService.CanJoin(record, fixture.Attacker.Party, BattleSideEnum.Defender), "Siege side inverted.");
            invalid(fixture.Attacker);
            Assert(!InternalWarTestService.CanJoin(record, fixture.Attacker.Party, BattleSideEnum.Attacker), "Unsupported siege reinforcement accepted.");
            fixture.Conflict.RequestPeace();
            Assert(!InternalWarTestService.CanJoin(record, fixture.Defender.Party, BattleSideEnum.Defender), "New siege reinforcement accepted during peace.");
        }
    }

    private static void Assert(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException("Combat policy: " + message);
    }
}
