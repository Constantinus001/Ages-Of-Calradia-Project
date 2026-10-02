param([string]$DiagnosticsAssemblyPath=(Join-Path (Split-Path -Parent $PSScriptRoot) 'bin\Win64_Shipping_Client\AgesOfCalradia.SoakDiagnostics.dll'))
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Verify-SupplyCapture.ps1') -DiagnosticsAssemblyPath $DiagnosticsAssemblyPath
$game='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord'
[Reflection.Assembly]::LoadFrom((Join-Path $game 'bin\Win64_Shipping_Client\TaleWorlds.LinQuick.dll'))|Out-Null
$naval=[Reflection.Assembly]::LoadFrom((Join-Path $game 'Modules\NavalDLC\bin\Win64_Shipping_Client\NavalDLC.dll'))
$reward=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.SupplyRewardObserver',$true)
$targets=@($reward.GetMethod('Targets',$flags).Invoke($null,@($naval)))
if($targets.Count -ne 5){throw 'Expected battle, naval distribution/recovery, valuation and ship destruction boundaries'}
$h=[HarmonyLib.Harmony]::new('aoc.reward.fixture')
try{
 $reward.GetMethod('Install',$flags).Invoke($null,@($h))|Out-Null
 foreach($target in $targets){
  if(-not ([HarmonyLib.Harmony]::GetPatchInfo($target).Owners -contains 'aoc.reward.fixture')){throw 'Missing reward target'}
 }
 $path=$capture.GetMethod('BeginSession',$flags).Invoke($null,@([string]$testRoot,[Func[double]]{42.5}))
 $callType=$reward.GetNestedType('Call',[Reflection.BindingFlags]'NonPublic')
 $instanceFlags=[Reflection.BindingFlags]'Instance,NonPublic'
 $outer=[Activator]::CreateInstance($callType,$true)
 $inner=[Activator]::CreateInstance($callType,$true)
 $callType.GetField('Stage',$instanceFlags).SetValue($outer,'outer')
 $callType.GetField('Stage',$instanceFlags).SetValue($inner,'RecoverGoldFromRemainingShipsAfterDistribution')
 $begin=$reward.GetMethod('Begin',$flags);$after=$reward.GetMethod('After',$flags)
 $begin.Invoke($null,@($outer))|Out-Null
 $outerContext=$reward.GetProperty('Context',$flags).GetValue($null,$null)
 $begin.Invoke($null,@($inner))|Out-Null
 Add-Type -TypeDefinition @'
using System.Runtime.CompilerServices;
public static class PenaltyObservationFixture {
 public static int Calls;
 [MethodImpl(MethodImplOptions.NoInlining)] public static float GetShipSellingPenalty() { Calls++; return 0.8f; }
}
'@
 $h.Patch([PenaltyObservationFixture].GetMethod('GetShipSellingPenalty'),$null,[HarmonyLib.HarmonyMethod]::new($reward.GetMethod('Penalty',$flags)))|Out-Null
 if([PenaltyObservationFixture]::GetShipSellingPenalty() -ne [single]0.8 -or [PenaltyObservationFixture]::Calls -ne 1){throw 'Penalty observer changed result or evaluated twice'}
 $ship=[Runtime.Serialization.FormatterServices]::GetUninitializedObject([TaleWorlds.CampaignSystem.Naval.Ship])
 $reward.GetMethod('Valuation',$flags).Invoke($null,@($ship,[single]123.5))|Out-Null
 $after.Invoke($null,@($inner,$null,$true,$targets[0]))|Out-Null
 if($reward.GetProperty('Context',$flags).GetValue($null,$null) -ne $outerContext){throw 'Nested reward context not restored'}
 $after.Invoke($null,@($outer,$null,$true,$targets[0]))|Out-Null
 if($reward.GetProperty('Context',$flags).GetValue($null,$null) -ne '; reward=none'){throw 'Reward context leaked'}
 $reward.GetMethod('ShipDestroyed',$flags).Invoke($null,@($ship,$null,$true))|Out-Null
 $capture.GetMethod('Stop',$flags).Invoke($null,@('fixture_completed'))|Out-Null
 $rows=@(Import-Csv -LiteralPath $path -Delimiter "`t")
 $values=@($rows|Where-Object kind -eq 'REWARD_VALUATION')
 $penalties=@($rows|Where-Object kind -eq 'REWARD_PENALTY')
 if($penalties.Count -ne 1 -or [Math]::Abs([double]$penalties[0].after-0.8) -gt 0.00001 -or $penalties[0].detail -notmatch 'originalRan=True'){throw 'Evaluated penalty missing/changed'}
 if($values.Count -ne 1 -or $values[0].after -ne '123.5'){throw 'Evaluated value duplicated or altered'}
 $destroyed=@($rows|Where-Object kind -eq 'SHIP_DESTRUCTION')
 if($destroyed.Count -ne 1 -or $destroyed[0].detail -notmatch 'ship=1;' -or $destroyed[0].detail -notmatch 'reward=none'){throw 'Post-reward destruction missing stable identity'}
 if(@($rows|Where-Object kind -eq 'REWARD_BEGIN').Count -ne 2 -or @($rows|Where-Object kind -eq 'REWARD_END').Count -ne 2){throw 'Unbalanced reward observations'}
 $capture.GetMethod('BeginSession',$flags).Invoke($null,@([string]$testRoot,[Func[double]]{42.5}))|Out-Null
 $begin.Invoke($null,@($outer))|Out-Null
 $nativeFailure=[InvalidOperationException]::new('synthetic native reward failure')
 $after.Invoke($null,@($outer,$nativeFailure,$true,$targets[0]))|Out-Null
 if($after.ReturnType -ne [void] -or $capture.GetProperty('Active',$flags).GetValue($null,$null) -or
    $reward.GetProperty('Context',$flags).GetValue($null,$null) -ne '; reward=none'){throw 'Exception finalizer failed to close evidence and clear context'}
}finally{$h.UnpatchAll('aoc.reward.fixture');$reward.GetMethod('Reset',$flags).Invoke($null,@())|Out-Null}
'PASS: installed native target binding, optional naval valuation target, unchanged observed value and nested reward cleanup. Not live payout/asset acceptance.'
