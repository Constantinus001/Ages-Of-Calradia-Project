param(
 [string]$ModuleRoot=(Split-Path -Parent $PSScriptRoot),
 [string]$PythonPath=(Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'),
 [ValidateRange(10,30)][int]$SecurityHoldMinutes=10,
 [switch]$PrivateTestNoSecurityScan
)
$ErrorActionPreference='Stop'
# Offline candidate only. No deployment switch and no writes to game/saves/settings.
# This is not full-Core release certification; protected artifacts remain external.
$ModuleRoot=(Resolve-Path -LiteralPath $ModuleRoot).Path
function Check([string]$Script,[string[]]$Arguments=@()) {
 & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Script @Arguments
 if($LASTEXITCODE -ne 0){throw "Candidate check failed: $Script"}
}
Check (Join-Path $ModuleRoot 'Tests\Verify-ProtectedPoliticalBaseline.ps1')
$run=Join-Path $env:USERPROFILE ('AocRelease\framework-'+[Guid]::NewGuid().ToString('N').Substring(0,12))
$snapshot=Join-Path $run 'source'
New-Item -ItemType Directory -Path $snapshot|Out-Null
$sourceDirs=@('Builds\Approved560CalendarFixes','Modules\AgesOfCalradiaCampaignSystems',
 'Modules\AgesOfCalradiaWorkshopProcurement','Modules\AgesOfCalradiaSoakDiagnostics','Modules\AgesOfCalradiaLogistics')
foreach($relative in $sourceDirs){
 $origin=Join-Path $ModuleRoot $relative
 foreach($file in Get-ChildItem -LiteralPath $origin -File -Recurse){
  $suffix=$file.FullName.Substring($origin.Length).TrimStart('\')
  $fixtureJson=$file.Extension -eq '.json' -and $suffix.StartsWith('Tests\fixtures\',[StringComparison]::OrdinalIgnoreCase)
  if($suffix -match '(^|\\)(bin|obj|__pycache__)(\\|$)' -or ($file.Extension -notin @('.cs','.csproj','.ps1','.py','.md','.xml') -and -not $fixtureJson)){continue}
  $destination=Join-Path (Join-Path $snapshot $relative) $suffix
  New-Item -ItemType Directory -Path (Split-Path $destination) -Force|Out-Null
  Copy-Item -LiteralPath $file.FullName -Destination $destination
 }
}
foreach($relative in @('SubModule.xml','docs\CODE_QUALITY.md','docs\CAMPAIGN_SYSTEMS_INSTALL.md','Tests\Split-CampaignSystemsPackage.ps1','Tests\Package-CampaignSystemsCandidate.ps1','Tests\Verify-CampaignSystemsPackage.ps1')){
 $destination=Join-Path $snapshot $relative
 New-Item -ItemType Directory -Path (Split-Path $destination) -Force|Out-Null
 Copy-Item -LiteralPath (Join-Path $ModuleRoot $relative) -Destination $destination
}
function SnapshotGit([string[]]$Arguments){
 & git -C $snapshot @Arguments
 if($LASTEXITCODE -ne 0){throw 'Isolated snapshot git operation failed'}
}
SnapshotGit @('init','--quiet')
SnapshotGit @('config','core.autocrlf','false')
SnapshotGit @('add','--all')
SnapshotGit @('-c','user.name=Codex Candidate Snapshot','-c','user.email=codex-snapshot@localhost','commit','--quiet','-m','Exact offline framework candidate sources')
if(@(& git -C $snapshot status --porcelain).Count){throw 'Snapshot not clean before build'}
$sourceCommit=(& git -C $snapshot rev-parse HEAD).Trim()
$inputs=@{}
foreach($relative in & git -C $snapshot ls-files){$path=Join-Path $snapshot $relative;$inputs[$path]=(Get-FileHash -LiteralPath $path).Hash}
# Bind external approved baseline, verification scripts and native references.
$game='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord'
$installedCore=Join-Path $game 'Modules\AOC CORE'
$installedManifest=Join-Path $installedCore 'SubModule.xml'
$inputs[$installedManifest]=(Get-FileHash -LiteralPath $installedManifest).Hash
[xml]$preservedManifest=Get-Content -Raw -LiteralPath $installedManifest
foreach($submodule in $preservedManifest.Module.SubModules.SubModule){
 $path=Join-Path $installedCore ('bin\Win64_Shipping_Client\'+$submodule.DLLName.value)
 $inputs[$path]=(Get-FileHash -LiteralPath $path).Hash
}
foreach($directory in @('bin\Win64_Shipping_Client','GUI','Tests')){
 foreach($file in Get-ChildItem -LiteralPath (Join-Path $ModuleRoot $directory) -File -Recurse){$inputs[$file.FullName]=(Get-FileHash -LiteralPath $file.FullName).Hash}
}
foreach($file in Get-ChildItem -LiteralPath $ModuleRoot -File){if($file.Extension -in @('.cs','.xml','.csproj')){$inputs[$file.FullName]=(Get-FileHash -LiteralPath $file.FullName).Hash}}
foreach($file in Get-ChildItem -LiteralPath (Join-Path $game 'bin\Win64_Shipping_Client') -Filter '*.dll'){$inputs[$file.FullName]=(Get-FileHash -LiteralPath $file.FullName).Hash}
foreach($path in @((Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.2\lib\net472\0Harmony.dll'),(Join-Path $ModuleRoot 'AssetSources\GauntletUI\ui_world_calendar_1.png'))){$inputs[$path]=(Get-FileHash -LiteralPath $path).Hash}
foreach($relative in @('Modules\SandBox\ModuleData\settlements.xml','Modules\NavalDLC\ModuleData\settlements.xml')){
 $path=Join-Path $game $relative;if(Test-Path -LiteralPath $path){$inputs[$path]=(Get-FileHash -LiteralPath $path).Hash}
}
$receipt=Join-Path $run 'verification.json'
$scope=if($PrivateTestNoSecurityScan){'private-diagnostic-test-not-public-release'}else{'offline-framework-candidate-not-full-Core-release'}
$record=[ordered]@{Scope=$scope;PrivateTestOnly=[bool]$PrivateTestNoSecurityScan;SourceCommit=$sourceCommit;InputHashes=$inputs;Deployed=$false;Stage='building';Security='pending'}
function Receipt { $record|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $receipt }
Receipt
Write-Output "Candidate workspace: $run"
try {
 & dotnet build (Join-Path $snapshot 'Builds\Approved560CalendarFixes\Approved560CalendarFixes.csproj') -c Release -v minimal /p:TreatWarningsAsErrors=true
 if($LASTEXITCODE -ne 0){throw 'Snapshot calendar companion build failed'}
 Check (Join-Path $snapshot 'Modules\AgesOfCalradiaCampaignSystems\Tests\Verify-Candidate.ps1') @('-BaselineRoot',$ModuleRoot,'-PythonPath',$PythonPath)
 $package=Join-Path $run 'package'
 $files=[ordered]@{
  'AOC CORE\SubModule.xml'='SubModule.xml'
  'AOC CORE\bin\Win64_Shipping_Client\AgesOfCalradia.CampaignSystems.dll'='Modules\AgesOfCalradiaCampaignSystems\bin\Win64_Shipping_Client\AgesOfCalradia.CampaignSystems.dll'
  'AOC CORE\bin\Win64_Shipping_Client\AgesOfCalradia.Approved560CalendarFixes.dll'='Builds\Approved560CalendarFixes\bin\Win64_Shipping_Client\AgesOfCalradia.Approved560CalendarFixes.dll'
  'AOC CORE\CampaignSystems.example.xml'='Modules\AgesOfCalradiaCampaignSystems\CampaignSystems.example.xml'
  'AOC CORE\bin\Win64_Shipping_Client\AgesOfCalradia.WorkshopProcurement.dll'='Modules\AgesOfCalradiaWorkshopProcurement\bin\Win64_Shipping_Client\AgesOfCalradia.WorkshopProcurement.dll'
  'AgesOfCalradiaSoakDiagnostics\SubModule.xml'='Modules\AgesOfCalradiaSoakDiagnostics\SubModule.xml'
  'AgesOfCalradiaSoakDiagnostics\bin\Win64_Shipping_Client\AgesOfCalradia.SoakDiagnostics.dll'='output\procurement-diagnostics\AgesOfCalradia.SoakDiagnostics.dll'
  'AgesOfCalradiaLogistics\SubModule.xml'='Modules\AgesOfCalradiaLogistics\SubModule.xml'
  'AgesOfCalradiaLogistics\bin\Win64_Shipping_Client\AgesOfCalradiaLogistics.dll'='output\campaign-systems-logistics\AgesOfCalradiaLogistics.dll'
  'AgesOfCalradiaLogistics\ModuleData\supply_items.xml'='Modules\AgesOfCalradiaLogistics\ModuleData\supply_items.xml'
  'AgesOfCalradiaLogistics\ModuleData\module_strings.xml'='Modules\AgesOfCalradiaLogistics\ModuleData\module_strings.xml'
 }
 $hashes=@{}
 foreach($relative in $files.Keys){
  $target=Join-Path $package $relative
  New-Item -ItemType Directory -Path (Split-Path $target) -Force|Out-Null
  $source=Join-Path $snapshot $files[$relative]
  Copy-Item -LiteralPath $source -Destination $target
  $hashes[$relative]=(Get-FileHash -LiteralPath $source).Hash
  if((Get-FileHash -LiteralPath $target).Hash -ne $hashes[$relative]){throw 'Package copy hash mismatch'}
 }
 # The checkout manifest is not the installed manifest: preserve user-approved
 # border/UI integrations and add ONLY this companion. Never replace unrelated
 # registrations with an older source manifest.
 [xml]$sourceManifest=Get-Content -Raw (Join-Path $snapshot 'SubModule.xml')
 $frameworkEntries=@($sourceManifest.Module.SubModules.SubModule|Where-Object {$_.DLLName.value -in @('AgesOfCalradia.CampaignSystems.dll','AgesOfCalradia.WorkshopProcurement.dll')})
 if($frameworkEntries.Count -ne 2){throw 'Expected framework and procurement registrations'}
 foreach($frameworkEntry in $frameworkEntries){
 $existingFramework=@($preservedManifest.Module.SubModules.SubModule|Where-Object {$_.DLLName.value -eq $frameworkEntry.DLLName.value})
 if($existingFramework.Count -gt 1){throw 'Installed framework registration duplicated'}
 if($existingFramework.Count -eq 1){
  if($existingFramework[0].OuterXml -ne $frameworkEntry.OuterXml){throw 'Existing framework registration differs; do not overwrite'}
 }else{$preservedManifest.Module.SubModules.AppendChild($preservedManifest.ImportNode($frameworkEntry,$true))|Out-Null}
 }
 $manifestTarget=Join-Path $package 'AOC CORE\SubModule.xml'
 $preservedManifest.Save($manifestTarget)
 $hashes['AOC CORE\SubModule.xml']=(Get-FileHash -LiteralPath $manifestTarget).Hash
 Check (Join-Path $snapshot 'Tests\Verify-CampaignSystemsPackage.ps1') @('-PackageRoot',$package,'-BaselineRoot',$ModuleRoot)
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 $archive=Join-Path $run 'CampaignSystems-candidate.zip'
 [IO.Compression.ZipFile]::CreateFromDirectory($package,$archive)
 $zip=[IO.Compression.ZipFile]::OpenRead($archive)
 try {
  if($zip.Entries.Count -ne $files.Count){throw 'Archive entry count mismatch'}
  foreach($entry in $zip.Entries){
   $relative=$entry.FullName.Replace('/','\')
   if(-not $hashes.ContainsKey($relative)){throw "Unexpected archive entry: $relative"}
   $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
   try{$hash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}finally{$sha.Dispose();$stream.Dispose()}
   if($hash -ne $hashes[$relative]){throw 'Archive content differs from tested candidate'}
  }
 }finally{$zip.Dispose()}
 $record.PackageHashes=$hashes;$record.Archive=$archive;$record.ArchiveHash=(Get-FileHash -LiteralPath $archive).Hash
 $splits=& (Join-Path $snapshot 'Tests\Split-CampaignSystemsPackage.ps1') -PackageRoot $package -Destination (Join-Path $run 'downloads') -GuidePath (Join-Path $snapshot 'docs\CAMPAIGN_SYSTEMS_INSTALL.md')
 $record.Downloads=$splits
 if($PrivateTestNoSecurityScan){
  # This path is local diagnostic testing only. Its receipt is rejected by the
  # public installer and cannot stand in for a security result.
  $record.Stage='private_test_hash_verification';Receipt
 }else{
  $record.Stage='security';Receipt
  $status=Get-MpComputerStatus
  if(-not $status.AntivirusEnabled -or -not $status.RealTimeProtectionEnabled){throw 'Defender unavailable/disabled; candidate blocked'}
  $scanStart=Get-Date
  Start-MpScan -ScanType CustomScan -ScanPath $package
  Start-MpScan -ScanType CustomScan -ScanPath $archive
  foreach($download in $splits.Values){Start-MpScan -ScanType CustomScan -ScanPath $download.Path}
  for($minute=0;$minute -lt $SecurityHoldMinutes;$minute++){
   Start-Sleep -Seconds 60
   $threats=@(Get-MpThreatDetection|Where-Object {$_.InitialDetectionTime -ge $scanStart.AddMinutes(-1) -and ($_.Resources -match [regex]::Escape($run))})
   if($threats.Count){throw 'Security detection: candidate blocked'}
   foreach($relative in $hashes.Keys){if((Get-FileHash -LiteralPath (Join-Path $package $relative)).Hash -ne $hashes[$relative]){throw 'Candidate changed during security hold'}}
   if((Get-FileHash -LiteralPath $archive).Hash -ne $record.ArchiveHash){throw 'Archive changed during security hold'}
   foreach($download in $splits.Values){if((Get-FileHash -LiteralPath $download.Path).Hash -ne $download.Hash){throw 'Split download changed during security hold'}}
   Write-Output "Security hold: $($minute+1)/$SecurityHoldMinutes minutes"
  }
 }
 foreach($path in $inputs.Keys){if((Get-FileHash -LiteralPath $path).Hash -ne $inputs[$path]){throw "Verification input changed: $path"}}
 if(@(& git -C $snapshot diff --name-only HEAD).Count){throw 'Tracked snapshot changed'}
 Check (Join-Path $ModuleRoot 'Tests\Verify-ProtectedPoliticalBaseline.ps1')
 if($PrivateTestNoSecurityScan){
  $record.Security='not_run_private_test_only';$record.Stage='private_test_candidate_pass';Receipt
  "PASS: private test candidate only; Defender scan deliberately not run; not a public release. Receipt: $receipt"
 }else{
  $record.Security='no_detection_during_scan_and_hold';$record.Stage='offline_candidate_pass';Receipt
  "PASS: offline candidate only; not deployed; full release/live acceptance outstanding. Receipt: $receipt"
 }
}catch{
 $record.Stage='blocked';$record.Error=$_.ToString();Receipt
 throw
}
