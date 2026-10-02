param([string]$OutputRoot)

$ErrorActionPreference = 'Stop'
$systemsSource = $PSScriptRoot
$modulesRoot = Split-Path -Parent $systemsSource
$repositoryRoot = Split-Path -Parent $modulesRoot
[xml]$manifest = Get-Content -LiteralPath (Join-Path $systemsSource 'SubModule.xml') -Raw
$version = [string]$manifest.Module.Version.value
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot ("artifacts\AgesOfCalradiaSystemsRS-{0}" -f $version)
}

$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$resolvedOutput = [IO.Path]::GetFullPath($OutputRoot)
$safePrefix = $artifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $resolvedOutput.StartsWith($safePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "R & S output must remain inside: $artifactsRoot"
}
if (Test-Path -LiteralPath $resolvedOutput) { Remove-Item -LiteralPath $resolvedOutput -Recurse -Force }

$moduleRoot = Join-Path $resolvedOutput 'Modules\AgesOfCalradiaSystemsRS'
$binaryRoot = Join-Path $moduleRoot 'bin\Win64_Shipping_Client'
New-Item -ItemType Directory -Path $binaryRoot -Force | Out-Null

function Invoke-FeatureBuild([string]$Project, [string[]]$Properties = @()) {
    $arguments = @('msbuild', $Project, '/t:Rebuild', '/p:Configuration=Release', "/p:OutputPath=$binaryRoot", '/v:minimal') + $Properties
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "R & S Release build failed for $Project with exit code $LASTEXITCODE." }
}
function Copy-TreeContents([string]$Source, [string]$Destination) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Get-ChildItem -LiteralPath $Source -Force | Copy-Item -Destination $Destination -Recurse -Force
}

$religionsRoot = Join-Path $modulesRoot 'AgesOfCalradiaReligions'
$successionRoot = Join-Path $modulesRoot 'AgesOfCalradiaSuccession'
Invoke-FeatureBuild (Join-Path $religionsRoot 'AgesOfCalradiaReligions.csproj') @("/p:CoreModuleDirectory=$repositoryRoot")
Invoke-FeatureBuild (Join-Path $religionsRoot 'Shipwright\AgesOfCalradiaReligions.Shipwright.csproj')
Invoke-FeatureBuild (Join-Path $successionRoot 'AgesOfCalradiaSuccession.csproj') @(
    "/p:ReligionsAssemblyPath=$(Join-Path $binaryRoot 'AgesOfCalradiaReligions.dll')"
)

foreach ($dll in @('AgesOfCalradiaReligions.dll', 'AgesOfCalradiaReligions.Shipwright.dll', 'AgesOfCalradiaSuccession.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $binaryRoot $dll) -PathType Leaf)) { throw "R & S assembly is missing: $dll" }
}
Get-ChildItem -LiteralPath $binaryRoot -Filter '*.pdb' -File | Remove-Item -Force
Copy-Item -LiteralPath (Join-Path $systemsSource 'SubModule.xml') -Destination $moduleRoot -Force
Copy-Item -LiteralPath (Join-Path $systemsSource 'README.md') -Destination $moduleRoot -Force

$moduleData = Join-Path $moduleRoot 'ModuleData'
New-Item -ItemType Directory -Path $moduleData -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $religionsRoot 'ModuleData\religions.json') -Destination $moduleData -Force
Copy-Item -LiteralPath (Join-Path $religionsRoot 'ModuleData\holy_sites.json') -Destination $moduleData -Force
Copy-TreeContents (Join-Path $religionsRoot 'GUI\Prefabs') (Join-Path $moduleRoot 'GUI\Prefabs')
$census = Join-Path $moduleRoot 'GUI\CustomUI\WorldEventsSkin'
New-Item -ItemType Directory -Path $census -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $religionsRoot 'GUI\CustomUI\WorldEventsSkin\page_cabinet_census_v1.png') -Destination $census -Force

& (Join-Path $systemsSource 'Tests\Verify-SystemsRSModule.ps1') -PackageRoot $moduleRoot
if ($LASTEXITCODE -ne 0) { throw 'R & S package verification failed.' }
$archive = "$resolvedOutput.zip"
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
Compress-Archive -LiteralPath (Join-Path $resolvedOutput 'Modules') -DestinationPath $archive -CompressionLevel Optimal
Write-Output "PASS: AOC SYSTEMS R & S package: $moduleRoot"
Write-Output "PASS: AOC SYSTEMS R & S archive: $archive"
