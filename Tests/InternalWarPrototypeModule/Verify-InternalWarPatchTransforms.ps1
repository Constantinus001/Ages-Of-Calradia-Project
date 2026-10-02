param(
    [string]$BannerlordDir = 'C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord',
    [string]$AssemblyPath = (Join-Path $PSScriptRoot 'bin\Release\AgesOfCalradiaInternalWarsTest.dll'),
    [string]$HarmonyPath = (Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.2\lib\net472\0Harmony.dll')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Standalone process only: loads managed metadata and invokes compiled instruction transformers.
# Never registers Harmony patches, constructs a campaign, invokes a native game method, or writes files.
# These checks prove transformation contracts, not CLR emission, game behavior, or mod compatibility.
$campaignPath = Join-Path $BannerlordDir 'bin\Win64_Shipping_Client\TaleWorlds.CampaignSystem.dll'
foreach ($path in @($campaignPath, $AssemblyPath, $HarmonyPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required assembly is missing: $path" }
}
[Reflection.Assembly]::LoadFrom($HarmonyPath) | Out-Null
$campaign = [Reflection.Assembly]::LoadFrom($campaignPath)
if ($campaign.ManifestModule.ModuleVersionId -ne [Guid]'886629fe-6e60-40d7-9a57-8d46017179d9') {
    throw 'This verifier requires the inspected v1.4.8 CampaignSystem MVID.'
}
$module = [Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($AssemblyPath))
$flags = [Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
$readInstructions = [HarmonyLib.PatchProcessor].GetMethods() | Where-Object {
    $_.Name -eq 'GetOriginalInstructions' -and $_.GetParameters()[1].ParameterType -eq [Reflection.Emit.ILGenerator]
}
if (@($readInstructions).Count -ne 1) { throw 'Harmony instruction-reader signature changed.' }
$script:assertions = 0
$script:scenarios = 0

function Assert-Contract([bool]$Condition, [string]$Message) {
    $script:assertions++
    if (-not $Condition) { throw $Message }
}

function Find-Method([Reflection.Assembly]$Assembly, [string]$TypeName, [string]$MethodName) {
    $type = $Assembly.GetType($TypeName, $true)
    $method = $type.GetMethod($MethodName, $flags)
    if ($null -eq $method) { throw "Missing method $TypeName.$MethodName" }
    return $method
}

function New-Instructions {
    return ,(New-Object 'System.Collections.Generic.List[HarmonyLib.CodeInstruction]')
}

function Invoke-Transformer([Reflection.MethodInfo]$Method, $Instructions) {
    $arguments = New-Object object[] 1
    $arguments[0] = $Instructions.PSObject.BaseObject
    $result = $Method.Invoke($null, $arguments)
    return ,([object[]]@($result))
}

function Assert-Rejected([Reflection.MethodInfo]$Method, $Instructions, [string]$Name) {
    $rejected = $false
    try { $null = Invoke-Transformer $Method $Instructions }
    catch {
        $failure = $_.Exception
        while ($null -ne $failure.InnerException) { $failure = $failure.InnerException }
        if ($failure -isnot [InvalidOperationException] -or $failure.Message -notmatch 'expected .*found') { throw }
        $rejected = $true
    }
    Assert-Contract $rejected "$Name accepted an unexpected instruction count/pattern."
    $script:scenarios++
}

function New-Label {
    $dynamicMethod = New-Object Reflection.Emit.DynamicMethod('InternalWarLabelOnly', [void], [Type[]]@())
    return $dynamicMethod.GetILGenerator().DefineLabel()
}

function Matching-Calls($Instructions, [Reflection.MethodInfo]$Method) {
    return ,([object[]]@($Instructions | Where-Object {
        ($_.opcode -eq [Reflection.Emit.OpCodes]::Call -or $_.opcode -eq [Reflection.Emit.OpCodes]::Callvirt) -and $_.operand -eq $Method
    }))
}

$aiType = 'TaleWorlds.CampaignSystem.GameComponents.DefaultMobilePartyAIModel'
$encounterType = 'TaleWorlds.CampaignSystem.EncounterManager'
$specifications = @(
    @{ Patch='InternalWarAiStancePatch'; TargetType=$aiType; Target='CalculateInitiativeScoresForEnemy'; NativeType=$aiType; Native='CalculateStanceScore'; Resolver='StanceScore'; Count=1; Actors=$false },
    @{ Patch='InternalWarAiHostilityPatch'; TargetType=$aiType; Target='GetBestInitiativeBehavior'; NativeType=$aiType; Native='IsEnemy'; Resolver='IsEnemy'; Count=3; Actors=$false },
    @{ Patch='InternalWarAiSettlementEncounterPatch'; TargetType=$encounterType; Target='StartSettlementEncounter'; NativeType='TaleWorlds.CampaignSystem.FactionManager'; Native='IsAtWarAgainstFaction'; Resolver='IsAtWarForSettlement'; Count=2; Actors=$true },
    @{ Patch='InternalWarRaidCrimePatch'; TargetType='TaleWorlds.CampaignSystem.Actions.BeHostileAction'; Target='ApplyInternal'; NativeType='TaleWorlds.CampaignSystem.IFaction'; Native='IsAtWarWith'; Resolver='IsWar'; Count=1; Actors=$true },
    @{ Patch='InternalWarRaidEncounterPatch'; TargetType='TaleWorlds.CampaignSystem.CampaignBehaviors.EncounterGameMenuBehavior'; Target='UpdateVillageHostileActionEncounter'; NativeType='TaleWorlds.CampaignSystem.IFaction'; Native='IsAtWarWith'; Resolver='IsWar'; Count=1; Actors=$false },
    @{ Patch='InternalWarSallyMenuPatch'; TargetType='TaleWorlds.CampaignSystem.CampaignBehaviors.EncounterGameMenuBehavior'; Target='menu_sally_out_from_gate_on_condition'; NativeType='TaleWorlds.CampaignSystem.IFaction'; Native='IsAtWarWith'; Resolver='IsWar'; Count=1; Actors=$false }
)

foreach ($spec in $specifications) {
    $target = Find-Method $campaign $spec.TargetType $spec.Target
    $native = Find-Method $campaign $spec.NativeType $spec.Native
    $patchType = 'AgesOfCalradiaInternalWarsTest.' + $spec.Patch
    $transformer = Find-Method $module $patchType 'Transpiler'
    $resolver = Find-Method $module $patchType $spec.Resolver
    $original = $readInstructions.Invoke($null, [object[]]@($target, $null))
    Assert-Contract ((Matching-Calls $original $native).Count -eq $spec.Count) ($spec.Patch + ': installed native call count changed.')
    $changed = Invoke-Transformer $transformer $original
    Assert-Contract ((Matching-Calls $changed $resolver).Count -eq $spec.Count) ($spec.Patch + ': wrong rewritten call count.')
    Assert-Contract ((Matching-Calls $changed $native).Count -eq 0) ($spec.Patch + ': native call was left partially rewritten.')
    $script:scenarios++

    # Synthetic input isolates metadata preservation and context load order; it is never executed.
    $synthetic = New-Instructions
    $label = New-Label
    for ($index = 0; $index -lt $spec.Count; $index++) {
        $call = New-Object HarmonyLib.CodeInstruction([Reflection.Emit.OpCodes]::Call, $native)
        if ($index -eq 0) {
            $call.labels.Add($label)
            $call.blocks.Add((New-Object HarmonyLib.ExceptionBlock([HarmonyLib.ExceptionBlockType]::BeginExceptionBlock)))
        }
        $synthetic.Add($call)
    }
    $changed = Invoke-Transformer $transformer $synthetic
    $calls = Matching-Calls $changed $resolver
    Assert-Contract ($calls.Count -eq $spec.Count) ($spec.Patch + ': synthetic rewrite count differs.')
    $firstCallIndex = [array]::IndexOf($changed, $calls[0])
    $metadataIndex = $firstCallIndex
    if ($spec.Actors) {
        Assert-Contract ($firstCallIndex -ge 2) ($spec.Patch + ': missing actor operands.')
        Assert-Contract ($changed[$firstCallIndex - 2].opcode -eq [Reflection.Emit.OpCodes]::Ldarg_0) ($spec.Patch + ': attacker argument changed.')
        Assert-Contract ($changed[$firstCallIndex - 1].opcode -eq [Reflection.Emit.OpCodes]::Ldarg_1) ($spec.Patch + ': defender argument changed.')
        $metadataIndex -= 2
    }
    Assert-Contract ($changed[$metadataIndex].labels.Contains($label)) ($spec.Patch + ': branch label was lost or bypasses injected operands.')
    Assert-Contract ($changed[$metadataIndex].blocks.Count -eq 1) ($spec.Patch + ': exception boundary was lost.')
    Assert-Contract (@($changed | Where-Object { $_.labels.Contains($label) }).Count -eq 1) ($spec.Patch + ': branch label was duplicated.')
    Assert-Contract (($changed | ForEach-Object { $_.blocks.Count } | Measure-Object -Sum).Sum -eq 1) ($spec.Patch + ': exception boundary was duplicated.')
    $script:scenarios++

    Assert-Rejected $transformer (New-Instructions) ($spec.Patch + ' missing')
    $extra = New-Instructions
    for ($index = 0; $index -le $spec.Count; $index++) {
        $extra.Add((New-Object HarmonyLib.CodeInstruction([Reflection.Emit.OpCodes]::Call, $native)))
    }
    Assert-Rejected $transformer $extra ($spec.Patch + ' duplicate')
    Write-Host ('PASS compiled transpiler: ' + $spec.Patch)
}

$partyTarget = Find-Method $campaign $encounterType 'StartPartyEncounter'
$partyPatchType = 'AgesOfCalradiaInternalWarsTest.InternalWarAiPartyEncounterPatch'
$partyTransformer = Find-Method $module $partyPatchType 'Transpiler'
$sameSide = Find-Method $module $partyPatchType 'UseNativeSameSide'
$getter = Find-Method $campaign 'TaleWorlds.CampaignSystem.Party.PartyBase' 'get_MapFaction'
$startBattle = Find-Method $campaign 'TaleWorlds.CampaignSystem.Actions.StartBattleAction' 'Apply'
$original = $readInstructions.Invoke($null, [object[]]@($partyTarget, $null))
$nativeBranches = @()
for ($index = 4; $index -lt $original.Count; $index++) {
    if (($original[$index].opcode -eq [Reflection.Emit.OpCodes]::Bne_Un -or $original[$index].opcode -eq [Reflection.Emit.OpCodes]::Bne_Un_S) -and
        $original[$index - 4].opcode -eq [Reflection.Emit.OpCodes]::Ldarg_0 -and $original[$index - 3].operand -eq $getter -and
        $original[$index - 2].opcode -eq [Reflection.Emit.OpCodes]::Ldarg_1 -and $original[$index - 1].operand -eq $getter) {
        $nativeBranches += $original[$index]
    }
}
Assert-Contract ($nativeBranches.Count -eq 1) 'Native party same-faction branch count changed.'
$battleDestination = $nativeBranches[0].operand
$changed = Invoke-Transformer $partyTransformer $original
$calls = Matching-Calls $changed $sameSide
Assert-Contract ($calls.Count -eq 1) 'Party encounter resolver was not inserted exactly once.'
$callIndex = [array]::IndexOf($changed, $calls[0])
Assert-Contract ($changed[$callIndex + 1].opcode -eq [Reflection.Emit.OpCodes]::Brfalse) 'False same-side result must branch to battle.'
Assert-Contract ($changed[$callIndex + 1].operand -eq $battleDestination) 'Native battle destination changed.'
Assert-Contract ((Matching-Calls $changed $startBattle).Count -eq 1) 'Native StartBattleAction.Apply branch was removed or duplicated.'
Assert-Contract ((Matching-Calls $changed $getter).Count -eq 2) 'Party encounter no longer evaluates the original faction operands.'
$script:scenarios++

function New-PartyPattern([int]$Count, [Reflection.Emit.OpCode]$Branch, [Reflection.Emit.Label]$EntryLabel, [Reflection.Emit.Label]$Destination) {
    $result = New-Instructions
    for ($index = 0; $index -lt $Count; $index++) {
        $result.Add((New-Object HarmonyLib.CodeInstruction([Reflection.Emit.OpCodes]::Ldarg_0)))
        $result.Add((New-Object HarmonyLib.CodeInstruction([Reflection.Emit.OpCodes]::Callvirt, $getter)))
        $result.Add((New-Object HarmonyLib.CodeInstruction([Reflection.Emit.OpCodes]::Ldarg_1)))
        $result.Add((New-Object HarmonyLib.CodeInstruction([Reflection.Emit.OpCodes]::Callvirt, $getter)))
        $jump = New-Object HarmonyLib.CodeInstruction($Branch, $Destination)
        $jump.labels.Add($EntryLabel)
        $jump.blocks.Add((New-Object HarmonyLib.ExceptionBlock([HarmonyLib.ExceptionBlockType]::BeginExceptionBlock)))
        $result.Add($jump)
    }
    return ,$result
}

foreach ($branch in @([Reflection.Emit.OpCodes]::Bne_Un_S, [Reflection.Emit.OpCodes]::Bne_Un)) {
    $entry = New-Label
    $destination = New-Label
    $changed = Invoke-Transformer $partyTransformer (New-PartyPattern 1 $branch $entry $destination)
    Assert-Contract ($changed.Count -eq 8) 'Party branch replacement inserted an unexpected number of instructions.'
    Assert-Contract ($changed[4].opcode -eq [Reflection.Emit.OpCodes]::Ldarg_0 -and $changed[5].opcode -eq [Reflection.Emit.OpCodes]::Ldarg_1) 'Party actors are reversed or missing.'
    Assert-Contract ($changed[6].operand -eq $sameSide -and $changed[7].opcode -eq [Reflection.Emit.OpCodes]::Brfalse) 'Party helper/branch polarity changed.'
    Assert-Contract ($changed[7].operand -eq $destination) 'Synthetic branch destination changed.'
    Assert-Contract ($changed[4].labels.Contains($entry) -and $changed[4].blocks.Count -eq 1) 'Party context loads lost the original entry metadata.'
    Assert-Contract ($changed[7].labels.Count -eq 0 -and $changed[7].blocks.Count -eq 0) 'Party branch duplicated moved metadata.'
    $script:scenarios++
}
Assert-Rejected $partyTransformer (New-Instructions) 'Party branch missing'
Assert-Rejected $partyTransformer (New-PartyPattern 2 ([Reflection.Emit.OpCodes]::Bne_Un_S) (New-Label) (New-Label)) 'Party branch duplicated'
Assert-Rejected $partyTransformer (New-PartyPattern 1 ([Reflection.Emit.OpCodes]::Beq_S) (New-Label) (New-Label)) 'Party branch wrong polarity'
Write-Host 'PASS compiled transpiler: InternalWarAiPartyEncounterPatch'
Write-Host ("Internal-war patch transforms passed: $script:scenarios scenarios, $script:assertions assertions; seven compiled transpilers, no patches applied or game methods invoked.")
Write-Host ('Test module SHA-256: ' + (Get-FileHash -LiteralPath $AssemblyPath -Algorithm SHA256).Hash)
