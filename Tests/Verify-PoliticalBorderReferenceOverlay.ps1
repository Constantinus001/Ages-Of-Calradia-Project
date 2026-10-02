param(
    [string]$BannerlordDir = 'C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord',
    [string]$ModuleRoot = 'C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\AOC CORE',
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\tmp\political-border-editor\bin\Win64_Shipping_Client\AgesOfCalradia.PoliticalBorderEditor.dll')
)

$ErrorActionPreference = 'Stop'
$gameBin = Join-Path $BannerlordDir 'bin\Win64_Shipping_Client'
foreach ($name in @(
    'TaleWorlds.Library.dll', 'TaleWorlds.DotNet.dll', 'TaleWorlds.Engine.dll',
    'TaleWorlds.Core.dll', 'TaleWorlds.Localization.dll',
    'TaleWorlds.ObjectSystem.dll', 'TaleWorlds.SaveSystem.dll',
    'TaleWorlds.CampaignSystem.dll', 'TaleWorlds.InputSystem.dll',
    'TaleWorlds.MountAndBlade.dll')) {
    [Reflection.Assembly]::LoadFrom((Join-Path $gameBin $name)) | Out-Null
}
$protectedPath = Join-Path $ModuleRoot 'bin\Win64_Shipping_Client\AgesOfCalradia.dll'
$protected = [Reflection.Assembly]::LoadFrom($protectedPath)
$editor = [Reflection.Assembly]::LoadFrom($AssemblyPath)
$ledger = $protected.GetType('TwelveMonthCalendar.CalendarWorldLedgerVM', $true)
$projection = $ledger.GetMethod('TryGetCampaignToReferenceProjection',
    [Reflection.BindingFlags]'Static,NonPublic')
if ($null -eq $projection -or $projection.ReturnType -ne [bool] -or
    $projection.GetParameters().Count -ne 2) {
    throw 'The protected campaign-to-reference calibration contract is unavailable.'
}

$asset = Join-Path $ModuleRoot 'GUI\SpriteParts\ui_world_calendar\strategic_map_game_overlay_darkred_borders_1672x941.png'
$expectedHash = 'C267E51690A7AC090C90FC736119D770728C4B4874EFB4BA60CF105D910F1FBC'
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $asset).Hash -ne $expectedHash) {
    throw 'The installed calibrated reference overlay asset hash is incorrect.'
}
Add-Type -AssemblyName System.Drawing
$bitmap = [Drawing.Bitmap]::new($asset)
try {
    if ($bitmap.Width -ne 1672 -or $bitmap.Height -ne 941) {
        throw 'The installed calibrated reference overlay dimensions are incorrect.'
    }
}
finally { $bitmap.Dispose() }

$overlay = $editor.GetType(
    'AgesOfCalradia.PoliticalBorderEditor.PoliticalBorderReferenceOverlay', $true)
$buildGraph = $overlay.GetMethod('BuildGraph',
    [Reflection.BindingFlags]'Static,NonPublic')
$nodes = $overlay.GetField('Nodes',
    [Reflection.BindingFlags]'Static,NonPublic').GetValue($null)
$edges = $overlay.GetField('Edges',
    [Reflection.BindingFlags]'Static,NonPublic').GetValue($null)
$nodes.Clear()
$edges.Clear()
$arguments = New-Object object[] 3
$arguments[0] = [string]$asset
$arguments[1] = [double[]]@(1.0, 0.0, 0.0)
$arguments[2] = [double[]]@(0.0, 1.0, 0.0)
$buildGraph.Invoke($null, $arguments) | Out-Null
if ($nodes.Count -ne 4278 -or $edges.Count -ne 11432) {
    throw "Unexpected reference graph topology: nodes=$($nodes.Count); edges=$($edges.Count)."
}
Write-Output 'PASS: installed overlay asset, protected calibration binding, and bounded 4,278-node/11,432-edge TRACE graph.'
