param([string]$ModuleRoot = (Split-Path -Parent $PSScriptRoot))
$itemFile = Join-Path $ModuleRoot 'ModuleData\supply_items.xml'
$moduleFile = Join-Path $ModuleRoot 'SubModule.xml'
[xml]$items = Get-Content -Raw $itemFile
[xml]$module = Get-Content -Raw $moduleFile
$supply = @($items.Items.Item | Where-Object { $_.id -eq 'aoc_logistics_supply' })
if ($supply.Count -ne 1) { throw 'Expected exactly one aoc_logistics_supply item.' }
if ($supply[0].mesh -ne 'crate_a' -or $supply[0].is_merchandise -ne 'true' -or $supply[0].Type -ne 'Goods') {
    throw 'Supply must be a merchantable Goods item using the crate_a mesh.'
}
$dependency = @($module.Module.DependedModules.DependedModule | Where-Object { $_.Id -eq 'AgesOfCalradia' })
if ($dependency.Count -ne 1) { throw 'The logistics module must explicitly load after AgesOfCalradia.' }
if ($module.Module.Version.value -ne 'v0.3.0' -or $dependency[0].DependentVersion -ne 'v1.5.14') {
    throw 'The standalone Logistics version or AOC CORE compatibility version is incorrect.'
}
foreach ($nativeId in @('Native', 'SandBoxCore', 'Sandbox')) {
    $nativeDependency = @($module.Module.DependedModules.DependedModule | Where-Object { $_.Id -eq $nativeId })
    if ($nativeDependency.Count -ne 1 -or $nativeDependency[0].DependentVersion -ne 'v1.4.8') {
        throw "Logistics must target Bannerlord v1.4.8: $nativeId"
    }
}
if (-not (Test-Path (Join-Path $ModuleRoot 'LogisticsReserveBehavior.cs'))) {
    throw 'The finite reserve behaviour is missing.'
}
$behavior = Get-Content -Raw (Join-Path $ModuleRoot 'LogisticsReserveBehavior.cs')
$menuPath = Join-Path $ModuleRoot 'LogisticsMenuBehavior.cs'
$menu = if (Test-Path -LiteralPath $menuPath) { Get-Content -Raw -LiteralPath $menuPath } else { '' }
if (($behavior -notmatch 'DailyTickTownEvent') -or
    ($menu -notmatch 'aoc_logistics_load_baggage') -or
    ($menu -notmatch 'aoc_logistics_provision_baggage')) {
    throw 'Market restocking and the baggage-loading town option are required.'
}
foreach ($contract in @('aoc_logistics_reserves', 'aoc_logistics_supply_debt',
    'aoc_logistics_last_supply_day', 'ProcessDailySupply', 'TryProcureAiSupply')) {
    if ($behavior -notmatch [regex]::Escape($contract)) { throw "The campaign supply contract is missing: $contract" }
}
$mathPath = Join-Path $ModuleRoot 'LogisticsSupplyMath.cs'
$aiPath = Join-Path $ModuleRoot 'LogisticsAiProcurementService.cs'
$marketPath = Join-Path $ModuleRoot 'LogisticsSupplyMarketService.cs'
if (-not (Test-Path -LiteralPath $mathPath) -or -not (Test-Path -LiteralPath $aiPath) -or -not (Test-Path -LiteralPath $marketPath)) {
    throw 'Pure supply math, AI procurement, or settlement-market integration is missing.'
}
$market = Get-Content -Raw -LiteralPath $marketPath
if ($market -notmatch 'Settlement\.ItemRoster' -or $market -match 'Owner\.ItemRoster') {
    throw 'Supply transactions must use the settlement market roster, never Town.Owner.ItemRoster.'
}
if (-not (Test-Path (Join-Path $ModuleRoot 'BaggageTrainMissionBehavior.cs'))) {
    throw 'The battlefield baggage train behaviour is missing.'
}
$battleBehavior = Get-Content -Raw (Join-Path $ModuleRoot 'BaggageTrainMissionBehavior.cs')
if ($battleBehavior -notmatch 'SupplyRadiusMeters = 6' -or $battleBehavior -notmatch 'BaggageTrainRegistry.Register') {
    throw 'A six-metre baggage supply range is required.'
}
if ($battleBehavior -notmatch 'ComputeSpawnPathDeploymentOffset' -or $battleBehavior -notmatch 'frame.IsValid') {
    throw 'Baggage trains must use a validated vanilla deployment frame.'
}
if ($battleBehavior -notmatch 'RearDeploymentDistanceMeters = 20f' -or $battleBehavior -notmatch 'MinimumWagonCount = 2' -or $battleBehavior -notmatch 'MaximumWagonCount = 6' -or $battleBehavior -notmatch 'CalculateWagonCount' -or $battleBehavior -notmatch 'IntactWagonPrefabNames' -or $battleBehavior -notmatch 'bd_hay_cart_b') {
    throw 'Baggage trains must scale a validated wagon footprint to party strength and reserve.'
}
if (-not (Test-Path (Join-Path $ModuleRoot 'BaggageResupplyMissionBehavior.cs'))) {
    throw 'The finite ammunition resupply behaviour is missing.'
}
if (-not (Test-Path (Join-Path $ModuleRoot 'BaggageGuardMissionBehavior.cs'))) {
    throw 'The baggage guard behaviour is missing.'
}
$raidPath = Join-Path $ModuleRoot 'BaggageTrainRaidMissionBehavior.cs'
if (-not (Test-Path -LiteralPath $raidPath)) { throw 'The baggage train capture behaviour is missing.' }
$raidBehavior = Get-Content -Raw -LiteralPath $raidPath
if ($raidBehavior -notmatch 'CaptureSeconds = 12f' -or $raidBehavior -notmatch 'TryCapture' -or $raidBehavior -notmatch 'BurnReserve') {
    throw 'Baggage capture must require sustained occupation, disable resupply, and burn bounded reserve.'
}
$resupplyBehavior = Get-Content -Raw (Join-Path $ModuleRoot 'BaggageResupplyMissionBehavior.cs')
if ($resupplyBehavior -notmatch 'RoundsPerReservePoint = 3' -or $resupplyBehavior -notmatch 'TryConsumeReserve') {
    throw 'Battle resupply must transfer finite reserve into ammunition.'
}
if (-not (Test-Path (Join-Path $ModuleRoot 'LogisticsDiagnostics.cs'))) {
    throw 'The logistics diagnostic logger is missing.'
}
$subModule = Get-Content -Raw (Join-Path $ModuleRoot 'LogisticsSubModule.cs')
if ($subModule -match 'mission != null && mission.IsFieldBattle') {
    throw 'Mission behaviours must be registered before Bannerlord assigns field-battle state.'
}
if ($subModule -notmatch 'BaggageTrainRaidMissionBehavior' -or $subModule -notmatch 'LogisticsPlayerSupplyNotificationBehavior') {
    throw 'Supply warning and baggage capture mission behaviours are not registered.'
}
$speedModelPath = Join-Path $ModuleRoot 'LogisticsPartySpeedModel.cs'
if (-not (Test-Path -LiteralPath $speedModelPath)) { throw 'The logistics campaign speed model is missing.' }
$speedModel = Get-Content -Raw -LiteralPath $speedModelPath
foreach ($required in @('BaseTravelSpeed = 4f', 'MaximumMapSpeed = 8f',
    'nativeFinalSpeed / nativeBaseSpeed * BaseTravelSpeed',
    'LimitMax(LogisticsPartySpeedMath.MaximumMapSpeed)',
    'TwelveMonthCalendar.CalendarPartySpeedModel')) {
    if ($speedModel -notmatch [regex]::Escape($required)) { throw "The 4/8 campaign speed contract is missing: $required" }
}
if ($subModule -notmatch 'AddModel\(new LogisticsPartySpeedModel') { throw 'The logistics campaign speed model is not registered.' }
if ($speedModel -notmatch 'GetSpeedFactor' -or $speedModel -notmatch 'Operational supply condition') {
    throw 'Supply condition is not applied through the existing speed wrapper.'
}
$resupplyBehavior = Get-Content -Raw (Join-Path $ModuleRoot 'BaggageResupplyMissionBehavior.cs')
if ($resupplyBehavior -notmatch 'AfterStart' -or $resupplyBehavior -notmatch '_reservePartiesBySide' -or $resupplyBehavior -notmatch 'TryConsumeReserve\(reserveParties') {
    throw 'Battle resupply must cache and debit the deterministic coalition reserve pool.'
}
$guardBehavior = Get-Content -Raw (Join-Path $ModuleRoot 'BaggageGuardMissionBehavior.cs')
if ($guardBehavior -notmatch 'FormationClass\.Infantry' -or $guardBehavior -notmatch 'HasPlayerControlledTroop') {
    throw 'Baggage guards must be detached only from non-player infantry formations.'
}
if ($battleBehavior -notmatch 'TrySpawnForSide' -or $battleBehavior -notmatch 'LogisticsDiagnostics\.Error') {
    throw 'Baggage-train native prefab failures must be logged and fail safely.'
}
$mathVerifier = Join-Path $PSScriptRoot 'LogisticsSupplyMathVerifier.csproj'
& dotnet run --project $mathVerifier --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Compiled logistics supply math verification failed.' }
Write-Host 'Supply item and module dependency contract verified.'
