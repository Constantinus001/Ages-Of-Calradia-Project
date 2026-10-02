param(
 [Parameter(Mandatory=$true)][string]$CandidateRun,
 [string]$ModuleRoot,
 [switch]$Apply
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'CampaignDeploymentProcessGuard.ps1')
if(-not $ModuleRoot){$ModuleRoot=Split-Path -Parent $PSScriptRoot}
$modules='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules'
$run=(Resolve-Path -LiteralPath $CandidateRun).Path
$receipt=Get-Content -Raw (Join-Path $run 'verification.json')|ConvertFrom-Json
if($receipt.Stage -ne 'offline_candidate_pass' -or $receipt.Security -ne 'no_detection_during_scan_and_hold'){throw 'Candidate has not passed its offline/security gate.'}
Assert-CampaignDeploymentIdle
function ProtectedCheck {
 & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $ModuleRoot 'Tests\Verify-ProtectedPoliticalBaseline.ps1')
 if($LASTEXITCODE -ne 0){throw 'Protected baseline verification failed.'}
}
ProtectedCheck
if((Get-FileHash -LiteralPath $receipt.Archive).Hash -ne $receipt.ArchiveHash){throw 'Candidate archive hash changed.'}
foreach($download in $receipt.Downloads.PSObject.Properties){
 if((Get-FileHash -LiteralPath $download.Value.Path).Hash -ne $download.Value.Hash){throw 'Split archive hash changed.'}
}
# Bind the exact installed Core used when the private additive manifest was built.
$corePrefix=(Join-Path $modules 'AOC CORE')+'\'
foreach($baselineInput in $receipt.InputHashes.PSObject.Properties){
 if($baselineInput.Name.StartsWith($corePrefix,[StringComparison]::OrdinalIgnoreCase)){
  $current=(Get-FileHash -LiteralPath $baselineInput.Name).Hash
  $relative=$baselineInput.Name.Substring($modules.Length+1)
  $candidate=$receipt.PackageHashes.PSObject.Properties[$relative].Value
  if($current -ne $baselineInput.Value -and $current -ne $candidate){throw "Installed Core changed since packaging: $($baselineInput.Name)"}
 }
}
$files=@(
 'AOC CORE\SubModule.xml','AOC CORE\CampaignSystems.example.xml',
 'AOC CORE\bin\Win64_Shipping_Client\AgesOfCalradia.CampaignSystems.dll',
 'AOC CORE\bin\Win64_Shipping_Client\AgesOfCalradia.Approved560CalendarFixes.dll',
 'AgesOfCalradiaWorkshopProcurement\SubModule.xml',
 'AgesOfCalradiaWorkshopProcurement\bin\Win64_Shipping_Client\AgesOfCalradia.WorkshopProcurement.dll',
 'AgesOfCalradiaSoakDiagnostics\SubModule.xml',
 'AgesOfCalradiaSoakDiagnostics\bin\Win64_Shipping_Client\AgesOfCalradia.SoakDiagnostics.dll'
)
$plan=@()
if($receipt.PackageHashes.PSObject.Properties['AOC CORE\bin\Win64_Shipping_Client\AgesOfCalradia.WorkshopProcurement.dll']){
 $files=@($files|Where-Object {$_ -notlike 'AgesOfCalradiaWorkshopProcurement\*'})+'AOC CORE\bin\Win64_Shipping_Client\AgesOfCalradia.WorkshopProcurement.dll'
 $launcher=Join-Path $env:USERPROFILE 'Documents\Mount and Blade II Bannerlord\Configs\LauncherData.xml'
 if(Test-Path -LiteralPath $launcher){
  [xml]$launcherData=Get-Content -Raw -LiteralPath $launcher
  if(@($launcherData.SelectNodes('//UserModData')|Where-Object {$_.Id -eq 'AgesOfCalradiaWorkshopProcurement' -and $_.IsSelected -eq 'true'}).Count){
   throw 'Disable the old Workshop Procurement candidate before deploying its integrated Core replacement.'
  }
 }
}
foreach($relative in $files){
 $source=Join-Path (Join-Path $run 'package') $relative
 $target=[IO.Path]::GetFullPath((Join-Path $modules $relative))
 if(-not $target.StartsWith($modules+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Target escaped Modules.'}
 $expected=$receipt.PackageHashes.PSObject.Properties[$relative].Value
 if(-not $expected -or (Get-FileHash -LiteralPath $source).Hash -ne $expected){throw "Unverified payload: $relative"}
 $old=if(Test-Path -LiteralPath $target){(Get-FileHash -LiteralPath $target).Hash}else{$null}
 $plan += [pscustomobject]@{Relative=$relative;Source=$source;Target=$target;Before=$old;After=$expected}
}
if(-not $Apply){$plan|Select-Object Relative,Before,After; 'PASS: read-only deployment preflight; no files changed.';return}
$backup=Join-Path $run ('deployment-'+[guid]::NewGuid().ToString('N').Substring(0,12))
New-Item -ItemType Directory -Path $backup|Out-Null
$record=[ordered]@{Candidate=$run;Applied=$false;State='backing_up';Files=$plan;Backup=$backup;Logistics='existing provider untouched';Launcher='unchanged';Capture='not armed'}
function SaveReceipt {$record|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $backup 'deployment.json')}
SaveReceipt
foreach($item in $plan){
 if($item.Before){
  $saved=Join-Path $backup $item.Relative
  New-Item -ItemType Directory -Path (Split-Path $saved) -Force|Out-Null
  Copy-Item -LiteralPath $item.Target -Destination $saved
  if((Get-FileHash -LiteralPath $saved).Hash -ne $item.Before){throw 'Backup verification failed; no deployment performed.'}
 }
}
$touched=@()
try{
 Assert-CampaignDeploymentIdle
 foreach($item in $plan){
  $current=if(Test-Path -LiteralPath $item.Target){(Get-FileHash -LiteralPath $item.Target).Hash}else{$null}
  if($current -ne $item.Before){throw 'Destination changed after backup.'}
  if((Get-FileHash -LiteralPath $item.Source).Hash -ne $item.After){throw 'Source changed after preflight.'}
  if($current -eq $item.After){continue}
  New-Item -ItemType Directory -Path (Split-Path $item.Target) -Force|Out-Null
  $touched+=$item
  Copy-Item -LiteralPath $item.Source -Destination $item.Target -Force
  if((Get-FileHash -LiteralPath $item.Target).Hash -ne $item.After){throw 'Installed hash mismatch.'}
 }
 ProtectedCheck
 foreach($item in $plan){if((Get-FileHash -LiteralPath $item.Target).Hash -ne $item.After){throw 'Final installed hash mismatch.'}}
 $record.Applied=$true;$record.State='deployed_hash_verified';SaveReceipt
 "PASS: $($files.Count) approved files verified; protected artifacts unchanged. Backup/receipt: $backup"
}catch{
 $record.State='failed';$record.Error=$_.ToString();SaveReceipt
 # Only exact installer-written outputs may be rolled back automatically.
 foreach($item in $touched){
  if((Test-Path -LiteralPath $item.Target) -and (Get-FileHash -LiteralPath $item.Target).Hash -eq $item.After){
   if($item.Before){Copy-Item -LiteralPath (Join-Path $backup $item.Relative) -Destination $item.Target -Force}
   else{Remove-Item -LiteralPath $item.Target}
  }
 }
 throw
}
