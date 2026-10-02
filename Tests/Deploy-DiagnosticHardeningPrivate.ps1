param([Parameter(Mandatory=$true)][string]$VerificationReceipt, [switch]$Apply)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'CampaignDeploymentProcessGuard.ps1')
$modules='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules'
$verification=Get-Content -LiteralPath $VerificationReceipt -Raw|ConvertFrom-Json
if($verification.status -ne 'OFFLINE_CHECKS_PASSED_NOT_LIVE_ACCEPTANCE' -or @($verification.checks|Where-Object {-not $_.passed}).Count){throw 'Combined offline checks have not passed'}
foreach($required in @('diagnostic-hardening','rolling-incident-preservation','analyzer-regressions','procurement-and-baseline','capture-readiness')){
 if(@($verification.checks|Where-Object {$_.name -eq $required -and $_.passed}).Count -ne 1){throw "Missing verification: $required"}
}
Assert-CampaignDeploymentIdle
function ProtectedCheck {
 & powershell.exe -NoProfile -File (Join-Path $PSScriptRoot 'Verify-ProtectedPoliticalBaseline.ps1')
 if($LASTEXITCODE -ne 0){throw 'Protected baseline verification failed'}
}
ProtectedCheck
# Only the diagnostic DLL and the already approved optional diagnostic ABI in
# procurement. No manifest, policy, calendar, UI, Core, marker or save writes.
$plan=@()
foreach($pair in @(
 @('output\procurement-diagnostics\AgesOfCalradia.SoakDiagnostics.dll','AgesOfCalradiaSoakDiagnostics\bin\Win64_Shipping_Client\AgesOfCalradia.SoakDiagnostics.dll'),
 @('Modules\AgesOfCalradiaWorkshopProcurement\bin\Win64_Shipping_Client\AgesOfCalradia.WorkshopProcurement.dll','AOC CORE\bin\Win64_Shipping_Client\AgesOfCalradia.WorkshopProcurement.dll')
)){
 $source=(Resolve-Path -LiteralPath (Join-Path $root $pair[0])).Path
 $target=(Resolve-Path -LiteralPath (Join-Path $modules $pair[1])).Path
 $proof=@($verification.artifacts|Where-Object {$_.path -eq $source})
 if($proof.Count -ne 1 -or (Get-FileHash -LiteralPath $source).Hash -ne $proof[0].sha256){throw "Unverified candidate: $source"}
 $plan += [pscustomobject]@{source=$source;target=$target;before=(Get-FileHash -LiteralPath $target).Hash;after=$proof[0].sha256;name=[IO.Path]::GetFileName($source)}
}
$framework=Join-Path $modules 'AOC CORE\bin\Win64_Shipping_Client\AgesOfCalradia.CampaignSystems.dll'
$frameworkProof=@($verification.artifacts|Where-Object {$_.path -like '*\AgesOfCalradia.CampaignSystems.dll'})
if($frameworkProof.Count -ne 1 -or (Get-FileHash -LiteralPath $framework).Hash -ne $frameworkProof[0].sha256){throw 'Installed framework differs from verified combination'}
if(-not $Apply){$plan|Format-List; 'PASS: exact two-file private diagnostic deployment preflight; no writes.'; return}
$backup=Join-Path $root ('output\diagnostic-hardening-deployment-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $backup|Out-Null
$record=[ordered]@{scope='private_diagnostics_not_public_release';security='not_run_private_test_only';verification=(Resolve-Path -LiteralPath $VerificationReceipt).Path;verificationSha256=(Get-FileHash -LiteralPath $VerificationReceipt).Hash;state='backing_up';files=$plan;backup=$backup;capture='not_armed';settings='unchanged';game='not_started_or_stopped'}
function SaveReceipt {$record|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $backup 'deployment.json') -Encoding UTF8}
SaveReceipt
foreach($item in $plan){
 Copy-Item -LiteralPath $item.target -Destination (Join-Path $backup $item.name)
 if((Get-FileHash -LiteralPath (Join-Path $backup $item.name)).Hash -ne $item.before){throw 'Backup hash mismatch; no deployment performed'}
}
$touched=@()
try{
 Assert-CampaignDeploymentIdle
 foreach($item in $plan){
  if((Get-FileHash -LiteralPath $item.source).Hash -ne $item.after -or (Get-FileHash -LiteralPath $item.target).Hash -ne $item.before){throw 'Candidate or target changed after preflight'}
  if($item.before -eq $item.after){continue}
  $touched += $item
  Copy-Item -LiteralPath $item.source -Destination $item.target -Force
  if((Get-FileHash -LiteralPath $item.target).Hash -ne $item.after){throw 'Installed hash mismatch'}
 }
 ProtectedCheck
 $record.state='private_diagnostics_deployed_hash_verified';SaveReceipt
 Write-Output ('PASS: two verified companion DLLs deployed. Backup and receipt: '+$backup)
}catch{
 $original=$_.ToString();$rollbackFailures=@()
 foreach($item in $touched){
  try{
   Copy-Item -LiteralPath (Join-Path $backup $item.name) -Destination $item.target -Force
   if((Get-FileHash -LiteralPath $item.target).Hash -ne $item.before){throw 'Rollback hash mismatch'}
  }catch{$rollbackFailures += $_.ToString()}
 }
 $record.state='deployment_failed';$record.error=$original;$record.rollbackFailures=$rollbackFailures;SaveReceipt
 throw "Deployment failed: $original; rollback failures: $($rollbackFailures -join '; ')"
}
