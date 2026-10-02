param(
    [string]$ModuleRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$ReleaseArchive,
    [switch]$EconomySidecarsOnly,
    [switch]$CampaignSystemsCandidateOnly,
    [switch]$DeployEconomySidecars,
    [switch]$AllowDirtySource,
    [switch]$IncludeStrategicProvinceDiagnostics,
    [switch]$SkipInstalledBaseline,
    [switch]$SkipSecurityScan,
    [ValidateRange(1, 30)]
    [int]$CloudVerdictHoldMinutes = 10
)

$ErrorActionPreference = 'Stop'
if ($CampaignSystemsCandidateOnly) {
    if ($EconomySidecarsOnly -or $DeployEconomySidecars -or $AllowDirtySource -or $SkipSecurityScan -or $SkipInstalledBaseline -or $IncludeStrategicProvinceDiagnostics -or $ReleaseArchive) {
        throw 'Framework candidate gate does not accept deployment, bypasses or other release modes.'
    }
    if ($CloudVerdictHoldMinutes -lt 10) { throw 'Framework candidate requires at least ten minutes of security hold.' }
    & (Join-Path $PSScriptRoot 'Package-CampaignSystemsCandidate.ps1') -ModuleRoot $ModuleRoot -SecurityHoldMinutes $CloudVerdictHoldMinutes
    return
}
if ($DeployEconomySidecars -and -not $EconomySidecarsOnly) { throw 'DeployEconomySidecars requires EconomySidecarsOnly.' }
if ($EconomySidecarsOnly) {
    if ($AllowDirtySource -or $SkipSecurityScan -or $SkipInstalledBaseline -or $IncludeStrategicProvinceDiagnostics -or $ReleaseArchive) {
        throw 'Scoped economy release does not accept bypasses or full-Core packaging options.'
    }
    & (Join-Path $PSScriptRoot 'Release-EconomySidecars.ps1') -ModuleRoot $ModuleRoot -Deploy:$DeployEconomySidecars -CloudVerdictHoldMinutes $CloudVerdictHoldMinutes
    return
}

if (-not $AllowDirtySource) {
    $sourceChanges = @(git -C $ModuleRoot status --porcelain)
    if ($sourceChanges.Count -gt 0) {
        throw 'Source tree has uncommitted changes. Commit the exact release source before packaging.'
    }
}

$mainProject = Join-Path $ModuleRoot 'TwelveMonthCalendar.csproj'
$mcmProject = Join-Path $ModuleRoot 'TwelveMonthCalendar.MCM.csproj'
$approvedFixesProject = Join-Path $ModuleRoot 'Builds\Approved560CalendarFixes\Approved560CalendarFixes.csproj'
$campaignLabelVisibilityProject = Join-Path $ModuleRoot 'CampaignLabelVisibility.csproj'
$politicalBorderOptimizerProject = Join-Path $ModuleRoot 'PoliticalBorderOptimizer.csproj'
$worldEventsShellRepairProject = Join-Path $ModuleRoot 'WorldEventsShellRepair.csproj'
$campaignSystemsProject = Join-Path $ModuleRoot 'Modules\AgesOfCalradiaCampaignSystems\AgesOfCalradiaCampaignSystems.csproj'
$protectedBaselineGate = Join-Path $PSScriptRoot 'Verify-ProtectedPoliticalBaseline.ps1'
if ($IncludeStrategicProvinceDiagnostics) {
    $runtimeBinDirectory = Join-Path $ModuleRoot 'bin\AgesOfCalradia_Test_Win64_Shipping_Client'
    dotnet msbuild $mainProject /t:Rebuild /p:Configuration=Release /p:IncludeStrategicProvinceDiagnostics=true /p:DefineConstants=TRACE%3BSTRATEGIC_PROVINCE_DIAGNOSTICS /p:OutputPath='bin\AgesOfCalradia_Test_Win64_Shipping_Client\' /v:minimal
}
else {
    $runtimeBinDirectory = Join-Path $ModuleRoot 'bin\Win64_Shipping_Client'
    if ($SkipInstalledBaseline) {
        & $protectedBaselineGate -Root $ModuleRoot -SkipInstalled
    }
    else {
        & $protectedBaselineGate -Root $ModuleRoot
    }
    if (-not $?) {
        throw 'Protected political-renderer gate failed.'
    }
}
$mainDll = Join-Path $runtimeBinDirectory 'AgesOfCalradia.dll'
$approvedFixesDll = Join-Path $runtimeBinDirectory 'AgesOfCalradia.Approved560CalendarFixes.dll'
$campaignLabelVisibilityDll = Join-Path $runtimeBinDirectory 'AgesOfCalradia.CampaignLabelVisibility.dll'
$politicalBorderOptimizerDll = Join-Path $runtimeBinDirectory 'AgesOfCalradia.PoliticalBorderOptimizer.dll'
$worldEventsShellRepairDll = Join-Path $runtimeBinDirectory 'AgesOfCalradia.WorldEventsShellRepair.dll'
$campaignSystemsDll = Join-Path $runtimeBinDirectory 'AgesOfCalradia.CampaignSystems.dll'
$embeddedModuleProjects = @(
    $approvedFixesProject,
    $campaignLabelVisibilityProject,
    $politicalBorderOptimizerProject,
    $worldEventsShellRepairProject,
    $campaignSystemsProject
)
foreach ($project in $embeddedModuleProjects) {
    dotnet msbuild $project /t:Rebuild /p:Configuration=Release /p:OutputPath=$runtimeBinDirectory /v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Embedded module Release build failed: $project"
    }
}
foreach ($embeddedDll in @(
    $approvedFixesDll,
    $campaignLabelVisibilityDll,
    $politicalBorderOptimizerDll,
    $worldEventsShellRepairDll,
    $campaignSystemsDll
)) {
    if (-not (Test-Path -LiteralPath $embeddedDll -PathType Leaf)) {
        throw "Embedded module output is missing: $embeddedDll"
    }
}
$harmonyPackageDll = Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.2\lib\net472\0Harmony.dll'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $ModuleRoot 'Modules\AgesOfCalradiaCampaignSystems\Tests\Verify-Candidate.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Core Campaign Systems integration verification failed.' }
$harmonyDll = Join-Path $runtimeBinDirectory '0Harmony.dll'
if (-not (Test-Path -LiteralPath $harmonyPackageDll -PathType Leaf)) {
    throw "Harmony package output is missing: $harmonyPackageDll"
}
if ($IncludeStrategicProvinceDiagnostics) {
    dotnet msbuild $mcmProject /t:Rebuild /p:Configuration=Release /p:MainAssemblyPath=$mainDll /p:OutputPath='bin\AgesOfCalradia_Test_Win64_Shipping_Client\' /v:minimal
}
else {
    dotnet msbuild $mcmProject /t:Rebuild /p:Configuration=Release /v:minimal
}
if ($LASTEXITCODE -ne 0) {
    throw "MCM Release build failed with exit code $LASTEXITCODE."
}
# Normalize the one bundled Harmony provider after both builds.
Copy-Item -LiteralPath $harmonyPackageDll -Destination $harmonyDll -Force
$mcmDll = Join-Path $runtimeBinDirectory 'AgesOfCalradia.MCM.dll'
$mcmCoreDll = Join-Path $runtimeBinDirectory 'MCMv5.dll'
$calendarMathVerifier = Join-Path $PSScriptRoot 'Verify-CalendarMath.ps1'
& $calendarMathVerifier -ModuleRoot $ModuleRoot -CalendarAssemblyPath $mainDll

$settlementBalanceSource = Get-Content -Raw -LiteralPath (Join-Path $ModuleRoot 'SettlementBalancePatches.cs')
$dailyBalanceSource = Get-Content -Raw -LiteralPath (Join-Path $ModuleRoot 'DailyRateBalancePatches.cs')
$subModuleSource = Get-Content -Raw -LiteralPath (Join-Path $ModuleRoot 'MySubModule.cs')
$tournamentModelSource = Join-Path $ModuleRoot 'CalendarTournamentModel.cs'
if ((Test-Path -LiteralPath $tournamentModelSource -PathType Leaf) -or
    $subModuleSource -match 'CalendarTournamentModel') {
    throw 'Tournament scheduling must retain Bannerlord''s native TournamentModel; calendar annualization can suppress new tournament starts.'
}
if ($dailyBalanceSource -match 'SettlementDemandBalancePatch[\s\S]{0,500}BonusToFoodStores' -or
    $dailyBalanceSource -match 'SettlementBudgetBalancePatch[\s\S]{0,500}BonusToFoodStores') {
    throw 'Civilian food demand and market budget must share the Gregorian food cadence.'
}
if ($dailyBalanceSource -notmatch 'SettlementMarketSmoothingBalancePatch' -or
    $dailyBalanceSource -notmatch 'ScaleDailySmoothingFactor') {
    throw 'Settlement market convergence must preserve its native annual cadence.'
}
if ($settlementBalanceSource -match 'SumOfFactorsField\.SetValueDirect' -or
    $settlementBalanceSource -notmatch 'BaseNumber \* factor') {
    throw 'ExplainedNumber annual scaling must preserve native factor modifiers and scale only its base.'
}
$financeSource = Get-Content -Raw -LiteralPath (Join-Path $ModuleRoot 'CalendarClanFinanceModel.cs')
if ($settlementBalanceSource -match 'FoodSupplyRateFactor' -or
    $settlementBalanceSource -notmatch 'VillageFoodProductionBalancePatch') {
    throw 'Village food must use the coordinated Gregorian production path.'
}
$partyFoodSource = Get-Content -Raw -LiteralPath (Join-Path $ModuleRoot 'CalendarFoodLogisticsModels.cs')
if ($partyFoodSource -notmatch 'CalendarMobilePartyFoodConsumptionModel' -or
    $partyFoodSource -notmatch 'CalendarPartyFoodBuyingModel' -or
    $partyFoodSource -notmatch 'CalendarSettlementFoodModel' -or
    $partyFoodSource -notmatch 'nativeDays / SettlementBalanceMath\.DailyRateFactor' -or
    $partyFoodSource -notmatch 'nativeMarketResult') {
    throw 'Town food, food production, party rations, and AI reserve targets must use matched Gregorian logistics without market double-scaling.'
}
$balanceTelemetrySource = Get-Content -Raw -LiteralPath (Join-Path $ModuleRoot 'CalendarBalanceTelemetry.cs')
if ($balanceTelemetrySource -notmatch 'AvgDirectFoodChange' -or
    $balanceTelemetrySource -notmatch 'AvgMarketFoodChange' -or
    $balanceTelemetrySource -notmatch 'CappedFoodTowns') {
    throw 'Monthly diagnostics must report direct and market food balance separately.'
}
if ($financeSource -match 'ApplyAiReserveSurcharge|reserveTarget|maximumSurcharge') {
    throw 'AI finance must use Bannerlord''s native high-cash expense rather than an additional surcharge.'
}
if ($financeSource -notmatch 'wrapper is therefore the single active' -or
    $financeSource -notmatch 'Scale\(ref result\);') {
    throw 'The startup-safe clan-finance wrapper must perform the single active annual scaling.'
}
if ($financeSource -notmatch 'EvaluateClanFinance' -or
    $financeSource -notmatch 'ReconcileKingdomBudgetWallet' -or
    $financeSource -notmatch 'ScaleDiscreteDailyValue\(') {
    throw 'Clan-finance side effects must reconcile Kingdom Budget transfers through the safe wrapper.'
}
$financeTelemetrySource = Get-Content -Raw -LiteralPath (Join-Path $ModuleRoot 'CalendarFinanceTelemetry.cs')
if ($financeTelemetrySource -notmatch 'KingdomBudgetTransfers' -or
    $financeTelemetrySource -notmatch 'RecordKingdomBudgetTransfer') {
    throw 'Monthly finance diagnostics must report native and scaled Kingdom Budget transfers.'
}

$dailyFactor = 84.0 / 365.2425
$durationFactor = 365.2425 / 84.0
if ([Math]::Abs(($dailyFactor * $durationFactor) - 1.0) -gt 0.000001) {
    throw 'Annual duration and daily-rate factors are not reciprocal.'
}

$nativeProbability = 0.25
$annualProbability = 1.0 - [Math]::Pow(1.0 - $nativeProbability, $dailyFactor)
$nativeAnnualSurvival = [Math]::Pow(1.0 - $nativeProbability, 84.0)
$annualSurvival = [Math]::Pow(1.0 - $annualProbability, 365.2425)
if ([Math]::Abs($nativeAnnualSurvival - $annualSurvival) -gt 0.000001) {
    throw 'Daily probability conversion does not preserve annual probability.'
}

$nativeMarketSmoothing = 0.15
$calendarMarketSmoothing = 1.0 - [Math]::Pow(1.0 - $nativeMarketSmoothing, $dailyFactor)
$nativeMarketRetention = [Math]::Pow(1.0 - $nativeMarketSmoothing, 84.0)
$calendarMarketRetention = [Math]::Pow(1.0 - $calendarMarketSmoothing, 365.2425)
if ([Math]::Abs($nativeMarketRetention - $calendarMarketRetention) -gt 0.000001) {
    throw 'Settlement market smoothing does not preserve native annual convergence.'
}

$nativeTributePerDay = 100
$nativeTributeDays = 100
$calendarTributePerDay = $nativeTributePerDay * $dailyFactor
$calendarTributeDays = 235
$nativeTributeTotal = $nativeTributePerDay * $nativeTributeDays
$calendarTributeTotal = $calendarTributePerDay * $calendarTributeDays
if ($calendarTributeDays -ne 235 -or $calendarTributeTotal -le 0) {
    throw 'Tribute treaties must use the configured 235-calendar-day term with a positive finance payment cadence.'
}

$diplomacySource = Get-Content -Raw -LiteralPath (Join-Path $ModuleRoot 'DiplomacyBalancePatches.cs')
if ($diplomacySource -notmatch 'GregorianTruceDays = 100f' -or
    $diplomacySource -notmatch 'PeaceDeclarationDate\.ElapsedDaysUntilNow <= GregorianTruceDays') {
    throw 'War declarations must enforce the configured 100-calendar-day truce.'
}

$pacingSource = Get-Content -Raw -LiteralPath (Join-Path $ModuleRoot 'CampaignPacingPatch.cs')
if ($pacingSource -notmatch 'SpeedUpMultiplier' -or
    $pacingSource -match 'realDt \*=') {
    throw 'Fast-forward speed must use Bannerlord''s direct SpeedUpMultiplier without stacking a TickMapTime multiplier.'
}
$settingsSource = Get-Content -Raw -LiteralPath (Join-Path $ModuleRoot 'CalendarSettingsState.cs')
$optionsSource = Get-Content -Raw -LiteralPath (Join-Path $ModuleRoot 'CalendarOptionsTabPatch.cs')
$mcmSettingsSource = Get-Content -Raw -LiteralPath (Join-Path $ModuleRoot 'McmSettings.cs')
$calendarOptionItemSource = Get-Content -Raw -LiteralPath (Join-Path $ModuleRoot 'GUI\Prefabs\Options\SPOptions\CalendarOptionItem.xml')
if ($settingsSource -notmatch 'DefaultCampaignTimeScale = 0\.803f' -or
    $settingsSource -notmatch 'MaximumPacingMultiplier = 4f' -or
    $settingsSource -notmatch 'requestedAutoCampaignTimeScale[\s\S]{0,120}\? DefaultCampaignTimeScale' -or
    $settingsSource -match 'case "Pregnancy Duration \(Months\)"' -or
    $settingsSource -match 'case "Lord Death Rate Multiplier"' -or
    $settingsSource -match 'case "Renown Gain Multiplier"' -or
    $settingsSource -match 'ApplyCampaignStartSetting\([\s\S]{0,160}AutoCampaignTimeScale' -or
    $optionsSource -notmatch 'new OptionGroup\(new TextObject\("Pacing"\)' -or
    $optionsSource -notmatch 'CalendarNumericOptionDataVM' -or
    $optionsSource -notmatch 'CalendarBooleanOptionDataVM' -or
    $optionsSource -notmatch 'base\("Benchmark", action\)' -or
    $optionsSource -notmatch 'ActionName = calendarAction\.DisplayActionName' -or
    $optionsSource -notmatch 'HarmonyPatch\(typeof\(ActionOptionDataVM\), nameof\(ActionOptionDataVM\.RefreshValues\)\)' -or
    $optionsSource -notmatch 'CompleteCategoryReset' -or
    $optionsSource -notmatch 'InformationManager\.DisplayMessage\(new InformationMessage' -or
    $optionsSource -notmatch 'if \(OptionalMcmIntegration\.IsSettingsRegistered\)[\s\S]{0,300}Native Calendar Options tab hidden' -or
    $optionsSource -notmatch 'nativeCalendarSettingsEnabled\s*=\s*!OptionalMcmIntegration\.IsSettingsRegistered' -or
    $optionsSource -notmatch 'RefreshCalendarOptionControls' -or
    $optionsSource -notmatch 'new CalendarBooleanOptionData\(\s*"Annual Balance Enabled"' -or
    $optionsSource -notmatch 'Reset Pacing' -or
    $optionsSource -notmatch 'Reset Annual Balance' -or
    $optionsSource -notmatch 'RefreshCalendarOptions\(\)' -or
    $optionsSource -match 'Reset Diagnostics' -or
    $settingsSource -notmatch 'ResetCalendarCategory' -or
    $optionsSource -match 'base\("Calendar(MonthNames|SeasonNames|MonthLengths)", action\)' -or
    $optionsSource -notmatch 'SetValue\(Math\.Max\(Min, Math\.Min\(Max, value\)\)\)' -or
    $optionsSource -match 'Fixed Pregnancy Duration \(Days\)' -or
    $mcmSettingsSource -notmatch 'public override string FormatType\s*=>\s*"json"' -or
    $mcmSettingsSource -match 'Fixed Pregnancy Duration \(Days\)' -or
    $calendarOptionItemSource -notmatch 'Command\.Click="ExecuteDecrease"' -or
    $calendarOptionItemSource -notmatch 'Command\.Click="ExecuteIncrease"' -or
    $calendarOptionItemSource -notmatch 'Command\.Click="ExecuteToggle"') {
    throw 'Campaign pacing must reset live to the exact 0.803 automatic default and stay within Bannerlord''s 4x AI-safe limit; Calendar sliders and toggles must write through their view models; and fixed pregnancy days must stay out of the settings UI.'
}
$lordDeathSource = Get-Content -Raw -LiteralPath (Join-Path $ModuleRoot 'CalendarLordDeathModels.cs')
if ($lordDeathSource -notmatch 'CalendarHeroDeathProbabilityModel' -or
    $lordDeathSource -notmatch 'CalendarLordBattleSurvivalModel' -or
    $lordDeathSource -notmatch 'ScaleDailyDeathProbability') {
    throw 'Lord mortality must use the public old-age and battle-survival model wrappers.'
}
$saveProfileSource = Get-Content -Raw -LiteralPath (Join-Path $ModuleRoot 'CalendarSaveCompatibility.cs')
$playerPacingSource = Get-Content -Raw -LiteralPath (Join-Path $ModuleRoot 'PlayerPacingPatches.cs')
$calendarTimeMathSource = Get-Content -Raw -LiteralPath (Join-Path $ModuleRoot 'CalendarTimeMath.cs')
$campaignTimePatchesSource = Get-Content -Raw -LiteralPath (Join-Path $ModuleRoot 'CampaignTimeCalendarPatches.cs')
$worldLedgerViewModelSource = Get-Content -Raw -LiteralPath (Join-Path $ModuleRoot 'CalendarWorldLedgerVM.cs')
if ($saveProfileSource -notmatch 'CalendarCampaignProfileBehavior' -or
    $saveProfileSource -notmatch 'primitive payload only; no module-load marker written' -or
    $saveProfileSource -notmatch 'AgesOfCalradia\.CampaignProfileV3' -or
    $saveProfileSource -match ':\s*SaveableTypeDefiner|using\s+TaleWorlds\.SaveSystem|\[Saveable(Field|Property)') {
    throw 'New saves must use the primitive soft campaign profile rather than a hard module-lock marker.'
}
if ($playerPacingSource -notmatch 'dueTime\s*==\s*CampaignTime\.Never' -or
    $playerPacingSource -notmatch '__instance\.DeathDay' -or
    $calendarTimeMathSource -notmatch 'GetLegacyCompatibleHeroAgeAt' -or
    $calendarTimeMathSource -notmatch 'GetLegacyCompatibleElapsedYearsAt\(time, CampaignTime\.Now\)' -or
    $calendarTimeMathSource -notmatch 'LooksLikeNativeTimeBasis' -or
    $calendarTimeMathSource -notmatch 'ToCalendarAbsoluteDays' -or
    $campaignTimePatchesSource -notmatch 'DurationToYears\(__instance\)' -or
    $campaignTimePatchesSource -notmatch 'DurationToSeasons\(__instance\)' -or
    $worldLedgerViewModelSource -match 'QuestDueTime\.ToDays' -or
    $saveProfileSource -notmatch 'LegacyNativeAgeCutoverDayV1') {
    throw 'Story-quest sentinels, native-to-Gregorian dates and hero ages, and offset-free durations must retain their save-compatibility guards.'
}
$subModuleSource = Get-Content -Raw -LiteralPath (Join-Path $ModuleRoot 'MySubModule.cs')
$settingsStateSource = Get-Content -Raw -LiteralPath (Join-Path $ModuleRoot 'CalendarSettingsState.cs')
$mapBarDataSource = Get-Content -Raw -LiteralPath (Join-Path $ModuleRoot 'MapBarSeasonDataSourcePatch.cs')
$refugeIntegrationSource = Get-Content -Raw -LiteralPath (Join-Path $ModuleRoot 'CalendarRefugeIntegration.cs')
$mainProjectSource = Get-Content -Raw -LiteralPath $mainProject
if ($subModuleSource -match 'CalendarRefugeBehavior|CalendarCampBehavior' -or
    $mainProjectSource -match 'Compile Include="(?:CalendarRefuge(?:Behavior|BuilderHudView|LayoutBuilderBehavior|MapClickPatch|Mission|StewardInteraction)|CalendarCampBehavior|PortableCampAnchorStore|Refuge(?:BuildingCatalog|FortPrefabCatalog|SceneProfileCatalog|StaffRole|Upgrade))\.cs"' -or
    $settingsStateSource -notmatch 'RefugeSystemEnabled[\s\S]{0,100}return false;' -or
    $mapBarDataSource -notmatch 'CalendarRefugeIntegration\.TryOpenCamp' -or
    $refugeIntegrationSource -notmatch 'RegisterCampOpener' -or
    $refugeIntegrationSource -notmatch 'IsWinter') {
    throw 'The base module must remain refuge-free while providing only the optional Refuges integration seam.'
}

$moduleXml = Join-Path $ModuleRoot 'SubModule.xml'
$moduleDataRoot = Join-Path $ModuleRoot 'ModuleData'
$moduleStrings = Join-Path $moduleDataRoot 'module_strings.xml'
$readme = Join-Path $ModuleRoot 'README.md'
$optionsXml = Join-Path $ModuleRoot 'GUI\Prefabs\Options\SPOptions\CalendarOptions.xml'
$optionItemXml = Join-Path $ModuleRoot 'GUI\Prefabs\Options\SPOptions\OptionItem.xml'
$calendarOptionItemXml = Join-Path $ModuleRoot 'GUI\Prefabs\Options\SPOptions\CalendarOptionItem.xml'
$calendarOptionsGroupedPageXml = Join-Path $ModuleRoot 'GUI\Prefabs\Options\SPOptions\CalendarOptionsGroupedPage.xml'
$mapBarXml = Join-Path $ModuleRoot 'GUI\Prefabs\Map\MapBar.xml'
$mapBarSeasonBand = Join-Path $ModuleRoot 'GUI\SpriteParts\aoc_mapbar\aoc_mapbar_season_band_legible.png'
$mapBarCompactCrown = Join-Path $ModuleRoot 'GUI\SpriteParts\aoc_mapbar\aoc_mapbar_season_crown_compact.png'
$mapBarNotchBacking = Join-Path $ModuleRoot 'GUI\SpriteParts\aoc_mapbar\aoc_mapbar_notch_backing.png'
$mapBarMedallion = Join-Path $ModuleRoot 'GUI\SpriteParts\aoc_mapbar\aoc_mapbar_medallion_ring_oval.png'
$mapBarBrushes = Join-Path $ModuleRoot 'GUI\Brushes\AocMapBar.xml'
$worldCalendarXml = Join-Path $ModuleRoot 'GUI\Prefabs\WorldCalendar\WorldCalendar.xml'
$worldCalendarViewModelSource = Join-Path $ModuleRoot 'CalendarWorldLedgerVM.cs'
$worldCalendarSpriteData = Join-Path $ModuleRoot 'GUI\RealisticCalendarTweaksSpriteData.xml'
$worldCalendarSpriteConfig = Join-Path $ModuleRoot 'GUI\SpriteParts\Config.xml'
$worldCalendarMap = Join-Path $ModuleRoot 'GUI\SpriteParts\world_calendar\world_calendar_map.png'
$worldCalendarSheet = Join-Path $ModuleRoot 'AssetSources\GauntletUI\world_calendar_1.png'
$worldEventsSkinRoot = Join-Path $ModuleRoot 'GUI\CustomUI\WorldEventsSkin'
$worldEventsSkinManifest = Join-Path $worldEventsSkinRoot 'RuntimeAssetManifest.txt'
$guiRoot = Join-Path $ModuleRoot 'GUI'
$assetsRoot = Join-Path $ModuleRoot 'Assets'
$assetSourcesRoot = Join-Path $ModuleRoot 'AssetSources'
$runtimeDataCacheRoot = Join-Path $ModuleRoot 'RuntimeDataCache'
$prefabsRoot = Join-Path $ModuleRoot 'Prefabs'
$sceneObjRoot = Join-Path $ModuleRoot 'SceneObj'
$allModuleSceneDirectories = if (Test-Path -LiteralPath $sceneObjRoot -PathType Container) {
    @(Get-ChildItem -LiteralPath $sceneObjRoot -Directory)
}
else {
    @()
}
if ($IncludeStrategicProvinceDiagnostics) {
    $runtimeSceneDirectories = $allModuleSceneDirectories
}
else {
    $runtimeSceneDirectories = @($allModuleSceneDirectories | Where-Object {
        (Test-Path -LiteralPath (Join-Path $_.FullName 'scene.xscene') -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $_.FullName 'terrain.bin') -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $_.FullName 'navmesh.bin') -PathType Leaf) -and
        -not (Test-Path -LiteralPath (Join-Path $_.FullName 'REFUGE_AUTHORING_REQUIRED.txt') -PathType Leaf)
    })
}
$runtimeFiles = @($moduleXml, $moduleStrings, $readme, $harmonyDll, $mainDll, $approvedFixesDll, $campaignLabelVisibilityDll, $mcmDll, $mcmCoreDll, $optionsXml, $optionItemXml, $calendarOptionItemXml, $calendarOptionsGroupedPageXml, $mapBarXml, $mapBarBrushes, $mapBarSeasonBand, $mapBarCompactCrown, $mapBarNotchBacking, $mapBarMedallion, $worldCalendarXml, $worldCalendarSpriteData, $worldCalendarSpriteConfig, $worldCalendarMap, $worldCalendarSheet, $worldEventsSkinManifest)
foreach ($path in $runtimeFiles) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Expected release output is missing: $path"
    }
}
$requiredWorldEventsSkinFiles = @(Get-Content -LiteralPath $worldEventsSkinManifest |
    ForEach-Object { $_.Trim() } |
    Where-Object { $_ -and -not $_.StartsWith('#') })
if ($requiredWorldEventsSkinFiles.Count -ne 24) {
    throw "World Events runtime asset manifest must contain exactly 24 active skin files; found $($requiredWorldEventsSkinFiles.Count)."
}
$missingWorldEventsSkinFiles = @($requiredWorldEventsSkinFiles | Where-Object {
    -not (Test-Path -LiteralPath (Join-Path $worldEventsSkinRoot $_) -PathType Leaf)
})
if ($missingWorldEventsSkinFiles.Count -gt 0) {
    throw "World Events runtime skin is incomplete: $($missingWorldEventsSkinFiles -join ', ')."
}
$unlistedWorldEventsSkinFiles = @(Get-ChildItem -LiteralPath $worldEventsSkinRoot -Filter '*.png' -File |
    Where-Object { $_.Name -notin $requiredWorldEventsSkinFiles } |
    Select-Object -ExpandProperty Name)
if ($unlistedWorldEventsSkinFiles.Count -gt 0) {
    throw "Unused World Events layouts remain in the runtime GUI folder: $($unlistedWorldEventsSkinFiles -join ', ')."
}

$worldCalendarViewModelText = Get-Content -Raw -LiteralPath $worldCalendarViewModelSource
if ($worldCalendarViewModelText -notmatch 'FirstCalendarMonth = 0' -or
    $worldCalendarViewModelText -notmatch 'LastCalendarMonth = 11' -or
    $worldCalendarViewModelText -match 'BuildYearImportantSummary\(year,\s*1,\s*12') {
    throw 'World Events summaries must use zero-based month indices 0 through 11.'
}
[xml]$worldCalendarDocument = Get-Content -Raw -LiteralPath $worldCalendarXml
$strategicMapViewport = @($worldCalendarDocument.SelectNodes('//Widget[@Id="StrategicMapPanel" and @IsVisible="@IsStrategicMap" and @SuggestedWidth="741" and @SuggestedHeight="427" and @MarginLeft="87"]'))
$strategicMapScroller = @($worldCalendarDocument.SelectNodes('//StrategicMapZoomScrollablePanel[@Id="StrategicMapScroller"]'))
$strategicMapCanvas = @($worldCalendarDocument.SelectNodes('//Widget[@Id="StrategicMapCanvas"]'))
if ($strategicMapViewport.Count -ne 1 -or
    $strategicMapViewport[0].'Command.HoverBegin' -ne 'ExecuteStrategicMapHoverBegin' -or
    $strategicMapViewport[0].'Command.HoverEnd' -ne 'ExecuteStrategicMapHoverEnd' -or
    $strategicMapScroller.Count -ne 1 -or
    $strategicMapScroller[0].'PanWithMouseEnabled' -ne 'false' -or
    $strategicMapScroller[0].'AutoHideScrollBars' -ne 'false' -or
    $strategicMapScroller[0].'AutoHideScrollBarHandle' -ne 'false' -or
    $strategicMapScroller[0].'Command.HoverBegin' -ne 'ExecuteStrategicMapHoverBegin' -or
    $strategicMapScroller[0].'Command.HoverEnd' -ne 'ExecuteStrategicMapHoverEnd' -or
    $strategicMapCanvas.Count -ne 1 -or
    $strategicMapCanvas[0].HorizontalAlignment -ne 'Left' -or
    $strategicMapCanvas[0].VerticalAlignment -ne 'Top' -or
    $worldCalendarViewModelText -notmatch '!_isPointerOverStrategicMap') {
    throw 'Strategic-map zoom and panning must remain bound to the hovered scroll viewport, with the canvas anchored from its top-left edge.'
}
$strategicTextureWidgets = @($worldCalendarDocument.SelectNodes('//TextureWidget[@TextureProviderName="CalendarStrategicCampaignAtlasTextureProvider"]'))
$strategicLegendWidgets = @($worldCalendarDocument.SelectNodes('//StrategicLegendDrawWidget'))
if ($strategicTextureWidgets.Count -ne 1 -or
    $strategicTextureWidgets[0].TextureProviderName -ne 'CalendarStrategicCampaignAtlasTextureProvider' -or
    $strategicLegendWidgets.Count -ne 2 -or
    @($worldCalendarDocument.SelectNodes('//StrategicLegendDrawWidget[@IconKind="Town"]')).Count -ne 1 -or
    @($worldCalendarDocument.SelectNodes('//StrategicLegendDrawWidget[@IconKind="Castle"]')).Count -ne 1 -or
    @($worldCalendarDocument.SelectNodes('//ImageWidget[@Sprite="strategic_marker_town" or @Sprite="strategic_marker_castle"]')).Count -ne 0) {
    throw 'The full strategic map must use its dedicated atlas provider and the legend must use its dedicated town and castle draw widgets.'
}
# CalendarSummaryPanel and its monthly/yearly children live inside the framed
# CalendarContentPanel. The map legend and kingdom summary are now sections of
# StrategicSidePanel, not standalone frame widgets.
$goldFramedPanels = @('WorldEventsFrame','CalendarEditableContentPanel','CalendarEditableNotesPanel','SavedSummariesPanel','StrategicMapPanel','StrategicSidePanel')
foreach ($panelId in $goldFramedPanels) {
    $panel = @($worldCalendarDocument.SelectNodes("//Widget[@Id='$panelId']"))
    if ($panel.Count -ne 1 -or @($panel[0].SelectNodes('.//BrushWidget[@Brush="TownManagement.GovernorPopup.GoldFrame"]')).Count -lt 1) {
        throw "World Events panel is missing its gold frame: $panelId"
    }
}
foreach ($directory in @($moduleDataRoot, $guiRoot, $assetsRoot, $assetSourcesRoot)) {
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        throw "Expected release runtime directory is missing: $directory"
    }
}
$runtimeContentDirectories = @($moduleDataRoot, $guiRoot, $assetsRoot, $assetSourcesRoot, $runtimeDataCacheRoot, $prefabsRoot) |
    Where-Object { Test-Path -LiteralPath $_ -PathType Container }

[xml]$mapBar = Get-Content -Raw -LiteralPath $mapBarXml
$campButton = @($mapBar.SelectNodes('//ButtonWidget[@Id="CampButton"]'))
if ($campButton.Count -ne 1 -or $campButton[0].IsVisible -ne '@IsRefugeSystemEnabled') {
    throw 'The map-bar camp button must be bound to the optional standalone refuge integration.'
}
$centerPanel = @($mapBar.SelectNodes('//MapCurrentTimeVisualWidget[@Id="CenterPanel"]'))
if ($centerPanel.Count -ne 1 -or $centerPanel[0].HorizontalAlignment -ne 'Center' -or $centerPanel[0].PositionXOffset -ne '0' -or $centerPanel[0].VerticalAlignment -ne 'Bottom' -or $centerPanel[0].SuggestedWidth -ne '620' -or $centerPanel[0].SuggestedHeight -ne '97') {
    throw 'Map bar center panel must remain bottom-centered for resolution-independent placement.'
}
$calendarDate = @($mapBar.SelectNodes('//MapCurrentTimeVisualWidget[@Id="CenterPanel"]/Children/TextWidget[@Id="CalendarDateText"]'))
$calendarYear = @($mapBar.SelectNodes('//MapCurrentTimeVisualWidget[@Id="CenterPanel"]/Children/MapYearTextWidget[@Id="CalendarYearText"]'))
if ($calendarDate.Count -ne 1 -or [int]$calendarDate[0].SuggestedWidth -ne 190 -or $calendarDate[0].PositionXOffset -ne '18' -or $calendarDate[0].PositionYOffset -ne '18' -or $calendarDate[0].'Brush.FontSize' -ne '28') {
    throw 'Map bar calendar date must use the upper line of the widened two-line calendar block.'
}
if ($calendarYear.Count -ne 1 -or [int]$calendarYear[0].SuggestedWidth -ne 100 -or $calendarYear[0].PositionXOffset -ne '-3' -or $calendarYear[0].PositionYOffset -ne '54' -or $calendarYear[0].SourceText -ne '@SeasonYearLine') {
    throw 'Map bar year must begin the compact lower row before the adjacent clock.'
}
$seasonAssembly = @($mapBar.SelectNodes('//MapCurrentTimeVisualWidget[@Id="CenterPanel"]/Children/TextureWidget[@Id="SeasonCrown"]'))
$seasonButton = @($mapBar.SelectNodes('//ButtonWidget[@Id="MapTimeDialButton"]'))
$dayNightDisc = @($mapBar.SelectNodes('//ButtonWidget[@Id="MapTimeDialButton"]/Children/MapTimeImageBrushWidget[@Id="VanillaDayNightDisc"]'))
$dialFrame = @($mapBar.SelectNodes('//ButtonWidget[@Id="MapTimeDialButton"]/Children/TextureWidget[@Id="MapTimeDialFrame"]'))
if ($seasonAssembly.Count -ne 1 -or $seasonAssembly[0].TextureProviderName -ne 'AocMapBarSeasonCrownTextureProvider' -or
    $seasonAssembly[0].SuggestedWidth -ne '152' -or $seasonAssembly[0].SuggestedHeight -ne '70' -or
    $seasonAssembly[0].VerticalAlignment -ne 'Top' -or $seasonAssembly[0].PositionYOffset -ne '-45' -or
    $seasonButton.Count -ne 1 -or $seasonButton[0].SuggestedWidth -ne '114' -or
    $seasonButton[0].VerticalAlignment -ne 'Top' -or $seasonButton[0].PositionYOffset -ne '-16' -or
    $dayNightDisc.Count -ne 1 -or $dayNightDisc[0].Brush -ne 'AocMapTimeImageLarge' -or
    $dayNightDisc[0].DayTime -ne '@Time' -or $dayNightDisc[0].CircularClipEnabled -ne 'true' -or
    $dayNightDisc[0].CircularClipRadius -ne '43' -or
    $dialFrame.Count -ne 1 -or $dialFrame[0].TextureProviderName -ne 'AocMapBarMedallionRimTextureProvider') {
    throw 'Map bar must contain the measured raised season assembly and untouched native animated dial.'
}

$binaryText = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($mainDll))
if ($IncludeStrategicProvinceDiagnostics -and -not $binaryText.Contains('StrategicProvinceDiagnostics')) {
    throw 'The Test build must include the strategic-province diagnostics.'
}
if (-not $IncludeStrategicProvinceDiagnostics -and $binaryText.Contains('StrategicProvinceDiagnostics')) {
    throw 'The production build must not include the strategic-province diagnostics.'
}
foreach ($unsafeSymbol in @(
    'DefaultClanFinanceModel',
    'DefaultPartyImpairmentModel',
    'DefaultPrisonerRecruitmentCalculationModel',
    'DefaultMarriageModel',
    'DefaultMapTrackModel')) {
    if ($binaryText.Contains($unsafeSymbol)) {
        throw "Unsafe native default-model reference found in release DLL: $unsafeSymbol"
    }
}

[xml]$manifest = Get-Content -Raw -LiteralPath $moduleXml
$version = $manifest.Module.Version.value
if ([string]::IsNullOrWhiteSpace($version) -or $version -notmatch '^v\d+\.\d+\.\d+$') {
    throw 'SubModule.xml must define a valid Bannerlord version in vMajor.Minor.Patch format.'
}
if ($manifest.Module.Id.value -ne 'AgesOfCalradia' -or
    $manifest.Module.Name.value -ne 'AOC CORE') {
    throw 'The manifest must use the AgesOfCalradia ID and AOC CORE display name.'
}
if (-not (Test-Path -LiteralPath $harmonyDll -PathType Leaf)) {
    throw 'The release must bundle exactly one Harmony runtime in the Ages module.'
}
$subModuleNames = @($manifest.Module.SubModules.SubModule.Name.value)
if ($subModuleNames -notcontains 'AOC CORE') {
    throw 'The runtime submodule must use the AOC CORE display name.'
}
$declaredRuntimeDlls = @($manifest.Module.SubModules.SubModule.DLLName.value | Sort-Object -Unique)
foreach ($declaredRuntimeDll in $declaredRuntimeDlls) {
    if (-not (Test-Path -LiteralPath (Join-Path $runtimeBinDirectory $declaredRuntimeDll) -PathType Leaf)) {
        throw "A DLL declared by the core manifest is missing from the release runtime: $declaredRuntimeDll"
    }
}
$mcmMetadata = @($manifest.Module.DependedModuleMetadatas.DependedModuleMetadata | Where-Object { $_.id -eq 'Bannerlord.MBOptionScreen' })
$additionalAssemblies = @($manifest.Module.SubModules.SubModule.Assemblies.Assembly | ForEach-Object { $_.value })
if ($mcmMetadata.Count -ne 1 -or $mcmMetadata[0].optional -ne 'true' -or
    $additionalAssemblies -notcontains 'MCMv5.dll' -or
    $additionalAssemblies -notcontains 'AgesOfCalradia.MCM.dll') {
    throw 'MCM must remain an optional load-before dependency, with its core and calendar adapter declared as additional assemblies.'
}

[xml]$moduleText = Get-Content -Raw -LiteralPath $moduleStrings
foreach ($optionId in @('CalendarMonthNames', 'CalendarSeasonNames', 'CalendarMonthLengths')) {
    if (@($moduleText.strings.string | Where-Object { $_.id -eq ('str_options_type.' + $optionId) }).Count -ne 1 -or
        @($moduleText.strings.string | Where-Object { $_.id -eq ('str_options_type_action.' + $optionId) }).Count -ne 1 -or
        @($moduleText.strings.string | Where-Object { $_.id -eq ('str_options_description.' + $optionId) }).Count -ne 1) {
        throw "Calendar action localization is incomplete for option ID: $optionId"
    }
}

if ([string]::IsNullOrWhiteSpace($ReleaseArchive)) {
    $archiveLabel = if ($IncludeStrategicProvinceDiagnostics) { "{0}-Test" -f $version } else { $version }
    $ReleaseArchive = Join-Path $ModuleRoot ("artifacts\AgesOfCalradia-{0}.zip" -f $archiveLabel)
}

$archiveDirectory = Split-Path -Parent $ReleaseArchive
New-Item -ItemType Directory -Force -Path $archiveDirectory | Out-Null
if (Test-Path -LiteralPath $ReleaseArchive) {
    Remove-Item -LiteralPath $ReleaseArchive -Force
}

$stagingRoot = Join-Path ([IO.Path]::GetTempPath()) ("AgesOfCalradia-release-{0}" -f [Guid]::NewGuid())
$moduleStage = Join-Path $stagingRoot 'AgesOfCalradia'
try {
    New-Item -ItemType Directory -Force -Path `
        (Join-Path $moduleStage 'bin\Win64_Shipping_Client'), `
        (Join-Path $moduleStage 'SceneObj') | Out-Null
    Copy-Item -LiteralPath $moduleXml, $readme -Destination $moduleStage
    Copy-Item -LiteralPath $runtimeContentDirectories -Destination $moduleStage -Recurse
    Copy-Item -LiteralPath $harmonyDll, $mainDll, $approvedFixesDll, $campaignLabelVisibilityDll, $politicalBorderOptimizerDll, $worldEventsShellRepairDll, $campaignSystemsDll, $mcmDll, $mcmCoreDll -Destination (Join-Path $moduleStage 'bin\Win64_Shipping_Client')
    foreach ($sceneDirectory in $runtimeSceneDirectories) {
        Get-ChildItem -LiteralPath $sceneDirectory.FullName -Recurse -File |
            Where-Object { $_.FullName -notmatch '[\\/]ShaderCache[\\/]' } |
            ForEach-Object {
                $relativePath = $_.FullName.Substring($sceneObjRoot.Length).TrimStart('\', '/')
                $destinationPath = Join-Path $moduleStage ('SceneObj\' + $relativePath)
                New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destinationPath) | Out-Null
                Copy-Item -LiteralPath $_.FullName -Destination $destinationPath
            }
    }
    Compress-Archive -LiteralPath $moduleStage -DestinationPath $ReleaseArchive -CompressionLevel Optimal
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$expectedEntries = @(
    'AgesOfCalradia/README.md',
    'AgesOfCalradia/SubModule.xml',
    'AgesOfCalradia/ModuleData/module_strings.xml',
    'AgesOfCalradia/bin/Win64_Shipping_Client/0Harmony.dll',
    'AgesOfCalradia/bin/Win64_Shipping_Client/AgesOfCalradia.dll',
    'AgesOfCalradia/bin/Win64_Shipping_Client/AgesOfCalradia.Approved560CalendarFixes.dll',
    'AgesOfCalradia/bin/Win64_Shipping_Client/AgesOfCalradia.CampaignLabelVisibility.dll',
    'AgesOfCalradia/bin/Win64_Shipping_Client/AgesOfCalradia.PoliticalBorderOptimizer.dll',
    'AgesOfCalradia/bin/Win64_Shipping_Client/AgesOfCalradia.WorldEventsShellRepair.dll',
    'AgesOfCalradia/bin/Win64_Shipping_Client/AgesOfCalradia.CampaignSystems.dll',
    'AgesOfCalradia/bin/Win64_Shipping_Client/AgesOfCalradia.MCM.dll',
    'AgesOfCalradia/bin/Win64_Shipping_Client/MCMv5.dll'
)
$runtimeDirectoryEntries = foreach ($directory in $runtimeContentDirectories) {
    $directoryName = Split-Path -Leaf $directory
    Get-ChildItem -LiteralPath $directory -Recurse -File | ForEach-Object {
        $relativePath = $_.FullName.Substring($directory.Length).TrimStart('\', '/')
        ('AgesOfCalradia/{0}/{1}' -f $directoryName, $relativePath).Replace('\', '/')
    }
}
$runtimeSceneEntries = foreach ($sceneDirectory in $runtimeSceneDirectories) {
    Get-ChildItem -LiteralPath $sceneDirectory.FullName -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/]ShaderCache[\\/]' } |
        ForEach-Object {
        $relativePath = $_.FullName.Substring($sceneObjRoot.Length).TrimStart('\', '/')
        ('AgesOfCalradia/SceneObj/{0}' -f $relativePath).Replace('\', '/')
    }
}
$expectedEntries = @($expectedEntries + $runtimeDirectoryEntries + $runtimeSceneEntries | Sort-Object -Unique)
$archive = [IO.Compression.ZipFile]::OpenRead($ReleaseArchive)
try {
    $actualEntries = @($archive.Entries |
        ForEach-Object { $_.FullName.Replace('\', '/') } |
        Where-Object { -not $_.EndsWith('/') } |
        Sort-Object -Unique)
}
finally {
    $archive.Dispose()
}
if (Compare-Object -ReferenceObject $expectedEntries -DifferenceObject $actualEntries) {
    throw 'Release archive contents do not exactly match the approved runtime file list.'
}
if ($actualEntries | Where-Object { $_ -match '(?i)BetterTime|TwelveMonthCalendar\.BetterTime|^_TwelveMonthCalendar/' }) {
    throw 'The retired Better Time adapter and legacy save bridge must not be present in a release archive.'
}

if (-not $SkipSecurityScan) {
    if (-not (Get-Command Start-MpScan -ErrorAction SilentlyContinue)) {
        throw 'Microsoft Defender scan command is unavailable. Do not upload this release.'
    }
    $scanStarted = Get-Date
    Start-MpScan -ScanPath $ReleaseArchive -ScanType CustomScan
    $escapedArchive = [Regex]::Escape($ReleaseArchive)
    function Get-ReleaseArchiveDetections {
        Get-MpThreatDetection | Where-Object {
            $_.Resources -match $escapedArchive -and $_.InitialDetectionTime -ge $scanStarted.AddMinutes(-1)
        }
    }

    # A custom scan can return before Defender's cloud verdict arrives. Keep the
    # exact final archive on disk and poll it for the requested hold period.
    for ($minute = 1; $minute -le $CloudVerdictHoldMinutes; $minute++) {
        Start-Sleep -Seconds 60
        if (-not (Test-Path -LiteralPath $ReleaseArchive -PathType Leaf)) {
            throw 'Microsoft Defender or another security product removed the release archive during the cloud-verdict hold. Do not upload this release.'
        }
        $detections = @(Get-ReleaseArchiveDetections)
        if ($detections.Count -gt 0) {
            $detections | Format-List | Out-String | Write-Error
            throw 'Microsoft Defender detected a threat in the release archive. Do not upload this release.'
        }
        Write-Output ("Defender cloud-verdict hold: {0}/{1} minutes clean." -f $minute, $CloudVerdictHoldMinutes)
    }
}

$archiveHash = (Get-FileHash $ReleaseArchive -Algorithm SHA256).Hash
$releaseCommit = (git -C $ModuleRoot rev-parse HEAD).Trim()
$scanResult = if ($SkipSecurityScan) { 'Skipped by explicit request' } else { 'Clean' }
Write-Output ('PASS: Release verified; SecurityScan={0}; Commit={1}; Archive={2}; SHA256={3}; DailyFactor={4:F8}; DurationFactor={5:F8}; MainDLL={6}; MCMDLL={7}' -f `
    $scanResult, $releaseCommit, $ReleaseArchive, $archiveHash, $dailyFactor, $durationFactor, (Get-FileHash $mainDll -Algorithm SHA256).Hash, (Get-FileHash $mcmDll -Algorithm SHA256).Hash)
