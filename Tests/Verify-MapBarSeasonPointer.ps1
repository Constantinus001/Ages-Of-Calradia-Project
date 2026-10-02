param(
    [string]$ModuleRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$BannerlordDir = 'C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord',
    [string]$SidecarPath = (Join-Path $PSScriptRoot '..\Builds\Approved560CalendarFixes\bin\Win64_Shipping_Client\AgesOfCalradia.Approved560CalendarFixes.dll')
)
$ErrorActionPreference = 'Stop'
$gameBin = Join-Path $BannerlordDir 'bin\Win64_Shipping_Client'
foreach ($name in @('TaleWorlds.Library', 'TaleWorlds.DotNet', 'TaleWorlds.Engine',
    'TaleWorlds.InputSystem', 'TaleWorlds.GauntletUI', 'TaleWorlds.Engine.GauntletUI',
    'TaleWorlds.TwoDimension', 'TaleWorlds.ScreenSystem', 'TaleWorlds.Core',
    'TaleWorlds.Localization', 'TaleWorlds.ObjectSystem', 'TaleWorlds.SaveSystem',
    'TaleWorlds.CampaignSystem', 'TaleWorlds.MountAndBlade')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $gameBin ($name + '.dll')))
}
$core = [Reflection.Assembly]::LoadFrom((Join-Path $ModuleRoot 'bin\Win64_Shipping_Client\AgesOfCalradia.dll'))
$calendar = $core.GetType('TwelveMonthCalendar.CalendarTimeMath', $true)
$sidecar = [Reflection.Assembly]::LoadFrom($SidecarPath)
$pointer = $sidecar.GetType('AgesOfCalradia.Approved560CalendarFixes.AocMapBarSeasonPointerWidget', $true)
$flags = [Reflection.BindingFlags]'Static,NonPublic'
$project = $pointer.GetMethod('TryProject', $flags)
if ($null -eq $project) { throw 'Season pointer projection helper is missing.' }
[TaleWorlds.CampaignSystem.CampaignTime].GetField('TimeTicksPerDay', $flags).SetValue($null, [long]1000000)

function Invoke-Calendar([string]$Name, [object[]]$Arguments) {
    $types = [Type[]]@($Arguments | ForEach-Object { $_.GetType() })
    $method = $calendar.GetMethod($Name, $flags, $null, $types, $null)
    if ($null -eq $method) { throw "Calendar method missing: $Name" }
    return $method.Invoke($null, $Arguments)
}
function Assert-Equal($Expected, $Actual, [string]$Label) {
    if ($Expected -ne $Actual) { throw "$Label expected $Expected; got $Actual." }
}
function Invoke-Projection([int]$Season, [int]$Day, [int]$Length) {
    $arguments = [object[]]@($Season, $Day, $Length, [single]0, [int]0)
    $valid = [bool]$project.Invoke($null, $arguments)
    return @{Valid=$valid;Angle=[double]$arguments[3];Remaining=[int]$arguments[4]}
}

# Calendar boundaries come from the approved assembly; Gregorian fixture dates
# independently establish the expected season and remaining complete date span.
foreach ($fixture in @(
    @{Date='1084-12-21';Season=3;End='1085-03-21'},
    @{Date='1085-01-01';Season=3;End='1085-03-21'},
    @{Date='1085-03-20';Season=3;End='1085-03-21'},
    @{Date='1085-03-21';Season=0;End='1085-06-21'},
    @{Date='1085-06-21';Season=1;End='1085-09-21'},
    @{Date='1085-09-21';Season=2;End='1085-12-21'},
    @{Date='1083-12-21';Season=3;End='1084-03-21'},
    @{Date='1084-02-29';Season=3;End='1084-03-21'},
    @{Date='1084-03-20';Season=3;End='1084-03-21'},
    @{Date='1084-03-21';Season=0;End='1084-06-21'})) {
    $date = [DateTime]::ParseExact($fixture.Date, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
    $end = [DateTime]::ParseExact($fixture.End, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
    $absolute = [double](Invoke-Calendar 'DaysBeforeYear' @([int]$date.Year)) + $date.DayOfYear - 1 + 0.5
    $time = Invoke-Calendar 'FromCalendarAbsoluteDays' @($absolute)
    $season = [int](Invoke-Calendar 'GetSeason' @($time))
    $seasonYear = [int](Invoke-Calendar 'GetSeasonYear' @($time))
    $day = [int](Invoke-Calendar 'GetDayOfSeason' @($time))
    $length = [int](Invoke-Calendar 'GetSeasonLength' @($seasonYear, $season))
    Assert-Equal $fixture.Season $season "$($fixture.Date) season"
    $result = Invoke-Projection $season $day $length
    Assert-Equal $true $result.Valid "$($fixture.Date) pointer visibility"
    Assert-Equal ([int]($end - $date).TotalDays) $result.Remaining "$($fixture.Date) days remaining"
    $slot = ($season + 1) % 4
    $startAngle = [Math]::PI + $slot * [Math]::PI / 4
    if ($result.Angle -lt $startAngle - 0.00001 -or $result.Angle -ge $startAngle + [Math]::PI / 4) {
        throw "$($fixture.Date) pointer escaped its seasonal panel."
    }
}

$previous = 0.0
foreach ($season in @(3, 0, 1, 2)) {
    $start = Invoke-Projection $season 0 90
    $middle = Invoke-Projection $season 45 90
    $last = Invoke-Projection $season 89 90
    if (-not ($start.Angle -gt $previous -and $middle.Angle -gt $start.Angle -and $last.Angle -gt $middle.Angle)) {
        throw 'Season arrow must advance left-to-right through winter, spring, summer, autumn.'
    }
    Assert-Equal 1 $last.Remaining 'Last season day tooltip count'
    $previous = $last.Angle
}
foreach ($invalid in @(@(-1,0,90), @(4,0,90), @(0,-1,90), @(0,90,90), @(0,0,0), @(0,0,-1))) {
    Assert-Equal $false (Invoke-Projection $invalid[0] $invalid[1] $invalid[2]).Valid 'Invalid calendar hides pointer'
}
$formatHint = $pointer.GetMethod('FormatHint', $flags)
foreach ($seasonName in @('Winter','Spring','Summer','Autumn')) {
    Assert-Equal ($seasonName + ': 1 day remaining') ($formatHint.Invoke($null, @($seasonName, [int]1))) 'Singular season hint'
    Assert-Equal ($seasonName + ': 72 days remaining') ($formatHint.Invoke($null, @($seasonName, [int]72))) 'Plural season hint'
}
foreach ($layoutPath in @('GUI/Prefabs/Map/MapBar.xml', 'Tools/PoliticalBorderSceneStudio/MapBarPreview/AocMapBarCenterCandidate.xml')) {
    [xml]$layout = Get-Content -Raw (Join-Path $ModuleRoot $layoutPath)
    $crown = $layout.SelectSingleNode('//*[@Id="SeasonCrown"]')
    Assert-Equal 'false' $crown.DoNotAcceptEvents 'Season arch accepts hover'
    $arrow = $layout.SelectSingleNode('//*[@Id="SeasonPointer"]')
    Assert-Equal $crown.ParentNode $arrow.ParentNode 'Arch and arrow share hover lookup scope'
    $dial = $layout.SelectSingleNode('//*[@Id="MapTimeDialButton"]')
    Assert-Equal $crown.ParentNode $dial.ParentNode 'Entire dial shares hover lookup scope'
    Assert-Equal 0 @($dial.SelectNodes('.//HintWidget')).Count 'No competing time hint on season dial'
    Assert-Equal 'ExecuteResetCamera' $dial.GetAttribute('Command.Click') 'Native dial click remains available'
}
Write-Host 'PASS: season progression, calendar boundaries, leap winter, remaining days, invalid-state hiding, hint text, and arch hover target.'
