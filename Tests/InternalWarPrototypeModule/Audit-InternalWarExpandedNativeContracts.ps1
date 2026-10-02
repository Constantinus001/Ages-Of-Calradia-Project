param(
    [string]$BannerlordDir = 'C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord',
    [string]$IldasmPath = 'C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.7.2 Tools\x64\ildasm.exe'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Read-only, installed-v1.4.8 contract audit. This does not load the game, apply
# patches, or prove gameplay behavior. Any changed/ambiguous IL requires review.
$campaignAssembly = Join-Path $BannerlordDir 'bin\Win64_Shipping_Client\TaleWorlds.CampaignSystem.dll'
if (-not (Test-Path -LiteralPath $campaignAssembly -PathType Leaf)) { throw "CampaignSystem assembly not found: $campaignAssembly" }
if (-not (Test-Path -LiteralPath $IldasmPath -PathType Leaf)) { throw "ildasm not found: $IldasmPath" }

function Normalize-Whitespace([string]$Text) {
    return [regex]::Replace($Text, '\s+', ' ').Trim()
}

function Read-NativeMethod([string]$Item, [string]$ExpectedSignature) {
    $text = (& $IldasmPath $campaignAssembly /TEXT "/ITEM=$Item") -join [Environment]::NewLine
    if ($LASTEXITCODE -ne 0 -or $text -match '(?im)^\s*error\s*:') { throw "Could not disassemble native target: $Item" }
    $methods = [regex]::Matches($text, '(?ms)^\s*\.method\s+(?<signature>.*?)^\s*\{(?<body>.*?)^\s*\}\s*// end of method [^\r\n]+')
    $matchingMethods = @($methods | Where-Object {
        (Normalize-Whitespace $_.Groups['signature'].Value) -ceq $ExpectedSignature
    })
    if ($matchingMethods.Count -ne 1) {
        $available = @($methods | ForEach-Object { Normalize-Whitespace $_.Groups['signature'].Value }) -join '; '
        throw "$Item expected one exact signature match; found $($matchingMethods.Count). Available: $available"
    }
    # ildasm can include overloads (PlayerEncounter also has a static Init()).
    # Select exactly one complete signature, never the first name-only match.
    $signature = Normalize-Whitespace $matchingMethods[0].Groups['signature'].Value
    $body = $matchingMethods[0].Groups['body'].Value
    $parsed = [regex]::Matches($body, '(?ms)^\s*(?<label>IL_[0-9a-fA-F]+):\s+(?<opcode>[A-Za-z0-9_.]+)(?<operand>.*?)(?=^\s*IL_[0-9a-fA-F]+:|\z)')
    if ($parsed.Count -eq 0) { throw "$Item contains no parsed IL instructions." }
    $instructions = @(
        foreach ($instruction in $parsed) {
            [pscustomobject]@{
                Label = $instruction.Groups['label'].Value
                Opcode = $instruction.Groups['opcode'].Value
                Operand = Normalize-Whitespace $instruction.Groups['operand'].Value
            }
        }
    )
    if (@($instructions.Label | Select-Object -Unique).Count -ne $instructions.Count) { throw "$Item contains duplicate IL labels." }
    return [pscustomobject]@{ Item = $Item; Signature = $signature; Instructions = $instructions }
}

function Test-Call($Instruction, [string]$Target) {
    return ($Instruction.Opcode -in @('call', 'callvirt')) -and
        $Instruction.Operand.Contains($Target + '(')
}

function Find-Calls($Method, [string]$Target) {
    for ($index = 0; $index -lt $Method.Instructions.Count; $index++) {
        if (Test-Call $Method.Instructions[$index] $Target) { $index }
    }
}

function Assert-CallCount($Method, [string]$Target, [int]$Expected) {
    $actual = @(Find-Calls $Method $Target).Count
    if ($actual -ne $Expected) { throw "$($Method.Item) expected $Expected call(s) to $Target; found $actual." }
}

function Assert-OrderedIndices([string]$Context, [int[]]$Indices) {
    $previous = -1
    foreach ($index in $Indices) {
        if ($index -le $previous) { throw "$Context call/instruction ordering changed: $($Indices -join ', ')." }
        $previous = $index
    }
}

$headers = (& $IldasmPath $campaignAssembly /TEXT /HEADERS) -join [Environment]::NewLine
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect CampaignSystem assembly headers.' }
$mvids = [regex]::Matches($headers, 'MVID: \{(?<id>[0-9A-Fa-f-]+)\}')
if ($mvids.Count -ne 1 -or $mvids[0].Groups['id'].Value -ne '886629FE-6E60-40D7-9A57-8D46017179D9') {
    throw 'Installed CampaignSystem MVID does not match the audited v1.4.8 compatibility gate.'
}

$getMenu = Read-NativeMethod 'TaleWorlds.CampaignSystem.GameComponents.DefaultEncounterGameMenuModel::GetEncounterMenu' `
    'public hidebysig virtual instance string GetEncounterMenu(class TaleWorlds.CampaignSystem.Party.PartyBase attackerParty, class TaleWorlds.CampaignSystem.Party.PartyBase defenderParty, [out] bool& startBattle, [out] bool& joinBattle) cil managed'
$init = Read-NativeMethod 'TaleWorlds.CampaignSystem.Encounters.PlayerEncounter::Init' `
    'assembly hidebysig instance void Init(class TaleWorlds.CampaignSystem.Party.PartyBase attackerParty, class TaleWorlds.CampaignSystem.Party.PartyBase defenderParty, [opt] class TaleWorlds.CampaignSystem.Settlements.Settlement settlement) cil managed'
$setupCall = 'TaleWorlds.CampaignSystem.Encounters.PlayerEncounter::SetupFields'
$menuCall = 'TaleWorlds.CampaignSystem.ComponentInterfaces.EncounterGameMenuModel::GetEncounterMenu'
$battleCall = 'TaleWorlds.CampaignSystem.Encounters.PlayerEncounter::StartBattle'
$activateCall = 'TaleWorlds.CampaignSystem.GameMenus.GameMenu::ActivateGameMenu'
Assert-CallCount $init $setupCall 1
Assert-CallCount $init $menuCall 2
Assert-CallCount $init $battleCall 1
Assert-CallCount $init $activateCall 1
$menus = @(Find-Calls $init $menuCall)
Assert-OrderedIndices $init.Item @(
    (Find-Calls $init $setupCall), $menus[0], (Find-Calls $init $battleCall), $menus[1], (Find-Calls $init $activateCall)
)

$initiative = Read-NativeMethod 'TaleWorlds.CampaignSystem.GameComponents.DefaultMobilePartyAIModel::GetBestInitiativeBehavior' `
    'public hidebysig virtual instance void GetBestInitiativeBehavior(class TaleWorlds.CampaignSystem.Party.MobileParty mobileParty, [out] valuetype TaleWorlds.CampaignSystem.Party.AiBehavior& bestInitiativeBehavior, [out] class TaleWorlds.CampaignSystem.Party.MobileParty& bestInitiativeTargetParty, [out] float32& bestInitiativeBehaviorScore, [out] valuetype [TaleWorlds.Library]TaleWorlds.Library.Vec2& averageEnemyVec) cil managed'
$isEnemy = Read-NativeMethod 'TaleWorlds.CampaignSystem.GameComponents.DefaultMobilePartyAIModel::IsEnemy' `
    'private hidebysig instance bool IsEnemy(class TaleWorlds.CampaignSystem.Party.PartyBase party, class TaleWorlds.CampaignSystem.Party.MobileParty mobileParty) cil managed'
$factionWar = 'TaleWorlds.CampaignSystem.FactionManager::IsAtWarAgainstFaction'
Assert-CallCount $initiative 'TaleWorlds.CampaignSystem.GameComponents.DefaultMobilePartyAIModel::IsEnemy' 3
Assert-CallCount $isEnemy 'TaleWorlds.CampaignSystem.Party.PartyBase::get_MapFaction' 1
Assert-CallCount $isEnemy 'TaleWorlds.CampaignSystem.Party.MobileParty::get_MapFaction' 1
Assert-CallCount $isEnemy $factionWar 1
Assert-OrderedIndices $isEnemy.Item @(
    (Find-Calls $isEnemy 'TaleWorlds.CampaignSystem.Party.PartyBase::get_MapFaction'),
    (Find-Calls $isEnemy 'TaleWorlds.CampaignSystem.Party.MobileParty::get_MapFaction'),
    (Find-Calls $isEnemy $factionWar)
)

$scores = Read-NativeMethod 'TaleWorlds.CampaignSystem.GameComponents.DefaultMobilePartyAIModel::CalculateInitiativeScoresForEnemy' `
    'private hidebysig instance void CalculateInitiativeScoresForEnemy(class TaleWorlds.CampaignSystem.Party.MobileParty mobileParty, class TaleWorlds.CampaignSystem.Party.MobileParty enemyParty, [out] float32& avoidScore, [out] float32& attackScore, float32 localAdvantage, float32 maxAggressiveness) cil managed'
$stance = Read-NativeMethod 'TaleWorlds.CampaignSystem.GameComponents.DefaultMobilePartyAIModel::CalculateStanceScore' `
    'private hidebysig instance float32 CalculateStanceScore(class TaleWorlds.CampaignSystem.Party.MobileParty mobileParty, class TaleWorlds.CampaignSystem.Party.MobileParty otherParty) cil managed'
Assert-CallCount $scores 'TaleWorlds.CampaignSystem.GameComponents.DefaultMobilePartyAIModel::CalculateStanceScore' 1
Assert-CallCount $stance $factionWar 1
Assert-CallCount $stance 'Helpers.DiplomacyHelper::IsSameFactionAndNotEliminated' 1
Assert-OrderedIndices $stance.Item @(
    (Find-Calls $stance $factionWar),
    (Find-Calls $stance 'Helpers.DiplomacyHelper::IsSameFactionAndNotEliminated')
)

$partyEncounter = Read-NativeMethod 'TaleWorlds.CampaignSystem.EncounterManager::StartPartyEncounter' `
    'public hidebysig static void StartPartyEncounter(class TaleWorlds.CampaignSystem.Party.PartyBase attackerParty, class TaleWorlds.CampaignSystem.Party.PartyBase defenderParty) cil managed'
$il = $partyEncounter.Instructions
$sameFactionBranches = @(
    for ($i = 0; $i -le $il.Count - 5; $i++) {
        if ($il[$i].Opcode -eq 'ldarg.0' -and
            (Test-Call $il[$i + 1] 'TaleWorlds.CampaignSystem.Party.PartyBase::get_MapFaction') -and
            $il[$i + 2].Opcode -eq 'ldarg.1' -and
            (Test-Call $il[$i + 3] 'TaleWorlds.CampaignSystem.Party.PartyBase::get_MapFaction') -and
            $il[$i + 4].Opcode -in @('bne.un', 'bne.un.s')) { $i }
    }
)
if ($sameFactionBranches.Count -ne 1) { throw "StartPartyEncounter expected one same-MapFaction branch; found $($sameFactionBranches.Count)." }
$branch = $sameFactionBranches[0] + 4
$targetLabel = $il[$branch].Operand
$destinations = @(for ($i = 0; $i -lt $il.Count; $i++) { if ($il[$i].Label -ceq $targetLabel) { $i } })
if ($destinations.Count -ne 1) { throw 'Same-MapFaction branch does not resolve to one IL label.' }
$destination = $destinations[0]
if ($destination + 2 -ge $il.Count -or $il[$destination].Opcode -ne 'ldarg.0' -or
    $il[$destination + 1].Opcode -ne 'ldarg.1' -or
    -not (Test-Call $il[$destination + 2] 'TaleWorlds.CampaignSystem.Actions.StartBattleAction::Apply')) {
    throw 'The different-faction branch no longer calls StartBattleAction.Apply(attackerParty, defenderParty).'
}
if ($branch + 5 -ge $il.Count -or $il[$branch + 1].Opcode -ne 'ldarg.0' -or
    $il[$branch + 2].Opcode -ne 'ldarg.1' -or
    -not (Test-Call $il[$branch + 3] 'TaleWorlds.CampaignSystem.Party.PartyBase::get_MapEventSide') -or
    -not (Test-Call $il[$branch + 4] 'TaleWorlds.CampaignSystem.Party.PartyBase::set_MapEventSide') -or
    $il[$branch + 5].Opcode -notin @('br', 'br.s')) {
    throw 'The same-faction fallthrough no longer assigns the defender map-event side and skips battle creation.'
}
$afterBattle = $destination + 3
if ($afterBattle -ge $il.Count -or $il[$branch + 5].Operand -cne $il[$afterBattle].Label) {
    throw 'Same-faction fallthrough no longer rejoins immediately after battle creation.'
}

$settlementEncounter = Read-NativeMethod 'TaleWorlds.CampaignSystem.EncounterManager::StartSettlementEncounter' `
    'public hidebysig static void StartSettlementEncounter(class TaleWorlds.CampaignSystem.Party.MobileParty attackerParty, class TaleWorlds.CampaignSystem.Settlements.Settlement settlement) cil managed'
$interfaceWar = 'TaleWorlds.CampaignSystem.IFaction::IsAtWarWith'
Assert-CallCount $settlementEncounter $factionWar 2
# The separate player raid-join query intentionally remains native.
Assert-CallCount $settlementEncounter $interfaceWar 1

$beHostile = Read-NativeMethod 'TaleWorlds.CampaignSystem.Actions.BeHostileAction::ApplyInternal' `
    'private hidebysig static void ApplyInternal(class TaleWorlds.CampaignSystem.Party.PartyBase attackerParty, class TaleWorlds.CampaignSystem.Party.PartyBase defenderParty, float32 ''value'') cil managed'
Assert-CallCount $beHostile $interfaceWar 1
$updateVillage = Read-NativeMethod 'TaleWorlds.CampaignSystem.CampaignBehaviors.EncounterGameMenuBehavior::UpdateVillageHostileActionEncounter' `
    'private hidebysig instance void UpdateVillageHostileActionEncounter(class TaleWorlds.CampaignSystem.GameMenus.MenuCallbackArgs args) cil managed'
Assert-CallCount $updateVillage $interfaceWar 1

$startHostile = Read-NativeMethod 'TaleWorlds.CampaignSystem.CampaignBehaviors.VillageHostileActionCampaignBehavior::StartHostileAction' `
    'private hidebysig static void StartHostileAction(valuetype TaleWorlds.CampaignSystem.CampaignBehaviors.VillageHostileActionCampaignBehavior/HostileActionType hostileActionType) cil managed'
$hostileCall = 'TaleWorlds.CampaignSystem.Actions.BeHostileAction::ApplyEncounterHostileAction'
$switchCall = 'TaleWorlds.CampaignSystem.GameMenus.GameMenu::SwitchToMenu'
Assert-CallCount $startHostile $hostileCall 1
Assert-CallCount $startHostile $switchCall 1
$raidWrites = @(
    for ($i = 0; $i -lt $startHostile.Instructions.Count; $i++) {
        $instruction = $startHostile.Instructions[$i]
        if ($instruction.Opcode -eq 'stfld' -and
            $instruction.Operand -ceq 'bool TaleWorlds.CampaignSystem.Encounters.PlayerEncounter::ForceRaid') { $i }
    }
)
if ($raidWrites.Count -ne 1) { throw "StartHostileAction expected exactly one ForceRaid field write; found $($raidWrites.Count)." }
if ($raidWrites[0] -lt 1 -or $startHostile.Instructions[$raidWrites[0] - 1].Opcode -ne 'ldc.i4.1') {
    throw 'StartHostileAction no longer sets ForceRaid to true.'
}
$switchIndex = Find-Calls $startHostile $switchCall
if ($switchIndex -lt 1 -or $startHostile.Instructions[$switchIndex - 1].Opcode -ne 'ldstr' -or
    $startHostile.Instructions[$switchIndex - 1].Operand -cne '"encounter"') {
    throw 'StartHostileAction no longer switches to the native encounter menu.'
}
Assert-OrderedIndices $startHostile.Item @((Find-Calls $startHostile $hostileCall), $raidWrites[0], $switchIndex)

$besiege = Read-NativeMethod 'TaleWorlds.CampaignSystem.Party.MobileParty::SetMoveBesiegeSettlement' `
    'public hidebysig instance void SetMoveBesiegeSettlement(class TaleWorlds.CampaignSystem.Settlements.Settlement settlement, valuetype TaleWorlds.CampaignSystem.Party.MobileParty/NavigationType navigationType) cil managed'
Assert-CallCount $besiege 'TaleWorlds.CampaignSystem.Party.MobileParty::ResetAllMovementParameters' 1
Assert-CallCount $besiege 'TaleWorlds.CampaignSystem.Party.MobileParty::SetTargetSettlement' 1
Assert-CallCount $besiege 'TaleWorlds.CampaignSystem.Party.MobileParty::set_DefaultBehavior' 1

$raidMovement = Read-NativeMethod 'TaleWorlds.CampaignSystem.Party.MobileParty::SetMoveRaidSettlement' `
    'public hidebysig instance void SetMoveRaidSettlement(class TaleWorlds.CampaignSystem.Settlements.Settlement settlement, valuetype TaleWorlds.CampaignSystem.Party.MobileParty/NavigationType navigationType, bool isTargetingPort) cil managed'
Assert-CallCount $raidMovement 'TaleWorlds.CampaignSystem.Party.MobileParty::ResetAllMovementParameters' 1
Assert-CallCount $raidMovement 'TaleWorlds.CampaignSystem.Party.MobileParty::SetTargetSettlement' 1
$raidBehavior = @(Find-Calls $raidMovement 'TaleWorlds.CampaignSystem.Party.MobileParty::set_DefaultBehavior')
if ($raidBehavior.Count -ne 1 -or $raidMovement.Instructions[$raidBehavior[0] - 1].Opcode -ne 'ldc.i4.4') {
    throw 'Raid movement no longer selects native RaidSettlement behavior.'
}
$sideSetter = Read-NativeMethod 'TaleWorlds.CampaignSystem.Party.PartyBase::set_MapEventSide' `
    'public hidebysig specialname instance void set_MapEventSide(class TaleWorlds.CampaignSystem.MapEvents.MapEventSide ''value'') cil managed'
$removeSide = 'TaleWorlds.CampaignSystem.MapEvents.MapEventSide::RemovePartyInternal'
$addSide = 'TaleWorlds.CampaignSystem.MapEvents.MapEventSide::AddPartyInternal'
Assert-CallCount $sideSetter $removeSide 1
Assert-CallCount $sideSetter $addSide 1
Assert-OrderedIndices $sideSetter.Item @((Find-Calls $sideSetter $removeSide), (Find-Calls $sideSetter $addSide))
$addParty = Read-NativeMethod 'TaleWorlds.CampaignSystem.MapEvents.MapEventSide::AddPartyInternal' `
    'assembly hidebysig instance void AddPartyInternal(class TaleWorlds.CampaignSystem.Party.PartyBase party) cil managed'
$involve = 'TaleWorlds.CampaignSystem.MapEvents.MapEvent::AddInvolvedPartyInternal'
$invalidate = 'TaleWorlds.CampaignSystem.MapEvents.MapEventSide::InvalidateSimulationSetup'
Assert-CallCount $addParty $involve 1
Assert-CallCount $addParty $invalidate 1
Assert-OrderedIndices $addParty.Item @((Find-Calls $addParty $involve), (Find-Calls $addParty $invalidate))
$sallyMenu = Read-NativeMethod 'TaleWorlds.CampaignSystem.CampaignBehaviors.EncounterGameMenuBehavior::menu_sally_out_from_gate_on_condition' `
    'private hidebysig static bool menu_sally_out_from_gate_on_condition(class TaleWorlds.CampaignSystem.GameMenus.MenuCallbackArgs args) cil managed'
Assert-CallCount $sallyMenu $interfaceWar 1
$sallyConsequence = Read-NativeMethod 'TaleWorlds.CampaignSystem.CampaignBehaviors.EncounterGameMenuBehavior::sally_out_consequence' `
    'private hidebysig instance void sally_out_consequence() cil managed'
Assert-CallCount $sallyConsequence 'TaleWorlds.CampaignSystem.EncounterManager::StartPartyEncounter' 1
$sallyAi = Read-NativeMethod 'TaleWorlds.CampaignSystem.CampaignBehaviors.SallyOutsCampaignBehavior::CheckSallyOut' `
    'private hidebysig instance void CheckSallyOut(class TaleWorlds.CampaignSystem.Settlements.Settlement settlement, bool checkForNavalSallyOut, [out] bool& salliedOut) cil managed'
Assert-CallCount $sallyAi 'TaleWorlds.CampaignSystem.EncounterManager::StartPartyEncounter' 1
$sallyStart = Read-NativeMethod 'TaleWorlds.CampaignSystem.Actions.StartBattleAction::ApplyStartSallyOut' `
    'public hidebysig static void ApplyStartSallyOut(class TaleWorlds.CampaignSystem.Settlements.Settlement settlement, class TaleWorlds.CampaignSystem.Party.MobileParty defenderParty) cil managed'
$startSallyCall = @(Find-Calls $sallyStart 'TaleWorlds.CampaignSystem.Actions.StartBattleAction::ApplyInternal')
if ($startSallyCall.Count -ne 1 -or $sallyStart.Instructions[$startSallyCall[0] - 1].Opcode -ne 'ldc.i4.7') {
    throw 'Native sally creation no longer uses BattleTypes.SallyOut.'
}
Assert-CallCount $sallyStart 'TaleWorlds.CampaignSystem.Settlements.Fief::get_GarrisonParty' 1
Write-Host 'Expanded native v1.4.8 contracts passed: 19 exact method signatures; encounter/AI/raid query and branch contracts; movement; raid reinforcement membership; player sally menu, native AI interception and garrison-led sally creation.'
