param(
    [string]$Version = '1.0.0',
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\CalradiaCampaignClock-v1.0.0.zip'),
    [string]$BannerlordDir = 'C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord'
)

$ErrorActionPreference = 'Stop'
$moduleRoot = $PSScriptRoot
$repoRoot = Split-Path -Parent (Split-Path -Parent $moduleRoot)
$projectPath = Join-Path $moduleRoot 'CalradiaCampaignClock.csproj'
$verificationPath = Join-Path $moduleRoot 'Tests\Verify-CampaignClock.ps1'
$protectedBaselinePath = Join-Path $repoRoot 'Tests\Verify-ProtectedPoliticalBaseline.ps1'
$assemblyPath = Join-Path $moduleRoot 'bin\Win64_Shipping_Client\CalradiaCampaignClock.dll'
$resolvedOutputPath = [IO.Path]::GetFullPath($OutputPath)

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must use major.minor.patch format: $Version"
}

dotnet msbuild $projectPath /t:Rebuild /p:Configuration=Release /p:BannerlordDir="$BannerlordDir" /restore
if ($LASTEXITCODE -ne 0) {
    throw 'Calradia Campaign Clock Release build failed.'
}

& $verificationPath -AssemblyPath $assemblyPath
if ($LASTEXITCODE -ne 0) {
    throw 'Calradia Campaign Clock verification failed.'
}

& $protectedBaselinePath -SkipInstalled
if ($LASTEXITCODE -ne 0) {
    throw 'Protected political baseline verification failed.'
}

$stagingRoot = Join-Path (
    [IO.Path]::GetTempPath()) (
    'CalradiaCampaignClock-release-' + [Guid]::NewGuid().ToString('N'))
$stagedModule = Join-Path $stagingRoot 'CalradiaCampaignClock'
$stagedBin = Join-Path $stagedModule 'bin\Win64_Shipping_Client'

try {
    New-Item -ItemType Directory -Path $stagedBin -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $moduleRoot 'SubModule.xml') -Destination $stagedModule
    Copy-Item -LiteralPath (Join-Path $moduleRoot 'CalradiaCampaignClock.settings.xml') -Destination $stagedModule
    Copy-Item -LiteralPath (Join-Path $moduleRoot 'README.md') -Destination $stagedModule
    Copy-Item -LiteralPath $assemblyPath -Destination $stagedBin

    $outputDirectory = Split-Path -Parent $resolvedOutputPath
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    if (Test-Path -LiteralPath $resolvedOutputPath) {
        Remove-Item -LiteralPath $resolvedOutputPath -Force
    }

    $archiveParameters = @{
        LiteralPath = $stagedModule
        DestinationPath = $resolvedOutputPath
        CompressionLevel = 'Optimal'
    }
    Compress-Archive @archiveParameters
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        $resolvedStagingRoot = [IO.Path]::GetFullPath($stagingRoot)
        $resolvedTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (-not $resolvedStagingRoot.StartsWith(
                $resolvedTempRoot,
                [StringComparison]::OrdinalIgnoreCase) -or
            -not (Split-Path -Leaf $resolvedStagingRoot).StartsWith(
                'CalradiaCampaignClock-release-',
                [StringComparison]::Ordinal)) {
            throw "Refusing to remove an unexpected staging path: $resolvedStagingRoot"
        }
        Remove-Item -LiteralPath $resolvedStagingRoot -Recurse -Force
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($resolvedOutputPath)
try {
    $actualEntries = @(
        $archive.Entries |
            Where-Object { -not [string]::IsNullOrEmpty($_.Name) } |
            ForEach-Object { $_.FullName.Replace('\', '/') })
}
finally {
    $archive.Dispose()
}

$expectedEntries = @(
    'CalradiaCampaignClock/SubModule.xml',
    'CalradiaCampaignClock/CalradiaCampaignClock.settings.xml',
    'CalradiaCampaignClock/README.md',
    'CalradiaCampaignClock/bin/Win64_Shipping_Client/CalradiaCampaignClock.dll'
)
$unexpectedEntries = @($actualEntries | Where-Object { $expectedEntries -notcontains $_ })
$missingEntries = @($expectedEntries | Where-Object { $actualEntries -notcontains $_ })
if ($unexpectedEntries.Count -gt 0 -or $missingEntries.Count -gt 0) {
    throw "Release archive composition is invalid. Missing=[$($missingEntries -join ', ')]; Unexpected=[$($unexpectedEntries -join ', ')]"
}

$hash = (Get-FileHash -LiteralPath $resolvedOutputPath -Algorithm SHA256).Hash
Write-Output "PASS: clean Calradia Campaign Clock v$Version player release created."
Write-Output "Archive: $resolvedOutputPath"
Write-Output "SHA-256: $hash"
