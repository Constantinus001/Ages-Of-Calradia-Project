using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
namespace TaleWorlds.CampaignSystem
{
 public class Kingdom { public string StringId; public Clan RulingClan; }
 public class Hero { public bool IsAlive=true; public float Relation; public float GetRelation(Hero other) { return Relation; } }
 public class Clan {
  public static Clan PlayerClan; public static readonly List<Clan> All=new List<Clan>();
  public string StringId; public string Name { get { return StringId; } } public Kingdom Kingdom; public Hero Leader=new Hero();
  public bool IsEliminated {get;set;} public bool IsMinorFaction {get;set;} public bool IsClanTypeMercenary {get;set;}
  public bool IsUnderMercenaryService {get;set;}
 }
 public struct CampaignTime { public static CampaignTime Now; public double ToDays; }
 public interface IDataStore { bool IsLoading {get;} bool SyncData<T>(string key,ref T data); }
 public static class FactionManager {
  internal static readonly HashSet<string> Wars=new HashSet<string>();
  internal static string Key(Clan a,Clan b) { return string.CompareOrdinal(a.StringId,b.StringId)<0 ? a.StringId+":"+b.StringId : b.StringId+":"+a.StringId; }
  public static bool IsAtWarAgainstFaction(Clan a,Clan b) { return Wars.Contains(Key(a,b)); }
 }
}
namespace TaleWorlds.CampaignSystem.Actions
{
 public static class DeclareWarAction {
  public static int Calls; public static Action<Clan,Clan> OnDeclare;
  public static void ApplyByDefault(Clan a,Clan b) { Calls++; FactionManager.Wars.Add(FactionManager.Key(a,b)); if(OnDeclare!=null) OnDeclare(a,b); }
 }
}
namespace AgesOfCalradiaInternalWarsTest
{
 internal sealed partial class InternalWarTestBehavior {
  private readonly InternalWarTestBehavior _root;
  private readonly InternalWarConflictRegistry _conflicts;
  private readonly Dictionary<string,InternalWarTestBehavior> _children;
  private InternalConflictRecord Conflict;
  internal string RecoveryBlocker=string.Empty;
  internal bool RoyalBan {get;set;}
  internal string GetRealmDeclarationBlocker(Kingdom kingdom) {return RoyalBan ? "royal ban" : string.Empty;}
  internal InternalWarTestBehavior() { _root=this; _conflicts=new InternalWarConflictRegistry(); _children=new Dictionary<string,InternalWarTestBehavior>(); }
  private InternalWarTestBehavior(InternalWarTestBehavior root,InternalConflictRecord war) { _root=root; _conflicts=root._conflicts; _children=root._children; Conflict=war; }
  private InternalWarTestBehavior ControllerFor(InternalConflictRecord war) {
   InternalWarTestBehavior value; if(!_children.TryGetValue(war.Id,out value)) {value=new InternalWarTestBehavior(_root,war);_children[war.Id]=value;} return value;
  }
  private void EndConflict(InternalWarTestState state,string reason) { Conflict.RequestPeace(); }
  private void BlockRecovery(string message) { RecoveryBlocker=message; }
  internal void Tick(double day) { CampaignTime.Now=new CampaignTime {ToDays=day}; AdvancePoliticalAi(); }
  internal void Add(InternalConflictRecord war) { if(!_conflicts.Add(war)) throw new Exception("Invalid fixture war"); }
  internal IEnumerable<InternalConflictRecord> Records {get{return _conflicts.Records;}}
 }
 internal static class InternalWarSuccessionBridge {
  internal static string PretenderClan;
  internal static bool HasDisputedClaim(Kingdom realm,Clan claimant,Clan rival) { return claimant.StringId==PretenderClan && rival==realm.RulingClan; }
 }
 internal static class InternalWarTestDiagnostics {
  internal static void Info(string text) { }
  internal static void Error(string text,Exception ex) { }
 }
}
