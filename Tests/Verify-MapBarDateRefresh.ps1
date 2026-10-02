$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dll = Join-Path $root 'Builds\Approved560CalendarFixes\bin\Win64_Shipping_Client\AgesOfCalradia.Approved560CalendarFixes.dll'
$assembly = [Reflection.Assembly]::LoadFrom($dll)
$type = $assembly.GetType('AgesOfCalradia.Approved560CalendarFixes.MapBarDateRefreshFix', $true)
$method = $type.GetMethod('NeedsRefresh', [Reflection.BindingFlags]'NonPublic,Static')
foreach ($case in @(
    @([double]::NaN, 10.0, $true),
    @(10.0, 10.999999, $false),
    @(10.0, 11.0, $true),
    @(11.0, 10.0, $true),
    @(10.0, 40.0, $true),
    @(10.0, [double]::NaN, $false),
    @(10.0, [double]::PositiveInfinity, $false)
)) {
    $actual = $method.Invoke($null, @([double]$case[0], [double]$case[1]))
    if ($actual -ne $case[2]) { throw "Date refresh gate failed: previous=$($case[0]), current=$($case[1])." }
}
Write-Host 'MapBar date refresh gate passed: first display, same-day suppression, midnight, reload backwards, skipped days, invalid values.'
