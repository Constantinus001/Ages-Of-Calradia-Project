param(
 [Parameter(Mandatory=$true)][string]$CandidateRun,
 [Parameter(Mandatory=$true)][string]$DeploymentBackup
)
$ErrorActionPreference='Stop'
$run=(Resolve-Path -LiteralPath $CandidateRun).Path
$backup=(Resolve-Path -LiteralPath $DeploymentBackup).Path
if(-not $backup.StartsWith($run+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Backup does not belong to candidate run.'}
$recordPath=Join-Path $backup 'deployment.json'
$record=Get-Content -Raw -LiteralPath $recordPath|ConvertFrom-Json
if(-not $record.Applied -or $record.State -ne 'private_test_deployed_hash_verified'){throw 'Deployment receipt is not an applied verified private deployment.'}
$modules='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules'
foreach($item in $record.Files){
 $target=[IO.Path]::GetFullPath($item.Target)
 if(-not $target.StartsWith($modules+'\',[StringComparison]::OrdinalIgnoreCase)){throw "Unsafe target: $target"}
 if(-not(Test-Path -LiteralPath $target)){if($item.Before){throw "Missing original target: $target"};continue}
 $current=(Get-FileHash -LiteralPath $target).Hash
 # Permit a retry after a process interruption only when this output is already
 # exactly restored. A third hash is never overwritten.
 if($current -ne $item.After -and $current -ne $item.Before){throw "Refusing rollback; target changed after deployment: $($item.Relative)"}
}
foreach($item in $record.Files){
 $target=$item.Target
 if($item.Before){
  $saved=Join-Path $backup $item.Relative
  if((Get-FileHash -LiteralPath $saved).Hash -ne $item.Before){throw "Backup hash mismatch: $($item.Relative)"}
  Copy-Item -LiteralPath $saved -Destination $target -Force
  if((Get-FileHash -LiteralPath $target).Hash -ne $item.Before){throw "Restored hash mismatch: $($item.Relative)"}
 }elseif(Test-Path -LiteralPath $target){
  Remove-Item -LiteralPath $target -Force
  if(Test-Path -LiteralPath $target){throw "Failed to remove installer-created: $($item.Relative)"}
 }
}
# This marker was created by this failed pre-launch attempt, is empty by protocol,
# and has no capture output. Do not touch a populated marker or any capture files.
$marker='C:\Users\fpicc\Documents\Mount and Blade II Bannerlord\AgesOfCalradiaSoakDiagnostics\AocFrameworkDiagnostics.enabled'
if(Test-Path -LiteralPath $marker){
 if((Get-Item -LiteralPath $marker).Length -ne 0){throw 'Refusing to remove nonempty diagnostic marker.'}
 Remove-Item -LiteralPath $marker -Force
}
$record.Applied=$false
$record.State='rolled_back_before_launch_conflicting_standalone_selected'
$record | Add-Member -NotePropertyName RollbackUtc -NotePropertyValue ((Get-Date).ToUniversalTime().ToString('o')) -Force
$record | Add-Member -NotePropertyName Marker -NotePropertyValue 'withdrawn_before_launch' -Force
$record|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $recordPath
'PASS: exact pre-deployment hashes restored; installer-created procurement DLL and empty marker removed.'
