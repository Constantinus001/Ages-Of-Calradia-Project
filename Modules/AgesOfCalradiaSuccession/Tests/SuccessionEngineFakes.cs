using System;
using TaleWorlds.CampaignSystem;
namespace TaleWorlds.CampaignSystem
{
    public abstract class CampaignBehaviorBase
    {
        public abstract void RegisterEvents();
        public abstract void SyncData(IDataStore dataStore);
    }
    public sealed class CampaignGameStarter { }
    public sealed class TestEvent<T>
    {
        private Action<T> _listeners;
        public void AddNonSerializedListener(object owner, Action<T> action) { _listeners += action; }
        public void Raise(T arg) { if (_listeners != null) _listeners(arg); }
    }
    public sealed class TestEvent
    {
        private Action _listeners;
        public void AddNonSerializedListener(object owner, Action action) { _listeners += action; }
        public void Raise() { if (_listeners != null) _listeners(); }
    }
    public sealed class TestDeathEvent
    {
        private Action<Hero, Hero, Actions.KillCharacterAction.KillCharacterActionDetail, bool> _listeners;
        public void AddNonSerializedListener(object owner, Action<Hero, Hero, Actions.KillCharacterAction.KillCharacterActionDetail, bool> action) { _listeners += action; }
        public void Raise(Hero hero) { if (_listeners != null) _listeners(hero, null, Actions.KillCharacterAction.KillCharacterActionDetail.DiedInBattle, false); }
    }
    public static class CampaignEvents
    {
        public static TestEvent<CampaignGameStarter> OnSessionLaunchedEvent = new TestEvent<CampaignGameStarter>();
        public static TestEvent DailyTickEvent = new TestEvent();
        public static TestEvent<Kingdom> KingdomCreatedEvent = new TestEvent<Kingdom>();
        public static TestDeathEvent HeroKilledEvent = new TestDeathEvent();
        public static TestEvent<float> TickEvent = new TestEvent<float>();
        public static TestEvent<Hero> HeroComesOfAgeEvent = new TestEvent<Hero>();
        public static void Reset()
        {
            OnSessionLaunchedEvent = new TestEvent<CampaignGameStarter>(); DailyTickEvent = new TestEvent();
            KingdomCreatedEvent = new TestEvent<Kingdom>(); HeroKilledEvent = new TestDeathEvent();
            TickEvent = new TestEvent<float>(); HeroComesOfAgeEvent = new TestEvent<Hero>();
        }
    }
    public struct CampaignTime
    {
        public static double Day { get; set; } = 100d;
        public static CampaignTime Now { get { return new CampaignTime(); } }
        public double ToDays { get { return Day; } }
    }
    public static class FactionManager
    {
        public static float GetRelationBetweenClans(Clan a, Clan b) { return 0f; }
        public static readonly System.Collections.Generic.HashSet<string> Wars = new System.Collections.Generic.HashSet<string>();
        public static bool IsAtWarAgainstFaction(Kingdom a, Kingdom b) { return Wars.Contains(a.StringId + ":" + b.StringId) || Wars.Contains(b.StringId + ":" + a.StringId); }
    }
}
namespace TaleWorlds.CampaignSystem.Actions
{
    public static class ChangeRulingClanAction
    {
        public static void Apply(Kingdom kingdom, Clan clan) { kingdom.RulingClan = clan; kingdom.Leader = clan.Leader; }
    }
    public static class ChangeClanLeaderAction
    {
        public static Action AfterApply { get; set; }
        public static int ApplyCount { get; set; }
        public static void ApplyWithSelectedNewLeader(Clan clan, Hero hero)
        {
            ApplyCount++;
            clan.Leader = hero;
            if (clan.Kingdom != null && clan.Kingdom.RulingClan == clan) clan.Kingdom.Leader = hero;
            if (AfterApply != null) AfterApply();
        }
    }
}
namespace TaleWorlds.Core { public sealed class UnusedCoreNamespace { } }
namespace TaleWorlds.Library
{
    public sealed class InformationMessage { public InformationMessage(string text) { } }
    public static class InformationManager { public static void DisplayMessage(InformationMessage message) { } }
}
namespace AgesOfCalradiaSuccession
{
    internal static class SuccessionDebugMenu { internal static void Register(CampaignGameStarter starter, SuccessionCampaignBehavior behavior) { } }
    internal static class SuccessionCoronationMenu { internal static void Register(CampaignGameStarter starter, SuccessionCampaignBehavior behavior) { } }
}
