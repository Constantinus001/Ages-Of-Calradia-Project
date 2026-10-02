using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
namespace AgesOfCalradiaInternalWarsTest
{
 internal static class StrategyProgram
 {
  private static int checks;private static Clan attacker,defender;private static MobileParty party;
  private static void Check(bool b,string why){checks++;if(!b)throw new Exception(why);}
  private sealed class Store:IDataStore {
   public bool IsLoading {get;set;}internal Dictionary<string,object> Values=new Dictionary<string,object>();
   public bool SyncData<T>(string key,ref T value){if(IsLoading){object v;if(!Values.TryGetValue(key,out v))return false;value=(T)v;}else Values[key]=value;return true;}
  }
  private static InternalWarTestBehavior Reset(){
   MobileParty.All.Clear();Settlement.All.Clear();InternalWarTestService.ReservedParties.Clear();InternalWarTestService.ReservedSettlements.Clear();
   attacker=new Clan{StringId="a"};defender=new Clan{StringId="d"};
   party=new MobileParty{StringId="npc",Clan=attacker};party.Party.NumberOfHealthyMembers=100;MobileParty.All.Add(party);
   MobileParty.MainParty=new MobileParty{StringId="player",Clan=attacker};CampaignTime.Now=new CampaignTime{ToHours=100};
   var owner=new InternalWarTestBehavior();owner.SetWar(new InternalConflictRecord{Id="war",KingdomId="realm",AttackerClanId="a",DefenderClanId="d",StartDay=0});return owner;
  }
  private static Settlement Target(string id,int distance,int strength,bool fort=true){
   var s=new Settlement{StringId=id,OwnerClan=defender,IsFortification=fort,GatePosition=new Position{X=distance}};s.Party.NumberOfHealthyMembers=strength;Settlement.All.Add(s);return s;
  }
  private static int Main(){
   var owner=Reset();Target("near-strong",1,1000);var far=Target("far-weak",10,20);new InternalWarSiegeAiCoordinator(owner).AdvanceAiSieges();
   Check(party.OrderedSiege==far,"Nearest strong target hid viable fortification");
   Check(owner.Conflict.GoalSettlementId==far.StringId&&owner.CurrentRecord.SettlementId==far.StringId,"Selected siege did not bind claim/record");
   owner=Reset();Target("near-strong",1,1000,false);far=Target("far-weak",10,20,false);new InternalWarSiegeAiCoordinator(owner).AdvanceAiSieges();
   Check(owner.OrderedRaid==far,"Nearest strong village hid viable raid");
   owner=Reset();var near=Target("near",1,20);far=Target("far",10,20);new InternalWarSiegeAiCoordinator(owner).AdvanceAiSieges();Check(party.OrderedSiege==near,"Nearest viable priority changed");
   owner=Reset();near=Target("near",1,20);far=Target("far",10,20);InternalWarTestService.ReservedSettlements.Add(near.StringId);
   new InternalWarSiegeAiCoordinator(owner).AdvanceAiSieges();Check(party.OrderedSiege==far,"Reserved target selected");
   owner=Reset();near=Target("huge",1,int.MaxValue);near.Parties.Add(new MobileParty{Clan=defender});near.Parties[0].Party.NumberOfHealthyMembers=int.MaxValue;
   new InternalWarSiegeAiCoordinator(owner).AdvanceAiSieges();Check(party.OrderedSiege==null,"Garrison sum overflow accepted impossible attack");
   owner=Reset();near=Target("reinforced",1,20);var reinforcement=new MobileParty{Clan=defender};reinforcement.Party.NumberOfHealthyMembers=100;near.Parties.Add(reinforcement);
   far=Target("far",10,20);new InternalWarSiegeAiCoordinator(owner).AdvanceAiSieges();Check(party.OrderedSiege==far,"Owner reinforcements ignored");
   owner=Reset();near=Target("claim",1,1000);far=Target("other",10,20);string why;owner.Conflict.TryAssignSettlementClaim(near.StringId,out why);
   new InternalWarSiegeAiCoordinator(owner).AdvanceAiSieges();Check(party.OrderedSiege==null,"Strategy abandoned unresolved claim");
   owner=Reset();Target("target",1,20);owner.RecoveryBlocker="unsafe";new InternalWarSiegeAiCoordinator(owner).AdvanceAiSieges();Check(party.OrderedSiege==null,"Recovery blocker bypassed");
   owner=Reset();Target("target",1,20);owner.HasActiveRaid=true;new InternalWarSiegeAiCoordinator(owner).AdvanceAiSieges();Check(party.OrderedSiege==null,"Active raid overlapped siege");
   owner=Reset();Target("target",1,20);owner.Conflict.RequestPeace();new InternalWarSiegeAiCoordinator(owner).AdvanceAiSieges();Check(party.OrderedSiege==null,"Peace pending launched operation");
   owner=Reset();Target("target",1,20);var ai=new InternalWarSiegeAiCoordinator(owner);ai.Defer();var save=new Store();ai.SyncData(save);
   var loaded=new InternalWarSiegeAiCoordinator(owner);save.IsLoading=true;loaded.SyncData(save);loaded.AdvanceAiSieges();Check(party.OrderedSiege==null,"Reload bypassed strategic backoff");
   CampaignTime.Now=new CampaignTime{ToHours=106};loaded.AdvanceAiSieges();Check(party.OrderedSiege!=null,"Persisted backoff never expired");
   foreach(double bad in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity,-1d}){
    owner=Reset();ai=new InternalWarSiegeAiCoordinator(owner);var invalid=new Store{IsLoading=true};invalid.Values["AOC_InternalConflict_NextStrategicHour"]=bad;ai.SyncData(invalid);
    Check(owner.RecoveryBlocker.Length>0,"Invalid saved schedule did not fail closed");
   }
   owner=Reset();ai=new InternalWarSiegeAiCoordinator(owner);ai.SyncData(new Store{IsLoading=true});Check(owner.RecoveryBlocker=="","Missing legacy schedule blocked campaign");
   owner=Reset();Target("target",1,20);ai=new InternalWarSiegeAiCoordinator(owner);ai.ToggleAutonomousAi();save=new Store();ai.SyncData(save);loaded=new InternalWarSiegeAiCoordinator(owner);save.IsLoading=true;loaded.SyncData(save);
   CampaignTime.Now=new CampaignTime{ToHours=200};loaded.AdvanceAiSieges();Check(party.OrderedSiege==null&&!loaded.AutonomousAi,"Disabled AI lost across reload");
   owner=Reset();Target("target",1,20);party.OnOrder=(p,s)=>s.OwnerClan=new Clan{StringId="foreign"};new InternalWarSiegeAiCoordinator(owner).AdvanceAiSieges();
   Check(owner.RecoveryBlocker.Length>0,"Native ownership mutation accepted");
   owner=Reset();Target("target",1,20);party.OnOrder=(p,s)=>p.Clan=defender;new InternalWarSiegeAiCoordinator(owner).AdvanceAiSieges();
   Check(owner.RecoveryBlocker.Length>0,"Native party-clan mutation accepted");
   owner=Reset();Target("target",1,20);party.OnOrder=(p,s)=>owner.Conflict.RequestPeace();new InternalWarSiegeAiCoordinator(owner).AdvanceAiSieges();
   Check(owner.RecoveryBlocker.Length>0,"Native peace mutation accepted");
   owner=Reset();Target("target",1,20);var replacement=new InternalWarTestRecord{ConflictId="replacement",State=InternalWarTestState.Preparing};
   party.OnOrder=(p,s)=>owner.CurrentRecord=replacement;new InternalWarSiegeAiCoordinator(owner).AdvanceAiSieges();
   Check(owner.RecoveryBlocker.Length>0&&owner.CurrentRecord==replacement&&replacement.State==InternalWarTestState.Preparing,"Replacement record mutated during recovery");
   owner=Reset();Target("target",1,20);party.OnOrder=(p,s)=>{throw new InvalidOperationException("native failure");};new InternalWarSiegeAiCoordinator(owner).AdvanceAiSieges();
   Check(owner.RecoveryBlocker.Length>0&&owner.CurrentRecord!=null,"Native movement failure did not preserve recovery state");
   Console.WriteLine("Actual strategic AI: "+checks+" assertions passed; native movement and raid boundaries injected.");return 0;
  }
 }
}
