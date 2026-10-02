param([Parameter(Mandatory=$true)][string]$PackageRoot)
$ErrorActionPreference='Stop'
$bin='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client'
$references=@('System','System.Core','System.Runtime.Serialization')
foreach($name in @('TaleWorlds.Library','TaleWorlds.DotNet','TaleWorlds.ScreenSystem','TaleWorlds.Localization','TaleWorlds.ObjectSystem','TaleWorlds.SaveSystem','TaleWorlds.Core','TaleWorlds.Engine','TaleWorlds.CampaignSystem','TaleWorlds.MountAndBlade')){
 $path=Join-Path $bin ($name+'.dll');[Reflection.Assembly]::LoadFrom($path)|Out-Null;$references+=$path
}
$harmony=Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.2\lib\net472\0Harmony.dll'
[Reflection.Assembly]::LoadFrom($harmony)|Out-Null
$references+=$harmony
$references+='C:/Program Files (x86)/Reference Assemblies/Microsoft/Framework/.NETFramework/v4.7.2/Facades/netstandard.dll'
$diagnostics=[Reflection.Assembly]::LoadFrom((Join-Path $PackageRoot 'AgesOfCalradiaSoakDiagnostics\bin\Win64_Shipping_Client\AgesOfCalradia.SoakDiagnostics.dll'))
[Reflection.Assembly]::LoadFrom((Join-Path $PackageRoot 'AOC CORE\bin\Win64_Shipping_Client\AgesOfCalradia.CampaignSystems.dll'))|Out-Null
Add-Type -ReferencedAssemblies $references -TypeDefinition @'
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;
public static class NativeQuestObservationFixture {
 static Campaign campaign;
 static QuestManager manager;
 static bool CampaignNow(ref Campaign __result){__result=campaign;return false;}
 static bool Manager(ref QuestManager __result){__result=manager;return false;}
 static bool TimeNow(ref CampaignTime __result){__result=CampaignTime.Days(42.5f);return false;}
 sealed class FixtureQuest : QuestBase {
  private FixtureQuest():base("unused",null,CampaignTime.Never,0){}
  public override TextObject Title {get{return new TextObject("fixture");}}
  public override bool IsRemainingTimeHidden {get{return false;}}
  protected override void SetDialogs(){}
  protected override void InitializeQuestOnGameLoad(){}
 }
 public static string Run(Assembly diagnostics,string directory){
  var flags=BindingFlags.Static|BindingFlags.NonPublic;
  var capture=diagnostics.GetType("AgesOfCalradia.SoakDiagnostics.SupplyCapture",true);
  var log=diagnostics.GetType("AgesOfCalradia.SoakDiagnostics.SoakLog",true);
  log.GetField("_directory",flags).SetValue(null,directory);
  log.GetField("_eventsPath",flags).SetValue(null,Path.Combine(directory,"AocSoakEvents.tsv"));
  var snapshot=diagnostics.GetType("AgesOfCalradia.SoakDiagnostics.CampaignSystemsObservation",true).GetMethod("Snapshot",flags);
  var harmony=new Harmony("aoc.native.quest.observation.fixture");
  // Native time initialization requires a real Campaign.Models graph. This
  // standalone adapter fixture supplies a fixed tick unit and restores it.
  var dayTicks=AccessTools.Field(typeof(CampaignTime),"TimeTicksPerDay");
  object oldDayTicks=dayTicks.GetValue(null);dayTicks.SetValue(null,100000L);
  campaign=(Campaign)FormatterServices.GetUninitializedObject(typeof(Campaign));
  manager=(QuestManager)FormatterServices.GetUninitializedObject(typeof(QuestManager));
  var quests=new MBList<QuestBase>();
  var due=new[]{CampaignTime.Days(45.5f),CampaignTime.Never,CampaignTime.Days(40f)};
  for(int i=0;i<due.Length;i++){
   var quest=(QuestBase)FormatterServices.GetUninitializedObject(typeof(FixtureQuest));
   typeof(QuestBase).GetProperty("StringId").GetSetMethod(true).Invoke(quest,new object[]{"fixture"+i});
   typeof(QuestBase).GetProperty("QuestDueTime").GetSetMethod(true).Invoke(quest,new object[]{due[i]});
   quests.Add(quest);
  }
  AccessTools.Field(typeof(QuestManager),"_quests").SetValue(manager,quests);
  try{
   harmony.Patch(AccessTools.PropertyGetter(typeof(Campaign),"Current"),new HarmonyMethod(typeof(NativeQuestObservationFixture),"CampaignNow"));
   harmony.Patch(AccessTools.PropertyGetter(typeof(Campaign),"QuestManager"),new HarmonyMethod(typeof(NativeQuestObservationFixture),"Manager"));
   harmony.Patch(AccessTools.PropertyGetter(typeof(CampaignTime),"Now"),new HarmonyMethod(typeof(NativeQuestObservationFixture),"TimeNow"));
   string path=(string)capture.GetMethod("BeginSession",flags).Invoke(null,new object[]{directory,new Func<double>(()=>42.5)});
   snapshot.Invoke(null,null);
   if(!(bool)capture.GetProperty("Active",flags).GetValue(null,null))throw new Exception("Native snapshot failed capture");
   capture.GetMethod("Stop",flags).Invoke(null,new object[]{"fixture_completed"});
   for(int i=0;i<due.Length;i++)if(quests[i].QuestDueTime!=due[i])throw new Exception("Observer changed saved quest deadline");
   return path;
  }finally{
   capture.GetMethod("Stop",flags).Invoke(null,new object[]{"fixture_cleanup"});
   harmony.UnpatchAll("aoc.native.quest.observation.fixture");campaign=null;manager=null;
   dayTicks.SetValue(null,oldDayTicks);
  }
 }
}
'@
$directory=Join-Path $env:TEMP ('aoc-native-quests-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory|Out-Null
$path=[NativeQuestObservationFixture]::Run($diagnostics,$directory)
$rows=@(Import-Csv -LiteralPath $path -Delimiter "`t")
$quests=@($rows|Where-Object kind -eq 'QUEST_DEADLINE')
if($quests.Count -ne 3 -or $quests[0].detail -notmatch 'remainingDays=3;' -or $quests[1].metric -ne 'never' -or $quests[2].detail -notmatch 'remainingDays=0;'){throw 'Native quest adapter emitted wrong deadline semantics'}
if(@($rows|Where-Object {$_.kind -eq 'CORE_SYSTEMS' -and $_.metric -eq 'no_campaign'}).Count -ne 1){throw 'Core no-campaign coverage status fabricated'}
"PASS: actual native QuestManager/QuestBase adapter; saved finite/Never/expired deadlines preserved, no scaling or completion mutation. Synthetic evidence: $path"
