using System;
using System.Collections.Generic;
using AgesOfCalradiaInternalWarsTest;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Siege;

internal static class SallyProgram
{
    private static int _checks;
    private static void Check(bool value, string reason) { _checks++; if (!value) throw new InvalidOperationException(reason); }
    private static int Main()
    {
        var defenderClan = new Clan { StringId = "owner" };
        var attackerClan = new Clan { StringId = "besieger" };
        var target = new Settlement { IsFortification = true, OwnerClan = defenderClan };
        var garrison = new MobileParty { StringId = "garrison", Clan = defenderClan, CurrentSettlement = target };
        var besieger = new MobileParty { StringId = "leader", Clan = attackerClan };
        target.Town = new Town { GarrisonParty = garrison };
        target.SiegeEvent = new SiegeEvent { BesiegerCamp = new BesiegerCamp { LeaderParty = besieger } };
        besieger.BesiegerCamp = target.SiegeEvent.BesiegerCamp;
        InternalWarTestService.Target = target;
        InternalWarTestService.Record = new InternalWarTestRecord { LeaderPartyId = "leader", AttackerClanId = "besieger",
            DefenderClanId = "owner", ConflictId = "operation", State = InternalWarTestState.Preparing };
        InternalWarTestService.War = new InternalConflictRecord { ActiveOperationId = "operation" };
        Check(InternalWarSallyService.CanStart(garrison.Party, besieger.Party), "Exact garrison cannot sally.");
        Check(!InternalWarSallyService.CanStart(besieger.Party, garrison.Party), "Reversed initiation accepted.");
        foreach (string change in new[] { "foreign owner", "army", "attached", "sea", "battle", "peace", "camp", "claim capture", "empty", "minor", "mercenary" })
        {
            if (change == "foreign owner") target.OwnerClan = attackerClan;
            if (change == "army") garrison.Army = new object();
            if (change == "attached") garrison.AttachedParties.Add(new MobileParty());
            if (change == "sea") besieger.IsCurrentlyAtSea = true;
            if (change == "battle") besieger.Party.MapEvent = new MapEvent();
            if (change == "peace") InternalWarTestService.War.Phase = InternalConflictPhase.PeacePending;
            if (change == "camp") besieger.BesiegerCamp = new BesiegerCamp();
            if (change == "claim capture") InternalWarTestService.Record.CaptureApplied = true;
            if (change == "empty") garrison.Party.NumberOfHealthyMembers = 0;
            if (change == "minor") defenderClan.IsMinorFaction = true;
            if (change == "mercenary") attackerClan.IsUnderMercenaryService = true;
            Check(!InternalWarSallyService.CanStart(garrison.Party, besieger.Party), "Unsafe sally accepted: " + change);
            target.OwnerClan = defenderClan; garrison.Army = null; garrison.AttachedParties.Clear(); besieger.IsCurrentlyAtSea = false;
            besieger.Party.MapEvent = null; InternalWarTestService.War.Phase = InternalConflictPhase.Active;
            besieger.BesiegerCamp = target.SiegeEvent.BesiegerCamp; InternalWarTestService.Record.CaptureApplied = false;
            garrison.Party.NumberOfHealthyMembers = 10; defenderClan.IsMinorFaction = false; attackerClan.IsUnderMercenaryService = false;
        }
        var battle = new MapEvent { IsSallyOut = true, MapEventSettlement = target,
            AttackerSide = new MapEventSide { LeaderParty = garrison.Party }, DefenderSide = new MapEventSide { LeaderParty = besieger.Party } };
        garrison.Party.MapEvent = battle; besieger.Party.MapEvent = battle;
        Check(InternalWarSallyService.Find(battle) == InternalWarTestService.Record, "Mechanical sally identity not found.");
        InternalWarTestService.War.Phase = InternalConflictPhase.PeacePending;
        Check(InternalWarSallyService.Find(battle) == InternalWarTestService.Record, "Pending peace lost existing battle identity.");
        InternalWarTestService.Record.State = InternalWarTestState.Marching;
        Check(InternalWarSallyService.Find(battle) == null, "Marching record acquired existing sally.");
        InternalWarTestService.Record.State = InternalWarTestState.Captured;
        Check(InternalWarSallyService.Find(battle) == null, "Terminal record acquired existing sally.");
        InternalWarTestService.Record.State = InternalWarTestState.Preparing;
        battle.AttackerSide.LeaderParty = besieger.Party; battle.DefenderSide.LeaderParty = garrison.Party;
        Check(InternalWarSallyService.Find(battle) == null, "Assault orientation accepted as sally.");
        battle.AttackerSide.LeaderParty = garrison.Party; battle.DefenderSide.LeaderParty = besieger.Party;
        battle.IsSallyOut = false;
        Check(InternalWarSallyService.Find(battle) == null, "Non-sally event acquired sally authorization.");
        garrison.Party.MapEvent = null; besieger.Party.MapEvent = null; InternalWarTestService.War.Phase = InternalConflictPhase.Active;
        MobileParty.All.Clear(); MobileParty.All.Add(garrison); MobileParty.All.Add(besieger);
        garrison.Party.NumberOfHealthyMembers = 201; besieger.Party.NumberOfHealthyMembers = 100;
        EncounterManager.Calls = 0;
        InternalWarSallyService.TryAutomatic(target);
        Check(EncounterManager.Calls == 1, "Own-clan force advantage did not request native entry.");
        garrison.Party.NumberOfHealthyMembers = 200;
        InternalWarSallyService.TryAutomatic(target);
        Check(EncounterManager.Calls == 1, "Exact threshold started another sally.");
        garrison.Party.NumberOfHealthyMembers = 300;
        MobileParty.MainParty = new MobileParty { CurrentSettlement = target };
        InternalWarSallyService.TryAutomatic(target);
        Check(EncounterManager.Calls == 1, "Automatic sally overrode an inside player.");
        Console.WriteLine("Sally service: " + _checks + " assertions passed; actual service with injected native boundaries.");
        return 0;
    }
}
namespace TaleWorlds.CampaignSystem
{
    public sealed class Clan { public string StringId { get; set; } public static Clan PlayerClan { get; set; }
        public bool IsMinorFaction { get; set; } public bool IsClanTypeMercenary { get; set; } public bool IsUnderMercenaryService { get; set; } }
    public static class EncounterManager { public static int Calls; public static void StartPartyEncounter(PartyBase first, PartyBase second) { Calls++; } }
}
namespace TaleWorlds.CampaignSystem.Party
{
    public sealed class PartyBase { public MobileParty MobileParty { get; set; } public MapEvent MapEvent { get; set; } public int NumberOfHealthyMembers { get; set; } = 10; }
    public sealed class MobileParty
    {
        public MobileParty() { Party = new PartyBase { MobileParty = this }; }
        public static List<MobileParty> All { get; } = new List<MobileParty>();
        public static MobileParty MainParty { get; set; } public string StringId { get; set; } public Clan Clan { get; set; }
        public PartyBase Party { get; } public Settlement CurrentSettlement { get; set; } public BesiegerCamp BesiegerCamp { get; set; }
        public bool IsActive { get; set; } = true; public bool IsCurrentlyAtSea { get; set; } public bool IsCaravan { get; set; }
        public bool IsVillager { get; set; } public bool IsLordParty { get; set; } public object Army { get; set; }
        public MobileParty AttachedTo { get; set; } public List<MobileParty> AttachedParties { get; } = new List<MobileParty>();
    }
}
namespace TaleWorlds.CampaignSystem.Settlements
{
    public sealed class Town { public MobileParty GarrisonParty { get; set; } }
    public sealed class Settlement { public bool IsFortification { get; set; } public Town Town { get; set; }
        public Clan OwnerClan { get; set; } public PartyBase Party { get; } = new PartyBase(); public SiegeEvent SiegeEvent { get; set; }
        public List<MobileParty> Parties { get; } = new List<MobileParty>(); }
}
namespace TaleWorlds.CampaignSystem.Siege
{
    public sealed class SiegeEvent { public BesiegerCamp BesiegerCamp { get; set; } }
    public sealed class BesiegerCamp { public MobileParty LeaderParty { get; set; } }
}
namespace TaleWorlds.CampaignSystem.MapEvents
{
    public sealed class MapEvent { public bool IsSallyOut { get; set; } public Settlement MapEventSettlement { get; set; }
        public MapEventSide AttackerSide { get; set; } public MapEventSide DefenderSide { get; set; } }
    public sealed class MapEventSide { public PartyBase LeaderParty { get; set; } }
}
namespace AgesOfCalradiaInternalWarsTest
{
    internal enum InternalWarTestState { Preparing, Assaulting, RoyalPeacePending, Marching, Captured }
    internal enum InternalConflictPhase { Active, PeacePending }
    internal sealed class InternalWarTestRecord { internal string LeaderPartyId, AttackerClanId, DefenderClanId, ConflictId;
        internal InternalWarTestState State; internal bool CleanupComplete { get; set; } internal bool CaptureApplied { get; set; } }
    internal sealed class InternalConflictRecord { internal InternalConflictPhase Phase; internal string ActiveOperationId; }
    internal static class InternalWarTestService
    {
        internal static Settlement Target; internal static InternalWarTestRecord Record; internal static InternalConflictRecord War;
        internal static InternalWarTestRecord Find(Settlement target) { return target == Target ? Record : null; }
        internal static Clan ResolveClan(MobileParty party) { return party == null ? null : party.Clan; }
        internal static Clan ResolveClan(PartyBase party) { return party == null ? null : ResolveClan(party.MobileParty); }
        internal static InternalConflictRecord FindConflict(Clan first, Clan second) { return War; }
        internal static void BlockRecovery(string message) { }
    }
    internal static class InternalWarCombatService { internal static bool Opposed(Clan first, Clan second) { return first != second; } }
    internal static class InternalWarTestDiagnostics { internal static void Error(string message, Exception error) { } }
}
