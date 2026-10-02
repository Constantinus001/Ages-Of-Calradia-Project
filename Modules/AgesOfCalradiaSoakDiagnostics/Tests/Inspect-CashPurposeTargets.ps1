param([string]$BannerlordDir='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord', [string]$DetailMethod='')
$ErrorActionPreference='Stop'
foreach($name in @('TaleWorlds.Library','TaleWorlds.Localization','TaleWorlds.ObjectSystem','TaleWorlds.SaveSystem','TaleWorlds.Core','TaleWorlds.CampaignSystem')){
 [Reflection.Assembly]::LoadFrom((Join-Path $BannerlordDir ('bin\Win64_Shipping_Client\'+$name+'.dll')))|Out-Null
}
[Reflection.Assembly]::LoadFrom((Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.2\lib\net472\0Harmony.dll'))|Out-Null
$types=@([TaleWorlds.CampaignSystem.Actions.GiveGoldAction],[TaleWorlds.CampaignSystem.GameComponents.DefaultClanFinanceModel],[TaleWorlds.CampaignSystem.CampaignBehaviors.ClanVariablesCampaignBehavior])
foreach($type in $types){
 foreach($method in $type.GetMethods([Reflection.BindingFlags]'Public,NonPublic,Static,Instance,DeclaredOnly')){
  if($method.Name -notmatch 'Apply|Wage|Tribute|Income|Expense|DailyTick'){continue}
  Write-Output ($type.Name+'::'+$method)
  if($method.Name -eq $DetailMethod){
   $method.GetParameters()|ForEach-Object {Write-Output (' ARG '+$_.Position+' '+$_.Name)}
   [HarmonyLib.PatchProcessor]::GetOriginalInstructions($method)|ForEach-Object{Write-Output ($_.opcode.ToString()+' '+$_.operand)}
  }
  foreach($i in [HarmonyLib.PatchProcessor]::GetOriginalInstructions($method)){
   if($i.operand -is [Reflection.MethodInfo] -and $i.operand.Name -match 'Gold|Wage|Tribute|Income|Expense|Budget|Apply'){
    Write-Output ('  '+$i.opcode+' '+$i.operand.DeclaringType.Name+'::'+$i.operand)
   }
  }
 }
}
