param(
    [string]$BannerlordRoot = 'C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$hostRoot = Join-Path $root 'Tools\MapBarPreviewHost'
$projectPath = Join-Path $hostRoot 'AocMapBarPreviewHost.csproj'
$hostPath = Join-Path $hostRoot 'MapBarPreviewHost.cs'
$viewModelPath = Join-Path $hostRoot 'MapBarPreviewViewModels.cs'
$reporterPath = Join-Path $hostRoot 'MapBarPreviewWidgetReporter.cs'
$providerPath = Join-Path $hostRoot 'AocMapBarSeasonCrownTextureProvider.cs'
$subModulePath = Join-Path $hostRoot 'MapBarPreviewSubModule.cs'
$manifestPath = Join-Path $hostRoot 'Module\SubModule.xml'
$stagePath = Join-Path $hostRoot 'Module\GUI\Prefabs\AocMapBarBlackStage.xml'
$brushPath = Join-Path $hostRoot 'Module\GUI\Brushes\AocMapBar.xml'
$startPath = Join-Path $hostRoot 'Start-MapBarExactPreview.ps1'
$candidatePath = Join-Path $root 'Tools\PoliticalBorderSceneStudio\MapBarPreview\AocMapBarCenterCandidate.xml'
$outputPath = Join-Path $hostRoot 'bin\Release\net472\AocMapBarPreviewHost.dll'

foreach ($path in @($projectPath, $hostPath, $viewModelPath, $reporterPath,
        $providerPath, $subModulePath, $manifestPath, $stagePath, $brushPath, $startPath,
        $candidatePath, $outputPath)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing MapBar preview host contract file: $path" }
}

& (Join-Path $root 'Tests\Verify-MapBarPreviewCandidate.ps1') -BannerlordRoot $BannerlordRoot
if (-not $?) { throw 'MapBar preview candidate contract failed.' }

[xml]$project = Get-Content -LiteralPath $projectPath -Raw
if ($project.Project.PropertyGroup.TargetFramework -notcontains 'net472') {
    throw 'MapBar preview host must target net472.'
}
$projectSource = Get-Content -LiteralPath $projectPath -Raw
if ($projectSource.Contains('Win64_Shipping_wEditor') -or
    -not $projectSource.Contains('Win64_Shipping_Client')) {
    throw 'MapBar preview host references are not isolated to Win64_Shipping_Client.'
}
foreach ($reference in @($project.Project.ItemGroup.Reference)) {
    if ($null -eq $reference.HintPath) { continue }
    $resolved = [string]$reference.HintPath -replace [regex]::Escape('$(BannerlordDir)'), $BannerlordRoot
    if (-not (Test-Path -LiteralPath $resolved)) {
        throw "Missing Shipping Client reference: $resolved"
    }
}

[xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
if ($manifest.Module.Id.value -ne 'AocMapBarPreviewHost' -or
    $manifest.Module.SubModules.SubModule.DLLName.value -ne 'AocMapBarPreviewHost.dll') {
    throw 'MapBar preview module manifest identity is incorrect.'
}
$dependencies = @($manifest.Module.DependedModules.DependedModule | ForEach-Object { $_.Id })
foreach ($dependency in @('Native', 'SandBoxCore', 'Sandbox')) {
    if ($dependency -notin $dependencies) { throw "MapBar preview manifest is missing dependency: $dependency" }
}

[xml]$stage = Get-Content -LiteralPath $stagePath -Raw
$stageWidget = $stage.SelectSingleNode('//*[@Id="MapBarPreviewBlackStage"]')
if ($null -eq $stageWidget -or $stageWidget.Sprite -ne 'BlankWhiteSquare_9' -or
    $stageWidget.Color -ne '#000000FF' -or
    $stageWidget.WidthSizePolicy -ne 'StretchToParent' -or
    $stageWidget.HeightSizePolicy -ne 'StretchToParent') {
    throw 'MapBar exact-preview black stage contract is incorrect.'
}

$hostSource = Get-Content -LiteralPath $hostPath -Raw
$requiredHostTokens = @(
    'aoc-mapbar-preview-request-v1',
    'aoc-mapbar-preview-result-v1',
    'StableGeometryFramesRequired = 3',
    'StableScreenshotFramesRequired = 2',
    'ScreenManager.TopScreen == null',
    'UIResourceManager.LoadSpriteCategory("ui_mapbar")',
    'UnloadSpriteCategory(ref _mapBarSpriteCategory',
    'TextureProviderFactory.RefreshProviderTypes()',
    'ScreenManager.AddGlobalLayer(_blackStageLayer, false)',
    'ScreenManager.AddGlobalLayer(_previewLayer, false)',
    'MapBarPreviewWidgetReporter.ValidateMapBarGeometry',
    'Utilities.TakeScreenshot(_screenshotPath)',
    'ValidateBmp(_screenshotPath',
    'ValidateReferenceLayerPixels(_screenshotPath',
    'REFERENCE_LAYER_VISUAL_GATE goldPixels=',
    'did not paint in the Shipping Client screenshot; gold pixels=',
    'Screenshot does not have a valid BMP signature.',
    'Screenshot BMP length does not match its file header.',
    'Screenshot BMP pixel data is incomplete.',
    'FileShare.None',
    'FileMode.CreateNew',
    'renderer = "Win64_Shipping_Client"',
    'DtdProcessing = DtdProcessing.Prohibit',
    'candidateSha256',
    'rectangles = _rectangles'
)
foreach ($token in $requiredHostTokens) {
    if (-not $hostSource.Contains($token)) { throw "MapBar Shipping Client host contract is missing: $token" }
}
if ($hostSource.Contains('UIResourceManager.Refresh')) {
    throw 'MapBar preview host must never globally refresh live UI resources.'
}
if ($hostSource.Contains('Win64_Shipping_wEditor')) {
    throw 'MapBar preview host source incorrectly claims the wEditor renderer.'
}

$reporter = Get-Content -LiteralPath $reporterPath -Raw
# Keep the diagnostic runtime contract correlated with the actual candidate.
# A missing notch rectangle previously shifted every lookup and prevented capture.
[xml]$candidate = Get-Content -LiteralPath $candidatePath -Raw
$idBlock = [regex]::Match($reporter, 'string\[\] ids = \{(?<body>.*?)\};', 'Singleline')
$geometryBlock = [regex]::Match($reporter, 'float\[,] geometry = \{(?<body>.*?)\};', 'Singleline')
$reportedIds = @([regex]::Matches($idBlock.Groups['body'].Value, '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
$reportedRows = @([regex]::Matches($geometryBlock.Groups['body'].Value, '\{([^{}]+)\}') | ForEach-Object { ,($_.Groups[1].Value.Split(',')) })
if ($reportedIds.Count -eq 0 -or $reportedIds.Count -ne $reportedRows.Count) {
    throw 'Runtime reporter widget IDs and geometry rows must have matching nonzero counts.'
}
for ($index = 0; $index -lt $reportedIds.Count; $index++) {
    $node = $candidate.SelectSingleNode("//*[@Id='$($reportedIds[$index])']")
    if ($null -eq $node) { throw "Runtime reporter widget is absent from candidate: $($reportedIds[$index])" }
    $attributes = @('PositionXOffset', 'PositionYOffset', 'SuggestedWidth', 'SuggestedHeight')
    for ($axis = 0; $axis -lt 4; $axis++) {
        $expected = [double]::Parse($reportedRows[$index][$axis].Trim().TrimEnd('f'), [Globalization.CultureInfo]::InvariantCulture)
        $actual = [double]::Parse($node.GetAttribute($attributes[$axis]), [Globalization.CultureInfo]::InvariantCulture)
        if ([Math]::Abs($expected - $actual) -gt 0.01) {
            throw "Runtime reporter disagrees with candidate: $($reportedIds[$index]) $($attributes[$axis]) ($expected versus $actual)."
        }
    }
}
foreach ($token in @('widget.GlobalPosition.X', 'widget.MeasuredSize.X',
        'GeometrySignature', 'HasReadyGeometry', 'panel.width / 802f',
        'item.parentId', 'item.sprite', 'item.brush', 'item.text',
        'item.rotation', 'RequireDirectChild', 'AssertLogicalGeometry',
        'AssertDrawOrder', 'Controls must remain clear of the native dial',
        'FastForward4xButton', 'MapBarFastForwardButton',
        'EndsWith("(Clone)"',
        'ExpandedMapBarFrame', 'NotchBackingPatch', 'CenterMapBarFrame', 'SeasonCrown',
        'VanillaDayNightDisc', 'MapTimeDialFrame', 'CalendarYearText',
        'CalendarClockText')) {
    if (-not $reporter.Contains($token)) { throw "Runtime rectangle reporter contract is missing: $token" }
}

$viewModel = Get-Content -LiteralPath $viewModelPath -Raw
foreach ($token in @('January, 8th', '1085', 'ClockDisplayText', '01:46 AM',
        'Super Fast Forward [4] - 4x',
        'public void ExecuteTimeControlChange(int value) { }',
        'public void ExecuteOpenCamp() { }')) {
    if (-not $viewModel.Contains($token)) { throw "MapBar preview mock VM contract is missing: $token" }
}

$subModule = Get-Content -LiteralPath $subModulePath -Raw
if (-not $subModule.Contains('MapBarPreviewHost.Tick(dt);') -or
    $subModule.Contains('CampaignGameStarter') -or $subModule.Contains('Harmony')) {
    throw 'MapBar preview submodule is not a diagnostics-only application-tick adapter.'
}

$provider = Get-Content -LiteralPath $providerPath -Raw
foreach ($token in @('class AocMapBarSeasonCrownTextureProvider',
        'class AocMapBarNotchBackingTextureProvider',
        'class AocMapBarMedallionRimTextureProvider',
        'ReadRgba', 'aoc_mapbar_season_crown_compact.png', 'aoc_mapbar_notch_backing.png',
        'aoc_mapbar_medallion_ring_oval.png',
        'EngineTexture.CreateFromByteArray',
        'ReleaseAfterNumberOfFrames(1)', 'failed safely')) {
    if (-not $provider.Contains($token)) { throw "Season crown provider contract is missing: $token" }
}

$start = Get-Content -LiteralPath $startPath -Raw
foreach ($token in @('Verify-ProtectedPoliticalBaseline.ps1',
        'Verify-MapBarPreviewCandidate.ps1',
        "GUI\SpriteParts\aoc_mapbar_preview",
        "GUI\Brushes\AocMapBar.xml",
        "'AocMapBarPreviewHost'", "'AocMapBarPreview.xml'",
        'Win64_Shipping_Client', "'Bannerlord.Native'", 'stableFramesRequired = 3',
        'aoc-mapbar-preview-request-v1',
        'AocMapBarPreviewHost*_MODULES_')) {
    if (-not $start.Contains($token)) { throw "MapBar preview start/deploy contract is missing: $token" }
}
if ($start.Contains('Win64_Shipping_wEditor') -or
    $start.Contains('GUI\Prefabs\Map\MapBar.xml')) {
    throw 'MapBar preview workflow can touch a non-diagnostics renderer or production MapBar.'
}

[xml]$brushes = Get-Content -LiteralPath $brushPath -Raw
$largeBrush = $brushes.SelectSingleNode('/Brushes/Brush[@Name="AocMapTimeImageLarge"]')
if ($null -eq $largeBrush -or @($largeBrush.Layers.BrushLayer).Count -ne 2 -or
    @($largeBrush.Layers.BrushLayer | Where-Object { $_.Sprite -eq 'MapBar\mapbar_center_circle_daynight' -and
        $_.OverridenWidth -eq '295.68' -and $_.OverridenHeight -eq '84' }).Count -ne 2 -or
    $largeBrush.Layers.BrushLayer[1].XOffset -ne '25.2') {
    throw 'Large native day/night brush contract is missing.'
}

Write-Host 'MapBar Shipping Client preview host verification passed: correlated runtime geometry, vanilla frame/dial/controls, compact halo, and three approved texture providers.'
