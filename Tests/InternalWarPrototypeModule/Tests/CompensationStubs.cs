using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
namespace TaleWorlds.CampaignSystem
{
 public class Kingdom { public string StringId = "realm"; public string Name { get { return StringId; } } }
 public class Hero { public static Hero MainHero; public string StringId = "hero"; public Clan Clan; public int Gold; public bool IsAlive = true; }
 public class Clan {
  public static Clan PlayerClan; public static readonly List<Clan> All = new List<Clan>();
  public string StringId; public string Name { get { return StringId; } } public Kingdom Kingdom; public Hero Leader;
  public bool IsMinorFaction {get;set;} public bool IsClanTypeMercenary {get;set;} public bool IsUnderMercenaryService {get;set;}
  public bool IsEliminated {get;set;}
 }
 public struct CampaignTime { public static CampaignTime Now; public double ToDays; }
 public interface IDataStore { bool IsLoading {get;} bool SyncData<T>(string key,ref T data); }
 public static class FactionManager { public static bool IsAtWarAgainstFaction(Clan a, Clan b) {return true;} }
}
namespace TaleWorlds.CampaignSystem.Actions
{
 public static class GiveGoldAction {
  public static int Calls; public static Action<Hero,Hero,int> Transfer;
  public static void ApplyBetweenCharacters(Hero a, Hero b, int gold) { Calls++; Transfer(a,b,gold); }
 }
 public static class DeclareWarAction { public static void ApplyByDefault(Clan a, Clan b) {} }
}
namespace AgesOfCalradiaInternalWarsTest
{
 // Data-only adapters. Actual diplomacy and conflict record sources are compiled unchanged.
 internal class RegistryStub { internal InternalConflictRecord Find(string a,string b) {return null;} internal bool Add(InternalConflictRecord r) {return true;} }
 internal class HistoryStub { internal void Archive(InternalConflictRecord r) {} }
 internal class AiStub { internal void Defer() {} }
 internal static class InternalWarTestService { internal static string GetDeclarationBlocker() {return "not part of this fixture";} }
 internal static class InternalWarTestDiagnostics { internal static void Info(string s) {} internal static void Error(string s, Exception e) {} }
 internal sealed partial class InternalWarTestBehavior {
  private readonly InternalWarTestBehavior _root; private InternalWarTestBehavior _selectedController;
  private readonly RegistryStub _conflicts = new RegistryStub(); private readonly HistoryStub _history = new HistoryStub(); private readonly AiStub _ai = new AiStub();
  internal InternalConflictRecord Conflict; internal InternalWarTestRecord CurrentRecord {get;set;}
  internal string RecoveryBlocker = ""; internal string PaymentMarker {get {return _uncertainPaymentConflictId;}}
  internal Action OnPeace {get;set;}
  internal void SyncSafety(IDataStore store) {SyncPaymentSafety(store);}
  internal InternalWarTestBehavior() {_root=this; _selectedController=this;}
  internal InternalWarTestBehavior Selected {get {return _selectedController;}}
  private static Clan FindClan(string id) {return Clan.All.Find(c => c.StringId == id);}
  internal string GetRealmDeclarationBlocker(Kingdom kingdom) {return string.Empty;}
  private InternalWarTestBehavior ControllerFor(InternalConflictRecord war) {return this;}
  internal void BlockRecovery(string reason) {if (RecoveryBlocker.Length == 0) RecoveryBlocker=reason;}
  private void RequestRoyalPeace(bool monarch, out string result) {Conflict.RequestPeace(); if (OnPeace!=null) OnPeace(); result="queued";}
 }
}
