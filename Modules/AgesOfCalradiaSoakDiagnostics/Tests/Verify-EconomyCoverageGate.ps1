param([string]$DiagnosticsAssemblyPath=(Join-Path (Split-Path -Parent $PSScriptRoot) 'bin\Win64_Shipping_Client\AgesOfCalradia.SoakDiagnostics.dll'))
$ErrorActionPreference='Stop'
$gate=Join-Path $PSScriptRoot 'Verify-EconomyRuntimeCoverage.ps1'
$modulePath=(Resolve-Path -LiteralPath $DiagnosticsAssemblyPath).Path
$mvid=[Reflection.Assembly]::LoadFrom($modulePath).ManifestModule.ModuleVersionId.ToString()
$testRoot=Join-Path $env:TEMP ('aoc-economy-gate-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot|Out-Null
$ledgerPath=Join-Path $testRoot 'AocEconomy-fixture.tsv'
$eventPath=Join-Path $testRoot 'AocSoakEvents.tsv'
# Generated test artifacts only; never write synthetic evidence to game logs.
[IO.File]::WriteAllText($eventPath,"synthetic test fixture`n")
$rows=New-Object 'System.Collections.Generic.List[object]'
function Row($kind,$transaction,$parent,$source,$delta){
    $rows.Add([pscustomobject]@{utc='2026-09-09T00:00:00Z';session='synthetic';sequence=$rows.Count;day=1;kind=$kind;transaction=$transaction;parent=$parent;owner='fixture';metric='fixture';before=0;after=$delta;delta=$delta;source=$source;detail=''})
}
Row SESSION_START 0 0 $mvid 0
Row BEGIN 1 0 root 0
foreach($kind in @('HERO_GOLD','SETTLEMENT_GOLD','WORKSHOP_GOLD','FOOD_STOCK','INVENTORY','WAGE_ASSESSMENT','TRIBUTE_COMPONENT','WORKSHOP_FLOW','FOOD_EXPLANATION','FINANCE_COMPONENT','CLAN_SETTLEMENT')){Row $kind 1 0 root 1}
Row END 1 0 root 0
Row SESSION_END 0 0 end 0
function Save-Rows($data){$data|Export-Csv -LiteralPath $ledgerPath -Delimiter "`t" -NoTypeInformation}
function Expect-Rejection($data,$pattern){
    Save-Rows $data
    $rejected=$false
    try{& $gate -DiagnosticsDirectory $testRoot -DiagnosticsAssemblyPath $modulePath|Out-Null}catch{if($_.Exception.Message -match $pattern){$rejected=$true}else{throw}}
    if(-not $rejected){throw "Coverage gate accepted invalid fixture: $pattern"}
}
Save-Rows $rows
& $gate -DiagnosticsDirectory $testRoot -DiagnosticsAssemblyPath $modulePath|Out-Null
# A completed zero-net settlement is an observed scope, not an unexercised payment.
$settlement=$rows|Where-Object kind -eq CLAN_SETTLEMENT
$settlement.after=0;$settlement.delta=0
Save-Rows $rows
& $gate -DiagnosticsDirectory $testRoot -DiagnosticsAssemblyPath $modulePath|Out-Null
$settlement.after=1;$settlement.delta=1
Expect-Rejection @($rows|Where-Object kind -ne 'WAGE_ASSESSMENT') 'NOT_EXERCISED'
Expect-Rejection @($rows|Where-Object kind -ne 'SESSION_END') 'Incomplete or unhealthy'
Expect-Rejection @($rows|Where-Object kind -ne 'END') 'Incomplete transaction scopes'
Expect-Rejection @($rows|Where-Object kind -ne 'HERO_GOLD') 'NOT_EXERCISED|Unsettled finance'
$rows[2].kind='UNEXPLAINED_DELTA'
Expect-Rejection $rows 'Incomplete accounting'
$rows[2].kind='HERO_GOLD'
$rows[2].delta='NaN'
Expect-Rejection $rows 'Invalid numeric'
$rows[2].delta=2
Expect-Rejection $rows 'delta does not match'
$rows[2].delta=1
$rows[2].session='different-session'
Expect-Rejection $rows 'Mixed sessions'
$rows[2].session='synthetic'
$rows[2].sequence=0
Expect-Rejection $rows 'Invalid ledger sequence'
$rows[2].sequence=2
$rows[1].parent=1
Expect-Rejection $rows 'Missing open parent'
$rows[1].parent=0
$rows[2].transaction=123
Expect-Rejection $rows 'outside open transaction'
$rows[2].transaction=1
Row BALANCE 0 0 after-end 0
Expect-Rejection $rows 'Rows after SESSION_END'
$rows.RemoveAt($rows.Count-1)
$rows[0].source='obsolete-build'
Expect-Rejection $rows 'different diagnostics build'
$rows[0].source=$mvid
$rows.RemoveAt($rows.Count-1)
Row BEGIN 2 0 second-root 0
Row WAGE_ASSESSMENT 2 0 wage 10
Row FINANCE_RESULT 2 0 'fixture.CalculateClanGoldChange' 0
Row CLAN_SETTLEMENT 2 0 second-root 0
Row END 2 0 second-root 0
Row SESSION_END 0 0 end 0
Save-Rows $rows
& $gate -DiagnosticsDirectory $testRoot -DiagnosticsAssemblyPath $modulePath|Out-Null
$zeroResult=$rows|Where-Object {$_.kind -eq 'FINANCE_RESULT'}
$zeroResult.after=1;$zeroResult.delta=1
Expect-Rejection $rows 'Unsettled finance'
Write-Output "PASS: coverage gate rejects missing coverage/cash, incomplete scopes, reconciliation gaps, stale builds, corrupt numbers, invalid deltas, mixed sessions, bad sequence/parents and trailing rows. Fixture: $testRoot"
Write-Output 'PASS: zero-net root without a cash mutation requires both settlement completion and observed zero finance result.'
