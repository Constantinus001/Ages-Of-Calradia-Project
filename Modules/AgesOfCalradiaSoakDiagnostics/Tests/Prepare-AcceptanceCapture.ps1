param(
 [string]$BannerlordDir='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord',
 [string]$LogDirectory=(Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Mount and Blade II Bannerlord\AgesOfCalradiaSoakDiagnostics'),
 [switch]$ArchiveExisting,
 [ValidateRange(1,29)][int]$TargetDays=5,
 [string]$CandidateRun,
 [string]$VerificationReceipt
)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
. (Join-Path $repo 'Tests\CampaignDeploymentProcessGuard.ps1')
Assert-CampaignDeploymentIdle
$check=& (Join-Path $PSScriptRoot 'Get-CaptureReadiness.ps1') -BannerlordDir $BannerlordDir -LogDirectory $LogDirectory -ProbeWrite -CandidateRun $CandidateRun -VerificationReceipt $VerificationReceipt
$allowed=@('Prior capture preserved; archive explicitly before a fresh acceptance session','No supply/framework activation marker')
$unexpected=@($check.blockers|Where-Object {$_ -notin $allowed})
if($unexpected.Count){throw ('Preparation blocked: '+($unexpected -join '; '))}
$files=@('AocFramework-current.tsv','AocFramework-current.tsv.state','AocFramework-current.summary.json','AocFramework-readiness.json','AocAcceptance.days')
$present=@($files|Where-Object {Test-Path -LiteralPath (Join-Path $LogDirectory $_)})
if($present.Count -and !$ArchiveExisting){throw 'Existing evidence preserved. Use -ArchiveExisting for an explicit, hash-verified archive.'}
$archive=$null
if($present.Count){
 $archive=Join-Path $LogDirectory ('acceptance-archive-'+[guid]::NewGuid().ToString('N'))
 New-Item -ItemType Directory -Path $archive|Out-Null
 $hashes=@{}
 foreach($name in $present){
  $source=Join-Path $LogDirectory $name
  $hashes[$name]=(Get-FileHash -LiteralPath $source).Hash
  Copy-Item -LiteralPath $source -Destination (Join-Path $archive $name)
  if((Get-FileHash -LiteralPath (Join-Path $archive $name)).Hash -ne $hashes[$name]){throw 'Archive verification failed; originals retained'}
 }
 Assert-CampaignDeploymentIdle
 # Validate every target and hash before deleting only these duplicated files.
 foreach($name in $present){
  $source=[IO.Path]::GetFullPath((Join-Path $LogDirectory $name))
  if([IO.Path]::GetDirectoryName($source) -ne [IO.Path]::GetFullPath($LogDirectory).TrimEnd('\')){throw 'Archive target escaped log directory'}
  if((Get-FileHash -LiteralPath $source).Hash -ne $hashes[$name]){throw 'Evidence changed during archive; originals retained'}
 }
 [IO.File]::WriteAllText((Join-Path $archive 'archive-receipt.json'),([pscustomobject]@{utc=[DateTime]::UtcNow.ToString('O');source=$LogDirectory;sha256=$hashes}|ConvertTo-Json -Depth 3))
 foreach($name in $present){Remove-Item -LiteralPath (Join-Path $LogDirectory $name)}
}
$daysPath=Join-Path $LogDirectory 'AocAcceptance.days'
$dayStream=[IO.File]::Open($daysPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
try{$bytes=[Text.Encoding]::UTF8.GetBytes($TargetDays.ToString([Globalization.CultureInfo]::InvariantCulture));$dayStream.Write($bytes,0,$bytes.Length);$dayStream.Flush($true)}finally{$dayStream.Dispose()}
$marker=Join-Path $LogDirectory 'AocFrameworkDiagnostics.enabled'
if(!(Test-Path -LiteralPath $marker) -and !(Test-Path -LiteralPath (Join-Path $LogDirectory 'AocSupplyCapture.enabled'))){
 $stream=[IO.File]::Open($marker,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read);$stream.Dispose()
}
[pscustomobject]@{archive=$archive;status='PREPARED_NOT_RECORDING';targetCampaignDays=$TargetDays;next='Load the agreed existing save once; verify RECORDING before advancing. Capture closes itself at the day bound; no save, speed, pause or game control changes.'}
