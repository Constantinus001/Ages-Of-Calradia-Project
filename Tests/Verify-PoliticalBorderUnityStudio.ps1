$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$unity = Join-Path $root 'Tools\PoliticalBorderMapEditor.Unity'
$exporter = Get-Content -LiteralPath (Join-Path $root 'Tools\PoliticalBorderSceneStudio\MainMapTerrainExporter.cs') -Raw
$hostSource = Get-Content -LiteralPath (Join-Path $root 'Tools\PoliticalBorderSceneStudio\PoliticalMapStudioHost.cs') -Raw
$package = Get-Content -LiteralPath (Join-Path $unity 'Assets\AocMapStudio\Scripts\MainMapPackage.cs') -Raw
$factory = Get-Content -LiteralPath (Join-Path $unity 'Assets\AocMapStudio\Scripts\MainMapTerrainFactory.cs') -Raw
$meshFactory = Get-Content -LiteralPath (Join-Path $unity 'Assets\AocMapStudio\Scripts\MainMapSurfaceMeshFactory.cs') -Raw
$terrainShaderPath = Join-Path $unity 'Assets\AocMapStudio\Resources\AocTerrain.shader'
if (-not (Test-Path -LiteralPath $terrainShaderPath)) { throw 'Embedded AOC terrain shader is missing.' }
$terrainShader = Get-Content -LiteralPath $terrainShaderPath -Raw
$waterFactory = Get-Content -LiteralPath (Join-Path $unity 'Assets\AocMapStudio\Scripts\MainMapWaterFactory.cs') -Raw
$waterShaderPath = Join-Path $unity 'Assets\AocMapStudio\Resources\AocWater.shader'
if (-not (Test-Path -LiteralPath $waterShaderPath)) { throw 'Embedded AOC water shader is missing.' }
$waterShader = Get-Content -LiteralPath $waterShaderPath -Raw
$settlementLayer = Get-Content -LiteralPath (Join-Path $unity 'Assets\AocMapStudio\Scripts\SettlementReferenceLayer.cs') -Raw
$settlementShaderPath = Join-Path $unity 'Assets\AocMapStudio\Resources\AocSettlement.shader'
if (-not (Test-Path -LiteralPath $settlementShaderPath)) { throw 'Embedded settlement shader is missing.' }
$settlementShader = Get-Content -LiteralPath $settlementShaderPath -Raw
$generatedLayer = Get-Content -LiteralPath (Join-Path $unity 'Assets\AocMapStudio\Scripts\GeneratedBorderReferenceLayer.cs') -Raw
$borderGeometry = Get-Content -LiteralPath (Join-Path $unity 'Assets\AocMapStudio\Scripts\BorderDisplayGeometry.cs') -Raw
$generatedShaderPath = Join-Path $unity 'Assets\AocMapStudio\Resources\AocGeneratedBorder.shader'
if (-not (Test-Path -LiteralPath $generatedShaderPath)) { throw 'Embedded generated-border shader is missing.' }
$generatedShader = Get-Content -LiteralPath $generatedShaderPath -Raw
$planning = Get-Content -LiteralPath (Join-Path $unity 'Assets\AocMapStudio\Scripts\BorderPlanningController.cs') -Raw
$planLayer = Get-Content -LiteralPath (Join-Path $unity 'Assets\AocMapStudio\Scripts\BorderPlanLayer.cs') -Raw
$planShaderPath = Join-Path $unity 'Assets\AocMapStudio\Resources\AocBorderPlan.shader'
if (-not (Test-Path -LiteralPath $planShaderPath)) { throw 'Embedded border-plan shader is missing.' }
$planShader = Get-Content -LiteralPath $planShaderPath -Raw
$fillLayer = Get-Content -LiteralPath (Join-Path $unity 'Assets\AocMapStudio\Scripts\PoliticalFillPreviewLayer.cs') -Raw
$topologyDiagnostics = Get-Content -LiteralPath (Join-Path $unity 'Assets\AocMapStudio\Scripts\BorderTopologyDiagnostics.cs') -Raw
$editableBoundary = Get-Content -LiteralPath (Join-Path $unity 'Assets\AocMapStudio\Scripts\EditablePoliticalBoundary.cs') -Raw
$fillShaderPath = Join-Path $unity 'Assets\AocMapStudio\Resources\AocPoliticalFill.shader'
if (-not (Test-Path -LiteralPath $fillShaderPath)) { throw 'Embedded political-fill shader is missing.' }
$fillShader = Get-Content -LiteralPath $fillShaderPath -Raw
$riverLayer = Get-Content -LiteralPath (Join-Path $unity 'Assets\AocMapStudio\Scripts\RiverReferenceLayer.cs') -Raw
$riverShaderPath = Join-Path $unity 'Assets\AocMapStudio\Resources\AocRiver.shader'
if (-not (Test-Path -LiteralPath $riverShaderPath)) { throw 'Embedded exact-river shader is missing.' }
$riverShader = Get-Content -LiteralPath $riverShaderPath -Raw
$bootstrap = Get-Content -LiteralPath (Join-Path $unity 'Assets\AocMapStudio\Scripts\MapStudioBootstrap.cs') -Raw
$theme = Get-Content -LiteralPath (Join-Path $unity 'Assets\AocMapStudio\Scripts\MapStudioGuiTheme.cs') -Raw
$sync = Get-Content -LiteralPath (Join-Path $unity 'Sync-UnityMapPackage.ps1') -Raw
$interactive = Get-Content -LiteralPath (Join-Path $unity 'Assets\AocMapStudio\Scripts\GauntletInteractivePreview.cs') -Raw
$guideCutout = Get-Content -LiteralPath (Join-Path $unity 'Assets\AocMapStudio\Scripts\GauntletGuideCutout.cs') -Raw
$editHistory = Get-Content -LiteralPath (Join-Path $unity 'Assets\AocMapStudio\Scripts\GauntletEditHistory.cs') -Raw
$recoveryStore = Get-Content -LiteralPath (Join-Path $unity 'Assets\AocMapStudio\Scripts\GauntletRecoveryStore.cs') -Raw
$presetFactory = Get-Content -LiteralPath (Join-Path $unity 'Assets\AocMapStudio\Scripts\GauntletPresetFactory.cs') -Raw
$nodeRules = Get-Content -LiteralPath (Join-Path $unity 'Assets\AocMapStudio\Scripts\GauntletNodeRules.cs') -Raw
$projectPackage = Get-Content -LiteralPath (Join-Path $unity 'Assets\AocMapStudio\Scripts\GauntletProjectPackage.cs') -Raw
$livePreview = Get-Content -LiteralPath (Join-Path $unity 'Assets\AocMapStudio\Scripts\GauntletLivePreviewController.cs') -Raw
$assetBrowser = Get-Content -LiteralPath (Join-Path $unity 'Assets\AocMapStudio\Scripts\GauntletAssetBrowser.cs') -Raw
$uiImporter = Get-Content -LiteralPath (Join-Path $unity 'Assets\AocMapStudio\Scripts\GauntletUiImportBrowser.cs') -Raw
$projectSettings = Get-Content -LiteralPath (Join-Path $unity 'ProjectSettings\ProjectSettings.asset') -Raw
$previewBackground = Join-Path $unity 'Assets\StreamingAssets\LivePreview\campaign-map-base.jpg'
function Assert-Contains([string]$Source,[string]$Pattern,[string]$Message) {
    if ($Source -notmatch $Pattern) { throw $Message }
}
Assert-Contains $exporter 'Resolution = 2049' 'Unexpected Main_map sampling resolution.'
Assert-Contains $exporter 'SamplesPerFrame = 1024' 'Native sampling is not bounded safely.'
Assert-Contains $exporter 'WarmupFrames = 600' 'Main_map sampling has no initialization guard.'
Assert-Contains $exporter 'WorldMaximum = 1040f' 'Exact Bannerlord extent is absent.'
Assert-Contains $exporter 'GetTerrainHeight' 'Terrain does not come from the scene.'
Assert-Contains $exporter 'GetWaterLevelAtPosition' 'Local water is not exported.'
Assert-Contains $hostSource 'EXPORT UNITY MAP PACKAGE' 'Export control is absent.'
Assert-Contains $hostSource 'TerrainExporter\.IsPackageCurrent\(exportPath\)' 'Resolution-aware automatic export is absent.'
Assert-Contains $hostSource 'if \(TerrainExporter\.IsActive\)' 'Export does not isolate native scene sampling from preview caches.'
Assert-Contains $package 'bannerlord-world-v1' 'Exact coordinate validation is absent.'
Assert-Contains $package 'Only the exact NavalDLC/Main_map package is accepted' 'Other scenes are not rejected.'
Assert-Contains $factory 'heightmapResolution = resolution' 'Terrain resolution is not preserved.'
Assert-Contains $factory 'SetHeightsDelayLOD' 'Terrain reconstruction is absent.'
Assert-Contains $factory 'data\.terrainLayers = new\[\] \{ baseLayer \}' 'Terrain has no deterministic visible layer.'
Assert-Contains $factory 'CreateTerrainTexture' 'Terrain base texture generation is absent.'
Assert-Contains $factory 'data\.SetAlphamaps\(0, 0, basePaint\)' 'Terrain base layer is not painted visibly.'
Assert-Contains $factory 'terrain\.drawHeightmap = true' 'Terrain heightmap rendering is not explicitly enabled.'
Assert-Contains $bootstrap 'RenderSettings\.ambientMode = AmbientMode\.Flat' 'Standalone terrain has no ambient light.'
Assert-Contains $bootstrap 'LightType\.Directional' 'Standalone terrain has no directional light.'
Assert-Contains $bootstrap 'MinimumZoom = 0\.5f' 'Border-detail zoom floor is not configured.'
Assert-Contains $bootstrap 'MaximumZoom = 1200f' 'Whole-map zoom ceiling is not configured.'
Assert-Contains $bootstrap 'ZoomAtCursor\(wheel\)' 'Zoom is not cursor-centered.'
Assert-Contains $bootstrap 'before\.x - after\.x' 'Cursor anchor is not preserved while zooming.'
Assert-Contains $bootstrap 'MainMapSurfaceMeshFactory\.Create\(package\)' 'Exact visible mesh is not created.'
Assert-Contains $meshFactory 'CellsPerChunk = 128' 'Visible mesh is not split into bounded chunks.'
Assert-Contains $meshFactory 'package\.Heights\[sourceRow \* resolution \+ sourceColumn\]' 'Visible mesh does not use exact exported heights.'
Assert-Contains $meshFactory 'Resources\.Load<Shader>\("AocTerrain"\)' 'Visible mesh does not load its embedded shader.'
Assert-Contains $terrainShader 'Shader "AOC/MainMapSurface"' 'Embedded terrain shader contract is invalid.'
Assert-Contains $sync 'worldMaxX -ne 1040' 'Sync does not validate exact bounds.'
Assert-Contains $sync 'aoc-main-map-water-bodies-v1' 'Sync does not derive authoritative water metadata.'
Assert-Contains $sync 'waterBodies\.Count -ne 5' 'Water plane count is not validated.'
Assert-Contains $package 'waterBodies\.bodies\.Length != 5' 'Runtime does not validate water plane count.'
Assert-Contains $bootstrap 'MainMapWaterFactory\.Create\(package\)' 'Exact water bodies are not rendered.'
Assert-Contains $waterFactory 'body\.height \+ 0\.015f' 'Water elevation is not preserved.'
Assert-Contains $waterFactory 'Mathf\.Cos\(body\.rotationRadians\)' 'Local water rotation is not preserved.'
Assert-Contains $waterShader 'Shader "AOC/MainMapWater"' 'Embedded water shader contract is invalid.'
Assert-Contains $sync 'aoc-main-map-settlements-v1' 'Sync does not derive settlement references.'
Assert-Contains $sync 'settlements\.Count -lt 100' 'Settlement source completeness is not validated.'
Assert-Contains $package 'SampleHeight\(float worldX, float worldY\)' 'Settlement terrain projection is absent.'
Assert-Contains $bootstrap 'SettlementReferenceLayer\.Create\(package\)' 'Settlement layer is not created.'
Assert-Contains $bootstrap 'GUI\.Toggle' 'Settlement visibility controls are absent.'
Assert-Contains $settlementLayer 'package\.SampleHeight\(settlement\.x, settlement\.y\)' 'Settlement markers do not follow exact terrain.'
Assert-Contains $settlementLayer 'camera\.orthographicSize > 85f' 'Settlement labels have no zoom visibility guard.'
Assert-Contains $settlementShader 'Shader "AOC/MainMapSettlement"' 'Embedded settlement shader contract is invalid.'
Assert-Contains $settlementLayer 'material.SetFloat\("_Round", round \? 1f : 0f\)' 'Settlement shapes are not configured.'
Assert-Contains $bootstrap 'private bool _showVillages = true' 'Villages are not visible by default.'
Assert-Contains $sync 'aoc-main-map-generated-borders-v1' 'Sync does not derive generated border references.'
Assert-Contains $sync 'generatedBorders\.Count -ne 148' 'Generated border source count is not validated.'
Assert-Contains $sync 'river_triangles\.bin' 'Authoritative Main_map river geometry is not synchronized.'
Assert-Contains $sync 'ToInt32\(\$riverBytes, 0\) -ne 2210' 'River triangle count is not validated during synchronization.'
Assert-Contains $package 'MainMapRiverGeometry\.Load' 'Runtime does not load exact river geometry.'
Assert-Contains $package 'reader\.BaseStream\.Position != reader\.BaseStream\.Length' 'Runtime does not validate the entire river payload.'
Assert-Contains $package 'GridMaximum = 1024' 'River geometry is not calibrated to its authored grid.'
Assert-Contains $bootstrap 'RiverReferenceLayer\.Create\(package\)' 'Exact rivers are not rendered in the standalone editor.'
Assert-Contains $bootstrap '"Exact Rivers"' 'Exact river visibility control is absent.'
Assert-Contains $riverLayer 'package\.Rivers\.GridToWorld' 'River vertices are not mapped into exact Bannerlord coordinates.'
Assert-Contains $riverLayer 'package\.SampleHeight' 'River geometry does not follow terrain depth.'
Assert-Contains $riverShader 'Shader "AOC/ExactRiverReference"' 'Embedded exact-river shader contract is invalid.'
Assert-Contains $package 'generatedBorders\.borders\.Length != 148' 'Runtime generated border count is not validated.'
Assert-Contains $package 'SampleVisibleSurface\(float worldX, float worldY\)' 'Border surface projection is absent.'
Assert-Contains $package 'TrySampleWaterSurface\(float worldX, float worldY' 'Exact water probing is absent.'
Assert-Contains $generatedLayer 'SampleVisibleSurface\(point\.x, point\.y\)' 'Generated borders do not follow the visible terrain/water surface.'
Assert-Contains $generatedLayer 'Mathf\.Clamp\(source\.x, map\.worldMinX' 'Generated borders are not clipped to exact map bounds.'
Assert-Contains $bootstrap 'GeneratedBorderReferenceLayer\.Create\(package,\s*_fillPreview\.BoundaryPaths\)' 'Generated border reference layer does not consume the authoritative fill contour.'
Assert-Contains $bootstrap '"Generated Borders"' 'Generated border visibility control is absent.'
Assert-Contains $generatedShader 'Shader "AOC/GeneratedBorder"' 'Embedded generated-border shader contract is invalid.'
Assert-Contains $planning 'PanelRect\.Contains\(guiMouse\)' 'Planning clicks are not isolated from its UI panel.'
Assert-Contains $planning 'new Rect\(18f, 18f, 650f, 268f\)\.Contains\(guiMouse\)' 'Planning clicks are not isolated from the map controls.'
Assert-Contains $planning 'Input\.GetMouseButtonDown\(0\)' 'Line planning does not accept terrain clicks.'
Assert-Contains $planning 'Input\.GetMouseButtonDown\(1\)' 'Line planning has no direct confirmation input.'
Assert-Contains $planning 'DrawPointerCrosshair' 'Drawing crosshair feedback is absent.'
Assert-Contains $planning 'Confirm Current Plan' 'Line planning confirmation control is absent.'
Assert-Contains $planning 'SAVE BORDER PLAN  Ctrl\+S' 'Readable plan handoff control is absent.'
Assert-Contains $planning 'aoc-political-border-plan-v1' 'Plan persistence schema is absent.'
Assert-Contains $planning 'Environment\.SpecialFolder\.MyDocuments' 'Plan output is not placed in a user-accessible folder.'
Assert-Contains $planning 'coordinateMode = "bannerlord-world-v1"' 'Saved plans do not declare exact world coordinates.'
Assert-Contains $planning '_layer\.Rebuild\(_plans, displayed' 'Plan geometry is not cleared after state changes.'
Assert-Contains $planning '"2  COAST"' 'Automatic inward coast-follow control is absent.'
Assert-Contains $planning 'border\.coastal' 'Coast-follow does not restrict snapping to coastal chains.'
Assert-Contains $planning 'distance > 35f' 'Coast snapping has no bounded selection radius.'
Assert-Contains $planning 'BuildCoastPath' 'Coast-follow does not route along source coastline points.'
Assert-Contains $planning 'ProvinceSplitSnapRadius' 'Province split endpoint snapping is absent.'
Assert-Contains $planning 'settlement.kind != "town" && settlement.kind != "castle"' 'Province splits do not target towns and castles.'
Assert-Contains $planning 'CoastInteriorOffset = 3\.5f' 'Coast-follow has no explicit land-side inset.'
Assert-Contains $planning 'LandClearance\(left\) >= LandClearance\(right\)' 'Coast-follow does not choose the terrain-side interior.'
Assert-Contains $planning 'PlanningTool\.Edit' 'Plan editing tool is absent.'
Assert-Contains $planning 'BeginEdit\(Vector2 cursor, float pickRadius\)' 'Editable-anchor selection is absent.'
Assert-Contains $planning 'DragSelected\(Vector2 cursor\)' 'Direct anchor dragging is absent.'
Assert-Contains $planning 'BuildCoastPath\(plan\.CoastChain' 'Dragging a coastal endpoint does not reroute it.'
Assert-Contains $planLayer 'selected \? new Color\(1f, 0\.72f, 0\.05f\)' 'Selected anchors have no visible highlight.'
Assert-Contains $planLayer 'DashLength = 5f' 'Planning lines are not visibly dotted.'
Assert-Contains $planLayer 'AppendAnchor' 'Planning anchors are not rendered.'
Assert-Contains $planLayer 'if \(plan\.FollowsCoast\)' 'Committed coast samples are exposed as excessive control anchors.'
Assert-Contains $planLayer 'SampleVisibleSurface' 'Planning lines do not follow visible terrain/water.'
Assert-Contains $package 'IsTerrainSubmerged\(float worldX, float worldY\)' 'Submerged terrain detection is absent.'
Assert-Contains $planLayer 'new Color\(0\.05f, 0\.92f, 1f\)' 'Submerged plans have no visible warning color.'
Assert-Contains $generatedLayer 'new Color32\(13, 235, 255, 255\)' 'Submerged generated borders have no visible warning color.'
Assert-Contains $generatedLayer 'TrySelect\(Vector2 cursor, float pickRadius' 'Generated border node selection is absent.'
Assert-Contains $generatedLayer 'DistanceToSegmentSquared\(cursor, a, b, out amount\)' 'Generated borders cannot be selected by clicking their visible ribbon.'
Assert-Contains $generatedLayer 'SelectionDescription' 'Generated-border node selection has no legible UI feedback.'
Assert-Contains $generatedLayer 'BorderDisplayGeometry\.BuildNetwork\(' 'Generated ribbons do not consume the shared planar network.'
Assert-Contains $borderGeometry 'const int CurveSubdivisions = 3' 'Generated-border smoothing is not deterministic.'
Assert-Contains $borderGeometry 'BuildCornerRoundedPath' 'Generated borders lack corner-contained smoothing.'
Assert-Contains $borderGeometry 'HasSelfIntersection\(rounded, border\.closed\)' 'Rounded complex borders can introduce self-intersections.'
Assert-Contains $borderGeometry 'HasSelfIntersection\(conservative, border\.closed\)' 'Unsafe rounded geometry has no conservative topology-preserving fallback.'
Assert-Contains $borderGeometry 'PathsCross\(paths\[first\]' 'Separately rounded border chains can cross each other.'
Assert-Contains $borderGeometry '\? fallbackPaths\[first\] : originalPaths\[first\]' 'Crossing network curves have no staged authored-contour fallback.'
Assert-Contains $borderGeometry 'BuildCornerRoundedPath\(controls,\s*borders\[index\]\.closed, 0\.35f\)' 'Close interior chains have no conservative smooth fallback.'
Assert-Contains $borderGeometry 'fallbackLevel\[first\] < 2' 'Interior network smoothing still drops directly to a blocky authored chain.'
Assert-Contains $borderGeometry '!borders\[index\]\.coastal' 'Raw coastal fragments can still be rendered as overlapping chains.'
Assert-Contains $fillLayer 'BoundaryPaths = BuildBoundaryPaths\(owner, barrier\)' 'Visible coastal topology is not derived from the final political fill.'
Assert-Contains $fillLayer 'TraceBoundaryLoops\(filled\)' 'Political-fill coastlines are not traced as closed contour loops.'
Assert-Contains $generatedLayer 'AppendPoliticalBoundaryRibbons' 'Authoritative fill contours are not rendered as coastal ribbons.'
Assert-Contains $borderGeometry 'ConformInteriorEndpointsToBoundary' 'Interior border endpoints are not display-snapped to the fill contour.'
Assert-Contains $bootstrap 'RebuildFillAndBoundary\(\)' 'Political fill and visible coastline can be rebuilt independently and drift.'
Assert-Contains $topologyDiagnostics 'sourceCoastalCrossings' 'Topology diagnostics omit raw coastal segment crossings.'
Assert-Contains $topologyDiagnostics 'interiorNetworkCrossings' 'Topology diagnostics omit tangled interior border chains.'
Assert-Contains $topologyDiagnostics 'Dictionary<long, List<int>> cells' 'Tangle diagnostics still perform an unbounded all-pairs segment audit.'
Assert-Contains $topologyDiagnostics 'HashSet<long> testedPairs' 'Spatial tangle diagnostics can report the same crossing more than once.'
Assert-Contains $topologyDiagnostics 'contourSpikes' 'Topology diagnostics omit triangular arrowhead spikes.'
Assert-Contains $topologyDiagnostics '\[AOC BorderTangle\]' 'Topology diagnostics do not emit coordinate-level findings.'
Assert-Contains $topologyDiagnostics 'border-topology-diagnostics\.log' 'Topology diagnostics are not written to a user-readable file.'
Assert-Contains $editableBoundary 'TrySelect\(Vector2 cursor, float pickRadius,\s*Vector2\[\]\[\] displayPaths' 'Visible political contours cannot be selected from their rendered curve.'
Assert-Contains $editableBoundary 'FindNearestControlSegment\(path, visiblePoint' 'Visible contour clicks cannot map back to editable controls.'
Assert-Contains $editableBoundary 'expanded\.Insert\(insertionIndex, insertedPoint\)' 'Clicking a visible contour segment does not create a precise editable anchor.'
Assert-Contains $editableBoundary 'DragSelected\(Vector2 cursor, MainMapManifest map\)' 'Visible political-contour anchors cannot be dragged.'
Assert-Contains $editableBoundary 'DeleteSelectedPoint\(\)' 'Visible political-contour anchors cannot be deleted.'
Assert-Contains $generatedLayer 'if \(source\.deleted \|\| source\.coastal\) continue' 'Edit mode can still select hidden legacy coastal fragments.'
Assert-Contains $generatedLayer 'Vector2\[\] rendered = _displayPaths' 'Interior border hit-testing does not use the visible smooth curve.'
Assert-Contains $generatedLayer 'FindNearestSourceSegment\(border, bestRenderedPoint' 'Visible interior clicks cannot create editable source anchors.'
Assert-Contains $generatedLayer 'PoliticalBoundaryDisplayPaths' 'The live fill preview cannot consume the rendered visible contour.'
Assert-Contains $borderGeometry 'BuildSmoothClosedContour' 'Visible political contours remain raster-blocky.'
Assert-Contains $borderGeometry 'private struct PathSegment' 'Final-contour intersection diagnostics have no spatial segment representation.'
Assert-Contains $borderGeometry 'HashSet<long> testedPairs' 'Final-contour diagnostics still repeat or globally compare segment pairs.'
Assert-Contains $borderGeometry 'rounded\[index \* 2\] = Vector2\.Lerp\(first, second, 0\.25f\)' 'Visible contours do not use bounded corner-cutting curves.'
Assert-Contains $borderGeometry 'for \(int pass = 0; pass < 2; pass\+\+\)' 'Visible contour smoothing is not deterministic and bounded.'
Assert-Contains $planning 'politicalBoundaryContours = BuildPoliticalBoundaryContours\(\)' 'Visible contour edits are not persisted in border-plan output.'
Assert-Contains $planning 'RestorePoliticalBoundaryPaths\(contours\)' 'Saved visible contour edits are not restored.'
Assert-Contains $planning 'ApplyBoundaryMask' 'Contour dragging does not update the political fill preview.'
Assert-Contains $planning 'Time\.unscaledTime \+ 0\.12f' 'Live fill preview updates are not throttled for responsiveness.'
Assert-Contains $generatedLayer 'ComputeDisplayPathSignature\(\)' 'Planar border geometry is rebuilt unnecessarily on every zoom-width update.'
Assert-Contains $generatedLayer '_displayPaths == null \|\| signature != _displayPathSignature' 'Generated display-network caching does not invalidate after edits.'
Assert-Contains $generatedLayer 'CountSelectedNodeConnections\(\)' 'Generated junctions are not treated as one logical node.'
Assert-Contains $generatedLayer 'SetEditZoom\(float cameraZoom\)' 'Generated node handles do not adapt to camera zoom.'
Assert-Contains $generatedLayer '_editZoom \* 0\.035f' 'Generated node handles have no screen-space decluttering distance.'
Assert-Contains $generatedLayer '!endpoint && !shared && hasPreviousVisible' 'Generated node decluttering hides mandatory endpoints or junctions.'
Assert-Contains $planning '_generatedBorders\.SetEditZoom\(cameraZoom\)' 'The editor camera does not update generated-node detail.'
Assert-Contains $planning 'sqrMagnitude >= 16f' 'A simple node-selection click still mutates the border.'
Assert-Contains $planning 'Node selected — drag deliberately' 'Generated-node selection feedback is ambiguous.'
Assert-Contains $generatedLayer 'candidate\.x = targetX' 'Dragging a shared generated junction separates connected borders.'
Assert-Contains $generatedLayer 'AppendSharedJunctionCaps\(width' 'Connected generated ribbons can still show visual cracks at shared junctions.'
Assert-Contains $generatedLayer 'width \* 0\.68f' 'Shared junctions do not receive an overlap-safe round cap.'
Assert-Contains $generatedLayer 'if \(!endpoint\) continue' 'Dense curves still receive bead-forming overlap disks.'
Assert-Contains $borderGeometry 'SimplificationTolerance = 0\.65f' 'Raster-derived border noise is not simplified for rendering.'
Assert-Contains $borderGeometry 'CollectSimplifiedIndices\(' 'Generated borders lack deterministic centerline simplification.'
Assert-Contains $borderGeometry 'sharedNodeKeys\.Contains' 'Render simplification can discard shared junction nodes.'
Assert-Contains $generatedLayer 'BaseScreenWidthPixels = 2\.4f' 'Generated borders change apparent thickness with zoom.'
Assert-Contains $generatedLayer '_editZoom \* 2f / Mathf\.Max\(1, Screen\.height\)' 'Border width is not converted from screen pixels to world units.'
Assert-Contains $generatedLayer 'DragSelected\(Vector2 cursor\)' 'Generated border node dragging is absent.'
Assert-Contains $generatedLayer 'AlignCoastalBordersInland\(float inset\)' 'Generated coast calibration is absent.'
Assert-Contains $generatedLayer 'TryFindNearestCoastInland' 'Generated coast calibration does not search for the actual land/water transition.'
Assert-Contains $generatedLayer 'SearchRadius = 32f' 'Generated coast calibration has no bounded recovery radius.'
Assert-Contains $generatedLayer 'currentWater == previousWater' 'Generated coast calibration does not detect coastline crossings.'
Assert-Contains $planning 'generatedBorders = BuildGeneratedBorders\(\)' 'Edited generated borders are absent from saved handoff files.'
Assert-Contains $bootstrap '"Fill: OFF"' 'Political-fill preview control is absent.'
Assert-Contains $bootstrap '_fillPreview\.Rebuild\(\)' 'Political-fill preview cannot be refreshed after edits.'
Assert-Contains $fillLayer 'RasterSize = MainMapRiverGeometry\.GridMaximum \+ 1' 'Political fill is not aligned to the authoritative 1025-cell topology grid.'
Assert-Contains $fillLayer 'MarkGeneratedBarriers' 'Political fill is not constrained by edited borders.'
Assert-Contains $fillLayer 'BorderDisplayGeometry\.BuildNetwork\(' 'Political fill barriers can drift away from the visible border network.'
Assert-Contains $fillLayer 'IsTerrainSubmerged' 'Political fill does not exclude local water.'
Assert-Contains $fillLayer '_package\.IsRiver' 'Political fill does not exclude exact authored rivers.'
Assert-Contains $fillLayer 'Dictionary<string, int> factionOwners' 'Political fill ownership is not faction-stable.'
Assert-Contains $fillLayer 'SetGlobalTexture\("_AocPoliticalFillTex"' 'Political fill is not bound directly to the exact terrain surface.'
Assert-Contains $fillLayer 'SetGlobalFloat\("_AocPoliticalFillEnabled"' 'Political-fill visibility does not control terrain composition.'
Assert-Contains $terrainShader 'tex2D\(_AocPoliticalFillTex, input\.uv\)' 'Exact terrain does not render the political fill texture.'
Assert-Contains $terrainShader 'lerp\(terrain, political\.rgb, blend\)' 'Political fill is not composited without z-fighting.'
Assert-Contains $planning '"Delete Node"' 'Generated-border node deletion control is absent.'
Assert-Contains $planning '"Delete Border"' 'Generated-border deletion control is absent.'
Assert-Contains $generatedLayer 'DeleteSelectedPoint\(\)' 'Generated-border node deletion behavior is absent.'
Assert-Contains $generatedLayer 'terrainSampleSpacing' 'Map-boundary border endpoints are falsely reported as disconnected.'
Assert-Contains $generatedLayer 'DeleteSelectedBorder\(\)' 'Generated-border deletion behavior is absent.'
Assert-Contains $planning 'deleted = source\.deleted' 'Deleted generated borders are not persisted.'
Assert-Contains $planning 'TryLoadSavedPlan\(\)' 'Saved generated-border edits are not restored automatically.'
Assert-Contains $planning 'document\.generatedBorders\.Length != _package\.GeneratedBorders\.Length' 'Saved generated-border count is not validated.'
Assert-Contains $planning '_generatedBorders\.NotifyExternalEdits\(\)' 'Loaded generated borders are not rebuilt and connected.'
Assert-Contains $planning 'target\.deleted = record\.deleted' 'Saved generated-border deletions are not restored.'
Assert-Contains $bootstrap '"SMOOTH \+ CONNECT"' 'Generated-border smoothing control is absent.'
Assert-Contains $generatedLayer 'SmoothAndConnect\(float coastInset, float endpointTolerance\)' 'Constrained generated-border smoothing is absent.'
Assert-Contains $generatedLayer 'EnsureConnectedNetwork\(endpointTolerance\)' 'Generated-border smoothing does not enforce network connectivity.'
Assert-Contains $generatedLayer 'SnapEndpointToNetwork' 'Open-chain endpoints cannot snap to border segments.'
Assert-Contains $generatedLayer 'InsertJunction\(bestBorder, bestSegment, bestPoint\)' 'T-junction nodes are not inserted into target chains.'
Assert-Contains $generatedLayer 'DisconnectedEndpointCount' 'Unresolved border endpoints are not audited.'
Assert-Contains $generatedLayer 'DistanceToSegmentSquared\(cursor, a, b' 'Generated-border editing cannot select a visible ribbon segment.'
Assert-Contains $generatedLayer 'selectedBorder\.points\.Length' 'Selected generated chains do not expose their control nodes.'
Assert-Contains $planning '_generatedBorders\.FinishEdit\(\)' 'Dragged endpoints are not reconnected when editing ends.'
Assert-Contains $generatedLayer 'TryFindNearestCoastInland\(smoothed, coastInset' 'Smoothed coastal nodes are not reprojected inland.'
Assert-Contains $generatedLayer 'AppendRibbon\(border, displayPaths\[index\], width \* 1\.25f, true' 'Generated borders have no cartographic outer keyline.'
Assert-Contains $generatedLayer 'AppendRibbon\(border, displayPaths\[index\], width, false' 'Generated borders have no clean inner stroke.'
Assert-Contains $generatedLayer 'new Color32\(20, 14, 16, 255\)' 'Generated border keylines do not separate the stroke from terrain.'
Assert-Contains $theme 'internal static class MapStudioGuiTheme' 'The standalone editor has no reusable professional visual system.'
Assert-Contains $theme 'ActiveButton' 'Active editor tools have no distinct visual state.'
Assert-Contains $theme 'PrimaryButton' 'Primary editor actions have no clear visual hierarchy.'
Assert-Contains $bootstrap 'MapStudioGuiTheme\.Badge' 'Exact map-source status is not presented clearly.'
Assert-Contains $planning 'SAVE BORDER PLAN  Ctrl\+S' 'The professional planning inspector lacks shortcut guidance.'
Assert-Contains $generatedLayer 'incomingNormal \+ outgoingNormal' 'Continuous generated ribbons do not use shared corner joins.'
Assert-Contains $generatedLayer 'AppendDisk\(point, height, overlapRadius' 'Open chain ends do not receive round caps.'
Assert-Contains $fillLayer 'filterMode = FilterMode\.Bilinear' 'Political-fill display exposes topology pixels at curved boundaries.'
Assert-Contains $generatedLayer 'SubmergedCoastalPointCount = CountSubmergedCoastalPoints\(\)' 'Coastal smoothing has no visible post-operation water audit.'
Assert-Contains $generatedLayer 'result = source\[pointIndex\]' 'Failed coastal reprojection does not preserve the last valid land node.'
Assert-Contains $generatedLayer '&& _package\.IsTerrainSubmerged\(candidate\.x, candidate\.y\)' 'Endpoint joining can reconnect coastal chains through water.'
Assert-Contains $fillShader 'Shader "AOC/PoliticalFillPreview"' 'Embedded political-fill shader contract is invalid.'
Assert-Contains $planShader 'Shader "AOC/BorderPlan"' 'Embedded border-plan shader contract is invalid.'
Assert-Contains $bootstrap 'IEnumerator Start\(\)' 'Standalone startup does not yield a responsive first frame.'
Assert-Contains $bootstrap 'yield return null' 'Standalone startup does not paint before loading heavy map data.'
Assert-Contains $bootstrap 'LIVE UI PREVIEW' 'Internal UI editor entry point is absent.'
Assert-Contains $generatedLayer 'IsTopologyAuditPending' 'Deferred topology-audit state is absent.'
Assert-Contains $generatedLayer 'RunTopologyAudit\(\)' 'Explicit topology audit command is absent.'
Assert-Contains $livePreview 'aoc-gauntlet-preview-request-v1' 'Versioned exact-preview request contract is absent.'
Assert-Contains $livePreview 'campaign-map-base\.jpg' 'Campaign screenshot is not the map-stage base.'
Assert-Contains $livePreview 'private bool _blackStage;' 'Live preview no longer defaults to the screenshot map stage.'
Assert-Contains $livePreview '"MapBar"' 'MapBar is not the default UI editor prefab.'
Assert-Contains $livePreview 'GauntletInteractivePreview' 'Interactive prefab canvas is not connected.'
Assert-Contains $livePreview 'GauntletAssetBrowser' 'Internal asset browser is not connected.'
foreach ($token in @('WORLD EVENTS', 'DrawWorldEventsEditor',
        'OpenWorldEventsEditor', 'WorldEvents-Editable.xml',
        'FRESH APPROVED COPY', 'previous copy backed up')) {
    if (-not $livePreview.Contains($token)) { throw "World Events editor contract is missing: $token" }
}
Assert-Contains $livePreview 'UseShellExecute = true' 'Exact preview host does not launch without a command shell.'
Assert-Contains $interactive 'DtdProcessing = DtdProcessing\.Prohibit' 'Prefab XML parsing does not prohibit DTDs.'
foreach ($token in @('Canvas root — spawn below', '+ TEXT',
        '+ BUTTON', '+ IMAGE', '+ PANEL', 'DUPLICATE', 'EXPAND', 'DELETE',
        '+ MAP BAR UI', 'SELECT PARENT', 'AddMapBar', 'AocCustomMapBar',
        'selectedMapBar', 'IsDescendant', '_dragging ? 0.86f : 0.66f',
        'GUI.DrawTextureWithTexCoords', 'screenshotGhost',
        'DrawHierarchy', 'HandleKeyboard', 'ResizeSelected', 'SnapSelected',
        'HandleCanvasNavigation', '_canvasZoom', '_canvasPan',
        '_locked', '_hidden', 'SHOW ALL',
        'PositionXOffset', 'PositionYOffset', 'ApplyAsset',
        'Protected WorldCalendar.xml is read-only')) {
    if (-not $interactive.Contains($token)) { throw "Interactive UI editor contract is missing: $token" }
}
foreach ($token in @('FindConnectedDarkBackdrop',
        'ApplyCircularButtonMask', 'AocExactUiDragCutout')) {
    if (-not $guideCutout.Contains($token)) { throw "Exact guide-cutout contract is missing: $token" }
}
foreach ($token in @('TryUndo', 'TryRedo', 'private const int Limit = 100')) {
    if (-not $editHistory.Contains($token)) { throw "Edit-history contract is missing: $token" }
}
foreach ($token in @('autosave.xml', 'LoadIfNewer', 'DtdProcessing.Prohibit')) {
    if (-not $recoveryStore.Contains($token)) { throw "Recovery contract is missing: $token" }
}
foreach ($token in @('World Event Title', 'World Event Option',
        'World Event Artwork', 'MapBar Button', 'MapBar Counter')) {
    if (-not $presetFactory.Contains($token)) { throw "Component-preset contract is missing: $token" }
}
foreach ($token in @('HintWidget', 'NavigationScope', 'IsVisual')) {
    if (-not $nodeRules.Contains($token)) { throw "Visible-node selection contract is missing: $token" }
}
if ($interactive.Contains('new Color(0.75f, 0.9f, 0.94f')) {
    throw 'Cyan widget hover boxes are still rendered in the live editor.'
}
foreach ($token in @('IMPORT UI PREFAB', 'IMPORT PATH', 'IMPORT SELECTED',
        'Modules', 'GUI', 'Prefabs', 'DtdProcessing = DtdProcessing.Prohibit',
        'The file is not a Gauntlet Prefab with a Window.',
        'AOC Political Map Studio", "UI Prefabs', '.aocui',
        'GauntletProjectPackage.Import')) {
    if (-not $uiImporter.Contains($token)) { throw "UI import-browser contract is missing: $token" }
}
foreach ($token in @('ZipArchiveMode.Create', 'prefab.xml', 'manifest.txt',
        'unsafe path', 'CollectDependencies', 'CreateEntryFromFile')) {
    if (-not $projectPackage.Contains($token)) { throw "Portable UI project contract is missing: $token" }
}
foreach ($token in @('AssetPackages', '*.tpac', 'IMPORT', 'EXPORT',
        'WorkspaceDirectory', 'File.Copy(source, target, false)',
        'LoadThumbnail', 'SPRITE", "BRUSH", "TPAC')) {
    if (-not $assetBrowser.Contains($token)) { throw "Asset browser contract is missing: $token" }
}
Assert-Contains $projectSettings 'companyName: Ages of Calradia' 'Standalone company branding is incorrect.'
Assert-Contains $projectSettings 'productName: AOC Political Map Studio' 'Standalone product branding is incorrect.'
Assert-Contains $projectSettings 'm_ShowUnitySplashScreen: 0' 'Unity splash screen is still enabled.'
if (-not (Test-Path -LiteralPath $previewBackground)) { throw 'Campaign live-preview background is missing.' }
Add-Type -AssemblyName System.Drawing
$previewImage = [System.Drawing.Image]::FromFile($previewBackground)
try {
    if ($previewImage.Width -ne 1920 -or $previewImage.Height -ne 1080) {
        throw 'Campaign live-preview background must be exactly 1920x1080.'
    }
} finally { $previewImage.Dispose() }
$version = Get-Content -LiteralPath (Join-Path $unity 'ProjectSettings\ProjectVersion.txt') -Raw
Assert-Contains $version '6000\.5\.10f1' 'Unity version is not pinned.'
Write-Output 'PASS: Unity exact Main_map export and reconstruction contracts.'
