param(
 [string]$DiagnosticsAssemblyPath=(Join-Path (Split-Path -Parent $PSScriptRoot) 'bin\Win64_Shipping_Client\AgesOfCalradia.SoakDiagnostics.dll'),
 [string]$CapturePath
)
$ErrorActionPreference='Stop'
$assembly=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $DiagnosticsAssemblyPath).Path)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$summary=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.FrameworkRuntimeSummary',$true)
$root=Join-Path $env:TEMP ('aoc-framework-summary-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root|Out-Null
$summary.GetMethod('Begin',$flags).Invoke($null,@([string]$root,[string]'fixture-session'))|Out-Null
foreach($row in @(
 @('WORKSHOP_CYCLE','succeeded',''),@('WORKSHOP_CYCLE','failed_see_gates',''),
 @('PROCUREMENT_TRANSFER','committed',''),@('PROCUREMENT_MOVEMENT','arrival',''),@('PROCUREMENT_MOVEMENT','consumption',''),
 @('REWARD_BEGIN','RecoverGoldFromRemainingShipsAfterDistribution','playerClan=True'),
 @('REWARD_END','RecoverGoldFromRemainingShipsAfterDistribution','originalRan=False; playerClan=True'),
 @('REWARD_END','RecoverGoldFromRemainingShipsAfterDistribution','originalRan=True; playerClan=True'),@('REWARD_VALUATION','native_ship_value','ship=1')
)){$summary.GetMethod('Observe',$flags).Invoke($null,@([string]$row[0],[string]$row[1],[string]$row[2]))|Out-Null}
$summary.GetMethod('Close',$flags).Invoke($null,@([string]'fixture_completed',[double]10,[double]12,[long]123,[long]7,[bool]$true))|Out-Null
$report=Get-Content -Raw -LiteralPath (Join-Path $root 'AocFramework-current.summary.json')|ConvertFrom-Json
if($report.status -ne 'CLOSED_OBSERVED_ONLY' -or $report.campaignDays -ne 2 -or $report.workshops.succeeded -ne 1 -or $report.workshops.failed -ne 1){throw 'Runtime summary lost workshop closure evidence'}
if($report.procurement.committed -ne 1 -or $report.procurement.arrivals -ne 1 -or $report.procurement.consumptions -ne 1){throw 'Runtime summary lost procurement evidence'}
if($report.naval.playerScopesObserved -ne 1 -or $report.naval.playerPenaltyStatus -ne 'NATIVE_RESULT_OBSERVED_FORMULA_NOT_INDEPENDENTLY_CERTIFIED' -or $report.naval.shipValuationStatus -ne 'OBSERVED_REQUIRES_OFFLINE_RECONCILIATION'){throw 'Runtime summary misreported naval coverage'}
if($report.workshops.cycles -ne ($report.workshops.succeeded+$report.workshops.failed)){throw 'Cycle outcomes do not account for every observed fixture cycle'}
Write-Output 'PASS: exported workshop/procurement/naval counters, including native failed_see_gates outcomes.'
if($CapturePath){
 # Replay only cycle records. Never rewrite the capture or its original summary.
 $records=@(& rg -a '\tWORKSHOP_CYCLE\t' -- $CapturePath)
 if($LASTEXITCODE -ne 0){throw 'Capture replay could not read workshop cycles'}
 $summary.GetMethod('Begin',$flags).Invoke($null,@([string]$root,[string]'offline-cycle-replay'))|Out-Null
 [long]$expectedSucceeded=0
 [long]$expectedFailed=0
 $session=$null
 foreach($record in $records){
  $fields=$record.Split([char]9)
  if($fields.Count -ne 12 -or $fields[4] -ne 'WORKSHOP_CYCLE'){throw 'Malformed cycle record'}
  if($null -eq $session){$session=$fields[1]}
  if($session -ne $fields[1]){throw 'Mixed sessions are not supported by this replay'}
  # Native bool outcome is an independent oracle for the metric-name counter.
  if($fields[10] -eq '1'){$expectedSucceeded++}
  elseif($fields[10] -eq '0'){$expectedFailed++}
  else{throw 'Unexpected native cycle outcome'}
  $summary.GetMethod('Observe',$flags).Invoke($null,@([string]$fields[4],[string]$fields[8],[string]$fields[11]))|Out-Null
 }
 $summary.GetMethod('Close',$flags).Invoke($null,@([string]'offline_cycle_counter_replay_only',[double]0,[double]0,[long]0,[long]$records.Count,[bool]$false))|Out-Null
 $replay=Get-Content -Raw -LiteralPath (Join-Path $root 'AocFramework-current.summary.json')|ConvertFrom-Json
 if($replay.workshops.cycles -ne $records.Count -or $replay.workshops.succeeded -ne $expectedSucceeded -or $replay.workshops.failed -ne $expectedFailed){throw 'Captured native outcomes disagree with runtime summary'}
 Write-Output "PASS: captured session $session; cycles=$($records.Count); succeeded=$expectedSucceeded; unsuccessful=$expectedFailed. Counter verification only."
}
