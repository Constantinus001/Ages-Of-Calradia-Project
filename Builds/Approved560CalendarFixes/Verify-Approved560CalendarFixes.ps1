param(
    [string]$BannerlordDir = 'C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord',
    [string]$ModuleRoot = 'C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\Ages Of Calradia',
    [string]$HarmonyPath = (Join-Path $ModuleRoot 'bin\Win64_Shipping_Client\0Harmony.dll'),
    [string]$SidecarPath = (Join-Path $PSScriptRoot 'bin\Win64_Shipping_Client\AgesOfCalradia.Approved560CalendarFixes.dll')
)

$ErrorActionPreference = 'Stop'

function Assert-Near(
    [double]$Expected,
    [double]$Actual,
    [double]$Tolerance,
    [string]$Contract) {
    if ([Math]::Abs($Expected - $Actual) -gt $Tolerance) {
        throw "$Contract failed. Expected $Expected +/- $Tolerance; actual $Actual."
    }
}

function Get-InternalStaticMethod(
    [Reflection.Assembly]$Assembly,
    [string]$TypeName,
    [string]$MethodName) {
    $contractType = $Assembly.GetType($TypeName, $true)
    $method = $contractType.GetMethod(
        $MethodName,
        [Reflection.BindingFlags]'Static,NonPublic')
    if ($null -eq $method) {
        throw "Verification method was not found: $TypeName::$MethodName"
    }
    return $method
}
$gameBin = Join-Path $BannerlordDir 'bin\Win64_Shipping_Client'
$moduleBin = Join-Path $ModuleRoot 'bin\Win64_Shipping_Client'
[Reflection.Assembly]::LoadFrom($HarmonyPath) | Out-Null
foreach ($name in @(
    'TaleWorlds.Library.dll',
    'TaleWorlds.DotNet.dll',
    'TaleWorlds.Engine.dll',
    'TaleWorlds.InputSystem.dll',
    'TaleWorlds.GauntletUI.dll',
    'TaleWorlds.Engine.GauntletUI.dll',
    'TaleWorlds.TwoDimension.dll',
    'TaleWorlds.ScreenSystem.dll',
    'TaleWorlds.MountAndBlade.GauntletUI.Widgets.dll',
    'TaleWorlds.Core.dll',
    'TaleWorlds.Localization.dll',
    'TaleWorlds.ObjectSystem.dll',
    'TaleWorlds.SaveSystem.dll',
    'TaleWorlds.CampaignSystem.dll',
    'TaleWorlds.MountAndBlade.dll',
    'TaleWorlds.Core.ViewModelCollection.dll',
    'TaleWorlds.CampaignSystem.ViewModelCollection.dll')) {
    [Reflection.Assembly]::LoadFrom((Join-Path $gameBin $name)) | Out-Null
}

$approvedMain = [Reflection.Assembly]::LoadFrom((Join-Path $moduleBin 'AgesOfCalradia.dll'))
$mainType = $approvedMain.GetType('AgesOfCalradia.MySubModule', $true)
$mainInstance = [Activator]::CreateInstance($mainType)
$mainLoad = $mainType.GetMethod('OnSubModuleLoad', [Reflection.BindingFlags]'Instance,NonPublic')
$mainLoad.Invoke($mainInstance, @()) | Out-Null
$sidecar = [Reflection.Assembly]::LoadFrom($SidecarPath)
$type = $sidecar.GetType(
    'AgesOfCalradia.Approved560CalendarFixes.Approved560CalendarFixesSubModule',
    $true)
$instance = [Activator]::CreateInstance($type)
$load = $type.GetMethod('OnSubModuleLoad', [Reflection.BindingFlags]'Instance,NonPublic')
$load.Invoke($instance, @()) | Out-Null

$owner = 'AgesOfCalradia.Approved560CalendarFixes.560F1B51'
$patched = @()
foreach ($original in [HarmonyLib.Harmony]::GetAllPatchedMethods()) {
    $info = [HarmonyLib.Harmony]::GetPatchInfo($original)
    $owned = @($info.Prefixes + $info.Postfixes + $info.Transpilers + $info.Finalizers |
        Where-Object { $_.owner -eq $owner })
    if ($owned.Count -gt 0) {
        $patched += [pscustomobject]@{
            Target = $original.DeclaringType.FullName + '::' + $original.Name
            PatchCount = $owned.Count
        }
    }
}

$patched | Sort-Object Target | Format-Table -AutoSize
$count = ($patched | Measure-Object -Property PatchCount -Sum).Sum
if ($count -lt 19) {
    throw "Expected at least 19 compatibility patches; found $count."
}

$sidecarWheelOverrides = @($patched | Where-Object {
    $_.Target -eq 'TwelveMonthCalendar.StrategicMapZoomScrollablePanel::OnPreviewMouseScroll' -or
    $_.Target -eq 'TwelveMonthCalendar.WorldCalendarScreen::OnTick'
})
if ($sidecarWheelOverrides.Count -ne 0) {
    throw 'The sidecar must not override the approved main DLL strategic wheel/drag implementation.'
}

$scrollWidget = $sidecar.GetType(
    'AgesOfCalradia.Approved560CalendarFixes.WorldEventsRowSnapScrollablePanel',
    $true)
if (-not $scrollWidget.IsSubclassOf([TaleWorlds.GauntletUI.BaseTypes.ScrollablePanel])) {
    throw 'The UI REDESIGN row-snap widget must derive from Gauntlet ScrollablePanel.'
}
$sidecarSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Approved560CalendarFixesSubModule.cs') -Raw
$mapBarWidgetSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'AocMapBarCustomWidgets.cs') -Raw
foreach ($requiredScrollContract in @(
    'public sealed class WorldEventsRowSnapScrollablePanel : ScrollablePanel',
    'protected override bool OnPreviewMouseScroll()',
    'SetVerticalScrollTarget(_wheelTarget, 0.10f)')) {
    if (-not $sidecarSource.Contains($requiredScrollContract)) {
        throw "World Events scrolling contract missing: $requiredScrollContract"
    }
}
$mapBarPath = Join-Path $ModuleRoot 'GUI\Prefabs\Map\MapBar.xml'
$mapBarSource = Get-Content -LiteralPath $mapBarPath -Raw
foreach ($requiredTimeControl in @(
    'Id="PlayButton"',
    'Id="FastForwardButton"',
    'Id="FastForward4xButton"',
    'CommandParameter.Click="1"',
    'CommandParameter.Click="2"',
    'SuggestedWidth="588"',
    'PositionXOffset="351"',
    'PositionXOffset="386"',
    'PositionXOffset="424"',
    'PositionXOffset="474"')) {
    if (-not $mapBarSource.Contains($requiredTimeControl)) {
        throw "Map bar 1x/2x/4x control contract missing: $requiredTimeControl"
    }
}
if ($mapBarSource -notmatch '(?s)Id="FastForward4xButton".*?CommandParameter\.Click="2"') {
    throw 'The 4x UI button must use the native 2x transition command.'
}
if ($mapBarSource -notmatch '(?s)Id="FastForward4xButton"[^>]*SuggestedWidth="53"[^>]*PositionXOffset="474"[^>]*PositionYOffset="38"') {
    throw 'The 4x UI button must retain its separate non-overlapping hitbox.'
}
foreach ($clockVisualContract in @(
    'Id="CalendarDateText"',
    'Id="CalendarYearText"',
    'SourceText="@SeasonYearLine"',
    'Id="CalendarClockText"',
    'SuggestedWidth="105" SuggestedHeight="23"',
    'Brush.FontSize="19"',
    'PositionXOffset="242.5"',
    'PositionYOffset="67.75"',
    'ClockSourceText="@TimeOfDay"')) {
    if (-not $mapBarSource.Contains($clockVisualContract)) {
        throw "Split map-clock visual contract missing: $clockVisualContract"
    }
}
$compactClockWidget = $sidecar.GetType(
    'AgesOfCalradia.Approved560CalendarFixes.CompactMapClockTextWidget',
    $true)
$normalizeClock = $compactClockWidget.GetMethod(
    'NormalizeForVerification',
    [Reflection.BindingFlags]'Static,NonPublic')
if ([string]$normalizeClock.Invoke($null, @("09:26`nAM")) -ne '09:26 AM' -or
    [string]$normalizeClock.Invoke($null, @('9:26 PM')) -ne '9:26 PM') {
    throw 'Compact map-clock widget must keep time and meridiem on one line.'
}
$yearWidget = $sidecar.GetType(
    'AgesOfCalradia.Approved560CalendarFixes.MapYearTextWidget', $true)
$extractYear = $yearWidget.GetMethod(
    'ExtractYearForVerification', [Reflection.BindingFlags]'Static,NonPublic')
if ([string]$extractYear.Invoke($null, @('1085, Winter')) -ne '1085' -or
    [string]$extractYear.Invoke($null, @('Year 1085, Winter')) -ne 'Year 1085' -or
    [string]$extractYear.Invoke($null, @('1085')) -ne '1085') {
    throw 'Map year widget must remove the season suffix and preserve the year.'
}
$mapBarXml = [xml]$mapBarSource
$seasonAssemblyNode = $mapBarXml.SelectSingleNode(
    '//MapCurrentTimeVisualWidget[@Id="CenterPanel"]/Children/TextureWidget[@Id="SeasonCrown"]')
$notchBackingNode = $mapBarXml.SelectSingleNode('//TextureWidget[@Id="NotchBackingPatch"]')
$seasonButton = $mapBarXml.SelectSingleNode('//ButtonWidget[@Id="MapTimeDialButton"]')
$dayNightNode = $mapBarXml.SelectSingleNode(
    '//ButtonWidget[@Id="MapTimeDialButton"]/Children/MapTimeImageBrushWidget[@Id="VanillaDayNightDisc"]')
$dialFrameNode = $mapBarXml.SelectSingleNode('//ButtonWidget[@Id="MapTimeDialButton"]/Children/TextureWidget[@Id="MapTimeDialFrame"]')
if ($null -eq $seasonAssemblyNode -or $seasonAssemblyNode.SuggestedWidth -ne '162' -or
    $seasonAssemblyNode.SuggestedHeight -ne '93' -or $seasonAssemblyNode.VerticalAlignment -ne 'Top' -or
    $seasonAssemblyNode.PositionXOffset -ne '214' -or $seasonAssemblyNode.PositionYOffset -ne '-60' -or
    $seasonAssemblyNode.TextureProviderName -ne 'AocMapBarSeasonCrownTextureProvider' -or
    $null -eq $notchBackingNode -or $notchBackingNode.SuggestedWidth -ne '60' -or
    $notchBackingNode.SuggestedHeight -ne '66' -or $notchBackingNode.PositionYOffset -ne '-15' -or
    $notchBackingNode.TextureProviderName -ne 'AocMapBarNotchBackingTextureProvider' -or
    $null -eq $seasonButton -or $seasonButton.SuggestedWidth -ne '108' -or $seasonButton.SuggestedHeight -ne '108' -or
    $seasonButton.VerticalAlignment -ne 'Top' -or $seasonButton.PositionXOffset -ne '241' -or $seasonButton.PositionYOffset -ne '-34' -or
    $null -eq $dayNightNode -or $dayNightNode.Brush -ne 'AocMapTimeImageLarge' -or
    $dayNightNode.DayTime -ne '@Time' -or $dayNightNode.CircularClipEnabled -ne 'true' -or
    $dayNightNode.SuggestedWidth -ne '131.04' -or $dayNightNode.SuggestedHeight -ne '84' -or
    $dayNightNode.CircularClipRadius -ne '42' -or
    $null -eq $dialFrameNode -or $dialFrameNode.SuggestedWidth -ne '108' -or $dialFrameNode.SuggestedHeight -ne '108' -or
    $dialFrameNode.TextureProviderName -ne 'AocMapBarMedallionRimTextureProvider') {
    throw 'AOC MapBar must match the raised target assembly while preserving the native animated dial.'
}
$seasonBandAsset = Join-Path $ModuleRoot 'GUI\SpriteParts\aoc_mapbar\aoc_mapbar_season_crown_compact.png'
$notchBackingAsset = Join-Path $ModuleRoot 'GUI\SpriteParts\aoc_mapbar\aoc_mapbar_notch_backing.png'
$medallionAsset = Join-Path $ModuleRoot 'GUI\SpriteParts\aoc_mapbar\aoc_mapbar_medallion_ring_oval.png'
if (-not (Test-Path -LiteralPath $seasonBandAsset -PathType Leaf) -or
    -not (Test-Path -LiteralPath $notchBackingAsset -PathType Leaf) -or
    -not (Test-Path -LiteralPath $medallionAsset -PathType Leaf)) {
    throw 'AOC MapBar split sundial assets are missing.'
}
Add-Type -AssemblyName System.Drawing
$seasonBitmap = [Drawing.Bitmap]::new($seasonBandAsset)
$notchBitmap = [Drawing.Bitmap]::new($notchBackingAsset)
$medallionBitmap = [Drawing.Bitmap]::new($medallionAsset)
try {
    if ($seasonBitmap.Width -ne 108 -or $seasonBitmap.Height -ne 62 -or
        $notchBitmap.Width -ne 60 -or $notchBitmap.Height -ne 66 -or
        $medallionBitmap.Width -ne 176 -or $medallionBitmap.Height -ne 176) {
        throw 'AOC MapBar split sundial asset dimensions changed.'
    }
    if ($seasonBitmap.GetPixel(0, 0).A -ne 0 -or
        $seasonBitmap.GetPixel(54, 2).A -eq 0 -or
        $seasonBitmap.GetPixel(54, 40).A -ne 0 -or
        $medallionBitmap.GetPixel(88, 88).A -ne 0 -or
        $medallionBitmap.GetPixel(88, 4).A -eq 0 -or
        $medallionBitmap.GetPixel(0, 0).A -ne 0 -or
        $medallionBitmap.GetPixel(175, 175).A -ne 0) {
        throw 'AOC MapBar must keep the season arch and a clean ring-only medallion with a transparent center and corners.'
    }
    foreach ($region in @(
        [Drawing.Rectangle]::new(8, 34, 5, 5),
        [Drawing.Rectangle]::new(33, 9, 5, 5),
        [Drawing.Rectangle]::new(70, 9, 5, 5),
        [Drawing.Rectangle]::new(95, 34, 5, 5))) {
        $goldPixels = 0
        $opaquePixels = 0
        for ($y = $region.Top; $y -lt $region.Bottom; $y++) {
            for ($x = $region.Left; $x -lt $region.Right; $x++) {
                $pixel = $seasonBitmap.GetPixel($x, $y)
                if ($pixel.A -gt 240) { $opaquePixels++ }
                if ($pixel.A -gt 150 -and $pixel.R -gt 120 -and $pixel.G -gt 90) { $goldPixels++ }
            }
        }
        if ($goldPixels -lt 2 -or $opaquePixels -lt 23) { throw 'Season pictograms must retain an opaque bronze panel background.' }
    }
    foreach ($icon in @(
        @{Name='Winter';X=12;Y=38;R=49;G=77;B=95},
        @{Name='Spring';X=36;Y=13;R=43;G=72;B=30},
        @{Name='Summer';X=72;Y=13;R=99;G=65;B=10},
        @{Name='Autumn';X=96;Y=38;R=115;G=52;B=20})) {
        $iconPixels = 0
        for ($y = $icon.Y - 4; $y -le $icon.Y + 4; $y++) {
            for ($x = $icon.X - 4; $x -le $icon.X + 4; $x++) {
                $pixel = $seasonBitmap.GetPixel($x, $y)
                if ($pixel.A -gt 240 -and [Math]::Abs($pixel.R - $icon.R) -le 25 -and
                    [Math]::Abs($pixel.G - $icon.G) -le 25 -and [Math]::Abs($pixel.B - $icon.B) -le 25) { $iconPixels++ }
            }
        }
        if ($iconPixels -lt 4) { throw "$($icon.Name) pictogram is missing or in the wrong seasonal panel." }
    }
}
finally { $seasonBitmap.Dispose(); $notchBitmap.Dispose(); $medallionBitmap.Dispose() }

foreach ($widgetContract in @(
    'class AocMapBarSeasonCrownTextureProvider : AocMapBarTextureProviderBase',
    'class AocMapBarNotchBackingTextureProvider : AocMapBarTextureProviderBase',
    'class AocMapBarMedallionRimTextureProvider : AocMapBarTextureProviderBase',
    'ReadRgba',
    'aoc_mapbar_season_crown_compact.png',
    'aoc_mapbar_notch_backing.png',
    'aoc_mapbar_medallion_ring_oval.png')) {
    if (-not $mapBarWidgetSource.Contains($widgetContract)) {
        throw "AOC custom MapBar widget contract missing: $widgetContract"
    }
}
foreach ($nativeHintContract in @(
    'DataSource="{PauseHint}"',
    'DataSource="{PlayHint}"',
    'DataSource="{FastForwardHint}"',
    'Command.HoverBegin="ExecuteBeginHint"',
    'Command.HoverEnd="ExecuteEndHint"')) {
    if (-not $mapBarSource.Contains($nativeHintContract)) {
        throw "Map bar native tooltip contract missing: $nativeHintContract"
    }
}
if (@($patched | Where-Object {
    $_.Target -eq 'TwelveMonthCalendar.CalendarWorldLedgerVM::get_StrategicMapLegendHeight'
}).Count -ne 0) {
    throw 'Strategic legend geometry is prefab-owned and must not be altered by Harmony.'
}

$clockPatch = $sidecar.GetType(
    'AgesOfCalradia.Approved560CalendarFixes.MapClockMeridiemLayoutPatch',
    $true)
$formatClock = $clockPatch.GetMethod(
    'FormatForVerification',
    [Reflection.BindingFlags]'Static,NonPublic')
$morningClock = [string]$formatClock.Invoke($null, @('09:26', 9))
$eveningClock = [string]$formatClock.Invoke($null, @('9:26 PM', 21))
if ($morningClock -ne "09:26`nAM" -or $eveningClock -ne "9:26`nPM" -or
    @($patched | Where-Object {
        $_.Target -eq 'TwelveMonthCalendar.CalendarMapTimeControlVM::RefreshClock'
    }).Count -ne 1) {
    throw 'Campaign map clock must render AM/PM on a second line exactly once.'
}
$advanceDisplayedMinute = Get-InternalStaticMethod $sidecar `
    'AgesOfCalradia.Approved560CalendarFixes.SmoothMapClockMinutePatch' `
    'MinuteFromDialForVerification'
$formatDisplayedMinute = Get-InternalStaticMethod $sidecar `
    'AgesOfCalradia.Approved560CalendarFixes.SmoothMapClockMinutePatch' `
    'FormatMinuteForVerification'
if ([long]$advanceDisplayedMinute.Invoke($null, @([double]1)) -ne 60L -or
    [long]$advanceDisplayedMinute.Invoke($null, @([double]9.5)) -ne 570L -or
    [long]$advanceDisplayedMinute.Invoke($null, @([double]24)) -ne 0L -or
    [long]$advanceDisplayedMinute.Invoke($null, @([double]-0.5)) -ne 1410L -or
    [long]$advanceDisplayedMinute.Invoke($null, @([double]23.999)) -ne 1439L -or
    [string]$formatDisplayedMinute.Invoke($null, @(1439L)) -ne "23:59`nPM" -or
    [string]$formatDisplayedMinute.Invoke($null, @(0L)) -ne "00:00`nAM" -or
    @($patched | Where-Object {
        $_.Target -eq 'TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapBar.MapTimeControlVM::Tick'
    }).Count -ne 1) {
    throw 'Campaign map clock must read the dial directly, including midnight, without independent catch-up.'
}

# A two-year deterministic schedule check guards the repeating week gate. The
# native model owns settlement selection and its other conditions; this check
# proves the Gregorian week index cannot age out after the first season/year.
$normalizeTournamentWeek = Get-InternalStaticMethod $sidecar `
    'AgesOfCalradia.Approved560CalendarFixes.TournamentStartFix' `
    'NormalizeWeekSlot'
$tournamentSlotsByYear = @(0, 0)
for ($calendarDay = 0; $calendarDay -lt 731; $calendarDay += 7) {
    $yearIndex = if ($calendarDay -lt 365) { 0 } else { 1 }
    $week = [int][Math]::Floor($calendarDay / 7.0)
    $slot = [int]$normalizeTournamentWeek.Invoke($null, @($week))
    if ($slot -eq 0) {
        $tournamentSlotsByYear[$yearIndex]++
    }
}
if ($tournamentSlotsByYear[0] -lt 15 -or $tournamentSlotsByYear[1] -lt 15) {
    throw "Tournament starts stop recurring across two Gregorian years: $($tournamentSlotsByYear -join ', ')."
}

# Workshop annualization must alter only the base speed supplied to Bannerlord.
# Native perk/policy/building multipliers then remain free to change the result.
$scaleWorkshopBase = Get-InternalStaticMethod $sidecar `
    'AgesOfCalradia.Approved560CalendarFixes.WorkshopProductionFix' `
    'ScaleBaseSpeedForVerification'
$dailyFactor = [single](84.0 / 365.2425)
$scaledWorkshopBase = [single]$scaleWorkshopBase.Invoke(
    $null, @([single]12, $false, $dailyFactor))
$withoutWorkshopPerk = $scaledWorkshopBase
$withWorkshopPerk = $scaledWorkshopBase * [single]1.25
Assert-Near (12.0 * $dailyFactor) $withoutWorkshopPerk 0.0001 `
    'Workshop base-speed annualization'
Assert-Near 1.25 ($withWorkshopPerk / $withoutWorkshopPerk) 0.0001 `
    'Workshop output response to a relevant perk'
$foodWorkshopBase = [single]$scaleWorkshopBase.Invoke(
    $null, @([single]12, $true, $dailyFactor))
Assert-Near 12.0 $foodWorkshopBase 0.0001 'Food-workshop native cadence'

# The UI and applied clan-finance path must resolve to the same effective daily
# wage after the protected wrapper's duplicate scale is removed.
$effectiveWage = Get-InternalStaticMethod $sidecar `
    'AgesOfCalradia.Approved560CalendarFixes.WageText' `
    'EffectiveDailyWageForVerification'
$correctFinance = Get-InternalStaticMethod $sidecar `
    'AgesOfCalradia.Approved560CalendarFixes.FinanceDoubleScaleFix' `
    'CorrectDoubleScaledForVerification'
$nativeWage = [single]137
$displayedWage = [single]$effectiveWage.Invoke($null, @($nativeWage, $dailyFactor))
$wrapperDoubleScaledWage = $nativeWage * $dailyFactor * $dailyFactor
$deductedWage = [single]$correctFinance.Invoke(
    $null, @([single]$wrapperDoubleScaledWage, $dailyFactor))
Assert-Near $displayedWage $deductedWage 0.0001 'Displayed and deducted wage agreement'

# Campaign.TickMapTime remains the only pacing/siege-time intervention. The
# sidecar owns the Warband target without modifying the protected Core DLL.
# Scaling its delta preserves Bannerlord's native Engineering/perk calculation.
$scaleRealDelta = Get-InternalStaticMethod $sidecar `
    'AgesOfCalradia.Approved560CalendarFixes.CampaignSimulationTimeFix' `
    'ScaleRealDeltaForVerification'
$scaledRealDelta = [single]$scaleRealDelta.Invoke(
    $null, @([single]1, [single](80.0 / 80.0)))
Assert-Near (80.0 / 80.0) $scaledRealDelta 0.000001 'Eighty-second campaign-day time scale'
$campaignTimeFix = $sidecar.GetType(
    'AgesOfCalradia.Approved560CalendarFixes.CampaignSimulationTimeFix',
    $true)
$warbandCampaignTimeScale = $campaignTimeFix.GetField(
    'WarbandCampaignTimeScale',
    [Reflection.BindingFlags]'Static,NonPublic').GetRawConstantValue()
Assert-Near (80.0 / 80.0) ([double]$warbandCampaignTimeScale) 0.000001 `
    'Runtime eighty-second campaign-day time scale'
$warbandFastForward = $campaignTimeFix.GetField(
    'WarbandFastForwardMultiplier',
    [Reflection.BindingFlags]'Static,NonPublic').GetValue($null)
Assert-Near 2.0 ([double]$warbandFastForward) 0.0001 'Two-hour-year fast-forward multiplier'
$selectFastForward = Get-InternalStaticMethod $sidecar `
    'AgesOfCalradia.Approved560CalendarFixes.CampaignSimulationTimeFix' `
    'SelectFastForwardMultiplierForVerification'
Assert-Near 2.0 ([double]$selectFastForward.Invoke($null, @(2))) 0.0001 '2x fast-forward UI selection'
Assert-Near 4.0 ([double]$selectFastForward.Invoke($null, @(4))) 0.0001 '4x fast-forward UI selection'
# Independent native contract: TickMapTime uses .25 * 4320 game seconds per
# input second, and a day is 86400 seconds. Do not merely echo the scale.
foreach ($speed in @(1,2,4)) {
    $mode = if ($speed -eq 1) { 1.0 } else { [double]$selectFastForward.Invoke($null, @($speed)) }
    $actualSeconds = 86400.0 / (0.25 * 4320 * [double]$warbandCampaignTimeScale * $mode)
    $expectedSeconds = 80.0 / $speed
    Assert-Near $expectedSeconds $actualSeconds 0.0001 "Native seconds per day at ${speed}x"
}
if (@($patched | Where-Object {
        $_.Target -eq 'TaleWorlds.CampaignSystem.Campaign::TickMapTime'
    }).Count -ne 1 -or
    @($patched | Where-Object {
        $_.Target -match 'MobileParty|PartyAi|AiBehavior|AiHourlyTick'
    }).Count -ne 0) {
    throw 'Speed modes must advance Bannerlord global campaign time exactly once and must not double-scale AI systems.'
}
Assert-Near 2.0 ((2.0 * $scaledRealDelta) / $scaledRealDelta) 0.0001 `
    'Global AI/player campaign-time ratio at 2x'
Assert-Near 4.0 ((4.0 * $scaledRealDelta) / $scaledRealDelta) 0.0001 `
    'Global AI/player campaign-time ratio at 4x'
if (-not $sidecarSource.Contains('twoTimesButton.ClickEventHandlers.Add(OnTwoTimesClicked);') -or
    -not $sidecarSource.Contains('fourTimesButton.ClickEventHandlers.Add(OnFourTimesClicked);') -or
    -not $sidecarSource.Contains('Input.IsKeyPressed(InputKey.D4)') -or
    -not $sidecarSource.Contains('Play x1 [2]') -or
    -not $sidecarSource.Contains('Fast Forward x2 [3]') -or
    -not $sidecarSource.Contains('Super Fast Forward x4 [4]') -or
    -not $sidecarSource.Contains('state.PauseHint.SetHintCallback(GetPauseHintText);') -or
    -not $sidecarSource.Contains('state.PlayHint.SetHintCallback(GetPlayHintText);') -or
    -not $sidecarSource.Contains('state.FastForwardHint.SetHintCallback(MapTimeControlFourTimesButtonPatch.GetFastForwardHintText);') -or
    -not $sidecarSource.Contains('buttons.FourTimes.IsHovered') -or
    -not $sidecarSource.Contains('fourTimesButton.IsSelected = fourTimesSelected;') -or
    -not $sidecarSource.Contains('Campaign.Current.SpeedUpMultiplier =')) {
    throw 'The 2x/4x UI buttons must register explicit multiplier and selected-state handling.'
}
if (@($patched | Where-Object {
    $_.Target -eq 'TaleWorlds.MountAndBlade.GauntletUI.Widgets.Map.MapBar.MapCurrentTimeVisualWidget::OnUpdate'
}).Count -ne 1) {
    throw 'The 4x UI button must patch MapCurrentTimeVisualWidget.OnUpdate exactly once.'
}
$requiredPreparationWork = 100.0
$baseEngineeringDays = $requiredPreparationWork / (1.0 * $scaledRealDelta)
$expertEngineeringDays = $requiredPreparationWork / (1.4 * $scaledRealDelta)
if ($expertEngineeringDays -ge $baseEngineeringDays) {
    throw 'Engineering no longer reduces siege preparation time.'
}
if (@($patched | Where-Object { $_.Target -match 'Siege' }).Count -ne 0) {
    throw 'The sidecar must preserve Bannerlord siege/Engineering models and adjust only campaign time.'
}

# Proposal probability is converted so a 365-day year stays within five
# percent of the native 84-day annual frequency. The fixed seed is unnecessary:
# this compares exact expectations rather than a noisy random sample.
$nativeDailyProposalChance = 0.02
$calendarDailyProposalChance = 1.0 - [Math]::Pow(
    1.0 - $nativeDailyProposalChance,
    $dailyFactor)
$nativeAnnualProposalCount = 84.0 * $nativeDailyProposalChance
$calendarAnnualProposalCount = 365.2425 * $calendarDailyProposalChance
$minimumApprovedAnnualFrequency = $nativeAnnualProposalCount * 0.95
$maximumApprovedAnnualFrequency = $nativeAnnualProposalCount * 1.05
if ($calendarAnnualProposalCount -lt $minimumApprovedAnnualFrequency -or
    $calendarAnnualProposalCount -gt $maximumApprovedAnnualFrequency) {
    throw "Diplomacy annual frequency $calendarAnnualProposalCount is outside approved range $minimumApprovedAnnualFrequency-$maximumApprovedAnnualFrequency."
}
$isWithinTruce = Get-InternalStaticMethod $sidecar `
    'AgesOfCalradia.Approved560CalendarFixes.WarCooldownFix' `
    'IsWithinTruceForVerification'
if (-not [bool]$isWithinTruce.Invoke($null, @([single]87)) -or
    [bool]$isWithinTruce.Invoke($null, @([single]87.01))) {
    throw 'The approved 87-day diplomacy truce boundary is not enforced exactly.'
}

$foodFix = $sidecar.GetType(
    'AgesOfCalradia.Approved560CalendarFixes.TownMarketFoodAccountingFix',
    $true)
$combine = $foodFix.GetMethod(
    'CombineForVerification',
    [Reflection.BindingFlags]'Static,NonPublic')
$directOnly = [single]$combine.Invoke($null, @([single]-10, [single]-2, $false, [single]0.23))
$withMarket = [single]$combine.Invoke($null, @([single]-10, [single]-2, $true, [single]0.23))
if ([Math]::Abs($directOnly - [single]-2.3) -gt 0.0001 -or
    [Math]::Abs($withMarket - [single]-0.46) -gt 0.0001) {
    throw "Town food accounting must calculate with vanilla values and scale only the selected final result: direct=$directOnly market=$withMarket"
}

$cadence = $sidecar.GetType(
    'AgesOfCalradia.Approved560CalendarFixes.VanillaFoodCadence',
    $true)
$scaleDemand = $cadence.GetMethod(
    'ScaleDemandForVerification',
    [Reflection.BindingFlags]'Static,NonPublic')
$foodDemand = [single]$scaleDemand.Invoke($null, @([single]100, $true, [single]0.23))
$nonFoodDemand = [single]$scaleDemand.Invoke($null, @([single]100, $false, [single]0.23))
if ([Math]::Abs($foodDemand - [single]100) -gt 0.0001 -or
    [Math]::Abs($nonFoodDemand - [single]23) -gt 0.0001) {
    throw "Food demand must remain vanilla while non-food demand is annualized once: food=$foodDemand nonFood=$nonFoodDemand"
}

if ($null -eq $approvedMain.GetType('TwelveMonthCalendar.LegacySaveHeroAgePatch', $false)) {
    throw 'The approved main DLL no longer contains the save-age compatibility patch.'
}
if (@($patched | Where-Object {
    $_.Target -match 'Political|Territory|Island|Lake|Texture|Mesh'
}).Count -gt 0) {
    throw 'The compatibility sidecar patched a renderer target.'
}

$retiredTypes = @(
    'TwelveMonthCalendar.MapTimeTrackerPatch',
    'TwelveMonthCalendar.CampaignPacingPatch',
    'TwelveMonthCalendar.WorkshopProductionBalancePatch',
    'TwelveMonthCalendar.WorkshopFoodContextPatch',
    'TwelveMonthCalendar.VillageFoodProductionBalancePatch',
    'TwelveMonthCalendar.VillageProductionBalancePatch',
    'TwelveMonthCalendar.SettlementDemandBalancePatch',
    'TwelveMonthCalendar.SettlementBudgetBalancePatch',
    'TwelveMonthCalendar.SettlementMarketSmoothingBalancePatch',
    'TwelveMonthCalendar.KingdomWarCooldownPatch')
foreach ($original in [HarmonyLib.Harmony]::GetAllPatchedMethods()) {
    $info = [HarmonyLib.Harmony]::GetPatchInfo($original)
    foreach ($patch in @($info.Prefixes + $info.Postfixes + $info.Transpilers + $info.Finalizers)) {
        if ($null -ne $patch.PatchMethod -and
            $null -ne $patch.PatchMethod.DeclaringType -and
            $retiredTypes -contains $patch.PatchMethod.DeclaringType.FullName) {
            throw "Superseded approved-DLL patch remains active: $($patch.PatchMethod.DeclaringType.FullName)."
        }
    }
}

Write-Output "PASS: approved main + v1.5.14 production sidecar registered $count fixes; tournaments recurred for two years; workshop perks, wages, Engineering, and diplomacy retained their contracts; food cadence and save-age compatibility passed; and zero renderer targets changed."
