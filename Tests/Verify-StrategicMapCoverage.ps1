param(
    [string]$ModuleRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$BannerlordDir = 'C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord'
)

$ErrorActionPreference = 'Stop'

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
}

$layoutPath = Join-Path $ModuleRoot 'CalendarStrategicMapLayout.cs'
$ledgerPath = Join-Path $ModuleRoot 'CalendarWorldLedgerVM.cs'
$fixedLayerPath = Join-Path $ModuleRoot 'CalendarWorldLedgerFixedStrategicLayers.cs'
$worldCalendarPrefabPath = Join-Path $ModuleRoot 'GUI\Prefabs\WorldCalendar\WorldCalendar.xml'
$spriteDataPath = Join-Path $ModuleRoot 'GUI\RealisticCalendarTweaksSpriteData.xml'
$provinceDirectory = Join-Path $ModuleRoot 'GUI\SpriteParts\ui_world_calendar'
$provinceIndexPath = Join-Path $provinceDirectory 'strategic_province_index.png'
$territoryIndexPath = Join-Path $provinceDirectory 'strategic_settlement_index.png'
$territoryManifestPath = Join-Path $provinceDirectory 'strategic_settlement_index_manifest.txt'
$textureProviderPath = Join-Path $ModuleRoot 'CalendarStrategicMapTextureProvider.cs'
$townMarkerPath = Join-Path $provinceDirectory 'strategic_marker_town.png'
$castleMarkerPath = Join-Path $provinceDirectory 'strategic_marker_castle.png'
$projectPath = Join-Path $ModuleRoot 'TwelveMonthCalendar.csproj'

$layoutSource = Get-Content -LiteralPath $layoutPath -Raw
$bindings = @(
    [regex]::Matches($layoutSource, '\{ "(strategic_province_\d{3})", "([^"]+)" \}') |
        ForEach-Object {
            [pscustomobject]@{
                SpriteName = $_.Groups[1].Value
                SettlementId = $_.Groups[2].Value
            }
        }
)

Assert-True ($bindings.Count -eq 133) "Expected 133 strategic province bindings; found $($bindings.Count)."
Assert-True ((@($bindings.SpriteName | Sort-Object -Unique).Count) -eq 133) 'Strategic province sprite names are not unique.'
Assert-True ((@($bindings.SettlementId | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count) -eq 0) 'A strategic province is missing its local ownership binding.'
# The static province table is retained only for disabled legacy layers.
# Several source-map regions contain more than one live settlement, so the
# generated settlement index/manifest is checked as an authoring asset below;
# the active composer intentionally uses the original province index instead
# (see its explicit source assertions below), not the generated split index.

[xml]$spriteData = Get-Content -LiteralPath $spriteDataPath
$provinceParts = @($spriteData.SpriteData.SpriteParts.SpritePart | Where-Object { $_.Name -match '^strategic_province_\d{3}$' })
$provinceSprites = @($spriteData.SpriteData.Sprites.GenericSprite | Where-Object { $_.Name -match '^strategic_province_\d{3}$' })
$borderPart = @($spriteData.SpriteData.SpriteParts.SpritePart | Where-Object { $_.Name -eq 'strategic_province_borders' })
$borderSprite = @($spriteData.SpriteData.Sprites.GenericSprite | Where-Object { $_.Name -eq 'strategic_province_borders' })
$markerParts = @($spriteData.SpriteData.SpriteParts.SpritePart | Where-Object { $_.Name -match '^strategic_marker_(town|castle)$' })
$markerSprites = @($spriteData.SpriteData.Sprites.GenericSprite | Where-Object { $_.Name -match '^strategic_marker_(town|castle)$' })

Assert-True ($provinceParts.Count -eq 133) "Expected 133 province sprite parts; found $($provinceParts.Count)."
Assert-True ($provinceSprites.Count -eq 133) "Expected 133 generic province sprites; found $($provinceSprites.Count)."
Assert-True ($borderPart.Count -eq 1 -and [int]$borderPart[0].Width -eq 1024 -and [int]$borderPart[0].Height -eq 948) 'The transparent province-border overlay is missing or has the wrong dimensions.'
Assert-True ($borderSprite.Count -eq 1) 'The transparent province-border overlay is not registered as a sprite.'
Assert-True ($markerParts.Count -eq 2 -and $markerSprites.Count -eq 2) 'Town and castle markers must be registered as atlas sprites.'
Assert-True ((@($markerParts.Name | Sort-Object) -join ',') -eq 'strategic_marker_castle,strategic_marker_town') 'The Strategic Map marker sprite names are incomplete.'
Assert-True ((@($markerSprites.Name | Sort-Object) -join ',') -eq 'strategic_marker_castle,strategic_marker_town') 'The Strategic Map marker generic sprites are incomplete.'
$spriteDataRaw = Get-Content -LiteralPath $spriteDataPath -Raw
Assert-True ($spriteDataRaw -match '<Name>strategic_marker_town</Name>\s*<Width>96</Width>\s*<Height>96</Height>\s*<SheetX>4</SheetX>\s*<SheetY>1732</SheetY>') 'The town marker is not located in its reserved alpha-safe atlas slot.'
Assert-True ($spriteDataRaw -match '<Name>strategic_marker_castle</Name>\s*<Width>96</Width>\s*<Height>96</Height>\s*<SheetX>388</SheetX>\s*<SheetY>1732</SheetY>') 'The castle marker is not located in its reserved alpha-safe atlas slot.'

$spritePartNames = @($provinceParts.Name | Sort-Object -Unique)
$bindingNames = @($bindings.SpriteName | Sort-Object -Unique)
Assert-True ((Compare-Object $spritePartNames $bindingNames).Count -eq 0) 'Province sprite definitions and code bindings differ.'
Assert-True ((Compare-Object @($provinceSprites.Name | Sort-Object -Unique) $bindingNames).Count -eq 0) 'Generic province sprites and code bindings differ.'

$allUiParts = @($spriteData.SpriteData.SpriteParts.SpritePart | Where-Object { $_.CategoryName -eq 'ui_world_calendar' })
for ($left = 0; $left -lt $allUiParts.Count; $left++) {
    for ($right = $left + 1; $right -lt $allUiParts.Count; $right++) {
        $a = $allUiParts[$left]
        $b = $allUiParts[$right]
        $overlaps = [int]$a.SheetX -lt ([int]$b.SheetX + [int]$b.Width) -and
            ([int]$a.SheetX + [int]$a.Width) -gt [int]$b.SheetX -and
            [int]$a.SheetY -lt ([int]$b.SheetY + [int]$b.Height) -and
            ([int]$a.SheetY + [int]$a.Height) -gt [int]$b.SheetY
        Assert-True (-not $overlaps) "UI atlas parts overlap: $($a.Name) and $($b.Name)."
    }
}

Add-Type -AssemblyName System.Drawing
if (-not ('StrategicMapPixelChecks' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'StrategicMapPixelChecks.cs') -ReferencedAssemblies System.Drawing
}
foreach ($part in $provinceParts) {
    $maskPath = Join-Path $provinceDirectory ($part.Name + '.png')
    Assert-True (Test-Path -LiteralPath $maskPath) "Missing province mask: $maskPath"
    $image = [System.Drawing.Image]::FromFile($maskPath)
    try {
        Assert-True ($image.Width -eq [int]$part.Width -and $image.Height -eq [int]$part.Height) "Mask dimensions differ from sprite data: $($part.Name)."
    }
    finally {
        $image.Dispose()
    }
}

# The game samples the packed atlas, not the loose PNGs. Verify every source
# mask was copied into the precise atlas coordinates declared by SpriteData.
$atlasPath = Join-Path $ModuleRoot 'AssetSources\GauntletUI\ui_world_calendar_1.png'
$atlas = [System.Drawing.Bitmap]::FromFile($atlasPath)
try {
    foreach ($part in $provinceParts) {
        $maskPath = Join-Path $provinceDirectory ($part.Name + '.png')
        $mask = [System.Drawing.Bitmap]::FromFile($maskPath)
        try {
            [StrategicMapPixelChecks]::Atlas($atlas, $mask, [int]$part.SheetX, [int]$part.SheetY, [string]$part.Name)
        }
        finally { $mask.Dispose() }
    }
    foreach ($markerPart in $markerParts) {
        $markerPath = Join-Path $provinceDirectory ($markerPart.Name + '.png')
        $marker = [System.Drawing.Bitmap]::FromFile($markerPath)
        try {
            [StrategicMapPixelChecks]::Atlas($atlas, $marker, [int]$markerPart.SheetX, [int]$markerPart.SheetY, [string]$markerPart.Name)
        }
        finally { $marker.Dispose() }
    }
}
finally { $atlas.Dispose() }

# Source masks must cover the bright map interiors; the base map supplies the
# black outlines, so a small transparent border allowance is intentional.
$baseMapPath = Join-Path $provinceDirectory 'strategic_map.png'
$baseMap = [System.Drawing.Bitmap]::FromFile($baseMapPath)
$coverageMap = New-Object System.Drawing.Bitmap($baseMap.Width, $baseMap.Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$coverageGraphics = [System.Drawing.Graphics]::FromImage($coverageMap)
try {
    $provinceDefinitions = [regex]::Matches($layoutSource, 'new CalendarStrategicProvinceDefinition\("(strategic_province_\d{3})", (\d+), (\d+),')
    foreach ($definition in $provinceDefinitions) {
        $maskPath = Join-Path $provinceDirectory ($definition.Groups[1].Value + '.png')
        $mask = [System.Drawing.Image]::FromFile($maskPath)
        try { $coverageGraphics.DrawImageUnscaled($mask, ([int]$definition.Groups[2].Value - 80), ([int]$definition.Groups[3].Value - 90)) }
        finally { $mask.Dispose() }
    }
    $coverageCounts = [StrategicMapPixelChecks]::Coverage($baseMap, $coverageMap)
    $landPixels = $coverageCounts[0]
    $coveredLandPixels = $coverageCounts[1]
    Assert-True (($coveredLandPixels / [double]$landPixels) -ge 0.975) "Province mask coverage is too low: $coveredLandPixels of $landPixels bright land pixels."
}
finally {
    $coverageGraphics.Dispose()
    $coverageMap.Dispose()
    $baseMap.Dispose()
}

# The active renderer consumes one precomputed province-index texture. Every
# transparent land pixel must name exactly one province, while opaque water and
# black border pixels must remain untouched for the composed texture to keep
# the province outlines on top of faction colour.
Assert-True (Test-Path -LiteralPath $provinceIndexPath) 'The composed Strategic Map province-index texture is missing.'
$indexMap = [System.Drawing.Bitmap]::FromFile($provinceIndexPath)
$baseMap = [System.Drawing.Bitmap]::FromFile($baseMapPath)
try {
    Assert-True ($indexMap.Width -eq $baseMap.Width -and $indexMap.Height -eq $baseMap.Height) 'The province-index texture dimensions differ from the base map.'
    [StrategicMapPixelChecks]::Index($baseMap, $indexMap, $false) | Out-Null
}
finally {
    $indexMap.Dispose()
    $baseMap.Dispose()
}

$settlementFiles = @(
    Join-Path $BannerlordDir 'Modules\SandBox\ModuleData\settlements.xml'
    Join-Path $BannerlordDir 'Modules\NavalDLC\ModuleData\settlements.xml'
) | Where-Object { Test-Path -LiteralPath $_ }
Assert-True ($settlementFiles.Count -gt 0) 'Could not locate campaign settlement data.'

$campaignSettlementIds = New-Object System.Collections.Generic.HashSet[string]
foreach ($settlementFile in $settlementFiles) {
    [xml]$settlementsXml = Get-Content -LiteralPath $settlementFile
    foreach ($settlement in @($settlementsXml.Settlements.Settlement | Where-Object { $null -ne $_.Components.Town })) {
        [void]$campaignSettlementIds.Add([string]$settlement.id)
    }
}

Assert-True ($campaignSettlementIds.Count -eq 133) "Expected 133 campaign towns/castles; found $($campaignSettlementIds.Count)."

Assert-True (Test-Path -LiteralPath $territoryManifestPath) 'The live settlement-territory manifest is missing.'
$territoryManifestEntries = @(
    Get-Content -LiteralPath $territoryManifestPath |
        Where-Object { $_ -match '^\s*(\d+)=([^=\s]+)\s*$' } |
        ForEach-Object {
            $match = [regex]::Match($_, '^\s*(\d+)=([^=\s]+)\s*$')
            [pscustomobject]@{
                Index = [int]$match.Groups[1].Value
                SettlementId = $match.Groups[2].Value
            }
        }
)
Assert-True ($territoryManifestEntries.Count -eq 133) 'The live settlement-territory manifest must contain exactly 133 entries.'
Assert-True ((@($territoryManifestEntries.Index | Sort-Object -Unique).Count) -eq 133) 'Settlement-territory manifest indexes are not unique.'
Assert-True ((@($territoryManifestEntries.Index | Where-Object { $_ -lt 1 -or $_ -gt 133 }).Count) -eq 0) 'Settlement-territory manifest indexes must be 1 through 133.'
$territorySettlementIds = @($territoryManifestEntries.SettlementId | Sort-Object -Unique)
Assert-True ($territorySettlementIds.Count -eq 133) 'Settlement-territory manifest settlement ids are not unique.'
Assert-True ((Compare-Object @($campaignSettlementIds | Sort-Object) $territorySettlementIds).Count -eq 0) 'The live settlement-territory manifest must cover every campaign town and castle exactly once.'

Assert-True (Test-Path -LiteralPath $territoryIndexPath) 'The live settlement-territory index is missing.'
$territoryMap = [System.Drawing.Bitmap]::FromFile($territoryIndexPath)
$baseMap = [System.Drawing.Bitmap]::FromFile($baseMapPath)
try {
    Assert-True ($territoryMap.Width -eq $baseMap.Width -and $territoryMap.Height -eq $baseMap.Height) 'The settlement-territory index dimensions differ from the base map.'
    $syntheticBorderPixels = [StrategicMapPixelChecks]::Index($baseMap, $territoryMap, $true)
    Assert-True ($syntheticBorderPixels -gt 0) 'Settlement-territory index is missing the internal split borders for shared source provinces.'
}
finally {
    $territoryMap.Dispose()
    $baseMap.Dispose()
}

$ledgerSource = Get-Content -LiteralPath $ledgerPath -Raw
$markerAnchorIds = @(
    [regex]::Matches($ledgerSource, '\{ "([^"]+)", new Vec2\(') |
        ForEach-Object { $_.Groups[1].Value } |
        Sort-Object -Unique
)
Assert-True ($markerAnchorIds.Count -eq 133) "Expected 133 strategic settlement marker anchors; found $($markerAnchorIds.Count)."
Assert-True ((Compare-Object @($campaignSettlementIds | Sort-Object) $markerAnchorIds).Count -eq 0) 'Campaign towns/castles and strategic marker anchors differ.'
Assert-True ($ledgerSource -notmatch 'ResolveTrackedSettlementOwner') 'The strategic map still contains the obsolete tracker-owner fallback.'
Assert-True ($ledgerSource -match 'BuildStrategicMarkers\(markerPoints\)') 'The strategic map does not build its live settlement-marker layer.'
Assert-True ($ledgerSource -match 'ResolveStrategicMarkerSpacing\(markerPoints\)') 'The Strategic Map does not resolve crowded settlement markers.'
Assert-True ($ledgerSource -match 'StrategicMarkerMinimumSeparation = 52f') 'The Strategic Map marker spacing is too small to prevent overlap while zoomed.'
Assert-True ($ledgerSource -match 'point\.DisplayX') 'The Strategic Map hit targets do not use the resolved marker positions.'
Assert-True ($ledgerSource -match 'CalendarWorldLedgerBehavior\.GetLiveSettlementFaction\(settlement\)') 'The strategic map is not reading live settlement owners.'
Assert-True ($ledgerSource -match 'settlement\.Village != null') 'The strategic map does not collect live village locations.'
Assert-True ($ledgerSource -match 'rgba\.Substring\(0, 6\) \+ "FF"') 'Strategic province fills must be opaque so the ownership regions are readable.'
Assert-True ($ledgerSource -match 'settlement\.IsUnderSiege') 'The strategic map does not detect live settlement sieges.'
Assert-True ($ledgerSource -match 'settlement\.SiegeEvent\.BesiegerCamp\.MapFaction') 'The strategic map does not resolve the besieging faction for contested provinces.'
Assert-True ($ledgerSource -match 'BuildStrategicContestedProvinces\(besiegersBySettlementId\)') 'The strategic map does not build the live siege-only province overlay.'
Assert-True ($ledgerSource -match 'Besieger = besieger') 'The strategic map does not retain the besieging faction for composed occupation rendering.'
$ledgerBehaviorPath = Join-Path $ModuleRoot 'CalendarWorldLedgerBehavior.cs'
$ledgerBehaviorSource = Get-Content -LiteralPath $ledgerBehaviorPath -Raw
Assert-True ($ledgerBehaviorSource -match 'private void OnDailyTick\(\)[\s\S]*?_ownershipRevision\+\+;') 'The open Strategic Map is not refreshed daily for siege state changes.'
Assert-True ($ledgerBehaviorSource -match 'GetLiveSettlementFaction\(Settlement settlement\)') 'The settlement ownership resolver is missing.'
Assert-True ($ledgerBehaviorSource -match 'settlement\.Town\.MapFaction') "The ownership resolver is not using Bannerlord's live town/castle map faction."
Assert-True ($ledgerBehaviorSource -match 'settlement\.MapFaction') "The ownership resolver is not using Bannerlord's live settlement map faction."

$worldCalendarPrefab = Get-Content -LiteralPath $worldCalendarPrefabPath -Raw
Assert-True ($worldCalendarPrefab -match 'DataSource="\{StrategicMarkers\}"') 'The World Calendar prefab does not render the live settlement-marker layer.'
Assert-True ($worldCalendarPrefab -match '<TextureWidget[^>]*TextureProviderName="CalendarStrategicCampaignAtlasTextureProvider"') 'The World Calendar prefab is not using the composed Strategic Map texture provider.'
Assert-True ($worldCalendarPrefab -match '<Widget IsVisible="false">[\s\S]*?DataSource="\{StrategicProvince001\}"') 'The legacy per-province widgets were not disabled.'
Assert-True (Test-Path -LiteralPath $textureProviderPath) 'The composed Strategic Map texture provider source is missing.'
$textureProviderSource = Get-Content -LiteralPath $textureProviderPath -Raw
Assert-True ($textureProviderSource -match 'class CalendarStrategicCampaignAtlasTextureProvider : TextureProvider') 'The Strategic Map composer is not a Gauntlet texture provider.'
Assert-True ($textureProviderSource -match 'strategic_province_index\.png') 'The Strategic Map composer does not load the original province-index texture.'
Assert-True ($textureProviderSource -match 'BuildSettlementIdsFromProvinceLayout') 'The Strategic Map composer is not binding original provinces to their established settlement layout.'
Assert-True ($textureProviderSource -match 'EngineTexture\.CreateFromMemory') 'The Strategic Map composer does not create a runtime texture.'
Assert-True ($textureProviderSource -match 'OwnerColorsBySettlementId') 'The Strategic Map composer does not track live owner colours by settlement id.'
Assert-True ($textureProviderSource -match 'ResolveProvinceOwnerColors' -and $textureProviderSource -match 'resolvedProvinceColors\[territory - 1\]') 'The Strategic Map composer does not map settlement territories to resolved live owner colours.'
Assert-True ($textureProviderSource -match 'bySettlementId\.TryGetValue\(_settlementIds\[province\]') 'Contested provinces are not bound to their exact besieged settlement.'
Assert-True ($textureProviderSource -notmatch 'strategic_settlement_index\.png') 'The Strategic Map composer is still using the inaccurate generated settlement-split index.'
Assert-True ($textureProviderSource -match 'UpdateMapState') 'The Strategic Map composer does not receive the live settlement-marker layout.'
Assert-True ($textureProviderSource -match 'DrawSettlementMarkers') 'The Strategic Map composer does not draw settlement markers into its composed texture.'
Assert-True ($textureProviderSource -match 'DrawVillageMarkers') 'The Strategic Map composer does not draw villages as map dots.'
Assert-True ($textureProviderSource -match 'StrategicVillageSnapshot') 'The Strategic Map composer does not retain a stable village snapshot across refreshes.'
Assert-True ($textureProviderSource -match 'StrategicMarkerSnapshot') 'The Strategic Map composer does not retain a stable marker snapshot across refreshes.'
Assert-True ($textureProviderSource -match 'DrawTownMarker') 'The Strategic Map town marker does not use the detailed town silhouette.'
Assert-True ($textureProviderSource -match 'DrawCastleMarker') 'The Strategic Map castle marker does not use the detailed castle silhouette.'
Assert-True ($textureProviderSource -match 'Color\.FromArgb\(255, 183, 136, 68\)') 'The Strategic Map town/castle markers are not filled muted bronze.'
Assert-True ($textureProviderSource -match 'DrawSiegeBadge') 'The Strategic Map does not render an explicit under-siege badge.'
Assert-True ($textureProviderSource -match 'ResolveContestedProvinceColors') 'The Strategic Map composer does not resolve contested regions from live siege data.'
Assert-True ($textureProviderSource -match 'attackerStripe') 'The Strategic Map composer does not render EU4-style alternating occupation stripes.'
Assert-True ($textureProviderSource -match 'ExpandTerritoriesAcrossPaleLand') 'The Strategic Map composer does not eliminate pale seams around province masks.'
Assert-True ($textureProviderSource -match 'point\.DisplayX') 'The composed Strategic Map does not use the resolved marker positions.'
foreach ($markerPath in @($townMarkerPath, $castleMarkerPath)) {
    Assert-True (Test-Path -LiteralPath $markerPath) "Strategic Map marker art is missing: $markerPath"
    $markerImage = [System.Drawing.Bitmap]::FromFile($markerPath)
    try {
        Assert-True ($markerImage.Width -eq 96 -and $markerImage.Height -eq 96) "Marker icon dimensions must be 96x96: $markerPath"
        Assert-True ($markerImage.GetPixel(0, 0).A -eq 0 -and $markerImage.GetPixel(95, 95).A -eq 0) "Marker icon background must be transparent: $markerPath"
    }
    finally {
        $markerImage.Dispose()
    }
}
$projectSource = Get-Content -LiteralPath $projectPath -Raw
Assert-True ($projectSource -match 'System.Drawing') 'The project is missing the System.Drawing reference required by the Strategic Map composer.'
Assert-True ($projectSource -match 'CalendarStrategicMapTextureProvider\.cs') 'The project does not compile the Strategic Map composer.'
Assert-True ($projectSource -notmatch 'CalendarStrategicMarkerTextureProviders\.cs') 'The unsafe direct marker texture providers must not be compiled.'
Assert-True ($worldCalendarPrefab -notmatch 'Sprite="strategic_city_labels"') 'The strategic map must not render the red static city-name artwork; markers are the clickable settlement signal.'
Assert-True ($worldCalendarPrefab -match 'DataSource="\{StrategicMarkers\}"[\s\S]*?Command\.Click="ExecuteSelect"') 'Strategic settlement markers must expose a click action.'
Assert-True ($worldCalendarPrefab -notmatch 'Sprite="strategic_marker_(town|castle)"') 'The broken atlas-backed marker sprites are still rendered by the Strategic Map.'
Assert-True ($worldCalendarPrefab -match 'Text="MAP LEGEND"') 'The Strategic Map legend is missing.'
$markerButton = [regex]::Match($worldCalendarPrefab, '<ButtonWidget(?=[^>]*PositionXOffset="@X")(?=[^>]*PositionYOffset="@Y")(?=[^>]*Command\.Click="ExecuteSelect")[^>]*>')
Assert-True $markerButton.Success 'The transparent settlement-marker hit target is missing.'
Assert-True ($markerButton.Value -notmatch 'Sprite=') 'The old coloured square block marker is still rendered.'
Assert-True ($worldCalendarPrefab -notmatch 'CalendarStrategic(?:Town|Castle)MarkerTextureProvider') 'The World Calendar must not instantiate unsafe direct marker texture providers.'
Assert-True (([regex]::Matches($worldCalendarPrefab, '<StrategicLegendDrawWidget[^>]*IconKind="Town"')).Count -eq 1) 'The Strategic Map legend is missing its engine-drawn town icon.'
Assert-True (([regex]::Matches($worldCalendarPrefab, '<StrategicLegendDrawWidget[^>]*IconKind="Castle"')).Count -eq 1) 'The Strategic Map legend is missing its engine-drawn castle icon.'
Assert-True ($worldCalendarPrefab -match 'Text="Town"') 'The Strategic Map legend is missing the Town label.'
Assert-True ($worldCalendarPrefab -match 'Text="Castle"') 'The Strategic Map legend is missing the Castle label.'
[StrategicMapPixelChecks]::Composer($worldCalendarPrefab)
Assert-True ($ledgerSource -match '\[DataSourceProperty\] public bool IsUnderSiege') 'Strategic settlement marker data does not expose its live siege state.'
Assert-True ($ledgerSource -match 'BuildStrategicPanelText\(\)') 'The strategic map is missing its selected-settlement details panel.'
Assert-True ($ledgerSource -match 'CanPlayerInspectSettlement') 'The strategic-map settlement details are missing faction access control.'

Write-Host 'Strategic map verification passed: 133 live settlement territories, complete index-manifest coverage, opaque province-border preservation, split shared regions, one live owner-colour composer, alpha-safe atlas town/castle marker art, a legend, and clickable settlement markers.'
