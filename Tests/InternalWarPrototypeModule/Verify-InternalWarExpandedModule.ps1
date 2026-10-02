param([string]$ModuleRoot = $PSScriptRoot)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $ModuleRoot).Path

function Require-Text {
    param([string]$Text, [string]$Pattern, [string]$Message)
    if ($Text -notmatch $Pattern) { throw $Message }
}

function Read-ModuleFile {
    param([string]$Name)
    $path = Join-Path $root $Name
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing required module file: $Name" }
    return Get-Content -LiteralPath $path -Raw
}

$project = Read-ModuleFile 'AgesOfCalradiaInternalWarsTest.csproj'
$manifest = [xml](Read-ModuleFile 'SubModule.xml')
$assemblyInfo = Read-ModuleFile 'AssemblyInfo.cs'
$conflict = Read-ModuleFile 'InternalConflictRecord.cs'
$raid = Read-ModuleFile 'InternalWarTestRaids.cs'
$combat = Read-ModuleFile 'InternalWarCombatService.cs'
$ai = Read-ModuleFile 'InternalWarTestAiCoordinator.cs'
$aiPatches = Read-ModuleFile 'InternalWarAiNativePatches.cs'
$raidPatches = Read-ModuleFile 'InternalWarRaidPatches.cs'
$monitor = Read-ModuleFile 'InternalWarDiagnosticMonitor.cs'
$diagnostics = Read-ModuleFile 'InternalWarDiagnosticsReport.cs'
$menu = Read-ModuleFile 'InternalWarTestMenu.cs'
$recovery = Read-ModuleFile 'InternalWarTestRecovery.cs'
$readme = Read-ModuleFile 'README.md'

if ($manifest.Module.Version.value -ne 'v0.6.0') { throw 'Unexpected test-module manifest version.' }
Require-Text $assemblyInfo 'AssemblyVersion\("0\.6\.0\.0"\)' 'Assembly version does not match v0.6.0.'

foreach ($file in @(
    'InternalConflictRecord.cs', 'InternalWarRaidRecord.cs', 'InternalWarCombatService.cs',
    'InternalWarCombatPatches.cs', 'InternalWarAiNativePatches.cs', 'InternalWarRaidPatches.cs',
    'InternalWarTestRaids.cs', 'InternalWarTestAiCoordinator.cs', 'InternalWarDiagnosticsReport.cs',
    'InternalWarDiagnosticMonitor.cs', 'InternalWarSiegeCleanup.cs', 'InternalWarTestDiplomacy.cs',
    'InternalWarControllers.cs', 'InternalWarScopedDataStore.cs', 'InternalWarNpcRaids.cs', 'InternalWarRaidArrivalPatch.cs',
    'InternalWarRaidJoinPolicy.cs', 'InternalWarSallyService.cs', 'InternalWarSallyPatches.cs',
    'InternalWarReliefService.cs', 'InternalWarReliefPatches.cs', 'InternalWarNonCapturingQuestPatch.cs', 'InternalWarRecoveryState.cs',
    'InternalWarRealmRules.cs', 'InternalWarRealmGovernance.cs', 'InternalWarRealmMenu.cs', 'InternalWarAcceptanceChecks.cs')) {
    Require-Text $project ([regex]::Escape($file)) "Expanded module source is not compiled: $file"
}

Require-Text $conflict 'enum InternalConflictPhase \{ Active, PeacePending, Closed \}' 'Conflict ledger phases are incomplete.'
Require-Text $conflict 'ActiveOperationId' 'Conflict ledger does not bind operations by identity.'
Require-Text $conflict 'CompleteOperation' 'Conflict ledger cannot complete a guarded operation.'
Require-Text $conflict 'RequestPeace' 'Conflict ledger cannot request guarded peace.'
Require-Text $conflict 'InternalWarGoal' 'Conflict ledger has no persisted war-goal type.'
Require-Text $conflict 'TryAssignSettlementClaim' 'Conflict ledger cannot constrain an active settlement claim.'
Require-Text $conflict 'MarkGoalSatisfied' 'Conflict ledger cannot record a captured claim outcome.'

Require-Text $combat 'Opposed\(' 'Combat policy lacks exact clan-pair hostility.'
Require-Text $combat 'CanStartFieldBattle' 'Combat policy lacks field-battle gating.'
Require-Text $combat 'CanJoinFieldBattle' 'Combat policy lacks field-battle join gating.'
Require-Text $ai 'SetMoveBesiegeSettlement\(target, MobileParty\.NavigationType\.Default\)' 'NPC siege coordinator lacks the audited native movement order.'
Require-Text $ai 'internal void Defer\(\).*CampaignTime\.Now\.ToHours \+ 6' 'NPC siege coordinator lacks backoff after an operation.'
Require-Text $aiPatches 'CalculateInitiativeScoresForEnemy' 'NPC hostility scoring patch is absent.'
Require-Text $aiPatches 'StartSettlementEncounter' 'NPC settlement-encounter guard is absent.'

Require-Text $raid 'TryBeginRaid' 'Player raid entry is absent.'
Require-Text $raid 'EnsureEntry' 'Player raid callback revalidation is absent.'
Require-Text $raid 'TryCancelUnstartedRaid' 'Player raid rollback is absent.'
Require-Text $raid 'IsRaidBattle' 'Player raid event identity guard is absent.'
Require-Text $raidPatches 'ApplyInternal' 'Native raid-hostility boundary patch is absent.'
Require-Text $raidPatches 'UpdateVillageHostileActionEncounter' 'Native raid menu boundary patch is absent.'

Require-Text $recovery 'CleanupPostconditions' 'Recovery does not verify cleanup postconditions.'
Require-Text $recovery 'RecoveryBlocker' 'Recovery does not fail closed on invalid persisted state.'
Require-Text $diagnostics 'CreateNew' 'Diagnostics can overwrite a previous report.' # Must appear only as guarded CreateNew.
Require-Text $monitor 'Ctrl\+Shift\+F10' 'Diagnostic hotkey contract is not documented in the monitor.'
Require-Text $menu 'Declare an internal clan war' 'Declaration control is absent from test menus.'
Require-Text $menu 'Raid this rival clan' 'Raid control is absent from test menus.'
Require-Text $menu 'Toggle siege/raid AI for this war' 'Per-war AI control is absent from test menus.'
Require-Text $menu 'Write diagnostic snapshot' 'Diagnostic menu control is absent.'

Require-Text $readme 'v0\.6\.0' 'README does not document the current system.'
Require-Text $readme 'Ctrl\+Shift\+F10' 'README does not document diagnostic recovery evidence.'
Require-Text $readme 'one siege or raid per war' 'README does not state the per-war operation boundary.'

Require-Text (Read-ModuleFile 'InternalWarTestBehavior.cs') 'if \(_root == this\) \{ SyncRegistry\(dataStore\); SyncPoliticalAi\(dataStore\); SyncRealmGovernance\(dataStore\); \}' 'Realm policy is not synchronized by the campaign root.'
Require-Text (Read-ModuleFile 'InternalWarTestDiplomacy.cs') 'GetRealmDeclarationBlocker\(attacker == null \? null : attacker.Kingdom\)' 'Player declarations bypass realm policy.'
Require-Text (Read-ModuleFile 'InternalWarPoliticalAi.cs') 'GetRealmDeclarationBlocker\(kingdom\)' 'NPC declarations bypass realm policy.'
Require-Text $menu 'InternalWarRealmMenu.Register\(starter, behavior, \(\) => _rootBehavior\)' 'Realm controls are not registered with campaign identity protection.'
Write-Host 'Expanded internal-war module contract passed: v0.6.0 registry, controllers, claim goals, combat, raids, AI, realm governance, durable recovery, diagnostics, UI controls, and documented test boundary.'
