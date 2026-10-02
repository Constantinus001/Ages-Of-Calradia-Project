param([string]$PackageRoot)

$ErrorActionPreference = 'Stop'
$systemsRoot = Split-Path -Parent $PSScriptRoot
$modulesRoot = Split-Path -Parent $systemsRoot
[xml]$manifest = Get-Content -LiteralPath (Join-Path $systemsRoot 'SubModule.xml') -Raw

if ($manifest.Module.Name.value -ne 'AOC SYSTEMS R & S' -or
    $manifest.Module.Id.value -ne 'AgesOfCalradiaSystemsRS' -or
    $manifest.Module.Version.value -ne 'v1.1.0') {
    throw 'R & S manifest name, ID, or version is incorrect.'
}

$expected = @(
    @{ Name = 'AOC SYSTEMS R & S: Religions'; DLL = 'AgesOfCalradiaReligions.dll'; Class = 'AgesOfCalradiaReligions.ReligionSubModule' },
    @{ Name = 'AOC SYSTEMS R & S: Succession'; DLL = 'AgesOfCalradiaSuccession.dll'; Class = 'AgesOfCalradiaSuccession.SuccessionSubModule' },
    @{ Name = 'AOC SYSTEMS R & S: Shipwright'; DLL = 'AgesOfCalradiaReligions.Shipwright.dll'; Class = 'AgesOfCalradiaReligions.Shipwright.ShipwrightSubModule' }
)
$actual = @($manifest.Module.SubModules.SubModule)
if ($actual.Count -ne 3) { throw "R & S must declare exactly three submodules; found $($actual.Count)." }
for ($index = 0; $index -lt 3; $index++) {
    if ($actual[$index].Name.value -ne $expected[$index].Name -or
        $actual[$index].DLLName.value -ne $expected[$index].DLL -or
        $actual[$index].SubModuleClassType.value -ne $expected[$index].Class) {
        throw "Incorrect R & S submodule at position $($index + 1)."
    }
}

$dependencies = @($manifest.Module.DependedModules.DependedModule | ForEach-Object { $_.Id })
if ($dependencies -notcontains 'AgesOfCalradia') { throw 'R & S must load after AOC CORE.' }
if ($dependencies -contains 'AgesOfCalradiaReligions') { throw 'Succession must use the Religion assembly internally, not its retired module identity.' }
$navalMetadata = @($manifest.Module.DependedModuleMetadatas.DependedModuleMetadata | Where-Object { $_.id -eq 'NavalDLC' })
if ($navalMetadata.Count -ne 1 -or $navalMetadata[0].optional -ne 'true' -or $navalMetadata[0].version -ne 'v1.2.8') {
    throw 'R & S must declare its audited War Sails v1.2.8 integration as optional.'
}
$incompatible = @($manifest.Module.IncompatibleModules.Module | ForEach-Object { $_.Id })
foreach ($id in @('AgesOfCalradiaSystems', 'AgesOfCalradiaReligions', 'AgesOfCalradiaSuccession')) {
    if ($incompatible -notcontains $id) { throw "R & S must reject duplicate module identity: $id" }
}

$religionDiagnostics = Get-Content -LiteralPath (Join-Path $modulesRoot 'AgesOfCalradiaReligions\ReligionDiagnostics.cs') -Raw
$successionDiagnostics = Get-Content -LiteralPath (Join-Path $modulesRoot 'AgesOfCalradiaSuccession\SuccessionDiagnostics.cs') -Raw
foreach ($source in @($religionDiagnostics, $successionDiagnostics)) {
    if ($source -notmatch 'Assembly\.GetExecutingAssembly\(\)\.Location' -or $source -match '"Modules",\s*"AgesOfCalradia') {
        throw 'R & S diagnostics must resolve their module folder from the assembly location.'
    }
}

$religion = Get-Content -LiteralPath (Join-Path $modulesRoot 'AgesOfCalradiaReligions\ReligionCampaignBehavior.cs') -Raw
$population = Get-Content -LiteralPath (Join-Path $modulesRoot 'AgesOfCalradiaReligions\PopulationCampaignBehavior.cs') -Raw
$succession = Get-Content -LiteralPath (Join-Path $modulesRoot 'AgesOfCalradiaSuccession\SuccessionCampaignBehavior.cs') -Raw
foreach ($token in @('AgesOfCalradiaReligions.HeroFaithV1', 'AgesOfCalradiaReligions.RealmFaithV1')) {
    if ($religion -notmatch [regex]::Escape($token)) { throw "Religion save key changed: $token" }
}
if ($population -notmatch 'AgesOfCalradiaReligions.PopulationStateV1') { throw 'Population save key changed.' }
foreach ($token in @('AOC_Succession_State_v2', 'AOC_Succession_Politics_v1')) {
    if ($succession -notmatch [regex]::Escape($token)) { throw "Succession save key changed: $token" }
}

if (-not [string]::IsNullOrWhiteSpace($PackageRoot)) {
    [xml]$packageManifest = Get-Content -LiteralPath (Join-Path $PackageRoot 'SubModule.xml') -Raw
    if ($packageManifest.Module.Id.value -ne 'AgesOfCalradiaSystemsRS') { throw 'Packaged R & S manifest is incorrect.' }
    foreach ($path in @(
        'bin\Win64_Shipping_Client\AgesOfCalradiaReligions.dll',
        'bin\Win64_Shipping_Client\AgesOfCalradiaReligions.Shipwright.dll',
        'bin\Win64_Shipping_Client\AgesOfCalradiaSuccession.dll',
        'ModuleData\religions.json',
        'ModuleData\holy_sites.json',
        'GUI\Prefabs\AocStrategicMapModes.xml',
        'GUI\CustomUI\WorldEventsSkin\page_cabinet_census_v1.png')) {
        if (-not (Test-Path -LiteralPath (Join-Path $PackageRoot $path) -PathType Leaf)) { throw "Packaged R & S file is missing: $path" }
    }
    if (Test-Path -LiteralPath (Join-Path $PackageRoot 'GUI\CustomUI\WorldEventsSkin\page_cabinet_census_v1_source.png')) {
        throw 'Editable Census source entered the R & S runtime package.'
    }
    $developmentFiles = @(Get-ChildItem -LiteralPath $PackageRoot -File -Recurse | Where-Object { $_.Extension -in @('.pdb', '.txt') })
    if ($developmentFiles.Count -gt 0) { throw "Development files entered R & S: $($developmentFiles.FullName -join ', ')" }
}

Write-Output 'PASS: AOC SYSTEMS R & S ordering, Shipwright sidecar, assets, incompatibilities, and save contracts verified.'
