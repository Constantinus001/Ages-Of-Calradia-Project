param(
 [string]$PythonPath=(Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'),
 [string]$BaselineRoot=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'Modules\AgesOfCalradiaWorkshopProcurement\Tests\Verify-Candidate.ps1') -BaselineRoot $BaselineRoot
if($LASTEXITCODE -ne 0){throw 'Procurement integration failed'}
& dotnet build (Join-Path $root 'Modules\AgesOfCalradiaCampaignSystems\AgesOfCalradiaCampaignSystems.csproj') -c Release -v minimal /p:TreatWarningsAsErrors=true ("/p:OutputPath="+(Join-Path $root 'output\campaign-systems-naval-guard\'))
if($LASTEXITCODE -ne 0){throw 'Core campaign-systems candidate build failed'}
& dotnet build (Join-Path $root 'Modules\AgesOfCalradiaLogistics\AgesOfCalradiaLogistics.csproj') -c Release -v minimal /p:TreatWarningsAsErrors=true ("/p:OutputPath="+(Join-Path $root 'output\campaign-systems-logistics\'))
if($LASTEXITCODE -ne 0){throw 'Logistics candidate build failed'}
foreach($script in @('Verify-Framework.ps1','Verify-NavalDuplicateRewardGuard.ps1','Verify-NavalCashout.ps1','Verify-LogisticsBridge.ps1')) {
 $extra=@();if($script -eq 'Verify-LogisticsBridge.ps1'){$extra=@('-BaselineRoot',$BaselineRoot)}
 if($script -in @('Verify-NavalDuplicateRewardGuard.ps1','Verify-NavalCashout.ps1')){$extra=@('-AssemblyPath',(Join-Path $root 'output\campaign-systems-naval-guard\AgesOfCalradia.CampaignSystems.dll'))}
 & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot $script) @extra
 if($LASTEXITCODE -ne 0){throw ("Framework test failed: "+$script)}
}
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'Modules\AgesOfCalradiaLogistics\Tests\Verify-SupplyItem.ps1')
if($LASTEXITCODE -ne 0){throw 'Logistics regression failed'}
if(-not(Test-Path -LiteralPath $PythonPath)){throw 'Python required for diagnostics regression; provide -PythonPath'}
& $PythonPath -m unittest discover -s (Join-Path $root 'Modules\AgesOfCalradiaSoakDiagnostics\Tests') -p 'test_*.py'
if($LASTEXITCODE -ne 0){throw 'Diagnostics analyzer regression failed'}
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'Modules\AgesOfCalradiaSoakDiagnostics\Tests\Verify-EconomyCoverageGate.ps1') -DiagnosticsAssemblyPath (Join-Path $root 'output\procurement-diagnostics\AgesOfCalradia.SoakDiagnostics.dll')
if($LASTEXITCODE -ne 0){throw 'Candidate-bound economy coverage preflight failed'}
foreach($script in @('Verify-SupplyCapture.ps1','Verify-SupplyCash.ps1','Verify-CausalDiagnostics.ps1','Verify-DiagnosticExtensions.ps1','Verify-WorkshopCashBoundaries.ps1','Verify-RollingFrameworkLog.ps1','Verify-RewardObservation.ps1','Verify-BattleAllocation.ps1','Verify-FrameworkRuntimeSummary.ps1','Verify-CaptureReadiness.ps1','Verify-ShipLifecycle.ps1','Verify-NavalPolicyObservation.ps1')) {
 $extra=@();if($script -eq 'Verify-NavalPolicyObservation.ps1'){$extra=@('-FrameworkAssemblyPath',(Join-Path $root 'output\campaign-systems-naval-guard\AgesOfCalradia.CampaignSystems.dll'))}
 & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root ('Modules\AgesOfCalradiaSoakDiagnostics\Tests\'+$script)) -DiagnosticsAssemblyPath (Join-Path $root 'output\procurement-diagnostics\AgesOfCalradia.SoakDiagnostics.dll') @extra
 if($LASTEXITCODE -ne 0){throw ('Candidate-bound diagnostics preflight failed: '+$script)}
}
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'Modules\AgesOfCalradiaSoakDiagnostics\Tests\Verify-NavalPatchCoexistence.ps1') -DiagnosticsAssemblyPath (Join-Path $root 'output\procurement-diagnostics\AgesOfCalradia.SoakDiagnostics.dll') -SystemsAssemblyPath (Join-Path $root 'output\campaign-systems-naval-guard\AgesOfCalradia.CampaignSystems.dll')
if($LASTEXITCODE -ne 0){throw 'Candidate-bound naval patch coexistence failed'}
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'Modules\AgesOfCalradiaSoakDiagnostics\Tests\Verify-ReadinessPolicy.ps1')
if($LASTEXITCODE -ne 0){throw 'Readiness policy regression failed'}
[xml]$manifest=Get-Content -Raw (Join-Path $root 'SubModule.xml')
$entries=@($manifest.Module.SubModules.SubModule | Where-Object {$_.DLLName.value -eq 'AgesOfCalradia.CampaignSystems.dll'})
if($entries.Count -ne 1 -or $entries[0].SubModuleClassType.value -ne 'AgesOfCalradia.CampaignSystems.CoreSystemsSubModule'){throw 'Core companion registration missing or duplicated'}
'PASS: Core companion candidate and optional Logistics bridge. Not deployed; publication/security/live acceptance remain required.'
