param([Parameter(Mandatory=$true)][string]$PackageRoot,[Parameter(Mandatory=$true)][string]$BaselineRoot)
$ErrorActionPreference='Stop'
$gameBin='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client'
foreach($name in @('TaleWorlds.Library','TaleWorlds.DotNet','TaleWorlds.ScreenSystem','TaleWorlds.Localization','TaleWorlds.ObjectSystem','TaleWorlds.SaveSystem','TaleWorlds.Core','TaleWorlds.Engine','TaleWorlds.CampaignSystem','TaleWorlds.MountAndBlade')){
 [Reflection.Assembly]::LoadFrom((Join-Path $gameBin ($name+'.dll')))|Out-Null
}
$coreName='AgesOfCalradia.CampaignSystems.dll'
if(@(Get-ChildItem -LiteralPath $PackageRoot -Recurse -Filter $coreName).Count -ne 1){throw 'Framework assembly must ship exactly once in Core'}
if(@(Get-ChildItem -LiteralPath $PackageRoot -Recurse -Filter 'AgesOfCalradia.dll').Count){throw 'Protected Core must not enter this delta candidate'}
$corePath=Join-Path $PackageRoot ('AOC CORE\bin\Win64_Shipping_Client\'+$coreName)
$core=[Reflection.Assembly]::LoadFrom($corePath)
$entry=$core.GetType('AgesOfCalradia.CampaignSystems.CoreSystemsSubModule',$true)
if(-not [TaleWorlds.MountAndBlade.MBSubModuleBase].IsAssignableFrom($entry)){throw 'Core entry point invalid'}
$commands=@($entry.GetMethods()|ForEach-Object {$_.GetCustomAttributes($false)}|Where-Object {$_.GetType().FullName -eq 'TaleWorlds.Library.CommandLineFunctionality+CommandLineArgumentFunction'})
if($commands.Count -ne 3){throw 'Core commands not discoverable in package'}
[xml]$manifest=Get-Content -Raw (Join-Path $PackageRoot 'AOC CORE\SubModule.xml')
$entries=@($manifest.Module.SubModules.SubModule|Where-Object {$_.DLLName.value -eq $coreName})
if($entries.Count -ne 1 -or $entries[0].SubModuleClassType.value -ne $entry.FullName){throw 'Core registration mismatch'}
$installedRoot='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\AOC CORE'
[xml]$installed=Get-Content -Raw (Join-Path $installedRoot 'SubModule.xml')
# Remove just the new entry from XML clones and compare normalized documents.
# This also protects dependencies, XML registrations and order, not only DLL names.
$candidateClone=$manifest.CloneNode($true)
$installedClone=$installed.CloneNode($true)
 $procName='AgesOfCalradia.WorkshopProcurement.dll'
 if(@(Get-ChildItem -LiteralPath $PackageRoot -Recurse -Filter $procName).Count -ne 1){throw 'Procurement must ship exactly once inside Core'}
 $proc=[Reflection.Assembly]::LoadFrom((Join-Path $PackageRoot ('AOC CORE\bin\Win64_Shipping_Client\'+$procName)))
 $procEntry=$proc.GetType('AgesOfCalradia.WorkshopProcurement.ProcurementSubModule',$true)
 if(-not [TaleWorlds.MountAndBlade.MBSubModuleBase].IsAssignableFrom($procEntry)){throw 'Invalid Core procurement entry point'}
 $procEntries=@($manifest.Module.SubModules.SubModule|Where-Object {$_.DLLName.value -eq $procName})
 if($procEntries.Count -ne 1 -or $procEntries[0].SubModuleClassType.value -ne $procEntry.FullName){throw 'Core procurement registration mismatch'}
 $ordered=@($manifest.Module.SubModules.SubModule.DLLName.value)
 if([array]::IndexOf($ordered,$coreName) -gt [array]::IndexOf($ordered,$procName)){throw 'Framework must load before procurement'}
foreach($document in @($candidateClone,$installedClone)){
 foreach($node in @($document.Module.SubModules.SubModule|Where-Object {$_.DLLName.value -in @($coreName,$procName)})){$node.ParentNode.RemoveChild($node)|Out-Null}
}
if($candidateClone.OuterXml -ne $installedClone.OuterXml){throw 'Candidate would alter existing Core registrations/settings'}
foreach($node in $manifest.Module.SubModules.SubModule){
 $dll=$node.DLLName.value
 if($dll -in @($coreName,$procName,'AgesOfCalradia.Approved560CalendarFixes.dll')){continue}
 if(-not(Test-Path -LiteralPath (Join-Path $installedRoot ('bin\Win64_Shipping_Client\'+$dll)))){throw "Required preserved Core assembly missing: $dll"}
}
foreach($module in @('AgesOfCalradiaLogistics','AgesOfCalradiaSoakDiagnostics')){
 [xml]$manifest=Get-Content -Raw (Join-Path $PackageRoot ($module+'\SubModule.xml'))
 if(-not @($manifest.Module.DependedModules.DependedModule|Where-Object {$_.Id -eq 'AgesOfCalradia'}).Count){throw "Missing Core ordering dependency: $module"}
 foreach($submodule in $manifest.Module.SubModules.SubModule){
  $path=Join-Path $PackageRoot ($module+'\bin\Win64_Shipping_Client\'+$submodule.DLLName.value)
  $assembly=[Reflection.Assembly]::LoadFrom($path)
  $type=$assembly.GetType($submodule.SubModuleClassType.value,$true)
  if(-not [TaleWorlds.MountAndBlade.MBSubModuleBase].IsAssignableFrom($type)){throw "Invalid module entry point: $module"}
  foreach($reference in $assembly.GetReferencedAssemblies()|Where-Object {$_.Name -eq 'AgesOfCalradia.CampaignSystems'}){
   if($reference.FullName -ne $core.GetName().FullName){throw 'Framework reference identity mismatch'}
  }
 }
}
'PASS: single Core assembly, declared dependency order, loadable entry points and three discoverable commands. Not an in-game launcher/startup test.'
