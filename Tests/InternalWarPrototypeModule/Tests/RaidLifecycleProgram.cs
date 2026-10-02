using System;
using AgesOfCalradiaInternalWarsTest;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

internal static class RaidLifecycleProgram
{
    private static int _assertions;
    private static int _scenarios;
    private sealed class Fixture
    {
        internal InternalWarTestBehavior Owner;
        internal InternalWarRaidCoordinator Coordinator;
        internal MobileParty Party;
        internal Settlement Target;
        internal PlayerEncounter Encounter;
    }
    private sealed class Store : IDataStore
    {
        public bool IsSaving { get; set; }
        public bool IsLoading { get; set; }
        internal string Payload;
        public void SyncData(string key, ref string value)
        {
            Assert(key == "AOC_InternalConflict_Raid_v1", "Unexpected raid save key.");
            if (IsLoading) value = Payload;
            if (IsSaving) Payload = value;
        }
    }
    private static int Main()
    {
        try
        {
            VerifySuccessfulEntry();
            VerifyDefendingPlayerRaid();
            VerifyDifferentWarVillageRejected();
            VerifyEntryCallbackInvalidation();
            VerifyPreEventFailureRollback();
            VerifyExistingEventInvalidation();
            VerifyEventIdentity();
            VerifySaveIdentityAndRetention();
            VerifyEntryIdentityChecks();
            VerifyNpcRaids();
            Console.WriteLine("Raid lifecycle checks passed: " + _scenarios + " scenarios, " + _assertions
                + " assertions. Actual raid coordinator/records; injected data boundaries, not native runtime evidence.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception.ToString()); return 1; }
    }
    private static Fixture Reset(bool forceRaid = false)
    {
        InternalWarTestBehavior.Settlements.Clear();
        InternalWarTestService.PartyReserved = false; InternalWarTestService.SettlementReserved = false;
        InternalWarTestBehavior.Parties.Clear();
        InternalWarTestDiagnostics.Messages.Clear();
        InternalWarTestDiagnostics.Errors.Clear();
        BeHostileAction.Calls = 0; BeHostileAction.OnNextApply = null;
        GameMenu.SwitchCalls = 0; GameMenu.LastMenu = null; GameMenu.OnNextSwitch = null;
        PlayerEncounter.Battle = null;
        Mission.Current = null;
        Game.Current = new Game();
        Game.Current.GameStateManager.ActiveState = new MapState();
        CampaignTime.Now = new CampaignTime { ToDays = 10 };
        Kingdom kingdom = new Kingdom { StringId = "kingdom" };
        Clan.PlayerClan = new Clan { StringId = "player", Kingdom = kingdom };
        MobileParty party = new MobileParty { StringId = "main", ActualClan = Clan.PlayerClan, IsActive = true };
        party.Party.NumberOfHealthyMembers = 40;
        MobileParty.MainParty = party;
        Settlement target = new Settlement { StringId = "village", IsVillage = true,
            OwnerClan = new Clan { StringId = "defender", Kingdom = kingdom } };
        party.CurrentSettlement = target;
        Settlement.CurrentSettlement = target;
        PlayerEncounter encounter = new PlayerEncounter { ForceRaid = forceRaid, PlayerSide = BattleSideEnum.Defender };
        PlayerEncounter.Current = encounter;
        PlayerEncounter.EncounterSettlement = target;
        InternalWarTestBehavior owner = new InternalWarTestBehavior { Conflict = new InternalConflictRecord
            { Id = "conflict", KingdomId = "kingdom", AttackerClanId = "player", DefenderClanId = "defender", StartDay = 1 } };
        InternalWarTestBehavior.Settlements.Add(target);
        InternalWarTestBehavior.Parties.Add(party);
        InternalWarCombatService.OpposedResult = true;
        return new Fixture { Owner = owner, Coordinator = new InternalWarRaidCoordinator(owner), Party = party, Target = target, Encounter = encounter };
    }
    private static void Start(Fixture fixture)
    {
        string result;
        Assert(fixture.Coordinator.TryBeginRaid(fixture.Target, out result), "Valid injected entry failed: " + result);
    }
    private static MobileParty Npc(Fixture fixture)
    {
        var party = new MobileParty { StringId = "npc", ActualClan = Clan.PlayerClan, IsActive = true };
        party.Party.NumberOfHealthyMembers = 80;
        InternalWarTestBehavior.Parties.Add(party);
        return party;
    }
    private static void VerifyNpcRaids()
    {
        Fixture f = Reset(); MobileParty npc = Npc(f);
        Assert(f.Coordinator.TryBeginNpcRaid(npc, f.Target), "NPC order rejected.");
        Assert(f.Coordinator.CanStartNpcRaid(npc, f.Target), "Recorded NPC arrival rejected.");
        Assert(!f.Coordinator.IsRaidHostility(npc.Party, f.Target.Party), "NPC acquired player crime exception.");
        Assert(!f.Coordinator.CanStartNpcRaid(f.Party, f.Target), "Player substituted for NPC order.");
        var foreign = Npc(f); foreign.StringId = "foreign";
        Assert(!f.Coordinator.CanStartNpcRaid(foreign, f.Target), "Foreign party acquired raid authority.");
        Assert(!f.Coordinator.CanStartNpcRaid(npc, new Settlement { StringId = "other", IsVillage = true, OwnerClan = f.Target.OwnerClan }),
            "Foreign village acquired raid authority.");
        var save = new Store { IsSaving = true }; f.Coordinator.SyncRaid(save);
        f.Coordinator = new InternalWarRaidCoordinator(f.Owner);
        f.Coordinator.SyncRaid(new Store { IsLoading = true, Payload = save.Payload });
        Assert(f.Owner.RecoveryBlocker.Length == 0 && f.Coordinator.CanStartNpcRaid(npc, f.Target), "NPC identity did not survive reload.");
        f.Owner.Conflict.RequestPeace(); f.Coordinator.AdvanceRaid();
        Assert(f.Coordinator.Raid.Closed && npc.HoldCalls == 1, "Peace did not stop the exact NPC order.");
        f = Reset(); npc = Npc(f);
        Assert(f.Coordinator.TryBeginNpcRaid(npc, f.Target), "Reentrant cleanup fixture did not start.");
        f.Owner.Conflict.RequestPeace();
        npc.OnNextHold = () => f.Coordinator.AdvanceRaid();
        f.Coordinator.AdvanceRaid();
        Assert(f.Coordinator.Raid.Closed && npc.HoldCalls == 1, "Reentrant native hold repeated raid cleanup.");
        _scenarios++;
        f = Reset(); npc = Npc(f); f.Coordinator.TryBeginNpcRaid(npc, f.Target);
        npc.TargetSettlement = new Settlement { StringId = "new_order" };
        f.Owner.Conflict.RequestPeace(); f.Coordinator.AdvanceRaid();
        Assert(f.Coordinator.Raid.Closed && npc.HoldCalls == 0, "Cleanup overwrote a changed order.");
        _scenarios++;
        f = Reset(); npc = Npc(f); f.Coordinator.TryBeginNpcRaid(npc, f.Target);
        var battle = new MapEvent { IsRaid = true, MapEventSettlement = f.Target,
            AttackerSide = new MapEventSide { LeaderParty = npc.Party } };
        npc.Party.MapEvent = battle; f.Target.Party.MapEvent = battle;
        Assert(f.Coordinator.IsRaidBattle(battle), "Owned NPC event unrecognized.");
        battle.AttackerSide.LeaderParty = f.Party.Party;
        Assert(!f.Coordinator.IsRaidBattle(battle), "Foreign event leader acquired ownership.");
        battle.AttackerSide.LeaderParty = npc.Party;
        f.Owner.Conflict.RequestPeace(); f.Coordinator.AdvanceRaid();
        Assert(battle.DiplomaticallyFinished && !f.Coordinator.Raid.Closed, "NPC battle did not defer closure to native teardown.");
        npc.Party.MapEvent = null; f.Target.Party.MapEvent = null; f.Coordinator.AdvanceRaid();
        Assert(f.Coordinator.Raid.Closed, "NPC event teardown did not release reservation.");
        _scenarios++;
        f = Reset(); npc = Npc(f); npc.OnRaidOrder = () => { throw new InvalidOperationException("Injected native order failure"); };
        Assert(!f.Coordinator.TryBeginNpcRaid(npc, f.Target) && f.Coordinator.Raid.StopRequested, "Failed native order was not queued for cleanup.");
        f.Coordinator.AdvanceRaid();
        Assert(f.Coordinator.Raid.Closed && npc.HoldCalls == 1, "Failed native order was not released.");
        _scenarios++;
        f = Reset(); npc = Npc(f); Fixture callbackFixture = f;
        npc.OnRaidOrder = () => callbackFixture.Owner.Conflict.RequestPeace();
        Assert(!f.Coordinator.TryBeginNpcRaid(npc, f.Target) && f.Coordinator.Raid.StopRequested,
            "Peace during native order callback did not invalidate raid authority.");
        f.Coordinator.AdvanceRaid();
        Assert(f.Coordinator.Raid.Closed, "Reentrant peace failed to release NPC raid.");
        _scenarios++;
        f = Reset(); npc = Npc(f); InternalWarTestService.PartyReserved = true;
        Assert(!f.Coordinator.TryBeginNpcRaid(npc, f.Target), "Reserved party accepted a new raid.");
        InternalWarTestService.PartyReserved = false; InternalWarTestService.SettlementReserved = true;
        Assert(!f.Coordinator.TryBeginNpcRaid(npc, f.Target), "Reserved village accepted a new raid.");
        _scenarios++;
    }
    private static MapEvent AttachBattle(Fixture fixture)
    {
        MapEvent battle = new MapEvent { IsRaid = true, MapEventSettlement = fixture.Target,
            AttackerSide = new MapEventSide { LeaderParty = fixture.Party.Party } };
        fixture.Party.Party.MapEvent = battle;
        fixture.Target.Party.MapEvent = battle;
        return battle;
    }
    private static void VerifySuccessfulEntry()
    {
        Fixture fixture = Reset();
        Start(fixture);
        Assert(fixture.Encounter.ForceRaid && BeHostileAction.Calls == 1 && GameMenu.SwitchCalls == 1
            && GameMenu.LastMenu == "encounter", "Valid entry did not request its owned native flow exactly once.");
        Assert(fixture.Coordinator.HasActiveRaid && !fixture.Coordinator.Raid.EventObserved,
            "Menu switching alone was incorrectly treated as evidence of an event.");
        Assert(fixture.Coordinator.IsRaidHostility(fixture.Party.Party, fixture.Target.Party), "Valid registered hostility rejected.");
        string result;
        Assert(!fixture.Coordinator.TryBeginRaid(fixture.Target, out result) && GameMenu.SwitchCalls == 1,
            "Repeated entry was allowed.");
        _scenarios++;
    }
    private static void VerifyEntryCallbackInvalidation()
    {
        foreach (bool duringSetup in new[] { true, false })
        {
            Fixture fixture = Reset();
            Action peace = () => fixture.Owner.Conflict.RequestPeace();
            if (duringSetup) fixture.Encounter.OnNextSetup = peace; else BeHostileAction.OnNextApply = peace;
            string result;
            Assert(!fixture.Coordinator.TryBeginRaid(fixture.Target, out result), "Callback peace did not reject entry.");
            Assert(GameMenu.SwitchCalls == 0 && !fixture.Encounter.ForceRaid && fixture.Coordinator.Raid.Closed,
                "Callback peace reached the menu or failed owned rollback.");
            Assert(BeHostileAction.Calls == (duringSetup ? 0 : 1), "Entry continued past callback invalidation.");
            _scenarios++;

            fixture = Reset();
            PlayerEncounter replacement = new PlayerEncounter { PlayerSide = BattleSideEnum.Defender };
            Action replace = () => PlayerEncounter.Current = replacement;
            if (duringSetup) fixture.Encounter.OnNextSetup = replace; else BeHostileAction.OnNextApply = replace;
            Assert(!fixture.Coordinator.TryBeginRaid(fixture.Target, out result), "Replaced encounter did not reject entry.");
            Assert(PlayerEncounter.Current == replacement && !replacement.ForceRaid && replacement.SetupCalls == 0
                && GameMenu.SwitchCalls == 0, "Entry or rollback mutated a replacement encounter.");
            Assert(fixture.Coordinator.Raid.StopRequested && !fixture.Coordinator.Raid.Closed,
                "Unowned encounter recovery was falsely declared complete.");
            _scenarios++;
        }
    }
    private static void VerifyDefendingPlayerRaid()
    {
        Fixture fixture = Reset();
        fixture.Owner.Conflict.AttackerClanId = "defender";
        fixture.Owner.Conflict.DefenderClanId = "player";
        Start(fixture);
        Assert(fixture.Coordinator.Raid.DefenderClanId == "defender"
            && fixture.Coordinator.Raid.ConflictId == fixture.Owner.Conflict.Id,
            "Counter-raid did not retain the actual village owner and selected war.");
        Assert(fixture.Encounter.SetupCalls == 1 && BeHostileAction.Calls == 1 && GameMenu.SwitchCalls == 1,
            "Defending player's counter-raid did not enter the native flow exactly once.");
        Assert(fixture.Coordinator.IsRaidHostility(fixture.Party.Party, fixture.Target.Party),
            "Defending player's counter-raid lost its scoped hostility.");
        Store save = new Store { IsSaving = true };
        fixture.Coordinator.SyncRaid(save);
        string payload = save.Payload;
        fixture = Reset();
        fixture.Owner.Conflict.AttackerClanId = "defender";
        fixture.Owner.Conflict.DefenderClanId = "player";
        fixture.Coordinator.SyncRaid(new Store { IsLoading = true, Payload = payload });
        Assert(fixture.Coordinator.HasActiveRaid && string.IsNullOrEmpty(fixture.Owner.RecoveryBlocker),
            "Defending player's valid counter-raid was rejected on reload.");
        Assert(fixture.Coordinator.IsRaidHostility(fixture.Party.Party, fixture.Target.Party),
            "Defending player's counter-raid authority did not survive reload.");
        save = new Store { IsSaving = true };
        fixture.Coordinator.SyncRaid(save);
        Assert(save.Payload == payload, "Counter-raid changed identity during save/load.");
        _scenarios++;
    }
    private static void VerifyDifferentWarVillageRejected()
    {
        Fixture fixture = Reset();
        fixture.Target.OwnerClan = new Clan { StringId = "other_rival", Kingdom = Clan.PlayerClan.Kingdom };
        InternalWarCombatService.OpposedResult = true;
        string result;
        Assert(!fixture.Coordinator.TryBeginRaid(fixture.Target, out result),
            "Registry-wide hostility authorized a village outside the selected war.");
        Assert(fixture.Encounter.SetupCalls == 0 && BeHostileAction.Calls == 0 && GameMenu.SwitchCalls == 0,
            "Unselected war entered a native callback before rejection.");
        Assert(fixture.Coordinator.Raid == null && !fixture.Encounter.ForceRaid,
            "Unselected war mutated raid state before rejection.");
        _scenarios++;
    }
    private static void VerifyPreEventFailureRollback()
    {
        foreach (bool originalForceRaid in new[] { false, true })
        foreach (string boundary in new[] { "setup", "hostility", "menu" })
        {
            Fixture fixture = Reset(originalForceRaid);
            Action fail = () => { throw new InvalidOperationException("injected " + boundary); };
            if (boundary == "setup") fixture.Encounter.OnNextSetup = fail;
            else if (boundary == "hostility") BeHostileAction.OnNextApply = fail;
            else GameMenu.OnNextSwitch = fail;
            string result;
            Assert(!fixture.Coordinator.TryBeginRaid(fixture.Target, out result), boundary + ": failure returned success.");
            Assert(fixture.Encounter.ForceRaid == originalForceRaid && PlayerEncounter.Current == fixture.Encounter,
                boundary + ": rollback did not restore only the owned ForceRaid value.");
            Assert(fixture.Coordinator.Raid.Closed && fixture.Coordinator.Raid.StopRequested && !fixture.Coordinator.HasActiveRaid,
                boundary + ": pre-event rollback did not finish the raid record.");
            Assert(InternalWarTestDiagnostics.Errors.Count == 1 && string.IsNullOrEmpty(fixture.Owner.RecoveryBlocker),
                boundary + ": ordinary injected entry failure was not diagnosed/recovered as expected.");
            _scenarios++;
        }
    }
    private static void VerifyExistingEventInvalidation()
    {
        Action<Fixture>[] invalidations = {
            fixture => fixture.Owner.Conflict.RequestPeace(),
            fixture => fixture.Owner.Conflict.Phase = InternalConflictPhase.Closed,
            fixture => fixture.Owner.Conflict = null,
            fixture => fixture.Owner.RecoveryBlocker = "injected invalid persistence",
            fixture => fixture.Target.OwnerClan.Kingdom = new Kingdom { StringId = "foreign" },
            fixture => fixture.Target.OwnerClan = new Clan { StringId = "other_owner", Kingdom = Clan.PlayerClan.Kingdom },
            fixture => fixture.Coordinator.Raid.StopRequested = true
        };
        foreach (Action<Fixture> invalidate in invalidations)
        {
            Fixture fixture = Reset();
            Start(fixture);
            MapEvent battle = AttachBattle(fixture);
            invalidate(fixture);
            Assert(!fixture.Coordinator.IsRaidHostility(fixture.Party.Party, fixture.Target.Party), "Invalidated authority allowed new hostility.");
            Assert(fixture.Coordinator.IsRaidBattle(battle), "Political invalidation lost the actual in-flight event.");
            Mission.Current = new Mission();
            fixture.Coordinator.AdvanceRaid();
            Assert(fixture.Coordinator.Raid.EventObserved && fixture.Coordinator.Raid.StopRequested && !battle.DiplomaticallyFinished,
                "Mission-time advance failed to queue stop safely.");
            Mission.Current = null;
            Game.Current.GameStateManager.ActiveState = new object();
            fixture.Coordinator.AdvanceRaid();
            Assert(!battle.DiplomaticallyFinished, "Non-map state finalized an event.");
            Game.Current.GameStateManager.ActiveState = new MapState();
            fixture.Coordinator.AdvanceRaid();
            Assert(battle.DiplomaticallyFinished && !fixture.Coordinator.Raid.Closed, "Owned map event stop lost pending teardown.");
            fixture.Party.Party.MapEvent = null; fixture.Target.Party.MapEvent = null;
            PlayerEncounter.Current = null;
            fixture.Coordinator.AdvanceRaid();
            Assert(fixture.Coordinator.Raid.Closed, "Native event teardown did not allow final closure.");
            _scenarios++;
        }
    }
    private static void VerifyEventIdentity()
    {
        Fixture fixture = Reset();
        Start(fixture);
        MapEvent battle = AttachBattle(fixture);
        Assert(fixture.Coordinator.IsRaidBattle(battle), "Owned event fixture not recognized.");
        battle.AttackerSide.LeaderParty = new MobileParty { StringId = fixture.Party.StringId }.Party;
        Assert(!fixture.Coordinator.IsRaidBattle(battle), "Same-ID replacement party claimed the owned event.");
        battle.AttackerSide.LeaderParty = fixture.Party.Party;
        fixture.Party.Party.MapEvent = new MapEvent();
        Assert(!fixture.Coordinator.IsRaidBattle(battle), "Unrelated event reference was accepted.");
        fixture.Party.Party.MapEvent = battle;
        battle.MapEventSettlement = new Settlement { StringId = "other_village", IsVillage = true };
        Assert(!fixture.Coordinator.IsRaidBattle(battle), "Unrelated village event was accepted.");
        battle.MapEventSettlement = fixture.Target;
        fixture.Target.IsVillage = false;
        Assert(!fixture.Coordinator.IsRaidBattle(battle), "Non-village event was accepted.");
        fixture.Target.IsVillage = true;
        battle.IsRaid = false;
        Assert(!fixture.Coordinator.IsRaidBattle(battle), "Non-raid event was accepted.");
        _scenarios += 6;
    }
    private static void VerifySaveIdentityAndRetention()
    {
        Fixture fixture = Reset();
        Start(fixture);
        Store save = new Store { IsSaving = true };
        fixture.Coordinator.SyncRaid(save);
        string valid = save.Payload;
        fixture = Reset();
        fixture.Coordinator.SyncRaid(new Store { IsLoading = true, Payload = valid });
        Assert(fixture.Coordinator.HasActiveRaid && string.IsNullOrEmpty(fixture.Owner.RecoveryBlocker), "Valid active raid did not reload.");
        _scenarios++;
        foreach (string payload in new[] { "invalid", valid.Replace("|conflict|", "|wrong_conflict|"),
            valid.Replace("|defender|", "|wrong_defender|"), valid.Replace("|10|0|", "|-1|0|") })
        {
            fixture = Reset();
            fixture.Coordinator.SyncRaid(new Store { IsLoading = true, Payload = payload });
            Assert(!string.IsNullOrEmpty(fixture.Owner.RecoveryBlocker), "Invalid saved raid did not block entry.");
            Assert(!fixture.Coordinator.IsRaidHostility(fixture.Party.Party, fixture.Target.Party), "Blocked saved raid retained new-hostility authority.");
            save = new Store { IsSaving = true };
            fixture.Coordinator.SyncRaid(save);
            Assert(save.Payload == payload, "Invalid original payload was overwritten on save.");
            _scenarios++;
        }
    }
    private static void VerifyEntryIdentityChecks()
    {
        Action<Fixture>[] changes = {
            fixture => fixture.Party.ActualClan = new Clan { StringId = "other", Kingdom = Clan.PlayerClan.Kingdom },
            fixture => fixture.Owner.Conflict.AttackerClanId = "other_attacker",
            fixture => fixture.Owner.Conflict.DefenderClanId = "other_defender",
            fixture => fixture.Target.OwnerClan.Kingdom = new Kingdom { StringId = "foreign" }
        };
        foreach (Action<Fixture> change in changes)
        {
            Fixture fixture = Reset();
            change(fixture);
            string result;
            Assert(!fixture.Coordinator.TryBeginRaid(fixture.Target, out result), "Changed entry identity was accepted.");
            Assert(BeHostileAction.Calls == 0 && GameMenu.SwitchCalls == 0 && !fixture.Encounter.ForceRaid,
                "Changed identity reached a hostile action or menu switch.");
            _scenarios++;
        }
    }
    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
