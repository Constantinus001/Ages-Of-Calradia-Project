param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\tmp\campaign-label-visibility\bin\Win64_Shipping_Client\AgesOfCalradia.CampaignLabelVisibility.dll'),
    [string]$SourcePath = (Join-Path $PSScriptRoot '..\CampaignLabelVisibilitySubModule.cs')
)

$ErrorActionPreference = 'Stop'
$source = Get-Content -Raw -LiteralPath $SourcePath
if ($source -notmatch 'PoliticalOverviewStartAltitude = 580f' -or
    $source -notmatch 'SettlementNameplateVM' -or
    $source -notmatch 'UpdateNameplateMT' -or
    $source -notmatch 'bool ____isTown' -or
    $source -notmatch 'cameraPosition\.z >= PoliticalOverviewStartAltitude && ____isTown' -or
    $source -notmatch '____bindIsVisibleOnMap = false' -or
    $source -match 'CampaignMapMaximumZoomPatch|MaximumCameraHeight|ExtendedMaximumCameraHeightMultiplier') {
    throw 'Campaign label visibility contract failed: political-overview town visibility is incomplete.'
}

if ($source -match 'CampaignPoliticalTerritoryFill|CampaignKingdomBorderBehavior|CampaignMapTerrainGridCache') {
    throw 'Campaign label visibility contract failed: isolated component references political rendering.'
}
if (-not (Test-Path -LiteralPath $AssemblyPath)) {
    throw "Campaign label visibility assembly is missing: $AssemblyPath"
}
$strings = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($AssemblyPath))
if ($strings -notmatch 'CampaignLabelVisibilitySubModule' -or
    $strings -notmatch 'SettlementNameplateZoomPatch' -or
    $strings -match 'CampaignMapMaximumZoomPatch') {
    throw 'Campaign label visibility contract failed: compiled patch types are absent.'
}

Write-Host 'Campaign-map political-overview town visibility verification passed; native zoom cap retained.'
