$ErrorActionPreference = 'Stop'
$module = Split-Path $PSScriptRoot
$project = Join-Path $module 'AgesOfCalradiaWarScoreRecovery.csproj'
$manifest = Join-Path $module 'SubModule.xml'
$output = Join-Path $module 'bin\Win64_Shipping_Client\AgesOfCalradia.WarScoreRecovery.dll'

if (!(Test-Path -LiteralPath $output)) { throw 'Release output is missing.' }
[xml]$xml = Get-Content -Raw -LiteralPath $manifest
if ($xml.Module.Id.value -ne 'AgesOfCalradiaWarScoreRecovery') { throw 'Manifest module identity changed.' }
if ($xml.Module.SubModules.SubModule.DLLName.value -ne 'AgesOfCalradia.WarScoreRecovery.dll') { throw 'Manifest DLL identity changed.' }
if (@($xml.Module.DependedModules.DependedModule | Where-Object { $_.Id -eq 'AgesOfCalradia' }).Count -ne 1) { throw 'Protected Core dependency must be declared exactly once.' }
if ((Get-ChildItem -LiteralPath (Split-Path $output) -File | Where-Object { $_.Name -eq 'AgesOfCalradia.dll' }).Count -ne 0) { throw 'Sidecar output must not contain or replace protected Core.' }
if ((Get-Content -Raw -LiteralPath $project) -notmatch 'WarScoreRecoveryPatch.cs') { throw 'Recovery patch was omitted from Release project.' }
foreach ($requiredSource in @('WarOccupationOutcome.cs', 'WarPeaceOutcomePatch.cs', 'WarScoreTrace.cs', 'WarScoreTraceBehavior.cs')) {
    if ((Get-Content -Raw -LiteralPath $project) -notmatch [regex]::Escape($requiredSource)) { throw "$requiredSource was omitted from Release project." }
}
if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'Runtime-Acceptance.md'))) { throw 'Live acceptance workflow is missing.' }
'PASS: sidecar manifest, identity, and protected-Core separation verified.'
