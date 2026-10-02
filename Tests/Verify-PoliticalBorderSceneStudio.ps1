$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$hostPath = Join-Path $root 'Tools\PoliticalBorderSceneStudio\PoliticalMapStudioHost.cs'
$subModulePath = Join-Path $root 'Tools\PoliticalBorderSceneStudio\PoliticalMapStudioSubModule.cs'
$projectPath = Join-Path $root 'Tools\PoliticalBorderSceneStudio\AocPoliticalMapStudio.Editor.csproj'
$modulePath = Join-Path $root 'Tools\PoliticalBorderSceneStudio\Module\SubModule.xml'
$launcherPath = Join-Path $root 'Tools\PoliticalBorderSceneStudio\Launcher\Program.cs'
$previewBridgePath = Join-Path $root 'Tools\PoliticalBorderSceneStudio\GauntletLivePreviewBridge.cs'
$previewViewModelsPath = Join-Path $root 'Tools\PoliticalBorderSceneStudio\GauntletPreviewViewModels.cs'
$previewReporterPath = Join-Path $root 'Tools\PoliticalBorderSceneStudio\GauntletPreviewWidgetReporter.cs'
$previewPrefabPath = Join-Path $root 'Tools\PoliticalBorderSceneStudio\Module\GUI\Prefabs\AocLivePreview.xml'
$previewSlotAPath = Join-Path $root 'Tools\PoliticalBorderSceneStudio\Module\GUI\Prefabs\AocLivePreviewSlotA.xml'
$previewSlotBPath = Join-Path $root 'Tools\PoliticalBorderSceneStudio\Module\GUI\Prefabs\AocLivePreviewSlotB.xml'
$unityPreviewControllerPath = Join-Path $root 'Tools\PoliticalBorderMapEditor.Unity\Assets\AocMapStudio\Scripts\GauntletLivePreviewController.cs'
$deployPath = Join-Path $root 'Tools\PoliticalBorderSceneStudio\Deploy-SceneStudio.ps1'

foreach ($path in @($hostPath, $subModulePath, $projectPath, $modulePath,
        $launcherPath, $previewBridgePath, $previewViewModelsPath,
        $previewReporterPath, $previewPrefabPath, $previewSlotAPath,
        $previewSlotBPath, $unityPreviewControllerPath, $deployPath)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing Scene Studio contract file: $path" }
}

$subModuleSource = Get-Content -LiteralPath $subModulePath -Raw
foreach ($token in @('private bool _activated;', 'Input.IsKeyPressed(InputKey.F8)',
        'PoliticalMapStudioHost.Initialize();', 'if (_activated)')) {
    if (-not $subModuleSource.Contains($token)) {
        throw "Scene Studio delayed-activation contract is missing: $token"
    }
}
$loadMethod = [regex]::Match($subModuleSource,
    'OnSubModuleLoad\(\)\s*\{(?<body>[^}]*)\}')
if (-not $loadMethod.Success) {
    throw 'Scene Studio module-load method could not be inspected.'
}
if ($loadMethod.Groups['body'].Value.Contains('PoliticalMapStudioHost.Initialize();')) {
    throw 'Scene Studio still initializes its scene host during native module load.'
}
if ($subModuleSource -notmatch 'if \(!_activated\)[\s\S]*?Utilities\.EditModeEnabled[\s\S]*?Input\.IsKeyPressed\(InputKey\.F8\)[\s\S]*?return;') {
    throw 'Scene Studio does not remain dormant until explicit F8 activation in Edit Mode.'
}
foreach ($token in @('GauntletLivePreviewBridge.Tick(dt);',
        'GauntletLivePreviewBridge.Shutdown();')) {
    if (-not $subModuleSource.Contains($token)) {
        throw "Scene Studio preview lifecycle contract is missing: $token"
    }
}

$bridge = Get-Content -LiteralPath $previewBridgePath -Raw
foreach ($token in @('aoc-gauntlet-preview-request-v2',
        'aoc-gauntlet-preview-result-v2', 'Preview source must be an AOC prefab',
        'ScreenManager.AddGlobalLayer', 'Utilities.TakeScreenshot',
        'AocLivePreviewSlotA', 'AocLivePreviewSlotB',
        'File.Replace(temporary, path, null)',
        'UIResourceManager.WidgetFactory.OnUnload(previewMovie)',
        'ReleaseMovie(_movie)', 'Guid.TryParseExact', 'revision',
        'prefabSha256', 'ComputeSha256',
        'preview-" + request.requestId + ".png"',
        'GauntletPreviewWidgetReporter.Capture', 'widgetRectangles',
        'Win64_Shipping_wEditor', 'IsMovieLoaded',
        'RenderSettleSeconds', 'PreparePreviewPrefab',
        'UIResourceManager.LoadSpriteCategory(', '"ui_mapbar"',
        '_mapBarSpriteCategory.Unload();',
        'WorldEventsRowSnapScrollablePanel',
        'StrategicMapZoomScrollablePanel', 'TextureProviderName',
        'ReplaceSplitClockWidgets', 'AddBlackStage', 'SaveXmlAtomic')) {
    if (-not $bridge.Contains($token)) {
        throw "Gauntlet live-preview bridge contract is missing: $token"
    }
}
if ($bridge.Contains('UIResourceManager.Refresh();')) {
    throw 'Gauntlet preview still performs a broad shared-resource refresh.'
}
if ($bridge -notmatch 'source\.StartsWith\(exchange[\s\S]*?source\.StartsWith\(aocPrefabs') {
    throw 'Gauntlet preview bridge does not constrain editable source paths.'
}
$previewViewModels = Get-Content -LiteralPath $previewViewModelsPath -Raw
foreach ($token in @('GauntletPreviewMapTimeControlViewModel',
        'public GauntletPreviewMapTimeControlViewModel MapTimeControl',
        'return "January, 8th";', 'return "1085";',
        'return "01:46";', 'return "AM";',
        'public int TimeFlowState', 'public float SeasonProgress',
        'public float SeasonPointerRotation', 'return 45f;',
        'public string WIN', 'public string SPR', 'public string SUM',
        'public string AUT', 'ExecuteTimeControlChange(int value) { }',
        'ExecuteResetCamera() { }', 'ExecuteToggleWorldCalendar() { }',
        'ExecuteBeginHint() { }', 'ExecuteEndHint() { }')) {
    if (-not $previewViewModels.Contains($token)) {
        throw "MapBar preview view-model contract is missing: $token"
    }
}
$previewReporter = Get-Content -LiteralPath $previewReporterPath -Raw
foreach ($token in @('MaximumReportedWidgets = 512',
        'widget.GlobalPosition.X', 'widget.GlobalPosition.Y',
        'widget.MeasuredSize.X', 'widget.MeasuredSize.Y',
        'widget.IsRecursivelyVisible()', 'widget.CurrentState',
        'textWidget.Text', 'widget.Sprite.Name')) {
    if (-not $previewReporter.Contains($token)) {
        throw "Runtime widget-rectangle report is missing: $token"
    }
}
foreach ($slotPath in @($previewSlotAPath, $previewSlotBPath)) {
    [xml]$slot = Get-Content -LiteralPath $slotPath -Raw
    if ($slot.Prefab.Window.Widget -eq $null) {
        throw "Gauntlet preview slot is not a valid prefab: $slotPath"
    }
}
$unityPreviewController = Get-Content -LiteralPath $unityPreviewControllerPath -Raw
foreach ($token in @('aoc-gauntlet-preview-request-v2',
        'aoc-gauntlet-preview-result-v2', 'DateTime.UtcNow.Ticks',
        'prefabSha256 = ComputeSha256(previewSource)',
        '"preview-" + requestId + ".png"',
        'result.requestId, _pendingRequestId',
        'result.revision != _pendingRequestRevision',
        'result.widgetRectangles.Length', 'IsInsideExchange(imagePath)')) {
    if (-not $unityPreviewController.Contains($token)) {
        throw "Unity exact-preview correlation contract is missing: $token"
    }
}
$deploy = Get-Content -LiteralPath $deployPath -Raw
if ($deploy.Contains("'AOC Political Map Studio.lnk'")) {
    throw 'Diagnostics deployment still replaces the primary Unity application shortcut.'
}
foreach ($token in @('Ages Of CalradiaSpriteData.xml', "'SpriteParts'")) {
    if (-not $deploy.Contains($token)) {
        throw "Exact-preview AOC sprite mirror is missing: $token"
    }
}
if ($deploy.Contains("Join-Path `$aocGui 'Prefabs'")) {
    throw 'Diagnostics deployment must not copy protected AOC prefabs.'
}

$launcher = Get-Content -LiteralPath $launcherPath -Raw
foreach ($token in @('Bannerlord.exe', '/singleplayer', '/open_scene',
        'NavalDLC", "SceneObj", "Main_map', 'AocPoliticalMapStudio*_MODULES_',
        'GetShortPathName', ' /open_scene " + shortSceneDirectory',
        'EnvironmentVariables["SteamAppId"] = "1393600"')) {
    if (-not $launcher.Contains($token)) { throw "Direct Scene Studio launcher contract is missing: $token" }
}
if ($launcher.Contains('steam://run/1393600')) {
    throw 'Scene Studio launcher still delegates to the Steam/Bannerlord launcher.'
}
if ($launcher.Contains('/editor_only')) {
    throw 'Scene Studio launcher still uses the black editor-only rendering host.'
}


$source = Get-Content -LiteralPath $hostPath -Raw
$required = @(
    'Utilities.EditModeEnabled',
    'MBEditor.GetEditorSceneView()',
    'ProjectedMousePositionOnGround',
    'GetTerrainHeight',
    'GetWaterLevelAtPosition',
    'SnapToWaterline',
    'AddTracePoint',
    'TryFindGeneratedNode',
    'IsAuthorizedMainMap',
    '"Main_map"',
    '"NavalDLC"',
    'MaximumDebugPrimitivesPerFrame',
    'SavedCache',
    'ResolveAocOverridePath',
    'ReplacesGenerated = true',
    'FillBrush',
    'PoliticalBorderOverrides.xml'
)
foreach ($token in $required) {
    if (-not $source.Contains($token)) { throw "Scene Studio contract is missing: $token" }
}
if ($source -notmatch 'if \(!IsAuthorizedMainMap\(scene\)\)[\s\S]*?return;') {
    throw 'Scene Studio does not fail closed before viewport input/rendering.'
}
$exclusiveExport = [regex]::Match($source,
    'if \(TerrainExporter\.IsActive\)\s*\{(?<body>[\s\S]*?)\}\s*SceneView view')
if (-not $exclusiveExport.Success) {
    throw 'Scene Studio does not enter its exclusive export path before SceneView discovery.'
}
$exclusiveBody = $exclusiveExport.Groups['body'].Value
foreach ($forbidden in @('DrawPanel', 'SetAcceptGlobalDebugRenderObjects',
        'MBEditor.GetEditorSceneView', 'RefreshCache', 'DrawScene')) {
    if ($exclusiveBody.Contains($forbidden)) {
        throw "Exclusive export path still invokes preview operation: $forbidden"
    }
}
$unauthorizedGate = [regex]::Match($source,
    'if \(!IsAuthorizedMainMap\(scene\)\)(?<body>[\s\S]*?)return;')
if (-not $unauthorizedGate.Success -or $unauthorizedGate.Groups['body'].Value.Contains('DrawPanel')) {
    throw 'Scene Studio renders UI during a transitional or unauthorized editor scene.'
}
if ($source -notmatch 'if \(_activeCache == null\) _activeCache = new CachedPath\(\);[\s\S]*?_activeCache\.Points\.Add') {
    throw 'Active paths are not incrementally cached.'
}

[xml]$module = Get-Content -LiteralPath $modulePath -Raw
if ($module.Module.Id.value -ne 'AocPoliticalMapStudio') {
    throw 'Scene Studio module id is incorrect.'
}

Write-Host 'Political Border Scene Studio verification passed: delayed F8 activation, correlated alternating-slot Gauntlet previews, fixed MapBar mock data, runtime widget rectangles, exact War Sails gate, bounded rendering, and shared AOC plan output are present.'
