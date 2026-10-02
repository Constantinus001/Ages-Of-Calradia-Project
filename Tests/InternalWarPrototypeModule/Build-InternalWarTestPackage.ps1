param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$buildRoot = Join-Path $root (Join-Path 'bin' $Configuration)
$assembly = Join-Path $buildRoot 'AgesOfCalradiaInternalWarsTest.dll'
if (-not (Test-Path -LiteralPath $assembly)) {
    throw "Build output does not exist: $assembly"
}

$packageContainer = Join-Path $root 'package'
$packageRoot = Join-Path $packageContainer 'AgesOfCalradiaInternalWarsTest'
$resolvedContainer = [IO.Path]::GetFullPath($packageContainer).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$resolvedPackage = [IO.Path]::GetFullPath($packageRoot)
if (-not $resolvedPackage.StartsWith($resolvedContainer, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean package outside the isolated package container: $resolvedPackage"
}
if (Test-Path -LiteralPath $packageRoot) { Remove-Item -LiteralPath $packageRoot -Recurse -Force }
$packageBin = Join-Path $packageRoot 'bin\Win64_Shipping_Client'
New-Item -ItemType Directory -Path $packageBin -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'SubModule.xml') -Destination $packageRoot -Force
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $packageRoot -Force
Copy-Item -LiteralPath (Join-Path $root 'COMPLETION_CHECKLIST.md') -Destination $packageRoot -Force
Copy-Item -LiteralPath (Join-Path $root 'Summarize-InternalWarDiagnostics.ps1') -Destination $packageRoot -Force
Copy-Item -LiteralPath $assembly -Destination $packageBin -Force

$symbols = Join-Path $buildRoot 'AgesOfCalradiaInternalWarsTest.pdb'
if (Test-Path -LiteralPath $symbols) {
    Copy-Item -LiteralPath $symbols -Destination $packageBin -Force
}

$checksums = Get-ChildItem -LiteralPath $packageRoot -Recurse -File | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($resolvedPackage.Length + 1).Replace('\', '/')
    '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash, $relative
}
Set-Content -LiteralPath (Join-Path $packageRoot 'SHA256SUMS.txt') -Value $checksums -Encoding Ascii

Write-Host "Prepared isolated test package: $packageRoot"
