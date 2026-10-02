param([Parameter(Mandatory=$true)][string]$VerificationReceipt)
$ErrorActionPreference='Stop'
$check=Join-Path $PSScriptRoot 'Get-CaptureReadiness.ps1'
$result=& $check -VerificationReceipt $VerificationReceipt
if(@($result.blockers|Where-Object {$_ -like '*candidate mismatch*' -or $_ -like '*package/receipt mismatch*'}).Count){throw 'Verified deployed build rejected'}
if($result.builds.Count -ne 3){throw 'Missing verified build identities'}
$fixture=Join-Path $env:TEMP ('aoc-build-receipt-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture|Out-Null
foreach($fault in @('status','hash','missing-check','duplicate-artifact')){
 $copy=Get-Content -LiteralPath $VerificationReceipt -Raw|ConvertFrom-Json
 switch($fault){
  'status' {$copy.status='OFFLINE_CHECKS_FAILED'}
  'hash' {$copy.artifacts[0].sha256=('0'*64)}
  'missing-check' {$copy.checks=@($copy.checks|Where-Object name -ne 'diagnostic-hardening')}
  'duplicate-artifact' {$copy.artifacts+=@($copy.artifacts[0])}
 }
 $path=Join-Path $fixture ($fault+'.json')
 $copy|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $path -Encoding UTF8
 $rejected=$false
 try{& $check -VerificationReceipt $path|Out-Null}catch{$rejected=$true}
 if(!$rejected){throw ('Invalid receipt accepted: '+$fault)}
}
$rejected=$false
try{& $check -VerificationReceipt $VerificationReceipt -CandidateRun 'not-used'|Out-Null}catch{$rejected=$true}
if(!$rejected){throw 'Ambiguous build authorities accepted'}
'PASS: deployed receipt matched; failed, changed, incomplete, duplicate and ambiguous authorities rejected. No game or capture controls used.'
