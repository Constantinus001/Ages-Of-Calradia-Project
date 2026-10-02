param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [string]$ModuleRoot = 'C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\AOC CORE'
)

$ErrorActionPreference = 'Stop'
$tool = Join-Path $Root 'Tools\PoliticalBorderMapEditor'
$native = Join-Path $Root 'Tools\PoliticalBorderMapEditor.Native'
$builder = Get-Content -Raw -LiteralPath (Join-Path $tool 'Build-MapEditor.ps1')
$project = Get-Content -Raw -LiteralPath (Join-Path $native 'AocMapStudio.Native.csproj')
$program = Get-Content -Raw -LiteralPath (Join-Path $native 'Program.cs')
$dataSource = Get-Content -Raw -LiteralPath (Join-Path $native 'MapStudioData.cs')
$window = Get-Content -Raw -LiteralPath (Join-Path $native 'MainWindow.cs')
$uiWindow = Get-Content -Raw -LiteralPath (Join-Path $native 'GauntletUiEditorWindow.cs')
$uiDocument = Get-Content -Raw -LiteralPath (Join-Path $native 'GauntletPrefabDocument.cs')
$uiLayout = Get-Content -Raw -LiteralPath (Join-Path $native 'GauntletLayoutEngine.cs')
$uiSprites = Get-Content -Raw -LiteralPath (Join-Path $native 'GauntletSpriteCatalog.cs')
$uiSpritePicker = Get-Content -Raw -LiteralPath (Join-Path $native 'GauntletSpritePickerWindow.cs')
$editor = Get-Content -Raw -LiteralPath (Join-Path $native 'NativeMapEditorControl.cs')
$interaction = Get-Content -Raw -LiteralPath (Join-Path $native 'NativeMapInteraction.cs')
$drawing = Get-Content -Raw -LiteralPath (Join-Path $native 'NativeMapDrawing.cs')
$geometry = Get-Content -Raw -LiteralPath (Join-Path $native 'PoliticalGeometrySnapshot.cs')
$boundary = Get-Content -Raw -LiteralPath (Join-Path $native 'PoliticalFillBoundaryIndex.cs')
$interaction = Get-Content -Raw -LiteralPath (Join-Path $native 'NativeMapInteraction.cs')
$terrain = Get-Content -Raw -LiteralPath (Join-Path $native 'NativeTerrainView.cs')
$readme = Get-Content -Raw -LiteralPath (Join-Path $tool 'README.md')

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

Assert-True ($project -match '<OutputType>WinExe</OutputType>' -and
    $project -match '<TargetFramework>net472</TargetFramework>' -and
    $project -match '<UseWPF>true</UseWPF>' -and
    $project -match '<TreatWarningsAsErrors>true</TreatWarningsAsErrors>') `
    'Map Studio is not a warnings-clean native .NET Framework WPF application.'
Assert-True ($program -match 'application\.Run\(new MainWindow\(data\)\)' -and
    $builder -match 'AocMapStudio\.exe' -and $builder -notmatch 'msedge\.exe|--app=') `
    'The launcher still depends on a browser instead of the native application.'

Assert-True ($builder -match "coordinateMode = 'bannerlord-world-v1'" -and
    $builder -match 'width = 2081; height = 2081' -and
    $builder -match 'worldMinX = 0\.0; worldMinY = 0\.0; worldMaxX = 1040\.0; worldMaxY = 1040\.0' -and
    $builder -match 'pixelsPerWorldUnit = 2\.0' -and
    $builder -match 'flora_bounding_rect' -and $builder -match 'sceneToken') `
    'The builder does not enforce the exact NavalDLC 0..1040 world plane and scene identity.'
Assert-True ($dataSource -match 'ExactCoordinateMode = "bannerlord-world-v1"' -and
    $dataSource -match '\(point\.X - _worldMinX\) \* _pixelsPerWorldUnit' -and
    $dataSource -match '\(_worldMaxY - point\.Y\) \* _pixelsPerWorldUnit' -and
    $dataSource -match 'point\.X / _pixelsPerWorldUnit \+ _worldMinX' -and
    $dataSource -match '_worldMaxY - point\.Y / _pixelsPerWorldUnit') `
    'World-to-map and inverse map transforms are not exact and reversible.'
Assert-True ($builder -match '\[switch\]\$IncludeApproximateWandReference' -and
    $builder -match "background = 'campaign-world-plane.png'" -and
    $builder -match "legacyGuide = 'legacy-strategic-guide.png'" -and
    $readme -match '86\.22-pixel RMS' -and $readme -match 'off by default') `
    'Approximate WAND/legacy imagery can still become the default coordinate source.'

Assert-True ($geometry -match 'CacheMagic = "AOCPBG60"' -and
    $geometry -match 'CacheVersion = 60' -and
    $geometry -match 'MaximumCombinedTriangles' -and
    $geometry -match 'GZipStream' -and
    $geometry -match 'PoliticalFilledAreaIndex' -and
    $geometry -match 'ClassifyLine' -and
    $geometry -match 'OutsideWorld' -and
    $geometry -match 'UnderwaterOrUnfilled') `
    'The bounded exact political-cache reader or subspan diagnostics are incomplete.'
Assert-True ($dataSource -match 'PoliticalGeometrySnapshot Geometry' -and
    $dataSource -match 'TryLoadLatest' -and $window -match 'SetPoliticalGeometrySnapshot') `
    'The exact political geometry cache is not connected to the native editor.'
Assert-True ($boundary -match 'class PoliticalFillBoundaryIndex' -and
    $boundary -match 'Dictionary<EdgeKey, int>' -and
    $boundary -match 'firstSide != secondSide' -and
    $boundary -match 'MaximumSnapRadiusWorld' -and
    $boundary -match 'BuildSpatialCells' -and
    $interaction -match '_exactCoastBoundary\.TrySnap' -and
    $interaction -match '_data\.SuppressedCoastal' -and
    $builder -match "landMask = ''" -and
    $builder -notmatch "landMask = 'campaign-land-water\.png'") `
    'Exact coast/lake snapping does not filter fill seams, bound queries, or retain its generated fallback.'

Assert-True ($editor -match '_showPoliticalFill = true' -and
    $drawing -match 'DrawPoliticalFill' -and $drawing -match 'RasterizePoliticalFill' -and
    $drawing -match 'BuildAuthoredFillGeometry' -and
    $drawing -match 'context\.PushClip\(authored\)' -and
    $editor -match 'path\.Closed' -and $dataSource -match '"fill-" \+ pathData\.Id') `
    'Political fill is not visible by default, cache-clipped, or derived from the same closed border path.'
Assert-True ($drawing -match 'Colors\.Black' -and $drawing -match 'DashStyle' -and
    $editor -match 'DrawCrosshair' -and $drawing -match 'double outer = 7d' -and
    $editor -match 'PromoteGenerated' -and $editor -match '_draggingNode') `
    'Black dotted planning lines, crosshair, circle-and-dot nodes, or generated editing are incomplete.'
Assert-True ($geometry -match 'PoliticalLineRegion\.UnderwaterOrUnfilled' -and
    $geometry -match 'PoliticalLineRegion\.OutsideWorld' -and
    $editor -match 'ClassifyLine' -and $editor -match 'span\.IsIssue' -and
    $editor -match '_diagnosticEdges\.Add' -and
    $drawing -match 'DrawDiagnostics' -and $drawing -match 'foreach \(EditorDiagnosticEdge edge in edges\)' -and
    $window -match 'RED EDGE = ONLY THE INVALID SPAN') `
    'Underwater/fill spill diagnostics do not restrict red to offending border subspans.'
Assert-True ($window -match 'ShowPoliticalFill = true' -and
    $window -match 'ShowSuppressedCoasts = false' -and
    $window -match 'ShowReferenceGuide = value' -and
    $window -match 'APPROX\. RED REFERENCE \(OFF\)' -and
    $window -match 'DARK-GREEN 10-UNIT GRID') `
    'Native layer defaults do not keep fill visible and approximate/coastal guides off.'

foreach ($toolName in @('Select', 'Line', 'Freehand', 'Coast', 'Fill')) {
    Assert-True ($window -match "EditorTool\.$toolName") "Native tool button is missing: $toolName"
}
Assert-True ($builder -match 'Get-FactionOwnerMap' -and $builder -match 'super_faction' -and
    $builder -match 'Get-FactionColorMap' -and $builder -match 'spkingdoms\.xml' -and
    $dataSource -match 'public string faction' -and $dataSource -match 'public string color' -and
    $interaction -match 'InferFaction' -and $interaction -match 'foreach \(SettlementData settlement' -and
    $interaction -match 'MapPoint\.Distance' -and $dataSource -match 'ResolveFactionColor' -and
    $dataSource -match 'MapStudioData\.NormalizeColor\(pathData\.FillColor\)' -and
    $dataSource -match 'MapStudioData\.NormalizeColor\(brush\.FillColor\)') `
    'New borders and fill strokes cannot infer or persist the nearest settlement faction color automatically.'
Assert-True ($window -match 'SMOOTH SELECTED' -and $window -match 'CLOSE \+ ATTACH FILL' -and
    $window -match 'FILL BRUSH RADIUS' -and $window -match 'BORDER WIDTH' -and
    $window -match 'TOWNS' -and $window -match 'CASTLES' -and $window -match 'VILLAGES') `
    'Required native editing, sizing, or settlement controls are missing.'
Assert-True ($window -match 'OverrideDocument\.Save' -and
    $window -match 'OverrideDocument\.Load' -and
    $window -match 'string backup = target \+ "\.backup-"' -and
    $window -match 'ModuleData.*PoliticalBorderOverrides\.xml' -and
    $window -notmatch 'AgesOfCalradia\.dll.*Write|WorldCalendar\.xml.*Write') `
    'Portable save/open or protected-safe AOC CORE installation is incomplete.'
Assert-True ($window -match 'UI EDITOR' -and $window -match 'GauntletUiEditorWindow' -and
    $uiWindow -match 'WIDGET HIERARCHY' -and $uiWindow -match 'LAYOUT PROPERTIES' -and
    $uiWindow -match '1920 × 1080' -and $uiWindow -match 'OnCanvasMove' -and
    $uiWindow -match 'PushUndo' -and $uiWindow -match 'STRUCTURAL, VERIFY IN BANNERLORD') `
    'The native Map Studio does not expose the visual Gauntlet UI layout workspace.'
Assert-True ($window -match 'BuildApplicationMenu' -and
    $window -match 'Political Map Editor' -and $window -match 'UI Layout Editor' -and
    $window -match 'Asset Browser' -and $builder -match "AocMapStudio\.exe'\)" -and
    $builder -match 'Copy-Item.*AocMapStudio\.exe' -and
    $readme -match 'no command window is\s+required') `
    'Map Studio is not deployed as a direct-launch application with internal workspace menus.'
Assert-True ($uiWindow -match 'ADD PANEL' -and $uiWindow -match 'ADD TEXT' -and
    $uiWindow -match 'ADD IMAGE' -and $uiWindow -match 'WORDS SHOWN' -and
    $uiDocument -match 'IMAGE / TEXTURE' -and $uiDocument -match 'BUTTON TEXT' -and
    $uiWindow -match 'DUPLICATE' -and $uiWindow -match 'MOVE UP' -and
    $uiDocument -match 'AddWidget' -and $uiDocument -match 'DuplicateWidget' -and
    $uiDocument -match 'DeleteWidget' -and $uiDocument -match 'MoveWidget' -and
    $uiLayout -match 'class GauntletLayoutEngine' -and
    $uiLayout -match 'WidthSizePolicy.*StretchToParent' -and
    $uiLayout -match 'StackLayout\.LayoutMethod' -and
    $uiLayout -match 'ParentBounds') `
    'In-application widget authoring or parent-relative Gauntlet layout is incomplete.'
Assert-True ($uiWindow -match 'CHOOSE TEXTURE' -and
    $uiWindow -match 'element\.GetAttribute\("Text"\)' -and
    $uiWindow -match 'element\.GetAttribute\("Sprite"\)' -and
    $uiWindow -match 'sprite\.LoadImage' -and
    $uiSprites -match 'class GauntletSpriteCatalog' -and
    $uiSprites -match 'SpriteParts' -and $uiSprites -match '\*\.png' -and
    $uiSpritePicker -match 'AOC Asset Browser' -and
    $uiSpritePicker -match 'USE SELECTED TEXTURE') `
    'Editable text placement or the in-application texture chooser is incomplete.'
Assert-True ($uiWindow -match 'ASSET BROWSER' -and
    $uiSprites -match 'ImportTexture' -and $uiSprites -match 'ImportTpac' -and
    $uiSprites -match 'ExportFile' -and $uiSprites -match 'TPAC' -and
    $uiSpritePicker -match 'TPAC PACKAGES' -and
    $uiSpritePicker -match 'IMPORT PNG' -and $uiSpritePicker -match 'EXPORT PNG' -and
    $uiSpritePicker -match 'IMPORT TPAC' -and $uiSpritePicker -match 'EXPORT TPAC' -and
    $uiSpritePicker -match 'SpriteSheetGenerator\.exe') `
    'The built-in texture and TPAC asset-browser workflow is incomplete.'
Assert-True ($uiDocument -match 'DtdProcessing = DtdProcessing\.Prohibit' -and
    $uiDocument -match 'SetAttribute' -and $uiDocument -match 'ValidateAttribute' -and
    $uiDocument -match 'File\.Replace' -and $uiDocument -match 'backup-' -and
    $uiDocument -match 'IsProtectedWorldCalendarPath' -and
    $uiDocument -match 'approved WorldCalendar\.xml is protected') `
    'UI prefab parsing, atomic persistence, validation, or protected-baseline guard is incomplete.'

Assert-True ($terrain -match 'Viewport3D' -and $terrain -match '0\.\.1040' -and
    $terrain -match 'FilledArea' -and $terrain -notmatch 'wand-basemap') `
    'The native 3D inspector is not based on the exact world/cache plane.'
Assert-True ($readme -match 'native Windows WPF application' -and
    $readme -match '2081x2081' -and $readme -match 'PoliticalBorderOverrides\.xml' -and
    $readme -match 'red over only the failing subspans' -and
    $readme -match 'UI EDITOR' -and $readme -match 'structural canvas') `
    'Native editor alignment, persistence, and diagnostic contracts are undocumented.'

$windowsPowerShell = Get-Command powershell.exe -ErrorAction Stop
& $windowsPowerShell.Source -NoProfile -ExecutionPolicy Bypass `
    -File (Join-Path $tool 'Build-MapEditor.ps1') -ModuleRoot $ModuleRoot
Assert-True ($LASTEXITCODE -eq 0) `
    'The deployed .cmd launcher builder does not parse or run under Windows PowerShell.'

$dataPath = Join-Path $tool 'map-data.js'
Assert-True (Test-Path -LiteralPath $dataPath -PathType Leaf) 'Prepared exact map-data.js is missing.'
$dataText = Get-Content -Raw -LiteralPath $dataPath
$prefix = 'window.AOC_POLITICAL_MAP_DATA='
Assert-True ($dataText.StartsWith($prefix, [StringComparison]::Ordinal)) 'Prepared map-data.js header is malformed.'
$prepared = $dataText.Substring($prefix.Length).TrimEnd(';') | ConvertFrom-Json
Assert-True ($prepared.map.coordinateMode -eq 'bannerlord-world-v1' -and
    $prepared.map.width -eq 2081 -and $prepared.map.height -eq 2081 -and
    $prepared.map.worldMinX -eq 0 -and $prepared.map.worldMaxX -eq 1040 -and
    $prepared.map.worldMinY -eq 0 -and $prepared.map.worldMaxY -eq 1040) `
    'Prepared map data is not on the exact inclusive world plane.'
Assert-True (@($prepared.settlements | Where-Object {
        [string]::IsNullOrWhiteSpace([string]$_.faction) }).Count -eq 0) `
    'Prepared settlements are missing automatic kingdom/clan ownership metadata.'
Assert-True (@($prepared.settlements | Where-Object {
        [string]$_.color -notmatch '^0x[0-9A-F]{8}$' }).Count -eq 0) `
    'Prepared settlements are missing normalized Bannerlord faction colors.'
Assert-True (@($prepared.settlements | Where-Object {
        $_.faction -eq 'kingdom:nord' }).Count -gt 0) `
    'NavalDLC Nord settlements were not resolved to their kingdom ownership.'

$generatedPath = Join-Path $ModuleRoot 'ModuleData\PoliticalBorderGeneratedLines.xml'
if (Test-Path -LiteralPath $generatedPath -PathType Leaf) {
    [xml]$source = Get-Content -Raw -LiteralPath $generatedPath
    $sourceChains = @($source.GeneratedBorderLines.Chain)
    Assert-True ($sourceChains.Count -eq $prepared.generated.Count) 'Generated chain count changed during map preparation.'
    for ($chainIndex = 0; $chainIndex -lt $sourceChains.Count; $chainIndex++) {
        $sourcePoints = @($sourceChains[$chainIndex].Point)
        $preparedPoints = @($prepared.generated[$chainIndex].points)
        Assert-True ($sourcePoints.Count -eq $preparedPoints.Count) "Generated point count changed in chain $chainIndex."
        for ($pointIndex = 0; $pointIndex -lt $sourcePoints.Count; $pointIndex++) {
            $x = [double]::Parse([string]$sourcePoints[$pointIndex].x, [Globalization.CultureInfo]::InvariantCulture)
            $y = [double]::Parse([string]$sourcePoints[$pointIndex].y, [Globalization.CultureInfo]::InvariantCulture)
            Assert-True ($x -eq [double]$preparedPoints[$pointIndex][0] -and
                $y -eq [double]$preparedPoints[$pointIndex][1]) `
                "Generated world coordinate changed at chain $chainIndex point $pointIndex."
        }
    }
}

Assert-True (Test-Path -LiteralPath (Join-Path $native 'bin\Release\net472\AocMapStudio.exe') -PathType Leaf) `
    'Native Map Studio Release executable is missing.'
Write-Host 'Native political border Map Studio verification passed: exact world alignment, attached fill, subspan diagnostics, native UI, and Bannerlord-compatible persistence.'
