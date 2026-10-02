param(
    [string]$ModuleRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$BannerlordDir = 'C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord'
)

$ErrorActionPreference = 'Stop'
$assemblyPath = Join-Path $ModuleRoot 'bin\Win64_Shipping_Client\AgesOfCalradia.SoakDiagnostics.dll'
if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw "Soak diagnostics assembly is missing: $assemblyPath"
}

$gameBin = Join-Path $BannerlordDir 'bin\Win64_Shipping_Client'
foreach ($name in @(
    'TaleWorlds.Library.dll', 'TaleWorlds.DotNet.dll', 'TaleWorlds.ScreenSystem.dll', 'TaleWorlds.Core.dll', 'TaleWorlds.Engine.dll',
    'TaleWorlds.ObjectSystem.dll', 'TaleWorlds.SaveSystem.dll',
    'TaleWorlds.CampaignSystem.dll', 'TaleWorlds.MountAndBlade.dll')) {
    [Reflection.Assembly]::LoadFrom((Join-Path $gameBin $name)) | Out-Null
}

$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$type = $assembly.GetType(
    'AgesOfCalradia.SoakDiagnostics.AgesOfCalradiaSoakDiagnosticsSubModule',
    $true)
if (-not $type.IsSubclassOf([TaleWorlds.MountAndBlade.MBSubModuleBase])) {
    throw 'Soak diagnostics entry point is not a Bannerlord submodule.'
}

try { $types = @($assembly.GetTypes()) }
catch [Reflection.ReflectionTypeLoadException] { $types = @($_.Exception.Types | Where-Object { $_ }) }

$saveDefiners = @($types | Where-Object {
    $_.BaseType -and $_.BaseType.FullName -eq 'TaleWorlds.SaveSystem.SaveableTypeDefiner'
})
if ($saveDefiners.Count -ne 0) {
    throw 'Soak diagnostics must not add a saveable type definer.'
}

$source = Get-Content -Raw -LiteralPath (Join-Path $ModuleRoot 'CalendarSoakBehavior.cs')
foreach ($required in @(
    'CampaignEvents.HourlyTickEvent', 'CampaignEvents.DailyTickEvent',
    'CampaignEvents.TournamentStarted', 'CampaignEvents.WarDeclared',
    'CampaignEvents.MakePeace', 'CampaignEvents.SiegeCompletedEvent',
    'SaveHandler.SaveAs', 'Utilities.QuitGame', 'DefaultTargetCalendarDays = 731d', 'AocSoakTargetDays.txt')) {
    if (-not $source.Contains($required)) {
        throw "Soak contract missing: $required"
    }
}
if ($source -match 'HarmonyPatch|Saveable(TypeDefiner|Field|Property)') {
    throw 'Soak diagnostics must not patch production code or create save-owned state.'
}
if ($source.Contains('day >= _control.NextCheckpointDay') -or $source.Contains('TryConsumeWallClockCheckpointRequest')) {
    throw 'Soak must not create intermediate checkpoints or automatic relaunches.'
}

$controlType = $assembly.GetType('AgesOfCalradia.SoakDiagnostics.SoakControl', $true)
$control = [Activator]::CreateInstance($controlType, $true)
$flags = [Reflection.BindingFlags]'Instance,NonPublic'
$deadlineField = $controlType.GetField('DeadlineUtc', $flags)
$reached = $controlType.GetMethod('IsDeadlineReached', $flags)
$deadline = [DateTime]::Parse('2026-09-04T20:00:00Z').ToUniversalTime()
if ($reached.Invoke($control, @($deadline))) { throw 'Default calendar soak must have no wall-clock completion.' }
$deadlineField.SetValue($control, $deadline)
if ($reached.Invoke($control, @($deadline.AddSeconds(-1))) -or
    -not $reached.Invoke($control, @($deadline)) -or
    -not $reached.Invoke($control, @($deadline.AddHours(1)))) {
    throw 'Wall-clock soak deadline must finish exactly at or after its UTC deadline.'
}
foreach ($required in @('"DeadlineUtc="', 'entries.TryGetValue("DeadlineUtc"',
    'DateTimeStyles.RoundtripKind', 'LogTownEconomy();', 'town.Prosperity', 'town.FoodStocks', 'town.Gold')) {
    if (-not $source.Contains($required)) { throw "Timed soak persistence/economy contract missing: $required" }
}

$calibration = $assembly.GetType('AgesOfCalradia.SoakDiagnostics.PacingCalibration', $true)
$expectedDuration = $calibration.GetMethod('ExpectedSecondsPerDay', [Reflection.BindingFlags]'Static,NonPublic')
foreach ($speed in @(1,2,4)) {
    $actual = [double]$expectedDuration.Invoke($null, @($speed))
    if ([Math]::Abs($actual * $speed - 80) -gt 0.00001) {
        throw 'Calibration expected duration must represent 80/40/20 seconds per day.'
    }
}
$calibrationSource = Get-Content -Raw -LiteralPath (Join-Path $ModuleRoot 'PacingCalibration.cs')
foreach ($required in @('Stopwatch', 'CampaignTime.Now.ToDays', 'now - _startDay >= 1d',
    'CampaignTimeControlMode.UnstoppablePlay', 'PACING_COMPLETE', 'if (!Enabled || _completed',
    'CampaignTimeControlMode.Stop')) {
    if (-not $calibrationSource.Contains($required)) { throw "Calibration contract missing: $required" }
}
if ($calibrationSource -match 'SaveAs|QuitGame|SoakSpeedMultiplier') { throw 'Calibration must not save, exit or use soak acceleration.' }
Write-Output 'PASS: diagnostics-only controller; deadline behavior; economy observations; pacing measurement contract; no save contract. Transaction hooks have a separate verifier.'

$receipt=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.SoakReloadReceipt',$true)
$matches=$receipt.GetMethod('Matches',[Reflection.BindingFlags]'Static,NonPublic')
$good=@([double]123.25,[double]123.25,'campaign-a','campaign-a',[int]500,[int]500,[double]10,[double]10)
if(-not $matches.Invoke($null,$good)){throw 'Valid save/reload receipt rejected.'}
foreach($change in @(@(1,[double]124),@(3,'wrong-campaign'),@(5,[int]499),@(7,[double]11),@(1,[double]::NaN),@(1,[double]::PositiveInfinity))){
    $bad=$good.Clone(); $bad[[int]$change[0]]=$change[1]
    if($matches.Invoke($null,$bad)){throw 'Mismatched or nonfinite reload state accepted.'}
}
foreach($required in @('CampaignEvents.OnSaveOverEvent','CampaignEvents.OnSessionLaunchedEvent',
    'string.Equals(saveName, _requestedSaveName, StringComparison.Ordinal)',
    'if (!_saveSucceeded || Campaign.Current.SaveHandler.IsSaving) return;',
    'RELOAD_CONFIRMED','SoakReloadReceipt.MarkReady()', 'LogDailyEconomy();', 'applyWithdrawals: false', 'AI_PAYROLL_OBSERVATION')){
    if(-not $source.Contains($required)){throw "Save/reload or daily economy contract missing: $required"}
}
if($source.Contains('_sawSaveInProgress')){throw 'Unsafe save-completion polling returned.'}
$saveEvent=[TaleWorlds.CampaignSystem.CampaignEvents].GetProperty('OnSaveOverEvent')
if($saveEvent.PropertyType.GetGenericArguments()[0] -ne [bool] -or $saveEvent.PropertyType.GetGenericArguments()[1] -ne [string]){
    throw 'Native OnSaveOverEvent signature drifted.'
}
foreach($required in @('AocSoakRun.enabled','!manager.ActiveStateDisabledByUser','!map.AtMenu','!map.MapConversationActive',
    '!map.IsSimulationActive','Mission.Current == null','!InformationManager.IsAnyInquiryActive()')){
    if(-not $source.Contains($required)){throw "Native pause safety contract missing: $required"}
}
if($source -match 'StopGameOnFocusLost|UnregisterActiveStateDisableRequest|CloseEscapeMenu|\.TickMapTime\(|\.RealTick\('){
    throw 'Soak must preserve the user-owned Alt-Tab/Escape fix and must not inject simulation ticks.'
}
Write-Output 'PASS: native save callback; matching reload state; daily economy; no focus/Escape override.'
