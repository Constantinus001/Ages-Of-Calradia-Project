param(
    [string]$ModuleRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputArchive,
    [switch]$AllowDirtySource,
    [switch]$SkipSecurityScan,
    [ValidateRange(1, 30)]
    [int]$CloudVerdictHoldMinutes = 10
)

$ErrorActionPreference = 'Stop'
$suiteVersion = 'v1.0.3'
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $ModuleRoot 'artifacts'))
if ([string]::IsNullOrWhiteSpace($OutputArchive)) {
    $OutputArchive = Join-Path $artifactsRoot "Ages-of-Calradia-Complete-Suite-$suiteVersion.zip"
}
$outputPath = [IO.Path]::GetFullPath($OutputArchive)
$safePrefix = $artifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $outputPath.StartsWith($safePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Complete-suite output must remain inside: $artifactsRoot"
}
if (-not $AllowDirtySource) {
    $sourceChanges = @(git -C $ModuleRoot status --porcelain)
    if ($sourceChanges.Count -gt 0) {
        throw 'Source tree has uncommitted changes. Commit the exact complete-suite source before packaging.'
    }
}

$protectedGate = Join-Path $PSScriptRoot 'Verify-ProtectedPoliticalBaseline.ps1'
& $protectedGate -Root $ModuleRoot -SkipInstalled
if (-not $?) { throw 'Protected political-renderer gate failed.' }

[xml]$coreManifest = Get-Content -LiteralPath (Join-Path $ModuleRoot 'SubModule.xml') -Raw
$coreVersion = [string]$coreManifest.Module.Version.value
$coreFullArchive = Join-Path $artifactsRoot "AgesOfCalradia-$coreVersion-complete-source.zip"
$coreSlimArchive = Join-Path $artifactsRoot "AgesOfCalradia-$coreVersion-player.zip"
$lrOutput = Join-Path $artifactsRoot 'CompleteSuite-SystemsLR'
$rsOutput = Join-Path $artifactsRoot 'CompleteSuite-SystemsRS'
$lrArchive = "$lrOutput.zip"
$rsArchive = "$rsOutput.zip"

foreach ($generatedPath in @($coreFullArchive, $coreSlimArchive, $outputPath)) {
    if (Test-Path -LiteralPath $generatedPath) { Remove-Item -LiteralPath $generatedPath -Force }
}

$coreGateArguments = @{
    ModuleRoot = $ModuleRoot
    ReleaseArchive = $coreFullArchive
    CloudVerdictHoldMinutes = 1
    SkipInstalledBaseline = $true
    SkipSecurityScan = $SkipSecurityScan
}
if ($AllowDirtySource) { $coreGateArguments.AllowDirtySource = $true }
& (Join-Path $PSScriptRoot 'Verify-Release.ps1') @coreGateArguments
if (-not $?) { throw 'Core release gate failed.' }

& (Join-Path $PSScriptRoot 'New-SlimPlayerRelease.ps1') `
    -SourceArchive $coreFullArchive `
    -DestinationArchive $coreSlimArchive `
    -ModuleRoot $ModuleRoot
if (-not $?) { throw 'Core player-archive slimming failed.' }

& (Join-Path $ModuleRoot 'Modules\AgesOfCalradiaSystemsLR\New-SystemsLRRelease.ps1') -OutputRoot $lrOutput
if (-not $?) { throw 'Systems L & R release failed.' }
& (Join-Path $ModuleRoot 'Modules\AgesOfCalradiaSystemsRS\New-SystemsRSRelease.ps1') -OutputRoot $rsOutput
if (-not $?) { throw 'Systems R & S release failed.' }

$stagingRoot = Join-Path ([IO.Path]::GetTempPath()) ("AOC-complete-suite-{0}" -f [Guid]::NewGuid())
$releaseRoot = Join-Path $stagingRoot 'release'
$modulesStage = Join-Path $releaseRoot 'Modules'
try {
    New-Item -ItemType Directory -Path $modulesStage -Force | Out-Null
    $coreExtract = Join-Path $stagingRoot 'core'
    $lrExtract = Join-Path $stagingRoot 'lr'
    $rsExtract = Join-Path $stagingRoot 'rs'
    Expand-Archive -LiteralPath $coreSlimArchive -DestinationPath $coreExtract
    Expand-Archive -LiteralPath $lrArchive -DestinationPath $lrExtract
    Expand-Archive -LiteralPath $rsArchive -DestinationPath $rsExtract

    $coreExtractedModule = Join-Path $coreExtract 'AgesOfCalradia'
    if (-not (Test-Path -LiteralPath $coreExtractedModule -PathType Container)) {
        throw 'Slim Core archive did not contain the expected module root.'
    }
    Copy-Item -LiteralPath $coreExtractedModule -Destination (Join-Path $modulesStage 'AOC CORE') -Recurse
    Copy-Item -LiteralPath (Join-Path $lrExtract 'Modules\AgesOfCalradiaSystemsLR') -Destination (Join-Path $modulesStage 'AOC L&R') -Recurse
    Copy-Item -LiteralPath (Join-Path $rsExtract 'Modules\AgesOfCalradiaSystemsRS') -Destination (Join-Path $modulesStage 'AOC R&S') -Recurse
    Copy-Item -LiteralPath (Join-Path $ModuleRoot 'COMPLETE_SUITE_INSTALLATION.md') -Destination $releaseRoot

    $expectedFolders = @('AOC CORE', 'AOC L&R', 'AOC R&S')
    $actualFolders = @(Get-ChildItem -LiteralPath $modulesStage -Directory | Select-Object -ExpandProperty Name | Sort-Object)
    if (Compare-Object -ReferenceObject ($expectedFolders | Sort-Object) -DifferenceObject $actualFolders) {
        throw 'Complete suite must contain exactly Core, Systems L & R, and Systems R & S.'
    }

    $expectedIds = @{
        'AOC CORE' = 'AgesOfCalradia'
        'AOC L&R' = 'AgesOfCalradiaSystemsLR'
        'AOC R&S' = 'AgesOfCalradiaSystemsRS'
    }
    foreach ($folder in $expectedFolders) {
        $modulePath = Join-Path $modulesStage $folder
        [xml]$manifest = Get-Content -LiteralPath (Join-Path $modulePath 'SubModule.xml') -Raw
        if ($manifest.Module.Id.value -ne $expectedIds[$folder]) {
            throw "Incorrect module identity in complete suite: $folder"
        }
        foreach ($dllName in @($manifest.Module.SubModules.SubModule.DLLName.value | Sort-Object -Unique)) {
            if (-not (Test-Path -LiteralPath (Join-Path $modulePath "bin\Win64_Shipping_Client\$dllName") -PathType Leaf)) {
                throw "Manifest-declared DLL is missing from $folder`: $dllName"
            }
        }
    }

    $corePath = Join-Path $modulesStage 'AOC CORE'
    $approvedCoreHash = '560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E'
    $approvedWorldEventsHash = 'E7013CF2B18B381119CC7479F0840BC423CD59565913BD22BBFC1E0C55A82E5E'
    if ((Get-FileHash -LiteralPath (Join-Path $corePath 'bin\Win64_Shipping_Client\AgesOfCalradia.dll') -Algorithm SHA256).Hash -ne $approvedCoreHash) {
        throw 'Complete suite does not contain the approved protected Core DLL.'
    }
    if ((Get-FileHash -LiteralPath (Join-Path $corePath 'GUI\Prefabs\WorldCalendar\WorldCalendar.xml') -Algorithm SHA256).Hash -ne $approvedWorldEventsHash) {
        throw 'Complete suite does not contain the approved protected World Events prefab.'
    }
    $legacySpriteRegistry = Join-Path $corePath 'GUI\Ages Of CalradiaSpriteData.xml'
    $folderMatchedSpriteRegistry = Join-Path $corePath 'GUI\AOC CORESpriteData.xml'
    if (-not (Test-Path -LiteralPath $folderMatchedSpriteRegistry -PathType Leaf)) {
        throw 'AOC CORE is missing its folder-matched sprite registry; Bannerlord would hide the World Events shell.'
    }
    if ((Get-FileHash -LiteralPath $legacySpriteRegistry -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $folderMatchedSpriteRegistry -Algorithm SHA256).Hash) {
        throw 'AOC CORE sprite registries differ; the packaged World Events UI is not deterministic.'
    }

    $worldEventsRoot = Join-Path $corePath 'GUI\CustomUI\WorldEventsSkin'
    $worldEventsManifest = Join-Path $worldEventsRoot 'RuntimeAssetManifest.txt'
    $requiredWorldEventsAssets = @(Get-Content -LiteralPath $worldEventsManifest |
        ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') })
    if ($requiredWorldEventsAssets.Count -ne 24) { throw 'Complete-suite World Events skin manifest is incomplete.' }
    foreach ($asset in $requiredWorldEventsAssets) {
        if (-not (Test-Path -LiteralPath (Join-Path $worldEventsRoot $asset) -PathType Leaf)) {
            throw "Complete suite is missing World Events UI asset: $asset"
        }
    }

    $lrRoot = Join-Path $modulesStage 'AOC L&R'
    foreach ($scene in @(
        'rct_refuge_temperate_land','rct_refuge_temperate_river','rct_refuge_temperate_coast',
        'rct_refuge_snow_land','rct_refuge_snow_river','rct_refuge_snow_coast',
        'rct_refuge_desert_land','rct_refuge_desert_river','rct_refuge_desert_coast')) {
        foreach ($file in @('scene.xscene','terrain.bin','navmesh.bin')) {
            if (-not (Test-Path -LiteralPath (Join-Path $lrRoot "SceneObj\$scene\$file") -PathType Leaf)) {
                throw "Complete suite is missing Refuge runtime asset: $scene/$file"
            }
        }
    }

    $rsRoot = Join-Path $modulesStage 'AOC R&S'
    foreach ($file in @(
        'ModuleData\religions.json','ModuleData\holy_sites.json',
        'GUI\Prefabs\AocStrategicMapModes.xml',
        'GUI\CustomUI\WorldEventsSkin\page_cabinet_census_v1.png')) {
        if (-not (Test-Path -LiteralPath (Join-Path $rsRoot $file) -PathType Leaf)) {
            throw "Complete suite is missing Religions/Population runtime asset: $file"
        }
    }

    $forbidden = @(Get-ChildItem -LiteralPath $releaseRoot -File -Recurse | Where-Object {
        $_.Extension -in @('.pdb','.log','.cs','.csproj','.ps1') -or
        $_.FullName -match '[\\/](Backups|Tests|AssetSources|obj)[\\/]'
    })
    if ($forbidden.Count -gt 0) {
        throw "Development files entered the complete suite: $($forbidden.FullName -join ', ')"
    }
    $harmonyCopies = @(Get-ChildItem -LiteralPath $modulesStage -Filter '0Harmony.dll' -File -Recurse)
    if ($harmonyCopies.Count -ne 1 -or $harmonyCopies[0].FullName -notlike "$(Join-Path $corePath '*')") {
        throw 'Complete suite must contain exactly one Harmony runtime, owned by Core.'
    }

    $checksumLines = @(Get-ChildItem -LiteralPath $modulesStage -File -Recurse | Sort-Object FullName | ForEach-Object {
        $relative = $_.FullName.Substring($releaseRoot.Length + 1).Replace('\', '/')
        "{0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash, $relative
    })
    [IO.File]::WriteAllLines((Join-Path $releaseRoot 'CHECKSUMS-SHA256.txt'), $checksumLines, [Text.UTF8Encoding]::new($false))

    New-Item -ItemType Directory -Path (Split-Path -Parent $outputPath) -Force | Out-Null
    Compress-Archive -Path (Join-Path $releaseRoot '*') -DestinationPath $outputPath -CompressionLevel Optimal
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) { Remove-Item -LiteralPath $stagingRoot -Recurse -Force }
}

if (-not $SkipSecurityScan) {
    if (-not (Get-Command Start-MpScan -ErrorAction SilentlyContinue)) {
        throw 'Microsoft Defender scan command is unavailable. Do not upload this release.'
    }
    $scanStarted = Get-Date
    Start-MpScan -ScanPath $outputPath -ScanType CustomScan
    $escapedArchive = [Regex]::Escape($outputPath)
    for ($minute = 1; $minute -le $CloudVerdictHoldMinutes; $minute++) {
        Start-Sleep -Seconds 60
        if (-not (Test-Path -LiteralPath $outputPath -PathType Leaf)) {
            throw 'Security software removed the complete-suite archive. Do not upload this release.'
        }
        $detections = @(Get-MpThreatDetection | Where-Object {
            $_.Resources -match $escapedArchive -and $_.InitialDetectionTime -ge $scanStarted.AddMinutes(-1)
        })
        if ($detections.Count -gt 0) {
            $detections | Format-List | Out-String | Write-Error
            throw 'Microsoft Defender detected a threat in the complete-suite archive. Do not upload this release.'
        }
        Write-Output ("Complete-suite Defender cloud-verdict hold: {0}/{1} minutes clean." -f $minute, $CloudVerdictHoldMinutes)
    }
}

$archiveHash = (Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash
Write-Output "PASS: Complete suite archive: $outputPath"
Write-Output "PASS: Complete suite SHA256: $archiveHash"
