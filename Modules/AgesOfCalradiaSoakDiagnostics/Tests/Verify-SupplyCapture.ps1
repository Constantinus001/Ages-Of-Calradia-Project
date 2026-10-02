param(
 [string]$BannerlordDir='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord',
 [string]$DiagnosticsAssemblyPath=(Join-Path (Split-Path -Parent $PSScriptRoot) 'bin\Win64_Shipping_Client\AgesOfCalradia.SoakDiagnostics.dll')
)
$ErrorActionPreference='Stop'
$moduleRoot=Split-Path -Parent $PSScriptRoot
$gameBin=Join-Path $BannerlordDir 'bin\Win64_Shipping_Client'
foreach($name in @('TaleWorlds.Library','TaleWorlds.DotNet','TaleWorlds.ScreenSystem','TaleWorlds.Localization','TaleWorlds.ObjectSystem','TaleWorlds.SaveSystem','TaleWorlds.Core','TaleWorlds.Engine','TaleWorlds.CampaignSystem','TaleWorlds.MountAndBlade')){
 [Reflection.Assembly]::LoadFrom((Join-Path $gameBin ($name+'.dll')))|Out-Null
}
$harmonyPath=Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.2\lib\net472\0Harmony.dll'
[Reflection.Assembly]::LoadFrom($harmonyPath)|Out-Null
$assembly=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $DiagnosticsAssemblyPath).Path)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$testRoot=Join-Path $env:TEMP ('aoc-supply-verifier-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot|Out-Null
$fixtureLog=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.SoakLog',$true)
$fixtureLog.GetField('_directory',$flags).SetValue($null,$testRoot)
$fixtureLog.GetField('_eventsPath',$flags).SetValue($null,(Join-Path $testRoot 'AocSoakEvents.tsv'))
$capture=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.SupplyCapture',$true)
$observer=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.SupplyChainObserver',$true)
$taps=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.SupplyValueTaps',$true)
$limit=$capture.GetMethod('LimitReason',$flags)
foreach($case in @(@(3599,29.99,5368709119,$null),@(0,15,0,$null),@(3600,0,0,'wall_limit_incomplete'),@(0,30,0,'day_target_reached_coverage_not_certified'),@(0,0,5368709120,'byte_limit_incomplete'),@(0,-1,0,'invalid_clock'))){
 if($limit.Invoke($null,@([double]$case[0],[double]$case[1],[long]$case[2])) -ne $case[3]){throw 'Supply capture limits failed'}
}
$decision=$observer.GetMethod('Decision',$flags)
foreach($case in @(@(0.2,10,50,$false,'random_gate'),@(0.1,10,50,$false,'below_dispatch_threshold'),@(0.1,50,50,$false,'threshold_met_no_load_inspect_party_state'),@(0.1,10,-1,$false,'prethreshold_party_or_battle_gate'),@(0.1,50,50,$true,'load_called_not_arrival'),@([single]::NaN,0,-1,$false,'unobserved_gate'))){
 if($decision.Invoke($null,@([single]$case[0],[int]$case[1],[int]$case[2],[bool]$case[3])) -ne $case[4]){throw 'Dispatch classifier invented an outcome'}
}
$taps.GetMethod('Validate',$flags).Invoke($null,@())|Out-Null
Add-Type -TypeDefinition @'
using System;
using System.Runtime.CompilerServices;
public static class InputCostFixture {
 public static int Calls; public static bool Throw;
 [MethodImpl(MethodImplOptions.NoInlining)] public static bool Check(int a, int b, int c, out int cost) {
  Calls++; if(Throw) throw new InvalidOperationException("input-native-sentinel"); cost=37; return true;
 }
 [MethodImpl(MethodImplOptions.NoInlining)] public static int Run() { int cost; if(!Check(1,2,3,out cost)) throw new Exception("bool changed"); return cost; }
 public static void Legacy(object[] __args) { }
}
'@
$inputFixtureHarmony=New-Object HarmonyLib.Harmony('aoc.supply.input.fixture')
try {
 $inputFixtureHarmony.Patch([InputCostFixture].GetMethod('Check'),$null,(New-Object HarmonyLib.HarmonyMethod([InputCostFixture].GetMethod('Legacy'))),$null,$null)|Out-Null
 Write-Output "Legacy workshop out-int binding caller=$([InputCostFixture]::Run()) (expected native 37)"
 $inputFixtureHarmony.UnpatchAll('aoc.supply.input.fixture')
 [InputCostFixture]::Calls=0
 $workshopType=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.SupplyWorkshopObserver',$true)
 $inputFixtureHarmony.Patch([InputCostFixture].GetMethod('Check'),$null,(New-Object HarmonyLib.HarmonyMethod($workshopType.GetMethod('InputGate',$flags))),$null,$null)|Out-Null
 for($i=0;$i -lt 10;$i++){if([InputCostFixture]::Run() -ne 37){throw 'Actual input-cost observer corrupted native out int'}}
 if([InputCostFixture]::Calls -ne 10){throw 'Input-cost observer repeated original'}
 [InputCostFixture]::Throw=$true
 $caught=$false
 try{[InputCostFixture]::Run()|Out-Null}catch{if($_.Exception.ToString().Contains('input-native-sentinel')){$caught=$true}else{throw}}
 if(-not $caught){throw 'Input-cost observer swallowed native exception'}
}finally{$inputFixtureHarmony.UnpatchAll('aoc.supply.input.fixture')}
# Verify we observe the actual cash/inventory body, not an inlineable wrapper.
$sale=@($observer.GetMethod('ScopeTargets',$flags).Invoke($null,@()) | Where-Object {$_.DeclaringType.Name -eq 'SellGoodsForTradeAction'})
if($sale.Count -ne 1 -or $sale[0].Name -ne 'ApplyInternal'){throw 'Sale observer targets wrapper instead of transaction body'}
$saleCalls=@([HarmonyLib.PatchProcessor]::GetOriginalInstructions($sale[0]) | ForEach-Object {if($_.operand -is [Reflection.MethodInfo]){$_.operand.Name}})
foreach($required in @('GetItemPrice','set_PartyTradeGold','ChangeGold','AddToCounts')){
 if($saleCalls -notcontains $required){throw "Native sale transaction changed: missing $required"}
}
try {
 $observer.GetMethod('Install',$flags).Invoke($null,@())|Out-Null
 foreach($typeName in @('SupplyCaravanObserver','SupplyCashObserver')){
  $type=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.'+$typeName,$true)
  foreach($method in $type.GetMethod('Targets',$flags).Invoke($null,@())){
   $info=[HarmonyLib.Harmony]::GetPatchInfo($method)
   if(-not ($info.Owners -contains 'aoc.soak.supply.observer.v1')){throw "Missing broad hook $method"}
   if(@($method.GetParameters() | Where-Object {$_.ParameterType.IsByRef}).Count){
    foreach($patch in @($info.Prefixes)+@($info.Postfixes)+@($info.Finalizers)){
     if($patch.owner -eq 'aoc.soak.supply.observer.v1' -and @($patch.PatchMethod.GetParameters() | Where-Object {$_.Name -eq '__args' -or ($_.ParameterType.IsByRef -and $_.Name -ne '__state')}).Count){throw "Unsafe out-parameter binding $method"}
    }
   }
  }
 }
 $workshop=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.SupplyWorkshopObserver',$true)
 $inputTarget=@($workshop.GetMethod('Targets',$flags).Invoke($null,@()) | Where-Object Name -eq 'DetermineItemRosterHasSufficientInputs')[0]
 $inputPatch=@([HarmonyLib.Harmony]::GetPatchInfo($inputTarget).Postfixes | Where-Object owner -eq 'aoc.soak.supply.observer.v1')[0].PatchMethod
 if($inputPatch.Name -ne 'InputGate' -or @($inputPatch.GetParameters() | Where-Object {$_.Name -eq '__args' -or $_.ParameterType.IsByRef}).Count){throw 'Unsafe workshop input cost binding'}
 $wool=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.SupplyWoolSaleObserver',$true)
 $sell=$wool.GetMethod('SellTarget',$flags).Invoke($null,@())
 $lookupObserver=$wool.GetMethod('LookupObserver',$flags).Invoke($null,@())
 if(@($lookupObserver.GetParameters() | Where-Object {$_.ParameterType.IsByRef -or $_.Name -eq '__args'}).Count){throw 'Wool lookup observer must not write back native arguments'}
 if($lookupObserver.GetParameters()[2].ParameterType -ne $wool.GetMethod('LookupTarget',$flags).Invoke($null,@()).GetParameters()[2].ParameterType.GetElementType()){throw 'Wool private struct binding differs from native'}
 $original=[System.Collections.Generic.List[HarmonyLib.CodeInstruction]]::new()
 foreach($instruction in [HarmonyLib.PatchProcessor]::GetOriginalInstructions($sell)){$original.Add($instruction)}
 $transformed=@($wool.GetMethod('QuantityTap',$flags).Invoke($null,@(,$original)))
 if($transformed.Count -ne $original.Count+2){throw 'Wool quantity tap must add only DUP and void observation'}
 foreach($method in @($sell,$wool.GetMethod('LookupTarget',$flags).Invoke($null,@()))){
  if(-not ([HarmonyLib.Harmony]::GetPatchInfo($method).Owners -contains 'aoc.soak.supply.observer.v1')){throw "Missing wool decision hook $method"}
 }
 $market=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.SupplyMarketObserver',$true)
 foreach($method in $market.GetMethod('Targets',$flags).Invoke($null,@())){
  if(-not ([HarmonyLib.Harmony]::GetPatchInfo($method).Owners -contains 'aoc.soak.supply.observer.v1')){throw "Missing market observer $method"}
 }
 foreach($method in $observer.GetMethod('ScopeTargets',$flags).Invoke($null,@())){
  $info=[HarmonyLib.Harmony]::GetPatchInfo($method)
  if(-not ($info.Owners -contains 'aoc.soak.supply.observer.v1')){throw "Missing native scope patch $method"}
 }
 $workshops=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.SupplyWorkshopObserver',$true)
 foreach($case in @(@(0.5,0.75,0,0.25),@(0.75,0.25,1,0.5),@(2,0.25,2,1.25))){
  $actual=$workshops.GetMethod('DerivedIncrement',$flags).Invoke($null,@([single]$case[0],[single]$case[1],[int]$case[2]))
  if([Math]::Abs($actual-$case[3]) -gt 0.00001){throw 'Native progress accounting failed'}
 }
 foreach($method in $workshops.GetMethod('Targets',$flags).Invoke($null,@())){
  if(-not ([HarmonyLib.Harmony]::GetPatchInfo($method).Owners -contains 'aoc.soak.supply.observer.v1')){throw "Missing workshop hook $method"}
  $patch=[HarmonyLib.Harmony]::GetPatchInfo($method)
  if(@($patch.Transpilers | Where-Object {$_.owner -eq 'aoc.soak.supply.observer.v1'}).Count){throw 'Workshop observer must not rewrite native instructions'}
 }
} finally {$observer.GetMethod('Uninstall',$flags).Invoke($null,@())|Out-Null}

# Execute the same DUP + void-observer transform against a deterministic getter.
# It must preserve outcomes, getter count and exceptions, with no game/RNG setup.
Add-Type -ReferencedAssemblies @($harmonyPath,'C:/Program Files (x86)/Reference Assemblies/Microsoft/Framework/.NETFramework/v4.7.2/Facades/netstandard.dll') -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
public static class SupplyTapFixture {
 public static MethodInfo Transform;
 public static int Evaluations, Observations;
 public static bool Throw;
 public static float Seen;
 public static float Value { [MethodImpl(MethodImplOptions.NoInlining)] get { Evaluations++; if(Throw) throw new InvalidOperationException("native-sentinel"); return 0.2f; } }
 [MethodImpl(MethodImplOptions.NoInlining)] public static bool Check() { return Value < 0.15f; }
 public static void Observe(float value) { Observations++; Seen=value; }
 public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> code) {
  return (IEnumerable<CodeInstruction>)Transform.Invoke(null,new object[]{code,typeof(SupplyTapFixture).GetProperty("Value").GetGetMethod(),typeof(SupplyTapFixture).GetMethod("Observe")});
 }
}
'@
[SupplyTapFixture]::Transform=$taps.GetMethod('Tap',$flags)
$h=New-Object HarmonyLib.Harmony('aoc.supply.fixture')
try{
 $h.Patch([SupplyTapFixture].GetMethod('Check'),$null,$null,(New-Object HarmonyLib.HarmonyMethod([SupplyTapFixture].GetMethod('Transpile'))),$null)|Out-Null
 if([SupplyTapFixture]::Check() -or [SupplyTapFixture]::Evaluations -ne 1 -or [SupplyTapFixture]::Observations -ne 1 -or [SupplyTapFixture]::Seen -ne [single]0.2){throw 'Value tap changed outcome or evaluation count'}
 [SupplyTapFixture]::Throw=$true
 $thrown=$false
 try{[SupplyTapFixture]::Check()|Out-Null}catch{if($_.Exception.ToString().Contains('native-sentinel')){$thrown=$true}}
 if(-not $thrown -or [SupplyTapFixture]::Evaluations -ne 2 -or [SupplyTapFixture]::Observations -ne 1){throw 'Tap changed native exception semantics'}
 $original=[HarmonyLib.PatchProcessor]::GetOriginalInstructions([SupplyTapFixture].GetMethod('Check'))
 $changed=@([SupplyTapFixture]::Transpile($original))
 $rejected=$false
 try{[SupplyTapFixture]::Transpile($changed)|Out-Null}catch{$rejected=$true}
 if(-not $rejected){throw 'Double tap was accepted'}
} finally {$h.UnpatchAll('aoc.supply.fixture')}

$path=$capture.GetMethod('BeginSession',$flags).Invoke($null,@([string]$testRoot,[Func[double]]{42.5}))
$write=$capture.GetMethod('Write',$flags)
$write.Invoke($null,@('SESSION_START',[long]0,[long]0,'fixture','supply_v1',[double]0,[double]0,'synthetic'))|Out-Null
$write.Invoke($null,@('ITEM_PRODUCED',[long]0,[long]0,'village','wool',[double]0,[double]3,"safe`ttext"))|Out-Null
$capture.GetMethod('Stop',$flags).Invoke($null,@('fixture_completed'))|Out-Null
$capture.GetMethod('Stop',$flags).Invoke($null,@('duplicate'))|Out-Null
$rows=@(Import-Csv -LiteralPath $path -Delimiter "`t")
if($rows.Count -ne 4 -or $rows[-1].kind -ne 'SESSION_END' -or $rows[1].detail -ne 'safe text' -or [double]$rows[1].after -ne 3){throw 'Bounded writer serialization/closure failed'}
if($rows[2].kind -ne 'DIAGNOSTIC_COST' -or [double]$rows[2].after -lt 0 -or $rows[2].detail -notmatch 'discardedRows=0;'){throw 'Missing writer cost/drop receipt'}
# A nested root transaction is committed atomically, including when the cap hits.
foreach($overflow in @($false,$true)){
 $path=$capture.GetMethod('BeginSession',$flags).Invoke($null,@([string]$testRoot,[Func[double]]{42.5}))
 $write.Invoke($null,@('SESSION_START',[long]0,[long]0,'fixture','supply_v2',[double]0,[double]0,''))|Out-Null
 $write.Invoke($null,@('BEGIN',[long]1,[long]0,'fixture','outer',[double]0,[double]0,''))|Out-Null
 $write.Invoke($null,@('BEGIN',[long]2,[long]1,'fixture','inner',[double]0,[double]0,''))|Out-Null
 if($overflow){$capture.GetField('_bytes',$flags).SetValue($null,[long](5368709120-8192))}
 $write.Invoke($null,@('END',[long]2,[long]1,'fixture','inner',[double]0,[double]0,''))|Out-Null
 $write.Invoke($null,@('END',[long]1,[long]0,'fixture','outer',[double]0,[double]0,''))|Out-Null
 $capture.GetMethod('Stop',$flags).Invoke($null,@('fixture_completed'))|Out-Null
 $r=@(Import-Csv -LiteralPath $path -Delimiter "`t")
 $expected=7
 if($overflow){$expected=2}
 if($r.Count -ne $expected -or $r[-1].kind -ne 'SESSION_END'){throw 'Atomic scope closure failed'}
 for($i=0;$i -lt $r.Count;$i++){if([int]$r[$i].sequence -ne ($i+1)){throw 'Dropped scope left a sequence gap'}}
 if($overflow -and ($r[-1].metric -ne 'byte_limit_incomplete' -or $r[-1].detail -notmatch 'discardedUncommittedRows=3')){throw 'Cap dropped evidence without explicit incomplete receipt'}
}
foreach($entry in @(@(30,0,'day_target_reached_coverage_not_certified'),@(0,5368709120,'byte_limit_incomplete'))){
 $path=$capture.GetMethod('BeginSession',$flags).Invoke($null,@([string]$testRoot,[Func[double]]{42.5}))
 $capture.GetField('_startDay',$flags).SetValue($null,[double](42.5-$entry[0]))
 $capture.GetField('_bytes',$flags).SetValue($null,[long]$entry[1])
 $capture.GetMethod('Tick',$flags).Invoke($null,@())|Out-Null
 $r=@(Import-Csv -LiteralPath $path -Delimiter "`t")
 if($capture.GetProperty('Active',$flags).GetValue($null,$null) -or $r[-1].metric -ne $entry[2]){throw 'Actual limit did not close session with honest status'}
}
Add-Type -TypeDefinition @'
public static class CoreObservationFixture {
 public static FixtureCampaign Current { get; set; }
 public static bool ConfigurationValid { get; set; }
 public sealed class FixtureCampaign { public FixturePolicy Supply { get; set; } }
 public sealed class FixturePolicy { public string Revision { get; set; } }
}
'@
$observation=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.CampaignSystemsObservation',$true).GetMethod('WriteSnapshot',$flags)
$quests=[System.Collections.Generic.List[System.Collections.Generic.KeyValuePair[string,System.Nullable[double]]]]::new()
$quests.Add([System.Collections.Generic.KeyValuePair[string,System.Nullable[double]]]::new('finite',45.5))
$quests.Add([System.Collections.Generic.KeyValuePair[string,System.Nullable[double]]]::new('never',$null))
$quests.Add([System.Collections.Generic.KeyValuePair[string,System.Nullable[double]]]::new('expired',40.0))
[CoreObservationFixture]::Current=New-Object CoreObservationFixture+FixtureCampaign
[CoreObservationFixture]::Current.Supply=New-Object CoreObservationFixture+FixturePolicy
[CoreObservationFixture]::Current.Supply.Revision=('a'*64)
foreach($valid in @($true,$false)){
 [CoreObservationFixture]::ConfigurationValid=$valid
 $path=$capture.GetMethod('BeginSession',$flags).Invoke($null,@([string]$testRoot,[Func[double]]{42.5}))
 $observation.Invoke($null,@([CoreObservationFixture],[double]42.5,$quests))|Out-Null
 $capture.GetMethod('Stop',$flags).Invoke($null,@('fixture_completed'))|Out-Null
 $r=@(Import-Csv -LiteralPath $path -Delimiter "`t")
 $expected=if($valid){'valid'}else{'configuration_rejected'}
 if($r[0].metric -ne $expected -or $r[0].detail -notmatch ('policyRevision='+('a'*64))){throw 'Framework policy evidence incorrect'}
 if($r[1].detail -notmatch 'remainingDays=3;' -or $r[1].after -ne '45.5' -or $r[2].metric -ne 'never' -or $r[3].detail -notmatch 'remainingDays=0;'){throw 'Quest observation altered deadline/never/expired semantics'}
 if($r[4].kind -ne 'QUEST_COVERAGE' -or $r[4].after -ne '3'){throw 'Quest coverage count incorrect'}
}
$quests.Clear()
$path=$capture.GetMethod('BeginSession',$flags).Invoke($null,@([string]$testRoot,[Func[double]]{42.5}))
$observation.Invoke($null,@($null,[double]42.5,$quests))|Out-Null
$capture.GetMethod('Stop',$flags).Invoke($null,@('fixture_completed'))|Out-Null
$r=@(Import-Csv -LiteralPath $path -Delimiter "`t")
if($r[0].metric -ne 'missing' -or $r[1].metric -ne 'none_active'){throw 'Missing Core or empty quests fabricated coverage'}
$quests.Add([System.Collections.Generic.KeyValuePair[string,System.Nullable[double]]]::new('invalid',[double]::NaN))
$rejected=$false
try{$observation.Invoke($null,@($null,[double]42.5,$quests))|Out-Null}catch{if($_.Exception.ToString() -match 'Invalid quest deadline'){$rejected=$true}else{throw}}
if(-not $rejected){throw 'Nonfinite quest deadline accepted'}
'PASS: Core policy identity/rejection/missing states; finite, Never, expired and invalid quest observations; no-quest coverage gap.'
'PASS: installed native hooks, dispatch/load IL preflight, no duplicate getter evaluation, native exceptions, decision classification, capture limits and durable idempotent closure.'
"Synthetic evidence: $testRoot"
'NOT_EXERCISED: real production/load/sale conservation, completed routes, live third-party patches and overhead.'
