// Explicit data and fault-injection boundaries only. No Bannerlord/native lifecycle is simulated.
using System;
using System.Collections.Generic;
using System.Linq;

namespace TaleWorlds.Core
{
    public enum BattleSideEnum { None = -1, Defender, Attacker }
    public sealed class Game
    {
        public static Game Current { get; set; }
        public GameStateManager GameStateManager { get; } = new GameStateManager();
    }
    public sealed class GameStateManager { public object ActiveState { get; set; } }
}
namespace TaleWorlds.MountAndBlade
{
    public sealed class Mission { public static Mission Current { get; set; } }
}
namespace TaleWorlds.CampaignSystem.GameState { public sealed class MapState { } }
namespace TaleWorlds.CampaignSystem
{
    public sealed class Kingdom { public string StringId { get; set; } }
    public sealed class Clan
    {
        public static Clan PlayerClan { get; set; }
        public string StringId { get; set; }
        public Kingdom Kingdom { get; set; }
    }
    public struct CampaignTime
    {
        public static CampaignTime Now { get; set; }
        public double ToDays { get; set; }
    }
    public interface IDataStore
    {
        bool IsSaving { get; }
        bool IsLoading { get; }
        void SyncData(string key, ref string value);
    }
}
namespace TaleWorlds.CampaignSystem.Party
{
    public enum AiBehavior { Hold, RaidSettlement }
    public sealed class PartyBase
    {
        public MobileParty MobileParty { get; set; }
        public Settlements.Settlement Settlement { get; set; }
        public MapEvents.MapEvent MapEvent { get; set; }
        public int NumberOfHealthyMembers { get; set; }
    }
    public sealed class MobileParty
    {
        public enum NavigationType { Default }
        public AiBehavior DefaultBehavior { get; set; }
        public AiBehavior ShortTermBehavior { get; set; }
        public Settlements.Settlement TargetSettlement { get; set; }
        public Settlements.Settlement ShortTermTargetSettlement { get; set; }
        public Action OnRaidOrder { get; set; }
        public int HoldCalls { get; private set; }
        public void SetMoveRaidSettlement(Settlements.Settlement target, NavigationType navigation, bool port)
        {
            TargetSettlement = target; DefaultBehavior = AiBehavior.RaidSettlement;
            ShortTermTargetSettlement = target; ShortTermBehavior = AiBehavior.RaidSettlement;
            if (OnRaidOrder != null) OnRaidOrder();
        }
        public Action OnNextHold { get; set; }
        public void SetMoveModeHold() {
            HoldCalls++; Action callback = OnNextHold; OnNextHold = null;
            if (callback != null) callback();
            DefaultBehavior = AiBehavior.Hold;
        }
        public MobileParty() { Party = new PartyBase { MobileParty = this }; }
        public static MobileParty MainParty { get; set; }
        public string StringId { get; set; }
        public Clan ActualClan { get; set; }
        public PartyBase Party { get; }
        public object Army { get; set; }
        public object BesiegerCamp { get; set; }
        public bool IsCurrentlyAtSea { get; set; }
        public bool IsActive { get; set; }
        public Settlements.Settlement CurrentSettlement { get; set; }
    }
}
namespace TaleWorlds.CampaignSystem.Settlements
{
    public sealed class Settlement
    {
        public Settlement() { Party = new Party.PartyBase { Settlement = this }; }
        public static Settlement CurrentSettlement { get; set; }
        public string StringId { get; set; }
        public bool IsVillage { get; set; }
        public Clan OwnerClan { get; set; }
        public Party.PartyBase Party { get; }
        public Village Village { get; } = new Village();
    }
    public sealed class Village
    {
        public enum VillageStates { Normal, BeingRaided, Looted }
        public VillageStates VillageState { get; set; }
    }
}
namespace TaleWorlds.CampaignSystem.MapEvents
{
    public sealed class MapEvent
    {
        public bool IsRaid { get; set; }
        public MapEventSide AttackerSide { get; set; }
        public Settlements.Settlement MapEventSettlement { get; set; }
        public bool DiplomaticallyFinished { get; set; }
    }
    public sealed class MapEventSide { public Party.PartyBase LeaderParty { get; set; } }
}
namespace TaleWorlds.CampaignSystem.Encounters
{
    public sealed class PlayerEncounter
    {
        public static PlayerEncounter Current { get; set; }
        public static Settlements.Settlement EncounterSettlement { get; set; }
        public static MapEvents.MapEvent Battle { get; set; }
        public bool ForceRaid { get; set; }
        public TaleWorlds.Core.BattleSideEnum PlayerSide { get; set; }
        public int SetupCalls { get; private set; }
        public Action OnNextSetup { get; set; }
        public void SetupFields(Party.PartyBase attacker, Party.PartyBase defender)
        {
            SetupCalls++;
            // Tests assume successful native/Harmony side assignment; they do not prove that integration.
            PlayerSide = TaleWorlds.Core.BattleSideEnum.Attacker;
            Action callback = OnNextSetup;
            OnNextSetup = null;
            if (callback != null) callback();
        }
    }
}
namespace TaleWorlds.CampaignSystem.Actions
{
    public static class BeHostileAction
    {
        public static int Calls { get; set; }
        public static Action OnNextApply { get; set; }
        public static void ApplyEncounterHostileAction(Party.PartyBase attacker, Party.PartyBase defender)
        {
            Calls++;
            Action callback = OnNextApply;
            OnNextApply = null;
            if (callback != null) callback();
        }
    }
}
namespace TaleWorlds.CampaignSystem.GameMenus
{
    public static class GameMenu
    {
        public static int SwitchCalls { get; set; }
        public static string LastMenu { get; set; }
        public static Action OnNextSwitch { get; set; }
        public static void SwitchToMenu(string id)
        {
            SwitchCalls++;
            LastMenu = id;
            Action callback = OnNextSwitch;
            OnNextSwitch = null;
            if (callback != null) callback();
        }
    }
}
namespace AgesOfCalradiaInternalWarsTest
{
    internal sealed class InternalWarTestBehavior
    {
        internal InternalConflictRecord Conflict { get; set; }
        internal InternalWarTestRecord CurrentRecord { get; set; }
        internal string RecoveryBlocker { get; set; } = string.Empty;
        internal static List<TaleWorlds.CampaignSystem.Settlements.Settlement> Settlements { get; } = new List<TaleWorlds.CampaignSystem.Settlements.Settlement>();
        internal static List<TaleWorlds.CampaignSystem.Party.MobileParty> Parties { get; } = new List<TaleWorlds.CampaignSystem.Party.MobileParty>();
        internal void BlockRecovery(string message) { RecoveryBlocker = message; }
        internal static TaleWorlds.CampaignSystem.Settlements.Settlement FindSettlement(string id)
        { return Settlements.FirstOrDefault(value => value.StringId == id); }
        internal static TaleWorlds.CampaignSystem.Party.MobileParty FindParty(string id)
        { return Parties.FirstOrDefault(value => value.StringId == id); }
    }
    internal static class InternalWarTestService
    {
        internal static bool PartyReserved { get; set; }
        internal static bool SettlementReserved { get; set; }
        internal static bool PartyAssigned(string id) { return PartyReserved; }
        internal static bool SettlementAssigned(string id) { return SettlementReserved; }
        internal static TaleWorlds.CampaignSystem.Clan ResolveClan(TaleWorlds.CampaignSystem.Party.PartyBase party)
        { return party == null || party.MobileParty == null ? null : party.MobileParty.ActualClan; }
    }
    internal static class InternalWarCombatService
    {
        internal static bool IsLandLord(TaleWorlds.CampaignSystem.Party.PartyBase party)
        { return party != null && party.MobileParty != null && party.MobileParty.IsActive
            && party.MobileParty.Army == null && party.MobileParty.BesiegerCamp == null
            && !party.MobileParty.IsCurrentlyAtSea && party.MobileParty.CurrentSettlement == null && party.MapEvent == null; }
        // Political eligibility is separately tested against its actual source. Here it is an injected boundary.
        internal static bool OpposedResult { get; set; }
        internal static bool Opposed(TaleWorlds.CampaignSystem.Clan first, TaleWorlds.CampaignSystem.Clan second)
        { return OpposedResult; }
    }
    internal static class InternalWarTestDiagnostics
    {
        internal static List<string> Messages { get; } = new List<string>();
        internal static List<Exception> Errors { get; } = new List<Exception>();
        internal static void Info(string message) { Messages.Add(message); }
        internal static void Error(string message, Exception exception) { Messages.Add(message); Errors.Add(exception); }
    }
}
