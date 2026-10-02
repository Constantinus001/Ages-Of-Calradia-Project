param(
    [string]$BannerlordDir = 'C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord',
    [string]$SeedSave = (Join-Path $env:USERPROFILE 'Documents\Mount and Blade II Bannerlord\Game Saves\save014.sav'),
    [ValidateRange(1,72)][int]$MaximumHours = 6,
    [ValidateRange(0,72)][double]$WallClockHours = 0,
    [ValidateRange(1,30)][int]$SaveGraceMinutes = 5,
    [ValidateRange(0,120)][int]$CheckpointAfterMinutes = 0,
    [ValidateRange(1,731)][double]$TargetCalendarDays = 731
)

$ErrorActionPreference = 'Stop'
$workspaceRoot = Split-Path -Parent $PSScriptRoot
$sourceModule = Join-Path $workspaceRoot 'Modules\AgesOfCalradiaSoakDiagnostics'
$targetModule = Join-Path $BannerlordDir 'Modules\AgesOfCalradiaSoakDiagnostics'
$saveDirectory = Join-Path $env:USERPROFILE 'Documents\Mount and Blade II Bannerlord\Game Saves'
$soakDirectory = Join-Path $env:USERPROFILE 'Documents\Mount and Blade II Bannerlord\AgesOfCalradiaSoakDiagnostics'
$gameExecutable = Join-Path $BannerlordDir 'bin\Win64_Shipping_Client\Bannerlord.exe'
$seedTarget = Join-Path $saveDirectory 'AOC_SOAK_SEED.sav'
$checkpointSave = 'AOC_SOAK_LONGRUN'
$controlPath = Join-Path $soakDirectory 'AocSoakControl.txt'
$summaryPath = Join-Path $soakDirectory 'AocSoakRunner.log'
$deadlinePath = Join-Path $soakDirectory 'AocSoakDeadline.txt'

function Write-RunnerLog([string]$Message) {
    $line = (Get-Date).ToUniversalTime().ToString('o') + "`t" + $Message
    Add-Content -LiteralPath $summaryPath -Value $line
    Write-Output $line
}

function Read-Control {
    if (-not (Test-Path -LiteralPath $controlPath -PathType Leaf)) { return @{} }
    $values = @{}
    foreach ($line in Get-Content -LiteralPath $controlPath) {
        $parts = $line.Split('=', 2)
        if ($parts.Count -eq 2) { $values[$parts[0]] = $parts[1] }
    }
    return $values
}

function Backup-IfPresent([string]$Path, [string]$BackupRoot) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    New-Item -ItemType Directory -Force -Path $BackupRoot | Out-Null
    $target = Join-Path $BackupRoot ([IO.Path]::GetFileName($Path))
    Move-Item -LiteralPath $Path -Destination $target
}

if ((Get-Process Bannerlord*,TaleWorlds* -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'Bannerlord is already running. Close it before starting the isolated soak.'
}
foreach ($required in @($sourceModule, $SeedSave, $gameExecutable)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Required soak input is missing: $required" }
}
if($CheckpointAfterMinutes -gt 0 -and ($WallClockHours -eq 0 -or $CheckpointAfterMinutes -ge $WallClockHours*60)){
    throw 'CheckpointAfterMinutes must precede the timed run deadline.'
}
& (Join-Path $workspaceRoot 'Tests/Verify-BestBordersRestore.ps1')
& (Join-Path $sourceModule 'Tests/Verify-SoakDiagnostics.ps1')
& (Join-Path $sourceModule 'Tests/Verify-PeaceDiagnostics.ps1')

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupRoot = Join-Path $soakDirectory ("backup-" + $stamp)
New-Item -ItemType Directory -Force -Path $soakDirectory | Out-Null
New-Item -ItemType Directory -Path $backupRoot | Out-Null
$resolvedSeed=(Resolve-Path -LiteralPath $SeedSave).Path
foreach($ledgerFile in @(Get-ChildItem -LiteralPath $soakDirectory -Filter 'AocEconomy-*.tsv' -File)){
    Backup-IfPresent $ledgerFile.FullName $backupRoot
}
foreach ($path in @($controlPath, $deadlinePath, (Join-Path $soakDirectory 'AocSoakCheckpoint.request'), (Join-Path $soakDirectory 'AocSoakEvents.tsv'), $summaryPath, $seedTarget,
        (Join-Path $soakDirectory 'AocSoakTargetDays.txt'),
        (Join-Path $soakDirectory 'AocPacingCalibration.enabled'), (Join-Path $soakDirectory 'AocSoakRun.enabled'),
        (Join-Path $soakDirectory 'AocSoakReloadReceipt.txt'),
        (Join-Path $soakDirectory 'AocPeaceDiagnostics.tsv'),
        (Join-Path $saveDirectory ($checkpointSave + '.sav')), (Join-Path $saveDirectory 'AOC_SOAK_LONGRUN_FINAL.sav'))) {
    Backup-IfPresent $path $backupRoot
}
if([string]::Equals($resolvedSeed,$seedTarget,[StringComparison]::OrdinalIgnoreCase)){
    $SeedSave=Join-Path $backupRoot 'AOC_SOAK_SEED.sav'
}

New-Item -ItemType Directory -Force -Path $targetModule | Out-Null
$targetBin=Join-Path $targetModule 'bin/Win64_Shipping_Client'
New-Item -ItemType Directory -Force -Path $targetBin | Out-Null
$targetDll=Join-Path $targetBin 'AgesOfCalradia.SoakDiagnostics.dll'
if(Test-Path -LiteralPath $targetDll){Copy-Item -LiteralPath $targetDll -Destination (Join-Path $backupRoot 'previous-SoakDiagnostics.dll')}
Copy-Item -LiteralPath (Join-Path $sourceModule 'bin/Win64_Shipping_Client/AgesOfCalradia.SoakDiagnostics.dll') -Destination $targetDll -Force
Copy-Item -LiteralPath (Join-Path $sourceModule 'SubModule.xml') -Destination (Join-Path $targetModule 'SubModule.xml') -Force
Copy-Item -LiteralPath $SeedSave -Destination $seedTarget
Write-RunnerLog "SETUP complete; seed=$SeedSave; backup=$backupRoot"

$moduleList = '_MODULES_*Bannerlord.Harmony*Native*SandBoxCore*Sandbox*StoryMode*CustomBattle*BirthAndDeath*NavalDLC*AgesOfCalradia*AgesOfCalradiaSystemsRS*AgesOfCalradiaSoakDiagnostics*_MODULES_'
$deadline = (Get-Date).ToUniversalTime().AddHours($MaximumHours)
if ($WallClockHours -gt 0) {
    $deadline = (Get-Date).ToUniversalTime().AddHours($WallClockHours)
    [IO.File]::WriteAllText($deadlinePath, $deadline.ToString('O'))
    Write-RunnerLog "WALL_CLOCK deadline=$($deadline.ToString('O')); final save grace=$SaveGraceMinutes minutes"
}
[IO.File]::WriteAllText((Join-Path $soakDirectory 'AocSoakTargetDays.txt'), $TargetCalendarDays.ToString([Globalization.CultureInfo]::InvariantCulture))
Write-RunnerLog "CAMPAIGN_TARGET days=$($TargetCalendarDays.ToString([Globalization.CultureInfo]::InvariantCulture)); fresh control and deadline state"
$saveToLoad = 'AOC_SOAK_SEED'
$launches = 0
$checkpointAt=(Get-Date).ToUniversalTime().AddMinutes($CheckpointAfterMinutes)
$checkpointSent=$false
[IO.File]::WriteAllText((Join-Path $soakDirectory 'AocSoakRun.enabled'), 'Dedicated diagnostic run; '+$stamp)

try {
while ((Get-Date).ToUniversalTime() -lt $deadline) {
    $launches++
    Write-RunnerLog "LAUNCH $launches save=$saveToLoad"
    # Run the stock game executable. This soak must never depend on BLSE.
    $process = Start-Process -FilePath $gameExecutable -WorkingDirectory (Split-Path -Parent $gameExecutable) -ArgumentList @('/singleplayer', $moduleList, '/continuesave', $saveToLoad) -PassThru
    while (-not $process.WaitForExit(1000)) {
        if($CheckpointAfterMinutes -gt 0 -and -not $checkpointSent -and (Get-Date).ToUniversalTime() -ge $checkpointAt){
            [IO.File]::WriteAllText((Join-Path $soakDirectory 'AocSoakCheckpoint.request'), 'timed save/reload verification')
            $checkpointSent=$true
            Write-RunnerLog 'CHECKPOINT requested; completion and a matching reload are required.'
        }
        if ((Get-Date).ToUniversalTime() -gt $deadline.AddMinutes($SaveGraceMinutes)) {
            throw 'Soak deadline/save grace exceeded. Game left running to avoid interrupting a save; inspect diagnostics. No PASS recorded.'
        }
        if ($WallClockHours -eq 0 -and (Get-Date).ToUniversalTime() -gt $deadline) {
            throw 'Soak watchdog expired. Game left running to preserve unsaved state. No PASS recorded.'
        }
    }
    Start-Sleep -Seconds 3

    $control = Read-Control
    if ($control['Completed'] -eq 'True') {
        $failures = @(Select-String -LiteralPath (Join-Path $soakDirectory 'AocSoakEvents.tsv') -Pattern "`t[A-Z_]*FAILURE`t")
        if ($failures.Count -gt 0) { throw "Soak completed with $($failures.Count) diagnostic failures. Inspect events; no PASS recorded." }
        if($CheckpointAfterMinutes -gt 0 -and [int]$control['ReloadsCompleted'] -lt 1){throw 'No verified checkpoint reload; no PASS recorded.'}
        $events=Get-Content -LiteralPath (Join-Path $soakDirectory 'AocSoakEvents.tsv')
        foreach($kind in @('ECONOMY_SNAPSHOT','TOWN_ECONOMY','WORKSHOP','MARKET_SAMPLE','CLAN_EXPENSES')){
            if(@($events | Select-String -SimpleMatch "`t$kind`t").Count -lt 2){throw "Insufficient $kind coverage; no PASS recorded."}
        }
        Write-RunnerLog "PASS bounded diagnostic collection/save-reload after $launches launches; reloads=$($control['ReloadsCompleted']); economy balance requires review."
        exit 0
    }
    if (-not (Test-Path -LiteralPath (Join-Path $saveDirectory ($checkpointSave + '.sav')) -PathType Leaf)) {
        throw "Bannerlord exited before producing $checkpointSave.sav. Inspect $soakDirectory."
    }
    $saveToLoad = $checkpointSave
}

throw "Soak exceeded the $MaximumHours-hour watchdog. Inspect $soakDirectory."
} finally {
    $armedMarker=Join-Path $soakDirectory 'AocSoakRun.enabled'
    if(Test-Path -LiteralPath $armedMarker){Move-Item -LiteralPath $armedMarker -Destination (Join-Path $backupRoot 'finished-AocSoakRun.enabled')}
}
