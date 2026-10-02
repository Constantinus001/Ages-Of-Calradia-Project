$ErrorActionPreference='Stop'
$game='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord'
foreach($name in @('TaleWorlds.Library','TaleWorlds.LinQuick','TaleWorlds.DotNet','TaleWorlds.ScreenSystem','TaleWorlds.Localization','TaleWorlds.ObjectSystem','TaleWorlds.SaveSystem','TaleWorlds.Core','TaleWorlds.Engine','TaleWorlds.CampaignSystem','TaleWorlds.MountAndBlade')){
 [Reflection.Assembly]::LoadFrom((Join-Path $game ('bin\Win64_Shipping_Client\'+$name+'.dll')))|Out-Null
}
$naval=[Reflection.Assembly]::LoadFrom((Join-Path $game 'Modules\NavalDLC\bin\Win64_Shipping_Client\NavalDLC.dll'))
$types=@([TaleWorlds.CampaignSystem.MapEvents.MapEventParty],$naval.GetType('NavalDLC.CampaignBehaviors.NavalShipDistributionCampaignBehavior',$true),[TaleWorlds.CampaignSystem.Naval.Ship],[TaleWorlds.CampaignSystem.Party.MobileParty])
$flags=[Reflection.BindingFlags]'Instance,Static,Public,NonPublic,DeclaredOnly'
foreach($type in $types){
 $type.FullName
 $type.GetMethods($flags)|Where-Object {$_.Name -match 'Gold|Ship|Distribut'}|ForEach-Object {$_.ToString()}
 $type.GetFields($flags)|ForEach-Object {'FIELD '+$_.FieldType.FullName+' '+$_.Name}
 $type.GetProperties($flags)|ForEach-Object {'PROPERTY '+$_.PropertyType.FullName+' '+$_.Name}
}
[Reflection.Assembly]::LoadFrom((Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.2\lib\net472\0Harmony.dll'))|Out-Null
$navalType=$types[1]
foreach($method in $navalType.GetMethods($flags)){
 if($null -eq $method.GetMethodBody()){continue}
 $instructions=@([HarmonyLib.PatchProcessor]::GetOriginalInstructions($method))
 $callsRecovery=@($instructions|Where-Object {$_.operand -is [Reflection.MethodInfo] -and $_.operand.Name -eq 'DistributePartyShipsAndRecoverGold'})
 if($callsRecovery.Count -gt 0){
  'RECOVERY CALLER IL '+$method.Name
  $instructions|ForEach-Object {$_.ToString()}
 }
}
$closure=$types[1].GetNestedTypes([Reflection.BindingFlags]'NonPublic')|Where-Object {$_.Name -eq '<>c__DisplayClass5_0'}
foreach($type in @([TaleWorlds.CampaignSystem.Actions.DestroyPartyAction],[TaleWorlds.CampaignSystem.Party.MobileParty])){
 foreach($method in $type.GetMethods($flags)|Where-Object {$_.Name -in @('ApplyInternal','RemoveParty')}){
  'CLEANUP IL '+$method.DeclaringType.FullName+'.'+$method.Name
  $instructions=@([HarmonyLib.PatchProcessor]::GetOriginalInstructions($method))
  $instructions|ForEach-Object {$_.ToString()}
  if($type -eq [TaleWorlds.CampaignSystem.Actions.DestroyPartyAction]){
   $calls=@($instructions|Where-Object {$_.operand -is [Reflection.MethodInfo]}|ForEach-Object {$_.operand.Name})
   $eventIndex=[Array]::IndexOf($calls,'OnMobilePartyDestroyed')
   $removeIndex=[Array]::IndexOf($calls,'RemoveParty')
   if($eventIndex -lt 0 -or $removeIndex -le $eventIndex){throw 'Native party destruction event/cleanup order changed'}
  }else{
   $shipCleanup=@($instructions|Where-Object {$_.operand -is [Reflection.MethodInfo] -and $_.operand.DeclaringType -eq [TaleWorlds.CampaignSystem.Actions.DestroyShipAction] -and $_.operand.Name -eq 'Apply'})
   if($shipCleanup.Count -ne 1){throw 'Native party removal ship cleanup changed'}
  }
 }
}
foreach($method in $closure.GetMethods($flags)|Where-Object {$_.Name -match 'RecoverGold.*b__0'}){
 'VALUATION IL'
 [HarmonyLib.PatchProcessor]::GetOriginalInstructions($method)|ForEach-Object {$_.ToString()}
}
foreach($type in $types[0..1]){
 foreach($method in $type.GetMethods($flags)|Where-Object {$_.Name -in @('CommitGoldChanges','RecoverGoldFromRemainingShipsAfterDistribution','DistributePartyShipsAndRecoverGold')}){
  'IL '+$method.DeclaringType.FullName+'.'+$method.Name
  [HarmonyLib.PatchProcessor]::GetOriginalInstructions($method)|ForEach-Object {$_.ToString()}
 }
}
