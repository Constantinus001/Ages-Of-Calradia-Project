param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [string]$InstalledModuleRoot = 'C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\AOC CORE',
    [switch]$SkipInstalled
)

$ErrorActionPreference = 'Stop'
$approvedHash = '560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E'
$approvedWorldEventsHash = 'E7013CF2B18B381119CC7479F0840BC423CD59565913BD22BBFC1E0C55A82E5E'
$repositoryDll = Join-Path $Root 'bin\Win64_Shipping_Client\AgesOfCalradia.dll'
$repositoryWorldEvents = Join-Path $Root 'GUI\Prefabs\WorldCalendar\WorldCalendar.xml'

function Assert-ApprovedDll([string]$Path, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label is missing: $Path"
    }
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if (-not [string]::Equals($actual, $approvedHash, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label changed the protected political renderer. Expected $approvedHash but found $actual. Do not deploy this build; use a separate sidecar module."
    }
}

function Assert-ApprovedWorldEvents([string]$Path, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label is missing: $Path"
    }
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if (-not [string]::Equals($actual, $approvedWorldEventsHash, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label changed the protected World Events layout. Expected $approvedWorldEventsHash but found $actual. New controls must use a separate sidecar integration."
    }
}

Assert-ApprovedDll $repositoryDll 'Repository main DLL'
Assert-ApprovedWorldEvents $repositoryWorldEvents 'Repository World Events prefab'
if (-not $SkipInstalled) {
    Assert-ApprovedDll (Join-Path $InstalledModuleRoot 'bin\Win64_Shipping_Client\AgesOfCalradia.dll') 'Installed main DLL'
    Assert-ApprovedWorldEvents (Join-Path $InstalledModuleRoot 'GUI\Prefabs\WorldCalendar\WorldCalendar.xml') 'Installed World Events prefab'
}

Write-Host 'Protected political renderer verification passed.'
