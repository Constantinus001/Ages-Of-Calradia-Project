using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
namespace AgesOfCalradiaInternalWarsTest
{
 internal static class CompensationProgram
 {
  private static int checks;
  private sealed class PaymentStore : IDataStore
  {
   public bool IsLoading {get;set;}
   internal string Payload;
   public bool SyncData<T>(string key, ref T value)
   {
    if (key != "AOC_InternalConflict_UncertainPayment") throw new Exception("Payment save key changed");
    if (IsLoading) value=(T)(object)Payload; else Payload=(string)(object)value;
    return true;
   }
  }
  private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
  private static InternalWarTestBehavior Fixture()
  {
   Clan.All.Clear(); var kingdom = new Kingdom();
   var payer = new Clan {StringId="player", Kingdom=kingdom}; var rival = new Clan {StringId="rival", Kingdom=kingdom};
   payer.Leader = new Hero {StringId="payer", Clan=payer, Gold=10000}; rival.Leader = new Hero {StringId="recipient", Clan=rival, Gold=200};
   Clan.All.Add(payer); Clan.All.Add(rival); Clan.PlayerClan=payer; Hero.MainHero=payer.Leader;
   CampaignTime.Now=new CampaignTime {ToDays=20}; GiveGoldAction.Calls=0;
   GiveGoldAction.Transfer=(a,b,g)=>{a.Gold-=g; b.Gold+=g;};
   return new InternalWarTestBehavior {Conflict=new InternalConflictRecord {Id="war", KingdomId="realm", AttackerClanId="player", DefenderClanId="rival", StartDay=1}};
  }
  private static void Uncertain(Action<InternalWarTestBehavior,Hero,Hero,int> callback)
  {
   var owner=Fixture(); GiveGoldAction.Transfer=(a,b,g)=>callback(owner,a,b,g);
   string result; owner.OfferCompensation("war",out result);
   Check(owner.RecoveryBlocker.Length>0 && owner.PaymentMarker=="war", "Uncertain native transfer was accepted");
   Check(owner.Conflict.CompensationGold==0 && string.IsNullOrEmpty(owner.Conflict.CompensationPayerId), "Uncertain transfer created a receipt");
   owner.OfferCompensation("war",out result); Check(GiveGoldAction.Calls==1, "Uncertain transfer was retried");
   var saved=new PaymentStore(); owner.SyncSafety(saved);
   var restored=Fixture(); saved.IsLoading=true; restored.SyncSafety(saved);
   Check(restored.RecoveryBlocker.Length>0 && restored.PaymentMarker=="war", "Reload lost payment safety marker");
   restored.OfferCompensation("war",out result); Check(GiveGoldAction.Calls==0, "Reload retried uncertain payment");
  }
  private static int Main()
  {
   var owner=Fixture(); string result; owner.OfferCompensation("war",out result);
   Check(Hero.MainHero.Gold==5000 && Clan.All[1].Leader.Gold==5200, "Normal balance transfer wrong");
   Check(owner.Conflict.CompensationGold==5000 && owner.Conflict.CompensationPayerId=="player", "Normal receipt missing");
   Check(owner.PaymentMarker=="" && owner.RecoveryBlocker=="", "Normal payment blocked");
   owner.OfferCompensation("war",out result); Check(GiveGoldAction.Calls==1, "Duplicate payment accepted");
   Uncertain((o,a,b,g)=>{a.Gold-=g;});
   Uncertain((o,a,b,g)=>{a.Gold-=g; b.Gold+=g-1;});
   Uncertain((o,a,b,g)=>{a.Gold-=g; b.Gold+=g; throw new InvalidOperationException("callback failed");});
   Uncertain((o,a,b,g)=>{a.Gold-=g; b.Gold+=g; a.Clan=null;});
   Uncertain((o,a,b,g)=>{a.Gold-=g; b.Gold+=g; b.Clan=null;});
   Uncertain((o,a,b,g)=>{a.Gold-=g; b.Gold+=g; b.Clan.Leader=new Hero();});
   Uncertain((o,a,b,g)=>{a.Gold-=g; b.Gold+=g; b.Clan.Kingdom=new Kingdom {StringId="other"};});
   Uncertain((o,a,b,g)=>{a.Gold-=g; b.Gold+=g; o.Conflict.Phase=InternalConflictPhase.Closed;});
   Uncertain((o,a,b,g)=>{a.Gold-=g; b.Gold+=g; o.BlockRecovery("other callback failed");});
   owner=Fixture(); Clan.All[1].Leader.Gold=int.MaxValue;
   owner.OfferCompensation("war",out result); Check(GiveGoldAction.Calls==0 && owner.Conflict.Phase==InternalConflictPhase.Active, "Overflow entered native payment");
   owner=Fixture(); Hero.MainHero.Gold=4999; owner.OfferCompensation("war",out result); Check(GiveGoldAction.Calls==0, "Insufficient funds accepted");
   owner=Fixture(); owner.OfferCompensation("stale",out result); Check(GiveGoldAction.Calls==0, "Stale dialog paid");
   owner=Fixture(); owner.OnPeace=()=>owner.BlockRecovery("peace callback failed");
   owner.OfferCompensation("war",out result); Check(GiveGoldAction.Calls==0 && owner.PaymentMarker=="" && result==owner.RecoveryBlocker, "Failed peace transition still transferred gold or hid its blocker");
   owner=Fixture(); GiveGoldAction.Transfer=(a,b,g)=>{string nested; owner.OfferCompensation("war",out nested); a.Gold-=g; b.Gold+=g;};
   owner.OfferCompensation("war",out result); Check(GiveGoldAction.Calls==1 && owner.Conflict.CompensationGold==5000, "Reentrant offer duplicated payment");
   Console.WriteLine("Actual compensation diplomacy: " + checks + " assertions passed; native gold and peace adapters injected."); return 0;
  }
 }
}
