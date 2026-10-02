param([Parameter(Mandatory = $true)][string]$LogDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# Read-only report aggregation. Never execute log content or infer unobserved gameplay success.
$directory = (Resolve-Path -LiteralPath $LogDirectory).Path
$samples = [Collections.Generic.List[object]]::new()
$checks = @('recovery', 'concurrent_reservations', 'clan_contract', 'closed_peace', 'payment_receipt',
    'operation_identity', 'siege_camp', 'capture_owner', 'cleanup_residue', 'battle_leaders', 'raid_record',
    'ui_clicks', 'mission_play', 'save_roundtrip', 'read_errors')
foreach ($file in @(Get-ChildItem -LiteralPath $directory -Recurse -File -Filter 'InternalWar_*.txt' | Sort-Object FullName)) {
    $lines = @(Get-Content -LiteralPath $file.FullName -Encoding UTF8)
    $build = 'unknown-build'
    foreach ($line in $lines) { if ($line -match '^module.build_id=(.+)$') { $build = $Matches[1]; break } }
    $incomplete = $build -eq 'unknown-build' -or -not ($lines -contains 'snapshot.complete=True')
    foreach ($line in $lines) {
        if ($line -match '^CHECK ([^|]+)\|(OBSERVED_OK|REVIEW|NOT_EXERCISED)\|(.*)$') {
            $id = $Matches[1]; $status = $Matches[2]; $detail = $Matches[3]
            $check = ($id -split '\.')[-1]
            if ($checks -notcontains $check) { $incomplete = $true; continue }
            $samples.Add([pscustomobject]@{ Build=$build; Check=$check; Status=$status; Evidence=($file.Name + ': ' + $id + ': ' + $detail) })
        } elseif ($line.StartsWith('CHECK ')) { $incomplete = $true }
    }
    $samples.Add([pscustomobject]@{Build=$build;Check='read_errors';Status=$(if($incomplete){'REVIEW'}else{'OBSERVED_OK'});Evidence=$file.Name})
}
$builds = @($samples | Select-Object -ExpandProperty Build -Unique)
if ($builds.Count -eq 0) { $builds = @('no-reports') }
foreach ($build in $builds) {
    foreach ($check in $checks) {
        $rows = @($samples | Where-Object { $_.Build -eq $build -and $_.Check -eq $check })
        $reviews = @($rows | Where-Object Status -eq 'REVIEW')
        $observed = @($rows | Where-Object Status -eq 'OBSERVED_OK')
        $status = if ($reviews.Count -gt 0) { 'REVIEW' } elseif ($observed.Count -gt 0) { 'OBSERVED_OK' } else { 'NOT_EXERCISED' }
        $evidence = if ($reviews.Count -gt 0) { $reviews[-1].Evidence } elseif ($observed.Count -gt 0) { $observed[-1].Evidence } else { 'No confirming observation; not a pass.' }
        [pscustomobject]@{Build=$build;Check=$check;Status=$status;ObservedSamples=$observed.Count;ReviewSamples=$reviews.Count;Evidence=$evidence}
    }
}
