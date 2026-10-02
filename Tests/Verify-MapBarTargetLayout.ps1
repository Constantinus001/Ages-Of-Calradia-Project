param()
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$mapBarPath = Join-Path $root 'GUI\Prefabs\Map\MapBar.xml'
$brushPath = Join-Path $root 'GUI\Brushes\AocMapBar.xml'
$rimAssetPath = Join-Path $root 'GUI\SpriteParts\aoc_mapbar\aoc_mapbar_medallion_ring_oval.png'
$crownAssetPath = Join-Path $root 'GUI\SpriteParts\aoc_mapbar\aoc_mapbar_season_crown_compact.png'
$previewCrownAssetPath = Join-Path $root 'Tools\MapBarPreviewHost\Module\GUI\SpriteParts\aoc_mapbar_preview\aoc_mapbar_season_crown_compact.png'
$widgetPath = Join-Path $root 'Builds\Approved560CalendarFixes\AocMapBarCustomWidgets.cs'
$sidecarPath = Join-Path $root 'Builds\Approved560CalendarFixes\Approved560CalendarFixesSubModule.cs'
$slimReleasePath = Join-Path $root 'Tests\New-SlimPlayerRelease.ps1'
[xml]$mapBar = Get-Content -LiteralPath $mapBarPath -Raw

$underlay = $mapBar.SelectSingleNode('//*[@Id="ExpandedMapBarUnderlay"]')
$center = $mapBar.SelectSingleNode('//*[@Id="CenterPanel"]')
if ($null -eq $underlay -or $underlay.SuggestedWidth -ne '885' -or $underlay.SuggestedHeight -ne '90' -or
    $null -eq $center -or $center.SuggestedWidth -ne '588' -or $center.SuggestedHeight -ne '97') {
    throw 'Production MapBar frame stack does not match the measured target proportions.'
}

# Bottom-aligned wings must reach the viewport edge without shifting their top
# or the top-aligned shortcut hitboxes. Check native and deployed 80% geometry.
foreach ($scale in @(1.0, 0.8)) {
    $bottom = [double]$underlay.PositionYOffset * $scale
    $top = $bottom - [double]$underlay.SuggestedHeight * $scale
    if ($underlay.VerticalAlignment -ne 'Bottom' -or $bottom -ne 0 -or
        [Math]::Abs($top - (-90 * $scale)) -gt 0.001) {
        throw 'MapBar wings must meet the screen bottom and preserve the top border and shortcut positions.'
    }
}

$expected = @(
    @{Id='SeasonCrown';X=214;Y=-60;W=162;H=93},
    @{Id='MapTimeDialButton';X=241;Y=-34;W=108;H=108},
    @{Id='CalendarDateText';X=104;Y=27;W=110;H=32},
    @{Id='CalendarYearText';X=104;Y=56;W=110;H=29},
    @{Id='CalendarClockText';X=242.5;Y=67.75;W=105;H=23},
    @{Id='PauseButton';X=351;Y=38;W=25;H=32},
    @{Id='PlayButton';X=386;Y=38;W=28;H=33},
    @{Id='FastForwardButton';X=424;Y=38;W=40;H=33},
    @{Id='FastForward4xButton';X=474;Y=38;W=53;H=33}
)
foreach ($item in $expected) {
    $node = $center.SelectSingleNode("Children/*[@Id='$($item.Id)']")
    if ($null -eq $node -or [double]$node.PositionXOffset -ne $item.X -or
        [double]$node.PositionYOffset -ne $item.Y -or
        [double]$node.SuggestedWidth -ne $item.W -or
        [double]$node.SuggestedHeight -ne $item.H) {
        throw "$($item.Id) does not match the target-layout geometry."
    }
}

$world = $underlay.SelectSingleNode('Children/ButtonWidget[@Id="WorldCalendarButton"]')
if ($null -eq $world -or $world.SuggestedWidth -ne '47' -or $world.SuggestedHeight -ne '43' -or
    $world.HorizontalAlignment -ne 'Right' -or $world.PositionXOffset -ne '-108' -or
    $world.PositionYOffset -ne '33') {
    throw 'World Calendar button is not in the target right-hand compartment.'
}

$dayNight = $center.SelectSingleNode('Children/ButtonWidget[@Id="MapTimeDialButton"]/Children/MapTimeImageBrushWidget[@Id="VanillaDayNightDisc"]')
$dialFrame = $center.SelectSingleNode('Children/ButtonWidget[@Id="MapTimeDialButton"]/Children/TextureWidget[@Id="MapTimeDialFrame"]')
$assembly = $center.SelectSingleNode('Children/TextureWidget[@Id="SeasonCrown"]')
$notchBacking = $mapBar.SelectSingleNode('//TextureWidget[@Id="NotchBackingPatch"]')
if ($null -eq $dayNight -or $dayNight.SuggestedWidth -ne '131.04' -or $dayNight.SuggestedHeight -ne '84' -or
    $dayNight.Brush -ne 'AocMapTimeImageLarge' -or $dayNight.DayTime -ne '@Time' -or
    $dayNight.CircularClipEnabled -ne 'true' -or $dayNight.CircularClipRadius -ne '42' -or
    $null -eq $dialFrame -or $dialFrame.SuggestedWidth -ne '108' -or $dialFrame.SuggestedHeight -ne '108' -or
    $dialFrame.TextureProviderName -ne 'AocMapBarMedallionRimTextureProvider' -or
    $null -eq $assembly -or $assembly.TextureProviderName -ne 'AocMapBarSeasonCrownTextureProvider' -or
    $null -eq $notchBacking -or $notchBacking.SuggestedWidth -ne '60' -or
    $notchBacking.SuggestedHeight -ne '66' -or $notchBacking.PositionYOffset -ne '-15' -or
    $notchBacking.TextureProviderName -ne 'AocMapBarNotchBackingTextureProvider') {
    throw 'Compact native animated dial or transparent segmented halo contract changed.'
}

$year = $center.SelectSingleNode('Children/*[@Id="CalendarYearText"]')
$clock = $center.SelectSingleNode('Children/*[@Id="CalendarClockText"]')
if ($year.GetAttribute('Brush.FontSize') -ne '24' -or $year.SourceText -ne '@SeasonYearLine' -or
    ([double]$year.PositionXOffset + [double]$year.SuggestedWidth) -gt [double]$clock.PositionXOffset) {
    throw 'Year and season must fit beside the clock without overlapping its text box.'
}
$dial = $center.SelectSingleNode('Children/*[@Id="MapTimeDialButton"]')
if (([double]$clock.PositionXOffset + [double]$clock.SuggestedWidth / 2) -ne
    ([double]$dial.PositionXOffset + [double]$dial.SuggestedWidth / 2) -or
    [double]$clock.PositionYOffset -lt ([double]$dial.PositionYOffset + [double]$dial.SuggestedHeight - 6.25) -or
    ([double]$clock.PositionYOffset + [double]$clock.SuggestedHeight) -gt [double]$center.SuggestedHeight) {
    throw 'Clock must be centered below the sundial and fit within the bar.'
}
$pause = $center.SelectSingleNode('Children/*[@Id="PauseButton"]')
if (([double]$dial.PositionXOffset + [double]$dial.SuggestedWidth) -gt [double]$pause.PositionXOffset) {
    throw 'Native dial hitbox overlaps the pause control.'
}

foreach ($rejected in @('SeasonBandOverlay','MedallionRingOverlay','SeasonPointerRotor','CalendarSeasonYearText')) {
    if ($null -ne $mapBar.SelectSingleNode("//*[@Id='$rejected']")) { throw "$rejected belongs to a rejected MapBar layout." }
}

[xml]$brushes = Get-Content -LiteralPath $brushPath -Raw
$dialLayers = @($brushes.SelectNodes('/Brushes/Brush[@Name="AocMapTimeImageLarge"]/Layers/BrushLayer'))
if ($dialLayers.Count -ne 2 -or
    @($dialLayers | Where-Object { $_.Sprite -eq 'MapBar\mapbar_center_circle_daynight' -and $_.OverridenWidth -eq '295.68' -and $_.OverridenHeight -eq '84' }).Count -ne 2 -or
    $dialLayers[1].XOffset -ne '25.2') {
    throw 'Native animated day/night brush geometry changed.'
}

Add-Type -AssemblyName System.Drawing
if ((Get-FileHash -LiteralPath $crownAssetPath -Algorithm SHA256).Hash -ne
    (Get-FileHash -LiteralPath $previewCrownAssetPath -Algorithm SHA256).Hash) {
    throw 'Production and preview segmented halos must remain byte-identical generator outputs.'
}
$crownBitmap = [Drawing.Bitmap]::new($crownAssetPath)
$rimBitmap = [Drawing.Bitmap]::new($rimAssetPath)
try {
    if ($crownBitmap.Width -ne 108 -or $crownBitmap.Height -ne 62 -or
        $crownBitmap.GetPixel(0, 0).A -ne 0 -or $crownBitmap.GetPixel(54, 40).A -ne 0 -or
        $crownBitmap.GetPixel(54, 2).A -eq 0) {
        throw 'Segmented halo dimensions or transparent day/night opening changed.'
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
                $pixel = $crownBitmap.GetPixel($x, $y)
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
                $pixel = $crownBitmap.GetPixel($x, $y)
                if ($pixel.A -gt 240 -and [Math]::Abs($pixel.R - $icon.R) -le 25 -and
                    [Math]::Abs($pixel.G - $icon.G) -le 25 -and [Math]::Abs($pixel.B - $icon.B) -le 25) { $iconPixels++ }
            }
        }
        if ($iconPixels -lt 4) { throw "$($icon.Name) pictogram is missing or in the wrong seasonal panel." }
    }
    if ($rimBitmap.Width -ne 176 -or $rimBitmap.Height -ne 176 -or
        $rimBitmap.GetPixel(0, 0).A -ne 0 -or $rimBitmap.GetPixel(88, 88).A -ne 0 -or
        $rimBitmap.GetPixel(175, 175).A -ne 0 -or $rimBitmap.GetPixel(88, 4).A -eq 0) {
        throw 'Circular rim must retain a transparent exterior and center with an opaque metal ring.'
    }
}
finally { $crownBitmap.Dispose(); $rimBitmap.Dispose() }

$provider = Get-Content -LiteralPath $widgetPath -Raw
foreach ($token in @('AocMapBarSeasonCrownTextureProvider : AocMapBarTextureProviderBase',
        'AocMapBarNotchBackingTextureProvider : AocMapBarTextureProviderBase',
        'AocMapBarMedallionRimTextureProvider : AocMapBarTextureProviderBase',
        'ReadRgba', 'aoc_mapbar_season_crown_compact.png', 'aoc_mapbar_notch_backing.png', 'aoc_mapbar_medallion_ring_oval.png',
        'ReleaseAfterNumberOfFrames(1)')) {
    if (-not $provider.Contains($token)) { throw "Production season assembly provider is missing: $token" }
}
$sidecar = Get-Content -LiteralPath $sidecarPath -Raw
foreach ($token in @('class CompactMapClockTextWidget : TextWidget',
        'NormalizeForVerification', 'class MapYearTextWidget : TextWidget',
        'OnBeforeInitialModuleScreenSetAsRoot()',
        'TextureProviderFactory.RefreshProviderTypes()',
        'MapBar texture-provider registration failed safely')) {
    if (-not $sidecar.Contains($token)) { throw "Production compact text contract is missing: $token" }
}
$slimRelease = Get-Content -LiteralPath $slimReleasePath -Raw
foreach ($token in @('AocMapBarSeasonCrownTextureProvider',
        'AocMapBarMedallionRimTextureProvider',
        'aoc_mapbar_season_crown_compact.png',
        'aoc_mapbar_medallion_ring_oval.png',
        '$approvedFixesSearchText',
        '[string][char]0',
        'Run Tests\Verify-Release.ps1 first.')) {
    if (-not $slimRelease.Contains($token)) { throw "Slim release MapBar preflight is missing: $token" }
}

Write-Host 'Production MapBar target-layout verification passed: measured frame stack, vanilla animated dial and controls, compact text, season assembly, and W placement.'
