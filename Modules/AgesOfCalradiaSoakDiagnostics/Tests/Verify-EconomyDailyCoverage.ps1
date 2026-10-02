param([Parameter(Mandatory=$true)][string]$DiagnosticsDirectory)
$ErrorActionPreference='Stop'
$files=@(Get-ChildItem -LiteralPath $DiagnosticsDirectory -Filter 'AocEconomyDaily-*.tsv')
if(-not $files){throw 'NOT_EXERCISED: no daily flow-summary files'}
$kinds=@{}
foreach($file in $files){
    $start=$false;$end=$false
    Import-Csv -LiteralPath $file.FullName -Delimiter "`t" | ForEach-Object {
        $row=$_
        if(-not $row.kind){throw 'Malformed daily summary'}
        foreach($field in @('day','count','net','positive','negative','first','last')){
            $number=0.0
            if(-not [double]::TryParse($row.$field,[Globalization.NumberStyles]::Float,[Globalization.CultureInfo]::InvariantCulture,[ref]$number) -or [double]::IsNaN($number) -or [double]::IsInfinity($number)){throw "Invalid summary numeric field: $field"}
        }
        if([double]$row.count -lt 1 -or [double]$row.positive -lt 0 -or [double]$row.negative -gt 0){throw 'Invalid aggregate counts/signs'}
        if([Math]::Abs([double]$row.net-([double]$row.positive+[double]$row.negative)) -gt 0.00001){throw 'Gross/net summary inconsistency'}
        if($row.kind -eq 'SESSION_START'){$start=$true}
        if($row.kind -eq 'SESSION_END'){$end=$true}
        if($row.kind -eq 'UNEXPLAINED_DELTA'){throw 'Daily observer reconciliation gap; investigate before acceptance'}
        if($row.kind -eq 'STATE_COVERAGE' -and $row.lastDetail -notmatch 'complete=True'){throw 'Incomplete state snapshot'}
        if(-not $kinds.ContainsKey($row.kind)){$kinds[$row.kind]=0L}
        $kinds[$row.kind]+=[long]$row.count
    }
    if(-not $start -or -not $end){throw "Incomplete daily session: $($file.Name)"}
}
$missing=@('STATE_COVERAGE','MARKET_STOCK','RECIPE_DEFINITION','RECIPE_CYCLE','RECIPE_PROGRESS','RECIPE_SPEED','VILLAGE_PRODUCTION','CLAN_SETTLEMENT','WALLET','ECONOMY_PACING')|Where-Object{-not $kinds.ContainsKey($_)}
if($missing){throw ('NOT_EXERCISED: '+($missing -join ', '))}
$events=Join-Path $DiagnosticsDirectory 'AocSoakEvents.tsv'
if(-not (Test-Path -LiteralPath $events)){throw 'Missing primary failure log'}
if(Select-String -LiteralPath $events -Pattern "`t[A-Z_]*FAILURE`t" -Quiet){throw 'Diagnostic failures invalidate coverage'}
'PASS: daily aggregate categories and closed evidence present. NOT a transaction-ledger, balance, or all-branch acceptance verdict.'
$kinds.GetEnumerator()|Sort-Object Name|Format-Table Name,Value
