using System;
using AgesOfCalradiaInternalWarsTest;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.MapEvents;

internal static class RaidArrivalProgram
{
    private static int _checks;
    private static void Check(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidOperationException(message); }
    private static int Main()
    {
        Clan.PlayerClan = new Clan();
        var rival = new Clan();
        var village = new Settlement { IsVillage = true, OwnerClan = rival };
        var npc = new MobileParty();
        MobileParty.MainParty = new MobileParty { CurrentSettlement = village };
        Check(InternalWarRaidArrivalPatch.Prefix(npc, village), "Unregistered raid changed.");
        InternalWarTestService.RegisteredStart = true;
        Check(!InternalWarRaidArrivalPatch.Prefix(npc, village), "Neutral inside player was drafted.");
        village.OwnerClan = Clan.PlayerClan;
        Check(InternalWarRaidArrivalPatch.Prefix(npc, village), "Owner's native defense path blocked.");
        village.OwnerClan = rival;
        MobileParty.MainParty.CurrentSettlement = null;
        Check(InternalWarRaidArrivalPatch.Prefix(npc, village), "Free recorded NPC arrival blocked.");
        village.Party.MapEvent = new MapEvent();
        InternalWarTestService.RegisteredBattle = true;
        Check(!InternalWarRaidArrivalPatch.Prefix(npc, village), "NPC direct-side reassignment allowed.");
        Check(!InternalWarRaidArrivalPatch.Prefix(MobileParty.MainParty, village), "Neutral player joined private raid.");
        village.OwnerClan = Clan.PlayerClan;
        Check(InternalWarRaidArrivalPatch.Prefix(MobileParty.MainParty, village), "Player defense of own village blocked.");
        Check(!InternalWarRaidArrivalPatch.Prefix(npc, village), "NPC reinforcement bypassed side adapter requirement.");
        InternalWarTestService.RegisteredBattle = false;
        Check(InternalWarRaidArrivalPatch.Prefix(npc, village), "Native unrelated map event changed.");
        village.IsVillage = false;
        InternalWarTestService.RegisteredBattle = true;
        Check(InternalWarRaidArrivalPatch.Prefix(npc, village), "Fortification arrival changed.");
        Check(InternalWarRaidArrivalPatch.Prefix(null, village), "Null party fallback changed.");
        Check(InternalWarRaidArrivalPatch.Prefix(npc, null), "Null target fallback changed.");
        VerifyNpcReinforcements();
        Console.WriteLine("Raid arrival guard: " + _checks + " assertions passed; actual prefix with injected service boundaries.");
        return 0;
    }
    private static void VerifyNpcReinforcements()
    {
        var attackerClan = new Clan();
        var defenderClan = new Clan();
        var leader = new MobileParty { Clan = attackerClan };
        var village = new Settlement { IsVillage = true, OwnerClan = defenderClan };
        var battle = new MapEvent { AttackerSide = new MapEventSide { LeaderParty = leader.Party },
            DefenderSide = new MapEventSide { LeaderParty = village.Party } };
        village.Party.MapEvent = battle;
        InternalWarTestService.RegisteredBattle = true;
        InternalWarTestService.War = new InternalConflictRecord();
        foreach (bool attackers in new[] { true, false })
        {
            var joiner = new MobileParty { Clan = attackers ? attackerClan : defenderClan, IsLordParty = true };
            Check(!InternalWarRaidArrivalPatch.Prefix(joiner, village), "Native faction branch was not suppressed after custom join.");
            Check(joiner.Party.MapEventSide == (attackers ? battle.AttackerSide : battle.DefenderSide), "Reinforcement joined wrong clan side.");
        }
        foreach (string invalid in new[] { "neutral", "army", "sea", "committed", "attached", "peace", "refused", "finalized" })
        {
            var joiner = new MobileParty { Clan = attackerClan, IsLordParty = true };
            if (invalid == "neutral") joiner.Clan = new Clan();
            if (invalid == "army") joiner.Army = new object();
            if (invalid == "sea") joiner.IsCurrentlyAtSea = true;
            if (invalid == "committed") joiner.Party.MapEvent = new MapEvent();
            if (invalid == "attached") joiner.AttachedTo = leader;
            if (invalid == "peace") InternalWarTestService.War.Phase = InternalConflictPhase.PeacePending;
            if (invalid == "refused") battle.JoinAllowed = false;
            if (invalid == "finalized") battle.IsFinalized = true;
            Check(!InternalWarRaidArrivalPatch.Prefix(joiner, village) && joiner.Party.MapEventSide == null,
                "Unsupported reinforcement accepted: " + invalid);
            InternalWarTestService.War.Phase = InternalConflictPhase.Active;
            battle.JoinAllowed = true; battle.IsFinalized = false;
        }
        var interrupted = new MobileParty { Clan = attackerClan, IsLordParty = true };
        battle.OnJoinCheck = () => InternalWarTestService.War.Phase = InternalConflictPhase.PeacePending;
        Check(!InternalWarRaidArrivalPatch.Prefix(interrupted, village) && interrupted.Party.MapEventSide == null,
            "Peace callback during join authorization was ignored.");
        InternalWarTestService.War.Phase = InternalConflictPhase.Active;
        foreach (string changed in new[] { "party clan", "owner", "leader clan" })
        {
            var joiner = new MobileParty { Clan = attackerClan, IsLordParty = true };
            battle.OnJoinCheck = () =>
            {
                if (changed == "party clan") joiner.Clan = new Clan();
                if (changed == "owner") village.OwnerClan = new Clan();
                if (changed == "leader clan") leader.Clan = new Clan();
            };
            Check(!InternalWarRaidArrivalPatch.Prefix(joiner, village) && joiner.Party.MapEventSide == null,
                "Identity changed during callback but joined anyway: " + changed);
            village.OwnerClan = defenderClan; leader.Clan = attackerClan;
        }
        battle.OnJoinCheck = null;
        var failing = new MobileParty { Clan = attackerClan, IsLordParty = true };
        failing.Party.OnSetSide = () => { throw new InvalidOperationException("Injected native callback failure"); };
        Check(!InternalWarRaidArrivalPatch.Prefix(failing, village) && InternalWarTestService.RecoveryBlocked,
            "Native setter failure did not block recovery.");
        int calls = failing.Party.SetterCalls;
        Check(!InternalWarRaidArrivalPatch.Prefix(failing, village) && failing.Party.SetterCalls == calls,
            "Uncertain native setter was retried.");
    }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch : Attribute
    { public HarmonyPatch(Type type, string name, Type[] arguments) { } }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPrefix : Attribute { }
}
namespace TaleWorlds.CampaignSystem
{
    public sealed class Clan { public static Clan PlayerClan { get; set; } }
    public static class EncounterManager { public static void StartSettlementEncounter(MobileParty party, Settlement settlement) { } }
}
namespace TaleWorlds.CampaignSystem.Party
{
    public sealed class MobileParty { public static MobileParty MainParty { get; set; } public Settlement CurrentSettlement { get; set; }
        public MobileParty() { Party = new PartyBase { MobileParty = this }; }
        public PartyBase Party { get; } public Clan Clan { get; set; } public bool IsLordParty { get; set; }
        public bool IsCurrentlyAtSea { get; set; } public object Army { get; set; } public MobileParty AttachedTo { get; set; } }
    public sealed class PartyBase { public MapEvent MapEvent { get; set; } public MobileParty MobileParty { get; set; }
        private MapEventSide _side;
        public Action OnSetSide { get; set; } public int SetterCalls { get; private set; }
        public MapEventSide MapEventSide { get { return _side; } set { SetterCalls++; if (OnSetSide != null) OnSetSide(); _side = value; } } }
}
namespace TaleWorlds.CampaignSystem.Settlements
{
    public sealed class Settlement { public bool IsVillage { get; set; } public Clan OwnerClan { get; set; }
        public PartyBase Party { get; } = new PartyBase(); }
}
namespace TaleWorlds.Core { public enum BattleSideEnum { None = -1, Defender, Attacker } }
namespace TaleWorlds.CampaignSystem.MapEvents
{
    public sealed class MapEvent { public bool IsFinalized { get; set; } public bool JoinAllowed { get; set; } = true;
        public Action OnJoinCheck { get; set; } public MapEventSide AttackerSide { get; set; } public MapEventSide DefenderSide { get; set; }
        public bool CanPartyJoinBattle(PartyBase party, TaleWorlds.Core.BattleSideEnum side) { if (OnJoinCheck != null) OnJoinCheck(); return JoinAllowed; } }
    public sealed class MapEventSide { public PartyBase LeaderParty { get; set; } }
}
namespace AgesOfCalradiaInternalWarsTest
{
    internal enum InternalConflictPhase { Active, PeacePending }
    internal sealed class InternalConflictRecord { internal InternalConflictPhase Phase { get; set; } }
    internal static class InternalWarTestService
    {
        internal static InternalConflictRecord War;
        internal static bool RecoveryBlocked;
        internal static void BlockRecovery(string reason) { RecoveryBlocked = true; }
        internal static bool RegisteredBattle;
        internal static bool RegisteredStart;
        internal static bool IsRaidBattle(MapEvent battle) { return RegisteredBattle; }
        internal static bool CanStartNpcRaid(MobileParty party, Settlement settlement) { return RegisteredStart; }
        internal static InternalConflictRecord FindRaidConflict(MapEvent battle) { return War; }
        internal static Clan ResolveClan(MobileParty party) { return party == null ? null : party.Clan; }
        internal static Clan ResolveClan(PartyBase party) { return party == null ? null : ResolveClan(party.MobileParty); }
    }
    internal static class InternalWarCombatService
    {
        internal static bool IsLandLord(PartyBase party) { return party != null && party.MobileParty != null
            && party.MobileParty.IsLordParty && !party.MobileParty.IsCurrentlyAtSea && party.MobileParty.Army == null
            && party.MobileParty.AttachedTo == null && party.MapEvent == null; }
        internal static bool Opposed(Clan first, Clan second) { return !InternalWarTestService.RecoveryBlocked && first != null && second != null && first != second; }
    }
    internal static class InternalWarTestDiagnostics { internal static void Info(string message) { }
        internal static void Error(string message, Exception error) { } }
}
