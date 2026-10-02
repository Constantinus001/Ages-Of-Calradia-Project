param([string]$BannerlordRoot = 'C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$candidatePath = Join-Path $root 'Tools\PoliticalBorderSceneStudio\MapBarPreview\AocMapBarCenterCandidate.xml'
[xml]$candidate = Get-Content -LiteralPath $candidatePath -Raw
$panel = $candidate.SelectSingleNode('//*[@Id="MapBarPreviewPanel"]')
if ($null -eq $panel -or $panel.Name -ne 'MapCurrentTimeVisualWidget' -or
    $panel.SuggestedWidth -ne '802' -or $panel.SuggestedHeight -ne '145' -or
    $panel.VerticalAlignment -ne 'Bottom') {
    throw 'Preview must match the measured 802 x 145 target composition.'
}

$expected = @(
    @{Id='ExpandedMapBarFrame';X=-41;Y=55;W=885;H=88;Sprite='MapBar\mapbar_center_frame'},
    @{Id='NotchBackingPatch';X=372;Y=64;W=60;H=66;Sprite=$null},
    @{Id='CenterMapBarFrame';X=111;Y=46;W=588;H=97;Sprite='MapBar\mapbar_center_frame'},
    @{Id='SeasonCrown';X=321;Y=-14;W=162;H=93;Sprite=$null},
    @{Id='MapTimeDialButton';X=348;Y=12;W=108;H=108;Sprite=$null},
    @{Id='CalendarDateText';X=211;Y=73;W=110;H=32;Sprite=$null},
    @{Id='CalendarYearText';X=211;Y=102;W=110;H=29;Sprite=$null},
    @{Id='CalendarClockText';X=349.5;Y=113.75;W=105;H=23;Sprite=$null},
    @{Id='PauseButton';X=458;Y=84;W=25;H=32;Sprite=$null},
    @{Id='PlayButton';X=493;Y=83.5;W=28;H=33;Sprite=$null},
    @{Id='FastForwardButton';X=531;Y=83.5;W=40;H=33;Sprite=$null},
    @{Id='FastForward4xButton';X=581;Y=83.5;W=53;H=33;Sprite=$null},
    @{Id='WorldCalendarButton';X=689;Y=88;W=47;H=43;Sprite=$null}
)
foreach ($item in $expected) {
    $node = $candidate.SelectSingleNode("//*[@Id='$($item.Id)']")
    if ($null -eq $node) { throw "Missing target-layout element: $($item.Id)" }
    if ($node.IsVisible -eq 'false') { throw "$($item.Id) must remain visible in the diagnostic preview." }
    if ([double]$node.PositionXOffset -ne $item.X -or [double]$node.PositionYOffset -ne $item.Y -or
        [double]$node.SuggestedWidth -ne $item.W -or [double]$node.SuggestedHeight -ne $item.H) {
        throw "$($item.Id) no longer matches the measured target geometry."
    }
    if ($null -ne $item.Sprite -and $node.Sprite -ne $item.Sprite) { throw "$($item.Id) must use $($item.Sprite)." }
}

$ids = @($candidate.SelectNodes('//*[@Id]') | ForEach-Object Id)
if (@($ids | Group-Object | Where-Object Count -gt 1).Count -ne 0) { throw 'Target MapBar contains duplicate widget IDs.' }
foreach ($removed in @('SeasonBandOverlay','MedallionRingOverlay','SeasonPointerRotor','SeasonProgressPointer','CalendarSeasonYearText')) {
    if ($ids -contains $removed) { throw "$removed is from a rejected layout." }
}
$providers = @($candidate.SelectNodes('//*[@TextureProviderName]'))
if ($providers.Count -ne 4 -or
    $null -eq $candidate.SelectSingleNode('//*[@Id="SeasonPointer" and @TextureProviderName="AocMapBarSeasonArrowTextureProvider"]') -or
    $null -eq $candidate.SelectSingleNode('//*[@Id="NotchBackingPatch" and @TextureProviderName="AocMapBarNotchBackingTextureProvider"]') -or
    $null -eq $candidate.SelectSingleNode('//*[@Id="MapTimeDialFrame" and @TextureProviderName="AocMapBarMedallionRimTextureProvider"]')) {
    throw 'Only the notch backing and compact medallion rim may use custom texture providers.'
}

$dial=$candidate.SelectSingleNode('//*[@Id="MapTimeDialButton"]')
$dayNight=$candidate.SelectSingleNode('//*[@Id="VanillaDayNightDisc"]')
$frame=$candidate.SelectSingleNode('//*[@Id="MapTimeDialFrame"]')
if($dayNight.ParentNode.ParentNode.Id-ne'MapTimeDialButton'-or$dayNight.Name-ne'MapTimeImageBrushWidget'-or
    $dayNight.Brush-ne'AocMapTimeImageLarge'-or$dayNight.DayTime-ne'@Time'-or
    $dayNight.SuggestedWidth-ne'131.04'-or$dayNight.SuggestedHeight-ne'84'-or
    $dayNight.CircularClipEnabled-ne'true'-or$dayNight.CircularClipRadius-ne'42') {
    throw 'The center must remain the native animated day/night disc.'
}
if($frame.ParentNode.ParentNode.Id-ne'MapTimeDialButton'-or$frame.Name-ne'TextureWidget'-or
    $frame.TextureProviderName-ne'AocMapBarMedallionRimTextureProvider') {
    throw 'The dial rim must remain the polished bronze medallion frame.'
}
if(([double]$candidate.SelectSingleNode('//*[@Id="PauseButton"]').PositionXOffset - 449) -lt 2) {
    throw 'Pause control spacing from the dial changed.'
}

$sandboxPath=Join-Path $BannerlordRoot 'Modules\SandBox\GUI\SandBoxSpriteData.xml'
[xml]$sprites=Get-Content -LiteralPath $sandboxPath -Raw
foreach($name in @('MapBar\mapbar_center_frame','MapBar\mapbar_center_circle_frame','MapBar\mapbar_center_circle_daynight')) {
    if($null-eq$sprites.SelectSingleNode("//SpritePart[Name='$name']")){throw "Missing native sprite: $name"}
}
Write-Host 'MapBar candidate verification passed: measured target layout, vanilla frame/dial/controls, and one transparent season crown.'
