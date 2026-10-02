param([Parameter(Mandatory=$true)][string]$DiagnosticsDirectory)
$ErrorActionPreference='Stop'
# Evidence inventory, NOT a balance/compatibility acceptance gate. Stream bounded summaries.
$requirements=[ordered]@{
    'P0 trustworthy evidence'=@('SESSION_START','SESSION_END','STATE_COVERAGE','OBSERVER_HEALTH','TRANSFER_RECONCILIATION','CLAN_SETTLEMENT')
    'P1 recipe-aware cadence'=@('RECIPE_DEFINITION','RECIPE_SPEED','RECIPE_PROGRESS','RECIPE_CADENCE','RECIPE_CYCLE','RECIPE_GATE','WAREHOUSE_ESTIMATE')
    'P1 food-input dependencies'=@('RESOURCE_FLOW','VILLAGE_PRODUCTION','VILLAGE_STATE','MARKET_STOCK','MARKET_SHORTAGE','MARKET_DEMAND','WAREHOUSE_STOCK','FOOD_EXPLANATION','FOOD_STOCK')
    'P2 cash attribution'=@('FINAL_FINANCE_RESULT','CLAN_CREDIT_CHECK','TRANSFER_RECONCILIATION','EXTERNAL_CASH_FLOW','BUDGET_FLOW','CLAN_BALANCE','KINGDOM_BALANCE','FINANCE_COMPONENT','WAGE_ASSESSMENT')
    'P3 weak workshops'=@('WORKSHOP_STATE','WORKSHOP_GOLD','WORKSHOP_ELIGIBILITY','WORKSHOP_TRANSITION','RECIPE_GATE')
    'P4 treaty reversals'=@('TREATY_STATE')
    'Acceptance and comparison'=@('RUN_IDENTITY','MODEL_IDENTITY','SAVE_RESULT','ECONOMY_PACING','NATIVE_SCOPE_TIMING','OBSERVER_HEALTH')
}
$counts=@{};$issues=New-Object 'System.Collections.Generic.List[string]'
$branches=@{zeroSettlement=$false;cycleSuccess=$false;cycleFailure=$false;gateReject=$false;treatyObserved=$false;saveSuccess=$false;rebellion=$false;bankruptcy=$false}
$files=@(Get-ChildItem -LiteralPath $DiagnosticsDirectory -Filter 'AocEconomyDaily-*.tsv')
foreach($file in $files){
    $start=0;$end=0
    Import-Csv -LiteralPath $file.FullName -Delimiter "`t" | ForEach-Object {
        $r=$_
        foreach($field in @('day','count','net','positive','negative','first','last')){
            $number=0.0
            if(-not [double]::TryParse($r.$field,[Globalization.NumberStyles]::Float,[Globalization.CultureInfo]::InvariantCulture,[ref]$number) -or [double]::IsNaN($number) -or [double]::IsInfinity($number)){throw "Malformed $field in $($file.Name)"}
        }
        if(-not $r.kind -or [double]$r.count -lt 1 -or [double]$r.count -ne [Math]::Floor([double]$r.count)){throw 'Invalid evidence count/kind'}
        if([double]$r.positive -lt 0 -or [double]$r.negative -gt 0 -or [Math]::Abs([double]$r.net-([double]$r.positive+[double]$r.negative)) -gt 0.00001){throw 'Inconsistent aggregate'}
        $counts[$r.kind]+=[long]$r.count
        if($r.kind -eq 'SESSION_START'){$start++}
        if($r.kind -eq 'SESSION_END'){$end++}
        if($r.kind -eq 'UNEXPLAINED_DELTA'){$issues.Add('Unexplained account delta')}
        if($r.kind -eq 'STATE_COVERAGE' -and $r.lastDetail -notmatch 'complete=True'){$issues.Add('Incomplete state snapshot')}
        if($r.kind -in @('TRANSFER_RECONCILIATION','CLAN_CREDIT_CHECK') -and ([double]$r.positive -ne 0 -or [double]$r.negative -ne 0)){$issues.Add("Review residual: $($r.kind) $($r.owner); inspect detailed trace, clamping and nested events")}
        if($r.kind -eq 'CLAN_SETTLEMENT' -and [double]$r.count -gt 0 -and [double]$r.positive -eq 0 -and [double]$r.negative -eq 0){$branches.zeroSettlement=$true}
        if($r.kind -eq 'RECIPE_CYCLE'){
            if([double]$r.positive -gt 0){$branches.cycleSuccess=$true}
            if([double]$r.positive -lt [double]$r.count){$branches.cycleFailure=$true}
        }
        if($r.kind -eq 'RECIPE_GATE' -and [double]$r.positive -lt [double]$r.count){$branches.gateReject=$true}
        if($r.kind -eq 'TREATY_STATE' -and $r.lastDetail -match 'treatyState=observed'){$branches.treatyObserved=$true}
        if($r.kind -eq 'SAVE_RESULT' -and $r.metric -eq 'success'){$branches.saveSuccess=$true}
        if($r.kind -eq 'WORKSHOP_ELIGIBILITY' -and $r.metric -eq 'rebellion_blocks_production'){$branches.rebellion=$true}
        if($r.kind -eq 'WORKSHOP_TRANSITION' -and $r.source -match 'Bankruptcy'){$branches.bankruptcy=$true}
    }
    if($start -ne 1 -or $end -ne 1){$issues.Add("Incomplete/ambiguous session: $($file.Name)")}
}
$events=Join-Path $DiagnosticsDirectory 'AocSoakEvents.tsv'
$peace=$false;$war=$false
if(Test-Path -LiteralPath $events){
    if(Select-String -LiteralPath $events -Pattern "`t[A-Z_]*FAILURE`t" -Quiet){$issues.Add('Primary diagnostic failure log contains failures')}
    $peace= [bool](Select-String -LiteralPath $events -Pattern "`tPEACE_DIAGNOSTIC`t" -Quiet)
    $war= [bool](Select-String -LiteralPath $events -Pattern "`tWAR_DIAGNOSTIC`t" -Quiet)
}else{$issues.Add('Missing primary diagnostic failure/event log')}
$packages=foreach($entry in $requirements.GetEnumerator()){
    $items=foreach($kind in $entry.Value){[pscustomobject]@{kind=$kind;observations=[long]$counts[$kind];status=$(if($counts[$kind]){'OBSERVED_CATEGORY_ONLY'}else{'NOT_EXERCISED'})}}
    [pscustomobject]@{solution=$entry.Key;requirements=@($items)}
}
$branchReport=foreach($key in ($branches.Keys|Sort-Object)){[pscustomobject]@{branch=$key;status=$(if($branches[$key]){'OBSERVED_BRANCH_ONLY'}else{'NOT_EXERCISED'})}}
[pscustomobject]@{
    summaryFiles=$files.Count
    status=$(if(-not $files){'NOT_EXERCISED'}elseif($issues.Count){'REVIEW_REQUIRED'}else{'EVIDENCE_PRESENT_NOT_ACCEPTANCE'})
    solutions=@($packages);branches=@($branchReport);issues=@($issues|Select-Object -Unique)
    warEventObserved=$war;peaceEventObserved=$peace
    rawTransactionReconciliation='NOT_VERIFIED: run Verify-EconomyRuntimeCoverage.ps1 against raw ledger evidence separately'
    saveReload='NOT_VERIFIED: save result is not proof of successful reload and preserved state'
    controlledComparison='NOT_VERIFIED: pair identical starting-save/settings control and candidate runs; compare gross flows, recipe cycles, shortages, capital and pacing over the same campaign-day interval'
    limitations='Category counts are not proof of every item, recipe, caller or branch. Last-detail aggregation can lose intermediate context. Retain raw traces for contested attribution; do not add endpoint totals to leaf money totals.'
}
