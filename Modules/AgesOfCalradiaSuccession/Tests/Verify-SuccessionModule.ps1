$ErrorActionPreference = 'Stop'
$module = Split-Path -Parent $PSScriptRoot
$manifest = [xml](Get-Content -LiteralPath (Join-Path $module 'SubModule.xml') -Raw)
$project = Get-Content -LiteralPath (Join-Path $module 'AgesOfCalradiaSuccession.csproj') -Raw
$behavior = Get-Content -LiteralPath (Join-Path $module 'SuccessionCampaignBehavior.cs') -Raw
$deathIntegration = Get-Content -LiteralPath (Join-Path $module 'SuccessionDeathIntegration.cs') -Raw
$electionPatches = Get-Content -LiteralPath (Join-Path $module 'SuccessionElectionPatches.cs') -Raw
$bridge = Get-Content -LiteralPath (Join-Path $module 'SuccessionReligionBridge.cs') -Raw
$readme = Get-Content -LiteralPath (Join-Path $module 'README.md') -Raw

if ($manifest.Module.Id.value -ne 'AgesOfCalradiaSuccession') { throw 'Unexpected succession module id.' }
if ($manifest.Module.Version.value -ne 'v0.4.3') { throw 'Unexpected succession claimant-realm naming version.' }
$dependencies = @($manifest.Module.DependedModules.DependedModule | ForEach-Object { $_.Id })
foreach ($required in @('AgesOfCalradia','AgesOfCalradiaReligions')) {
    if ($dependencies -notcontains $required) { throw "Missing module dependency: $required" }
}
foreach ($file in @('SuccessionSubModule.cs','SuccessionCampaignBehavior.cs','SuccessionReligionBridge.cs','SuccessionDiagnostics.cs','SuccessionLaw.cs','SuccessionClaim.cs','SuccessionResolver.cs','SuccessionPersistence.cs','SuccessionService.cs','SuccessionDebugMenu.cs','SuccessionRecognition.cs','SuccessionPoliticsPersistence.cs','SuccessionCoronationMenu.cs','SuccessionCivilWar.cs','SuccessionCampaignMapBorderBridge.cs')) {
    if ($project -notmatch [regex]::Escape($file)) { throw "Missing compile item: $file" }
}
if ($bridge -notmatch 'ReligionService.GetHeroReligion' -or $bridge -notmatch 'ReligionService.GetRealmReligion') {
    throw 'Succession religion bridge is not using the public read-only service.'
}
if ($deathIntegration -notmatch 'KingSelectionKingdomDecision' -or $deathIntegration -notmatch 'RemoveDecision' -or $behavior -notmatch 'ChangeRulingClanAction.Apply') {
    throw 'Verified hereditary replacement path is missing.'
}
if ($behavior -match 'ChangeKingdomAction|DestroyKingdomAction|Harmony') {
    throw 'Succession behavior contains an unsafe kingdom mutation or runtime patch.'
}
if ($behavior -match 'KingdomDecisionAdded.AddNonSerializedListener' -or $behavior -notmatch 'HeroKilledEvent.AddNonSerializedListener' -or
    $behavior -notmatch 'TickEvent.AddNonSerializedListener') { throw 'Death-driven deferred dispatch contract is missing or the unsafe late decision listener remains.' }
foreach ($file in @('SuccessionDeathIntegration.cs','SuccessionDispatchQueue.cs','SuccessionElectionPatches.cs')) {
    if ($project -notmatch [regex]::Escape($file)) { throw "Missing dispatch compile item: $file" }
}
if ($deathIntegration -notmatch 'AOC_Succession_Dispatch_v1' -or $electionPatches -notmatch 'BeforeAddDecision' -or
    $electionPatches -notmatch 'BeforeApplyChosenOutcome') { throw 'Persisted dispatch or ruler-only Harmony guards are missing.' }
if ($readme -notmatch 'does not turn inheritance into an election') {
    throw 'No-election succession boundary is not documented.'
}
$allSource = Get-ChildItem -LiteralPath $module -Filter '*.cs' | Get-Content -Raw
if ($allSource -match 'WorldCalendar|PoliticalMap|IslandExclusion|GUI\\Prefabs') {
    throw 'Succession foundation references protected UI or map systems.'
}
if ($readme -notmatch 'deterministic emergency order' -or $readme -notmatch 'does\s+not restore voting') { throw 'No-claimant deterministic fallback is not documented.' }
if ($behavior -match 'native decision retained|emergency fallback') { throw 'Succession can fall back to a ruler vote.' }
$debugMenu = Get-Content -LiteralPath (Join-Path $module 'SuccessionDebugMenu.cs') -Raw
foreach ($menu in @('town','castle','village')) {
    if ($debugMenu -notmatch ('AddGameMenuOption\("' + $menu + '"')) { throw "Missing debug ruler-death option on $menu menu." }
}
if ($debugMenu -notmatch 'KillCharacterAction.ApplyByOldAge' -or $debugMenu -notmatch 'Hero.MainHero' -or $debugMenu -notmatch 'ShowInquiry') {
    throw 'Debug ruler-death action is missing its native death path, player protection, or confirmation.'
}
if ($behavior -notmatch 'HeroComesOfAgeEvent' -or $behavior -notmatch 'ChangeClanLeaderAction.ApplyWithSelectedNewLeader' -or $behavior -notmatch 'AppointRegent') {
    throw 'Underage-heir regency or adulthood transfer path is missing.'
}
$civilWar = Get-Content -LiteralPath (Join-Path $module 'SuccessionCivilWar.cs') -Raw
if ($behavior -notmatch 'EvaluatePoliticalState' -or $behavior -notmatch 'GetRecognition' -or $behavior -notmatch 'HoldCoronation') {
    throw 'Legitimacy, clan recognition, or coronation mechanics are missing.'
}
if ($civilWar -notmatch 'Kingdom.CreateKingdom' -or $civilWar -notmatch 'ApplyByJoinToKingdomByDefection' -or $civilWar -notmatch 'ApplyByClaimOnThrone') {
    throw 'Debug succession civil-war path is incomplete.'
}
if ($civilWar -notmatch 'pretender.Name \+ "''s Realm"' -or $civilWar -match 'pretender.Name \+ "''s Claimant Realm"') {
    throw 'Generated claimant kingdom does not use the required [claimant name]''s Realm naming.'
}
if ($debugMenu -notmatch 'Cause a succession civil war' -or $debugMenu -notmatch 'ShowInquiry') {
    throw 'Confirmed settlement civil-war debug option is missing.'
}
$subModule = Get-Content -LiteralPath (Join-Path $module 'SuccessionSubModule.cs') -Raw
$politicsPersistence = Get-Content -LiteralPath (Join-Path $module 'SuccessionPoliticsPersistence.cs') -Raw
if ($behavior -notmatch 'RunSafely\("political-state initialization"' -or $behavior -notmatch 'RunSafely\("coronation menu registration"') {
    throw 'Startup-sensitive succession features are not isolated.'
}
if ($bridge -notmatch 'catch \(Exception exception\)' -or $bridge -notmatch 'using neutral legitimacy') {
    throw 'Religion bridge does not have neutral startup fallbacks.'
}
if ($subModule -notmatch 'entering OnSubModuleLoad' -or $subModule -notmatch 'OnGameStart failed') {
    throw 'Early startup diagnostics are incomplete.'
}
if ($politicsPersistence -notmatch 'catch \(UriFormatException\)') {
    throw 'Malformed political save payloads are not isolated.'
}
$borderBridge = Get-Content -LiteralPath (Join-Path $module 'SuccessionCampaignMapBorderBridge.cs') -Raw
if ($civilWar -notmatch 'VariantOfKingdomColor' -or $civilWar -notmatch 'ClanOriginalBanner' -or $civilWar -notmatch 'RequestRefresh') {
    throw 'Claimant realm does not receive a parent-color variant, its claimant clan banner, and an immediate border refresh.'
}
if ($borderBridge -notmatch 'CampaignKingdomBorderBehavior' -or $borderBridge -notmatch 'GetCampaignBehavior' -or $borderBridge -notmatch 'MarkDirty') {
    throw 'Campaign political border sidecar notification is incomplete.'
}
if ($borderBridge -match 'Harmony|CampaignPoliticalTerritoryFill|CampaignKingdomBorderBehavior.cs') {
    throw 'Campaign border bridge patches or owns protected border rendering.'
}
Write-Host 'Succession v0.4.3 verification passed: claimant realm naming, campaign-border refresh, realm color variants, startup isolation, legitimacy, coronation, regencies, no voting, and no protected UI/map edits.'
