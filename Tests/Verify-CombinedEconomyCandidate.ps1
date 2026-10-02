param(
 [string]$Root=(Split-Path -Parent $PSScriptRoot),
 [string]$PythonPath=(Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe')
)
$ErrorActionPreference='Stop'
if(!(Test-Path -LiteralPath $PythonPath)){throw 'Python runtime required for analyzer regression checks'}
$runRoot=Join-Path $Root ('output\combined-economy-check-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $runRoot|Out-Null
$results=New-Object 'System.Collections.Generic.List[object]'
function Check-Step([string]$Name,[string]$Executable,[string[]]$Arguments){
 $timer=[Diagnostics.Stopwatch]::StartNew()
 Get-Command $Executable -ErrorAction Stop|Out-Null
 # Windows PowerShell treats unittest's normal stderr progress as ErrorRecords.
 # Native exit status remains authoritative; log IO failures still terminate.
 $previousPreference=$ErrorActionPreference
 try {
  $ErrorActionPreference='Continue'
  & $Executable @Arguments 2>&1 | ForEach-Object {$_.ToString()} | Tee-Object -FilePath (Join-Path $runRoot ($Name+'.log')) -ErrorAction Stop | Out-Host
  $code=$LASTEXITCODE
 } finally {$ErrorActionPreference=$previousPreference}
 $results.Add([pscustomobject]@{name=$Name;exitCode=$code;passed=($code -eq 0);seconds=$timer.Elapsed.TotalSeconds})
}
Check-Step 'procurement-and-baseline' 'powershell.exe' @('-NoProfile','-File',(Join-Path $Root 'Modules\AgesOfCalradiaWorkshopProcurement\Tests\Verify-Candidate.ps1'))
Check-Step 'framework-settings' 'powershell.exe' @('-NoProfile','-File',(Join-Path $Root 'Modules\AgesOfCalradiaCampaignSystems\Tests\Verify-Framework.ps1'))
Check-Step 'naval-reward-guard' 'powershell.exe' @('-NoProfile','-File',(Join-Path $Root 'Modules\AgesOfCalradiaCampaignSystems\Tests\Verify-NavalDuplicateRewardGuard.ps1'))
Check-Step 'logistics-bridge' 'powershell.exe' @('-NoProfile','-File',(Join-Path $Root 'Modules\AgesOfCalradiaCampaignSystems\Tests\Verify-LogisticsBridge.ps1'))
$diagnostics=Join-Path $Root 'output\procurement-diagnostics\AgesOfCalradia.SoakDiagnostics.dll'
Check-Step 'capture-readiness' 'powershell.exe' @('-NoProfile','-File',(Join-Path $Root 'Modules\AgesOfCalradiaSoakDiagnostics\Tests\Verify-CaptureReadiness.ps1'),'-DiagnosticsAssemblyPath',$diagnostics)
Check-Step 'causal-diagnostics' 'powershell.exe' @('-NoProfile','-File',(Join-Path $Root 'Modules\AgesOfCalradiaSoakDiagnostics\Tests\Verify-CausalDiagnostics.ps1'),'-DiagnosticsAssemblyPath',$diagnostics)
Check-Step 'diagnostic-extensions' 'powershell.exe' @('-NoProfile','-File',(Join-Path $Root 'Modules\AgesOfCalradiaSoakDiagnostics\Tests\Verify-DiagnosticExtensions.ps1'),'-DiagnosticsAssemblyPath',$diagnostics)
Check-Step 'workshop-cash-purposes' 'powershell.exe' @('-NoProfile','-File',(Join-Path $Root 'Modules\AgesOfCalradiaSoakDiagnostics\Tests\Verify-WorkshopCashBoundaries.ps1'),'-DiagnosticsAssemblyPath',$diagnostics)
Check-Step 'diagnostic-hardening' 'powershell.exe' @('-NoProfile','-File',(Join-Path $Root 'Modules\AgesOfCalradiaSoakDiagnostics\Tests\Verify-DiagnosticHardening.ps1'),'-DiagnosticsAssemblyPath',$diagnostics)
Check-Step 'wallet-registration-identity' 'powershell.exe' @('-NoProfile','-File',(Join-Path $Root 'Modules\AgesOfCalradiaSoakDiagnostics\Tests\Verify-SupplyCash.ps1'),'-DiagnosticsAssemblyPath',$diagnostics)
Check-Step 'rolling-incident-preservation' 'powershell.exe' @('-NoProfile','-File',(Join-Path $Root 'Modules\AgesOfCalradiaSoakDiagnostics\Tests\Verify-RollingFrameworkLog.ps1'),'-DiagnosticsAssemblyPath',$diagnostics)
Check-Step 'readiness-policy' 'powershell.exe' @('-NoProfile','-File',(Join-Path $Root 'Modules\AgesOfCalradiaSoakDiagnostics\Tests\Verify-ReadinessPolicy.ps1'))
Check-Step 'analyzer-regressions' $PythonPath @('-m','unittest','discover','-s',(Join-Path $Root 'Modules\AgesOfCalradiaSoakDiagnostics\Tests'),'-p','test_*.py')
# Only companions enter this isolated fixture layout. Never copy protected Core.
$fixture=Join-Path $runRoot 'fixture-modules'
$framework=Join-Path $Root 'Modules\AgesOfCalradiaCampaignSystems\bin\Win64_Shipping_Client\AgesOfCalradia.CampaignSystems.dll'
$procurement=Join-Path $Root 'Modules\AgesOfCalradiaWorkshopProcurement\bin\Win64_Shipping_Client\AgesOfCalradia.WorkshopProcurement.dll'
foreach($entry in @(@($framework,'AOC CORE'),@($diagnostics,'AgesOfCalradiaSoakDiagnostics'))){
 $bin=Join-Path $fixture ($entry[1]+'\bin\Win64_Shipping_Client')
 New-Item -ItemType Directory -Path $bin -Force|Out-Null
 Copy-Item -LiteralPath $entry[0] -Destination $bin
}
Check-Step 'native-quest-observation' 'powershell.exe' @('-NoProfile','-File',(Join-Path $Root 'Modules\AgesOfCalradiaCampaignSystems\Tests\Verify-NativeQuestObservation.ps1'),'-PackageRoot',$fixture)
$hashes=@($framework,$procurement,$diagnostics,(Join-Path $Root 'Modules\AgesOfCalradiaCampaignSystems\CampaignSystems.combined-economy-candidate.xml'))|ForEach-Object{
 [pscustomobject]@{path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash}
}
$failed=@($results|Where-Object{!$_.passed})
$receipt=[ordered]@{schema=1;utc=[DateTime]::UtcNow.ToString('O');status=$(if($failed.Count){'OFFLINE_CHECKS_FAILED'}else{'OFFLINE_CHECKS_PASSED_NOT_LIVE_ACCEPTANCE'});checks=@($results.ToArray());artifacts=$hashes;deployed=$false;publicRelease=$false;limit='No game launched, capture armed, settings activated or save changed. Native fixtures are not campaign balance certification.'}
$receipt|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $runRoot 'receipt.json') -Encoding UTF8
Write-Output ('Receipt: '+(Join-Path $runRoot 'receipt.json'))
if($failed.Count){throw ('Combined candidate failed: '+(($failed|ForEach-Object{$_.name}) -join ', '))}
'PASS: combined offline checks only; no per-fix game test requested.'
