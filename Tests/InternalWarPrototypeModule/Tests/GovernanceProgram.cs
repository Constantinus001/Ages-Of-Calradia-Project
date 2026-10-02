using System;
using TaleWorlds.CampaignSystem;
namespace AgesOfCalradiaInternalWarsTest
{
 internal static class GovernanceProgram
 {
  private static int checks;
  private static void Check(bool ok,string why) {checks++;if(!ok)throw new Exception(why);}
  private sealed class Store : IDataStore {
   public bool IsLoading {get;set;} public bool IsSaving {get {return !IsLoading;}}
   internal string Payload="";
   public bool SyncData<T>(string key,ref T value) {
    if(key!="AOC_InternalConflict_RealmRules_v1")throw new Exception("Unexpected policy key");
    if(IsLoading)value=(T)(object)Payload;else Payload=(string)(object)value;return true;
   }
  }
  private static InternalWarTestBehavior Reset() {
   Clan.All.Clear();var realm=new Kingdom();Clan.PlayerClan=new Clan{Kingdom=realm};realm.RulingClan=Clan.PlayerClan;
   Clan.All.Add(Clan.PlayerClan);return new InternalWarTestBehavior();
  }
  private static InternalConflictRecord War(string id,string realm="realm") {
   return new InternalConflictRecord {Id=id,KingdomId=realm,AttackerClanId="a"+id,DefenderClanId="b"+id};
  }
  private static int Main() {
   var rules=InternalWarRealmRules.Deserialize("");Check(rules!=null&&!rules.IsBanned("realm"),"Legacy policy not permissive");
   rules.SetBanned("realm",true);rules.SetBanned("escaped\n%realm",true);
   var restored=InternalWarRealmRules.Deserialize(rules.Serialize());
   Check(restored!=null&&restored.IsBanned("realm")&&restored.IsBanned("escaped\n%realm"),"Escaped policy roundtrip failed");
   restored.SetBanned("realm",false);Check(!restored.IsBanned("realm")&&restored.IsBanned("escaped\n%realm"),"Repeal affected another kingdom");
   foreach(string bad in new[]{"v2","realm-rules-v1\n","realm-rules-v1\nx\nx","realm-rules-v1\n%GG","realm-rules-v1\n%20"})
    Check(InternalWarRealmRules.Deserialize(bad)==null,"Malformed policy accepted");
   var owner=Reset();string result;
   owner.SetRealmDeclarationBan("realm",false,true,out result);Check(owner.RealmDeclarationsBanned("realm"),"Ruler ban failed");
   Check(owner.GetRealmDeclarationBlocker(Clan.PlayerClan.Kingdom).Length>0,"Declaration ban unavailable");
   Check(owner.GetRealmDeclarationBlocker(new Kingdom{StringId="foreign"})=="","Ban leaked across kingdoms");
   owner.SetRealmDeclarationBan("realm",false,false,out result);Check(owner.RealmDeclarationsBanned("realm"),"Stale policy confirmation changed law");
   var child=owner.Child(War("child"));Check(child.RealmDeclarationsBanned("realm"),"Child did not use root policy");
   child.SetRealmDeclarationBan("realm",true,false,out result);Check(!owner.RealmDeclarationsBanned("realm"),"Child changed detached policy");
   owner.SetRealmDeclarationBan("realm",false,true,out result);var saved=new Store();owner.Sync(saved);
   var reloaded=Reset();saved.IsLoading=true;reloaded.Sync(saved);Check(reloaded.RealmDeclarationsBanned("realm"),"Save lost royal policy");
   Clan.PlayerClan.Kingdom.RulingClan=new Clan();reloaded.SetRealmDeclarationBan("realm",true,false,out result);
   Check(reloaded.RealmDeclarationsBanned("realm"),"Deposed ruler could repeal policy");
   owner=Reset();owner.SetRealmDeclarationBan("foreign",false,true,out result);Check(!owner.RealmDeclarationsBanned("foreign"),"Foreign policy changed");
   owner=Reset();Clan.PlayerClan.Leader.IsAlive=false;owner.SetRealmDeclarationBan("realm",false,true,out result);
   Check(!owner.RealmDeclarationsBanned("realm"),"Dead ruler issued policy");
   owner=Reset();var a=War("a");var b=War("b");var foreign=War("foreign","foreign");var closed=War("closed");closed.Phase=InternalConflictPhase.Closed;
   owner.Add(a);owner.Add(b);owner.Add(foreign);owner.Add(closed);
   owner.RequestRealmPeace("realm",out result);
   Check(a.Phase==InternalConflictPhase.PeacePending&&b.Phase==InternalConflictPhase.PeacePending,"Realm order missed local war");
   Check(foreign.Phase==InternalConflictPhase.Active&&closed.Phase==InternalConflictPhase.Closed,"Realm peace touched foreign/closed war");
   Check(owner.PeaceCalls==2,"Wrong controller dispatch count");owner.RequestRealmPeace("realm",out result);
   Check(owner.PeaceCalls==2,"Repeated order dispatched duplicate cleanup");
   Check(!owner.RealmDeclarationsBanned("realm"),"Peace silently changed declaration policy");
   owner=Reset();a=War("a");b=War("b");owner.Add(a);owner.Add(b);
   owner.OnPeace=w=>Clan.PlayerClan.Kingdom.RulingClan=new Clan();owner.RequestRealmPeace("realm",out result);
   Check(owner.PeaceCalls==1&&b.Phase==InternalConflictPhase.Active,"Lost authority did not stop remaining orders");
   owner=Reset();a=War("a");b=War("b");owner.Add(a);owner.Add(b);
   owner.OnPeace=w=>owner.BlockRecovery("native uncertainty");owner.RequestRealmPeace("realm",out result);
   Check(owner.PeaceCalls==1&&b.Phase==InternalConflictPhase.Active,"Recovery blocker did not stop remaining orders");
   Check(result.Contains("1 war(s) queued")&&result.Contains("native uncertainty"),"Partial order hid queued work or failure cause");
   owner=Reset();a=War("a");b=War("b");owner.Add(a);owner.Add(b);
   owner.OnPeace=w=>{string nested;owner.RequestRealmPeace("realm",out nested);};owner.RequestRealmPeace("realm",out result);
   Check(owner.PeaceCalls==2&&a.Phase==InternalConflictPhase.PeacePending&&b.Phase==InternalConflictPhase.PeacePending,"Reentrant order duplicated controller dispatch");
   owner=Reset();var malformed=new Store{IsLoading=true,Payload="invalid"};owner.Sync(malformed);
   Check(owner.RecoveryBlocker.Length>0,"Bad policy did not fail closed");var preserved=new Store();owner.Sync(preserved);
   Check(preserved.Payload=="invalid","Bad policy payload overwritten");owner.SetRealmDeclarationBan("realm",false,true,out result);
   Check(!owner.RealmDeclarationsBanned("realm"),"Blocked campaign changed policy");
   owner=Reset();owner.Add(War("one"));owner.Add(War("two","foreign"));
   Check(owner.GetRealmOverview().Contains("Open wars: 1")&&!owner.GetRealmOverview().Contains("atwo"),"Overview leaked foreign war");
   Clan.PlayerClan.Kingdom=null;Check(owner.GetRealmOverview().Contains("does not belong"),"Independent overview crashed");
   checks+=GovernanceMenuTests.Run();
   Console.WriteLine("Actual realm governance/menu: "+checks+" assertions passed; native peace and UI dispatch injected.");return 0;
  }
 }
}
