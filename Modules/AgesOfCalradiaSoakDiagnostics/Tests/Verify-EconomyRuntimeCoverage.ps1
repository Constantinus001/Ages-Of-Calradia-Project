param(
    [Parameter(Mandatory=$true)][string]$DiagnosticsDirectory,
    [string]$DiagnosticsAssemblyPath=(Join-Path (Split-Path -Parent $PSScriptRoot) 'bin\Win64_Shipping_Client\AgesOfCalradia.SoakDiagnostics.dll')
)
$ErrorActionPreference='Stop'
$files=@(Get-ChildItem -LiteralPath $DiagnosticsDirectory -Filter 'AocEconomy-*.tsv')
if(-not $files){throw 'NOT_EXERCISED: no transaction ledgers. Snapshot-only runs do not satisfy economic coverage.'}
$counts=@{};$open=@{};$cashRoots=New-Object 'System.Collections.Generic.HashSet[string]';$settlementRoots=New-Object 'System.Collections.Generic.HashSet[string]'
$parents=@{};$componentRoots=New-Object 'System.Collections.Generic.HashSet[string]'
$zeroSettlements=New-Object 'System.Collections.Generic.HashSet[string]';$zeroResults=New-Object 'System.Collections.Generic.HashSet[string]'
$modulePath=(Resolve-Path -LiteralPath $DiagnosticsAssemblyPath).Path
$expectedMvid=[Reflection.Assembly]::LoadFrom($modulePath).ManifestModule.ModuleVersionId.ToString()
$sessions=New-Object 'System.Collections.Generic.HashSet[string]'
foreach($file in $files){
    $started=$false;$ended=$false;$session=$null;$sequence=-1L
    Import-Csv -LiteralPath $file.FullName -Delimiter "`t" | ForEach-Object {
        $row=$_
        if(-not $row.session -or -not $row.kind){throw "Malformed ledger row in $($file.Name)"}
        if($ended){throw 'Rows after SESSION_END'}
        if(-not $started -and $row.kind -ne 'SESSION_START'){throw 'Ledger must begin with SESSION_START'}
        if($session -and $row.session -ne $session){throw 'Mixed sessions in ledger'}
        $nextSequence=0L
        if(-not [long]::TryParse($row.sequence,[ref]$nextSequence) -or $nextSequence -le $sequence){throw 'Invalid ledger sequence'}
        $sequence=$nextSequence
        foreach($field in @('day','before','after','delta')){
            $number=0.0
            if(-not [double]::TryParse($row.$field,[Globalization.NumberStyles]::Float,[Globalization.CultureInfo]::InvariantCulture,[ref]$number) -or [double]::IsNaN($number) -or [double]::IsInfinity($number)){throw "Invalid numeric ledger field: $field"}
        }
        $before=[double]::Parse($row.before,[Globalization.CultureInfo]::InvariantCulture)
        $after=[double]::Parse($row.after,[Globalization.CultureInfo]::InvariantCulture)
        $delta=[double]::Parse($row.delta,[Globalization.CultureInfo]::InvariantCulture)
        if([Math]::Abs(($after-$before)-$delta) -gt 0.000001){throw 'Ledger delta does not match balances'}
        if($row.kind -eq 'SESSION_START'){
            if($started -or -not $sessions.Add($row.session)){throw 'Duplicate ledger session'}
            if($row.source -ne $expectedMvid){throw 'Runtime evidence is from a different diagnostics build; repeat short validation.'}
            $started=$true;$session=$row.session
        }
        if($row.kind -eq 'SESSION_END'){$ended=$true}
        $key=$row.session+':'+$row.transaction
        if($row.kind -eq 'BEGIN'){
            if($row.transaction -notmatch '^[1-9][0-9]*$' -or $parents.ContainsKey($key)){throw 'Invalid or duplicate transaction'}
            $parent=$row.session+':'+$row.parent
            if($row.parent -ne '0' -and -not $open.ContainsKey($parent)){throw 'Missing open parent transaction'}
            $open[$key]=$true;$parents[$key]=$parent
        }
        elseif($row.transaction -ne '0' -and -not $open.ContainsKey($key)){throw 'Row outside open transaction'}
        if($row.kind -eq 'END'){if(-not $open.ContainsKey($key)){throw 'END without BEGIN'};$open.Remove($key)}
        if($row.kind -eq 'UNEXPLAINED_DELTA'){throw "Incomplete accounting: $($row.owner) $($row.metric) residual=$($row.delta)"}
        if($row.detail -match 'nativeException=(?!none)'){throw 'Native exception recorded'}
        if($row.kind -notin @('BEGIN','END','COVERAGE','BALANCE','ROSTER_BIND')){
            if(-not $counts.ContainsKey($row.kind)){$counts[$row.kind]=0}
            if($delta -ne 0 -or $row.kind -in @('WORKSHOP_FLOW','FOOD_EXPLANATION','CLAN_SETTLEMENT')){$counts[$row.kind]++}
        }
        $root=$key
        while($parents.ContainsKey($root) -and $parents[$root] -notmatch ':0$'){$root=$parents[$root]}
        if($row.kind -eq 'HERO_GOLD' -and $delta -ne 0){$cashRoots.Add($root)|Out-Null}
        if($row.kind -eq 'CLAN_SETTLEMENT'){$settlementRoots.Add($root)|Out-Null}
        if($row.kind -eq 'CLAN_SETTLEMENT' -and $delta -eq 0){$zeroSettlements.Add($root)|Out-Null}
        if($row.kind -eq 'FINANCE_RESULT' -and $row.source -match '\.CalculateClanGoldChange$' -and $after -eq 0){$zeroResults.Add($root)|Out-Null}
        if($row.kind -in @('WAGE_ASSESSMENT','TRIBUTE_COMPONENT') -and $delta -ne 0){$componentRoots.Add($root)|Out-Null}
    }
    if(-not $started -or -not $ended){throw "Incomplete or unhealthy ledger session: $($file.Name)"}
}
if($open.Count){throw 'Incomplete transaction scopes: run must end and flush before coverage review'}
$missing=@('HERO_GOLD','SETTLEMENT_GOLD','WORKSHOP_GOLD','FOOD_STOCK','INVENTORY','WAGE_ASSESSMENT','TRIBUTE_COMPONENT','WORKSHOP_FLOW','FOOD_EXPLANATION','FINANCE_COMPONENT','CLAN_SETTLEMENT')|Where-Object{-not $counts.ContainsKey($_) -or $counts[$_] -eq 0}
if($missing){throw ('NOT_EXERCISED: '+($missing -join ', '))}
foreach($root in $componentRoots){
    $zeroNet=$zeroSettlements.Contains($root) -and $zeroResults.Contains($root)
    if(-not $settlementRoots.Contains($root) -or (-not $cashRoots.Contains($root) -and -not $zeroNet)){throw "Unsettled finance evidence in $root; component is not proof of payment"}
}
$events=Join-Path $DiagnosticsDirectory 'AocSoakEvents.tsv'
if(-not (Test-Path -LiteralPath $events)){throw 'Missing primary failure log'}
if(Select-String -LiteralPath $events -Pattern "`t[A-Z_]*FAILURE`t" -Quiet){throw 'Diagnostic failures invalidate runtime coverage'}
Write-Output 'PASS: observed runtime categories, closed scopes, linked finance settlement evidence and no unexplained snapshot differences. This is coverage, not a balance verdict.'
$counts.GetEnumerator()|Sort-Object Name|Format-Table Name,Value -AutoSize
