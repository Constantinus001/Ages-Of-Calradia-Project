[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$CaptureDirectory,
    [string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$capture = (Resolve-Path -LiteralPath $CaptureDirectory).ProviderPath
if (-not (Test-Path -LiteralPath $capture -PathType Container)) { throw 'CaptureDirectory must be a directory.' }
if (-not $OutputPath) { $OutputPath = Join-Path $capture 'capture-analysis.md' }
$output = [IO.Path]::GetFullPath($OutputPath)
if ([IO.Path]::GetExtension($output) -ne '.md') { throw 'OutputPath must be a Markdown (.md) file, preserving capture inputs.' }
if (-not ('AgesOfCalradia.PoliticalRenderDiagnostics.CaptureAnalyzer' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'CaptureAnalyzer.cs')
}
$result = [AgesOfCalradia.PoliticalRenderDiagnostics.CaptureAnalyzer]::Analyze($capture)
[IO.File]::WriteAllText($output, $result.Markdown, [Text.UTF8Encoding]::new($false))
Write-Output $output
