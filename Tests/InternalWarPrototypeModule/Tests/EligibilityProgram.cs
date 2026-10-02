using System;
using System.Linq;
using AgesOfCalradiaInternalWarsTest;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;

internal static class EligibilityProgram
{
    private static int _assertions;
    private static int _scenarios;

    private sealed class Fixture
    {
        internal Clan Player { get; set; }
        internal MobileParty MainParty { get; set; }
        internal Settlement Target { get; set; }
        internal InternalWarTestRecord Record { get; set; }
        internal PartyBase EncounterAttacker { get; set; }
        internal PartyBase EncounterDefender { get; set; }
    }

    private static int Main()
    {
        try
        {
            VerifySwornPlayerMinorFlagException();
            VerifyPlayerBlockers();
            VerifyConflictStateBlockers();
            VerifyTargetBlockers();
            VerifyEligibleListAndNoTargets();
            VerifyBeginSiegeAllowedAndReadOnly();
            VerifyBeginSiegeBlockers();
            VerifyBeginSiegeStateAndRepeatedStart();
            VerifyPlayerSiegeEncounterAllowed();
            VerifyPlayerSiegeEncounterBlocked();
            VerifyEncounterPostfixCorrection();
            VerifyEncounterPostfixNoOps();
            VerifyOperationAuthorization();
            VerifyForeignSiegeIsolation();
            VerifyContinuingConflict();
            VerifyMultipleControllerRouting();
            Console.WriteLine("Combat policy assertions: " + CombatPolicyTests.Run());
            Console.WriteLine("Internal-war eligibility service checks passed: " + _scenarios
                + " scenarios, " + _assertions + " assertions. Actual service source; data stubs only, no native runtime evidence.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        finally
        {
            InternalWarTestService.Detach();
            Clan.PlayerClan = null;
            MobileParty.MainParty = null;
            Settlement.All.Clear();
            Settlement.CurrentSettlement = null;
            PlayerSiege.PlayerSiegeEvent = null;
            MapEvent.PlayerMapEvent = null;
            PlayerEncounter.Current = null;
        }
    }

    private static Fixture Reset()
    {
        InternalWarTestService.Detach();
        Settlement.All.Clear();
        Settlement.CurrentSettlement = null;
        PlayerSiege.PlayerSiegeEvent = null;
        MapEvent.PlayerMapEvent = null;
        PlayerEncounter.Current = null;
        Clan player = new Clan { StringId = "player_faction", Kingdom = new Kingdom { StringId = "original_kingdom" }, IsMinorFaction = true };
        player.Leader = new Hero { Clan = player };
        Clan.PlayerClan = player;
        MobileParty.MainParty = new MobileParty { StringId = "main_party", ActualClan = player, IsActive = true };
        MobileParty.MainParty.Party.NumberOfHealthyMembers = 40;
        Settlement target = NewTarget("castle", player.Kingdom);
        Settlement.All.Add(target);
        return new Fixture { Player = player, MainParty = MobileParty.MainParty, Target = target };
    }

    private static Settlement NewTarget(string id, Kingdom kingdom)
    {
        return new Settlement
        {
            StringId = id,
            IsFortification = true,
            OwnerClan = new Clan { StringId = id + "_owner", Kingdom = kingdom }
        };
    }

    private static void VerifySwornPlayerMinorFlagException()
    {
        Fixture fixture = Reset();
        Assert(fixture.Player.IsMinorFaction, "Fixture must exercise the retained native player minor flag.");
        Assert(InternalWarTestService.GetPlayerStartBlocker() == string.Empty, "Sworn player with minor flag was rejected.");
        Assert(InternalWarTestService.GetTargetStartBlocker(fixture.Target) == string.Empty, "Regular same-kingdom defender was rejected.");
        Assert(InternalWarTestService.GetStartBlocker() == string.Empty, "Eligible player/target combination was rejected.");
        Assert(InternalWarTestService.GetEligibleTargets().Single() == fixture.Target, "Eligible target was not returned.");
        Assert(fixture.Player.IsMinorFaction, "Eligibility checks mutated the player minor flag.");
        fixture.Player.IsMinorFaction = false;
        Assert(InternalWarTestService.GetStartBlocker() == string.Empty, "Regular player clan was rejected.");
        Assert(!fixture.Player.IsMinorFaction, "Eligibility checks mutated a false player minor flag.");
        _scenarios += 2;
    }

    private static void VerifyPlayerBlockers()
    {
        PlayerBlocked("missing player", fixture => Clan.PlayerClan = null, "player clan is unavailable");
        PlayerBlocked("missing leader", fixture => fixture.Player.Leader = null, "living leader");
        PlayerBlocked("missing kingdom", fixture => fixture.Player.Kingdom = null, "vassal or ruler");
        PlayerBlocked("mercenary clan type", fixture => fixture.Player.IsClanTypeMercenary = true, "Mercenary-type");
        PlayerBlocked("mercenary contract", fixture => fixture.Player.IsUnderMercenaryService = true, "mercenary contract");
        PlayerBlocked("missing main party", fixture => MobileParty.MainParty = null, "main party is unavailable");
        PlayerBlocked("inactive main party", fixture => fixture.MainParty.IsActive = false, "inactive");
        PlayerBlocked("naval main party", fixture => fixture.MainParty.IsCurrentlyAtSea = true, "on land");
        PlayerBlocked("attached main party", fixture => fixture.MainParty.AttachedTo = new MobileParty(), "Detach");
        PlayerBlocked("linked main party", fixture => fixture.MainParty.AttachedParties.Add(new MobileParty()), "Detach");
        PlayerBlocked("army membership", fixture => fixture.MainParty.Army = new Army(), "army");
        PlayerBlocked("party map event", fixture => fixture.MainParty.Party.MapEvent = new MapEvent(), "map event");
        PlayerBlocked("siege camp membership", fixture => fixture.MainParty.BesiegerCamp = new BesiegerCamp(), "siege camp");
    }

    private static void PlayerBlocked(string name, Action<Fixture> mutate, string messageFragment)
    {
        Fixture fixture = Reset();
        mutate(fixture);
        string blocker = InternalWarTestService.GetPlayerStartBlocker();
        Contains(blocker, messageFragment, name);
        Assert(InternalWarTestService.GetStartBlocker() == blocker, name + ": aggregate gate lost the player blocker.");
        Assert(fixture.Player.IsMinorFaction, name + ": checking eligibility mutated the player's minor flag.");
        _scenarios++;
    }

    private static void VerifyConflictStateBlockers()
    {
        foreach (InternalWarTestState state in (InternalWarTestState[])Enum.GetValues(typeof(InternalWarTestState)))
        {
            Reset();
            InternalWarTestService.Attach(new InternalWarTestBehavior
            {
                CurrentRecord = new InternalWarTestRecord { State = state, CleanupComplete = true }
            });
            bool operational = state == InternalWarTestState.Marching || state == InternalWarTestState.Preparing
                || state == InternalWarTestState.Assaulting || state == InternalWarTestState.RoyalPeacePending;
            string blocker = InternalWarTestService.GetStartBlocker();
            if (operational) Contains(blocker, "This war already has an active operation", state.ToString());
            else Assert(blocker == string.Empty, "Terminal/inactive state blocked a new test: " + state);
            _scenarios++;
        }
    }

    private static void VerifyTargetBlockers()
    {
        TargetBlocked("null target", fixture => fixture.Target = null, "owned town or castle");
        TargetBlocked("unowned target", fixture => fixture.Target.OwnerClan = null, "owned town or castle");
        TargetBlocked("village target", fixture => fixture.Target.IsFortification = false, "town or castle");
        TargetBlocked("player-owned target", fixture => fixture.Target.OwnerClan = fixture.Player, "already owns");
        TargetBlocked("foreign kingdom target", fixture => fixture.Target.OwnerClan.Kingdom = new Kingdom(), "your kingdom");
        TargetBlocked("independent target", fixture => fixture.Target.OwnerClan.Kingdom = null, "your kingdom");
        TargetBlocked("minor defender", fixture => fixture.Target.OwnerClan.IsMinorFaction = true, "Minor-faction defenders");
        TargetBlocked("mercenary defender type", fixture => fixture.Target.OwnerClan.IsClanTypeMercenary = true, "Mercenary defenders");
        TargetBlocked("mercenary defender contract", fixture => fixture.Target.OwnerClan.IsUnderMercenaryService = true, "Mercenary defenders");
        TargetBlocked("existing target siege", fixture => fixture.Target.SiegeEvent = new SiegeEvent(), "under siege");
        TargetBlocked("target map event", fixture => fixture.Target.Party.MapEvent = new MapEvent(), "map event");
        TargetBlocked("target with missing player", fixture => Clan.PlayerClan = null, "player clan is unavailable");
        TargetBlocked("target with independent player", fixture => fixture.Player.Kingdom = null, "your kingdom");
    }

    private static void VerifyOperationAuthorization()
    {
        Fixture fixture = Reset();
        InternalWarTestRecord record = new InternalWarTestRecord { State = InternalWarTestState.Assaulting,
            KingdomId = fixture.Player.Kingdom.StringId, AttackerClanId = fixture.Player.StringId,
            DefenderClanId = fixture.Target.OwnerClan.StringId, SettlementId = fixture.Target.StringId,
            LeaderPartyId = fixture.MainParty.StringId };
        InternalWarTestService.Attach(new InternalWarTestBehavior { CurrentRecord = record });
        MapEvent battle = new MapEvent { IsSiegeAssault = true, MapEventSettlement = fixture.Target,
            AttackerSide = new MapEventSide { LeaderParty = fixture.MainParty.Party } };
        fixture.Target.Party.MapEvent = battle;
        fixture.MainParty.Party.MapEvent = battle;
        Assert(InternalWarTestService.Find(battle) == record, "Exact operation battle rejected.");
        Assert(InternalWarTestService.CanAuthorizeCapture(fixture.Player.Leader, fixture.Target), "Exact capture rejected.");
        Assert(!InternalWarTestService.CanAuthorizeCapture(new Hero { Clan = fixture.Target.OwnerClan }, fixture.Target), "Foreign capturer accepted.");
        battle.AttackerSide.LeaderParty = new PartyBase();
        Assert(InternalWarTestService.Find(battle) == null, "Foreign battle leader accepted.");
        battle.AttackerSide.LeaderParty = fixture.MainParty.Party;
        fixture.MainParty.Party.MapEvent = new MapEvent();
        Assert(InternalWarTestService.Find(battle) == null, "Unrelated player event accepted.");
        fixture.MainParty.Party.MapEvent = battle;
        record.State = InternalWarTestState.Marching;
        Assert(!InternalWarTestService.CanAuthorizeCapture(fixture.Player.Leader, fixture.Target), "Marching record hijacked capture.");
        record.State = InternalWarTestState.Assaulting;
        fixture.Target.OwnerClan = fixture.Player;
        Assert(!InternalWarTestService.CanAuthorizeCapture(fixture.Player.Leader, fixture.Target), "Duplicate ownership transfer accepted.");
        _scenarios += 7;
    }

    private static void VerifyMultipleControllerRouting()
    {
        Fixture fixture = Reset();
        var selected = new InternalWarTestBehavior();
        var owner = new InternalWarTestBehavior { CurrentRecord = new InternalWarTestRecord
            { ConflictId = "other-war-operation", KingdomId = fixture.Player.Kingdom.StringId,
                AttackerClanId = fixture.Player.StringId, DefenderClanId = fixture.Target.OwnerClan.StringId,
                SettlementId = fixture.Target.StringId, LeaderPartyId = fixture.MainParty.StringId,
                State = InternalWarTestState.Marching } };
        var root = new InternalWarTestBehavior { SelectedController = selected,
            TestControllers = new System.Collections.Generic.List<InternalWarTestBehavior> { selected, owner } };
        InternalWarTestService.Attach(root);
        Assert(InternalWarTestService.Current == null, "Selected empty controller exposed another war's operation.");
        Assert(InternalWarTestService.PartyAssigned(fixture.MainParty.StringId), "Unselected marching party was not reserved.");
        Assert(InternalWarTestService.SettlementAssigned(fixture.Target.StringId), "Unselected marching settlement was not reserved.");
        Assert(!InternalWarTestService.PartyAssigned("free-party") && !InternalWarTestService.SettlementAssigned("free-target"),
            "Unrelated identities were reserved.");
        Assert(!string.IsNullOrEmpty(InternalWarTestService.GetPlayerStartBlocker()),
            "Selecting another war allowed the same marching player party to start twice.");
        Assert(!string.IsNullOrEmpty(InternalWarTestService.GetTargetStartBlocker(fixture.Target)),
            "Selecting another war released its reserved target.");
        Assert(InternalWarTestService.OwnsOperation(owner.CurrentRecord), "Operation ownership depended on selection.");
        Assert(!InternalWarTestService.OwnsOperation(new InternalWarTestRecord { ConflictId = owner.CurrentRecord.ConflictId }),
            "A copied record acquired operation ownership.");
        owner.CurrentRecord.State = InternalWarTestState.Captured;
        owner.CurrentRecord.CleanupComplete = true;
        Assert(!InternalWarTestService.PartyAssigned(fixture.MainParty.StringId)
            && !InternalWarTestService.SettlementAssigned(fixture.Target.StringId), "Completed operation retained reservations.");
        Assert(InternalWarTestService.GetPlayerStartBlocker() == string.Empty
            && InternalWarTestService.GetTargetStartBlocker(fixture.Target) == string.Empty,
            "Completed other-war operation still blocked new orders.");
        _scenarios += 10;

        owner.CurrentRecord.State = InternalWarTestState.Assaulting;
        owner.CurrentRecord.CleanupComplete = false;
        MapEvent battle = new MapEvent { IsSiegeAssault = true, MapEventSettlement = fixture.Target,
            AttackerSide = new MapEventSide { LeaderParty = fixture.MainParty.Party } };
        fixture.Target.Party.MapEvent = battle;
        fixture.MainParty.Party.MapEvent = battle;
        Assert(InternalWarTestService.Find(fixture.Target) == owner.CurrentRecord,
            "Settlement lookup ignored its unselected controller.");
        Assert(InternalWarTestService.Find(battle) == owner.CurrentRecord,
            "Battle lookup ignored its unselected controller.");
        Assert(InternalWarTestService.CanAuthorizeCapture(fixture.Player.Leader, fixture.Target),
            "Capture authorization depended on the selected war.");
        int captureCalls = 0;
        owner.CaptureObserver = settlement => { Assert(settlement == fixture.Target, "Wrong capture target routed."); captureCalls++; };
        selected.CaptureObserver = settlement => { throw new InvalidOperationException("Capture routed to the selected unrelated war."); };
        InternalWarTestService.MarkCapture(fixture.Target);
        Assert(captureCalls == 1, "Capture was not dispatched exactly once to its owning controller.");
        root.SelectedController = owner;
        Assert(InternalWarTestService.Current == owner.CurrentRecord && InternalWarTestService.Find(battle) == owner.CurrentRecord,
            "Changing selection replaced the live operation identity.");
        root.SelectedController = selected;
        Assert(InternalWarTestService.Find(battle) == owner.CurrentRecord,
            "Changing selection back invalidated ongoing battle routing.");
        _scenarios += 6;
    }

    private static void VerifyForeignSiegeIsolation()
    {
        foreach (InternalWarTestState state in new[] { InternalWarTestState.Marching, InternalWarTestState.Preparing,
            InternalWarTestState.Assaulting, InternalWarTestState.RoyalPeacePending })
        {
            Fixture fixture = ResetSiegeEncounter();
            fixture.Record.State = state;
            string recordBefore = fixture.Record.Serialize();
            SiegeEvent siege = fixture.Target.SiegeEvent;
            BesiegerCamp camp = siege.BesiegerCamp;
            Town town = new Town { Settlement = fixture.Target };
            Assert(InternalWarTestService.Find(fixture.Target) == fixture.Record,
                state + ": the recorded leader's current camp was rejected.");
            _scenarios++;

            camp.LeaderParty = new MobileParty { StringId = "other_same_clan_party", ActualClan = fixture.Player };
            Assert(InternalWarTestService.Find(fixture.Target) == null,
                state + ": a different same-clan leader's siege was adopted.");
            Assert(!InternalWarTestService.DefenderParties(town, MapEvent.BattleTypes.Siege).Any(),
                state + ": another same-clan leader's siege received the operation defender list.");
            _scenarios++;

            camp.LeaderParty = new MobileParty { StringId = "foreign_siege_leader",
                ActualClan = new Clan { StringId = "foreign_clan", Kingdom = new Kingdom { StringId = "foreign_kingdom" } } };
            Assert(InternalWarTestService.Find(fixture.Target) == null,
                state + ": a foreign-led siege at the recorded target was adopted.");
            Assert(!InternalWarTestService.DefenderParties(town, MapEvent.BattleTypes.Siege).Any(),
                state + ": a foreign-led siege received the operation defender list.");
            _scenarios++;

            camp.LeaderParty = null;
            Assert(InternalWarTestService.Find(fixture.Target) == null,
                state + ": a leaderless current siege was adopted.");
            _scenarios++;
            siege.BesiegerCamp = null;
            Assert(InternalWarTestService.Find(fixture.Target) == null,
                state + ": a current siege without a camp was adopted.");
            _scenarios++;
            Assert(fixture.Record.Serialize() == recordBefore && fixture.Player.IsMinorFaction,
                state + ": lookup changed the operation record or native player minor flag.");
        }

        Fixture marching = ResetBeginSiege();
        string marchingBefore = marching.Record.Serialize();
        Assert(InternalWarTestService.Find(marching.Target) == null,
            "A marching record without an owned camp enabled settlement siege overrides.");
        Assert(!InternalWarTestService.DefenderParties(new Town { Settlement = marching.Target }, MapEvent.BattleTypes.Siege).Any(),
            "A marching record without an owned camp supplied a siege defender list.");
        Assert(marching.Record.Serialize() == marchingBefore && marching.Player.IsMinorFaction,
            "Marching lookup changed saved state or the native player minor flag.");
        _scenarios++;
    }

    private static void VerifyContinuingConflict()
    {
        Fixture fixture = Reset();
        InternalConflictRecord conflict = new InternalConflictRecord { Id = "war", KingdomId = fixture.Player.Kingdom.StringId,
            AttackerClanId = fixture.Player.StringId, DefenderClanId = fixture.Target.OwnerClan.StringId };
        InternalWarTestRecord record = new InternalWarTestRecord { State = InternalWarTestState.Captured, CleanupComplete = true };
        InternalWarTestBehavior behavior = new InternalWarTestBehavior { CurrentRecord = record, Conflict = conflict };
        InternalWarTestService.Attach(behavior);
        Assert(InternalWarTestService.HasOpenConflict && !InternalWarTestService.HasOperationalConflict, "War and operation lifetimes conflated.");
        Assert(InternalWarTestService.GetStartBlocker() == string.Empty, "Clean continuing war cannot select next target.");
        Settlement neutral = NewTarget("neutral", fixture.Player.Kingdom);
        Assert(InternalWarTestService.GetTargetStartBlocker(neutral) != string.Empty, "Continuation changed defending clan.");
        record.CleanupComplete = false;
        Assert(InternalWarTestService.GetStartBlocker() != string.Empty, "Unverified cleanup allowed continuation.");
        record.CleanupComplete = true;
        conflict.Phase = InternalConflictPhase.PeacePending;
        Assert(InternalWarTestService.GetStartBlocker() != string.Empty, "Pending peace allowed another attack.");
        conflict.Phase = InternalConflictPhase.Active;
        behavior.RecoveryBlocker = "Malformed preserved payload";
        Assert(InternalWarTestService.GetStartBlocker() == behavior.RecoveryBlocker, "Malformed save did not fail closed.");
        _scenarios += 6;
    }

    private static void TargetBlocked(string name, Action<Fixture> mutate, string messageFragment)
    {
        Fixture fixture = Reset();
        mutate(fixture);
        Contains(InternalWarTestService.GetTargetStartBlocker(fixture.Target), messageFragment, name);
        Settlement.All.Clear();
        Settlement.All.Add(fixture.Target);
        Assert(!InternalWarTestService.GetEligibleTargets().Any(), name + ": rejected target appeared in eligible list.");
        Assert(fixture.Player.IsMinorFaction, name + ": checking a defender mutated the player's minor flag.");
        _scenarios++;
    }

    private static void VerifyEligibleListAndNoTargets()
    {
        Fixture fixture = Reset();
        Settlement second = NewTarget("town", fixture.Player.Kingdom);
        Settlement minor = NewTarget("minor_castle", fixture.Player.Kingdom);
        minor.OwnerClan.IsMinorFaction = true;
        Settlement foreign = NewTarget("foreign_castle", new Kingdom());
        Settlement.All.Add(second);
        Settlement.All.Add(minor);
        Settlement.All.Add(foreign);
        Settlement.All.Add(null);
        Settlement[] eligible = InternalWarTestService.GetEligibleTargets().ToArray();
        Assert(eligible.Length == 2 && eligible.Contains(fixture.Target) && eligible.Contains(second),
            "Mixed target list did not contain exactly the regular same-kingdom castle and town.");
        Assert(minor.OwnerClan.IsMinorFaction, "Target filtering mutated the minor defender flag.");
        _scenarios++;

        Settlement.All.Clear();
        Settlement.All.Add(minor);
        Settlement.All.Add(foreign);
        Contains(InternalWarTestService.GetStartBlocker(), "No eligible target", "all targets rejected");
        Settlement.All.Clear();
        Contains(InternalWarTestService.GetStartBlocker(), "No eligible target", "empty target list");
        fixture.MainParty.Army = new Army();
        Contains(InternalWarTestService.GetStartBlocker(), "army", "player blocker takes precedence over no targets");
        _scenarios += 3;
    }

    private static Fixture ResetBeginSiege()
    {
        Fixture fixture = Reset();
        fixture.MainParty.CurrentSettlement = fixture.Target;
        Settlement.CurrentSettlement = fixture.Target;
        fixture.Record = new InternalWarTestRecord
        {
            ConflictId = "test_begin",
            KingdomId = fixture.Player.Kingdom.StringId,
            AttackerClanId = fixture.Player.StringId,
            DefenderClanId = fixture.Target.OwnerClan.StringId,
            SettlementId = fixture.Target.StringId,
            LeaderPartyId = fixture.MainParty.StringId,
            State = InternalWarTestState.Marching,
            StartDay = 1
        };
        InternalWarTestService.Attach(new InternalWarTestBehavior { CurrentRecord = fixture.Record });
        return fixture;
    }

    private static void VerifyBeginSiegeAllowedAndReadOnly()
    {
        Fixture fixture = ResetBeginSiege();
        string originalRecord = fixture.Record.Serialize();
        Clan originalOwner = fixture.Target.OwnerClan;
        Kingdom originalKingdom = fixture.Player.Kingdom;
        Assert(InternalWarTestService.GetBeginSiegeBlocker(fixture.Target) == string.Empty,
            "Arrived, eligible player with a matching marching test was blocked from beginning the siege.");
        Assert(fixture.Record.Serialize() == originalRecord, "Checking begin eligibility changed the saved record.");
        Assert(fixture.Player.IsMinorFaction, "Begin eligibility changed the player minor flag.");
        Assert(fixture.Target.OwnerClan == originalOwner && fixture.Player.Kingdom == originalKingdom
            && originalOwner.Kingdom == originalKingdom, "Begin eligibility mutated political ownership or membership.");
        _scenarios++;
    }

    private static void VerifyBeginSiegeBlockers()
    {
        BeginBlocked("no attached behavior", fixture => InternalWarTestService.Detach());
        BeginBlocked("no saved record", fixture => InternalWarTestService.Attach(new InternalWarTestBehavior()));
        BeginBlocked("null target", fixture => fixture.Target = null);
        BeginBlocked("different target", fixture => fixture.Target = NewTarget("other_town", fixture.Player.Kingdom));
        BeginBlocked("missing player", fixture => Clan.PlayerClan = null);
        BeginBlocked("different player identity", fixture => fixture.Player.StringId = "other_player");
        BeginBlocked("missing player leader", fixture => fixture.Player.Leader = null);
        BeginBlocked("missing player kingdom", fixture => fixture.Player.Kingdom = null);
        BeginBlocked("both clans moved to a different kingdom", fixture =>
        {
            Kingdom replacement = new Kingdom { StringId = "replacement_kingdom" };
            fixture.Player.Kingdom = replacement;
            fixture.Target.OwnerClan.Kingdom = replacement;
        });
        BeginBlocked("missing main party", fixture => MobileParty.MainParty = null);
        BeginBlocked("different main-party identity", fixture => fixture.MainParty.StringId = "other_party");
        BeginBlocked("main party belongs to another clan", fixture => fixture.MainParty.ActualClan = fixture.Target.OwnerClan);
        BeginBlocked("inactive main party", fixture => fixture.MainParty.IsActive = false);
        BeginBlocked("player army membership", fixture => fixture.MainParty.Army = new Army());
        BeginBlocked("player map event", fixture => fixture.MainParty.Party.MapEvent = new MapEvent());
        BeginBlocked("existing player siege camp", fixture => fixture.MainParty.BesiegerCamp = new BesiegerCamp());
        BeginBlocked("mercenary player type", fixture => fixture.Player.IsClanTypeMercenary = true);
        BeginBlocked("mercenary player contract", fixture => fixture.Player.IsUnderMercenaryService = true);
        BeginBlocked("unowned target", fixture => fixture.Target.OwnerClan = null);
        BeginBlocked("changed regular owner within same kingdom", fixture => fixture.Target.OwnerClan = new Clan
        {
            StringId = "replacement_defender", Kingdom = fixture.Player.Kingdom
        });
        BeginBlocked("player-owned target", fixture => fixture.Target.OwnerClan = fixture.Player);
        BeginBlocked("foreign defender kingdom", fixture => fixture.Target.OwnerClan.Kingdom = new Kingdom { StringId = "foreign" });
        BeginBlocked("minor-faction defender", fixture => fixture.Target.OwnerClan.IsMinorFaction = true);
        BeginBlocked("mercenary defender type", fixture => fixture.Target.OwnerClan.IsClanTypeMercenary = true);
        BeginBlocked("mercenary defender contract", fixture => fixture.Target.OwnerClan.IsUnderMercenaryService = true);
        BeginBlocked("village target", fixture => fixture.Target.IsFortification = false);
        BeginBlocked("existing foreign siege", fixture => fixture.Target.SiegeEvent = new SiegeEvent());
        BeginBlocked("target map event", fixture => fixture.Target.Party.MapEvent = new MapEvent());
        BeginBlocked("party has not entered a settlement", fixture => fixture.MainParty.CurrentSettlement = null);
        BeginBlocked("party is at another settlement", fixture => fixture.MainParty.CurrentSettlement = NewTarget("remote", fixture.Player.Kingdom));
        BeginBlocked("campaign has no current settlement", fixture => Settlement.CurrentSettlement = null);
        BeginBlocked("campaign menu belongs to another settlement", fixture => Settlement.CurrentSettlement = NewTarget("remote_menu", fixture.Player.Kingdom));
        BeginBlocked("zero healthy troops", fixture => fixture.MainParty.Party.NumberOfHealthyMembers = 0);
        BeginBlocked("invalid negative healthy count", fixture => fixture.MainParty.Party.NumberOfHealthyMembers = -1);
    }

    private static void VerifyBeginSiegeStateAndRepeatedStart()
    {
        foreach (InternalWarTestState state in (InternalWarTestState[])Enum.GetValues(typeof(InternalWarTestState)))
        {
            if (state == InternalWarTestState.Marching) continue;
            BeginBlocked("non-marching state " + state, fixture => fixture.Record.State = state);
        }

        Fixture repeatedFixture = ResetBeginSiege();
        Assert(InternalWarTestService.GetBeginSiegeBlocker(repeatedFixture.Target) == string.Empty,
            "Initial begin-siege check unexpectedly failed.");
        // This represents the observable post-handoff state. The actual native transition is not stubbed or claimed tested.
        repeatedFixture.Record.State = InternalWarTestState.Preparing;
        repeatedFixture.MainParty.BesiegerCamp = new BesiegerCamp();
        repeatedFixture.Target.SiegeEvent = new SiegeEvent();
        Assert(!string.IsNullOrWhiteSpace(InternalWarTestService.GetBeginSiegeBlocker(repeatedFixture.Target)),
            "A repeated begin request remained eligible after the siege had started.");
        Assert(repeatedFixture.Player.IsMinorFaction, "Repeated begin check mutated the player minor flag.");
        _scenarios++;
    }

    private static void BeginBlocked(string name, Action<Fixture> mutate)
    {
        Fixture fixture = ResetBeginSiege();
        mutate(fixture);
        string originalRecord = fixture.Record.Serialize();
        string blocker = InternalWarTestService.GetBeginSiegeBlocker(fixture.Target);
        Assert(!string.IsNullOrWhiteSpace(blocker), "Begin siege was incorrectly allowed: " + name + ".");
        Assert(InternalWarTestService.GetBeginSiegeBlocker(fixture.Target) == blocker,
            "Repeated begin eligibility checks changed the diagnostic: " + name + ".");
        Assert(fixture.Record.Serialize() == originalRecord, "Begin eligibility mutated saved state: " + name + ".");
        Assert(fixture.Player.IsMinorFaction, "Begin eligibility mutated the player minor flag: " + name + ".");
        _scenarios++;
    }

    private static Fixture ResetSiegeEncounter()
    {
        Fixture fixture = ResetBeginSiege();
        fixture.Record.State = InternalWarTestState.Preparing;
        fixture.MainParty.CurrentSettlement = null;
        Settlement.CurrentSettlement = null;
        SiegeEvent siege = new SiegeEvent { BesiegedSettlement = fixture.Target };
        BesiegerCamp camp = new BesiegerCamp { SiegeEvent = siege, LeaderParty = fixture.MainParty };
        siege.BesiegerCamp = camp;
        fixture.Target.SiegeEvent = siege;
        fixture.MainParty.BesiegerCamp = camp;
        PlayerSiege.PlayerSiegeEvent = siege;
        fixture.EncounterAttacker = fixture.MainParty.Party;
        fixture.EncounterDefender = fixture.Target.Party;
        return fixture;
    }

    private static void VerifyPlayerSiegeEncounterAllowed()
    {
        foreach (InternalWarTestState state in new[] { InternalWarTestState.Preparing, InternalWarTestState.Assaulting })
        {
            Fixture fixture = ResetSiegeEncounter();
            fixture.Record.State = state;
            string recordBefore = fixture.Record.Serialize();
            Assert(InternalWarTestService.IsPlayerSiegeEncounter(fixture.EncounterAttacker, fixture.EncounterDefender),
                "Matching player siege encounter was rejected in state " + state + ".");
            Assert(fixture.Record.Serialize() == recordBefore && fixture.Player.IsMinorFaction,
                "Encounter identity predicate mutated state or the player minor flag.");
            _scenarios++;
        }
    }

    private static void VerifyPlayerSiegeEncounterBlocked()
    {
        EncounterBlocked("no behavior", fixture => InternalWarTestService.Detach());
        EncounterBlocked("no record", fixture => InternalWarTestService.Attach(new InternalWarTestBehavior()));
        foreach (InternalWarTestState state in (InternalWarTestState[])Enum.GetValues(typeof(InternalWarTestState)))
        {
            if (state == InternalWarTestState.Preparing || state == InternalWarTestState.Assaulting) continue;
            EncounterBlocked("state " + state, fixture => fixture.Record.State = state);
        }
        EncounterBlocked("null attacker", fixture => fixture.EncounterAttacker = null);
        EncounterBlocked("null defender", fixture => fixture.EncounterDefender = null);
        EncounterBlocked("different attacker party", fixture => fixture.EncounterAttacker = new MobileParty { ActualClan = fixture.Player }.Party);
        EncounterBlocked("different defender party", fixture => fixture.EncounterDefender = NewTarget("different", fixture.Player.Kingdom).Party);
        EncounterBlocked("mobile defender", fixture => fixture.EncounterDefender = new MobileParty { ActualClan = fixture.Target.OwnerClan }.Party);
        EncounterBlocked("same settlement through a different party object", fixture => fixture.EncounterDefender = new PartyBase { Settlement = fixture.Target });
        EncounterBlocked("attacker and defender reversed", fixture =>
        {
            fixture.EncounterAttacker = fixture.Target.Party;
            fixture.EncounterDefender = fixture.MainParty.Party;
        });
        EncounterBlocked("missing player", fixture => Clan.PlayerClan = null);
        EncounterBlocked("player identity changed", fixture => fixture.Player.StringId = "changed_player");
        EncounterBlocked("no main party", fixture => MobileParty.MainParty = null);
        EncounterBlocked("main-party identity changed", fixture => fixture.MainParty.StringId = "changed_party");
        EncounterBlocked("main-party clan changed", fixture => fixture.MainParty.ActualClan = fixture.Target.OwnerClan);
        EncounterBlocked("target identity changed", fixture => fixture.Target.StringId = "changed_target");
        EncounterBlocked("target has no owner", fixture => fixture.Target.OwnerClan = null);
        EncounterBlocked("target owner identity changed", fixture => fixture.Target.OwnerClan.StringId = "changed_owner");
        EncounterBlocked("target now player-owned", fixture => fixture.Target.OwnerClan = fixture.Player);
        EncounterBlocked("player kingdom missing", fixture => fixture.Player.Kingdom = null);
        EncounterBlocked("defender kingdom changed", fixture => fixture.Target.OwnerClan.Kingdom = new Kingdom { StringId = "foreign" });
        EncounterBlocked("both clans moved kingdoms", fixture =>
        {
            Kingdom kingdom = new Kingdom { StringId = "replacement" };
            fixture.Player.Kingdom = kingdom;
            fixture.Target.OwnerClan.Kingdom = kingdom;
        });
        EncounterBlocked("missing target siege", fixture => fixture.Target.SiegeEvent = null);
        EncounterBlocked("target siege replaced", fixture => fixture.Target.SiegeEvent = new SiegeEvent());
        EncounterBlocked("siege points at a different settlement", fixture => fixture.Target.SiegeEvent.BesiegedSettlement = NewTarget("other_siege_target", fixture.Player.Kingdom));
        EncounterBlocked("camp siege missing", fixture => fixture.MainParty.BesiegerCamp.SiegeEvent = null);
        EncounterBlocked("camp siege replaced", fixture => fixture.MainParty.BesiegerCamp.SiegeEvent = new SiegeEvent());
        EncounterBlocked("siege camp missing", fixture => fixture.Target.SiegeEvent.BesiegerCamp = null);
        EncounterBlocked("siege camp replaced", fixture => fixture.Target.SiegeEvent.BesiegerCamp = new BesiegerCamp());
        EncounterBlocked("main-party camp missing", fixture => fixture.MainParty.BesiegerCamp = null);
        EncounterBlocked("main-party camp replaced", fixture => fixture.MainParty.BesiegerCamp = new BesiegerCamp());
        EncounterBlocked("camp leader missing", fixture => fixture.Target.SiegeEvent.BesiegerCamp.LeaderParty = null);
        EncounterBlocked("camp led by another party", fixture => fixture.Target.SiegeEvent.BesiegerCamp.LeaderParty = new MobileParty { ActualClan = fixture.Player });
        EncounterBlocked("player siege missing", fixture => PlayerSiege.PlayerSiegeEvent = null);
        EncounterBlocked("player siege replaced", fixture => PlayerSiege.PlayerSiegeEvent = new SiegeEvent());
        EncounterBlocked("player still inside target", fixture => fixture.MainParty.CurrentSettlement = fixture.Target);
        EncounterBlocked("player already has map event", fixture => fixture.MainParty.Party.MapEvent = new MapEvent());
        EncounterBlocked("global player map event already exists", fixture => MapEvent.PlayerMapEvent = new MapEvent());
    }

    private static void EncounterBlocked(string name, Action<Fixture> mutate)
    {
        Fixture fixture = ResetSiegeEncounter();
        mutate(fixture);
        string recordBefore = fixture.Record.Serialize();
        Assert(!InternalWarTestService.IsPlayerSiegeEncounter(fixture.EncounterAttacker, fixture.EncounterDefender),
            "Encounter side correction was incorrectly eligible: " + name + ".");
        Assert(fixture.Record.Serialize() == recordBefore, "Encounter predicate mutated saved state: " + name + ".");
        Assert(fixture.Player.IsMinorFaction, "Encounter predicate mutated player minor flag: " + name + ".");
        _scenarios++;
    }

    private static void VerifyEncounterPostfixCorrection()
    {
        foreach (InternalWarTestState state in new[] { InternalWarTestState.Preparing, InternalWarTestState.Assaulting })
        {
            Fixture fixture = ResetSiegeEncounter();
            fixture.Record.State = state;
            string recordBefore = fixture.Record.Serialize();
            PlayerEncounter encounter = new PlayerEncounter(BattleSideEnum.Defender, BattleSideEnum.Attacker);
            PlayerEncounter.Current = encounter;
            TestPlayerEncounterSidePatch.Postfix(encounter, fixture.EncounterAttacker, fixture.EncounterDefender);
            Assert(encounter.PlayerSide == BattleSideEnum.Attacker && encounter.OpponentSide == BattleSideEnum.Defender,
                "Actual postfix did not correct the inverted player siege sides in " + state + ".");
            TestPlayerEncounterSidePatch.Postfix(encounter, fixture.EncounterAttacker, fixture.EncounterDefender);
            Assert(encounter.PlayerSide == BattleSideEnum.Attacker && encounter.OpponentSide == BattleSideEnum.Defender,
                "Repeated postfix execution inverted the corrected sides.");
            Assert(fixture.Record.Serialize() == recordBefore && fixture.Player.IsMinorFaction,
                "Actual postfix changed campaign record or player minor flag.");
            _scenarios++;
        }
    }

    private static void VerifyEncounterPostfixNoOps()
    {
        PostfixNoOp("noncurrent encounter", (fixture, encounter) => PlayerEncounter.Current =
            new PlayerEncounter(BattleSideEnum.Defender, BattleSideEnum.Attacker));
        PostfixNoOp("missing current encounter", (fixture, encounter) => PlayerEncounter.Current = null);
        PostfixNoOp("player map event", (fixture, encounter) => fixture.MainParty.Party.MapEvent = new MapEvent());
        PostfixNoOp("global player map event", (fixture, encounter) => MapEvent.PlayerMapEvent = new MapEvent());
        PostfixNoOp("wrong attacker", (fixture, encounter) => fixture.EncounterAttacker = fixture.Target.Party);
        PostfixNoOp("wrong defender", (fixture, encounter) => fixture.EncounterDefender = new MobileParty().Party);
        PostfixNoOp("pending cleanup", (fixture, encounter) => fixture.Record.State = InternalWarTestState.RoyalPeacePending);
        PostfixNoOp("terminal record", (fixture, encounter) => fixture.Record.State = InternalWarTestState.Lifted);

        foreach (BattleSideEnum playerSide in (BattleSideEnum[])Enum.GetValues(typeof(BattleSideEnum)))
        {
            foreach (BattleSideEnum opponentSide in (BattleSideEnum[])Enum.GetValues(typeof(BattleSideEnum)))
            {
                if (playerSide == BattleSideEnum.Defender && opponentSide == BattleSideEnum.Attacker) continue;
                Fixture fixture = ResetSiegeEncounter();
                PlayerEncounter encounter = new PlayerEncounter(playerSide, opponentSide);
                PlayerEncounter.Current = encounter;
                TestPlayerEncounterSidePatch.Postfix(encounter, fixture.EncounterAttacker, fixture.EncounterDefender);
                Assert(encounter.PlayerSide == playerSide && encounter.OpponentSide == opponentSide,
                    "Postfix changed an already-correct or unsupported side pair: " + playerSide + "/" + opponentSide + ".");
                _scenarios++;
            }
        }

        Fixture nullFixture = ResetSiegeEncounter();
        PlayerEncounter current = new PlayerEncounter(BattleSideEnum.Defender, BattleSideEnum.Attacker);
        PlayerEncounter.Current = current;
        TestPlayerEncounterSidePatch.Postfix(null, nullFixture.EncounterAttacker, nullFixture.EncounterDefender);
        Assert(current.PlayerSide == BattleSideEnum.Defender && current.OpponentSide == BattleSideEnum.Attacker,
            "Null-instance postfix changed the current encounter.");
        _scenarios++;
    }

    private static void PostfixNoOp(string name, Action<Fixture, PlayerEncounter> mutate)
    {
        Fixture fixture = ResetSiegeEncounter();
        PlayerEncounter encounter = new PlayerEncounter(BattleSideEnum.Defender, BattleSideEnum.Attacker);
        PlayerEncounter.Current = encounter;
        mutate(fixture, encounter);
        string recordBefore = fixture.Record.Serialize();
        TestPlayerEncounterSidePatch.Postfix(encounter, fixture.EncounterAttacker, fixture.EncounterDefender);
        Assert(encounter.PlayerSide == BattleSideEnum.Defender && encounter.OpponentSide == BattleSideEnum.Attacker,
            "Postfix changed an out-of-scope encounter: " + name + ".");
        Assert(fixture.Record.Serialize() == recordBefore && fixture.Player.IsMinorFaction,
            "Out-of-scope postfix mutated record or player minor flag: " + name + ".");
        _scenarios++;
    }

    private static void Contains(string actual, string expectedFragment, string context)
    {
        Assert(!string.IsNullOrWhiteSpace(actual) && actual.IndexOf(expectedFragment, StringComparison.OrdinalIgnoreCase) >= 0,
            context + ": expected diagnostic containing '" + expectedFragment + "', got '" + actual + "'.");
    }

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
