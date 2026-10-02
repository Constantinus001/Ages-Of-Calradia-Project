$ErrorActionPreference = 'Stop'
$module = Split-Path $PSScriptRoot
Add-Type -Path @((Join-Path $module 'WarScoreRecoveryMath.cs'), (Join-Path $module 'WarOccupationOutcome.cs'), (Join-Path $PSScriptRoot 'WarScoreRecoveryVerifier.cs'))
[WarScoreRecoveryVerifier]::Run()
