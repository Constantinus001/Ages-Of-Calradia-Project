using System;
using System.Collections.Generic;
using AgesOfCalradiaInternalWarsTest;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.Core;

internal static class ReliefProgram
{
    private static int _checks;
    private static void Check(bool result, string message) { _checks++; if (!result) throw new InvalidOperationException(message); }
    private static int Main()
    {
        var besiegerClan = new Clan { StringId = "besieger" };
        var ownerClan = new Clan { StringId = "owner" };
        var besieger = new MobileParty { StringId = "leader", Clan = besiegerClan };
        var relief = new MobileParty { StringId = "relief", Clan = ownerClan };
        var target = new Settlement { IsFortification = true, OwnerClan = ownerClan };
        var siege = new SiegeEvent { BesiegedSettlement = target };
        siege.BesiegerCamp = new BesiegerCamp { SiegeEvent = siege, LeaderParty = besieger };
        target.SiegeEvent = siege; besieger.BesiegerCamp = siege.BesiegerCamp;
        InternalWarTestService.Target = target;
        InternalWarTestService.Record = new InternalWarTestRecord { AttackerClanId = "besieger", DefenderClanId = "owner",
            LeaderPartyId = "leader", ConflictId = "op", State = InternalWarTestState.Preparing };
        InternalWarTestService.War = new InternalConflictRecord { ActiveOperationId = "op" };
        Check(InternalWarReliefService.CanStart(besieger.Party, relief.Party), "Canonical relief rejected.");
        Check(InternalWarReliefService.CanStart(relief.Party, besieger.Party), "Reverse relief arrival rejected.");
        PartyBase first = relief.Party, second = besieger.Party;
        InternalWarReliefEncounterPatch.Prefix(ref first, ref second);
        Check(first == besieger.Party && second == relief.Party, "Encounter refs did not normalize.");
        InternalWarReliefEncounterPatch.Prefix(ref first, ref second);
        Check(first == besieger.Party && second == relief.Party, "Canonical args changed on repeated prefix.");
        first = relief.Party; second = besieger.Party;
        InternalWarReliefPlayerInitPatch.Prefix(ref first, ref second);
        Check(first == besieger.Party && second == relief.Party, "Direct player Init did not normalize before side assignment.");
        foreach (string invalid in new[] { "neutral", "sea", "army", "attached", "inside", "event", "empty", "peace", "stale", "foreign camp" })
        {
            if (invalid == "neutral") relief.Clan = new Clan { StringId = "neutral" };
            if (invalid == "sea") relief.IsCurrentlyAtSea = true;
            if (invalid == "army") besieger.Army = new object();
            if (invalid == "attached") relief.AttachedTo = besieger;
            if (invalid == "inside") relief.CurrentSettlement = target;
            if (invalid == "event") relief.Party.MapEvent = new MapEvent();
            if (invalid == "empty") besieger.Party.NumberOfHealthyMembers = 0;
            if (invalid == "peace") InternalWarTestService.War.Phase = InternalConflictPhase.PeacePending;
            if (invalid == "stale") InternalWarTestService.Record.State = InternalWarTestState.Marching;
            if (invalid == "foreign camp") siege.BesiegerCamp.LeaderParty = relief;
            Check(!InternalWarReliefService.CanStart(relief.Party, besieger.Party), "Unsafe relief accepted: " + invalid);
            first = relief.Party; second = besieger.Party;
            InternalWarReliefEncounterPatch.Prefix(ref first, ref second);
            Check(first == relief.Party && second == besieger.Party, "Invalid encounter was mutated: " + invalid);
            relief.Clan = ownerClan; relief.IsCurrentlyAtSea = false; besieger.Army = null; relief.AttachedTo = null;
            relief.CurrentSettlement = null; relief.Party.MapEvent = null; besieger.Party.NumberOfHealthyMembers = 10;
            InternalWarTestService.War.Phase = InternalConflictPhase.Active; InternalWarTestService.Record.State = InternalWarTestState.Preparing;
            siege.BesiegerCamp.LeaderParty = besieger;
        }
        var battle = new MapEvent { IsSiegeOutside = true, MapEventSettlement = target,
            AttackerSide = new MapEventSide { LeaderParty = besieger.Party }, DefenderSide = new MapEventSide { LeaderParty = relief.Party } };
        besieger.Party.MapEvent = battle; relief.Party.MapEvent = battle;
        Check(InternalWarReliefService.Find(battle) == InternalWarTestService.Record, "Recorded relief event unresolved.");
        Check(InternalWarReliefService.BattleSideFor(InternalWarTestService.Record, battle, ownerClan) == BattleSideEnum.Defender,
            "Owner clan got wrong normalized side.");
        battle.AttackerSide.LeaderParty = relief.Party; battle.DefenderSide.LeaderParty = besieger.Party;
        Check(InternalWarReliefService.Find(battle) == InternalWarTestService.Record, "Existing reversed event identity lost.");
        Check(InternalWarReliefService.BattleSideFor(InternalWarTestService.Record, battle, ownerClan) == BattleSideEnum.Attacker,
            "Existing actual side was not respected.");
        Check(InternalWarReliefService.BattleSideFor(InternalWarTestService.Record, battle, new Clan()) == BattleSideEnum.None,
            "Neutral clan got battle side.");
        battle.IsSiegeOutside = false;
        Check(InternalWarReliefService.Find(battle) == null, "Non-relief event accepted.");
        Console.WriteLine("Relief service/prefix: " + _checks + " assertions passed; injected native boundaries.");
        return 0;
    }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string name, Type[] args) { } }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPrefix : Attribute { }
}
namespace TaleWorlds.Core { public enum BattleSideEnum { None = -1, Defender, Attacker } }
namespace TaleWorlds.CampaignSystem
{
    public sealed class Clan { public string StringId { get; set; } public static Clan PlayerClan { get; set; }
        public bool IsMinorFaction { get; set; } public bool IsClanTypeMercenary { get; set; } public bool IsUnderMercenaryService { get; set; } }
    public static class EncounterManager { public static void StartPartyEncounter(PartyBase first, PartyBase second) { } }
}
namespace TaleWorlds.CampaignSystem.Encounters { public sealed class PlayerEncounter { } }
namespace TaleWorlds.CampaignSystem.Party
{
    public sealed class PartyBase { public MobileParty MobileParty { get; set; } public MapEvent MapEvent { get; set; }
        public int NumberOfHealthyMembers { get; set; } = 10; }
    public sealed class MobileParty
    {
        public MobileParty() { Party = new PartyBase { MobileParty = this }; }
        public static MobileParty MainParty { get; set; } public string StringId { get; set; } public Clan Clan { get; set; }
        public PartyBase Party { get; } public bool IsActive { get; set; } = true; public bool IsLordParty { get; set; } = true;
        public bool IsCurrentlyAtSea { get; set; } public bool IsCaravan { get; set; } public bool IsVillager { get; set; }
        public Settlement CurrentSettlement { get; set; } public BesiegerCamp BesiegerCamp { get; set; }
        public object Army { get; set; } public MobileParty AttachedTo { get; set; } public List<MobileParty> AttachedParties { get; } = new List<MobileParty>();
    }
}
namespace TaleWorlds.CampaignSystem.Settlements
{
    public sealed class Settlement { public bool IsFortification { get; set; } public Clan OwnerClan { get; set; }
        public SiegeEvent SiegeEvent { get; set; } public PartyBase Party { get; } = new PartyBase(); }
}
namespace TaleWorlds.CampaignSystem.Siege
{
    public sealed class SiegeEvent { public Settlement BesiegedSettlement { get; set; } public BesiegerCamp BesiegerCamp { get; set; } }
    public sealed class BesiegerCamp { public SiegeEvent SiegeEvent { get; set; } public MobileParty LeaderParty { get; set; } }
}
namespace TaleWorlds.CampaignSystem.MapEvents
{
    public sealed class MapEvent { public bool IsSiegeOutside { get; set; } public Settlement MapEventSettlement { get; set; }
        public MapEventSide AttackerSide { get; set; } public MapEventSide DefenderSide { get; set; } }
    public sealed class MapEventSide { public PartyBase LeaderParty { get; set; } }
}
namespace AgesOfCalradiaInternalWarsTest
{
    internal enum InternalWarTestState { Preparing, Assaulting, RoyalPeacePending, Marching }
    internal enum InternalConflictPhase { Active, PeacePending }
    internal sealed class InternalWarTestRecord { internal string AttackerClanId, DefenderClanId, LeaderPartyId, ConflictId;
        internal InternalWarTestState State; internal bool CleanupComplete { get; set; } internal bool CaptureApplied { get; set; } }
    internal sealed class InternalConflictRecord { internal string ActiveOperationId; internal InternalConflictPhase Phase; }
    internal static class InternalWarTestService
    {
        internal static Settlement Target; internal static InternalWarTestRecord Record; internal static InternalConflictRecord War;
        internal static InternalWarTestRecord Find(Settlement target) { return target == Target ? Record : null; }
        internal static Clan ResolveClan(PartyBase party) { return party == null || party.MobileParty == null ? null : party.MobileParty.Clan; }
        internal static InternalConflictRecord FindConflict(Clan first, Clan second) { return War; }
    }
    internal static class InternalWarCombatService
    {
        internal static bool Opposed(Clan first, Clan second) { return first != second; }
        internal static bool IsLandLord(PartyBase party) { MobileParty p = party.MobileParty; return p != null && p.IsActive && p.IsLordParty
            && !p.IsCurrentlyAtSea && !p.IsCaravan && !p.IsVillager && p.CurrentSettlement == null && p.BesiegerCamp == null
            && p.Army == null && p.AttachedTo == null && p.AttachedParties.Count == 0 && party.MapEvent == null; }
    }
}
