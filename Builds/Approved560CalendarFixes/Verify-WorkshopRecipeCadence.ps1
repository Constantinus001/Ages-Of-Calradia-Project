param(
 [string]$BannerlordDir='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord',
 [string]$ModuleRoot=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
 [string]$HarmonyPath=(Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.2\lib\net472\0Harmony.dll')
)
$ErrorActionPreference='Stop'
# Also installs all three patches against the real native methods in this isolated process.
. (Join-Path $PSScriptRoot 'Verify-Approved560CalendarFixes.ps1') -BannerlordDir $BannerlordDir -ModuleRoot $ModuleRoot -HarmonyPath $HarmonyPath
$recipeType=$sidecar.GetType('AgesOfCalradia.Approved560CalendarFixes.WorkshopRecipeCadenceFix',$true)
$scale=$recipeType.GetMethod('ScaleRecipe',[Reflection.BindingFlags]'NonPublic,Static')
$productionType=[TaleWorlds.CampaignSystem.Settlements.Workshops.WorkshopType+Production]
$food=New-Object TaleWorlds.Core.ItemCategory
$food.GetType().GetProperty('Properties').SetValue($food,[TaleWorlds.Core.ItemCategory+Property]::BonusToFoodStores,$null)
$industrial=New-Object TaleWorlds.Core.ItemCategory
function Make-Recipe([object[]]$Outputs){
 $p=[Activator]::CreateInstance($productionType,@([single]12))
 $list=$productionType.GetField('_outputs',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($p)
 foreach($category in $Outputs){$list.Add([ValueTuple[TaleWorlds.Core.ItemCategory,int]]::new($category,2))}
 return $p
}
$industrialRecipe=Make-Recipe @($industrial)
$foodRecipe=Make-Recipe @($food)
$mixedRecipe=Make-Recipe @($industrial,$food)
foreach($factor in @([single](84/365.2425),[single]1)){
 foreach($recipe in @($industrialRecipe,$foodRecipe,$mixedRecipe)){
  $isFood=$recipe.Outputs | Where-Object {$_.Item1.Properties -eq [TaleWorlds.Core.ItemCategory+Property]::BonusToFoodStores}
  $expected=if($isFood){12}else{12*$factor}
  Assert-Near $expected ($scale.Invoke($null,@($recipe,$true,$factor))) 0.00001 'Recipe classification, including mixed outputs'
  Assert-Near 12 ($scale.Invoke($null,@($recipe,$false,$factor))) 0.00001 'Annual balancing disabled'
  Assert-Near 12 $recipe.ConversionSpeed 0.00001 'Shared recipe definition untouched'
 }
}
# Interleaving food and industrial recipes must never share contextual state.
foreach($recipe in @($industrialRecipe,$foodRecipe,$industrialRecipe)){
 $expected=if($recipe.Outputs[0].Item1 -eq $food){12}else{12*$dailyFactor}
 Assert-Near $expected ($scale.Invoke($null,@($recipe,$true,$dailyFactor))) 0.00001 'Interleaved mixed-workshop recipes'
}
$transpiler=$recipeType.GetMethod('Transpiler',[Reflection.BindingFlags]'NonPublic,Static')
$targets=@($recipeType.GetMethod('TargetMethods',[Reflection.BindingFlags]'NonPublic,Static').Invoke($null,@()))
if($targets.Count -ne 3){throw 'Production and both estimates must be patched'}
foreach($target in $targets){
 $original=[Collections.Generic.List[HarmonyLib.CodeInstruction]]::new()
 foreach($instruction in [HarmonyLib.PatchProcessor]::GetOriginalInstructions($target)){$original.Add($instruction)}
 $changed=@($transpiler.Invoke($null,(,$original)))
 if($changed.Count -ne $original.Count){throw 'Native control flow was resized'}
 $differences=0
 for($i=0;$i -lt $original.Count;$i++){
  if($changed[$i].opcode -ne $original[$i].opcode -or $changed[$i].operand -ne $original[$i].operand){$differences++}
  if(($changed[$i].labels -join ',') -ne ($original[$i].labels -join ',') -or ($changed[$i].blocks -join ',') -ne ($original[$i].blocks -join ',')){throw 'Branch labels or exception blocks changed'}
 }
 if($differences -ne 1){throw 'Expected only the recipe getter replacement; progress/failure loops must stay native'}
 $rejected=$false
 try{$transpiler.Invoke($null,(,[Collections.Generic.List[HarmonyLib.CodeInstruction]]::new()))|Out-Null}catch{$rejected=$true}
 if(-not $rejected){throw 'Changed native pattern accepted'}
 $rejected=$false
 try{$transpiler.Invoke($null,(,[Collections.Generic.List[HarmonyLib.CodeInstruction]]::new([HarmonyLib.CodeInstruction[]]$changed)))|Out-Null}catch{$rejected=$true}
 if(-not $rejected){throw 'Already-adjusted pattern accepted a second time'}
}
$speedTarget=[HarmonyLib.AccessTools]::Method([TaleWorlds.CampaignSystem.GameComponents.DefaultWorkshopModel],'GetEffectiveConversionSpeedOfProduction')
$speedInfo=[HarmonyLib.Harmony]::GetPatchInfo($speedTarget)
if($speedInfo -and @($speedInfo.Prefixes | Where-Object {$_.owner -eq $owner}).Count){throw 'Old whole-workshop prefix still active'}
'PASS: recipe classification, mixed outputs, annual-off, interleaving, unchanged native control flow, all warehouse/production targets, changed-pattern rejection and no double adjustment.'
