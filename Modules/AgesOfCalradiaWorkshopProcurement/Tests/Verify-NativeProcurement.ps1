param([string]$BaselineRoot=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path)
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$diagnosticsPath=Join-Path $root 'output\procurement-diagnostics\AgesOfCalradia.SoakDiagnostics.dll'
. (Join-Path $root 'Builds\Approved560CalendarFixes\Verify-NativeWorkshopBatch.ps1') -ModuleRoot $BaselineRoot -WithDiagnostics -DiagnosticsPath $diagnosticsPath
$candidate=Join-Path $PSScriptRoot '..\bin\Win64_Shipping_Client\AgesOfCalradia.WorkshopProcurement.dll'
$framework=Join-Path $PSScriptRoot '..\bin\Win64_Shipping_Client\AgesOfCalradia.CampaignSystems.dll'
[Reflection.Assembly]::LoadFrom((Resolve-Path $framework).Path)|Out-Null
$assembly=[Reflection.Assembly]::LoadFrom((Resolve-Path $candidate).Path)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$assembly.GetType('AgesOfCalradia.WorkshopProcurement.ProcurementPatches',$true).GetMethod('Validate',$flags).Invoke($null,@()) | Out-Null
$wine=$assembly.GetType('AgesOfCalradia.WorkshopProcurement.WineOperatingMargin',$true)
$wineTarget=$wine.GetMethod('TargetMethod',$flags).Invoke($null,@())
$wineTranspiler=$wine.GetMethod('Transpiler',$flags)
$altered=[System.Collections.Generic.List[HarmonyLib.CodeInstruction]]::new()
foreach($instruction in [HarmonyLib.PatchProcessor]::GetOriginalInstructions($wineTarget)){
 $copy=[HarmonyLib.CodeInstruction]::new($instruction)
 if($copy.opcode -eq [Reflection.Emit.OpCodes]::Ldc_R4 -and $copy.operand -eq 200){$copy.operand=[single]201}
 $altered.Add($copy)
}
$rejected=$false
try{$wineTranspiler.Invoke($null,[object[]]@($altered))|Out-Null}catch{$rejected=$true}
if(!$rejected){throw 'Changed native wine margin expression was not rejected'}
'PASS: wine margin native-pattern mismatch fails preflight.'
$h=[HarmonyLib.Harmony]::new('AgesOfCalradia.WorkshopProcurement.v1')
$h.PatchAll($assembly)
$references+=$candidate
$references+=$framework
$references+='System.Runtime.Serialization'
Add-Type -Path (Join-Path $PSScriptRoot 'NativeProcurementFixture.cs') -ReferencedAssemblies $references
try {
 $diagnosticObserver.GetMethod('Install',[Reflection.BindingFlags]'Static,NonPublic').Invoke($null,@())|Out-Null
 [NativeProcurementFixture]::Run($assembly,[Func[string]]{[NativeWorkshopBatchFixture]::Run($null)})
 $moduleType=$assembly.GetType('AgesOfCalradia.WorkshopProcurement.ProcurementSubModule',$true)
 $ownerField=$moduleType.GetField('_owner',[Reflection.BindingFlags]'Static,NonPublic')
 $owner=[Activator]::CreateInstance($moduleType)
 $duplicate=[Activator]::CreateInstance($moduleType)
 $ownerField.SetValue($null,$owner)
 try {
  $before=@([HarmonyLib.Harmony]::GetAllPatchedMethods()|Where-Object {[HarmonyLib.Harmony]::GetPatchInfo($_).Owners -contains 'AgesOfCalradia.WorkshopProcurement.v1'}).Count
  $moduleType.GetMethod('OnSubModuleLoad',[Reflection.BindingFlags]'Instance,NonPublic').Invoke($duplicate,@())|Out-Null
  $moduleType.GetMethod('OnSubModuleUnloaded',[Reflection.BindingFlags]'Instance,NonPublic').Invoke($duplicate,@())|Out-Null
  $after=@([HarmonyLib.Harmony]::GetAllPatchedMethods()|Where-Object {[HarmonyLib.Harmony]::GetPatchInfo($_).Owners -contains 'AgesOfCalradia.WorkshopProcurement.v1'}).Count
  if(-not [object]::ReferenceEquals($ownerField.GetValue($null),$owner) -or $before -eq 0 -or $after -ne $before){throw 'Duplicate submodule changed active ownership or patches'}
  'PASS: duplicate legacy submodule load/unload cannot acquire ownership or remove active Core procurement patches.'
 } finally {$ownerField.SetValue($null,$null)}
}
catch { Write-Output $_.Exception.ToString(); throw }
finally {
 $diagnosticObserver.GetMethod('Uninstall',[Reflection.BindingFlags]'Static,NonPublic').Invoke($null,@())|Out-Null
 $h.UnpatchAll('AgesOfCalradia.WorkshopProcurement.v1')
}
