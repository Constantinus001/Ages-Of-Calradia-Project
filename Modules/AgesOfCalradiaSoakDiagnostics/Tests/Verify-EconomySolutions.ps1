$ErrorActionPreference='Stop'
$root=Join-Path $env:TEMP ('aoc-solutions-fixture-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root|Out-Null
$reporter=Join-Path $PSScriptRoot 'Summarize-EconomySolutions.ps1'
$report=& $reporter -DiagnosticsDirectory $root
if($report.status -ne 'NOT_EXERCISED' -or $report.solutions.Count -ne 7){throw 'Missing evidence did not report all seven packages'}
$rows=New-Object 'System.Collections.Generic.List[object]'
function Add([string]$kind,[double]$positive=0,[double]$negative=0){
    $rows.Add([pscustomobject]@{day=1;kind=$kind;owner='fixture';metric='fixture';source='synthetic';count=1;net=($positive+$negative);positive=$positive;negative=$negative;first=0;last=0;lastDetail='synthetic only'})
}
Add SESSION_START
Add TRANSFER_RECONCILIATION 5 -5
Add CLAN_SETTLEMENT
$path=Join-Path $root 'AocEconomyDaily-fixture.tsv'
$rows|Export-Csv -LiteralPath $path -Delimiter "`t" -NoTypeInformation
$report=& $reporter -DiagnosticsDirectory $root
if($report.status -ne 'REVIEW_REQUIRED' -or -not ($report.issues -match 'Review residual') -or -not ($report.issues -match 'Incomplete/ambiguous')){throw 'Offsetting residuals or missing session end escaped review'}
if(($report.branches|Where-Object branch -eq zeroSettlement).status -ne 'OBSERVED_BRANCH_ONLY'){throw 'Genuine zero settlement dropped'}
if(($report.branches|Where-Object branch -eq treatyObserved).status -ne 'NOT_EXERCISED'){throw 'Treaty evidence invented'}
$rows[0].count=0
$rows|Export-Csv -LiteralPath $path -Delimiter "`t" -NoTypeInformation
$rejected=$false
try{& $reporter -DiagnosticsDirectory $root|Out-Null}catch{$rejected=$true}
if(-not $rejected){throw 'Zero-count evidence accepted'}
'PASS: seven-package report; absent evidence, partial sessions, offsetting residuals, zero settlements and invalid counts. Synthetic fixtures only.'
