param(
 [string]$BannerlordDir='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord',
 [string]$ModuleRoot=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
 [string]$HarmonyPath=(Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.2\lib\net472\0Harmony.dll')
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Verify-WorkshopRecipeCadence.ps1') -BannerlordDir $BannerlordDir -ModuleRoot $ModuleRoot -HarmonyPath $HarmonyPath
$policy=$sidecar.GetType('AgesOfCalradia.Approved560CalendarFixes.FoodAwareVillageProductionFix',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$apply=$policy.GetMethod('ApplyPolicy',$flags)
$classify=$policy.GetMethod('UsesNativeCadence',$flags)
$bridge=$sidecar.GetType('AgesOfCalradia.Approved560CalendarFixes.ApprovedCalendarBridge',$true)
$factor=[single]$bridge.GetProperty('Factor',$flags).GetValue($null,$null)
if($factor -ge 1 -or $factor -le 0){throw 'Annual scaling test requires a genuine fractional factor'}
foreach($id in @('cow','sheep','hog','wool','horse','war_horse','pack_animal','hardwood','iron','clay','flax','cotton','silver','unknown_mod_input')){
 $category=[TaleWorlds.Core.ItemCategory]::new($id)
 $native=$id -in @('cow','sheep','hog','wool')
 if($classify.Invoke($null,@($category)) -ne $native){throw "Category exemption incorrect: $id"}
 foreach($enabled in @($true,$false)){
  foreach($production in @([single]0,[single]12.5)){
   $value=[TaleWorlds.CampaignSystem.ExplainedNumber]::new($production,$false,$null)
   $argsForPolicy=[object[]]@($category,$enabled,$value)
   $apply.Invoke($null,$argsForPolicy)|Out-Null
   $expected=if($enabled -and -not $native){$production*$factor}else{$production}
   Assert-Near $expected $argsForPolicy[2].ResultNumber 0.00001 "Final native production preserved/scaled for $id annual=$enabled"
  }
 }
}
$category=[TaleWorlds.Core.ItemCategory]::new('custom_food')
$category.GetType().GetProperty('Properties').SetValue($category,[TaleWorlds.Core.ItemCategory+Property]::BonusToFoodStores,$null)
if(-not $classify.Invoke($null,@($category))){throw 'Existing food property exemption lost'}
# Preserve a native explanation including modifiers, not merely a bare base value.
$nativeResult=[TaleWorlds.CampaignSystem.ExplainedNumber]::new([single]10,$true,$null)
$nativeResult.Add([single]2.5,[TaleWorlds.Localization.TextObject]::new('native bonus'),$null)
$arguments=[object[]]@([TaleWorlds.Core.ItemCategory]::new('wool'),$true,$nativeResult)
$apply.Invoke($null,$arguments)|Out-Null
Assert-Near 12.5 $arguments[2].ResultNumber 0.00001 'Native bonuses retained for restored inputs'
if(@($arguments[2].GetLines()).Count -ne @($nativeResult.GetLines()).Count){throw 'Native explanation altered for exempt supply'}
'PASS: four exact input categories, food properties, non-exempt horses/materials, annual-off, zero output, and native production bonuses.'
