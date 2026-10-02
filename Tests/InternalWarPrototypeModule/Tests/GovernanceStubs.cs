using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
namespace TaleWorlds.CampaignSystem
{
 public class Kingdom { public string StringId = "realm"; public string Name {get {return StringId;}} public Clan RulingClan; }
 public class Hero { public bool IsAlive = true; }
 public class Clan {
  public static Clan PlayerClan; public static readonly List<Clan> All = new List<Clan>();
  public string StringId = "player"; public string Name {get {return StringId;}}
  public Kingdom Kingdom; public Hero Leader = new Hero(); public bool IsEliminated {get;set;}
 }
 public interface IDataStore { bool IsLoading {get;} bool IsSaving {get;} bool SyncData<T>(string key,ref T value); }
}
namespace AgesOfCalradiaInternalWarsTest
{
 internal static class InternalWarTestDiagnostics { internal static void Info(string s) {} }
 internal sealed partial class InternalWarTestBehavior {
  private readonly InternalWarTestBehavior _root;
  private readonly List<InternalConflictRecord> _wars;
  private readonly Dictionary<string,InternalWarTestBehavior> _children;
  private InternalConflictRecord _war;
  internal string RecoveryBlocker {get {return _root._blocker;}}
  private string _blocker = "";
  internal bool PoliticalAiEnabled {get {return true;}}
  internal IEnumerable<InternalConflictRecord> Conflicts {get {return _root._wars;}}
  internal Action<InternalConflictRecord> OnPeace {get;set;}
  internal int PeaceCalls {get;private set;}
  internal InternalWarTestBehavior() {_root=this; _wars=new List<InternalConflictRecord>(); _children=new Dictionary<string,InternalWarTestBehavior>();}
  private InternalWarTestBehavior(InternalWarTestBehavior root, InternalConflictRecord war) {_root=root; _war=war; _wars=root._wars; _children=root._children;}
  private InternalWarTestBehavior ControllerFor(InternalConflictRecord war) {
   InternalWarTestBehavior child;
   if (!_children.TryGetValue(war.Id,out child)) {_children[war.Id]=child=new InternalWarTestBehavior(_root,war);}
   return child;
  }
  internal InternalWarTestBehavior Child(InternalConflictRecord war) {return ControllerFor(war);}
  internal void Add(InternalConflictRecord war) {_root._wars.Add(war);}
  internal void BlockRecovery(string reason) {if (_root._blocker.Length==0) _root._blocker=reason;}
  internal void Sync(IDataStore store) {SyncRealmGovernance(store);}
  private static Clan FindClan(string id) {return Clan.All.Find(c=>c.StringId==id);}
  private void RequestRoyalPeace(bool monarch,out string result) {
   if (!monarch) throw new Exception("Realm order skipped native ruler authority");
   _root.PeaceCalls++; _war.RequestPeace();
   if (_root.OnPeace!=null) _root.OnPeace(_war); result="queued";
  }
 }
}
