$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$rows = @(& (Join-Path $PSScriptRoot '..\Summarize-InternalWarDiagnostics.ps1') -LogDirectory (Join-Path $PSScriptRoot 'DiagnosticFixtures'))
function Require([bool]$value,[string]$reason) {if(-not $value){throw $reason}}
Require ($rows.Count -eq 30) 'Every build must include every check, including unexercised cases.'
$capture = $rows | Where-Object { $_.Build -eq 'fixture-build-a' -and $_.Check -eq 'capture_owner' }
Require ($capture.Status -eq 'REVIEW' -and $capture.ReviewSamples -eq 1 -and $capture.ObservedSamples -eq 1) 'Later success erased earlier review evidence.'
Require ($capture.Evidence -like '*owner mismatch*') 'Review evidence lost.'
Require (($rows | Where-Object { $_.Build -eq 'fixture-build-b' -and $_.Check -eq 'capture_owner' }).Status -eq 'OBSERVED_OK') 'Builds contaminated each other.'
Require (($rows | Where-Object { $_.Build -eq 'fixture-build-b' -and $_.Check -eq 'read_errors' }).Status -eq 'REVIEW') 'Incomplete/malformed report was hidden.'
Require (($rows | Where-Object { $_.Build -eq 'fixture-build-a' -and $_.Check -eq 'payment_receipt' }).Status -eq 'OBSERVED_OK') 'Actual receipt observation was ignored.'
Require (($rows | Where-Object { $_.Build -eq 'fixture-build-a' -and $_.Check -eq 'ui_clicks' }).Status -eq 'NOT_EXERCISED') 'Missing UI evidence became a pass.'
Write-Host 'Diagnostic summary: 7 assertions passed; fixed synthetic reports, no native calls.'
