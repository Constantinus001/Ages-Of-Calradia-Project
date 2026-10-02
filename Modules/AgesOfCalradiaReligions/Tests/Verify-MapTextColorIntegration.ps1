$ErrorActionPreference = 'Stop'
$moduleRoot = Split-Path -Parent $PSScriptRoot
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $moduleRoot)
$source = Get-Content -LiteralPath (Join-Path $moduleRoot 'MapOverlayTextColorIntegration.cs') -Raw
$project = Get-Content -LiteralPath (Join-Path $moduleRoot 'AgesOfCalradiaReligions.csproj') -Raw
$submodule = Get-Content -LiteralPath (Join-Path $moduleRoot 'ReligionSubModule.cs') -Raw
$manifest = [xml](Get-Content -LiteralPath (Join-Path $moduleRoot 'SubModule.xml') -Raw)

if ($manifest.Module.Version.value -ne 'v0.10.0') { throw 'Unexpected religion map-text and Shipwright version.' }
if ($project -notmatch 'MapOverlayTextColorIntegration.cs') { throw 'Map-text integration is not compiled.' }
if ($submodule -notmatch 'MapOverlayTextColorIntegration.Install' -or $submodule -notmatch 'MapOverlayTextColorIntegration.Reset') {
    throw 'Map-text integration lifecycle is incomplete.'
}
foreach ($token in @('StrategicMapCanvas','CalendarStrategicKingdomLabelVM','CalendarStrategicFriendlyArmyVM',
    'CampaignPoliticalOverlayView','GetAllChildrenOfTypeRecursive<TextWidget>','Color.Black',
    'Brush privateBrush = brush.Clone()','privateBrush.FontColor = Color.Black',
    'PoliticalOutlineAmount','PoliticalOutlineDarkenFactor','TextOutlineColor',
    'DarkenKingdomColor','privateBrush.DefaultStyle.FontColor = Color.Black',
    'CorrectCoastalPoliticalLabelAnchors','NeedsLandAnchor','anchorSettlement',
    'renderedBrushBlack','missingBrushes','DIAGNOSTIC FAILURE','[MAPTEXT]')) {
    if ($source -notmatch [regex]::Escape($token)) { throw "Map-text integration is missing: $token" }
}
if ($source -match 'WorldCalendar.xml|PoliticalKingdomLabel.xml|CampaignKingdomBorderBehavior|CampaignPoliticalTerritoryFill') {
    throw 'Map-text integration references a protected prefab or border renderer.'
}

& (Join-Path $repositoryRoot 'Tests\Verify-ProtectedPoliticalBaseline.ps1')
Write-Host 'Black map-overlay text sidecar verification passed; runtime diagnostics are present and protected artifacts remain approved.'
