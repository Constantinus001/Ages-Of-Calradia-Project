param([string]$Root=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path)
$ErrorActionPreference='Stop'
# Rejected combinations must fail before building, snapshotting, scanning or deployment.
$gate=Join-Path $Root 'Tests\Verify-Release.ps1'
$cases=@(
 @('-EconomySidecarsOnly'), @('-DeployEconomySidecars'), @('-AllowDirtySource'),
 @('-SkipSecurityScan'), @('-SkipInstalledBaseline'), @('-IncludeStrategicProvinceDiagnostics'),
 @('-ReleaseArchive','not-an-archive.zip'), @('-CloudVerdictHoldMinutes','1')
)
foreach($case in $cases){
 # Windows PowerShell wraps redirected native stderr as ErrorRecords. These
 # expected child failures must be inspected rather than terminate the fixture.
 $previousPreference=$ErrorActionPreference
 try {
  $ErrorActionPreference='Continue'
  $output=& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $gate -CampaignSystemsCandidateOnly @case 2>&1
  $exitCode=$LASTEXITCODE
 } finally { $ErrorActionPreference=$previousPreference }
 if($exitCode -eq 0){throw "Unsafe candidate combination accepted: $case"}
 if(($output -join "`n") -notmatch 'Framework candidate (gate does not accept|requires at least ten)'){
  throw "Candidate failed for an unexpected reason: $output"
 }
}
'PASS: eight invalid framework release combinations rejected before side effects.'
