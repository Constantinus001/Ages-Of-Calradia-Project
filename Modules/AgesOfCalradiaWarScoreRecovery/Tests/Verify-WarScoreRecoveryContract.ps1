$ErrorActionPreference = 'Stop'
$module = Split-Path $PSScriptRoot
$root = Split-Path (Split-Path $module)
$core = Join-Path $root 'bin\Win64_Shipping_Client\AgesOfCalradia.dll'
$expectedHash = '560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E'
$actualHash = (Get-FileHash -LiteralPath $core -Algorithm SHA256).Hash
if ($actualHash -ne $expectedHash) { throw "Protected Core hash changed: $actualHash" }

$bannerlord = 'C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client'
@('TaleWorlds.CampaignSystem.dll', 'TaleWorlds.Core.dll', 'TaleWorlds.Library.dll', 'TaleWorlds.ObjectSystem.dll') |
    ForEach-Object { [Reflection.Assembly]::LoadFrom((Join-Path $bannerlord $_)) | Out-Null }
$assembly = [Reflection.Assembly]::LoadFrom($core)
$ledger = $assembly.GetType('TwelveMonthCalendar.CalendarWorldLedgerBehavior', $true)
$flags = [Reflection.BindingFlags]'Instance, NonPublic'
$allFlags = [Reflection.BindingFlags]'Instance, Static, Public, NonPublic'

function Assert-MethodSignature([string]$name, [string]$returnType, [string[]]$parameterTypes) {
    $method = @($ledger.GetMethods($allFlags) | Where-Object { $_.Name -eq $name -and $_.GetParameters().Count -eq $parameterTypes.Count })
    if ($method.Count -ne 1) { throw "Expected one $name overload with $($parameterTypes.Count) parameters." }
    if ($method[0].ReturnType.FullName -ne $returnType) { throw "$name return type changed." }
    for ($index = 0; $index -lt $parameterTypes.Count; $index++) {
        if ($method[0].GetParameters()[$index].ParameterType.FullName -ne $parameterTypes[$index]) { throw "$name parameter $index changed." }
    }
}

Assert-MethodSignature 'ReadWarScore' 'System.Int32' @('TaleWorlds.CampaignSystem.Kingdom', 'TaleWorlds.CampaignSystem.Kingdom')
Assert-MethodSignature 'AddWarScore' 'System.Void' @('TaleWorlds.CampaignSystem.Kingdom', 'TaleWorlds.CampaignSystem.Kingdom', 'System.Int32', 'System.String')
Assert-MethodSignature 'OnMapEventEndedForWarScore' 'System.Void' @('TaleWorlds.CampaignSystem.MapEvents.MapEvent')
Assert-MethodSignature 'OnSettlementOwnerChanged' 'System.Void' @('TaleWorlds.CampaignSystem.Settlements.Settlement', 'System.Boolean', 'TaleWorlds.CampaignSystem.Hero', 'TaleWorlds.CampaignSystem.Hero', 'TaleWorlds.CampaignSystem.Hero', 'TaleWorlds.CampaignSystem.Actions.ChangeOwnerOfSettlementAction+ChangeOwnerOfSettlementDetail')
Assert-MethodSignature 'SetWarScore' 'System.Void' @('TaleWorlds.CampaignSystem.Kingdom', 'TaleWorlds.CampaignSystem.Kingdom', 'System.Int32')
Assert-MethodSignature 'OnPeaceMade' 'System.Void' @('TaleWorlds.CampaignSystem.IFaction', 'TaleWorlds.CampaignSystem.IFaction', 'TaleWorlds.CampaignSystem.Actions.MakePeaceAction+MakePeaceDetail')
Assert-MethodSignature 'GetWarScore' 'System.Int32' @('TaleWorlds.CampaignSystem.Kingdom', 'TaleWorlds.CampaignSystem.Kingdom')
Assert-MethodSignature 'TryConcludeWar' 'System.Boolean' @('TaleWorlds.CampaignSystem.Kingdom', 'TaleWorlds.CampaignSystem.Kingdom', 'System.Boolean', 'System.String&')
$ownerHandler = @($ledger.GetMethods($flags) | Where-Object { $_.Name -eq 'OnSettlementOwnerChanged' })[0]
if ($ownerHandler.GetParameters()[2].Name -ne 'newOwner' -or $ownerHandler.GetParameters()[3].Name -ne 'oldOwner' -or $ownerHandler.GetParameters()[5].Name -ne 'detail') { throw 'Protected settlement-owner parameter identity changed.' }
$active = $ledger.GetField('_active', [Reflection.BindingFlags]'Static, NonPublic')
$scores = $ledger.GetField('_warScores', [Reflection.BindingFlags]'Instance, NonPublic')
$occupations = $ledger.GetField('_warOccupations', [Reflection.BindingFlags]'Instance, NonPublic')
if ($active -eq $null -or $active.FieldType -ne $ledger -or $scores -eq $null -or !$scores.FieldType.IsGenericType -or $scores.FieldType.GetGenericTypeDefinition().FullName -ne 'System.Collections.Generic.List`1' -or $scores.FieldType.GetGenericArguments()[0] -ne [string] -or $occupations -eq $null -or !$occupations.FieldType.IsGenericType -or $occupations.FieldType.GetGenericTypeDefinition().FullName -ne 'System.Collections.Generic.List`1' -or $occupations.FieldType.GetGenericArguments()[0] -ne [string]) { throw 'Protected war-score storage contract changed.' }

'PASS: approved Core hash and war-score recovery private-target contracts verified.'
