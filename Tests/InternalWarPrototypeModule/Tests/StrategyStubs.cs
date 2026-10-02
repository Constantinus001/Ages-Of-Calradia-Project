using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
namespace TaleWorlds.CampaignSystem
{
 public class Clan {public string StringId;}
 public struct CampaignTime {public static CampaignTime Now;public double ToHours;public double ToDays {get {return ToHours/24;}}}
 public interface IDataStore {bool IsLoading {get;} bool SyncData<T>(string key,ref T value);}
 public struct Position {public int X;public Position ToVec2(){return this;}public double DistanceSquared(Position p){return (double)(X-p.X)*(X-p.X);}}
}
namespace TaleWorlds.CampaignSystem.Party
{
 public class PartyBase {public int NumberOfHealthyMembers;public object MapEvent {get;set;}public MobileParty MobileParty;}
 public class MobileParty {
  public static readonly List<MobileParty> All=new List<MobileParty>();public static MobileParty MainParty;
  public enum NavigationType {Default}
  public string StringId;public Clan Clan;public PartyBase Party;public Position Position;public bool Eligible=true;
  public Settlement OrderedSiege {get;private set;}
  public Action<MobileParty,Settlement> OnOrder {get;set;}
  public MobileParty(){Party=new PartyBase {MobileParty=this};}
  public void SetMoveBesiegeSettlement(Settlement s,NavigationType n){OrderedSiege=s;if(OnOrder!=null)OnOrder(this,s);}
 }
}
namespace TaleWorlds.CampaignSystem.Settlements
{
 public class Village {public enum VillageStates {Normal,Raided}public VillageStates VillageState {get;set;}}
 public class Settlement {
  public static readonly List<Settlement> All=new List<Settlement>();public string StringId;public Clan OwnerClan;
  public bool IsFortification;public bool IsVillage {get {return !IsFortification;}}public object SiegeEvent {get;set;}
  public PartyBase Party=new PartyBase();public List<MobileParty> Parties=new List<MobileParty>();public Position GatePosition;
  public Village Village=new Village();
 }
}
namespace AgesOfCalradiaInternalWarsTest
{
 internal static class InternalWarTestDiagnostics {internal static void Info(string s){}internal static void Error(string s,Exception e){}}
 internal static class InternalWarTestService {
  internal static readonly HashSet<string> ReservedParties=new HashSet<string>(),ReservedSettlements=new HashSet<string>();
  internal static bool PartyAssigned(string id){return ReservedParties.Contains(id);}
  internal static bool SettlementAssigned(string id){return ReservedSettlements.Contains(id);}
  internal static Clan ResolveClan(MobileParty p){return p==null?null:p.Clan;}
 }
 internal static class InternalWarCombatService {
  internal static bool IsLandLord(PartyBase p){return p!=null&&p.MobileParty!=null&&p.MobileParty.Eligible;}
  internal static bool Opposed(Clan a,Clan b){return a!=null&&b!=null&&a!=b;}
 }
 internal sealed class CleanupStub {internal void Reset(){}}
 internal sealed partial class InternalWarTestBehavior {
  private readonly InternalWarConflictRegistry _conflicts=new InternalWarConflictRegistry();private readonly CleanupStub _cleanup=new CleanupStub();
  internal InternalConflictRecord Conflict;internal InternalWarTestRecord CurrentRecord {get;set;}
  internal bool HasActiveRaid {get;set;}internal string RecoveryBlocker="";
  internal IEnumerable<InternalConflictRecord> Conflicts {get {return _conflicts.Records;}}
  internal Settlement OrderedRaid {get;private set;}
  internal void SetWar(InternalConflictRecord war){Conflict=war;if(!_conflicts.Add(war))throw new Exception("Bad fixture");}
  internal void BlockRecovery(string s){RecoveryBlocker=s;}
  internal bool TryBeginNpcRaid(MobileParty p,Settlement s){OrderedRaid=s;HasActiveRaid=true;return true;}
 }
}
