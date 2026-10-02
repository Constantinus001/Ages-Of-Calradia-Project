param([string]$AssemblyPath=(Join-Path (Split-Path $PSScriptRoot) 'bin\Win64_Shipping_Client\AgesOfCalradia.CampaignSystems.dll'))
$ErrorActionPreference='Stop'
$game='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord'
$gameBin=Join-Path $game 'bin\Win64_Shipping_Client'
foreach($name in @('TaleWorlds.Library','TaleWorlds.DotNet','TaleWorlds.ScreenSystem','TaleWorlds.Localization','TaleWorlds.ObjectSystem','TaleWorlds.Core','TaleWorlds.CampaignSystem','TaleWorlds.MountAndBlade')){
 [Reflection.Assembly]::LoadFrom((Join-Path $gameBin ($name+'.dll')))|Out-Null
}
[Reflection.Assembly]::LoadFrom((Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.2\lib\net472\0Harmony.dll'))|Out-Null
$naval=[Reflection.Assembly]::LoadFrom((Join-Path $game 'Modules\NavalDLC\bin\Win64_Shipping_Client\NavalDLC.dll'))
$type=$naval.GetType('NavalDLC.CampaignBehaviors.NavalShipDistributionCampaignBehavior',$true)
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
$target=$type.GetMethod('DistributePartyShipsAndRecoverGold',$flags,$null,@([TaleWorlds.CampaignSystem.Party.MobileParty]),$null)
if($null -eq $target){throw 'Bannerlord 1.4.8 NavalDLC distribution target missing'}
foreach($callerName in @('OnMobilePartyDestroyed','OnPartyDisbanded')){
 $caller=$type.GetMethod($callerName,$flags)
 if($null -eq $caller){throw "Naval callback missing: $callerName"}
 $calls=@([HarmonyLib.PatchProcessor]::GetOriginalInstructions($caller)|Where-Object {$_.operand -is [Reflection.MethodInfo] -and $_.operand -eq $target})
 if($calls.Count -ne 1){throw "Expected exactly one $callerName distribution call"}
}
$guardAssembly=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath))
$guard=$guardAssembly.GetType('AgesOfCalradia.CampaignSystems.NavalDuplicateRewardGuard',$true)
$static=[Reflection.BindingFlags]'Static,NonPublic'
$reset=$guard.GetMethod('Reset',$static);$enter=$guard.GetMethod('TryEnter',$static);$finish=$guard.GetMethod('Finish',$static)
$reset.Invoke($null,@())|Out-Null
$party=New-Object object
if(-not $enter.Invoke($null,@($party))){throw 'First native distribution was blocked'}
if($enter.Invoke($null,@($party))){throw 'Duplicate native distribution was allowed'}
$finish.Invoke($null,@($party,$null))|Out-Null
if($enter.Invoke($null,@($party))){throw 'Completed distribution became eligible again'}
$retry=New-Object object
if(-not $enter.Invoke($null,@($retry))){throw 'Retry fixture first call blocked'}
$failure=[InvalidOperationException]::new('native failure')
$failureArguments=New-Object object[] 2
$failureArguments[0]=$retry
$failureArguments[1]=$failure
$finish.Invoke($null,$failureArguments)|Out-Null
if(-not $enter.Invoke($null,@($retry))){throw 'Native failure was incorrectly converted into a permanent completion'}
'PASS: audited both native duplicate callers and verified first-only/safe-native-failure guard behavior.'
