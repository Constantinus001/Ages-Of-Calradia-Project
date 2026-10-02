param(
 [string]$DiagnosticsAssemblyPath=(Join-Path (Split-Path -Parent $PSScriptRoot) 'bin/Win64_Shipping_Client/AgesOfCalradia.SoakDiagnostics.dll'),
 [string]$SystemsAssemblyPath=(Join-Path $PSScriptRoot '../../AgesOfCalradiaCampaignSystems/bin/Win64_Shipping_Client/AgesOfCalradia.CampaignSystems.dll')
)
# Synthetic target only; real shipped patch methods, isolated PowerShell process.
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
. (Join-Path $repo 'Modules/AgesOfCalradiaSoakDiagnostics/Tests/Verify-SupplyCapture.ps1') -DiagnosticsAssemblyPath $DiagnosticsAssemblyPath
$systems=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $SystemsAssemblyPath).Path)
$guard=$systems.GetType('AgesOfCalradia.CampaignSystems.NavalDuplicateRewardGuard',$true)
$reward=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.SupplyRewardObserver',$true)
$cash=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.SupplyCashObserver',$true)
$instanceFlags=[Reflection.BindingFlags]'Instance,NonPublic,Public'
$fixtureRefs=@([AppDomain]::CurrentDomain.GetAssemblies()|Where-Object {$_.GetName().Name -like 'TaleWorlds.*'}|ForEach-Object {$_.Location})
$fixtureRefs+='C:/Program Files (x86)/Reference Assemblies/Microsoft/Framework/.NETFramework/v4.7.2/Facades/netstandard.dll'
Add-Type -ReferencedAssemblies $fixtureRefs -TypeDefinition @'
using System;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem.Party;
public static class NavalAuditTarget {
 public static int Calls;
 public static bool Fail;
 public static bool SkipRequested;
 public static bool CompetingPrefix() { return !SkipRequested; }
 [MethodImpl(MethodImplOptions.NoInlining)]
 public static void DistributePartyShipsAndRecoverGold(MobileParty party) {
  Calls++;
  if(Fail) throw new InvalidOperationException("synthetic native failure");
 }
}

'@
function New-AuditParty {
 $party=[Runtime.Serialization.FormatterServices]::GetUninitializedObject([TaleWorlds.CampaignSystem.Party.MobileParty])
 $partyBase=[Runtime.Serialization.FormatterServices]::GetUninitializedObject([TaleWorlds.CampaignSystem.Party.PartyBase])
 $partyField=@($party.GetType().GetFields($instanceFlags)|Where-Object FieldType -eq ([TaleWorlds.CampaignSystem.Party.PartyBase]))
 if($partyField.Count -ne 1){throw 'Ambiguous native Party field'}
 $partyField[0].SetValue($party,$partyBase)
 $shipsField=@($partyBase.GetType().GetFields($instanceFlags)|Where-Object {$_.Name -eq '_ships'})
 if($shipsField.Count -ne 1){throw 'Missing native ships backing field'}
 $shipsField[0].SetValue($partyBase,[Activator]::CreateInstance($shipsField[0].FieldType))
 return $party
}
$target=[NavalAuditTarget].GetMethod('DistributePartyShipsAndRecoverGold')
foreach($guardFirst in @($true,$false)){
 $id='aoc.investigation.naval.'+$guardFirst
 $h=[HarmonyLib.Harmony]::new($id)
 $guard.GetMethod('Reset',$flags).Invoke($null,@())|Out-Null
 $reward.GetMethod('Reset',$flags).Invoke($null,@())|Out-Null
 $cash.GetMethod('Reset',$flags).Invoke($null,@())|Out-Null
 $path=$capture.GetMethod('BeginSession',$flags).Invoke($null,@([string]$testRoot,[Func[double]]{42.5}))
 try {
  $gp=[HarmonyLib.HarmonyMethod]::new($guard.GetMethod('Prefix',$flags))
  $rp=[HarmonyLib.HarmonyMethod]::new($reward.GetMethod('NavalBefore',$flags))
  $gp.priority=if($guardFirst){800}else{0}
  $rp.priority=if($guardFirst){0}else{800}
  $h.Patch($target,$gp,$null,$null,[HarmonyLib.HarmonyMethod]::new($guard.GetMethod('Finalizer',$flags)))|Out-Null
  $h.Patch($target,$rp,$null,$null,[HarmonyLib.HarmonyMethod]::new($reward.GetMethod('After',$flags)))|Out-Null
  $party=New-AuditParty
  [NavalAuditTarget]::Calls=0
  [NavalAuditTarget]::Fail=$false
  [NavalAuditTarget]::DistributePartyShipsAndRecoverGold($party)
  [NavalAuditTarget]::DistributePartyShipsAndRecoverGold($party)
  if([NavalAuditTarget]::Calls -ne 1){throw 'Guard failed to suppress duplicate synthetic original'}
  if(-not $capture.GetProperty('Active',$flags).GetValue($null,$null)){throw 'Observation failed during coexistence'}
  if($reward.GetProperty('Context',$flags).GetValue($null,$null) -ne '; reward=none'){throw 'Reward context leaked'}
  $capture.GetMethod('Stop',$flags).Invoke($null,@('synthetic_coexistence_complete'))|Out-Null
  $rows=@(Import-Csv -LiteralPath $path -Delimiter "`t")
  $begins=@($rows|Where-Object kind -eq 'REWARD_BEGIN').Count
  $ends=@($rows|Where-Object kind -eq 'REWARD_END').Count
  if($begins -ne $ends){throw 'Unbalanced scopes'}
  $executed=@($rows|Where-Object {$_.kind -eq 'REWARD_END' -and $_.detail -match 'originalRan=True'}).Count
  $skipped=@($rows|Where-Object {$_.kind -eq 'REWARD_SKIPPED' -or ($_.kind -eq 'REWARD_END' -and $_.detail -match 'originalRan=False')}).Count
  if($executed -ne 1 -or $skipped -ne 1){throw 'Skipped original was misreported as an executed reward'}
  Write-Output "COEXISTENCE guardFirst=$guardFirst originalCalls=$([NavalAuditTarget]::Calls) observedScopes=$begins contextRestored=True"
 } finally {$h.UnpatchAll($id)}
}
# Hypothetical third-party skip, not evidence of that event in the live capture.
$id='aoc.investigation.naval.competing-skip'
$h=[HarmonyLib.Harmony]::new($id)
try {
 $guard.GetMethod('Reset',$flags).Invoke($null,@())|Out-Null
 $gp=[HarmonyLib.HarmonyMethod]::new($guard.GetMethod('Prefix',$flags));$gp.priority=800
 $skip=[HarmonyLib.HarmonyMethod]::new([NavalAuditTarget].GetMethod('CompetingPrefix'));$skip.priority=0
 $h.Patch($target,$gp,$null,$null,[HarmonyLib.HarmonyMethod]::new($guard.GetMethod('Finalizer',$flags)))|Out-Null
 $h.Patch($target,$skip)|Out-Null
 $party=New-AuditParty
 [NavalAuditTarget]::Calls=0
 [NavalAuditTarget]::SkipRequested=$true
 [NavalAuditTarget]::DistributePartyShipsAndRecoverGold($party)
 [NavalAuditTarget]::SkipRequested=$false
 [NavalAuditTarget]::DistributePartyShipsAndRecoverGold($party)
 if([NavalAuditTarget]::Calls -ne 1){throw 'Competing prefix skip incorrectly latched the guard'}
 Write-Output 'PASS: a competing prefix skip releases the new gate and a later original executes once.'
 $guard.GetMethod('Reset',$flags).Invoke($null,@())|Out-Null
 [NavalAuditTarget]::Calls=0
 [NavalAuditTarget]::Fail=$true
 $threw=$false
 try {[NavalAuditTarget]::DistributePartyShipsAndRecoverGold($party)}catch{$threw=$true}
 if(-not $threw){throw 'Synthetic native exception suppressed'}
 [NavalAuditTarget]::Fail=$false
 [NavalAuditTarget]::DistributePartyShipsAndRecoverGold($party)
 if([NavalAuditTarget]::Calls -ne 2){throw 'Native failure did not allow retry'}
 Write-Output 'PASS: real guard finalizer preserves native failure and permits retry on the synthetic target.'
}finally{$h.UnpatchAll($id)}
