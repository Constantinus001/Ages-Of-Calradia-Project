using System;
using System.Collections.Generic;
using System.Reflection;
using AgesOfCalradiaSuccession;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

internal static partial class SuccessionEngineVerifier
{
    private static int _checks;
    private static void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); _checks++; }
    private static Hero Add(Clan clan, string id, int age, bool female = false, Hero father = null)
    {
        var hero = new Hero { StringId = id, IsAlive = true, Age = age, IsFemale = female, Clan = clan, Father = father };
        clan.Heroes.Add(hero); Hero.AllAliveHeroes.Add(hero);
        if (father != null) father.Children.Add(hero);
        return hero;
    }
    private static Clan House(Kingdom realm, string id)
    {
        var clan = new Clan { StringId = id, Kingdom = realm };
        realm.Clans.Add(clan); Clan.All.Add(clan); return clan;
    }
    private static void Reset()
    {
        Kingdom.All.Clear(); Clan.All.Clear(); Hero.AllAliveHeroes.Clear(); Hero.DeadOrDisabledHeroes.Clear(); CampaignEvents.Reset();
        ChangeClanLeaderAction.AfterApply = null; ChangeClanLeaderAction.ApplyCount = 0;
        CampaignTime.Day = 100d;
        FactionManager.Wars.Clear(); ChangeKingdomAction.AfterApply = null; ChangeKingdomAction.ApplyCount = 0; Hero.MainHero = null;
    }
    private static SuccessionCampaignBehavior Start(Kingdom realm, Clan house, Hero king)
    {
        realm.RulingClan = house; realm.Leader = king; house.Leader = king;
        Kingdom.All.Add(realm);
        var behavior = new SuccessionCampaignBehavior();
        Campaign.Current = new Campaign { Behavior = behavior };
        behavior.RegisterEvents(); CampaignEvents.OnSessionLaunchedEvent.Raise(new CampaignGameStarter());
        return behavior;
    }
    private static void Die(Hero hero)
    {
        hero.IsAlive = false;
        Hero.AllAliveHeroes.Remove(hero); Hero.DeadOrDisabledHeroes.Add(hero);
        CampaignEvents.HeroKilledEvent.Raise(hero); CampaignEvents.TickEvent.Raise(0.1f);
    }
    private static void SetMap(object behavior, string field, string key, string value)
    {
        ((Dictionary<string,string>)behavior.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(behavior))[key] = value;
    }
    private static int Main()
    {
        try
        {
            Reset();
            var realm = new Kingdom { StringId = "realm" };
            Clan house = House(realm, "house");
            Hero king = Add(house, "king", 60);
            Hero child = Add(house, "child", 10, father: king);
            Clan adults = House(realm, "adults"); adults.Leader = Add(adults, "regent", 40);
            var behavior = Start(realm, house, king);
            Die(king);
            Check(behavior.GetMinorHeir(realm) == child && behavior.GetRegent(realm) == adults.Leader, "real behavior opens child regency");
            behavior.HoldCoronation(realm, adults.Leader, false);
            Check(!behavior.IsCoronated(realm), "regent cannot bypass menu and crown himself through behavior API");
            child.Age = 18;
            CampaignEvents.HeroComesOfAgeEvent.Raise(child);
            CampaignEvents.TickEvent.Raise(0.1f);
            Check(realm.Leader == child && behavior.GetMinorHeir(realm) == null && behavior.GetRegent(realm) == null, "real behavior transfers authority at adulthood");

            Reset(); realm = new Kingdom { StringId = "emergency" };
            house = House(realm, "empty-house"); king = Add(house, "king", 60);
            Clan minorHouse = House(realm, "minor-house");
            Hero emergencyChild = Add(minorHouse, "emergency-child", 12); minorHouse.Leader = emergencyChild;
            behavior = Start(realm, house, king);
            Die(king);
            Check(behavior.GetMinorHeir(realm) == emergencyChild && realm.Leader != emergencyChild,
                "emergency child leader receives regency rather than direct adult accession");

            SetMap(behavior, "_lawByKingdom", realm.StringId, "999");
            Check(behavior.GetLaw(realm) == SuccessionLaw.AbsolutePrimogeniture, "undefined saved law uses culture default");
            Reset(); realm = new Kingdom { StringId = "maturity" }; house = House(realm, "house");
            king = Add(house, "king", 60); child = Add(house, "child", 10, father: king);
            Add(house, "senior-uncle", 70);
            adults = House(realm, "adults"); adults.Leader = Add(adults, "regent", 40);
            behavior = Start(realm, house, king); Die(king);
            Hero regent = adults.Leader;
            regent.IsAlive = false; Hero.AllAliveHeroes.Remove(regent); Hero.DeadOrDisabledHeroes.Add(regent);
            CampaignEvents.HeroKilledEvent.Raise(regent);
            SetMap(behavior, "_lawByKingdom", realm.StringId, "HouseSeniority");
            child.Age = 18; CampaignEvents.HeroComesOfAgeEvent.Raise(child);
            CampaignEvents.TickEvent.Raise(0.1f);
            Check(realm.Leader == child && behavior.GetRegent(realm) == null,
                "maturity during queued regent death honors recorded heir despite changed law");

            Reset(); realm = new Kingdom { StringId = "dead-minor" }; house = House(realm, "house");
            king = Add(house, "king", 60); child = Add(house, "child", 10, father: king);
            adults = House(realm, "adults"); adults.Leader = Add(adults, "regent", 40);
            behavior = Start(realm, house, king); Die(king);
            Clan successorHouse = House(realm, "successor-house");
            Hero successor = Add(successorHouse, "successor", 30); successorHouse.Leader = successor;
            Die(child); Die(adults.Leader);
            Check(realm.Leader == successor && behavior.GetMinorHeir(realm) == null,
                "adult fallback after child and regent deaths clears stale minor state");
            behavior.HoldCoronation(realm, successor, false);
            Check(behavior.IsCoronated(realm), "adult successor can coronate after failed regency");
            SetMap(behavior, "_recognitionByRealmClan", realm.StringId + ":" + successorHouse.StringId, "999");
            Check(behavior.GetRecognition(realm, successorHouse) == ClanRecognition.Neutral, "undefined saved recognition uses neutral state");
            SetMap(behavior, "_legitimacyByKingdom", realm.StringId, "NaN");
            Check(behavior.GetLegitimacy(realm) == 50f, "NaN legitimacy uses neutral default");
            SetMap(behavior, "_legitimacyByKingdom", realm.StringId, "Infinity");
            Check(behavior.GetLegitimacy(realm) == 50f, "infinite legitimacy uses neutral default");
            SetMap(behavior, "_legitimacyByKingdom", realm.StringId, "200");
            Check(behavior.GetLegitimacy(realm) == 100f, "saved legitimacy bounded above");
            SetMap(behavior, "_legitimacyByKingdom", realm.StringId, "-10");
            Check(behavior.GetLegitimacy(realm) == 0f, "saved legitimacy bounded below");
            RegencyTransitions();
            VacantRegencyAndRecovery();
            ServiceLifetime();
            StalePretenders();
            RepeatedRegencies();
            NativeLeadershipBeforeDeath(false);
            NativeLeadershipBeforeDeath(true);
            PoliticsDuringTransfer(false);
            PoliticsDuringTransfer(true);
            CrisisLifecycle();
            AbdicationAndVacancy();
            Console.WriteLine("Succession engine verifier passed: " + _checks + " assertions using production campaign behavior and native boundary doubles.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
    private sealed class Store : IDataStore
    {
        private readonly Dictionary<string, string> _values;
        public bool IsSaving { get; private set; }
        public bool IsLoading { get { return !IsSaving; } }
        internal Store(Dictionary<string, string> values, bool saving) { _values = values; IsSaving = saving; }
        public void SyncData(string key, ref string value)
        {
            if (IsSaving) _values[key] = value;
            else if (!_values.TryGetValue(key, out value)) value = string.Empty;
        }
    }
    private static void RegencyTransitions()
    {
        Reset();
        var realm = new Kingdom { StringId = "child-replacement" };
        Clan house = House(realm, "house"); Hero king = Add(house, "king", 60);
        Hero first = Add(house, "first", 12, father: king);
        Hero second = Add(house, "second", 10, father: king);
        Clan adults = House(realm, "adults"); adults.Leader = Add(adults, "regent", 40);
        var behavior = Start(realm, house, king); Die(king); Die(first);
        Check(behavior.GetMinorHeir(realm) == second && behavior.GetRegent(realm) == adults.Leader,
            "child death immediately selects next heir while retaining a living regent");
        var saved = new Dictionary<string, string>(); behavior.SyncData(new Store(saved, true));
        CampaignEvents.Reset(); var loaded = new SuccessionCampaignBehavior();
        loaded.SyncData(new Store(saved, false)); loaded.RegisterEvents();
        CampaignEvents.OnSessionLaunchedEvent.Raise(new CampaignGameStarter()); CampaignEvents.TickEvent.Raise(0.1f);
        Check(loaded.GetMinorHeir(realm) == second && loaded.GetRegent(realm) == adults.Leader,
            "replacement heir and regent round trip through production SyncData");
        second.Age = 18;
        ChangeClanLeaderAction.AfterApply = delegate { throw new InvalidOperationException("injected partial clan-leader transfer"); };
        CampaignEvents.HeroComesOfAgeEvent.Raise(second);
        Check(ChangeClanLeaderAction.ApplyCount == 0, "maturity listener defers native changes");
        CampaignEvents.TickEvent.Raise(0.1f);
        CampaignEvents.DailyTickEvent.Raise(); CampaignEvents.TickEvent.Raise(0.1f);
        Check(ChangeClanLeaderAction.ApplyCount == 1, "failed maturity transfer is quarantined across daily audits");
        saved.Clear(); loaded.SyncData(new Store(saved, true));
        CampaignEvents.Reset(); loaded = new SuccessionCampaignBehavior();
        loaded.SyncData(new Store(saved, false)); loaded.RegisterEvents();
        CampaignEvents.OnSessionLaunchedEvent.Raise(new CampaignGameStarter());
        CampaignEvents.DailyTickEvent.Raise(); CampaignEvents.TickEvent.Raise(0.1f);
        Check(ChangeClanLeaderAction.ApplyCount == 1 && loaded.GetMinorHeir(realm) == second,
            "failed maturity transfer stays quarantined after production save reload");
    }
    private static void VacantRegencyAndRecovery()
    {
        Reset();
        var realm = new Kingdom { StringId = "vacant-regency" };
        Clan house = House(realm, "house"); Hero king = Add(house, "king", 60);
        Hero first = Add(house, "first", 12, father: king);
        Hero second = Add(house, "second", 10, father: king);
        Clan adults = House(realm, "adults"); adults.Leader = Add(adults, "regent", 40);
        var behavior = Start(realm, house, king); Die(king); Die(adults.Leader);
        Check(behavior.GetRegent(realm) == null && behavior.GetMinorHeir(realm) == first,
            "vacant regency clears deceased regent identity without losing child");
        adults.Leader = Add(adults, "new-regent", 40);
        // Native Kingdom.Leader reads RulingClan.Leader; mirror that getter in this field-based double.
        if (realm.RulingClan == adults) realm.Leader = adults.Leader;
        CampaignTime.Day++;
        CampaignEvents.DailyTickEvent.Raise();
        Check(behavior.GetRegent(realm) == null, "daily vacancy audit queues instead of immediately transferring authority");
        CampaignEvents.TickEvent.Raise(0.1f);
        Check(behavior.GetRegent(realm) == adults.Leader, "vacant regency appoints newly available adult on later audit");
        // Simulate a save from another mod after the native death state changed,
        // but before our listener was delivered.
        first.IsAlive = false; Hero.AllAliveHeroes.Remove(first); Hero.DeadOrDisabledHeroes.Add(first);
        var saved = new Dictionary<string,string>(); behavior.SyncData(new Store(saved, true));
        CampaignEvents.Reset(); var loaded = new SuccessionCampaignBehavior();
        loaded.SyncData(new Store(saved, false)); loaded.RegisterEvents();
        CampaignEvents.OnSessionLaunchedEvent.Raise(new CampaignGameStarter()); CampaignEvents.TickEvent.Raise(0.1f);
        Check(loaded.GetMinorHeir(realm) == second, "startup recovery replaces saved dead child without waiting for daily tick");
        second.Age = 18; CampaignEvents.HeroComesOfAgeEvent.Raise(second);
        saved.Clear(); loaded.SyncData(new Store(saved, true));
        CampaignEvents.Reset(); loaded = new SuccessionCampaignBehavior();
        loaded.SyncData(new Store(saved, false)); loaded.RegisterEvents();
        CampaignEvents.OnSessionLaunchedEvent.Raise(new CampaignGameStarter()); CampaignEvents.TickEvent.Raise(0.1f);
        Check(realm.Leader == second && loaded.GetMinorHeir(realm) == null,
            "save before deferred maturity resumes one adult accession through production SyncData");
        int count = ChangeClanLeaderAction.ApplyCount;
        CampaignEvents.DailyTickEvent.Raise(); CampaignEvents.TickEvent.Raise(0.1f);
        Check(ChangeClanLeaderAction.ApplyCount == count, "post-reload daily tick cannot repeat completed maturity");
    }
    private static void ServiceLifetime()
    {
        Reset();
        var realm = new Kingdom { StringId = "same-id" };
        Clan house = House(realm, "house"); Hero king = Add(house, "king", 60);
        var behavior = Start(realm, house, king);
        SetMap(behavior, "_lawByKingdom", realm.StringId, "HouseSeniority");
        Check(SuccessionService.GetLaw(realm) == SuccessionLaw.HouseSeniority, "public service reads current campaign law");
        Campaign.Current = null;
        Check(SuccessionService.GetLaw(realm) == SuccessionLaw.AbsolutePrimogeniture && SuccessionService.GetClaimants(realm).Count == 0,
            "public service cannot expose prior campaign state after exit");
        Campaign.Current = new Campaign();
        Check(SuccessionService.GetLaw(realm) == SuccessionLaw.AbsolutePrimogeniture, "campaign without succession behavior has neutral service defaults");
        Campaign.Current = new Campaign { Behavior = new SuccessionCampaignBehavior() };
        Check(SuccessionService.GetLaw(new Kingdom { StringId = realm.StringId }) == SuccessionLaw.AbsolutePrimogeniture,
            "new campaign with reused kingdom id cannot inherit previous campaign law");
    }
    private static void StalePretenders()
    {
        Reset();
        var realm = new Kingdom { StringId = "pretenders" };
        Clan house = House(realm, "house"); Hero king = Add(house, "king", 60);
        Clan rivals = House(realm, "rivals"); Hero oldLeader = Add(rivals, "old-leader", 50); rivals.Leader = oldLeader;
        var behavior = Start(realm, house, king);
        SetMap(behavior, "_pretenderByKingdom", realm.StringId, oldLeader.StringId);
        Hero newLeader = Add(rivals, "new-leader", 40); rivals.Leader = newLeader;
        Check(behavior.GetPretender(realm) == null, "cached pretender who lost clan leadership is invalidated");
        Check(behavior.GetCivilWarPretender(realm) == newLeader, "civil-war fallback uses the current eligible clan leader");
        SetMap(behavior, "_pretenderByKingdom", realm.StringId, newLeader.StringId);
        newLeader.Age = 17;
        Check(behavior.GetPretender(realm) == null, "underage clan leader cannot become adult pretender");
        newLeader.Age = 40; newLeader.IsActive = false;
        Check(behavior.GetPretender(realm) == null, "inactive clan leader cannot remain a cached pretender");
        newLeader.IsActive = true;
        Clan inconsistent = House(realm, "inconsistent"); inconsistent.Leader = newLeader;
        SetMap(behavior, "_recognitionByRealmClan", realm.StringId + ":" + inconsistent.StringId, "SupportsPretender");
        Check(!behavior.GetCivilWarSupporters(realm, null).Contains(inconsistent), "clan cannot borrow another clans leader to qualify as supporter");
        rivals.IsEliminated = true;
        SetMap(behavior, "_recognitionByRealmClan", realm.StringId + ":" + rivals.StringId, "SupportsPretender");
        Check(behavior.GetCivilWarSupporters(realm, oldLeader).Count == 0,
            "eliminated clan cannot remain a civil-war supporter or be reinserted by a stale pretender");
        Check(behavior.GetCivilWarPretender(null) == null, "null realm has no civil-war pretender");
    }
    private static void RepeatedRegencies()
    {
        Reset(); var realm = new Kingdom { StringId = "regent-chain" };
        Clan house = House(realm, "house"); Hero king = Add(house, "king", 60);
        Hero child = Add(house, "child", 10, father: king);
        for (int i = 0; i < 3; i++)
        {
            Clan adultHouse = House(realm, "regent-house-" + i);
            adultHouse.Leader = Add(adultHouse, "regent-" + i, 40 + i);
        }
        var behavior = Start(realm, house, king); Die(king);
        for (int i = 0; i < 3; i++)
        {
            Hero regent = behavior.GetRegent(realm);
            Check(regent != null && regent.IsAlive, "next regent exists in replacement chain " + i);
            Die(regent);
            Check(behavior.GetMinorHeir(realm) == child, "replacement regent never displaces recorded child " + i);
        }
        child.Age = 18; CampaignEvents.HeroComesOfAgeEvent.Raise(child); CampaignEvents.TickEvent.Raise(0.1f);
        Check(realm.Leader == child && realm.RulingClan == house && behavior.GetRegent(realm) == null,
            "three regent deaths cannot displace dynasty at adulthood");
    }
    private static void PoliticsDuringTransfer(bool fail)
    {
        Reset(); var realm = new Kingdom { StringId = "politics-transfer" };
        Clan house = House(realm, "house"); Hero king = Add(house, "king", 65);
        Hero heir = Add(house, "heir", 35, father: king);
        var behavior = Start(realm, house, king);
        SetMap(behavior, "_coronatedByKingdom", realm.StringId, "false");
        SetMap(behavior, "_legitimacyByKingdom", realm.StringId, "37");
        king.IsAlive = false; Hero.AllAliveHeroes.Remove(king); Hero.DeadOrDisabledHeroes.Add(king);
        CampaignEvents.HeroKilledEvent.Raise(king);
        if (fail)
        {
            ChangeClanLeaderAction.AfterApply = delegate { throw new InvalidOperationException("partial transfer before politics"); };
            CampaignEvents.TickEvent.Raise(0.1f);
        }
        else { house.Leader = heir; realm.Leader = heir; }
        behavior.HoldCoronation(realm, heir, false);
        Check(!behavior.IsCoronated(realm), "coronation blocked during unresolved transfer " + fail);
        CampaignTime.Day += 10;
        CampaignEvents.DailyTickEvent.Raise();
        Check(!behavior.IsCoronated(realm) && behavior.GetLegitimacy(realm) == 37f,
            "daily politics preserves unresolved transfer state " + fail);
        var saved = new Dictionary<string, string>(); behavior.SyncData(new Store(saved, true));
        CampaignEvents.Reset(); var loaded = new SuccessionCampaignBehavior();
        loaded.SyncData(new Store(saved, false)); Campaign.Current = new Campaign { Behavior = loaded }; loaded.RegisterEvents();
        CampaignEvents.OnSessionLaunchedEvent.Raise(new CampaignGameStarter());
        Check(!loaded.IsCoronated(realm) && loaded.GetLegitimacy(realm) == 37f,
            "startup politics preserves unresolved transfer state " + fail);
        CampaignEvents.TickEvent.Raise(0.1f);
        CampaignTime.Day += 7; CampaignEvents.DailyTickEvent.Raise();
        Check(fail ? !loaded.IsCoronated(realm) && loaded.GetLegitimacy(realm) == 37f : loaded.IsCoronated(realm),
            "coronation resumes only after successful transfer " + fail);
    }
    private static void AbdicationAndVacancy()
    {
        Reset(); var realm = new Kingdom { StringId = "abdication" };
        Clan house = House(realm, "royal"); Hero king = Add(house, "king", 60);
        Clan rivals = House(realm, "rival"); Hero successor = Add(rivals, "successor", 40); rivals.Leader = successor;
        var behavior = Start(realm, house, king);
        behavior.InterceptRulerDecision(new TaleWorlds.CampaignSystem.Election.KingSelectionKingdomDecision { Kingdom = realm, IsEnforced = true });
        CampaignEvents.TickEvent.Raise(0.1f);
        Check(realm.Leader == successor && king.IsAlive, "abdication excludes living incumbent from fallback order");
        Reset(); realm = new Kingdom { StringId = "regent-abdication" }; house = House(realm, "royal"); king = Add(house, "king", 60);
        Hero ward = Add(house, "ward", 10, father: king);
        Clan firstHouse = House(realm, "first-regent"); firstHouse.Leader = Add(firstHouse, "first", 50);
        Clan secondHouse = House(realm, "second-regent"); secondHouse.Leader = Add(secondHouse, "second", 40);
        behavior = Start(realm, house, king); Die(king);
        Hero formerRegent = behavior.GetRegent(realm);
        behavior.InterceptRulerDecision(new TaleWorlds.CampaignSystem.Election.KingSelectionKingdomDecision { Kingdom = realm, IsEnforced = true });
        var abdicationSave = new Dictionary<string, string>(); behavior.SyncData(new Store(abdicationSave, true));
        CampaignEvents.Reset(); behavior = new SuccessionCampaignBehavior(); behavior.SyncData(new Store(abdicationSave, false)); behavior.RegisterEvents();
        CampaignEvents.TickEvent.Raise(0.1f);
        Check(behavior.GetRegent(realm) != formerRegent && behavior.GetRegent(realm) != null && behavior.GetMinorHeir(realm) == ward,
            "saved regent abdication replaces the regent and preserves the child");
        Reset(); realm = new Kingdom { StringId = "vacancy" }; house = House(realm, "royal"); king = Add(house, "king", 60);
        behavior = Start(realm, house, king); Die(king);
        rivals = House(realm, "new-house"); successor = Add(rivals, "new-ruler", 40); rivals.Leader = successor;
        CampaignTime.Day++; CampaignEvents.DailyTickEvent.Raise();
        Check(realm.Leader == king, "vacancy recovery defers native action until tick");
        CampaignEvents.TickEvent.Raise(0.1f);
        Check(realm.Leader == successor, "empty throne recovers when a new eligible clan arrives");
        Reset(); realm = new Kingdom { StringId = "blocked-transfer" }; house = House(realm, "royal"); king = Add(house, "king", 60);
        Hero heir = Add(house, "heir", 30, father: king); behavior = Start(realm, house, king);
        ChangeClanLeaderAction.AfterApply = delegate { house.Leader = king; realm.Leader = king; };
        Die(king);
        int count = ChangeClanLeaderAction.ApplyCount;
        CampaignTime.Day++; CampaignEvents.DailyTickEvent.Raise(); CampaignEvents.TickEvent.Raise(0.1f);
        Check(ChangeClanLeaderAction.ApplyCount == count && realm.Leader != heir,
            "competing mod reverting native transfer is detected and quarantined");
    }
    private static void NativeLeadershipBeforeDeath(bool changeRulingClan)
    {
        Reset(); var realm = new Kingdom { StringId = "native-first" };
        Clan house = House(realm, "house"); Hero king = Add(house, "king", 65);
        Hero elder = Add(house, "elder", 35, father: king);
        Hero younger = Add(house, "younger", 30, father: king);
        var behavior = Start(realm, house, king);
        king.IsAlive = false; Hero.AllAliveHeroes.Remove(king); Hero.DeadOrDisabledHeroes.Add(king);
        house.Leader = younger; realm.Leader = younger;
        if (changeRulingClan)
        {
            Clan interim = House(realm, "interim"); interim.Leader = Add(interim, "interim-leader", 40);
            realm.RulingClan = interim; realm.Leader = interim.Leader;
        }
        CampaignEvents.HeroKilledEvent.Raise(king); CampaignEvents.TickEvent.Raise(0.1f);
        Check(realm.Leader == elder && realm.RulingClan == house,
            "persisted monarch identity survives native leadership changes before death notification " + changeRulingClan);
        int count = ChangeClanLeaderAction.ApplyCount;
        CampaignEvents.HeroKilledEvent.Raise(king); CampaignEvents.TickEvent.Raise(0.1f);
        Check(ChangeClanLeaderAction.ApplyCount == count && behavior.GetMinorHeir(realm) == null,
            "late duplicate death cannot replace adult dynastic successor " + changeRulingClan);
    }
}
