param([switch]$SkipPackage)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Isolated test-module gate, never a rebuild of the protected Core project.
# Each reflection verifier has its own process so it cannot lock the next build.
function Invoke-CheckedScript([string]$Path) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Path
    if ($LASTEXITCODE -ne 0) { throw "Verification failed: $Path (exit $LASTEXITCODE)" }
}
function Build-Checked([string]$Path) {
    & dotnet msbuild $Path /t:Rebuild /p:Configuration=Release /p:UseSharedCompilation=false /p:TreatWarningsAsErrors=true /v:minimal
    if ($LASTEXITCODE -ne 0) { throw "Release build failed: $Path" }
}
Build-Checked (Join-Path $PSScriptRoot 'AgesOfCalradiaInternalWarsTest.csproj')
foreach ($verifier in @('Record', 'Eligibility', 'RaidLifecycle', 'Controller', 'RaidArrival', 'PoliticalAi', 'Sally', 'Relief', 'NonCapturingQuest', 'Compensation', 'Governance', 'Strategy', 'Acceptance')) {
    Build-Checked (Join-Path $PSScriptRoot "Tests\InternalWar${verifier}Verifier.csproj")
    & (Join-Path $PSScriptRoot "Tests\bin\Release\InternalWar${verifier}Verifier.exe")
    if ($LASTEXITCODE -ne 0) { throw "Behavior verifier failed: $verifier" }
}
# This source-compilation verifier uses PowerShell 7's Roslyn compiler (nameof/C# 6).
foreach ($sourceCheck in @('Verify-InternalWarScopedDataStore.ps1', 'Verify-InternalWarRaidJoinPolicy.ps1', 'Verify-InternalWarRecoveryState.ps1', 'Verify-InternalWarDiagnosticMonitor.ps1')) {
    & pwsh.exe -NoProfile -File (Join-Path $PSScriptRoot "Tests\$sourceCheck")
    if ($LASTEXITCODE -ne 0) { throw "Source compilation verification failed: $sourceCheck" }
}
foreach ($script in @('Audit-InternalWarNativeContracts.ps1',
    'Audit-InternalWarExpandedNativeContracts.ps1', 'Verify-InternalWarPatchBindings.ps1',
    'Verify-InternalWarPatchTransforms.ps1', 'Verify-InternalWarDiagnosticFormatting.ps1', 'Tests\Verify-InternalWarDiagnosticSummary.ps1')) {
    Invoke-CheckedScript (Join-Path $PSScriptRoot $script)
}
Invoke-CheckedScript (Join-Path (Split-Path $PSScriptRoot -Parent) 'Verify-ProtectedPoliticalBaseline.ps1')
if (-not $SkipPackage) {
    Invoke-CheckedScript (Join-Path $PSScriptRoot 'Build-InternalWarTestPackage.ps1')
    Invoke-CheckedScript (Join-Path $PSScriptRoot 'Verify-InternalWarTestModule.ps1')
}
Invoke-CheckedScript (Join-Path $PSScriptRoot 'Verify-InternalWarExpandedModule.ps1')
Write-Host 'Isolated internal-war system gate passed. No installed game files or campaign saves were changed; live gameplay verification remains required.'
