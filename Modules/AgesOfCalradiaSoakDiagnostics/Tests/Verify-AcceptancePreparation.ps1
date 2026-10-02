$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$fixture=Join-Path $env:TEMP ('aoc-acceptance-preparation-'+[guid]::NewGuid().ToString('N'))
$game=Join-Path $fixture 'game';$logs=Join-Path $fixture 'logs'
New-Item -ItemType Directory -Path $logs -Force|Out-Null
$configs=Join-Path $fixture 'Configs';New-Item -ItemType Directory -Path $configs|Out-Null
$mods=(@('Bannerlord.Harmony','AgesOfCalradia','AgesOfCalradiaSoakDiagnostics','NavalDLC')|ForEach-Object {'<UserModData><Id>'+$_+'</Id><IsSelected>true</IsSelected></UserModData>'}) -join ''
[IO.File]::WriteAllText((Join-Path $configs 'LauncherData.xml'),('<UserData><SingleplayerData><ModDatas>'+$mods+'</ModDatas></SingleplayerData></UserData>'))
foreach($part in @(
 @('AgesOfCalradiaSoakDiagnostics','AgesOfCalradia.SoakDiagnostics','AgesOfCalradiaSoakDiagnostics'),
 @('AgesOfCalradiaCampaignSystems','AgesOfCalradia.CampaignSystems','AOC CORE'),
 @('AgesOfCalradiaWorkshopProcurement','AgesOfCalradia.WorkshopProcurement','AOC CORE')
)){
 $dest=Join-Path $game ('Modules\'+$part[2]+'\bin\Win64_Shipping_Client')
 New-Item -ItemType Directory -Path $dest -Force|Out-Null
 Copy-Item -LiteralPath (Join-Path $repo ('Modules\'+$part[0]+'\bin\Win64_Shipping_Client\'+$part[1]+'.dll')) -Destination $dest
}
$old=Join-Path $logs 'AocFramework-current.tsv'
[IO.File]::WriteAllText($old,'preserved fixture, not a campaign')
[IO.File]::WriteAllText((Join-Path $logs 'unrelated.txt'),'do not change')
$hash=(Get-FileHash -LiteralPath $old).Hash
$rejected=$false
try{& (Join-Path $PSScriptRoot 'Prepare-AcceptanceCapture.ps1') -BannerlordDir $game -LogDirectory $logs|Out-Null}catch{if($_.Exception.Message -notmatch 'archive'){throw};$rejected=$true}
if(!$rejected -or (Get-FileHash -LiteralPath $old).Hash -ne $hash){throw 'Implicit archival or evidence overwrite'}
$result=& (Join-Path $PSScriptRoot 'Prepare-AcceptanceCapture.ps1') -BannerlordDir $game -LogDirectory $logs -ArchiveExisting
if($result.status -ne 'PREPARED_NOT_RECORDING' -or $result.targetCampaignDays -ne 5){throw 'Prepared falsely claimed recording'}
if((Get-FileHash -LiteralPath (Join-Path $result.archive 'AocFramework-current.tsv')).Hash -ne $hash -or (Test-Path -LiteralPath $old)){throw 'Archive not preserved/reset'}
if((Get-Content -Raw -LiteralPath (Join-Path $logs 'unrelated.txt')) -ne 'do not change'){throw 'Unrelated file changed'}
if((Get-Content -Raw -LiteralPath (Join-Path $logs 'AocAcceptance.days')) -ne '5'){throw 'Wrong acceptance day bound'}
$ready=& (Join-Path $PSScriptRoot 'Get-CaptureReadiness.ps1') -BannerlordDir $game -LogDirectory $logs -ProbeWrite
if($ready.status -ne 'READY_TO_LOAD_NOT_RECORDING'){throw ($ready|ConvertTo-Json -Depth 4)}
# Isolated candidate receipt is authoritative, not whichever workspace build
# happens to exist later. Tampering must still fail closed before archival.
$candidate=Join-Path $fixture 'candidate';$package=Join-Path $candidate 'package'
New-Item -ItemType Directory -Path $package|Out-Null
Copy-Item -LiteralPath (Join-Path $game 'Modules/AOC CORE'),(Join-Path $game 'Modules/AgesOfCalradiaSoakDiagnostics') -Destination $package -Recurse
$hashes=@{}
foreach($file in Get-ChildItem -LiteralPath $package -File -Recurse){$hashes[$file.FullName.Substring($package.Length+1)]=(Get-FileHash -LiteralPath $file.FullName).Hash}
$archive=Join-Path $candidate 'fixture.zip';[IO.File]::WriteAllText($archive,'synthetic fixture archive identity')
$receiptPath=Join-Path $candidate 'verification.json'
$receipt=@{Stage='private_test_candidate_pass';Archive=$archive;ArchiveHash=(Get-FileHash -LiteralPath $archive).Hash;PackageHashes=$hashes}
[IO.File]::WriteAllText($receiptPath,($receipt|ConvertTo-Json -Depth 4))
$ready=& (Join-Path $PSScriptRoot 'Get-CaptureReadiness.ps1') -BannerlordDir $game -LogDirectory $logs -CandidateRun $candidate
if($ready.status -ne 'READY_TO_LOAD_NOT_RECORDING'){throw 'Valid candidate receipt rejected'}
$key='AgesOfCalradiaSoakDiagnostics\bin\Win64_Shipping_Client\AgesOfCalradia.SoakDiagnostics.dll'
$receipt.PackageHashes[$key]='0'*64
[IO.File]::WriteAllText($receiptPath,($receipt|ConvertTo-Json -Depth 4))
[IO.File]::WriteAllText($old,'must not archive on bad candidate identity')
$before=(Get-FileHash -LiteralPath $old).Hash
$rejected=$false
try{& (Join-Path $PSScriptRoot 'Prepare-AcceptanceCapture.ps1') -BannerlordDir $game -LogDirectory $logs -CandidateRun $candidate -ArchiveExisting|Out-Null}catch{
 if($_.Exception.Message -notmatch 'package/receipt mismatch'){throw};$rejected=$true
}
if(!$rejected -or (Get-FileHash -LiteralPath $old).Hash -ne $before){throw 'Mismatched candidate allowed evidence changes'}
'PASS: exact candidate receipt binding and tamper rejection before evidence archival.'
'PASS: explicit hash-verified archival, unrelated files preserved, bounded marker and offline-ready distinct from recording. Temporary fixture only.'
