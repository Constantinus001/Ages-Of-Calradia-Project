param(
 [string]$ModuleRoot=(Split-Path -Parent $PSScriptRoot),
 [string]$Python='C:\Users\fpicc\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe',
 [ValidateRange(1,30)][int]$CloudVerdictHoldMinutes=10,
 [switch]$Deploy
)
$ErrorActionPreference='Stop'
$BannerlordDir='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord'
function Run-Check([string]$File,[string[]]$Extra=@()) {
 & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $File @Extra
 if($LASTEXITCODE -ne 0){throw "Verification failed: $File"}
}
function Assert-Closed {
 if(Get-Process | Where-Object {$_.ProcessName -match 'Bannerlord|TaleWorlds.*Launcher'}){throw 'Game/launcher is running; no process will be stopped.'}
}
function Git-Snapshot([string[]]$Arguments) {
 & git -C $snapshot @Arguments
 if($LASTEXITCODE -ne 0){throw 'Isolated source snapshot git operation failed'}
}
$ModuleRoot=(Resolve-Path -LiteralPath $ModuleRoot).Path
$baseGate=Join-Path $ModuleRoot 'Tests\Verify-ProtectedPoliticalBaseline.ps1'
Run-Check $baseGate
# AppData writes from packaged Codex are redirected to its LocalCache. Defender's
# service does not share that virtual view. Use a short, non-AppData location so
# the compiler, scanner and deployment receipt refer to the same physical files.
$run=Join-Path $env:USERPROFILE ('AocRelease\'+[Guid]::NewGuid().ToString('N').Substring(0,12))
$snapshot=Join-Path $run 'source'
New-Item -ItemType Directory -Path $snapshot -Force|Out-Null
# Copy only authored source, scripts and documentation. No user index or branch
# is modified, and protected binaries/assets never enter this snapshot/package.
foreach($relative in @('Builds\Approved560CalendarFixes','Modules\AgesOfCalradiaSoakDiagnostics')){
 $source=Join-Path $ModuleRoot $relative
 foreach($file in Get-ChildItem -LiteralPath $source -Recurse -File){
  $suffix=$file.FullName.Substring($source.Length).TrimStart('\')
  $fixtureJson=$file.Extension -eq '.json' -and $suffix.StartsWith('Tests\fixtures\',[StringComparison]::OrdinalIgnoreCase)
  if($suffix -match '(^|\\)(bin|obj|__pycache__)(\\|$)' -or ($file.Extension -notin @('.cs','.csproj','.ps1','.py','.md','.xml') -and -not $fixtureJson)){continue}
  $target=Join-Path (Join-Path $snapshot $relative) $suffix
  New-Item -ItemType Directory -Path (Split-Path $target) -Force|Out-Null
  Copy-Item -LiteralPath $file.FullName -Destination $target
 }
}
foreach($relative in @('Tests\Verify-Release.ps1','Tests\Release-EconomySidecars.ps1','Tests\Verify-ProtectedPoliticalBaseline.ps1','docs\CODE_QUALITY.md')){
 $target=Join-Path $snapshot $relative
 New-Item -ItemType Directory -Path (Split-Path $target) -Force|Out-Null
 Copy-Item -LiteralPath (Join-Path $ModuleRoot $relative) -Destination $target
}
Git-Snapshot @('init','--quiet')
Git-Snapshot @('config','core.autocrlf','false')
Git-Snapshot @('add','--all')
Git-Snapshot @('-c','user.name=Codex Release Snapshot','-c','user.email=codex-snapshot@localhost','commit','--quiet','-m','Exact isolated economy sidecar verification source')
$commit=(& git -C $snapshot rev-parse HEAD).Trim()
if(@(& git -C $snapshot status --porcelain).Count){throw 'Snapshot is not clean before build'}
$sourceHashes=@{}
foreach($file in & git -C $snapshot ls-files){$sourceHashes[$file]=(Get-FileHash -LiteralPath (Join-Path $snapshot $file)).Hash}
$externalHashes=@{}
$externalFiles=@(Get-ChildItem -LiteralPath $ModuleRoot -File | Where-Object {$_.Extension -in @('.cs','.csproj','.xml')})
foreach($dir in @('GUI','bin\Win64_Shipping_Client')){$externalFiles+=Get-ChildItem -LiteralPath (Join-Path $ModuleRoot $dir) -Recurse -File}
foreach($relative in @('Tests\Verify-CalendarMath.ps1','Tests\Verify-StrategicMapCoverage.ps1','Tests\StrategicMapPixelChecks.cs','Tests\Verify-ProtectedPoliticalBaseline.ps1','AssetSources\GauntletUI\ui_world_calendar_1.png')){$externalFiles+=Get-Item -LiteralPath (Join-Path $ModuleRoot $relative)}
$externalFiles+=Get-Item -LiteralPath (Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.2\lib\net472\0Harmony.dll')
foreach($relative in @('Modules\SandBox\ModuleData\settlements.xml','Modules\NavalDLC\ModuleData\settlements.xml')){
 $path=Join-Path $BannerlordDir $relative; if(Test-Path -LiteralPath $path){$externalFiles+=Get-Item -LiteralPath $path}
}
foreach($file in $externalFiles){$externalHashes[$file.FullName]=(Get-FileHash -LiteralPath $file.FullName).Hash}
$nativeHashes=@{}; foreach($file in Get-ChildItem -LiteralPath (Join-Path $BannerlordDir 'bin\Win64_Shipping_Client') -Filter '*.dll'){$nativeHashes[$file.FullName]=(Get-FileHash -LiteralPath $file.FullName).Hash}
function Assert-Inputs {
 foreach($file in $sourceHashes.Keys){if((Get-FileHash -LiteralPath (Join-Path $snapshot $file)).Hash -ne $sourceHashes[$file]){throw "Snapshot source changed: $file"}}
 foreach($map in @($externalHashes,$nativeHashes)){foreach($file in $map.Keys){if((Get-FileHash -LiteralPath $file).Hash -ne $map[$file]){throw "Verification input changed: $file"}}}
}
$calendar=Join-Path $snapshot 'Builds\Approved560CalendarFixes'
$diagnostics=Join-Path $snapshot 'Modules\AgesOfCalradiaSoakDiagnostics'
foreach($project in @((Join-Path $calendar 'Approved560CalendarFixes.csproj'),(Join-Path $diagnostics 'AgesOfCalradiaSoakDiagnostics.csproj'))){
 & dotnet msbuild $project /restore /t:Rebuild /p:Configuration=Release /p:TreatWarningsAsErrors=true /v:minimal
 if($LASTEXITCODE -ne 0){throw "Release build failed: $project"}
}
foreach($name in @('Verify-WorkshopPayment.ps1','Verify-WorkshopBatch.ps1','Verify-WorkshopRecipeCadence.ps1','Verify-SettlementPayment.ps1')){
 Run-Check (Join-Path $calendar $name) @('-ModuleRoot',$ModuleRoot)
}
Run-Check (Join-Path $calendar 'Verify-NativeWorkshopBatch.ps1') @('-ModuleRoot',$ModuleRoot,'-WithDiagnostics')
Run-Check (Join-Path $calendar 'Verify-AiSettlementSale.ps1') @('-ModuleRoot',$ModuleRoot,'-WithDiagnostics')
Run-Check (Join-Path $diagnostics 'Tests\Verify-SupplyCash.ps1')
Run-Check (Join-Path $diagnostics 'Tests\Verify-WorkshopCashBoundaries.ps1')
Run-Check (Join-Path $diagnostics 'Tests\Verify-CaptureReadiness.ps1')
Run-Check (Join-Path $diagnostics 'Tests\Verify-ReadinessPolicy.ps1')
Run-Check (Join-Path $diagnostics 'Tests\Verify-ShipLifecycle.ps1')
Run-Check (Join-Path $diagnostics 'Tests\Verify-RewardObservation.ps1')
Run-Check (Join-Path $diagnostics 'Tests\Verify-WoolOutParameter.ps1')
& $Python -B -m unittest discover -s (Join-Path $diagnostics 'Tests') -p 'test_*.py'
if($LASTEXITCODE -ne 0){throw 'Supply analyzer regressions failed'}
foreach($name in @('Verify-CalendarMath.ps1','Verify-StrategicMapCoverage.ps1')){Run-Check (Join-Path $ModuleRoot ('Tests\'+$name))}
foreach($file in $sourceHashes.Keys){if((Get-FileHash -LiteralPath (Join-Path $snapshot $file)).Hash -ne $sourceHashes[$file]){throw "Source changed during build: $file"}}
$package=Join-Path $run 'package'
New-Item -ItemType Directory -Path $package|Out-Null
$names=@('AgesOfCalradia.Approved560CalendarFixes.dll','AgesOfCalradia.SoakDiagnostics.dll')
$sources=@((Join-Path $calendar ('bin\Win64_Shipping_Client\'+$names[0])),(Join-Path $diagnostics ('bin\Win64_Shipping_Client\'+$names[1])))
$targets=@((Join-Path $BannerlordDir ('Modules\AOC CORE\bin\Win64_Shipping_Client\'+$names[0])),(Join-Path $BannerlordDir ('Modules\AgesOfCalradiaSoakDiagnostics\bin\Win64_Shipping_Client\'+$names[1])))
foreach($source in $sources){Copy-Item -LiteralPath $source -Destination $package}
if(Compare-Object $names @(Get-ChildItem -LiteralPath $package -File|ForEach-Object Name)){throw 'Package allowlist differs'}
$hashes=@{}; foreach($name in $names){$hashes[$name]=(Get-FileHash -LiteralPath (Join-Path $package $name)).Hash}
$receipt=Join-Path $run 'verification.json'
@{SourceCommit=$commit; SourceHashes=$sourceHashes; ExternalHashes=$externalHashes; NativeHashes=$nativeHashes; PackageHashes=$hashes; Deployed=$false; Security='pending'}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $receipt
$defender=Get-MpComputerStatus
if(-not $defender.AntivirusEnabled){throw "Defender unavailable/disabled; deployment blocked. Candidate: $run"}
$scanStart=Get-Date
try { Start-MpScan -ScanPath $package -ScanType CustomScan }
catch {
 @{SourceCommit=$commit; SourceHashes=$sourceHashes; ExternalHashes=$externalHashes; NativeHashes=$nativeHashes; PackageHashes=$hashes; Deployed=$false; Security='scan_failed'; Error=$_.ToString()}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $receipt
 throw
}
for($minute=0;$minute -lt $CloudVerdictHoldMinutes;$minute++){
 Start-Sleep -Seconds 60
 $threats=@(Get-MpThreatDetection|Where-Object {$_.InitialDetectionTime -ge $scanStart.AddMinutes(-1) -and ($_.Resources -match [regex]::Escape($package))})
 if($threats.Count){throw 'Security detection in exact candidate; no deployment'}
 foreach($name in $names){if((Get-FileHash -LiteralPath (Join-Path $package $name)).Hash -ne $hashes[$name]){throw 'Candidate changed during security hold'}}
 Write-Output "Security hold: $($minute+1)/$CloudVerdictHoldMinutes minutes clean"
}
Assert-Inputs
if($Deploy){
 Assert-Closed
 Run-Check $baseGate
 $backup=Join-Path $run 'installed-backup'; New-Item -ItemType Directory -Path $backup|Out-Null
 $backupHashes=@{}
 for($i=0;$i -lt 2;$i++){
  $backupHashes[$names[$i]]=(Get-FileHash -LiteralPath $targets[$i]).Hash
  Copy-Item -LiteralPath $targets[$i] -Destination (Join-Path $backup $names[$i])
  if((Get-FileHash -LiteralPath (Join-Path $backup $names[$i])).Hash -ne $backupHashes[$names[$i]]){throw 'Backup hash mismatch'}
 }
 try{
  Assert-Closed
  for($i=0;$i -lt 2;$i++){
   Copy-Item -LiteralPath (Join-Path $package $names[$i]) -Destination $targets[$i]
   if((Get-FileHash -LiteralPath $targets[$i]).Hash -ne $hashes[$names[$i]]){throw 'Installed hash mismatch'}
  }
  Run-Check $baseGate
 }catch{
  $originalFailure=$_; $restoreFailures=@()
  for($i=0;$i -lt 2;$i++){
   try {
    Copy-Item -LiteralPath (Join-Path $backup $names[$i]) -Destination $targets[$i]
    if((Get-FileHash -LiteralPath $targets[$i]).Hash -ne $backupHashes[$names[$i]]){throw 'Restored hash mismatch'}
   }catch{$restoreFailures+="$($names[$i]): $_"}
  }
  throw "Deployment failed: $originalFailure. Rollback failures: $($restoreFailures -join '; '). Backups: $backup"
 }
}
@{SourceCommit=$commit; SourceHashes=$sourceHashes; ExternalHashes=$externalHashes; NativeHashes=$nativeHashes; PackageHashes=$hashes; BackupHashes=$backupHashes; Deployed=[bool]$Deploy; Security='clean'}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $receipt
"PASS: scoped economy sidecars; deployed=$Deploy; receipt=$receipt"
