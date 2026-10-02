param(
    [Parameter(Mandatory = $true)]
    [string]$SourceArchive,

    [Parameter(Mandatory = $true)]
    [string]$DestinationArchive,

    [string]$ModuleRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression.FileSystem

$sourcePath = (Resolve-Path -LiteralPath $SourceArchive).Path
$destinationPath = [IO.Path]::GetFullPath($DestinationArchive)
$worldEventsSkinPath = Join-Path $ModuleRoot 'GUI\CustomUI\WorldEventsSkin'
$worldEventsManifestPath = Join-Path $worldEventsSkinPath 'RuntimeAssetManifest.txt'
$worldEventsPrefabPath = Join-Path $ModuleRoot 'GUI\Prefabs\WorldCalendar\WorldCalendar.xml'
$spriteConfigSourcePath = Join-Path $ModuleRoot 'GUI\SpriteParts\Config.xml'
$protectedWorldEventsEntries = [ordered]@{
    'AgesOfCalradia/GUI/Ages Of CalradiaSpriteData.xml' = (Join-Path $ModuleRoot 'GUI\Ages Of CalradiaSpriteData.xml')
    'AgesOfCalradia/GUI/SpriteParts/ui_world_events_v2/aoc_world_events_shell_v8.png' = (Join-Path $ModuleRoot 'GUI\SpriteParts\ui_world_events_v2\aoc_world_events_shell_v8.png')
    'AgesOfCalradia/GUI/SpriteParts/ui_world_events_v6/aoc_world_events_shell_calendar_selected_v6.png' = (Join-Path $ModuleRoot 'GUI\SpriteParts\ui_world_events_v6\aoc_world_events_shell_calendar_selected_v6.png')
    'AgesOfCalradia/GUI/SpriteParts/ui_world_events_v6/aoc_world_events_shell_story_selected_v6.png' = (Join-Path $ModuleRoot 'GUI\SpriteParts\ui_world_events_v6\aoc_world_events_shell_story_selected_v6.png')
    'AgesOfCalradia/GUI/SpriteParts/ui_world_events_v6/aoc_world_events_shell_realm_selected_v6.png' = (Join-Path $ModuleRoot 'GUI\SpriteParts\ui_world_events_v6\aoc_world_events_shell_realm_selected_v6.png')
    'AgesOfCalradia/GUI/SpriteParts/ui_world_events_v6/aoc_world_events_shell_strategic_selected_v6.png' = (Join-Path $ModuleRoot 'GUI\SpriteParts\ui_world_events_v6\aoc_world_events_shell_strategic_selected_v6.png')
    'AgesOfCalradia/Assets/GauntletUI/ui_world_events_v2_1_tex.tpac' = (Join-Path $ModuleRoot 'Assets\GauntletUI\ui_world_events_v2_1_tex.tpac')
    'AgesOfCalradia/Assets/GauntletUI/ui_world_events_v6_1_tex.tpac' = (Join-Path $ModuleRoot 'Assets\GauntletUI\ui_world_events_v6_1_tex.tpac')
    'AgesOfCalradia/RuntimeDataCache/2A1830F3-4740-45CC-9938-C6FAB79CFEC6.rdc' = (Join-Path $ModuleRoot 'RuntimeDataCache\2A1830F3-4740-45CC-9938-C6FAB79CFEC6.rdc')
    'AgesOfCalradia/RuntimeDataCache/0622BBE7-B8CC-4CDC-A98D-44F6C9335248.rdc' = (Join-Path $ModuleRoot 'RuntimeDataCache\0622BBE7-B8CC-4CDC-A98D-44F6C9335248.rdc')
}
$harmonySourcePath = Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.2\lib\net472\0Harmony.dll'
$harmonyEntryName = 'AgesOfCalradia/bin/Win64_Shipping_Client/0Harmony.dll'
$worldEventsPrefabEntryName = 'AgesOfCalradia/GUI/Prefabs/WorldCalendar/WorldCalendar.xml'
$spriteConfigEntryName = 'AgesOfCalradia/GUI/SpriteParts/Config.xml'
$approvedFixesSourcePath = Join-Path $ModuleRoot 'bin\Win64_Shipping_Client\AgesOfCalradia.Approved560CalendarFixes.dll'
$approvedFixesEntryName = 'AgesOfCalradia/bin/Win64_Shipping_Client/AgesOfCalradia.Approved560CalendarFixes.dll'
$campaignLabelSourcePath = Join-Path $ModuleRoot 'bin\Win64_Shipping_Client\AgesOfCalradia.CampaignLabelVisibility.dll'
$campaignLabelEntryName = 'AgesOfCalradia/bin/Win64_Shipping_Client/AgesOfCalradia.CampaignLabelVisibility.dll'
$politicalBorderOptimizerSourcePath = Join-Path $ModuleRoot 'bin\Win64_Shipping_Client\AgesOfCalradia.PoliticalBorderOptimizer.dll'
$politicalBorderOptimizerEntryName = 'AgesOfCalradia/bin/Win64_Shipping_Client/AgesOfCalradia.PoliticalBorderOptimizer.dll'
$worldEventsShellRepairSourcePath = Join-Path $ModuleRoot 'bin\Win64_Shipping_Client\AgesOfCalradia.WorldEventsShellRepair.dll'
$worldEventsShellRepairEntryName = 'AgesOfCalradia/bin/Win64_Shipping_Client/AgesOfCalradia.WorldEventsShellRepair.dll'
$releaseSourceEntries = [ordered]@{
    'AgesOfCalradia/SubModule.xml' = (Join-Path $ModuleRoot 'SubModule.xml')
    'AgesOfCalradia/README.md' = (Join-Path $ModuleRoot 'README.md')
    'AgesOfCalradia/GUI/Prefabs/Map/MapBar.xml' = (Join-Path $ModuleRoot 'GUI\Prefabs\Map\MapBar.xml')
    'AgesOfCalradia/bin/Win64_Shipping_Client/AgesOfCalradia.MCM.dll' = (Join-Path $ModuleRoot 'bin\Win64_Shipping_Client\AgesOfCalradia.MCM.dll')
    'AgesOfCalradia/bin/Win64_Shipping_Client/MCMv5.dll' = (Join-Path $ModuleRoot 'bin\Win64_Shipping_Client\MCMv5.dll')
    $campaignLabelEntryName = $campaignLabelSourcePath
    $politicalBorderOptimizerEntryName = $politicalBorderOptimizerSourcePath
    $worldEventsShellRepairEntryName = $worldEventsShellRepairSourcePath
    'AgesOfCalradia/bin/Win64_Shipping_Client/AgesOfCalradia.CampaignSystems.dll' = (Join-Path $ModuleRoot 'bin\Win64_Shipping_Client\AgesOfCalradia.CampaignSystems.dll')
}
if ($sourcePath -eq $destinationPath) {
    throw 'SourceArchive and DestinationArchive must be different files.'
}
if (Test-Path -LiteralPath $destinationPath) {
    throw "Destination archive already exists: $destinationPath"
}
if (-not (Test-Path -LiteralPath $worldEventsManifestPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $worldEventsPrefabPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $spriteConfigSourcePath -PathType Leaf)) {
    throw 'World Events runtime asset manifest, prefab, or sprite config is missing.'
}
if (-not (Test-Path -LiteralPath $harmonySourcePath -PathType Leaf)) {
    throw "Required bundled Harmony 2.4.2 binary is missing: $harmonySourcePath"
}
if (-not (Test-Path -LiteralPath $approvedFixesSourcePath -PathType Leaf)) {
    throw "Required approved-build fixes sidecar is missing: $approvedFixesSourcePath"
}
$approvedFixesBinaryText = [Text.Encoding]::UTF8.GetString(
    [IO.File]::ReadAllBytes($approvedFixesSourcePath))
$approvedFixesSearchText = $approvedFixesBinaryText.Replace(
    [string][char]0,
    [string]::Empty)
foreach ($requiredMapBarToken in @(
    'AocMapBarSeasonCrownTextureProvider',
    'AocMapBarNotchBackingTextureProvider',
    'AocMapBarMedallionRimTextureProvider',
    'aoc_mapbar_season_crown_compact.png',
    'aoc_mapbar_notch_backing.png',
    'aoc_mapbar_medallion_ring_oval.png')) {
    if (-not $approvedFixesSearchText.Contains($requiredMapBarToken)) {
        throw "Approved-build fixes sidecar is stale and cannot package the MapBar: missing $requiredMapBarToken. Run Tests\Verify-Release.ps1 first."
    }
}
foreach ($protectedAsset in $protectedWorldEventsEntries.Values) {
    if (-not (Test-Path -LiteralPath $protectedAsset -PathType Leaf)) {
        throw "Required UI REDESIGN runtime asset is missing: $protectedAsset"
    }
}
foreach ($releaseSource in $releaseSourceEntries.Values) {
    if (-not (Test-Path -LiteralPath $releaseSource -PathType Leaf)) {
        throw "Required v1.5.14 release source is missing: $releaseSource"
    }
}
$requiredWorldEventsAssets = @(Get-Content -LiteralPath $worldEventsManifestPath |
    ForEach-Object { $_.Trim() } |
    Where-Object { $_ -and -not $_.StartsWith('#') })
$worldEventsRuntimeFiles = @($requiredWorldEventsAssets | ForEach-Object {
    Join-Path $worldEventsSkinPath $_
}) + $worldEventsManifestPath
$missingWorldEventsSourceAssets = @($worldEventsRuntimeFiles | Where-Object {
    -not (Test-Path -LiteralPath $_ -PathType Leaf)
})
if ($missingWorldEventsSourceAssets.Count -gt 0) {
    throw "World Events runtime source assets are missing: $($missingWorldEventsSourceAssets -join ', ')"
}
$requiredWorldEventsEntryNames = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
[void]$requiredWorldEventsEntryNames.Add(
    'AgesOfCalradia/GUI/CustomUI/WorldEventsSkin/RuntimeAssetManifest.txt')
foreach ($assetName in $requiredWorldEventsAssets) {
    [void]$requiredWorldEventsEntryNames.Add(
        "AgesOfCalradia/GUI/CustomUI/WorldEventsSkin/$assetName")
}

function Test-IsDevelopmentEntry {
    param([string]$EntryName)

    $EntryName = $EntryName.Replace('\', '/')

    if ($EntryName.StartsWith(
        'AgesOfCalradia/AssetSources/',
        [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    if ($protectedWorldEventsEntries.Contains($EntryName)) {
        return $false
    }

    return $EntryName -match '(?i)(\.bak$|\.before-|\.backup-)'
}

$inputArchive = [IO.Compression.ZipFile]::OpenRead($sourcePath)
$outputArchive = [IO.Compression.ZipFile]::Open(
    $destinationPath,
    [IO.Compression.ZipArchiveMode]::Create)
try {
    $writtenEntries = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $inputArchive.Entries) {
        $entryName = $entry.FullName.Replace('\', '/')
        if (Test-IsDevelopmentEntry -EntryName $entryName) {
            continue
        }

        if ($entryName.Equals($harmonyEntryName, [StringComparison]::OrdinalIgnoreCase) -or
            $entryName.Equals($worldEventsPrefabEntryName, [StringComparison]::OrdinalIgnoreCase) -or
            $entryName.Equals($spriteConfigEntryName, [StringComparison]::OrdinalIgnoreCase) -or
            $entryName.Equals($approvedFixesEntryName, [StringComparison]::OrdinalIgnoreCase) -or
            $releaseSourceEntries.Contains($entryName) -or
            $protectedWorldEventsEntries.Contains($entryName)) {
            continue
        }

        $newEntry = $outputArchive.CreateEntry(
            $entryName,
            [IO.Compression.CompressionLevel]::Optimal)
        [void]$writtenEntries.Add($entryName)
        $newEntry.LastWriteTime = $entry.LastWriteTime
        if ([string]::IsNullOrEmpty($entry.Name)) {
            continue
        }

        $sourceStream = $entry.Open()
        $destinationStream = $newEntry.Open()
        try {
            $sourceStream.CopyTo($destinationStream)
        }
        finally {
            $destinationStream.Dispose()
            $sourceStream.Dispose()
        }
    }

    $harmonyEntry = $outputArchive.CreateEntry(
        $harmonyEntryName,
        [IO.Compression.CompressionLevel]::Optimal)
    [void]$writtenEntries.Add($harmonyEntryName)
    $harmonyEntry.LastWriteTime = (Get-Item -LiteralPath $harmonySourcePath).LastWriteTime
    $sourceStream = [IO.File]::OpenRead($harmonySourcePath)
    $destinationStream = $harmonyEntry.Open()
    try {
        $sourceStream.CopyTo($destinationStream)
    }
    finally {
        $destinationStream.Dispose()
        $sourceStream.Dispose()
    }

    $approvedFixesEntry = $outputArchive.CreateEntry(
        $approvedFixesEntryName,
        [IO.Compression.CompressionLevel]::Optimal)
    [void]$writtenEntries.Add($approvedFixesEntryName)
    $approvedFixesEntry.LastWriteTime = (Get-Item -LiteralPath $approvedFixesSourcePath).LastWriteTime
    $sourceStream = [IO.File]::OpenRead($approvedFixesSourcePath)
    $destinationStream = $approvedFixesEntry.Open()
    try {
        $sourceStream.CopyTo($destinationStream)
    }
    finally {
        $destinationStream.Dispose()
        $sourceStream.Dispose()
    }

    $worldEventsPrefabEntry = $outputArchive.CreateEntry(
        $worldEventsPrefabEntryName,
        [IO.Compression.CompressionLevel]::Optimal)
    [void]$writtenEntries.Add($worldEventsPrefabEntryName)
    $worldEventsPrefabEntry.LastWriteTime = (Get-Item -LiteralPath $worldEventsPrefabPath).LastWriteTime
    $sourceStream = [IO.File]::OpenRead($worldEventsPrefabPath)
    $destinationStream = $worldEventsPrefabEntry.Open()
    try {
        $sourceStream.CopyTo($destinationStream)
    }
    finally {
        $destinationStream.Dispose()
        $sourceStream.Dispose()
    }

    $spriteConfigEntry = $outputArchive.CreateEntry(
        $spriteConfigEntryName,
        [IO.Compression.CompressionLevel]::Optimal)
    [void]$writtenEntries.Add($spriteConfigEntryName)
    $spriteConfigEntry.LastWriteTime = (Get-Item -LiteralPath $spriteConfigSourcePath).LastWriteTime
    $sourceStream = [IO.File]::OpenRead($spriteConfigSourcePath)
    $destinationStream = $spriteConfigEntry.Open()
    try {
        $sourceStream.CopyTo($destinationStream)
    }
    finally {
        $destinationStream.Dispose()
        $sourceStream.Dispose()
    }

    foreach ($protectedEntryName in $protectedWorldEventsEntries.Keys) {
        $protectedSourcePath = $protectedWorldEventsEntries[$protectedEntryName]
        $protectedEntry = $outputArchive.CreateEntry(
            $protectedEntryName,
            [IO.Compression.CompressionLevel]::Optimal)
        [void]$writtenEntries.Add($protectedEntryName)
        $protectedEntry.LastWriteTime = (Get-Item -LiteralPath $protectedSourcePath).LastWriteTime
        $sourceStream = [IO.File]::OpenRead($protectedSourcePath)
        $destinationStream = $protectedEntry.Open()
        try {
            $sourceStream.CopyTo($destinationStream)
        }
        finally {
            $destinationStream.Dispose()
            $sourceStream.Dispose()
        }
    }

    foreach ($releaseEntryName in $releaseSourceEntries.Keys) {
        $releaseSourcePath = $releaseSourceEntries[$releaseEntryName]
        $newEntry = $outputArchive.CreateEntry(
            $releaseEntryName,
            [IO.Compression.CompressionLevel]::Optimal)
        [void]$writtenEntries.Add($releaseEntryName)
        $newEntry.LastWriteTime = (Get-Item -LiteralPath $releaseSourcePath).LastWriteTime
        $sourceStream = [IO.File]::OpenRead($releaseSourcePath)
        $destinationStream = $newEntry.Open()
        try {
            $sourceStream.CopyTo($destinationStream)
        }
        finally {
            $destinationStream.Dispose()
            $sourceStream.Dispose()
        }
    }

    foreach ($runtimeAsset in $worldEventsRuntimeFiles) {
        $relativePath = $runtimeAsset.Substring($ModuleRoot.Length).
            TrimStart('\', '/').Replace('\', '/')
        $entryName = "AgesOfCalradia/$relativePath"
        if (-not $writtenEntries.Add($entryName)) {
            continue
        }

        $newEntry = $outputArchive.CreateEntry(
            $entryName,
            [IO.Compression.CompressionLevel]::Optimal)
        $newEntry.LastWriteTime = (Get-Item -LiteralPath $runtimeAsset).LastWriteTime
        $sourceStream = [IO.File]::OpenRead($runtimeAsset)
        $destinationStream = $newEntry.Open()
        try {
            $sourceStream.CopyTo($destinationStream)
        }
        finally {
            $destinationStream.Dispose()
            $sourceStream.Dispose()
        }
    }
}
finally {
    $outputArchive.Dispose()
    $inputArchive.Dispose()
}

$archive = [IO.Compression.ZipFile]::OpenRead($destinationPath)
try {
    $entryNames = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $archive.Entries) {
        $entryName = $entry.FullName.Replace('\', '/')
        [void]$entryNames.Add($entryName)
        if (Test-IsDevelopmentEntry -EntryName $entryName) {
            throw "Development entry remained in player archive: $entryName"
        }
    }

    $worldEventsManifestEntry = $archive.GetEntry(
        'AgesOfCalradia/GUI/CustomUI/WorldEventsSkin/RuntimeAssetManifest.txt')
    if ($null -eq $worldEventsManifestEntry) {
        throw 'Player archive is missing the World Events runtime asset manifest.'
    }
    $reader = [IO.StreamReader]::new($worldEventsManifestEntry.Open())
    try {
        $requiredWorldEventsAssets = @($reader.ReadToEnd() -split "`r?`n" |
            ForEach-Object { $_.Trim() } |
            Where-Object { $_ -and -not $_.StartsWith('#') })
    }
    finally {
        $reader.Dispose()
    }
    $missingWorldEventsAssets = @($requiredWorldEventsAssets | Where-Object {
        -not $entryNames.Contains(
            "AgesOfCalradia/GUI/CustomUI/WorldEventsSkin/$_")
    })
    if ($missingWorldEventsAssets.Count -gt 0) {
        throw "Player archive is missing World Events runtime assets: $($missingWorldEventsAssets -join ', ')"
    }

    $harmonyArchiveEntry = $archive.GetEntry($harmonyEntryName)
    if ($null -eq $harmonyArchiveEntry) {
        throw 'Player archive is missing the bundled Harmony binary.'
    }
    $expectedHarmonyHash = (Get-FileHash -LiteralPath $harmonySourcePath -Algorithm SHA256).Hash
    $harmonyStream = $harmonyArchiveEntry.Open()
    try {
        $sha = [Security.Cryptography.SHA256]::Create()
        try {
            $actualHarmonyHash = ([BitConverter]::ToString(
                $sha.ComputeHash($harmonyStream))).Replace('-', '')
        }
        finally {
            $sha.Dispose()
        }
    }
    finally {
        $harmonyStream.Dispose()
    }
    if ($actualHarmonyHash -ne $expectedHarmonyHash) {
        throw "Bundled Harmony hash mismatch. Expected $expectedHarmonyHash; found $actualHarmonyHash."
    }
    $approvedFixesArchiveEntry = $archive.GetEntry($approvedFixesEntryName)
    if ($null -eq $approvedFixesArchiveEntry) {
        throw 'Player archive is missing the approved-build fixes sidecar.'
    }
    $expectedApprovedFixesHash = (Get-FileHash -LiteralPath $approvedFixesSourcePath -Algorithm SHA256).Hash
    $approvedFixesStream = $approvedFixesArchiveEntry.Open()
    try {
        $sha = [Security.Cryptography.SHA256]::Create()
        try {
            $actualApprovedFixesHash = ([BitConverter]::ToString(
                $sha.ComputeHash($approvedFixesStream))).Replace('-', '')
        }
        finally {
            $sha.Dispose()
        }
    }
    finally {
        $approvedFixesStream.Dispose()
    }
    if ($actualApprovedFixesHash -ne $expectedApprovedFixesHash) {
        throw "Approved-build fixes sidecar hash mismatch. Expected $expectedApprovedFixesHash; found $actualApprovedFixesHash."
    }
    $worldEventsPrefabEntry = $archive.GetEntry($worldEventsPrefabEntryName)
    if ($null -eq $worldEventsPrefabEntry) {
        throw 'Player archive is missing the World Events prefab.'
    }
    $prefabReader = [IO.StreamReader]::new($worldEventsPrefabEntry.Open())
    try {
        [xml]$worldEventsPrefab = $prefabReader.ReadToEnd()
    }
    finally {
        $prefabReader.Dispose()
    }
    $mainTabButtons = @($worldEventsPrefab.SelectNodes(
        '//ListPanel[@DataSource="{Tabs}"]/ItemTemplate/ButtonWidget'))
    $replacementMainTabTextures = @($worldEventsPrefab.SelectNodes(
        '//ListPanel[@DataSource="{Tabs}"]/ItemTemplate/ButtonWidget/Children/TextureWidget'))
    if ($mainTabButtons.Count -ne 1 -or $replacementMainTabTextures.Count -ne 0) {
        throw 'World Events must use the approved baked-shell tab hitboxes without replacement texture layers.'
    }
    foreach ($requiredShellSprite in @(
        'aoc_world_events_shell_v8',
        'aoc_world_events_shell_calendar_selected_v6',
        'aoc_world_events_shell_story_selected_v6',
        'aoc_world_events_shell_realm_selected_v6',
        'aoc_world_events_shell_strategic_selected_v6')) {
        if ($null -eq $worldEventsPrefab.SelectSingleNode(
            "//Widget[@Sprite='$requiredShellSprite']")) {
            throw "World Events prefab is missing approved UI REDESIGN shell sprite: $requiredShellSprite"
        }
    }

    foreach ($protectedEntryName in $protectedWorldEventsEntries.Keys) {
        if (-not $entryNames.Contains($protectedEntryName)) {
            throw "Player archive is missing protected UI REDESIGN asset: $protectedEntryName"
        }
    }

    $spriteDataEntry = $archive.GetEntry(
        'AgesOfCalradia/GUI/Ages Of CalradiaSpriteData.xml')
    if ($null -eq $spriteDataEntry) {
        throw 'Player archive is missing Ages Of CalradiaSpriteData.xml.'
    }

    $reader = [IO.StreamReader]::new($spriteDataEntry.Open())
    try {
        [xml]$spriteData = $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }

    $requiredDirectTextures = @(
        'strategic_map_atlas.png',
        'strategic_province_index.png',
        'strategic_city_labels.png',
        'strategic_marker_town.png',
        'strategic_marker_castle.png'
    ) | ForEach-Object {
        "AgesOfCalradia/GUI/SpriteParts/ui_world_calendar/$_"
    }
    $missingDirectTextures = @($requiredDirectTextures | Where-Object {
        -not $entryNames.Contains($_)
    })
    if ($missingDirectTextures.Count -gt 0) {
        throw "Player archive is missing directly loaded textures: $($missingDirectTextures -join ', ')"
    }

    $requiredGuiFiles = @(Get-ChildItem -LiteralPath (Join-Path $ModuleRoot 'GUI') -File -Recurse)
    $requiredGuiEntries = @($requiredGuiFiles | ForEach-Object {
        'AgesOfCalradia/' + $_.FullName.Substring($ModuleRoot.Length + 1).Replace('\', '/')
    })
    $missingGuiEntries = @($requiredGuiEntries | Where-Object {
        -not $entryNames.Contains($_)
    })
    if ($missingGuiEntries.Count -gt 0) {
        throw "Player archive is missing GUI files: $($missingGuiEntries -join ', ')"
    }
    $mismatchedGuiEntries = @()
    foreach ($guiFile in $requiredGuiFiles) {
        $guiEntryName = 'AgesOfCalradia/' +
            $guiFile.FullName.Substring($ModuleRoot.Length + 1).Replace('\', '/')
        $guiEntry = $archive.GetEntry($guiEntryName)
        $guiStream = $guiEntry.Open()
        try {
            $sha = [Security.Cryptography.SHA256]::Create()
            try {
                $archiveGuiHash = ([BitConverter]::ToString(
                    $sha.ComputeHash($guiStream))).Replace('-', '')
            }
            finally {
                $sha.Dispose()
            }
        }
        finally {
            $guiStream.Dispose()
        }
        $sourceGuiHash = (Get-FileHash -LiteralPath $guiFile.FullName -Algorithm SHA256).Hash
        if ($archiveGuiHash -ne $sourceGuiHash) {
            $mismatchedGuiEntries += $guiEntryName
        }
    }
    if ($mismatchedGuiEntries.Count -gt 0) {
        throw "Player archive contains changed GUI files: $($mismatchedGuiEntries -join ', ')"
    }

    $missingCategoryAssets = @()
    foreach ($category in $spriteData.SpriteData.SpriteCategories.SpriteCategory) {
        for ($sheetId = 1; $sheetId -le [int]$category.SpriteSheetCount; $sheetId++) {
            $tpacEntry = 'AgesOfCalradia/Assets/GauntletUI/{0}_{1}_tex.tpac' -f
                [string]$category.Name,
                $sheetId
            if (-not $entryNames.Contains($tpacEntry)) {
                $missingCategoryAssets += $tpacEntry
            }
        }
    }
    if ($missingCategoryAssets.Count -gt 0) {
        throw "Player archive is missing declared category assets: $($missingCategoryAssets -join ', ')"
    }

    [pscustomobject]@{
        Archive = $destinationPath
        Entries = $archive.Entries.Count
        SizeMB = [Math]::Round((Get-Item -LiteralPath $destinationPath).Length / 1MB, 2)
        DeclaredSpriteParts = @($spriteData.SpriteData.SpriteParts.SpritePart).Count
        MissingDirectTextures = $missingDirectTextures.Count
        MissingCategoryAssets = $missingCategoryAssets.Count
        MissingGuiFiles = $missingGuiEntries.Count
        ChangedGuiFiles = $mismatchedGuiEntries.Count
        SHA256 = (Get-FileHash -LiteralPath $destinationPath -Algorithm SHA256).Hash
    }
}
finally {
    $archive.Dispose()
}
