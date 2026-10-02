param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\tmp\political-border-editor\bin\Win64_Shipping_Client\AgesOfCalradia.PoliticalBorderEditor.dll'),
    [string]$BannerlordDir = 'C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord'
)

$ErrorActionPreference = 'Stop'
$gameBin = Join-Path $BannerlordDir 'bin\Win64_Shipping_Client'
$moduleBin = Join-Path $PSScriptRoot '..\bin\Win64_Shipping_Client'
[Reflection.Assembly]::LoadFrom((Join-Path $moduleBin '0Harmony.dll')) | Out-Null
foreach ($name in @(
    'TaleWorlds.Library.dll', 'TaleWorlds.DotNet.dll', 'TaleWorlds.Engine.dll',
    'TaleWorlds.TwoDimension.dll', 'TaleWorlds.GauntletUI.dll',
    'TaleWorlds.Engine.GauntletUI.dll', 'TaleWorlds.Core.dll',
    'TaleWorlds.Localization.dll', 'TaleWorlds.ObjectSystem.dll',
    'TaleWorlds.SaveSystem.dll', 'TaleWorlds.CampaignSystem.dll',
    'TaleWorlds.CampaignSystem.ViewModelCollection.dll',
    'TaleWorlds.InputSystem.dll', 'TaleWorlds.MountAndBlade.dll',
    'TaleWorlds.MountAndBlade.GauntletUI.Widgets.dll',
    'TaleWorlds.ScreenSystem.dll')) {
    [Reflection.Assembly]::LoadFrom((Join-Path $gameBin $name)) | Out-Null
}
[Reflection.Assembly]::LoadFrom((Join-Path $BannerlordDir 'Modules\SandBox\bin\Win64_Shipping_Client\SandBox.ViewModelCollection.dll')) | Out-Null
[Reflection.Assembly]::LoadFrom((Join-Path $BannerlordDir 'Modules\Native\bin\Win64_Shipping_Client\TaleWorlds.MountAndBlade.View.dll')) | Out-Null
[Reflection.Assembly]::LoadFrom((Join-Path $BannerlordDir 'Modules\SandBox\bin\Win64_Shipping_Client\SandBox.View.dll')) | Out-Null
[Reflection.Assembly]::LoadFrom((Join-Path $moduleBin 'AgesOfCalradia.dll')) | Out-Null
$navalBin = Join-Path $BannerlordDir 'Modules\NavalDLC\bin\Win64_Shipping_Client'
foreach ($name in @('NavalDLC.dll', 'NavalDLC.ViewModelCollection.dll',
    'NavalDLC.GauntletUI.Widgets.dll', 'NavalDLC.GauntletUI.dll',
    'NavalDLC.View.dll')) {
    [Reflection.Assembly]::LoadFrom((Join-Path $navalBin $name)) | Out-Null
}

$assembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)
$patchType = $assembly.GetType(
    'AgesOfCalradia.PoliticalBorderEditor.PoliticalBorderEditorPatches', $true)
$stateType = $assembly.GetType(
    'AgesOfCalradia.PoliticalBorderEditor.PoliticalBorderEditorState', $true)
$mapType = [HarmonyLib.AccessTools]::TypeByName('SandBox.View.Map.MapScreen')
$nameplateType = [HarmonyLib.AccessTools]::TypeByName(
    'SandBox.ViewModelCollection.Nameplate.SettlementNameplateVM')
if ($null -eq $mapType -or $null -eq $nameplateType) {
    throw 'Bannerlord editor target types were not resolved.'
}
$campaignVec2 = [TaleWorlds.CampaignSystem.CampaignVec2]
$click = @($mapType.GetMethods(
    [Reflection.BindingFlags]'Instance,Public,NonPublic') | Where-Object {
        $_.Name -eq 'HandleLeftMouseButtonClick' -and
        @($_.GetParameters() | Where-Object {
            $_.ParameterType -eq $campaignVec2 }).Count -gt 0
    })
$tick = $mapType.GetMethod('OnFrameTick',
    [Reflection.BindingFlags]'Instance,Public,NonPublic', $null, [Type[]]@([single]), $null)
$nameplate = [HarmonyLib.AccessTools]::Method($nameplateType, 'UpdateNameplateMT')
$borderBehavior = [HarmonyLib.AccessTools]::TypeByName(
    'TwelveMonthCalendar.CampaignKingdomBorderBehavior')
$fillAlpha = if ($null -eq $borderBehavior) { $null } else {
    [HarmonyLib.AccessTools]::Method($borderBehavior,
        'SetPoliticalOverlayAlpha', [Type[]]@([single]))
}
if ($click.Count -ne 1 -or $null -eq $tick -or $null -eq $nameplate -or
    $null -eq $fillAlpha) {
    throw 'Bannerlord editor target methods were not resolved uniquely.'
}
$navalAssembly = [AppDomain]::CurrentDomain.GetAssemblies() |
    Where-Object { $_.GetName().Name -eq 'NavalDLC.View' } |
    Select-Object -First 1
$navalMap = if ($null -eq $navalAssembly) { $null } else {
    $navalAssembly.GetType('NavalDLC.View.Map.NavalMapScreen', $false)
}
$findField = $stateType.GetMethod('FindInstanceField',
    [Reflection.BindingFlags]'Static,NonPublic')
$findMethod = $stateType.GetMethod('FindInstanceMethod',
    [Reflection.BindingFlags]'Static,NonPublic')
$resolvedRay = $findField.Invoke($null, @($navalMap, '_mouseRay'))
$resolvedIntersection = $findMethod.Invoke(
    $null, @($navalMap, 'GetCursorIntersectionPoint'))
if ($null -eq $navalMap -or $null -eq $resolvedRay -or
    $null -eq $resolvedIntersection -or
    $resolvedRay.DeclaringType.FullName -ne 'SandBox.View.Map.MapScreen' -or
    $resolvedIntersection.DeclaringType.FullName -ne 'SandBox.View.Map.MapScreen') {
    throw "NavalMapScreen inherited cursor projection members were not resolved: type=$navalMap; ray=$resolvedRay; rayOwner=$($resolvedRay.DeclaringType.FullName); intersection=$resolvedIntersection; intersectionOwner=$($resolvedIntersection.DeclaringType.FullName)."
}

$cameraType = [HarmonyLib.AccessTools]::TypeByName(
    'SandBox.View.Map.MapCameraView')
foreach ($propertyName in @('Camera', 'CameraFrame', 'CameraBearing',
    'CameraDistance', 'TargetCameraDistance', 'IdealCameraTarget')) {
    $property = if ($null -eq $cameraType) { $null } else {
        $cameraType.GetProperty($propertyName,
            [Reflection.BindingFlags]'Instance,Public,NonPublic')
    }
    if ($null -eq $property -or $null -eq $property.GetGetMethod($true)) {
        throw "Full 2D editor camera property is unavailable: $propertyName"
    }
    if ($propertyName -in @('CameraFrame', 'CameraBearing', 'CameraDistance',
        'TargetCameraDistance') -and $null -eq $property.GetSetMethod($true)) {
        throw "Full 2D editor camera property cannot be restored: $propertyName"
    }
}
$scenePreviewType = $assembly.GetType(
    'AgesOfCalradia.PoliticalBorderEditor.PoliticalBorderEditorScenePreview', $true)
foreach ($methodName in @('BeginFrame', 'AddLine', 'AddDisc', 'EndFrame',
    'CreatePersistentEntity', 'Clear')) {
    $methods = @($scenePreviewType.GetMethods(
        [Reflection.BindingFlags]'Static,NonPublic') | Where-Object {
            $_.Name -eq $methodName
        })
    if ($methods.Count -eq 0) {
        throw "Production editor scene-preview method is unavailable: $methodName"
    }
}

$harmonyId = 'AgesOfCalradia.PoliticalBorderEditor.RuntimeVerification'
$harmony = [HarmonyLib.Harmony]::new($harmonyId)
try {
    $prefix = [HarmonyLib.HarmonyMethod]::new(
        $patchType.GetMethod('BeforeMapClick',
            [Reflection.BindingFlags]'Static,NonPublic'))
    $framePostfix = [HarmonyLib.HarmonyMethod]::new(
        $patchType.GetMethod('AfterMapFrameTick',
            [Reflection.BindingFlags]'Static,NonPublic'))
    $labelPostfix = [HarmonyLib.HarmonyMethod]::new(
        $patchType.GetMethod('AfterSettlementNameplateUpdate',
            [Reflection.BindingFlags]'Static,NonPublic'))
    $labelPostfix.priority = [HarmonyLib.Priority]::Last
    $fillPrefix = [HarmonyLib.HarmonyMethod]::new(
        $patchType.GetMethod('BeforePoliticalOverlayAlpha',
            [Reflection.BindingFlags]'Static,NonPublic'))
    $harmony.Patch($click[0], $prefix, $null, $null, $null) | Out-Null
    $harmony.Patch($tick, $null, $framePostfix, $null, $null) | Out-Null
    $harmony.Patch($nameplate, $null, $labelPostfix, $null, $null) | Out-Null
    $harmony.Patch($fillAlpha, $fillPrefix, $null, $null, $null) | Out-Null
    $owned = @([HarmonyLib.Harmony]::GetAllPatchedMethods() | Where-Object {
        ([HarmonyLib.Harmony]::GetPatchInfo($_).Owners -contains $harmonyId)
    })
    if ($owned.Count -ne 4) {
        throw "Expected four editor runtime targets; resolved $($owned.Count)."
    }
    Write-Output 'PASS: map input, full-2D camera, production preview meshes, settlement labels, and editor fill-alpha bindings resolve under .NET Framework.'
}
finally {
    $harmony.UnpatchAll($harmonyId)
}
