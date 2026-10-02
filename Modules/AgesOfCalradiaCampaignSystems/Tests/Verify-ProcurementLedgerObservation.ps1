param([Parameter(Mandatory=$true)][string]$PackageRoot)
$ErrorActionPreference='Stop'
$bin='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client'
$references=@('System','System.Core','System.Runtime.Serialization')
foreach($name in @('TaleWorlds.Library','TaleWorlds.DotNet','TaleWorlds.ScreenSystem','TaleWorlds.Localization','TaleWorlds.ObjectSystem','TaleWorlds.SaveSystem','TaleWorlds.Core','TaleWorlds.Engine','TaleWorlds.CampaignSystem','TaleWorlds.MountAndBlade')){
 $path=Join-Path $bin ($name+'.dll');[Reflection.Assembly]::LoadFrom($path)|Out-Null;$references+=$path
}
$harmony=Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.2\lib\net472\0Harmony.dll'
[Reflection.Assembly]::LoadFrom($harmony)|Out-Null;$references+=$harmony
$references+='C:/Program Files (x86)/Reference Assemblies/Microsoft/Framework/.NETFramework/v4.7.2/Facades/netstandard.dll'
$diag=[Reflection.Assembly]::LoadFrom((Join-Path $PackageRoot 'AgesOfCalradiaSoakDiagnostics\bin\Win64_Shipping_Client\AgesOfCalradia.SoakDiagnostics.dll'))
[Reflection.Assembly]::LoadFrom((Join-Path $PackageRoot 'AOC CORE\bin\Win64_Shipping_Client\AgesOfCalradia.CampaignSystems.dll'))|Out-Null
$procPath=Join-Path $PackageRoot 'AOC CORE\bin\Win64_Shipping_Client\AgesOfCalradia.WorkshopProcurement.dll'
if(-not(Test-Path -LiteralPath $procPath)){$procPath=Join-Path $PackageRoot 'AgesOfCalradiaWorkshopProcurement\bin\Win64_Shipping_Client\AgesOfCalradia.WorkshopProcurement.dll'}
$proc=[Reflection.Assembly]::LoadFrom($procPath)
Add-Type -ReferencedAssemblies $references -TypeDefinition @'
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
public static class LedgerObservationFixture {
 static Campaign campaign;
 const string Payload="{\"Schema\":1,\"Orders\":[],\"AcceptNewOrders\":true}";
 static bool CampaignNow(ref Campaign __result){__result=campaign;return false;}
 static bool TimeNow(ref CampaignTime __result){__result=CampaignTime.Days(42.5f);return false;}
 static bool Loaded(ref string __result){__result=Payload;return false;}
 public static string Run(Assembly diag, Assembly proc, string directory){
  var flags=BindingFlags.Static|BindingFlags.NonPublic;
  var capture=diag.GetType("AgesOfCalradia.SoakDiagnostics.SupplyCapture",true);
  var observer=diag.GetType("AgesOfCalradia.SoakDiagnostics.ProcurementObservation",true);
  var log=diag.GetType("AgesOfCalradia.SoakDiagnostics.SoakLog",true);
  log.GetField("_directory",flags).SetValue(null,directory);
  log.GetField("_eventsPath",flags).SetValue(null,Path.Combine(directory,"events.tsv"));
  var abi=proc.GetType("AgesOfCalradia.WorkshopProcurement.ProcurementDiagnostics",true);
  var ticks=AccessTools.Field(typeof(CampaignTime),"TimeTicksPerDay");object previous=ticks.GetValue(null);ticks.SetValue(null,100000L);
  campaign=(Campaign)FormatterServices.GetUninitializedObject(typeof(Campaign));
  var harmony=new Harmony("aoc.ledger.observation.fixture");
  try{
   harmony.Patch(AccessTools.PropertyGetter(typeof(Campaign),"Current"),new HarmonyMethod(typeof(LedgerObservationFixture),"CampaignNow"));
   harmony.Patch(AccessTools.PropertyGetter(typeof(CampaignTime),"Now"),new HarmonyMethod(typeof(LedgerObservationFixture),"TimeNow"));
   harmony.Patch(abi.GetMethod("LoadedLedger"),new HarmonyMethod(typeof(LedgerObservationFixture),"Loaded"));
   harmony.Patch(abi.GetMethod("CurrentLedger"),new HarmonyMethod(typeof(LedgerObservationFixture),"Loaded"));
   string path=(string)capture.GetMethod("BeginSession",flags).Invoke(null,new object[]{directory,new Func<double>(()=>42.5)});
   observer.GetMethod("BeginLedgerObservation",flags).Invoke(null,null);
   observer.GetMethod("BeginLedgerObservation",flags).Invoke(null,null);
   var plan=(Delegate)abi.GetField("PlanObserved",flags).GetValue(null);
   if(plan==null||plan.GetInvocationList().Length!=1)throw new Exception("Missing or duplicate planner observer");
   plan.DynamicInvoke("fixture-shop","offer","categoryReserveRejected=2; policy=fixture");
   var transfer=(Delegate)abi.GetField("TransferObserved",flags).GetValue(null);
   if(transfer==null||transfer.GetInvocationList().Length!=1)throw new Exception("Missing or duplicate transfer observer");
   transfer.DynamicInvoke("fixture-transfer","fixture-order","dispatch","fixture-shop","begin");
   if((string)observer.GetProperty("CashContext",flags).GetValue(null,null)!="; procurementTransfer=fixture-transfer")throw new Exception("Missing transaction cash context");
   transfer.DynamicInvoke("fixture-transfer","fixture-order","dispatch","fixture-shop","committed");
   if((string)observer.GetProperty("CashContext",flags).GetValue(null,null)!="; procurementTransfer=none")throw new Exception("Transaction context leaked after completion");
   var movement=(Delegate)abi.GetField("MovementObserved",flags).GetValue(null);
   if(movement==null||movement.GetInvocationList().Length!=1)throw new Exception("Missing or duplicate movement observer");
   movement.DynamicInvoke("00112233445566778899aabbccddeeff","fixture-order","dispatch","fixture-town","fixture-item","fixture-category",10,7,0,3);
   var accounting=(Delegate)abi.GetField("AccountingObserved",flags).GetValue(null);
   if(accounting==null||accounting.GetInvocationList().Length!=1)throw new Exception("Missing or duplicate accounting observer");
   accounting.DynamicInvoke("00112233445566778899aabbccddeefe","fixture-order","return","fixture-shop",67,0,60,60,7);
   abi.GetMethod("ObserveLedger",flags).Invoke(null,new object[]{"serialized_for_save",Payload});
   transfer.DynamicInvoke("fixture-interrupted","fixture-order","return","fixture-shop","begin");
   capture.GetMethod("Stop",flags).Invoke(null,new object[]{"fixture_completed"});
   if(abi.GetField("LedgerObserved",flags).GetValue(null)!=null)throw new Exception("Observer leaked after capture closure");
   if(abi.GetField("MovementObserved",flags).GetValue(null)!=null)throw new Exception("Movement observer leaked after capture closure");
   if(abi.GetField("AccountingObserved",flags).GetValue(null)!=null)throw new Exception("Accounting observer leaked after capture closure");
   if(abi.GetField("TransferObserved",flags).GetValue(null)!=null)throw new Exception("Transfer observer leaked after capture closure");
   if(abi.GetField("PlanObserved",flags).GetValue(null)!=null)throw new Exception("Planner observer leaked after capture closure");
   if((string)observer.GetProperty("CashContext",flags).GetValue(null,null)!="; procurementTransfer=none")throw new Exception("Interrupted transaction context leaked");
   string text=File.ReadAllText(path);
   if(!text.Contains("PROCUREMENT_PLAN")||!text.Contains("categoryReserveRejected=2"))throw new Exception("Planner decision not captured");
   if(!text.Contains("PROCUREMENT_MOVEMENT")||!text.Contains("cargoAfter=3"))throw new Exception("Movement receipt not captured");
   if(!text.Contains("PROCUREMENT_ACCOUNTING")||!text.Contains("cashDelta=60"))throw new Exception("Accounting receipt not captured");
   if(!text.Contains("PROCUREMENT_TRANSFER")||!text.Contains("transaction=fixture-transfer"))throw new Exception("Transfer boundary not captured");
   int loaded=0,saved=0;
   foreach(string line in text.Split('\n')){
    if(line.Contains("\tloaded_payload\t"))loaded++;
    if(line.Contains("\tserialized_for_save\t"))saved++;
   }
   if(loaded!=2||saved!=1||!text.Contains("payloadBase64="+Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Payload))))
    throw new Exception("Missing, duplicate or changed ledger observation");
   return path;
  }finally{
   capture.GetMethod("Stop",flags).Invoke(null,new object[]{"fixture_cleanup"});
   harmony.UnpatchAll("aoc.ledger.observation.fixture");ticks.SetValue(null,previous);campaign=null;
  }
 }
}
'@
$directory=Join-Path $env:TEMP ('aoc-ledger-observation-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory|Out-Null
$path=[LedgerObservationFixture]::Run($diag,$proc,$directory)
"PASS: real optional ABI, payload preservation, duplicate subscription prevention and close-time detach. Synthetic fixture: $path"
