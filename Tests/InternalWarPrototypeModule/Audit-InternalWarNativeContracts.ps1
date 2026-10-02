param(
    [string]$BannerlordDir = 'C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord',
    [string]$IldasmPath = 'C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.7.2 Tools\x64\ildasm.exe'
)
$ErrorActionPreference = 'Stop'

$campaignAssembly = Join-Path $BannerlordDir 'bin\Win64_Shipping_Client\TaleWorlds.CampaignSystem.dll'
if (-not (Test-Path -LiteralPath $campaignAssembly)) { throw "CampaignSystem assembly not found: $campaignAssembly" }
if (-not (Test-Path -LiteralPath $IldasmPath)) { throw "ildasm not found: $IldasmPath" }

function Read-NativeMethod([string]$Item) {
    $text = (& $IldasmPath $campaignAssembly /TEXT "/ITEM=$Item") -join [Environment]::NewLine
    if ($LASTEXITCODE -ne 0 -or $text -match '(?m)^error\s*:') { throw "Could not disassemble native target: $Item" }
    return $text
}

$headers = (& $IldasmPath $campaignAssembly /TEXT /HEADERS) -join [Environment]::NewLine
if ($headers -notmatch 'MVID: \{886629FE-6E60-40D7-9A57-8D46017179D9\}') {
    throw 'Installed CampaignSystem MVID does not match the prototype compatibility gate.'
}

$continuity = Read-NativeMethod 'TaleWorlds.CampaignSystem.CampaignBehaviors.PartyDiplomaticHandlerCampaignBehavior::CheckSiegeEventContinuity'
if ($continuity -notmatch 'SiegeEvent::CanPartyJoinSide' -or $continuity -notmatch 'MobileParty::set_BesiegerCamp') {
    throw 'Native siege continuity no longer uses the two guarded party-removal contracts.'
}

$mapUpdate = Read-NativeMethod 'TaleWorlds.CampaignSystem.MapEvents.MapEvent::Update'
$warCallCount = ([regex]::Matches($mapUpdate, 'IFaction::IsAtWarWith')).Count
if ($warCallCount -ne 1) { throw "MapEvent.Update expected exactly one IFaction.IsAtWarWith call; found $warCallCount." }

$ownerChange = Read-NativeMethod 'TaleWorlds.CampaignSystem.Actions.ChangeOwnerOfSettlementAction::ApplyBySiege'
if ($ownerChange -notmatch 'Hero newOwner' -or $ownerChange -notmatch 'Hero capturerHero' -or $ownerChange -notmatch 'Settlement settlement') {
    throw 'ApplyBySiege parameters no longer match the capture-redirection patch.'
}

$claimant = Read-NativeMethod 'TaleWorlds.CampaignSystem.CampaignBehaviors.SettlementClaimantCampaignBehavior::OnSettlementOwnerChanged'
if ($claimant -notmatch 'bool openToClaim' -or $claimant -notmatch 'ChangeOwnerOfSettlementDetail detail') {
    throw 'Settlement claimant parameters no longer match the no-redistribution patch.'
}

$playerTravel = Read-NativeMethod 'TaleWorlds.CampaignSystem.Party.MobileParty::SetMoveGoToSettlement'
if ($playerTravel -notmatch 'Settlement::get_GatePosition' -or $playerTravel -notmatch 'MobileParty::set_TargetPosition' -or
    $playerTravel -notmatch 'MobileParty::MoveTargetPoint' -or $playerTravel -notmatch 'MobileParty::set_DefaultBehavior') {
    throw 'Player travel no longer initializes both target positions and settlement approach behavior.'
}
$leaveSettlement = Read-NativeMethod 'TaleWorlds.CampaignSystem.Encounters.PlayerEncounter::LeaveSettlement'
if ($leaveSettlement -notmatch 'LeaveSettlementAction::ApplyForParty') { throw 'Native player settlement exit contract changed.' }

$nativeBegin = Read-NativeMethod 'TaleWorlds.CampaignSystem.CampaignBehaviors.EncounterGameMenuBehavior::game_menu_town_town_besiege_on_consequence'
$beginCalls = @('PlayerEncounter::Finish', 'SiegeEventManager::StartSiegeEvent', 'PlayerSiege::StartPlayerSiege', 'PlayerSiege::StartSiegePreparation')
$previousCall = -1
foreach ($call in $beginCalls) {
    $position = $nativeBegin.IndexOf($call, [StringComparison]::Ordinal)
    if ($position -le $previousCall) { throw "Native player-siege startup sequence changed at $call." }
    $previousCall = $position
}
$nativeBeginCondition = Read-NativeMethod 'TaleWorlds.CampaignSystem.CampaignBehaviors.EncounterGameMenuBehavior::game_menu_town_town_besiege_on_condition'
if ($nativeBeginCondition -notmatch 'FactionManager::IsAtWarAgainstFaction' -or
    $nativeBeginCondition -notmatch 'PartyBase::get_NumberOfHealthyMembers' -or
    $nativeBeginCondition -notmatch 'Settlement::get_IsUnderSiege') {
    throw 'Native siege availability changed; re-audit the record-scoped entry exception.'
}
$playerPreparation = Read-NativeMethod 'TaleWorlds.CampaignSystem.Siege.PlayerSiege::StartSiegePreparation'
if ($playerPreparation -notmatch '"menu_siege_strategies"' -or $playerPreparation -notmatch 'GameMenu::ActivateGameMenu') {
    throw 'Native player siege preparation no longer activates the audited recovery menu.'
}

$setupFields = Read-NativeMethod 'TaleWorlds.CampaignSystem.Encounters.PlayerEncounter::SetupFields'
if ($setupFields -notmatch 'PartyBase attackerParty' -or $setupFields -notmatch 'PartyBase defenderParty' -or
    $setupFields -notmatch 'MapEvent::get_PlayerMapEvent' -or $setupFields -notmatch 'Settlement::get_MapFaction' -or
    $setupFields -notmatch 'PlayerEncounter::set_PlayerSide' -or $setupFields -notmatch 'PlayerEncounter::set_OpponentSide') {
    throw 'Player encounter setup side-assignment contract changed.'
}
foreach ($side in @('PlayerSide', 'OpponentSide')) {
    $setter = Read-NativeMethod ('TaleWorlds.CampaignSystem.Encounters.PlayerEncounter::set_' + $side)
    if ($setter -notmatch 'BattleSideEnum' -or -not $setter.Contains('<' + $side + '>k__BackingField')) {
        throw "Native private side field changed: $side"
    }
}
$finishEncounter = Read-NativeMethod 'TaleWorlds.CampaignSystem.Encounters.PlayerEncounter::Finish'
if ($finishEncounter.IndexOf('GameMenu::ExitToLast') -lt 0 -or
    $finishEncounter.IndexOf('GameMenu::ExitToLast') -gt $finishEncounter.IndexOf('PlayerEncounter::get_Current()')) {
    throw 'Native Finish no longer exits a friendly town menu independently of the encounter.'
}

# Check emitted entry calls, not only source text or comments.
$testAssembly = Join-Path $PSScriptRoot 'bin\Release\AgesOfCalradiaInternalWarsTest.dll'
if (-not (Test-Path -LiteralPath $testAssembly)) { throw 'Build the isolated Release module before auditing its entry sequence.' }
$testBegin = (& $IldasmPath $testAssembly /TEXT '/ITEM=AgesOfCalradiaInternalWarsTest.InternalWarTestBehavior::TryBeginSiege') -join [Environment]::NewLine
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect compiled test entry.' }
$previousCall = -1
foreach ($call in @('InternalWarTestService::GetBeginSiegeBlocker', 'InternalWarTestBehavior::ExitSettlementForTest',
    'SiegeEventManager::StartSiegeEvent', 'PlayerSiege::StartPlayerSiege', 'PlayerSiege::StartSiegePreparation')) {
    $position = $testBegin.IndexOf($call, [StringComparison]::Ordinal)
    if ($position -le $previousCall) { throw "Compiled player-siege handoff missing or reordered: $call" }
    $previousCall = $position
}

$siegeMenus = Read-NativeMethod 'TaleWorlds.CampaignSystem.CampaignBehaviors.SiegeEventCampaignBehavior::AddGameMenus'
if ($siegeMenus -notmatch '"menu_siege_strategies"' -or $siegeMenus -notmatch 'AddWaitGameMenu') {
    throw 'The native siege preparation menu required for emergency cleanup is missing.'
}

Write-Host 'Native v1.4.8 internal-war contracts passed: exact MVID, player travel/entry sequence, settlement exit, siege recovery menu, siege continuity, one MapEvent war query, capture redirect, and claimant suppression.'
