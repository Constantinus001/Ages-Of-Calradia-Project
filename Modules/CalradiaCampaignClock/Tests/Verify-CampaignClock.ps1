param(
    [string]$ModuleRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$AssemblyPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'bin\Win64_Shipping_Client\CalradiaCampaignClock.dll')
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $ModuleRoot)
$projectPath = Join-Path $ModuleRoot 'CalradiaCampaignClock.csproj'
$subModulePath = Join-Path $ModuleRoot 'SubModule.xml'
$settingsPath = Join-Path $ModuleRoot 'CalradiaCampaignClock.settings.xml'
$extensionPath = Join-Path $ModuleRoot 'MapBarClockExtensions.cs'
$subModuleSourcePath = Join-Path $ModuleRoot 'CampaignClockSubModule.cs'
$releaseScriptPath = Join-Path $ModuleRoot 'New-CampaignClockRelease.ps1'
$verifierProject = Join-Path $PSScriptRoot 'ClockFormatterVerifier.csproj'

foreach ($requiredPath in @(
    $projectPath,
    $subModulePath,
    $settingsPath,
    $extensionPath,
    $subModuleSourcePath,
    $releaseScriptPath,
    $AssemblyPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required campaign-clock artifact is missing: $requiredPath"
    }
}

$protectedDll = Join-Path $repoRoot 'bin\Win64_Shipping_Client\AgesOfCalradia.dll'
$protectedPrefab = Join-Path $repoRoot 'GUI\Prefabs\WorldCalendar\WorldCalendar.xml'
$dllHash = (Get-FileHash -LiteralPath $protectedDll -Algorithm SHA256).Hash
$prefabHash = (Get-FileHash -LiteralPath $protectedPrefab -Algorithm SHA256).Hash
if ($dllHash -ne '560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E') {
    throw "Protected AgesOfCalradia.dll changed: $dllHash"
}
if ($prefabHash -ne 'E7013CF2B18B381119CC7479F0840BC423CD59565913BD22BBFC1E0C55A82E5E') {
    throw "Protected WorldCalendar.xml changed: $prefabHash"
}

[xml]$subModule = Get-Content -LiteralPath $subModulePath -Raw
if ($subModule.Module.Id.value -ne 'CalradiaCampaignClock' -or
    $subModule.Module.SubModules.SubModule.DLLName.value -ne 'CalradiaCampaignClock.dll' -or
    $subModule.Module.SubModules.SubModule.SubModuleClassType.value -ne 'CalradiaCampaignClock.CampaignClockSubModule') {
    throw 'The standalone module descriptor identity is invalid.'
}

$requiredDependencies = @('Bannerlord.Harmony', 'Bannerlord.UIExtenderEx', 'Native', 'SandBoxCore', 'Sandbox')
$dependencyIds = @($subModule.Module.DependedModules.DependedModule | ForEach-Object { $_.Id })
foreach ($dependency in $requiredDependencies) {
    if ($dependencyIds -notcontains $dependency) {
        throw "Standalone clock dependency is missing: $dependency"
    }
}
if ($dependencyIds -contains 'AgesOfCalradia') {
    throw 'The standalone clock must not require Ages of Calradia.'
}

$packagedMapBar = Join-Path $ModuleRoot 'GUI\Prefabs\Map\MapBar.xml'
if (Test-Path -LiteralPath $packagedMapBar) {
    throw 'The standalone clock must not package a full MapBar.xml override.'
}

$releaseScriptSource = Get-Content -LiteralPath $releaseScriptPath -Raw
foreach ($releaseContract in @(
    'CalradiaCampaignClock/bin/Win64_Shipping_Client/CalradiaCampaignClock.dll',
    'CalradiaCampaignClock/CalradiaCampaignClock.settings.xml',
    'CalradiaCampaignClock/SubModule.xml',
    'CalradiaCampaignClock/README.md')) {
    if (-not $releaseScriptSource.Contains($releaseContract)) {
        throw "Player-package allowlist is missing: $releaseContract"
    }
}
$allowlistMatch = [regex]::Match(
    $releaseScriptSource,
    '(?s)\$expectedEntries\s*=\s*@\((?<body>.*?)\)')
if (-not $allowlistMatch.Success) {
    throw 'Player-package allowlist could not be parsed.'
}
$allowlistBody = $allowlistMatch.Groups['body'].Value
foreach ($forbiddenReleaseEntry in @('.pdb', '/obj/', '/Tests/', '.exe')) {
    if ($allowlistBody.Contains($forbiddenReleaseEntry)) {
        throw "Player-package allowlist contains a forbidden entry: $forbiddenReleaseEntry"
    }
}

$extensionSource = Get-Content -LiteralPath $extensionPath -Raw
$subModuleSource = Get-Content -LiteralPath $subModuleSourcePath -Raw
foreach ($contract in @(
    '[ViewModelMixin(nameof(MapTimeControlVM.Tick), true)]',
    'CampaignTime.Now.ToHours',
    '% CampaignTime.HoursInDay',
    'viewModel.Time = hourInDay',
    'Id=\"CalradiaCampaignClockText\"',
    'Text=\"@CalradiaCampaignClockText\"',
    "descendant::MapCurrentTimeVisualWidget[@Id='CenterPanel']/Children")) {
    if (-not $extensionSource.Contains($contract)) {
        throw "Campaign clock integration contract is missing: $contract"
    }
}
if (-not $subModuleSource.Contains('ClockOwnerCompatibility.HasAgesOfCalradiaClock()')) {
    throw 'Duplicate Ages of Calradia clock ownership is not guarded.'
}

$allProductionSource = Get-Content -LiteralPath @(
    (Join-Path $ModuleRoot 'CampaignClockSubModule.cs'),
    (Join-Path $ModuleRoot 'CampaignClockSettings.cs'),
    (Join-Path $ModuleRoot 'ClockFormatter.cs'),
    (Join-Path $ModuleRoot 'ClockOwnerCompatibility.cs'),
    $extensionPath) -Raw
foreach ($forbidden in @(
    'MapTimeTracker',
    'TickMapTime',
    'SpeedUpMultiplier',
    'MapWeatherModel',
    'DaysInYear',
    'CalendarFormatter',
    'WorldCalendar')) {
    if ($allProductionSource -match [regex]::Escape($forbidden)) {
        throw "Standalone clock crossed an excluded gameplay/calendar boundary: $forbidden"
    }
}

dotnet build $verifierProject -c Release --nologo | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw 'Clock formatter verifier failed to build.'
}
$verifierExe = Join-Path $PSScriptRoot 'bin\Release\net472\ClockFormatterVerifier.exe'
& $verifierExe
if ($LASTEXITCODE -ne 0) {
    throw 'Clock formatter verification failed.'
}

Write-Output 'PASS: standalone campaign clock is additive, independently packaged, protected-baseline safe, duplicate-owner guarded, gameplay-neutral, and format-verified.'
