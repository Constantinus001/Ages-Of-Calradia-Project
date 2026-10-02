param(
 [string]$BannerlordDir='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord',
 [string]$ModuleRoot=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
 [string]$HarmonyPath=(Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.2\lib\net472\0Harmony.dll'),
 [string]$SidecarPath=(Join-Path $PSScriptRoot 'bin\Win64_Shipping_Client\AgesOfCalradia.Approved560CalendarFixes.dll')
)
$ErrorActionPreference='Stop'
[Diagnostics.Trace]::Listeners.Add([Diagnostics.ConsoleTraceListener]::new())|Out-Null
. (Join-Path $PSScriptRoot 'Verify-Approved560CalendarFixes.ps1') -BannerlordDir $BannerlordDir -ModuleRoot $ModuleRoot -HarmonyPath $HarmonyPath -SidecarPath $SidecarPath
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$quote=$sidecar.GetType('AgesOfCalradia.Approved560CalendarFixes.WorkshopApprovalQuoteFix',$true)
$cycle=$sidecar.GetType('AgesOfCalradia.Approved560CalendarFixes.WorkshopApprovalCycle',$true)
foreach($capital in @($false,$true)){foreach($started in @($false,$true)){
 if($quote.GetMethod('UsePaymentQuote',$flags).Invoke($null,@($capital,$started)) -ne ($capital -and $started)){throw 'Initialization/capital guard changed'}
}}
foreach($value in @(0,1,999,1000,1001,2000,-1)){
 if($quote.GetMethod('NormalizeQuote',$flags).Invoke($null,@($value,$true)) -ne [Math]::Min(1000,$value)){throw 'Payment cap differs from native'}
 if($quote.GetMethod('NormalizeQuote',$flags).Invoke($null,@($value,$false)) -ne $value){throw 'Unrelated quote altered'}
}
$target=$quote.GetMethod('TargetMethod',$flags).Invoke($null,@())
$original=[Collections.Generic.List[HarmonyLib.CodeInstruction]]::new()
foreach($i in [HarmonyLib.PatchProcessor]::GetOriginalInstructions($target)){$original.Add($i)}
$transpiler=$quote.GetMethod('Transpiler',$flags)
$changed=@($transpiler.Invoke($null,(,$original)))
if($changed.Count -ne $original.Count){throw 'Native control flow resized'}
$changes=0
for($i=0;$i -lt $original.Count;$i++){
 if($original[$i].opcode -ne $changed[$i].opcode -or $original[$i].operand -ne $changed[$i].operand){$changes++}
 if(($original[$i].labels -join ',') -ne ($changed[$i].labels -join ',') -or ($original[$i].blocks -join ',') -ne ($changed[$i].blocks -join ',')){throw 'Native branch/exception metadata changed'}
}
if($changes -ne 1){throw 'More than one price call replaced'}
foreach($bad in @([Collections.Generic.List[HarmonyLib.CodeInstruction]]::new(),[Collections.Generic.List[HarmonyLib.CodeInstruction]]::new([HarmonyLib.CodeInstruction[]]$changed))){
 $rejected=$false; try{$transpiler.Invoke($null,(,$bad))|Out-Null}catch{$rejected=$true}
 if(-not $rejected){throw 'Missing/already modified quote accepted'}
}
Add-Type -TypeDefinition @'
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
public static class ApprovalCycleFixture {
 public static FieldInfo Context;
 [MethodImpl(MethodImplOptions.NoInlining)] public static bool Run(object a, object b, bool capital) {
  if (a != null) throw new InvalidOperationException("approval-native-sentinel");
  return (bool)Context.GetValue(null);
 }
}
'@
[ApprovalCycleFixture]::Context=$quote.GetField('CapitalCycle',$flags)
$h=[HarmonyLib.Harmony]::new('aoc.approval.fixture')
try{
 $h.Patch([ApprovalCycleFixture].GetMethod('Run'),[HarmonyLib.HarmonyMethod]::new($cycle.GetMethod('Prefix',$flags)),$null,$null,[HarmonyLib.HarmonyMethod]::new($cycle.GetMethod('Finalizer',$flags)))|Out-Null
 foreach($initial in @($false,$true)){
  [ApprovalCycleFixture]::Context.SetValue($null,$initial)
  foreach($capital in @($false,$true)){
   if([ApprovalCycleFixture]::Run($null,$null,$capital) -ne $capital){throw 'Context did not match active cycle'}
   if([ApprovalCycleFixture]::Context.GetValue($null) -ne $initial){throw 'Nested context leaked'}
  }
  $caught=$false
  try{[ApprovalCycleFixture]::Run('throw',$null,$true)|Out-Null}catch{if($_.Exception.ToString().Contains('approval-native-sentinel')){$caught=$true}else{throw}}
  if(-not $caught -or [ApprovalCycleFixture]::Context.GetValue($null) -ne $initial){throw 'Exception swallowed or context leaked'}
 }
}finally{$h.UnpatchAll('aoc.approval.fixture'); [ApprovalCycleFixture]::Context.SetValue($null,$false)}
'PASS: native installation, one quote replacement, cap/initialization guards, unchanged control flow, duplicate rejection and exception-safe nested context.'
'LIMIT: locked quotes are not cash escrow; third-party cash changes and campaign integration still require live validation.'
