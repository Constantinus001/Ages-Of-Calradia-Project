$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') {
    $windowsPowerShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    & $windowsPowerShell -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath
    exit $LASTEXITCODE
}
$module = Split-Path $PSScriptRoot
$root = Split-Path (Split-Path $module)
$core = Join-Path $root 'bin\Win64_Shipping_Client\AgesOfCalradia.dll'
$sidecar = Join-Path $module 'bin\Win64_Shipping_Client\AgesOfCalradia.WarScoreRecovery.dll'
$bannerlord = 'C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client'
$harmonyPath = Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.2\lib\net472\0Harmony.dll'
if (!(Test-Path -LiteralPath $sidecar)) { throw 'Release sidecar output is missing.' }

[Reflection.Assembly]::LoadFrom($harmonyPath) | Out-Null
@('TaleWorlds.CampaignSystem.dll', 'TaleWorlds.Core.dll', 'TaleWorlds.Library.dll', 'TaleWorlds.ObjectSystem.dll', 'TaleWorlds.MountAndBlade.dll') |
    ForEach-Object { [Reflection.Assembly]::LoadFrom((Join-Path $bannerlord $_)) | Out-Null }
$coreAssembly = [Reflection.Assembly]::LoadFrom($core)
$sidecarAssembly = [Reflection.Assembly]::LoadFrom($sidecar)
$ledger = $coreAssembly.GetType('TwelveMonthCalendar.CalendarWorldLedgerBehavior', $true)
$patchFlags = [Reflection.BindingFlags]'Static, NonPublic, Public'
$recovery = $sidecarAssembly.GetType('AgesOfCalradia.WarScoreRecovery.WarScoreRecoveryPatch', $true)
$peace = $sidecarAssembly.GetType('AgesOfCalradia.WarScoreRecovery.WarPeaceOutcomePatch', $true)
$harmony = [HarmonyLib.Harmony]::new('AgesOfCalradia.WarScoreRecovery.v1')

try {
    $recovery.GetMethod('Install', $patchFlags).Invoke($null, @($harmony)) | Out-Null
    $peace.GetMethod('Install', $patchFlags).Invoke($null, @($harmony)) | Out-Null
    foreach ($name in @('OnMapEventEndedForWarScore', 'OnSettlementOwnerChanged', 'OnPeaceMade', 'GetWarScore', 'TryConcludeWar')) {
        $target = @($ledger.GetMethods([Reflection.BindingFlags]'Instance, Static, Public, NonPublic') | Where-Object { $_.Name -eq $name })[0]
        $info = [HarmonyLib.Harmony]::GetPatchInfo($target)
        if ($info -eq $null -or @($info.Owners | Where-Object { $_ -eq 'AgesOfCalradia.WarScoreRecovery.v1' }).Count -ne 1) { throw "Harmony fixture did not bind $name." }
    }
}
finally {
    $peace.GetMethod('Uninstall', $patchFlags).Invoke($null, @($harmony)) | Out-Null
    $recovery.GetMethod('Uninstall', $patchFlags).Invoke($null, @($harmony)) | Out-Null
}

'PASS: native protected-Core Harmony targets install and uninstall as one sidecar fixture.'
