param(
    [string]$ModuleRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$BannerlordDir = 'C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord',
    [string]$CalendarAssemblyPath,
    [switch]$ExpectRefugeSystemEnabled,
    [ValidateSet('Auto','Protected560','Development')][string]$Contract = 'Auto'
)

$ErrorActionPreference = 'Stop'

$gameBin = Join-Path $BannerlordDir 'bin\Win64_Shipping_Client'
foreach ($assemblyName in @(
    'TaleWorlds.Library.dll',
    'TaleWorlds.Core.dll',
    'TaleWorlds.Localization.dll',
    'TaleWorlds.ObjectSystem.dll',
    'TaleWorlds.SaveSystem.dll',
    'TaleWorlds.CampaignSystem.dll')) {
    [Reflection.Assembly]::LoadFrom((Join-Path $gameBin $assemblyName)) | Out-Null
}

if ([string]::IsNullOrWhiteSpace($CalendarAssemblyPath)) {
    $CalendarAssemblyPath = Join-Path $ModuleRoot 'bin\Win64_Shipping_Client\AgesOfCalradia.dll'
}
$approvedHash = '560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E'
$actualHash = (Get-FileHash -LiteralPath $CalendarAssemblyPath -Algorithm SHA256).Hash
if ($Contract -eq 'Auto') { $Contract = if ($actualHash -eq $approvedHash) { 'Protected560' } else { 'Development' } }
if ($Contract -eq 'Protected560' -and $actualHash -ne $approvedHash) { throw 'Protected560 contract requires the exact approved binary.' }
# Fixed, audited contracts, never infer expected schema/defaults from the object
# under test. Schema-6 source migrations are not present in the immutable core.
$expectedSchema = if ($Contract -eq 'Protected560') { 5 } else { 6 }
$expectedScale = if ($Contract -eq 'Protected560') { 0.15 } else { 0.803 }
$expectedFastForward = if ($Contract -eq 'Protected560') { 4.0 } else { 2.0 }
Write-Output "Calendar contract: $Contract (schema $expectedSchema); assembly=$CalendarAssemblyPath"
$calendarAssembly = [Reflection.Assembly]::LoadFrom($CalendarAssemblyPath)
$calendarMath = $calendarAssembly.GetType('TwelveMonthCalendar.CalendarTimeMath', $true)

function Invoke-CalendarMath([string]$Name, [object[]]$Arguments) {
    $method = $calendarMath.GetMethod(
        $Name,
        [Reflection.BindingFlags]'Static,NonPublic')
    if ($null -eq $method) {
        throw "CalendarTimeMath method was not found: $Name"
    }

    return $method.Invoke($null, $Arguments)
}

function Assert-Equal($Expected, $Actual, [string]$Name) {
    if ($Expected -ne $Actual) {
        throw "$Name failed. Expected '$Expected'; actual '$Actual'."
    }
}

function Assert-True($Actual, [string]$Name) {
    if (-not $Actual) {
        throw "$Name failed. Expected true; actual '$Actual'."
    }
}

function Assert-Near([double]$Expected, [double]$Actual, [double]$Tolerance, [string]$Name) {
    if ([Math]::Abs($Expected - $Actual) -gt $Tolerance) {
        throw "$Name failed. Expected '$Expected' +/- '$Tolerance'; actual '$Actual'."
    }
}

try {
    $calendarTypes = @($calendarAssembly.GetTypes())
}
catch [Reflection.ReflectionTypeLoadException] {
    $calendarTypes = @($_.Exception.Types | Where-Object { $null -ne $_ })
}

$customSaveDefiners = @($calendarTypes | Where-Object {
    $null -ne $_.BaseType -and
    $_.BaseType.FullName -eq 'TaleWorlds.SaveSystem.SaveableTypeDefiner'
})
Assert-Equal 0 $customSaveDefiners.Count 'Removable-save custom type definer count'

$moduleOwnedSaveFields = @($calendarTypes | ForEach-Object {
    $_.GetFields([Reflection.BindingFlags]'Public,NonPublic,Instance,Static') | Where-Object {
        @($_.GetCustomAttributesData() | Where-Object {
            $_.AttributeType.FullName -eq 'TaleWorlds.SaveSystem.SaveableFieldAttribute'
        }).Count -gt 0
    }
})
Assert-Equal 0 $moduleOwnedSaveFields.Count 'Removable-save custom saveable field count'
$featureSettingsType = $calendarAssembly.GetType('TwelveMonthCalendar.CalendarSettingsState', $true)
$refugeFeatureProperty = $featureSettingsType.GetProperty(
    'RefugeSystemEnabled',
    [Reflection.BindingFlags]'Static,NonPublic')
Assert-Equal ([bool]$ExpectRefugeSystemEnabled) ([bool]$refugeFeatureProperty.GetValue($null)) 'Build-specific refuge feature flag'

Assert-Equal $true (Invoke-CalendarMath 'IsLeapYear' @(1084)) 'Leap year 1084'
Assert-Equal $false (Invoke-CalendarMath 'IsLeapYear' @(1100)) 'Century year 1100'
Assert-Equal $true (Invoke-CalendarMath 'IsLeapYear' @(1200)) 'Four-hundred-year 1200'
Assert-Equal 366 (Invoke-CalendarMath 'GetYearLength' @(1084)) 'Leap-year length'
Assert-Equal 365 (Invoke-CalendarMath 'GetYearLength' @(1085)) 'Common-year length'
Assert-Equal 80 (Invoke-CalendarMath 'GetSeasonStartDayOfYear' @(1084, 0)) 'Leap-year spring boundary'
Assert-Equal 79 (Invoke-CalendarMath 'GetSeasonStartDayOfYear' @(1085, 0)) 'Common-year spring boundary'

$campaignAssembly = [AppDomain]::CurrentDomain.GetAssemblies() |
    Where-Object { $_.GetName().Name -eq 'TaleWorlds.CampaignSystem' } |
    Select-Object -First 1
$fastForwardModeMethod = $calendarMath.GetMethod(
    'IsFastForwardMode',
    [Reflection.BindingFlags]'Static,NonPublic')
$timeControlMode = $campaignAssembly.GetType('TaleWorlds.CampaignSystem.CampaignTimeControlMode', $true)
Assert-Equal $false ($fastForwardModeMethod.Invoke($null, @([Enum]::Parse($timeControlMode, 'StoppablePlay')))) 'Normal mode detection'
Assert-Equal $true ($fastForwardModeMethod.Invoke($null, @([Enum]::Parse($timeControlMode, 'StoppableFastForward')))) 'Fast-forward mode detection'

$campaignTimeType = $campaignAssembly.GetType('TaleWorlds.CampaignSystem.CampaignTime', $true)
$campaignTimeType.GetField(
    'TimeTicksPerDay',
    [Reflection.BindingFlags]'Static,NonPublic').SetValue($null, [long]1000000)
$campaignDaysFactory = $campaignTimeType.GetMethod(
    'Days',
    [Reflection.BindingFlags]'Static,Public',
    $null,
    [Type[]]@([single]),
    $null)
$ageAtMethod = $calendarMath.GetMethod(
    'GetLegacyCompatibleHeroAgeAt',
    [Reflection.BindingFlags]'Static,NonPublic')
$markLegacyAge = $featureSettingsType.GetMethod(
    'MarkLegacySaveAgeCompatibility',
    [Reflection.BindingFlags]'Static,NonPublic')
$markModernAge = $featureSettingsType.GetMethod(
    'MarkModernSaveAgeCompatibility',
    [Reflection.BindingFlags]'Static,NonPublic')
$beginCampaignSession = $featureSettingsType.GetMethod(
    'BeginCampaignSession',
    [Reflection.BindingFlags]'Static,NonPublic')
$looksLikeNativeBasis = $calendarMath.GetMethod(
    'LooksLikeNativeTimeBasis',
    [Reflection.BindingFlags]'Static,NonPublic')
$toCalendarAbsoluteDays = $calendarMath.GetMethod(
    'ToCalendarAbsoluteDays',
    [Reflection.BindingFlags]'Static,NonPublic',
    $null,
    [Type[]]@($campaignTimeType),
    $null)
$durationToYears = $calendarMath.GetMethod(
    'DurationToYears',
    [Reflection.BindingFlags]'Static,NonPublic')
$durationToSeasons = $calendarMath.GetMethod(
    'DurationToSeasons',
    [Reflection.BindingFlags]'Static,NonPublic')
$nativeCampaignStartDay = [double]$calendarMath.GetProperty(
    'NativeCampaignStartDay',
    [Reflection.BindingFlags]'Static,NonPublic').GetValue($null)
$gregorianCampaignStartDay = [double]$calendarMath.GetProperty(
    'GregorianCampaignStartDay',
    [Reflection.BindingFlags]'Static,NonPublic').GetValue($null)
$nativeCampaignStart = $campaignDaysFactory.Invoke($null, @([single]$nativeCampaignStartDay))
$gregorianCampaignStart = $campaignDaysFactory.Invoke($null, @([single]$gregorianCampaignStartDay))
Assert-True ($looksLikeNativeBasis.Invoke($null, @($nativeCampaignStart))) 'Native raw campaign-time basis detection'
Assert-Equal $false ($looksLikeNativeBasis.Invoke($null, @($gregorianCampaignStart))) 'Gregorian raw campaign-time basis detection'
$markLegacyAge.Invoke($null, @([double]100000.0)) | Out-Null
$oneCalendarYearDuration = $campaignDaysFactory.Invoke($null, @([single]365.2425))
$oneCalendarSeasonDuration = $campaignDaysFactory.Invoke($null, @([single](365.2425 / 4.0)))
Assert-Near 1.0 ([double]$durationToYears.Invoke($null, @($oneCalendarYearDuration))) 0.0001 'Duration years exclude absolute-date epoch offset'
Assert-Near 1.0 ([double]$durationToSeasons.Invoke($null, @($oneCalendarSeasonDuration))) 0.0001 'Duration seasons exclude absolute-date epoch offset'
$nativeThirtyYearBirth = $campaignDaysFactory.Invoke($null, @([single]97480.0))
$nativeFortyYearBirth = $campaignDaysFactory.Invoke($null, @([single]96640.0))
$cutoverTime = $campaignDaysFactory.Invoke($null, @([single]100000.0))
$oneGregorianYearLater = $campaignDaysFactory.Invoke($null, @([single]100365.2425))
$postCutoverBirth = $campaignDaysFactory.Invoke($null, @([single]100100.0))
$postCutoverReference = $campaignDaysFactory.Invoke($null, @([single]100465.2425))
Assert-Near 30.0 ([double]$ageAtMethod.Invoke($null, @($nativeThirtyYearBirth, $cutoverTime))) 0.0001 'Legacy age preserved at cutover'
Assert-Near 31.0 ([double]$ageAtMethod.Invoke($null, @($nativeThirtyYearBirth, $oneGregorianYearLater))) 0.0001 'Legacy hero future Gregorian aging'
Assert-Near 1.0 ([double]$ageAtMethod.Invoke($null, @($postCutoverBirth, $postCutoverReference))) 0.0001 'Post-cutover newborn Gregorian aging'
Assert-Near 40.0 ([double]$ageAtMethod.Invoke($null, @($nativeFortyYearBirth, $cutoverTime))) 0.0001 'Legacy dead-hero age at death'
Assert-Near $gregorianCampaignStartDay ([double]$toCalendarAbsoluteDays.Invoke($null, @($nativeCampaignStart))) 0.01 'Native campaign epoch maps to Gregorian April 1084'
Assert-Equal 1084 (Invoke-CalendarMath 'GetYear' @($nativeCampaignStart)) 'Mapped native-save calendar year'
Assert-Equal 3 (Invoke-CalendarMath 'GetMonth' @($nativeCampaignStart)) 'Mapped native-save calendar month'

# Installation/update/removal regression: installing on a native-basis save
# preserves the cutover age, repeated updates keep advancing on Gregorian time,
# and removal can still read the original unmodified native BirthDay value.
$nativeBirthDayBeforeInstall = [double]$nativeThirtyYearBirth.ToDays
$markLegacyAge.Invoke($null, @([double]100000.0)) | Out-Null
Assert-Near 30.0 ([double]$ageAtMethod.Invoke($null, @($nativeThirtyYearBirth, $cutoverTime))) 0.0001 'Hero age after installation on native save'
for ($updateCycle = 1; $updateCycle -le 3; $updateCycle++) {
    $markLegacyAge.Invoke($null, @([double]100000.0)) | Out-Null
    Assert-Near 31.0 ([double]$ageAtMethod.Invoke($null, @($nativeThirtyYearBirth, $oneGregorianYearLater))) 0.0001 "Hero age after update/reload cycle $updateCycle"
}
Assert-Near $nativeBirthDayBeforeInstall ([double]$nativeThirtyYearBirth.ToDays) 0.0001 'Installation/update leaves native BirthDay unmodified for removal'
$nativeAgeAfterRemoval = (100000.0 - [double]$nativeThirtyYearBirth.ToDays) / 84.0
Assert-Near 30.0 $nativeAgeAfterRemoval 0.0001 'Hero age after removing calendar patches at cutover'

$markModernAge.Invoke($null, @()) | Out-Null
Assert-Equal $false ([bool]$featureSettingsType.GetProperty('IsLegacySaveAgeCompatibility', [Reflection.BindingFlags]'Static,NonPublic').GetValue($null)) 'Modern save leaves native Hero.Age path unpatched'
Assert-Near 1.0 ([double]$durationToYears.Invoke($null, @($oneCalendarYearDuration))) 0.0001 'Modern-save Gregorian elapsed year across update'
$beginCampaignSession.Invoke($null, @()) | Out-Null
Assert-Equal $false ([bool]$featureSettingsType.GetProperty('IsLegacySaveAgeCompatibility', [Reflection.BindingFlags]'Static,NonPublic').GetValue($null)) 'Cross-campaign legacy age reset'

$profileType = $calendarAssembly.GetType('TwelveMonthCalendar.CalendarCampaignProfile', $true)
$captureProfile = $profileType.GetMethod('Capture', [Reflection.BindingFlags]'Static,Public')
$profile = $captureProfile.Invoke($null, @())
Assert-Equal $expectedSchema $profile.SchemaVersion 'Campaign profile schema'
Assert-Equal 1.0 $profile.NormalPlayTimeMultiplier 'Campaign profile normal pace'
Assert-Near $expectedScale $profile.CampaignTimeScale 0.000001 'Campaign profile contract-specific automatic scale'
Assert-Equal $expectedFastForward $profile.FastForwardTimeMultiplier 'Campaign profile fast-forward speed'
Assert-True (-not [string]::IsNullOrWhiteSpace($profile.Fingerprint)) 'Campaign profile fingerprint'
$profileValidationArguments = [object[]]@($null)
Assert-True ($profileType.GetMethod('TryValidate').Invoke($profile, $profileValidationArguments)) 'Campaign profile validation'

$settingsType = $calendarAssembly.GetType('TwelveMonthCalendar.CalendarSettingsState', $true)
$monthNameArguments = [object[]]@(
    'January|February|March|April|May|June|July|August|September|October|November|December',
    $null,
    $null)
Assert-True ($settingsType.GetMethod('TryParseMonthNamesDelimited').Invoke($null, $monthNameArguments)) 'Month-name editor parser'
Assert-Equal 12 @($monthNameArguments[1]).Length 'Month-name editor count'
$seasonNameArguments = [object[]]@('Spring|Summer|Autumn|Winter', $null, $null)
Assert-True ($settingsType.GetMethod('TryParseSeasonNamesDelimited').Invoke($null, $seasonNameArguments)) 'Season-name editor parser'
Assert-Equal 4 @($seasonNameArguments[1]).Length 'Season-name editor count'
$monthLengthArguments = [object[]]@('31|28|31|30|31|30|31|31|30|31|30|31', $null, $null)
Assert-True ($settingsType.GetMethod('TryParseMonthLengthsDelimited').Invoke($null, $monthLengthArguments)) 'Month-length editor parser'
Assert-Equal 365 ((@($monthLengthArguments[1]) | Measure-Object -Sum).Sum) 'Month-length editor total'

$legacyProfile = $captureProfile.Invoke($null, @())
$legacyProfile.SchemaVersion = 2
$legacyProfile.NormalPlayTimeMultiplier = 1.25
$legacyProfile.FastForwardTimeMultiplier = 2.5
Assert-True ($legacyProfile.TryUpgradeLegacyProfile()) 'Legacy profile upgrade'
Assert-Equal $expectedSchema $legacyProfile.SchemaVersion 'Legacy profile schema migration'
Assert-Equal 1.0 $legacyProfile.NormalPlayTimeMultiplier 'Legacy profile fixed normal pace migration'
Assert-Equal 4.0 $legacyProfile.FastForwardTimeMultiplier 'Legacy profile fast-forward speed migration clamps to AI-safe maximum'
Assert-Equal $false $legacyProfile.LegacyNativeAgeBasis 'Legacy profile defers native-basis detection to saved raw time'

$v15Profile = $captureProfile.Invoke($null, @())
$v15Profile.SchemaVersion = 3
$v15Profile.FastForwardTimeMultiplier = 128.0
Assert-True ($v15Profile.TryUpgradeLegacyProfile()) 'v1.5 profile upgrade'
Assert-Equal $expectedSchema $v15Profile.SchemaVersion 'v1.5 profile schema migration'
Assert-Equal 4.0 $v15Profile.FastForwardTimeMultiplier 'v1.5 profile fast-forward clamp'
Assert-True $v15Profile.AnnualBalanceEnabled 'v1.5 profile annual-balance master migration'

$automaticPacingProfile = $captureProfile.Invoke($null, @())
$automaticPacingProfile.SchemaVersion = 5
$automaticPacingProfile.AutoCampaignTimeScale = $true
$automaticPacingProfile.CampaignTimeScale = 0.15
$automaticPacingProfile.FastForwardTimeMultiplier = 4.0
Assert-True ($automaticPacingProfile.TryUpgradeLegacyProfile()) 'Automatic pacing profile upgrade'
Assert-Equal $expectedSchema $automaticPacingProfile.SchemaVersion 'Automatic pacing profile schema migration or current-schema preservation'
Assert-Near $expectedScale $automaticPacingProfile.CampaignTimeScale 0.000001 'Automatic pacing contract-specific scale migration/preservation'
Assert-Equal $expectedFastForward $automaticPacingProfile.FastForwardTimeMultiplier 'Automatic pacing contract-specific fast-forward migration/preservation'

$manualPacingProfile = $captureProfile.Invoke($null, @())
$manualPacingProfile.SchemaVersion = 5
$manualPacingProfile.AutoCampaignTimeScale = $false
$manualPacingProfile.CampaignTimeScale = 0.15
$manualPacingProfile.FastForwardTimeMultiplier = 4.0
Assert-True ($manualPacingProfile.TryUpgradeLegacyProfile()) 'Manual pacing profile upgrade'
Assert-Near 0.15 $manualPacingProfile.CampaignTimeScale 0.000001 'Manual pacing profile preserves selected scale'
Assert-Equal 4.0 $manualPacingProfile.FastForwardTimeMultiplier 'Manual pacing profile preserves selected fast-forward speed'

$profile.NormalPlayTimeMultiplier = 1.0
$profile.FastForwardTimeMultiplier = 2.0
$profile.RefreshFingerprint()
$settingsType.GetMethod('ApplyPersistedCampaignProfile', [Reflection.BindingFlags]'Static,NonPublic').Invoke($null, @($profile)) | Out-Null
Assert-Equal 1.0 ($settingsType.GetProperty('NormalPlayTimeMultiplier').GetValue($null)) 'Saved profile fixed normal pace restore'
Assert-Equal 2.0 ($settingsType.GetProperty('FastForwardTimeMultiplier').GetValue($null)) 'Saved profile fast-forward speed restore'

$serializedProfile = $profile.Serialize()
$deserializeArguments = [object[]]@($serializedProfile, $null, $null)
Assert-True ($profileType.GetMethod('TryDeserialize', [Reflection.BindingFlags]'Static,Public').Invoke($null, $deserializeArguments)) 'Soft profile serialization round trip'
$roundTripProfile = $deserializeArguments[1]
Assert-Equal $expectedSchema $roundTripProfile.SchemaVersion 'Soft profile round-trip schema'
Assert-Equal 2.0 $roundTripProfile.FastForwardTimeMultiplier 'Soft profile round-trip fast-forward speed'
Assert-Equal $profile.AnnualBalanceEnabled $roundTripProfile.AnnualBalanceEnabled 'Soft profile round-trip annual-balance master'

# Represent three separate campaign saves and cycle each through four
# serialize/load/apply operations. Fingerprints prove that no profile field is
# replaced by the previously loaded campaign's settings.
$profileFixtures = @()
foreach ($profileIndex in 0..2) {
    $fixture = $captureProfile.Invoke($null, @())
    $fixture.AutoCampaignTimeScale = $false
    $fixture.CampaignTimeScale = [single](0.12 + (0.05 * $profileIndex))
    $fixture.FastForwardTimeMultiplier = [single](2.0 + $profileIndex)
    $fixture.PregnancyDurationMonths = 8 + $profileIndex
    $fixture.RenownGainMultiplier = [single](0.3 + (0.2 * $profileIndex))
    $fixture.BalanceNpcMarriage = ($profileIndex % 2 -eq 0)
    $fixture.BalanceQuestDeadlines = ($profileIndex -ne 1)
    $fixture.AnnualBalanceEnabled = ($profileIndex -ne 2)
    $fixture.RefreshFingerprint()
    $profileFixtures += $fixture
}

foreach ($fixture in $profileFixtures) {
    $expectedFingerprint = $fixture.Fingerprint
    $cycledProfile = $fixture
    foreach ($reloadCycle in 1..4) {
        $payload = $cycledProfile.Serialize()
        $cycleArguments = [object[]]@($payload, $null, $null)
        Assert-True ($profileType.GetMethod('TryDeserialize', [Reflection.BindingFlags]'Static,Public').Invoke($null, $cycleArguments)) "Profile $expectedFingerprint reload cycle $reloadCycle"
        $cycledProfile = $cycleArguments[1]
        Assert-Equal $expectedFingerprint $cycledProfile.Fingerprint "Profile fingerprint cycle $reloadCycle"
        $settingsType.GetMethod('ApplyPersistedCampaignProfile', [Reflection.BindingFlags]'Static,NonPublic').Invoke($null, @($cycledProfile)) | Out-Null
        $recapturedProfile = $captureProfile.Invoke($null, @())
        Assert-Equal $expectedFingerprint $recapturedProfile.Fingerprint "Applied profile isolation cycle $reloadCycle"
    }
}

$lordDeathBalance = $calendarAssembly.GetType('TwelveMonthCalendar.CalendarLordDeathBalance', $true)
$scaleDailyDeath = $lordDeathBalance.GetMethod('ScaleDailyDeathProbability', [Reflection.BindingFlags]'Static,NonPublic')
$scaleBattleSurvival = $lordDeathBalance.GetMethod('ScaleBattleSurvivalChance', [Reflection.BindingFlags]'Static,NonPublic')
$scaledDailyDeath = [single]$scaleDailyDeath.Invoke($null, @([single]0.02))
$scaledBattleSurvival = [single]$scaleBattleSurvival.Invoke($null, @([single]0.80))
Assert-True ($scaledDailyDeath -gt 0 -and $scaledDailyDeath -lt 0.02) 'Lord old-age mortality reduction'
Assert-True ([Math]::Abs($scaledBattleSurvival - 0.96) -lt 0.0001) 'Lord battle survival reduction'

$auditType = $calendarAssembly.GetType('TwelveMonthCalendar.CalendarPatchSafetyAudit', $true)
$flags = [Reflection.BindingFlags]'Static,NonPublic'
$auditType.GetMethod('BeginStartupAudit', $flags).Invoke($null, @()) | Out-Null
Assert-True ($auditType.GetMethod('ValidateCampaignTimeCalendarTargets', $flags).Invoke($null, @())) 'CampaignTime target audit'
Assert-True ($auditType.GetMethod('ValidateCampaignTimeStringTarget', $flags).Invoke($null, @())) 'CampaignTime string target audit'
$trackerType = $campaignAssembly.GetType('TaleWorlds.CampaignSystem.MapTimeTracker', $true)
$trackerTick = $trackerType.GetMethod('Tick', [Reflection.BindingFlags]'Instance,Public,NonPublic', $null, @([single]), $null)
$campaignType = $campaignAssembly.GetType('TaleWorlds.CampaignSystem.Campaign', $true)
$campaignTick = $campaignType.GetMethod('TickMapTime', [Reflection.BindingFlags]'Instance,Public,NonPublic', $null, @([single]), $null)
Assert-True ($auditType.GetMethod('ValidateMapTimeTrackerTarget', $flags).Invoke($null, @($trackerTick))) 'Map time target audit'
Assert-True ($auditType.GetMethod('ValidateCampaignPacingTarget', $flags).Invoke($null, @($campaignTick))) 'Campaign pacing target audit'
$auditType.GetMethod('EnsureCoreTargetsValidated', $flags).Invoke($null, @()) | Out-Null

Write-Output 'PASS: Calendar math, installation/update/removal hero ages, four-cycle multi-profile reloads, pacing, and target-audit checks passed.'
