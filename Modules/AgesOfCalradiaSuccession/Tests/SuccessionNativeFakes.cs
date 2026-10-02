// Native boundary doubles only. The actual death integration, dispatch queue,
// persistence codec and Harmony installer/prefixes are linked unchanged.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;

namespace TaleWorlds.CampaignSystem
{
    public sealed class Hero
    {
        public string StringId;
        public bool IsAlive;
        public bool IsActive { get; set; } = true;
        public bool IsLord { get; set; } = true;
        public bool IsFemale { get; set; }
        public float Age { get; set; }
        public Hero Father { get; set; }
        public Hero Mother { get; set; }
        public Hero Spouse { get; set; }
        public Clan Clan { get; set; }
        public float ReligiousLegitimacy { get; set; } = 50f;
        public readonly List<Hero> Children = new List<Hero>();
        public static Hero MainHero { get; set; }
        public static readonly List<Hero> AllAliveHeroes = new List<Hero>();
        public static readonly List<Hero> DeadOrDisabledHeroes = new List<Hero>();
        public string Name { get { return StringId; } }
        public CultureObject Culture { get; set; }
    }
    public sealed class CultureObject { public string StringId { get; set; } }
    public sealed class Clan
    {
#if SUCCESSION_ENGINE_TEST
        public readonly List<Settlements.Town> Fiefs = new List<Settlements.Town>();
        public TaleWorlds.Core.Banner Banner { get; set; }
        public TaleWorlds.Core.Banner ClanOriginalBanner { get; set; }
#endif
        public static readonly List<Clan> All = new List<Clan>();
        public string StringId { get; set; }
        public CultureObject Culture { get; set; }
        public Hero Leader { get; set; }
        public Kingdom Kingdom { get; set; }
        public bool IsEliminated { get; set; }
        public bool IsMinorFaction { get; set; }
        public bool IsClanTypeMercenary { get; set; }
        public int Tier { get; set; }
        public float Renown { get; set; }
        public readonly List<Hero> Heroes = new List<Hero>();
    }
    public sealed class Kingdom
    {
#if SUCCESSION_ENGINE_TEST
        public uint Color { get; set; }
        public uint Color2 { get; set; }
        public Settlements.Settlement InitialHomeSettlement { get; set; }
        public List<Settlements.Town> Fiefs { get { var fiefs = new List<Settlements.Town>(); foreach (Clan c in Clans) fiefs.AddRange(c.Fiefs); return fiefs; } }
        public static Kingdom CreateKingdom(string id) { var k = new Kingdom { StringId = id }; All.Add(k); CampaignEvents.KingdomCreatedEvent.Raise(k); return k; }
        public void InitializeKingdom(TaleWorlds.Localization.TextObject a, TaleWorlds.Localization.TextObject b, CultureObject culture, TaleWorlds.Core.Banner banner, uint c, uint d, Settlements.Settlement home, TaleWorlds.Localization.TextObject e, TaleWorlds.Localization.TextObject f, TaleWorlds.Localization.TextObject g) { Culture = culture; }
#endif
        public static readonly List<Kingdom> All = new List<Kingdom>();
        public string StringId;
        public bool IsEliminated { get; set; }
        public Hero Leader;
        public CultureObject Culture { get; set; }
        public readonly List<Clan> Clans = new List<Clan>();
        public Clan RulingClan { get; set; }
        public string Name { get { return StringId; } }
        public readonly List<KingdomDecision> UnresolvedDecisions = new List<KingdomDecision>();
        public int NativeAdds;
        public int NativeOutcomes;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void AddDecision(KingdomDecision decision, bool ignored)
        {
            NativeAdds++;
            UnresolvedDecisions.Add(decision);
        }
        public void RemoveDecision(KingdomDecision decision) { UnresolvedDecisions.Remove(decision); }
    }
    public sealed class Campaign
    {
        public static Campaign Current;
        public object Behavior;
        public T GetCampaignBehavior<T>() where T : class { return Behavior as T; }
    }
    public interface IDataStore
    {
        bool IsSaving { get; }
        bool IsLoading { get; }
        void SyncData(string key, ref string value);
    }
}
namespace TaleWorlds.CampaignSystem.Actions
{
    public static class KillCharacterAction { public enum KillCharacterActionDetail { DiedInBattle } }
}
namespace TaleWorlds.CampaignSystem.Election
{
    public class KingdomDecision { public Kingdom Kingdom; public bool IsEnforced; }
    public class DecisionOutcome { }
    public sealed class KingSelectionKingdomDecision : KingdomDecision
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ApplyChosenOutcome(DecisionOutcome outcome) { Kingdom.NativeOutcomes++; }
    }
}
namespace AgesOfCalradiaSuccession
{
    internal static class SuccessionReligionBridge
    {
        internal static float GetReligiousLegitimacy(Hero hero) { return hero == null ? 50f : hero.ReligiousLegitimacy; }
        internal static string GetOfficialFaith(Kingdom kingdom) { return string.Empty; }
        internal static string GetPersonalFaith(Hero hero) { return string.Empty; }
    }
    internal static class SuccessionDiagnostics
    {
        internal static readonly List<string> Messages = new List<string>();
        internal static void Info(string message) { Messages.Add(message); }
        internal static void Error(string message, Exception exception) { Messages.Add(message + exception.Message); }
    }
#if !SUCCESSION_ENGINE_TEST
    public sealed partial class SuccessionCampaignBehavior
    {
        private bool IsCrisisBusy { get { return false; } }
        private static int CurrentDay { get { return 0; } }
        private bool CrisisBlocksRealm(Kingdom kingdom) { return false; }
        private void AuditCrises() { }
        private readonly Dictionary<string, string> _monarchByKingdom = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _regentByKingdom = new Dictionary<string, string>();
        internal readonly Dictionary<string, Hero> Heroes = new Dictionary<string, Hero>();
        internal readonly Dictionary<string, Hero> Minors = new Dictionary<string, Hero>();
        internal int Transfers;
        internal Action DuringTransfer;
        internal Action AfterTransfer;
        internal bool ThrowOnTransfer;
        internal void Record(Kingdom kingdom, Hero monarch)
        {
            Heroes[monarch.StringId] = monarch;
            _monarchByKingdom[kingdom.StringId] = monarch.StringId;
        }
        internal void Regent(Kingdom kingdom, Hero regent)
        {
            Heroes[regent.StringId] = regent;
            _regentByKingdom[kingdom.StringId] = regent.StringId;
        }
        private Hero FindHero(string id) { Hero hero; return Heroes.TryGetValue(id, out hero) ? hero : null; }
        private static string Get(IDictionary<string,string> map, string key) { string value; return map.TryGetValue(key, out value) ? value : string.Empty; }
        private Hero GetRegent(Kingdom kingdom) { return FindHero(Get(_regentByKingdom, kingdom.StringId)); }
        private Hero GetMinorHeir(Kingdom kingdom) { Hero minor; return Minors.TryGetValue(kingdom.StringId, out minor) ? minor : null; }
        private void Snapshot(Kingdom kingdom) { if (kingdom.Leader != null) Record(kingdom, kingdom.Leader); }
        private void ResolveSuccession(Kingdom kingdom, Hero abdicator = null)
        {
            Transfers++;
            if (DuringTransfer != null) DuringTransfer();
            if (ThrowOnTransfer) throw new InvalidOperationException("injected native failure");
            Record(kingdom, kingdom.Leader);
            if (AfterTransfer != null) AfterTransfer();
        }
        internal void Tick() { OnSuccessionTick(0.1f); }
        internal void Death(Hero hero) { OnHeroKilled(hero, null, TaleWorlds.CampaignSystem.Actions.KillCharacterAction.KillCharacterActionDetail.DiedInBattle, false); }
        internal string SaveQueue() { return _dispatch.Serialize(); }
        internal void LoadQueue(string value) { _dispatch.Deserialize(value); }
        internal void Recover() { RecoverPendingSuccessions(); }
    }
#endif
}
