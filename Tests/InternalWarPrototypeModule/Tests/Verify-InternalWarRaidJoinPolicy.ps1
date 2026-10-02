$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# Actual raid join policy compiled with data-only game/service boundaries; run with pwsh.
$policy = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\InternalWarRaidJoinPolicy.cs') -Raw
$stubs = @'
namespace TaleWorlds.Core { public enum BattleSideEnum { None=-1, Defender, Attacker } }
namespace TaleWorlds.CampaignSystem {
 public class Kingdom { public string StringId; }
 public class Clan { public static Clan PlayerClan; public string StringId; public Kingdom Kingdom;
  public bool IsClanTypeMercenary, IsUnderMercenaryService, IsMinorFaction; }
}
namespace TaleWorlds.CampaignSystem.Party {
 public class PartyBase { public MobileParty MobileParty; public TaleWorlds.CampaignSystem.Settlements.Settlement Settlement;
  public TaleWorlds.CampaignSystem.MapEvents.MapEvent MapEvent; }
 public class MobileParty { public static MobileParty MainParty; public Clan Clan;
  public PartyBase Party; public bool IsActive=true, IsLordParty=true, IsCaravan, IsVillager, IsCurrentlyAtSea;
  public object Army, BesiegerCamp; public MobileParty AttachedTo;
  public System.Collections.Generic.List<MobileParty> AttachedParties=new System.Collections.Generic.List<MobileParty>();
  public TaleWorlds.CampaignSystem.Settlements.Settlement CurrentSettlement;
  public MobileParty() { Party=new PartyBase { MobileParty=this }; }
 }
}
namespace TaleWorlds.CampaignSystem.Settlements {
 public class Settlement { public bool IsVillage=true; public Clan OwnerClan; public Party.PartyBase Party;
  public Settlement() { Party=new Party.PartyBase { Settlement=this }; } }
}
namespace TaleWorlds.CampaignSystem.MapEvents {
 public class MapEventSide { public Party.PartyBase LeaderParty; }
 public class MapEvent { public bool IsRaid=true; public Settlements.Settlement MapEventSettlement;
  public MapEventSide AttackerSide, DefenderSide; }
}
namespace AgesOfCalradiaInternalWarsTest {
 internal enum InternalConflictPhase { Active, PeacePending, Closed }
 internal class InternalConflictRecord { internal string KingdomId="realm"; internal InternalConflictPhase Phase;
  internal string OpponentOf(string id) { return id=="a" ? "b" : id=="b" ? "a" : null; } }
 internal static class InternalWarTestService { internal static bool RecoveryBlocked;
  internal static TaleWorlds.CampaignSystem.Clan ResolveClan(TaleWorlds.CampaignSystem.Party.PartyBase p)
  { return p==null ? null : p.Settlement!=null ? p.Settlement.OwnerClan : p.MobileParty==null ? null : p.MobileParty.Clan; } }
 public static class RaidJoinVerifier {
  static int count;
  static void Check(bool b,string message) { count++; if(!b) throw new System.Exception(message); }
  public static int Run() {
   count=0;
   var realm=new TaleWorlds.CampaignSystem.Kingdom { StringId="realm" };
   var a=new TaleWorlds.CampaignSystem.Clan { StringId="a", Kingdom=realm };
   var b=new TaleWorlds.CampaignSystem.Clan { StringId="b", Kingdom=realm };
   var village=new TaleWorlds.CampaignSystem.Settlements.Settlement { OwnerClan=b };
   var raider=new TaleWorlds.CampaignSystem.Party.MobileParty { Clan=a };
   var joiner=new TaleWorlds.CampaignSystem.Party.MobileParty { Clan=b };
   var battle=new TaleWorlds.CampaignSystem.MapEvents.MapEvent { MapEventSettlement=village,
    AttackerSide=new TaleWorlds.CampaignSystem.MapEvents.MapEventSide { LeaderParty=raider.Party },
    DefenderSide=new TaleWorlds.CampaignSystem.MapEvents.MapEventSide { LeaderParty=village.Party } };
   raider.Party.MapEvent=battle; village.Party.MapEvent=battle;
   var war=new InternalConflictRecord();
   System.Func<bool> allowed=()=>InternalWarRaidJoinPolicy.CanJoin(war,battle,joiner.Party,TaleWorlds.Core.BattleSideEnum.Defender);
   Check(allowed(),"Owner lord could not defend.");
   Check(InternalWarRaidJoinPolicy.CanJoin(war,battle,village.Party,TaleWorlds.Core.BattleSideEnum.Defender),"Village party rejected.");
   Check(!InternalWarRaidJoinPolicy.CanJoin(war,battle,village.Party,TaleWorlds.Core.BattleSideEnum.Attacker),"Village became attacker.");
   Check(!InternalWarRaidJoinPolicy.CanJoin(war,battle,joiner.Party,TaleWorlds.Core.BattleSideEnum.None),"Invalid side accepted.");
   joiner.Army=new object(); Check(!allowed(),"Army joined."); joiner.Army=null;
   joiner.AttachedTo=raider; Check(!allowed(),"Attached party joined."); joiner.AttachedTo=null;
   joiner.AttachedParties.Add(raider); Check(!allowed(),"Mixed followers joined."); joiner.AttachedParties.Clear();
   joiner.IsCaravan=true; Check(!allowed(),"Caravan joined."); joiner.IsCaravan=false;
   joiner.IsVillager=true; Check(!allowed(),"Villager joined."); joiner.IsVillager=false;
   joiner.IsCurrentlyAtSea=true; Check(!allowed(),"Sea party joined."); joiner.IsCurrentlyAtSea=false;
   joiner.IsActive=false; Check(!allowed(),"Inactive party joined."); joiner.IsActive=true;
   joiner.IsLordParty=false; Check(!allowed(),"Civilian joined."); joiner.IsLordParty=true;
   joiner.BesiegerCamp=new object(); Check(!allowed(),"Besieger joined."); joiner.BesiegerCamp=null;
   joiner.Party.MapEvent=new TaleWorlds.CampaignSystem.MapEvents.MapEvent(); Check(!allowed(),"Foreign event party joined."); joiner.Party.MapEvent=null;
   b.IsClanTypeMercenary=true; Check(!allowed(),"Mercenary joined."); b.IsClanTypeMercenary=false;
   b.IsUnderMercenaryService=true; Check(!allowed(),"Contract mercenary joined."); b.IsUnderMercenaryService=false;
   b.IsMinorFaction=true; Check(!allowed(),"Minor NPC joined."); TaleWorlds.CampaignSystem.Clan.PlayerClan=b;
   Check(allowed(),"Player minor metadata exception lost."); b.IsMinorFaction=false;
   joiner.CurrentSettlement=village; Check(!allowed(),"NPC inside village joined as free lord.");
   TaleWorlds.CampaignSystem.Party.MobileParty.MainParty=joiner;
   Check(allowed(),"Owner main party inside village could not defend.");
   joiner.CurrentSettlement=new TaleWorlds.CampaignSystem.Settlements.Settlement(); Check(!allowed(),"Main party in foreign village joined."); joiner.CurrentSettlement=null;
   war.Phase=InternalConflictPhase.PeacePending; Check(!allowed(),"Peace allowed reinforcement."); war.Phase=InternalConflictPhase.Active;
   InternalWarTestService.RecoveryBlocked=true; Check(!allowed(),"Recovery blocker bypassed."); InternalWarTestService.RecoveryBlocked=false;
   b.Kingdom=new TaleWorlds.CampaignSystem.Kingdom { StringId="other" }; Check(!allowed(),"Defector joined."); b.Kingdom=realm;
   raider.Party.MapEvent=null; Check(!allowed(),"Disconnected event leader accepted."); raider.Party.MapEvent=battle;
   battle.DefenderSide.LeaderParty=raider.Party; Check(!allowed(),"Wrong defending leader accepted."); battle.DefenderSide.LeaderParty=village.Party;
   joiner.Clan=a; Check(!allowed(),"Enemy joined defender side.");
   Check(InternalWarRaidJoinPolicy.CanJoin(war,battle,joiner.Party,TaleWorlds.Core.BattleSideEnum.Attacker),"Attacker reinforcement rejected.");
   return count;
  }
 }
}
'@
Add-Type -TypeDefinition ($policy + [Environment]::NewLine + $stubs)
$count = [AgesOfCalradiaInternalWarsTest.RaidJoinVerifier]::Run()
Write-Output "Actual raid join policy passed: $count assertions; no native game calls."
