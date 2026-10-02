param(
    [string]$BannerlordDir = 'C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord',
    [string]$ModuleRoot = 'C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\Ages Of Calradia',
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\tmp\campaign-label-visibility\bin\Win64_Shipping_Client\AgesOfCalradia.CampaignLabelVisibility.dll')
)

$ErrorActionPreference = 'Stop'
$gameBin = Join-Path $BannerlordDir 'bin\Win64_Shipping_Client'
$moduleBin = Join-Path $ModuleRoot 'bin\Win64_Shipping_Client'
[Reflection.Assembly]::LoadFrom((Join-Path $moduleBin '0Harmony.dll')) | Out-Null
foreach ($name in @(
    'TaleWorlds.Library.dll', 'TaleWorlds.DotNet.dll', 'TaleWorlds.Engine.dll',
    'TaleWorlds.TwoDimension.dll', 'TaleWorlds.GauntletUI.dll',
    'TaleWorlds.Core.dll', 'TaleWorlds.Localization.dll', 'TaleWorlds.ObjectSystem.dll',
    'TaleWorlds.SaveSystem.dll', 'TaleWorlds.CampaignSystem.dll',
    'TaleWorlds.MountAndBlade.dll', 'TaleWorlds.MountAndBlade.GauntletUI.Widgets.dll')) {
    [Reflection.Assembly]::LoadFrom((Join-Path $gameBin $name)) | Out-Null
}
[Reflection.Assembly]::LoadFrom((Join-Path $BannerlordDir 'Modules\SandBox\bin\Win64_Shipping_Client\SandBox.ViewModelCollection.dll')) | Out-Null
[Reflection.Assembly]::LoadFrom((Join-Path $BannerlordDir 'Modules\SandBox\bin\Win64_Shipping_Client\SandBox.View.dll')) | Out-Null

$assembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)
$type = $assembly.GetType('AgesOfCalradia.CampaignLabelVisibility.CampaignLabelVisibilitySubModule', $true)
$instance = [Activator]::CreateInstance($type)
$type.GetMethod('OnSubModuleLoad', [Reflection.BindingFlags]'Instance,NonPublic').Invoke($instance, @()) | Out-Null

$targets = @()
foreach ($original in [HarmonyLib.Harmony]::GetAllPatchedMethods()) {
    $info = [HarmonyLib.Harmony]::GetPatchInfo($original)
    $owned = @($info.Prefixes + $info.Postfixes + $info.Transpilers + $info.Finalizers |
        Where-Object { $_.owner -eq 'AgesOfCalradia.CampaignLabelVisibility' })
    if ($owned.Count -gt 0) {
        $targets += $original.DeclaringType.FullName + '::' + $original.Name
    }
}
if ($targets.Count -ne 1 -or
    $targets -notcontains 'SandBox.ViewModelCollection.Nameplate.SettlementNameplateVM::UpdateNameplateMT') {
    throw "Unexpected campaign-map city-label targets: $($targets -join ', ')."
}
Write-Output 'PASS: campaign-map overview town hiding resolved under .NET Framework; native map zoom limit is untouched.'
