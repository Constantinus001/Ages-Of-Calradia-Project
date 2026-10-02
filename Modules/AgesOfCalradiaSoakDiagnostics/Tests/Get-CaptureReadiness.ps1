param(
 [string]$BannerlordDir='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord',
 [string]$LogDirectory=(Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Mount and Blade II Bannerlord\AgesOfCalradiaSoakDiagnostics'),
 [switch]$ProbeWrite,
 [string]$CandidateRun,
 [string]$VerificationReceipt
)
# No capture controls, saves, deployments or game actions. Optional probe owns
# exactly one unique temporary file and never opens the capture for writing.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'CaptureReadinessPolicy.ps1')
$repo=Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$blockers=[Collections.Generic.List[string]]::new()
$builds=@()
$candidateReceipt=$null
$verifiedBuilds=@{}
if($CandidateRun -and $VerificationReceipt){throw 'Choose a packaged candidate or a combined verification receipt, not both'}
if($VerificationReceipt){
 $verified=Get-Content -LiteralPath $VerificationReceipt -Raw|ConvertFrom-Json
 if($verified.status -ne 'OFFLINE_CHECKS_PASSED_NOT_LIVE_ACCEPTANCE' -or @($verified.checks|Where-Object {-not $_.passed}).Count){throw 'Combined verification has not passed'}
 foreach($required in @('diagnostic-hardening','rolling-incident-preservation','analyzer-regressions','procurement-and-baseline','capture-readiness')){
  if(@($verified.checks|Where-Object {$_.name -eq $required -and $_.passed}).Count -ne 1){throw ('Missing combined verification: '+$required)}
 }
 foreach($name in @('AgesOfCalradia.SoakDiagnostics','AgesOfCalradia.CampaignSystems','AgesOfCalradia.WorkshopProcurement')){
  $entry=@($verified.artifacts|Where-Object {[IO.Path]::GetFileName($_.path) -eq ($name+'.dll')})
  if($entry.Count -ne 1 -or $entry[0].sha256 -notmatch '^[a-fA-F0-9]{64}$'){throw ('Missing/ambiguous verified artifact: '+$name)}
  if(!(Test-Path -LiteralPath $entry[0].path) -or (Get-FileHash -LiteralPath $entry[0].path).Hash -ne $entry[0].sha256){throw ('Verified artifact changed: '+$name)}
  $verifiedBuilds[$name]=$entry[0]
 }
}
if($CandidateRun){
 $CandidateRun=(Resolve-Path -LiteralPath $CandidateRun).Path
 $candidateReceipt=Get-Content -Raw -LiteralPath (Join-Path $CandidateRun 'verification.json')|ConvertFrom-Json
 if($candidateReceipt.Stage -notin @('private_test_candidate_pass','offline_candidate_pass')){throw 'Candidate receipt has not passed verification'}
 if(!(Test-Path -LiteralPath $candidateReceipt.Archive) -or
    (Get-FileHash -LiteralPath $candidateReceipt.Archive).Hash -ne $candidateReceipt.ArchiveHash){throw 'Candidate receipt archive hash mismatch'}
}
$launcherPath=Join-Path (Split-Path -Parent $LogDirectory) 'Configs\LauncherData.xml'
try{
 [xml]$launcher=Get-Content -Raw -LiteralPath $launcherPath
 foreach($id in @('Bannerlord.Harmony','AgesOfCalradia','AgesOfCalradiaSoakDiagnostics','NavalDLC')){
  $entry=@($launcher.UserData.SingleplayerData.ModDatas.UserModData|Where-Object Id -eq $id)
  if($entry.Count -ne 1 -or $entry[0].IsSelected -ne 'true'){$blockers.Add('Launcher module not selected: '+$id)}
 }
}catch{$blockers.Add('Cannot verify launcher selection: '+$_.Exception.Message)}
foreach($part in @(
 @('AgesOfCalradiaSoakDiagnostics','AgesOfCalradia.SoakDiagnostics','AgesOfCalradiaSoakDiagnostics'),
 @('AgesOfCalradiaCampaignSystems','AgesOfCalradia.CampaignSystems','AOC CORE'),
 @('AgesOfCalradiaWorkshopProcurement','AgesOfCalradia.WorkshopProcurement','AOC CORE')
)){
 $source=Join-Path $repo ('Modules\'+$part[0]+'\bin\Win64_Shipping_Client\'+$part[1]+'.dll')
 $expected=$null
 if($VerificationReceipt){
  $source=$verifiedBuilds[$part[1]].path
  $expected=$verifiedBuilds[$part[1]].sha256
 }
 if($candidateReceipt){
  $relative=$part[2]+'\bin\Win64_Shipping_Client\'+$part[1]+'.dll'
  $source=Join-Path (Join-Path $CandidateRun 'package') $relative
  $expected=$candidateReceipt.PackageHashes.PSObject.Properties[$relative].Value
  if(!$expected){$blockers.Add('Missing candidate receipt hash: '+$part[1]);continue}
 }
 $installed=Join-Path $BannerlordDir ('Modules\'+$part[2]+'\bin\Win64_Shipping_Client\'+$part[1]+'.dll')
 if(!(Test-Path -LiteralPath $source) -or !(Test-Path -LiteralPath $installed)){$blockers.Add('Missing assembly: '+$part[1]);continue}
 $hash=(Get-FileHash -LiteralPath $installed).Hash
 $sourceHash=(Get-FileHash -LiteralPath $source).Hash
 if($expected -and $sourceHash -ne $expected){$blockers.Add('Candidate package/receipt mismatch: '+$part[1])}
 if($hash -ne $sourceHash){$blockers.Add('Installed/candidate mismatch: '+$part[1])}
 $mvid=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes($installed)).ManifestModule.ModuleVersionId.ToString()
 $builds+= [pscustomobject]@{name=$part[1];sha256=$hash;mvid=$mvid;path=$installed}
}
$free=[IO.DriveInfo]::new([IO.Path]::GetPathRoot([IO.Path]::GetFullPath($LogDirectory))).AvailableFreeSpace
if($free -lt 6442450944){$blockers.Add('Less than 6 GiB free for 5 GiB capture plus reserve')}
$writable='NOT_PROBED'
if($ProbeWrite){
 $probe=Join-Path $LogDirectory ('.aoc-write-probe-'+[guid]::NewGuid().ToString('N'))
 try{
  $stream=[IO.File]::Open($probe,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
  try{$stream.WriteByte(1);$stream.Flush($true)}finally{$stream.Dispose()}
  $writable='VERIFIED'
 }catch{$writable='FAILED';$blockers.Add('Write probe: '+$_.Exception.Message)}
 finally{if(Test-Path -LiteralPath $probe){Remove-Item -LiteralPath $probe}}
}
$names=@('Bannerlord','Bannerlord.Native','Bannerlord.BLSE.Launcher','Bannerlord.BLSE.LauncherEx','Launcher.Native','TaleWorlds.MountAndBlade.Launcher')
$processes=@(Get-Process -ErrorAction Stop | Where-Object {$_.ProcessName -in $names})
$rolling=Test-Path -LiteralPath (Join-Path $LogDirectory 'AocFrameworkDiagnostics.enabled')
$single=Test-Path -LiteralPath (Join-Path $LogDirectory 'AocSupplyCapture.enabled')
if(!$rolling -and !$single){$blockers.Add('No supply/framework activation marker')}
if($rolling -and $single){$blockers.Add('Conflicting supply/framework activation markers')}
foreach($marker in @('AocEconomyShortCapture.enabled','AocEconomyTransactions.enabled','AocFramework.close.request','AocPacingCalibration.enabled')){
 if(Test-Path -LiteralPath (Join-Path $LogDirectory $marker)){$blockers.Add('Conflicting/stale request: '+$marker)}
}
$log=Join-Path $LogDirectory 'AocFramework-current.tsv'
$receiptPath=Join-Path $LogDirectory 'AocFramework-readiness.json'
$receipt=$null;$state='NOT_RECORDING';$age=$null
if($processes.Count){
 if(!(Test-Path -LiteralPath $receiptPath)){$blockers.Add('Running process has no readiness receipt; loaded capture is unverified')}
 else{
  try{
   $receipt=Get-Content -Raw -LiteralPath $receiptPath | ConvertFrom-Json
   $age=([DateTimeOffset]::UtcNow-[DateTimeOffset]::Parse($receipt.utc)).TotalSeconds
   $checkpoint=Get-Content -LiteralPath ($log+'.state')
   $tail=Read-CaptureTailSession -Path $log
   foreach($problem in @(Get-CaptureReceiptProblems -Receipt $receipt -Builds $builds -Processes $processes -Log $log -Length (Get-Item -LiteralPath $log).Length -Checkpoint $checkpoint -TailSession $tail)){$blockers.Add($problem)}
   if(!$blockers.Count){$state='RECORDING'}
  }catch{$blockers.Add('Unreadable/inconsistent runtime receipt: '+$_.Exception.Message)}
 }
}else{
 if((Test-Path -LiteralPath $log) -or (Test-Path -LiteralPath ($log+'.state'))){$blockers.Add('Prior capture preserved; archive explicitly before a fresh acceptance session')}
 if(!$blockers.Count){$state='READY_TO_LOAD_NOT_RECORDING'}
}
if($blockers.Count){$state='BLOCKED'}
[pscustomobject]@{status=$state;blockers=@($blockers);writeProbe=$writable;freeBytes=$free;builds=$builds;processes=@($processes|Select-Object Id,ProcessName);runtime=$receipt;heartbeatAgeSeconds=$age;limit='Readiness is not acceptance. Installed hooks do not prove branch execution; an open scope or paused campaign is not an accounting defect.'}
