using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
namespace TaleWorlds.CampaignSystem {
 public class Campaign {public static Campaign Current=new Campaign();}
 public class Kingdom {public string StringId="realm";}
 public class Clan {public string StringId;public Kingdom Kingdom;}
 public static class FactionManager {public static bool Hostile=true;public static bool IsAtWarAgainstFaction(Clan a,Clan b){return Hostile;}}
}
namespace TaleWorlds.CampaignSystem.Party {
 public class PartyBase {public Battle MapEvent {get;set;}}
 public class Battle {public string EventType="Siege";}
 public class Camp {public MobileParty LeaderParty;}
 public class MobileParty {public PartyBase Party=new PartyBase();public Camp BesiegerCamp {get;set;}}
}
namespace TaleWorlds.CampaignSystem.Settlements {
 public class Siege {public Camp BesiegerCamp;}
 public class Settlement {public Clan OwnerClan;public Siege SiegeEvent {get;set;}}
}
namespace AgesOfCalradiaInternalWarsTest {
 internal sealed class InternalWarTestBehavior {
  internal const int PeaceCompensationGold=5000;
  internal string RecoveryBlocker="";internal InternalConflictRecord Conflict;internal InternalWarTestRecord CurrentRecord;
  internal InternalWarRaidRecord Raid {get;set;}internal bool HasActiveRaid {get{return Raid!=null&&!Raid.Closed;}}
  internal readonly List<InternalWarTestBehavior> Controllers=new List<InternalWarTestBehavior>();
  internal static readonly Dictionary<string,Clan> Clans=new Dictionary<string,Clan>();
  internal static MobileParty Party;internal static Settlement Target;internal static string ThrowClan;
  internal static Clan FindClan(string id){if(id==ThrowClan)throw new Exception("native read");Clan c;return Clans.TryGetValue(id,out c)?c:null;}
  internal static MobileParty FindParty(string id){return Party;}
  internal static Settlement FindSettlement(string id){return Target;}
  internal InternalConflictRecord FindConflict(string a,string b){return Conflict!=null&&Conflict.IsOpen?Conflict:null;}
 }
 internal static class InternalWarCombatService {internal static bool IsBattleOfConflict(Battle battle,InternalConflictRecord war){return true;}}
 internal static partial class InternalWarDiagnosticsReport {
  internal sealed class DiagnosticSnapshot {
   internal readonly Dictionary<string,bool?> Checks=new Dictionary<string,bool?>();internal int Errors;
   internal void Check(string id,bool? ok,string detail){Checks[id]=ok;}
   internal void Read(string id,Action action){try{action();}catch(Exception){Errors++;}}
  }
  internal static DiagnosticSnapshot Run(InternalWarTestBehavior owner){var s=new DiagnosticSnapshot();ReadAcceptanceChecks(s,owner);return s;}
 }
 internal static class AcceptanceProgram {
  private static int count;
  private static void Check(bool b,string why){count++;if(!b)throw new Exception(why);}
  private static InternalWarTestBehavior Fixture(){
   InternalWarTestBehavior.Clans.Clear();InternalWarTestBehavior.ThrowClan=null;FactionManager.Hostile=true;
   var kingdom=new Kingdom();var a=new Clan{StringId="a",Kingdom=kingdom};var b=new Clan{StringId="b",Kingdom=kingdom};
   InternalWarTestBehavior.Clans.Add("a",a);InternalWarTestBehavior.Clans.Add("b",b);
   InternalWarTestBehavior.Party=new MobileParty();InternalWarTestBehavior.Target=new Settlement{OwnerClan=b};
   var owner=new InternalWarTestBehavior{Conflict=new InternalConflictRecord{Id="one",KingdomId="realm",AttackerClanId="a",DefenderClanId="b",ActiveOperationId="op"},
    CurrentRecord=new InternalWarTestRecord{ConflictId="op",KingdomId="realm",AttackerClanId="a",DefenderClanId="b",SettlementId="town",LeaderPartyId="party",State=InternalWarTestState.Preparing}};
   owner.Controllers.Add(owner);return owner;
  }
  private static int Main(){
   var owner=Fixture();var s=InternalWarDiagnosticsReport.Run(owner);
   Check(s.Checks["war.one.clan_contract"]==true,"Valid active contract not observed");
   Check(s.Checks["war.one.siege_camp"]==false,"Missing preparing camp not flagged");
   Check(s.Checks["war.one.capture_owner"]==null&&s.Checks["war.one.payment_receipt"]==null,"Unexercised capture/payment passed");
   Check(s.Checks["manual.ui_clicks"]==null&&s.Checks["manual.save_roundtrip"]==null,"Manual-only evidence passed");
   Check(s.Checks["session.concurrent_reservations"]==null,"Single war proved concurrency");
   InternalWarTestBehavior.Target.SiegeEvent=new Siege{BesiegerCamp=new Camp{LeaderParty=InternalWarTestBehavior.Party}};
   InternalWarTestBehavior.Party.BesiegerCamp=InternalWarTestBehavior.Target.SiegeEvent.BesiegerCamp;
   s=InternalWarDiagnosticsReport.Run(owner);Check(s.Checks["war.one.siege_camp"]==true,"Owned camp not observed");
   owner.CurrentRecord.CaptureApplied=true;s=InternalWarDiagnosticsReport.Run(owner);Check(s.Checks["war.one.capture_owner"]==false,"Wrong capture owner hidden");
   InternalWarTestBehavior.Target.OwnerClan=InternalWarTestBehavior.Clans["a"];s=InternalWarDiagnosticsReport.Run(owner);Check(s.Checks["war.one.capture_owner"]==true,"Matching capture owner not observed");
   owner.CurrentRecord.CleanupComplete=true;owner.CurrentRecord.State=InternalWarTestState.Captured;s=InternalWarDiagnosticsReport.Run(owner);
   Check(s.Checks["war.one.cleanup_residue"]==false,"Leftover camp hidden");
   InternalWarTestBehavior.Target.SiegeEvent=null;s=InternalWarDiagnosticsReport.Run(owner);Check(s.Checks["war.one.cleanup_residue"]==true,"Clean terminal observation missing");
   owner=Fixture();owner.Conflict.Phase=InternalConflictPhase.Closed;s=InternalWarDiagnosticsReport.Run(owner);Check(s.Checks["war.one.closed_peace"]==false,"Closed hostility hidden");
   FactionManager.Hostile=false;s=InternalWarDiagnosticsReport.Run(owner);Check(s.Checks["war.one.closed_peace"]==true,"Closed peace not observed");
   owner=Fixture();var child=new InternalWarTestBehavior{Conflict=new InternalConflictRecord{Id="two",KingdomId="realm",AttackerClanId="a",DefenderClanId="b",ActiveOperationId="op"},CurrentRecord=owner.CurrentRecord};
   owner.Controllers.Add(child);s=InternalWarDiagnosticsReport.Run(owner);Check(s.Checks.ContainsKey("war.two.clan_contract"),"Unselected controller not checked");
   Check(s.Checks["session.concurrent_reservations"]==false,"Duplicate reservation hidden");
   owner.Conflict.CompensationGold=5000;owner.Conflict.CompensationPayerId="a";s=InternalWarDiagnosticsReport.Run(owner);Check(s.Checks["war.one.payment_receipt"]==true,"Valid receipt not observed");
   owner.Conflict.CompensationPayerId="foreign";s=InternalWarDiagnosticsReport.Run(owner);Check(s.Checks["war.one.payment_receipt"]==false,"Invalid receipt hidden");
   owner.RecoveryBlocker="unsafe";s=InternalWarDiagnosticsReport.Run(owner);Check(s.Checks["session.recovery"]==false,"Recovery blocker hidden");
   InternalWarTestBehavior.ThrowClan="a";s=InternalWarDiagnosticsReport.Run(owner);Check(s.Errors==2&&s.Checks.ContainsKey("manual.ui_clicks"),"Native read failure erased unrelated evidence");
   Console.WriteLine("Automatic acceptance checks: "+count+" assertions passed; actual checks, native data adapters.");return 0;
  }
 }
}
