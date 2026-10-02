param(
    [string]$BannerlordDir = 'C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord'
)

$ErrorActionPreference = 'Stop'
$module = Split-Path -Parent $PSScriptRoot
$manifest = [xml](Get-Content -LiteralPath (Join-Path $module 'SubModule.xml') -Raw)
$project = Get-Content -LiteralPath (Join-Path $module 'Shipwright\AgesOfCalradiaReligions.Shipwright.csproj') -Raw
$behavior = Get-Content -LiteralPath (Join-Path $module 'Shipwright\ShipwrightCampaignBehavior.cs') -Raw
$launcher = Get-Content -LiteralPath (Join-Path $module 'Shipwright\ShipwrightPortLauncher.cs') -Raw
$contract = Get-Content -LiteralPath (Join-Path $module 'Shipwright\NavalContractVerifier.cs') -Raw
$builder = Get-Content -LiteralPath (Join-Path $module 'Shipwright\DromonBuilderSession.cs') -Raw
$catalog = Get-Content -LiteralPath (Join-Path $module 'Shipwright\NavalUpgradeCatalog.cs') -Raw
$readme = Get-Content -LiteralPath (Join-Path $module 'Shipwright\README.md') -Raw

if ($manifest.Module.Id.value -ne 'AgesOfCalradiaReligions') { throw 'Shipwright is not packaged in the Religions module.' }
if ($manifest.Module.Version.value -ne 'v0.10.0') { throw 'Unexpected Religions Shipwright module version.' }
$coreDependency = @($manifest.Module.DependedModules.DependedModule | Where-Object { $_.Id -eq 'AgesOfCalradia' })
if ($coreDependency.Count -ne 1 -or $coreDependency[0].DependentVersion -ne 'v1.5.14') {
    throw 'Religions is not aligned with the current Ages of Calradia v1.5.14 core contract.'
}

$submodules = @($manifest.Module.SubModules.SubModule)
$shipwrightSubmodule = @($submodules | Where-Object { $_.DLLName.value -eq 'AgesOfCalradiaReligions.Shipwright.dll' })
if ($shipwrightSubmodule.Count -ne 1 -or $shipwrightSubmodule[0].SubModuleClassType.value -ne 'AgesOfCalradiaReligions.Shipwright.ShipwrightSubModule') {
    throw 'Religions manifest does not register exactly one separate Shipwright DLL.'
}
$navalMetadata = @($manifest.Module.DependedModuleMetadatas.DependedModuleMetadata | Where-Object { $_.id -eq 'NavalDLC' })
if ($navalMetadata.Count -ne 1 -or $navalMetadata[0].optional -ne 'true' -or $navalMetadata[0].version -ne 'v1.2.8') {
    throw 'Religions does not declare the audited War Sails v1.2.8 integration as optional.'
}

foreach ($file in @('ShipwrightSubModule.cs','ShipwrightCampaignBehavior.cs','ShipwrightPortLauncher.cs','NavalContractVerifier.cs','ShipwrightDiagnostics.cs','DromonBuilderSession.cs','NavalUpgradeCatalog.cs')) {
    if ($project -notmatch [regex]::Escape($file)) { throw "Missing compile item: $file" }
}
if ($behavior -notmatch 'AddGameMenuOption' -or $behavior -notmatch 'settlement.HasPort' -or $behavior -notmatch 'GameMenuOption.LeaveType.Trade') {
    throw 'Port-town commissioning menu contract is incomplete.'
}
if ($launcher -notmatch 'empire_heavy_ship' -or $launcher -notmatch 'DromonBuilderSession.Start' -or $launcher -notmatch 'PortScreenModes.TradeMode') {
    throw 'Single-Dromon native commissioning path is incomplete.'
}
if ($launcher -notmatch 'GameStateManager.Current.CreateState<PortState>' -or $launcher -match 'new PortState\(' -or $launcher -notmatch 'GameStateManager.Current.PushState' -or $launcher -notmatch 'preview.Owner == mainParty') {
    throw 'Native screen launch or ownership-result verification is missing.'
}
if ($launcher -notmatch 'preview.Owner = portParty' -or $launcher -notmatch 'preview.Owner = null') {
    throw 'Trade-mode candidate attachment or cancellation/failure cleanup is missing.'
}
foreach ($slot in @('fore','aft','bow','hull','side','deck','sail','roof')) {
    if ($builder -notmatch ('"' + $slot + '"')) { throw "Dromon builder is missing ordered slot: $slot" }
}
if ($builder -notmatch 'ShowMultiSelectionInquiry' -or $builder -notmatch 'ShowTextInquiry' -or
    $builder -notmatch 'EquipUpgradePiece' -or $builder -notmatch 'SetName' -or
    $builder -notmatch 'OpenConfiguredCommissioning') {
    throw 'Sequential part selection, naming, configuration, or native preview handoff is incomplete.'
}
if ($catalog -notmatch 'NavalDLC.NavalDLCExtensions' -or $catalog -notmatch 'GetAvailableShipUpgradePieces' -or
    $catalog -notmatch 'BindingFlags.Public \| BindingFlags.Static' -or $catalog -notmatch 'TargetInvocationException') {
    throw 'Optional War Sails current-port merchandise boundary is incomplete.'
}
if ($contract -notmatch 'GetConstructor' -or $contract -notmatch 'AvailableSlots.Count' -or $contract -notmatch 'TryValidate') {
    throw 'War Sails runtime contract audit is incomplete.'
}
$allSource = Get-ChildItem -LiteralPath (Join-Path $module 'Shipwright') -Filter '*.cs' | Get-Content -Raw
if ($allSource -match 'Harmony|ChangeHeroGold|GiveGoldAction|ApplyByProduction|ChangeShipOwnerAction|ItemRoster') {
    throw 'Shipwright bypasses or patches native trade ownership/economy behavior.'
}
if ($allSource -match 'WorldCalendar|PoliticalMap|IslandExclusion|GUI\\Prefabs') {
    throw 'Shipwright source references protected UI or map systems.'
}
if ($readme -notmatch 'native port screen' -or $readme -notmatch 'Cancelling detaches it' -or $readme -notmatch 'separate assembly') {
    throw 'Shipwright native ownership and protected-sidecar boundaries are not documented.'
}

$navalRoot = Join-Path $BannerlordDir 'Modules\NavalDLC'
$navalManifestPath = Join-Path $navalRoot 'SubModule.xml'
$hullsPath = Join-Path $navalRoot 'ModuleData\ship_hulls.xml'
if (-not (Test-Path -LiteralPath $navalManifestPath -PathType Leaf)) { throw "War Sails manifest is missing: $navalManifestPath" }
if (-not (Test-Path -LiteralPath $hullsPath -PathType Leaf)) { throw "War Sails hull catalog is missing: $hullsPath" }
$navalManifest = [xml](Get-Content -LiteralPath $navalManifestPath -Raw)
if ($navalManifest.Module.Version.value -ne 'v1.2.8' -or $navalManifest.Module.RequiredBaseVersion.value -ne 'v1.4.8') {
    throw "Installed War Sails contract is not the audited NavalDLC v1.2.8 / Bannerlord v1.4.8 pair."
}
$hulls = [xml](Get-Content -LiteralPath $hullsPath -Raw)
$dromon = @($hulls.ShipHulls.ShipHull | Where-Object { $_.id -eq 'empire_heavy_ship' })
if ($dromon.Count -ne 1 -or @($dromon[0].AvailableSlots.ShipSlot).Count -eq 0) {
    throw 'Installed War Sails data does not contain exactly one slotted empire_heavy_ship hull.'
}

$baseBin = Join-Path $BannerlordDir 'bin\Win64_Shipping_Client'
$navalBin = Join-Path $navalRoot 'bin\Win64_Shipping_Client'
$assemblyResolver = [ResolveEventHandler]{
    param($sender, $eventArgs)
    $dependencyName = (New-Object Reflection.AssemblyName($eventArgs.Name)).Name + '.dll'
    foreach ($directory in @($navalBin, $baseBin)) {
        $candidate = Join-Path $directory $dependencyName
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return [Reflection.Assembly]::LoadFrom($candidate)
        }
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($assemblyResolver)
try {
    $coreAssembly = [Reflection.Assembly]::LoadFrom((Join-Path $baseBin 'TaleWorlds.Core.dll'))
    $campaignAssembly = [Reflection.Assembly]::LoadFrom((Join-Path $baseBin 'TaleWorlds.CampaignSystem.dll'))
    $partyType = $campaignAssembly.GetType('TaleWorlds.CampaignSystem.Party.PartyBase', $true)
    $shipType = $campaignAssembly.GetType('TaleWorlds.CampaignSystem.Naval.Ship', $true)
    $shipHullType = $coreAssembly.GetType('TaleWorlds.Core.ShipHull', $true)
    $portStateType = $campaignAssembly.GetType('TaleWorlds.CampaignSystem.GameState.PortState', $true)
    $portModeType = $campaignAssembly.GetType('TaleWorlds.CampaignSystem.GameState.PortScreenModes', $true)
    $gameStateManagerType = $coreAssembly.GetType('TaleWorlds.Core.GameStateManager', $true)

    if ($null -eq $shipType.GetConstructor(@($shipHullType))) { throw 'Installed API is missing public Ship(ShipHull).' }
    $portSignature = [Type[]]@($partyType, $partyType, [Action], $portModeType)
    if ($null -eq $portStateType.GetConstructor($portSignature)) { throw 'Installed API is missing the audited callback PortState constructor.' }
    $createStateMethods = @($gameStateManagerType.GetMethods() | Where-Object { $_.Name -eq 'CreateState' -and $_.IsGenericMethodDefinition })
    if ($createStateMethods.Count -eq 0) { throw 'Installed API is missing GameStateManager.CreateState<T>.' }
    if ([Enum]::GetNames($portModeType) -notcontains 'TradeMode') { throw 'Installed API is missing PortScreenModes.TradeMode.' }
}
finally {
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($assemblyResolver)
}

$binary = Join-Path $module 'bin\Win64_Shipping_Client\AgesOfCalradiaReligions.Shipwright.dll'
if (-not (Test-Path -LiteralPath $binary -PathType Leaf)) { throw "Release binary is missing: $binary" }

Write-Host 'Religions Shipwright v0.10.0 verification passed: eight ordered vanilla-part steps, naming, native port filtering, manager-created 3D preview, Trade-mode economy, and protected isolation.'
