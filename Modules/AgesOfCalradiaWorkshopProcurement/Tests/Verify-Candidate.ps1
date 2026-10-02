param([string]$BaselineRoot=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path)
$ErrorActionPreference='Stop'
$module=Split-Path $PSScriptRoot
$root=(Resolve-Path (Join-Path $module '..\..')).Path
& dotnet build (Join-Path $module 'AgesOfCalradiaWorkshopProcurement.csproj') -c Release -v minimal
if($LASTEXITCODE -ne 0){throw 'Procurement Release build failed'}
& dotnet build (Join-Path $root 'Modules\AgesOfCalradiaSoakDiagnostics\AgesOfCalradiaSoakDiagnostics.csproj') -c Release -v minimal /p:TreatWarningsAsErrors=true ("/p:OutputPath="+(Join-Path $root 'output\procurement-diagnostics\'))
if($LASTEXITCODE -ne 0){throw 'Procurement-aware diagnostics build failed'}
foreach($script in @('Verify-Procurement.ps1','Verify-NativeProcurement.ps1')){
 $extra=@();if($script -eq 'Verify-NativeProcurement.ps1'){$extra=@('-BaselineRoot',$BaselineRoot)}
 & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot $script) @extra
 if($LASTEXITCODE -ne 0){throw ('Procurement verification failed: '+$script)}
}
foreach($script in @('Verify-ProtectedPoliticalBaseline.ps1','Verify-CalendarMath.ps1','Verify-StrategicMapCoverage.ps1')){
 & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $BaselineRoot ('Tests\'+$script))
 if($LASTEXITCODE -ne 0){throw ('Baseline regression failed: '+$script)}
}
[xml]$manifest=Get-Content -Raw (Join-Path $module 'SubModule.xml')
if($manifest.Module.DefaultModule.value -ne 'false'){throw 'Candidate must remain opt-in'}
'PASS: undeployed procurement candidate. This is not the publication/security gate or live economic acceptance.'
