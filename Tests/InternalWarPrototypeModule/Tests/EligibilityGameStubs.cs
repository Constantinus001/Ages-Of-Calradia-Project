// Data-only substitutes for compiling the actual service eligibility logic without loading Bannerlord.
// These types do not model native callbacks, save loading, diplomacy, menus, or siege execution.
using System;
using System.Collections.Generic;

namespace TaleWorlds.Core
{
    public enum BattleSideEnum { None = -1, Defender, Attacker }
}

namespace TaleWorlds.CampaignSystem
{
    public sealed class Kingdom { public string StringId { get; set; } }
    public sealed class Army { }
    public sealed class Hero { public Clan Clan { get; set; } }
    public sealed class Clan
    {
        public static Clan PlayerClan { get; set; }
        public string StringId { get; set; }
        public Hero Leader { get; set; }
        public Kingdom Kingdom { get; set; }
        public bool IsClanTypeMercenary { get; set; }
        public bool IsUnderMercenaryService { get; set; }
        public bool IsMinorFaction { get; set; }
    }
}

namespace TaleWorlds.CampaignSystem.MapEvents
{
    public sealed class MapEvent
    {
        public static MapEvent PlayerMapEvent { get; set; }
        public enum BattleTypes { None, Siege }
        public bool IsSiegeAssault { get; set; }
        public bool IsFieldBattle { get; set; }
        public Settlements.Settlement MapEventSettlement { get; set; }
        public MapEventSide AttackerSide { get; set; }
        public MapEventSide DefenderSide { get; set; }
    }
    public sealed class MapEventSide { public Party.PartyBase LeaderParty { get; set; } }
}

namespace TaleWorlds.CampaignSystem.Encounters
{
    public sealed class PlayerEncounter
    {
        public PlayerEncounter(TaleWorlds.Core.BattleSideEnum playerSide, TaleWorlds.Core.BattleSideEnum opponentSide)
        {
            PlayerSide = playerSide;
            OpponentSide = opponentSide;
        }
        public static PlayerEncounter Current { get; set; }
        public TaleWorlds.Core.BattleSideEnum PlayerSide { get; private set; }
        public TaleWorlds.Core.BattleSideEnum OpponentSide { get; private set; }
        public void SetupFields(Party.PartyBase attackerParty, Party.PartyBase defenderParty)
        {
            throw new InvalidOperationException("Native encounter setup is outside the eligibility verifier.");
        }
    }
}

namespace TaleWorlds.CampaignSystem.Siege
{
    public sealed class SiegeEvent
    {
        public BesiegerCamp BesiegerCamp { get; set; }
        public Settlements.Settlement BesiegedSettlement { get; set; }
    }
    public sealed class BesiegerCamp
    {
        public SiegeEvent SiegeEvent { get; set; }
        public Party.MobileParty LeaderParty { get; set; }
    }
    public static class PlayerSiege
    {
        public static SiegeEvent PlayerSiegeEvent { get; set; }
    }
}

namespace TaleWorlds.CampaignSystem.Party
{
    public sealed class PartyBase
    {
        public int NumberOfHealthyMembers { get; set; }
        public MapEvents.MapEvent MapEvent { get; set; }
        public Settlements.Settlement Settlement { get; set; }
        public MobileParty MobileParty { get; set; }
    }

    public sealed class MobileParty
    {
        public MobileParty() { Party = new PartyBase { MobileParty = this }; }
        public static MobileParty MainParty { get; set; }
        public string StringId { get; set; }
        public PartyBase Party { get; private set; }
        public bool IsActive { get; set; }
        public bool IsLordParty { get; set; }
        public bool IsCurrentlyAtSea { get; set; }
        public MobileParty AttachedTo { get; set; }
        public List<MobileParty> AttachedParties { get; } = new List<MobileParty>();
        public Army Army { get; set; }
        public Siege.BesiegerCamp BesiegerCamp { get; set; }
        public Clan ActualClan { get; set; }
        public Hero LeaderHero { get; set; }
        public Settlements.Settlement HomeSettlement { get; set; }
        public Settlements.Settlement CurrentSettlement { get; set; }
        public bool IsVillager { get; set; }
        public bool IsCaravan { get; set; }
        public bool IsMilitia { get; set; }
    }
}

namespace TaleWorlds.CampaignSystem.Settlements
{
    public sealed class Settlement
    {
        public Settlement()
        {
            Party = new Party.PartyBase { Settlement = this };
            Parties = new List<Party.MobileParty>();
        }
        public static List<Settlement> All { get; private set; } = new List<Settlement>();
        public static Settlement CurrentSettlement { get; set; }
        public string StringId { get; set; }
        public bool IsFortification { get; set; }
        public Clan OwnerClan { get; set; }
        public Siege.SiegeEvent SiegeEvent { get; set; }
        public Party.PartyBase Party { get; private set; }
        public List<Party.MobileParty> Parties { get; private set; }
    }

    public sealed class Town
    {
        public Settlement Settlement { get; set; }
        public bool InRebelliousState { get; set; }
    }
}

namespace AgesOfCalradiaInternalWarsTest
{
    // Sally policy has its own actual-source verifier; unrelated service scenarios inject no sally event.
    internal static class InternalWarSallyService
    {
        internal static InternalWarTestRecord Find(TaleWorlds.CampaignSystem.MapEvents.MapEvent battle) { return null; }
    }
    internal static class InternalWarReliefService
    {
        internal static InternalWarTestRecord Find(TaleWorlds.CampaignSystem.MapEvents.MapEvent battle) { return null; }
    }
    internal sealed class InternalWarTestBehavior
    {
        private InternalWarTestBehavior _selected;
        internal InternalWarTestBehavior SelectedController { get { return _selected ?? this; } set { _selected = value; } }
        internal System.Collections.Generic.List<InternalWarTestBehavior> TestControllers { get; set; }
        internal System.Collections.Generic.IEnumerable<InternalWarTestBehavior> Controllers
        { get { return TestControllers == null ? (System.Collections.Generic.IEnumerable<InternalWarTestBehavior>)new[] { this } : TestControllers; } }
        internal bool OwnsPartyOrder(string id) { return CurrentRecord != null && CurrentRecord.IsOperational && CurrentRecord.LeaderPartyId == id; }
        internal bool OwnsSettlementOrder(string id) { return CurrentRecord != null && CurrentRecord.IsOperational && CurrentRecord.SettlementId == id; }
        internal InternalWarTestRecord CurrentRecord { get; set; }
        internal InternalConflictRecord Conflict { get; set; }
        internal InternalWarConflictRegistry Registry { get; set; }
        internal System.Collections.Generic.IEnumerable<InternalConflictRecord> Conflicts
        { get { return Registry != null ? Registry.Records : Conflict == null ? new InternalConflictRecord[0] : new[] { Conflict }; } }
        internal InternalConflictRecord FindConflict(string first, string second)
        {
            if (Registry != null) return Registry.Find(first, second);
            return Conflict != null && Conflict.IsOpen && ((Conflict.AttackerClanId == first && Conflict.DefenderClanId == second)
                || (Conflict.AttackerClanId == second && Conflict.DefenderClanId == first)) ? Conflict : null;
        }
        internal string RecoveryBlocker { get; set; }
        internal void DrainCleanup(float dt) { throw new InvalidOperationException("Native cleanup is not modeled."); }
        internal bool HasActiveRaid { get; set; }
        internal bool IsRaidBattle(TaleWorlds.CampaignSystem.MapEvents.MapEvent battle) { return false; }
        internal bool IsRaidHostility(TaleWorlds.CampaignSystem.Party.PartyBase attacker, TaleWorlds.CampaignSystem.Party.PartyBase defender) { return false; }
        internal bool CanStartNpcRaid(TaleWorlds.CampaignSystem.Party.MobileParty party, TaleWorlds.CampaignSystem.Settlements.Settlement target) { return false; }
        internal void BlockRecovery(string reason) { RecoveryBlocker = reason; }
        internal Action<TaleWorlds.CampaignSystem.Settlements.Settlement> CaptureObserver { get; set; }
        internal void MarkCapture(TaleWorlds.CampaignSystem.Settlements.Settlement settlement)
        {
            // An injected observer verifies dispatch only; no native ownership transfer is simulated.
            if (CaptureObserver != null) { CaptureObserver(settlement); return; }
            throw new InvalidOperationException("Native capture execution is outside the eligibility verifier.");
        }
    }
}
