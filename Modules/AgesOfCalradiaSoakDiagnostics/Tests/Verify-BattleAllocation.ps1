param([string]$DiagnosticsAssemblyPath=(Join-Path (Split-Path -Parent $PSScriptRoot) 'bin\Win64_Shipping_Client\AgesOfCalradia.SoakDiagnostics.dll'))
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Verify-SupplyCapture.ps1') -DiagnosticsAssemblyPath $DiagnosticsAssemblyPath
$observer=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.SupplyBattleAllocationObserver',$true)
$targets=@($observer.GetMethod('Targets',$flags).Invoke($null,@()))
if($targets.Count -ne 2){throw 'Missing exact allocation targets'}
$naval=[Reflection.Assembly]::LoadFrom((Join-Path $BannerlordDir 'Modules/NavalDLC/bin/Win64_Shipping_Client/NavalDLC.dll'))
foreach($model in @($naval.GetType('NavalDLC.GameComponents.NavalDLCShipCostModel',$true),[TaleWorlds.CampaignSystem.GameComponents.DefaultShipCostModel])){
 $costTargets=@($observer.GetMethod('CostTargets',$flags).Invoke($null,@($model)))
 if($costTargets.Count -ne 2){throw 'Native ship model binding incomplete'}
}
$h=[HarmonyLib.Harmony]::new('aoc.allocation.fixture')
try {
 $observer.GetMethod('Install',$flags).Invoke($null,@($h))|Out-Null
 foreach($target in $targets){if(-not ([HarmonyLib.Harmony]::GetPatchInfo($target).Owners -contains 'aoc.allocation.fixture')){throw 'Missing allocation binding'}}
 $path=$capture.GetMethod('BeginSession',$flags).Invoke($null,@([string]$testRoot,[Func[double]]{42.5}))
 $event=[Runtime.Serialization.FormatterServices]::GetUninitializedObject([TaleWorlds.CampaignSystem.MapEvents.MapEvent])
 $party=[Runtime.Serialization.FormatterServices]::GetUninitializedObject([TaleWorlds.CampaignSystem.MapEvents.MapEventParty])
 $list=[TaleWorlds.Library.MBList[TaleWorlds.CampaignSystem.MapEvents.MapEventParty]]::new()
 # Empty native lists first exercise nested scopes without running gameplay.
 $before=$observer.GetMethod('Before',$flags);$after=$observer.GetMethod('After',$flags)
 $args=@($event,$list,$list,$targets[0],$null);$before.Invoke($null,$args)|Out-Null;$outer=$args[4]
 $args=@($event,$list,$list,$targets[1],$null);$before.Invoke($null,$args)|Out-Null;$inner=$args[4]
 $request=$capture.GetProperty('CloseRequestPath',$flags).GetValue($null,$null)
 New-Item -ItemType File -Path $request|Out-Null
 if($capture.GetMethod('TryRequestedClose',$flags).Invoke($null,@([Action]{}))){throw 'Requested close interrupted battle allocation'}
 if(-not (Test-Path -LiteralPath $request)){throw 'Deferred close request was acknowledged too early'}
 Remove-Item -LiteralPath $request
 Add-Type -TypeDefinition @'
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
public static class AllocationModelFixture {
 public static int ValueCalls, PenaltyCalls;
 [MethodImpl(MethodImplOptions.NoInlining)] public static float GetShipTradeValue(Ship s,PartyBase seller,PartyBase buyer) { ValueCalls++; return 123.5f; }
 [MethodImpl(MethodImplOptions.NoInlining)] public static float GetShipSellingPenalty() { PenaltyCalls++; return 0.8f; }
}
'@ -ReferencedAssemblies (@([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object {$_.GetName().Name.StartsWith('TaleWorlds.')} | ForEach-Object Location) + @('C:/Program Files (x86)/Reference Assemblies/Microsoft/Framework/.NETFramework/v4.7.2/Facades/netstandard.dll'))
 $h.Patch([AllocationModelFixture].GetMethod('GetShipTradeValue'),$null,[HarmonyLib.HarmonyMethod]::new($observer.GetMethod('Value',$flags)))|Out-Null
 $h.Patch([AllocationModelFixture].GetMethod('GetShipSellingPenalty'),$null,[HarmonyLib.HarmonyMethod]::new($observer.GetMethod('Penalty',$flags)))|Out-Null
 $ship=[Runtime.Serialization.FormatterServices]::GetUninitializedObject([TaleWorlds.CampaignSystem.Naval.Ship])
 if([AllocationModelFixture]::GetShipTradeValue($ship,$null,$null) -ne [single]123.5 -or [AllocationModelFixture]::ValueCalls -ne 1){throw 'Ship value altered or evaluated twice'}
 if([AllocationModelFixture]::GetShipSellingPenalty() -ne [single]0.8 -or [AllocationModelFixture]::PenaltyCalls -ne 1){throw 'Penalty altered or evaluated twice'}
 $after.Invoke($null,@($inner,$null,$true,$targets[1]))|Out-Null
 if($observer.GetField('_current',$flags).GetValue($null) -ne $outer){throw 'Nested allocation context not restored'}
 $after.Invoke($null,@($outer,$null,$false,$targets[0]))|Out-Null
 if($null -ne $observer.GetField('_current',$flags).GetValue($null)){throw 'Allocation context leaked'}
 # Stable commit join exists even when allocation predates capture; missing event
 # is explicit, never assigned a fabricated event identity.
 $context=$observer.GetMethod('PartyContext',$flags)
 $first=$context.Invoke($null,@($party));$second=$context.Invoke($null,@($party))
 if($first -ne $second -or $first -notmatch 'mapEvent=unobserved'){throw 'Unobserved allocation incorrectly attributed'}
 $nativeParty=[Runtime.Serialization.FormatterServices]::GetUninitializedObject([TaleWorlds.CampaignSystem.Party.PartyBase])
 [HarmonyLib.AccessTools]::Field([TaleWorlds.CampaignSystem.Party.PartyBase],'_ships').SetValue($nativeParty,[TaleWorlds.Library.MBList[TaleWorlds.CampaignSystem.Naval.Ship]]::new())
 [HarmonyLib.AccessTools]::Field([TaleWorlds.CampaignSystem.MapEvents.MapEventParty],'<Party>k__BackingField').SetValue($party,$nativeParty)
 $winners=[TaleWorlds.Library.MBList[TaleWorlds.CampaignSystem.MapEvents.MapEventParty]]::new();$winners.Add($party)
 $args=@($event,$winners,$list,$targets[0],$null);$before.Invoke($null,$args)|Out-Null
 [HarmonyLib.AccessTools]::Field([TaleWorlds.CampaignSystem.MapEvents.MapEventParty],'<PlunderedGold>k__BackingField').SetValue($party,123)
 $after.Invoke($null,@($args[4],$null,$true,$targets[0]))|Out-Null
 $linked=$context.Invoke($null,@($party))
 if($linked -notmatch 'mapEvent=1' -or $linked -notmatch 'mapParty=1'){throw 'Allocation identity not linked to later commit context'}
 $capture.GetMethod('Stop',$flags).Invoke($null,@('fixture_completed'))|Out-Null
 $rows=@(Import-Csv -LiteralPath $path -Delimiter "`t")
 if(@($rows|Where-Object kind -eq 'BATTLE_ALLOCATION_BEGIN').Count -ne 3 -or @($rows|Where-Object kind -eq 'BATTLE_ALLOCATION_END').Count -ne 3){throw 'Unbalanced allocations'}
 $snapshots=@($rows|Where-Object kind -eq 'BATTLE_ALLOCATION_PARTY')
 if($snapshots.Count -ne 2 -or $snapshots[0].after -ne '0' -or $snapshots[1].after -ne '123'){throw 'Native party allocation delta not captured'}
 if(@($rows|Where-Object kind -eq 'BATTLE_ALLOCATION_VALUE').Count -ne 1 -or @($rows|Where-Object kind -eq 'BATTLE_ALLOCATION_PENALTY').Count -ne 1){throw 'Missing actual evaluation evidence'}
 if(@($rows|Where-Object { $_.kind -eq 'BATTLE_ALLOCATION_END' -and $_.detail -match 'originalRan=False' }).Count -ne 1){throw 'Skipped native body treated as executed'}
 $capture.GetMethod('BeginSession',$flags).Invoke($null,@([string]$testRoot,[Func[double]]{42.5}))|Out-Null
 $args=@($event,$list,$list,$targets[0],$null);$before.Invoke($null,$args)|Out-Null
 $after.Invoke($null,@($args[4],[InvalidOperationException]::new('synthetic native exception'),$true,$targets[0]))|Out-Null
 if($after.ReturnType -ne [void] -or $capture.GetProperty('Active',$flags).GetValue($null,$null) -or $null -ne $observer.GetField('_current',$flags).GetValue($null)){throw 'Native exception not preserved or context leaked'}
} finally { $h.UnpatchAll('aoc.allocation.fixture');$observer.GetMethod('Reset',$flags).Invoke($null,@())|Out-Null }
'PASS: exact native allocation bindings, nested/skipped/error scopes, stable missing-coverage join, one-time model results. Not live naval acceptance.'
