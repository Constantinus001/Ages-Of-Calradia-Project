param([Parameter(Mandatory=$true)][string]$PackageRoot)
$ErrorActionPreference='Stop'
$core=Join-Path $PackageRoot 'AOC CORE\bin\Win64_Shipping_Client\AgesOfCalradia.CampaignSystems.dll'
$procurement=Join-Path $PackageRoot 'AOC CORE\bin\Win64_Shipping_Client\AgesOfCalradia.WorkshopProcurement.dll'
if(-not(Test-Path -LiteralPath $procurement)){$procurement=Join-Path $PackageRoot 'AgesOfCalradiaWorkshopProcurement\bin\Win64_Shipping_Client\AgesOfCalradia.WorkshopProcurement.dll'}
[Reflection.Assembly]::LoadFrom($core)|Out-Null
[Reflection.Assembly]::LoadFrom($procurement)|Out-Null
Add-Type -ReferencedAssemblies @('System','System.Core','System.Runtime.Serialization',$core,$procurement) -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using AgesOfCalradia.WorkshopProcurement;
public static class LedgerCapacityMeasurement {
 static ProcurementOrder Order(int i) {
  return new ProcurementOrder {Town="destination",Workshop="shop"+i,Type="smithy",Source="source",Recipe=0,
   Quantity=3,OriginalQuantity=3,GoodsCost=100,FreightCost=7,DepartureDay=20,ArrivalDay=22,TransferComplete=true,
   Lines=new List<ProcurementLine>{new ProcurementLine{Item="iron",Category="iron",UnitsPerBatch=2,Remaining=6}}};
 }
 public static string Run(int count) {
  var state=new ProcurementState();for(int i=0;i<count;i++)state.Orders.Add(Order(i));
  var method=typeof(ProcurementState).GetMethod("CanAdmit",BindingFlags.NonPublic|BindingFlags.Instance);
  var proposed=Order(count);var args=new object[]{proposed};
  method.Invoke(state,args); // warm-up outside timing
  var times=new double[11];bool accepted=false;
  for(int i=0;i<times.Length;i++){
   var timer=Stopwatch.StartNew();accepted=(bool)method.Invoke(state,args);timer.Stop();times[i]=timer.Elapsed.TotalMilliseconds;
  }
  if(state.Orders.Count!=count || state.Find("destination","shop"+count)!=null)throw new Exception("Admission mutated state");
  Array.Sort(times);
  string encoded=state.Encode();
  if(ProcurementState.Decode(encoded).Orders.Count!=count)throw new Exception("Large ledger round-trip failed");
  return string.Format(System.Globalization.CultureInfo.InvariantCulture,
   "orders={0}; payloadCharacters={1}; accepted={2}; admissionMedianMs={3:0.###}; admissionMaxMs={4:0.###}; samples=11; not_live_frame_cost",
   count,encoded.Length,accepted,times[5],times[10]);
 }
}
'@
foreach($count in @(0,500,2000,4000)){[LedgerCapacityMeasurement]::Run($count)}
'PASS: measured exact packaged ledger; no admission mutation; all populated ledgers round-trip. Not native campaign performance certification.'
