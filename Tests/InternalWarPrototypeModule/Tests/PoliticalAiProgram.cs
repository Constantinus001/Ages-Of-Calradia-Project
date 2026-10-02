using System;
using System.Linq;
using AgesOfCalradiaInternalWarsTest;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
internal static class PoliticalAiProgram
{
 static int checks;
 static void Check(bool value,string text) {checks++;if(!value)throw new Exception(text);}
 static Clan Add(string id,Kingdom realm,float relation=0) {var c=new Clan{StringId=id,Kingdom=realm};c.Leader.Relation=relation;Clan.All.Add(c);return c;}
 static InternalWarTestBehavior Reset(out Kingdom realm,out Clan npc) {
  Clan.All.Clear();FactionManager.Wars.Clear();DeclareWarAction.Calls=0;DeclareWarAction.OnDeclare=null;InternalWarSuccessionBridge.PretenderClan=null;
  realm=new Kingdom{StringId="realm"};Clan.PlayerClan=Add("player",realm);realm.RulingClan=Clan.PlayerClan;
  npc=Add("a",realm);return new InternalWarTestBehavior();
 }
 static InternalConflictRecord War(string id,string attacker,string defender,int day=0,string realm="realm") {
  return new InternalConflictRecord{Id=id,AttackerClanId=attacker,DefenderClanId=defender,KingdomId=realm,StartDay=day};
 }
 static int Main() {
  try {
   Kingdom realm;Clan npc;var owner=Reset(out realm,out npc);npc.Leader.Relation=-40;
   owner.Tick(1);Check(DeclareWarAction.Calls==1,"Hostile NPC did not declare.");
   Add("b",realm,-50);owner.Tick(2);Check(DeclareWarAction.Calls==1,"Weekly schedule ran early.");
   owner.Tick(8);Check(DeclareWarAction.Calls==2,"Weekly declaration did not resume.");
   owner=Reset(out realm,out npc);npc.Leader.Relation=-50;
   for(int i=0;i<4;i++)owner.Add(War("cap"+i,"x"+i,"y"+i,0));
   owner.Tick(1);Check(DeclareWarAction.Calls==0,"Four-war cap bypassed.");
   owner=Reset(out realm,out npc);npc.Leader.Relation=-50;
   var old=War("old","a","player");old.Phase=InternalConflictPhase.Closed;owner.Add(old);
   owner.Tick(89);Check(DeclareWarAction.Calls==0,"Pair cooldown bypassed.");
   owner.Tick(96);Check(DeclareWarAction.Calls==1,"Expired pair cooldown did not release.");
   owner=Reset(out realm,out npc);owner.Tick(1);Check(DeclareWarAction.Calls==0,"Neutral NPC declared without reason.");
   InternalWarSuccessionBridge.PretenderClan=npc.StringId;owner.Tick(8);
   Check(DeclareWarAction.Calls==1,"Succession dispute did not permit declaration.");
   owner=Reset(out realm,out npc);var ruler=Add("ruler",realm);realm.RulingClan=ruler;
   var war=War("royal","a","player");owner.Add(war);owner.TogglePoliticalAi();owner.Tick(30);
   Check(war.Phase==InternalConflictPhase.PeacePending,"Declaration toggle disabled NPC monarch peace.");
   Check(DeclareWarAction.Calls==0,"Disabled declaration toggle allowed war.");
   owner=Reset(out realm,out npc);realm.RulingClan=npc;war=War("involved","a","player");owner.Add(war);owner.Tick(30);
   Check(war.Phase==InternalConflictPhase.Active,"Participating NPC ruler arbitrated its own war.");
   owner=Reset(out realm,out npc);war=War("playerking","a","player");owner.Add(war);owner.Tick(30);
   Check(war.Phase==InternalConflictPhase.Active,"Player ruler authority was automated.");
   owner=Reset(out realm,out npc);war=War("foreign","a","b",0,"oldrealm");owner.Add(war);
   realm.RulingClan=Add("newruler",realm);owner.Tick(30);
   Check(war.Phase==InternalConflictPhase.Active,"New kingdom's politics ended a former kingdom war.");
   owner=Reset(out realm,out npc);var another=Add("b",realm);war=War("negotiated","a",another.StringId);owner.Add(war);owner.TogglePoliticalAi();owner.Tick(14);
   Check(war.Phase==InternalConflictPhase.PeacePending,"Disabled declarations prevented existing NPC negotiation.");
   owner=Reset(out realm,out npc);npc.Leader.Relation=-50;DeclareWarAction.OnDeclare=(a,b)=>{a.Kingdom=null;};owner.Tick(1);
   Check(owner.Records.Single().Phase==InternalConflictPhase.PeacePending,"Native declaration defection did not queue recovery.");
   owner=Reset(out realm,out npc);npc.Leader.Relation=-50;owner.RoyalBan=true;owner.Tick(1);
   Check(DeclareWarAction.Calls==0,"NPC declaration bypassed royal policy.");
   owner.RoyalBan=false;owner.Tick(8);Check(DeclareWarAction.Calls==1,"Repealed policy still blocked NPC declarations.");
   owner=Reset(out realm,out npc);owner.RoyalBan=true;realm.RulingClan=Add("ruler",realm);
   war=War("royalban","a","player");owner.Add(war);owner.Tick(30);
   Check(war.Phase==InternalConflictPhase.PeacePending,"Royal ban stopped existing war peace.");
   Console.WriteLine("Actual political AI passed: "+checks+" assertions; native callbacks and peace transition injected.");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
 }
}
