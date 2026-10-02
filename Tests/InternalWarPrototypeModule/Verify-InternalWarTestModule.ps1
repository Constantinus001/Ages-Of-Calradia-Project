$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$manifest = [xml](Get-Content -LiteralPath (Join-Path $root 'SubModule.xml') -Raw)
$project = Get-Content -LiteralPath (Join-Path $root 'AgesOfCalradiaInternalWarsTest.csproj') -Raw
$patches = Get-Content -LiteralPath (Join-Path $root 'InternalWarTestPatches.cs') -Raw
$encounterPatch = Get-Content -LiteralPath (Join-Path $root 'InternalWarTestEncounterPatch.cs') -Raw
$safety = Get-Content -LiteralPath (Join-Path $root 'InternalWarTestPatchSafety.cs') -Raw
$behavior = Get-Content -LiteralPath (Join-Path $root 'InternalWarTestBehavior.cs') -Raw
$behavior += [Environment]::NewLine + (Get-Content -LiteralPath (Join-Path $root 'InternalWarSiegeCleanup.cs') -Raw)
$behavior += [Environment]::NewLine + (Get-Content -LiteralPath (Join-Path $root 'InternalWarTestRecovery.cs') -Raw)
$behavior += [Environment]::NewLine + (Get-Content -LiteralPath (Join-Path $root 'InternalWarTestDiplomacy.cs') -Raw)
$menu = Get-Content -LiteralPath (Join-Path $root 'InternalWarTestMenu.cs') -Raw
$service = Get-Content -LiteralPath (Join-Path $root 'InternalWarTestService.cs') -Raw
$readme = Get-Content -LiteralPath (Join-Path $root 'README.md') -Raw
$packageScript = Get-Content -LiteralPath (Join-Path $root 'Build-InternalWarTestPackage.ps1') -Raw
$nativeAudit = Get-Content -LiteralPath (Join-Path $root 'Audit-InternalWarNativeContracts.ps1') -Raw
$record = Get-Content -LiteralPath (Join-Path $root 'InternalWarTestRecord.cs') -Raw
$diagnostics = Get-Content -LiteralPath (Join-Path $root 'InternalWarTestDiagnostics.cs') -Raw

if ($manifest.Module.Id.value -ne 'AgesOfCalradiaInternalWarsTest') { throw 'Unexpected test module id.' }
if ($manifest.Module.Name.value -notmatch '^TEST ONLY') { throw 'Manifest does not identify the module as test-only.' }
if (@($manifest.Module.DependedModules.DependedModule | Where-Object { $_.Id -eq 'Bannerlord.Harmony' }).Count -ne 1) { throw 'Harmony launcher dependency is missing.' }
if ($project -notmatch '<OutputPath>bin\\\$\(Configuration\)\\</OutputPath>') { throw 'Build output is not isolated inside the test module.' }
foreach ($file in @('InternalWarTestSubModule.cs','InternalWarTestDiagnostics.cs','InternalWarTestRecord.cs','InternalWarTestService.cs','InternalWarTestBehavior.cs','InternalWarTestMenu.cs','InternalWarTestPatchSafety.cs','InternalWarTestPatches.cs','InternalWarTestEncounterPatch.cs','Audit-InternalWarNativeContracts.ps1')) {
    if ($project -notmatch [regex]::Escape($file)) { throw "Missing compile item: $file" }
}
foreach ($target in @('SiegeEvent.CanPartyJoinSide','MobileParty.BesiegerCamp','Town.GetDefenderParties','Town.GetNextDefenderParty','MapEvent.Update','MapEvent.CanPartyJoinBattle','ChangeOwnerOfSettlementAction.ApplyBySiege','SettlementClaimantCampaignBehavior.OnSettlementOwnerChanged')) {
    if (($patches + $safety) -notmatch [regex]::Escape($target)) { throw "Missing documented or validated native target: $target" }
}
if ($safety -notmatch 'SupportedCampaignSystemMvid' -or $safety -notmatch 'return false') { throw 'Fail-closed compatibility gate is missing.' }
$playerStartBody = [regex]::Match($behavior, '(?s)internal bool TryStart\(.*?(?=internal bool TryBeginSiege)').Value
if ($behavior -notmatch 'DeclareWarAction.ApplyByDefault\(attacker, defender\)' -or $playerStartBody -notmatch 'SetMoveGoToSettlement' -or
    $playerStartBody -match 'SetMoveBesiegeSettlement|ApplyStartAssaultAgainstWalls') {
    throw 'Player travel must use settlement approach and native menu assault, not AI siege/assault orders.'
}
$beginBody = [regex]::Match($behavior, '(?s)internal bool TryBeginSiege\(.*?(?=private void EnsurePlayerSiegeIsCurrent)').Value
$beginCalls = @('GetBeginSiegeBlocker(target)', 'ExitSettlementForTest(party)', 'StartSiegeEvent(target, party)',
    'StartPlayerSiege(BattleSideEnum.Attacker)', 'StartSiegePreparation()')
$previousCall = -1
foreach ($call in $beginCalls) {
    $position = $beginBody.IndexOf($call, [StringComparison]::Ordinal)
    if ($position -le $previousCall) { throw "Missing or reordered guarded player-siege handoff: $call" }
    $previousCall = $position
}
if (([regex]::Matches($beginBody, 'EnsurePlayerSiegeIsCurrent\(record, target, party, siege\)')).Count -ne 3 -or
    $beginBody -notmatch 'record == CurrentRecord && record\.IsOperational') {
    throw 'Native entry callbacks are not revalidated or can resurrect an ended test.'
}
if ($menu -notmatch '\[TEST\] Begin siege of this settlement' -or $menu -notmatch 'GetBeginSiegeBlocker\(target\)' -or
    $menu -notmatch 'target\.StringId != record\.SettlementId' -or $service -notmatch 'party\.CurrentSettlement != target' -or
    $service -notmatch 'NumberOfHealthyMembers <= 0') { throw 'Target-only, arrival-gated player siege action is missing.' }
if ($encounterPatch -notmatch 'PlayerEncounter\.SetupFields' -or $encounterPatch -notmatch '__instance != PlayerEncounter\.Current' -or
    $encounterPatch -notmatch 'IsPlayerSiegeEncounter\(attackerParty, defenderParty\)' -or
    $encounterPatch -notmatch '\[HarmonyPrepare\]' -or $safety -notmatch '<PlayerSide>k__BackingField' -or
    $safety -notmatch '<OpponentSide>k__BackingField' -or $safety -notmatch 'field\.FieldType != typeof\(BattleSideEnum\)' -or
    $service -notmatch 'MapEvent\.PlayerMapEvent != null' -or $service -notmatch 'PlayerSiege\.PlayerSiegeEvent == siege') {
    throw 'Player encounter side correction lacks exact-field compatibility or scoped existing-battle guards.'
}
if ($behavior -notmatch 'PlayerEncounter\.LeaveSettlement\(\)' -or $behavior -notmatch 'PlayerEncounter\.Finish\(true\)') { throw 'Player settlement-exit sequence is missing.' }
if ($behavior -notmatch 'attacker != Clan\.PlayerClan' -or $service -notmatch 'MobileParty\.MainParty' -or $menu -notmatch 'EligibleTargets') {
    throw 'Player-clan-centered test contract is missing.'
}
$playerGate = [regex]::Match($service, '(?s)internal static string GetPlayerStartBlocker\(\).*?(?=internal static string GetTargetStartBlocker)').Value
$targetGate = [regex]::Match($service, '(?s)internal static string GetTargetStartBlocker\(.*?(?=internal static IEnumerable<Settlement> GetEligibleTargets)').Value
if ($playerGate -notmatch 'Clan\.PlayerClan' -or $playerGate -match 'playerClan\.IsMinorFaction' -or
    $playerGate -notmatch 'playerClan\.IsUnderMercenaryService' -or $playerGate -notmatch 'playerClan\.IsClanTypeMercenary' -or
    $targetGate -notmatch 'target\.OwnerClan\.IsMinorFaction' -or $targetGate -notmatch 'target\.OwnerClan\.IsUnderMercenaryService') {
    throw 'Player minor-flag exception or mercenary/defender eligibility safeguards are missing.'
}
if ($behavior -notmatch 'InternalWarTestService\.GetPlayerStartBlocker\(\)' -or
    $behavior -notmatch 'InternalWarTestService\.GetTargetStartBlocker\(target\)' -or
    $menu -notmatch 'InternalWarTestService\.GetStartBlocker\(\)' -or
    $menu -notmatch 'args\.Tooltip = new TextObject\(blocker\)' -or $behavior -notmatch 'Cannot start: ') {
    throw 'Shared eligibility gates or precise player diagnostics are missing.'
}
if ($menu -match 'EligibleAttackers|Choose the AI clan') { throw 'Legacy AI-attacker test flow is still present.' }
if ($behavior -match 'SetDoNotMakeNewDecisions\(true\)') { throw 'The player main-party AI decision lock must not be enabled.' }
if ($behavior -notmatch 'AfterSiegeCompletedEvent' -or $behavior -notmatch 'InternalWarTestState\.Defeated') { throw 'Defeat cleanup contract is missing.' }
if ($behavior -notmatch 'MaximumMarchDays = 14' -or $behavior -notmatch 'MaximumConflictDays = 60') { throw 'Conflict safety timeouts are missing.' }
if ($behavior -notmatch 'target\.OwnerClan != defender' -or $behavior -notmatch 'siege\.BesiegerCamp\.LeaderParty != leader') { throw 'Ownership or foreign-siege invalidation is missing.' }
if ($menu -notmatch 'Emergency abort and cleanup' -or $menu -notmatch 'Internal-war status') { throw 'Emergency cleanup or status controls are missing.' }
if ($menu -notmatch 'Kingdom\.RulingClan == Clan\.PlayerClan' -or $behavior -notmatch 'requireMonarch') { throw 'Monarch authority is not enforced.' }
if ($behavior -notmatch 'RoyalPeacePending' -or $behavior -notmatch 'DiplomaticallyFinished' -or $behavior -notmatch 'ContinuePendingCleanup') {
    throw 'Royal-peace test path is incomplete.'
}
if ($menu -notmatch 'menu_siege_strategies') { throw 'Siege-menu recovery controls are missing.' }
if ($behavior -match 'FactionHelper\.FinishAllRelatedHostileActions' -or $behavior -match 'record\.State = InternalWarTestState\.Failed') {
    throw 'Cleanup must remain operational after failure and must not use the unscoped native hostile-action helper.'
}
if ($behavior -notmatch 'CleanupRetryHours = 6' -or $behavior -notmatch 'CampaignTime\.Now\.ToHours >= _nextCleanupAttemptHour' -or
    $behavior -notmatch 'failure != _lastCleanupFailure') { throw 'Cleanup retry backoff or repeated-error suppression is missing.' }
$cleanupBody = [regex]::Match($behavior, '(?s)private void CleanupSiege\(.*?(?=internal void RestoreLeaderControl)').Value
if ($cleanupBody -notmatch 'camp\.LeaderParty == leader' -or $cleanupBody -notmatch 'leader\.AttachedParties\.Count != 0' -or
    $cleanupBody -notmatch 'foreignLeader\.BesiegerCamp != camp') { throw 'Foreign-camp cleanup ownership and single-party detachment guards are missing.' }
$startBody = [regex]::Match($behavior, '(?s)private void OnSiegeStarted\(.*?(?=private void CleanupSiege)').Value
if ($startBody -notmatch '!record\.IsOperational' -or $startBody -notmatch 'record\.State == InternalWarTestState\.RoyalPeacePending' -or
    $startBody -notmatch 'leader\.StringId == record\.LeaderPartyId') { throw 'Siege-start record resurrection guards are missing.' }
$pendingBody = [regex]::Match($behavior, '(?s)internal void ContinuePendingCleanup\(.*?(?=// Native lifecycle adapter)').Value
if ($pendingBody -notmatch 'mapEvent\.IsSiegeAssault' -or $pendingBody -notmatch 'leader\.Party\.MapEvent == mapEvent' -or
    $pendingBody -notmatch 'siege\.BesiegerCamp\.LeaderParty == leader') { throw 'Pending cleanup can affect a foreign map event.' }
if ($pendingBody.IndexOf('CaptureTestSiegeMenu(') -lt 0 -or
    $pendingBody.IndexOf('CaptureTestSiegeMenu(') -ge $pendingBody.IndexOf('EndDiplomacy(') -or
    $behavior -notmatch 'Campaign\.Current\.CurrentMenuContext != testMenuContext' -or
    $behavior -notmatch 'testMenuContext\.GameMenu != testMenu' -or
    $behavior -notmatch 'PlayerEncounter\.Current == testEncounter' -or $behavior -notmatch 'GameMenu\.ExitToLast\(\)') {
    throw 'Owned siege-menu cleanup is not captured before diplomacy or guarded against UI changes.'
}
if ($behavior -notmatch 'try \{ ReconcileAfterLoad\(\); \}' -or $behavior -notmatch 'Both in original kingdom:') {
    throw 'Load cleanup recovery or original-kingdom runtime evidence is missing.'
}
$recordValidationMissing = $record -notmatch 'Enum\.IsDefined' -or $record -notmatch 'startDay < 0' -or $record -notmatch 'PendingFinalState'
$recordMigrationMissing = $record -notmatch 'parts\[0\] == "v1"' -or $record -notmatch '"v2"'
if ($recordValidationMissing -or $recordMigrationMissing) { throw 'Saved payload validation or migration is incomplete.' }
if ($behavior -notmatch 'record\.PendingFinalState' -or $behavior -notmatch 'DiplomaticallyFinished = true') { throw 'Deferred native-event cleanup is missing.' }
if ($diagnostics -notmatch 'catch \(IOException exception\)' -or $diagnostics -notmatch 'Trace\.WriteLine') { throw 'Diagnostic file failures do not leave an observable fallback.' }
$allSource = Get-ChildItem -LiteralPath $root -Filter '*.cs' | Get-Content -Raw
if ($allSource -match 'Kingdom.CreateKingdom|ChangeKingdomAction|Clan\.MapFaction\s*=|WorldCalendar|CampaignPoliticalTerritoryFill') {
    throw 'Test module crosses the no-proxy-kingdom or protected-renderer boundary.'
}
if ($readme -notmatch 'No production `Modules` file is changed' -or $readme -notmatch 'not release code') { throw 'Isolation or prototype limitation is undocumented.' }
$expectedPackageRoot = '$packageRoot = Join-Path $packageContainer ''AgesOfCalradiaInternalWarsTest'''
$packageEscapesIsolation = $packageScript -match 'steamapps|Bannerlord\\Modules'
$packageRootIsMissing = -not $packageScript.Contains($expectedPackageRoot)
$packageGuardIsMissing = $packageScript -notmatch 'StartsWith\(\$resolvedContainer'
$packageChecksumsAreMissing = $packageScript -notmatch 'SHA256SUMS'
if ($packageEscapesIsolation -or $packageRootIsMissing -or $packageGuardIsMissing -or $packageChecksumsAreMissing) {
    throw 'Package builder is not safely confined to the test folder or lacks checksums.'
}
if ($nativeAudit -notmatch '886629FE-6E60-40D7-9A57-8D46017179D9' -or $nativeAudit -notmatch 'warCallCount -ne 1') { throw 'Native IL audit is incomplete.' }
foreach ($recordTestFile in @('Tests\InternalWarRecordVerifier.csproj', 'Tests\Program.cs', 'Tests\InternalWarEligibilityVerifier.csproj', 'Tests\EligibilityProgram.cs', 'Tests\EligibilityGameStubs.cs')) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $recordTestFile))) { throw "Missing deterministic test: $recordTestFile" }
}
$releaseDll = Join-Path $root 'bin\Release\AgesOfCalradiaInternalWarsTest.dll'
$packageRoot = Join-Path $root 'package\AgesOfCalradiaInternalWarsTest'
$packageDll = Join-Path $packageRoot 'bin\Win64_Shipping_Client\AgesOfCalradiaInternalWarsTest.dll'
foreach ($requiredArtifact in @($releaseDll, $packageDll, (Join-Path $packageRoot 'SubModule.xml'), (Join-Path $packageRoot 'README.md'), (Join-Path $packageRoot 'COMPLETION_CHECKLIST.md'), (Join-Path $packageRoot 'SHA256SUMS.txt'))) {
    if (-not (Test-Path -LiteralPath $requiredArtifact)) { throw "Missing built or packaged artifact: $requiredArtifact" }
}
if ((Get-FileHash -LiteralPath $releaseDll -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $packageDll -Algorithm SHA256).Hash) {
    throw 'Packaged DLL does not match the current Release build.'
}
if ((Get-FileHash -LiteralPath (Join-Path $root 'SubModule.xml') -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath (Join-Path $packageRoot 'SubModule.xml') -Algorithm SHA256).Hash) {
    throw 'Packaged manifest does not match the source manifest.'
}
foreach ($document in @('README.md', 'COMPLETION_CHECKLIST.md', 'Summarize-InternalWarDiagnostics.ps1')) {
    if ((Get-FileHash -LiteralPath (Join-Path $root $document) -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath (Join-Path $packageRoot $document) -Algorithm SHA256).Hash) {
        throw "Packaged documentation does not match source: $document"
    }
}
Write-Host 'Isolated internal-war test module contract passed: source-only test location, local build output, same-kingdom native siege, capture redirect, royal peace, v1.4.8 fail-closed patches, and no protected renderer dependency.'
