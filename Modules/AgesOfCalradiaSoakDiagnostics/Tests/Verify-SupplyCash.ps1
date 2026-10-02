param([string]$DiagnosticsAssemblyPath=(Join-Path (Split-Path -Parent $PSScriptRoot) 'bin\Win64_Shipping_Client\AgesOfCalradia.SoakDiagnostics.dll'))
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Verify-SupplyCapture.ps1') -DiagnosticsAssemblyPath $DiagnosticsAssemblyPath
$cash=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.SupplyCashObserver',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$instanceFlags=[Reflection.BindingFlags]'Instance,NonPublic'
$delta=$cash.GetMethod('UnobservedDelta',$flags)
foreach($case in @(@(100,70,0,-30,0),@(100,60,0,-30,-10),@(100,100,0,30,-30),@(100,130,0,0,30),@(100,100,0,0,0))){
 if($delta.Invoke($null,@([double]$case[0],[double]$case[1],[double]$case[2],[double]$case[3])) -ne $case[4]){throw 'Nested cash residual contract failed'}
}
$h=[HarmonyLib.Harmony]::new('aoc.supply.cash.fixture')
try{
 $cash.GetMethod('Install',$flags).Invoke($null,@($h))|Out-Null
 foreach($method in $cash.GetMethod('BoundaryTargets',$flags).Invoke($null,@())){
  if(@($method.GetParameters()|Where-Object {$_.ParameterType.IsByRef}).Count){throw 'Unsafe argument-array boundary'}
  if(-not ([HarmonyLib.Harmony]::GetPatchInfo($method).Owners -contains 'aoc.supply.cash.fixture')){throw "Missing boundary $method"}
 }
}finally{$h.UnpatchAll('aoc.supply.cash.fixture')}
Add-Type -TypeDefinition 'public sealed class CashFixture { public int Gold {get;set;} public string StringId {get{return "synthetic-wallet";}} }'
$cash.GetMethod('Reset',$flags).Invoke($null,@())|Out-Null
$path=$capture.GetMethod('BeginSession',$flags).Invoke($null,@([string]$testRoot,[Func[double]]{42.5}))
$write.Invoke($null,@('SESSION_START',[long]0,[long]0,'fixture','supply_v7',[double]0,[double]0,'synthetic'))|Out-Null
$take=$cash.GetMethod('Capture',$flags); $after=$cash.GetMethod('After',$flags)
$wallet=[CashFixture]::new(); $wallet.Gold=100
$procurement=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.ProcurementObservation',$true)
$transfer=$procurement.GetMethod('WriteTransfer',$flags)
$transfer.Invoke($null,@('fixture-transfer','fixture-order','dispatch','fixture-shop','begin'))|Out-Null
$outer=$take.Invoke($null,@($wallet,'Gold','outer'))
$outer.GetType().GetField('TransferEndpoint',$instanceFlags).SetValue($outer,$true)
$inner=$take.Invoke($null,@($wallet,'Gold','inner'))
$wallet.Gold=70; $after.Invoke($null,@($inner,$null))|Out-Null
# Unhooked ten-unit change is found at the wider boundary, not the whole forty.
$wallet.Gold=60; $after.Invoke($null,@($outer,$null))|Out-Null
$transfer.Invoke($null,@('fixture-transfer','fixture-order','dispatch','fixture-shop','committed'))|Out-Null
$outer=$take.Invoke($null,@($wallet,'Gold','outer-net-zero'))
$inner=$take.Invoke($null,@($wallet,'Gold','inner-credit'))
$wallet.Gold=90; $after.Invoke($null,@($inner,$null))|Out-Null
$wallet.Gold=60; $after.Invoke($null,@($outer,$null))|Out-Null
$w=$outer.GetType().GetField('Wallet',$instanceFlags).GetValue($outer)
if($w.GetType().GetField('Observed',$instanceFlags).GetValue($w) -ne -40){throw 'Actual observer double-counted nested change'}
# Real native objects, without starting a Campaign: aliases must follow leader
# changes, and switching back to a direct wallet must read its own backing cash.
$party=[Runtime.Serialization.FormatterServices]::GetUninitializedObject([TaleWorlds.CampaignSystem.Party.MobileParty])
$component=[Runtime.Serialization.FormatterServices]::GetUninitializedObject([TaleWorlds.CampaignSystem.Party.PartyComponents.LordPartyComponent])
$heroA=[Runtime.Serialization.FormatterServices]::GetUninitializedObject([TaleWorlds.CampaignSystem.Hero])
$heroB=[Runtime.Serialization.FormatterServices]::GetUninitializedObject([TaleWorlds.CampaignSystem.Hero])
$party.GetType().GetField('_partyComponent',$instanceFlags).SetValue($party,$component)
$lordFlag=$party.GetType().GetField('<IsLordParty>k__BackingField',$instanceFlags)
$lordFlag.SetValue($party,$true)
$leader=$component.GetType().GetField('_leader',$instanceFlags)
$get=$cash.GetMethod('Get',$flags)
$tradeProperty=$party.GetType().GetProperty('PartyTradeGold')
foreach($hero in @($heroA,$heroB)){
 $leader.SetValue($component,$hero)
 $canonical=$get.Invoke($null,@($party,$tradeProperty,'synthetic_alias'))
 if(-not [object]::ReferenceEquals($canonical.GetType().GetField('Owner',$instanceFlags).GetValue($canonical),$hero)){throw 'Lord wallet did not canonicalize to current leader'}
 $directHero=$get.Invoke($null,@($hero,$hero.GetType().GetProperty('Gold'),'synthetic_alias'))
 if(-not [object]::ReferenceEquals($canonical,$directHero)){throw 'Hero and party got separate canonical wallets'}
}
$lordFlag.SetValue($party,$false)
$party.GetType().GetField('_partyTradeGold',$instanceFlags).SetValue($party,77)
$direct=$get.Invoke($null,@($party,$tradeProperty,'synthetic_direct'))
if($direct.GetType().GetMethod('Read',$instanceFlags).Invoke($direct,@()) -ne 77){throw 'Direct party wallet did not return to own backing cash'}
$capture.GetMethod('Stop',$flags).Invoke($null,@('synthetic_cash_complete'))|Out-Null
$rows=@(Import-Csv -LiteralPath $path -Delimiter "`t")
$changes=@($rows|Where-Object kind -eq 'WALLET_CHANGE')
if(@($changes|Where-Object {$_.detail -match 'procurementTransfer=fixture-transfer'}).Count -ne 2){throw 'Native cash rows lost transaction context'}
if(@($changes|Where-Object {$_.detail -match 'procurementTransfer=none'}).Count -ne 2){throw 'Transaction context leaked into unrelated cash'}
$nets=@($changes|ForEach-Object {[double]$_.after-[double]$_.before})
if(($nets -join ',') -ne '-30,-10,30,-30'){throw "Serialized observer net changes wrong: $nets"}
$receipts=@($rows|Where-Object kind -eq 'GOLD_TRANSFER_ENDPOINT')
if($receipts.Count -ne 1 -or [double]$receipts[0].before -ne 100 -or [double]$receipts[0].after -ne 60){throw 'Transfer context lost beneath nested cash observations'}
if(@($rows|Where-Object { $_.kind -eq 'WALLET_BASELINE' -and $_.owner -like 'CashFixture/*' }).Count -ne 1){throw 'Duplicate wallet baseline'}
if(@($rows|Where-Object kind -eq 'WALLET_ALIAS').Count -ne 3){throw 'Leader/direct alias transitions missing'}
# Registration changes native GetHashCode after initial trade-wallet observation.
# Actual native object and actual Get/Capture/After, not a substitute dictionary.
$cash.GetMethod('Reset',$flags).Invoke($null,@())|Out-Null
$path=$capture.GetMethod('BeginSession',$flags).Invoke($null,@([string]$testRoot,[Func[double]]{42.5}))
$newParty=[Runtime.Serialization.FormatterServices]::GetUninitializedObject([TaleWorlds.CampaignSystem.Party.MobileParty])
$nativeId=[TaleWorlds.ObjectSystem.MBObjectBase].GetField('<Id>k__BackingField',$instanceFlags)
$rawCash=$newParty.GetType().GetField('_partyTradeGold',$instanceFlags)
$first=$get.Invoke($null,@($newParty,$tradeProperty,'synthetic_pre_registration'))
$call=$take.Invoke($null,@($newParty,'PartyTradeGold','synthetic_registration'))
$oldHash=$newParty.GetHashCode()
$id=[Activator]::CreateInstance([TaleWorlds.ObjectSystem.MBGUID],@([uint32]123456))
$nativeId.SetValue($newParty,$id)
if($newParty.GetHashCode() -eq $oldHash){throw 'Fixture did not exercise native hash transition'}
$second=$get.Invoke($null,@($newParty,$tradeProperty,'synthetic_post_registration'))
if(-not [object]::ReferenceEquals($first,$second)){throw 'Native registration duplicated the wallet'}
$innerRegistered=$take.Invoke($null,@($newParty,'PartyTradeGold','synthetic_nested_registered'))
$rawCash.SetValue($newParty,7);$after.Invoke($null,@($innerRegistered,$null))|Out-Null
$rawCash.SetValue($newParty,19);$after.Invoke($null,@($call,$null))|Out-Null
if($first.GetType().GetField('Observed',$instanceFlags).GetValue($first) -ne 19){throw 'Registration lost observed delta'}
if($cash.GetField('Aliases',$flags).GetValue($null).Count -ne 1){throw 'Registration duplicated alias entry'}
$otherParty=[Runtime.Serialization.FormatterServices]::GetUninitializedObject([TaleWorlds.CampaignSystem.Party.MobileParty])
$nativeId.SetValue($otherParty,$id)
$other=$get.Invoke($null,@($otherParty,$tradeProperty,'synthetic_same_native_id'))
if([object]::ReferenceEquals($first,$other)){throw 'Distinct native objects merged by native ID'}
$capture.GetMethod('Stop',$flags).Invoke($null,@('synthetic_registration_complete'))|Out-Null
$registrationRows=@(Import-Csv -LiteralPath $path -Delimiter "`t")
if(@($registrationRows|Where-Object kind -eq 'WALLET_BASELINE').Count -ne 2){throw 'Registration emitted duplicate baseline'}
$rawCash.SetValue($newParty,25)
$unchanged=$get.Invoke($null,@($newParty,$tradeProperty,'synthetic_hidden_mutation'))
if($unchanged.GetType().GetField('Observed',$instanceFlags).GetValue($unchanged) -ne 19){throw 'Lookup silently rebased a true unobserved mutation'}
$cash.GetMethod('ResetPeriod',$flags).Invoke($null,@())|Out-Null
if($cash.GetField('Wallets',$flags).GetValue($null).Count -ne 0 -or $cash.GetField('Aliases',$flags).GetValue($null).Count -ne 0){throw 'Period reset retained wallets'}
$fresh=$get.Invoke($null,@($newParty,$tradeProperty,'synthetic_new_period'))
if([object]::ReferenceEquals($first,$fresh) -or $fresh.GetType().GetField('Start',$instanceFlags).GetValue($fresh) -ne 25){throw 'New period did not rebaseline actual cash'}
# Native alias contract must still delegate lord party gold to its leader.
$getter=[TaleWorlds.CampaignSystem.Party.MobileParty].GetProperty('PartyTradeGold').GetGetMethod()
$calls=@([HarmonyLib.PatchProcessor]::GetOriginalInstructions($getter)|ForEach-Object {if($_.operand -is [Reflection.MethodInfo]){$_.operand.Name}})
foreach($required in @('get_IsLordParty','get_LeaderHero','get_Gold')){if($calls -notcontains $required){throw "Native alias contract changed: $required"}}
'PASS: installed exact non-ref native boundaries; actual observer nested debit, hidden debit, offsetting credit/debit and serialized reconciliation; native lord-wallet alias, leader change and direct-wallet restoration.'
'NOT_EXERCISED: campaign-driven lifecycle, all finance callers, native callback inlining and runtime overhead.'
