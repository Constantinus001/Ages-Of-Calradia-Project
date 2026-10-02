param(
 [Parameter(Mandatory=$true)][string]$CandidateRun,
 [string]$LauncherPath=(Join-Path $env:USERPROFILE 'Documents\Mount and Blade II Bannerlord\Configs\LauncherData.xml')
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'CampaignDeploymentProcessGuard.ps1')
Assert-CampaignDeploymentIdle
$run=(Resolve-Path -LiteralPath $CandidateRun).Path
$launcher=(Resolve-Path -LiteralPath $LauncherPath).Path
[xml]$document=Get-Content -Raw -LiteralPath $launcher
$entries=@($document.SelectNodes('//UserModData') | Where-Object {$_.Id -eq 'AgesOfCalradiaWorkshopProcurement'})
if($entries.Count -ne 1 -or $entries[0].IsSelected -ne 'true'){
 throw 'Expected exactly one selected standalone Workshop Procurement launcher entry.'
}
$before=@{}
foreach($entry in $document.SelectNodes('//UserModData')){$before[$entry.Id]=$entry.IsSelected}
$backup=Join-Path $run 'launcher-before-integrated-procurement.xml'
if(Test-Path -LiteralPath $backup){throw 'Refusing to replace existing launcher backup.'}
Copy-Item -LiteralPath $launcher -Destination $backup
$originalHash=(Get-FileHash -LiteralPath $launcher).Hash
if((Get-FileHash -LiteralPath $backup).Hash -ne $originalHash){throw 'Launcher backup hash mismatch.'}
$entries[0].IsSelected='false'
$document.Save($launcher)
[xml]$verified=Get-Content -Raw -LiteralPath $launcher
$after=@{}
foreach($entry in $verified.SelectNodes('//UserModData')){$after[$entry.Id]=$entry.IsSelected}
if($after['AgesOfCalradiaWorkshopProcurement'] -ne 'false'){throw 'Standalone launcher entry remains selected.'}
foreach($id in $before.Keys){
 if($id -ne 'AgesOfCalradiaWorkshopProcurement' -and $after[$id] -ne $before[$id]){
  throw "Unexpected launcher selection change: $id"
 }
}
[ordered]@{Launcher=$launcher;BeforeHash=$originalHash;Backup=$backup;Disabled='AgesOfCalradiaWorkshopProcurement';ChangedUtc=(Get-Date).ToUniversalTime().ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'launcher-private-test-change.json')
'PASS: only the selected standalone Workshop Procurement launcher entry was disabled; verified backup retained.'
