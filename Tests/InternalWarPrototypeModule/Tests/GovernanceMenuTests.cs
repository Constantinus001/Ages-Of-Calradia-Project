using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.Core;
namespace TaleWorlds.CampaignSystem.GameMenus
{
 public class GameMenuOption {public enum LeaveType {Submenu}}
 public class MenuCallbackArgs {public bool IsEnabled;public GameMenuOption.LeaveType optionLeaveType;public TaleWorlds.Localization.TextObject Tooltip;}
}
namespace TaleWorlds.Localization {public class TextObject {public TextObject(string s) {}}}
namespace TaleWorlds.Library
{
 public class InquiryData {public Action Confirm;public InquiryData(string title,string text,bool a,bool b,string yes,string no,Action confirm,Action cancel) {Confirm=confirm;}}
 public class InformationMessage {public InformationMessage(string s) {}}
 public static class InformationManager {
  public static InquiryData Last;
  public static void ShowInquiry(InquiryData data,bool pause) {Last=data;}
  public static void DisplayMessage(InformationMessage message) {}
 }
}
namespace TaleWorlds.Core
{
 public class InquiryElement {public object Identifier; public InquiryElement(object id,string text,object image) {Identifier=id;}}
 public class MultiSelectionInquiryData {
  public Action<List<InquiryElement>> Confirm;
  public MultiSelectionInquiryData(string title,string text,List<InquiryElement> items,bool exit,int min,int max,string yes,string no,
   Action<List<InquiryElement>> confirm,Action<List<InquiryElement>> cancel) {Confirm=confirm;}
 }
 public static class MBInformationManager {public static MultiSelectionInquiryData Last;public static void ShowMultiSelectionInquiry(MultiSelectionInquiryData d,bool pause) {Last=d;}}
}
namespace TaleWorlds.CampaignSystem
{
 public class CampaignGameStarter {
  public sealed class Entry {public Func<MenuCallbackArgs,bool> Condition;public Action<MenuCallbackArgs> Execute;}
  public readonly Dictionary<string,Entry> Entries=new Dictionary<string,Entry>();
  public void AddGameMenuOption(string menu,string id,string label,Func<MenuCallbackArgs,bool> condition,Action<MenuCallbackArgs> execute,bool leave,int index) {
   Entries.Add(id,new Entry{Condition=condition,Execute=execute});
  }
 }
}
namespace AgesOfCalradiaInternalWarsTest
{
 internal static class GovernanceMenuTests
 {
  private static int checks;
  private static void Check(bool b,string why) {checks++;if(!b)throw new Exception(why);}
  internal static int Run() {
   var realm=new Kingdom();Clan.PlayerClan=new Clan{Kingdom=realm};realm.RulingClan=Clan.PlayerClan;
   var owner=new InternalWarTestBehavior();var current=owner;var starter=new CampaignGameStarter();
   InternalWarRealmMenu.Register(starter,owner,()=>current);Check(starter.Entries.Count==8,"Missing realm menu registrations");
   var entry=starter.Entries["aoc_realm_controls_town"];var args=new MenuCallbackArgs();entry.Condition(args);
   Check(args.IsEnabled,"Current ruler cannot access controls");entry.Execute(args);
   MBInformationManager.Last.Confirm(new List<InquiryElement>{new InquiryElement("policy","",null)});
   realm.RulingClan=new Clan();TaleWorlds.Library.InformationManager.Last.Confirm();
   Check(!owner.RealmDeclarationsBanned("realm"),"Stale ruler confirmation applied policy");
   entry.Condition(args);Check(!args.IsEnabled,"Deposed ruler controls remain enabled");
   realm.RulingClan=Clan.PlayerClan;entry.Execute(args);
   MBInformationManager.Last.Confirm(new List<InquiryElement>{new InquiryElement("policy","",null)});
   current=new InternalWarTestBehavior();TaleWorlds.Library.InformationManager.Last.Confirm();
   Check(!owner.RealmDeclarationsBanned("realm"),"Old campaign dialog mutated old owner");
   current=owner;entry.Execute(args);MBInformationManager.Last.Confirm(new List<InquiryElement>{new InquiryElement("policy","",null)});
   TaleWorlds.Library.InformationManager.Last.Confirm();Check(owner.RealmDeclarationsBanned("realm"),"Confirmed policy not applied");
   var war=new InternalConflictRecord{Id="war",KingdomId="realm",AttackerClanId="a",DefenderClanId="b"};owner.Add(war);
   entry.Execute(args);MBInformationManager.Last.Confirm(new List<InquiryElement>{new InquiryElement("peace","",null)});
   Check(war.Phase==InternalConflictPhase.Active,"Menu queued peace without final confirmation");
   TaleWorlds.Library.InformationManager.Last.Confirm();Check(war.Phase==InternalConflictPhase.PeacePending,"Confirmed realm peace not dispatched");
   current=new InternalWarTestBehavior();starter.Entries["aoc_realm_overview_town"].Condition(args);
   Check(!args.IsEnabled,"Old campaign overview remained enabled");return checks;
  }
 }
}
