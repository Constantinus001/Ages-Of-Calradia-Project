param(
 [string]$DiagnosticsAssemblyPath=(Join-Path (Split-Path $PSScriptRoot) 'bin/Win64_Shipping_Client/AgesOfCalradia.SoakDiagnostics.dll'),
 [string]$FrameworkAssemblyPath=(Join-Path (Split-Path (Split-Path $PSScriptRoot)) 'AgesOfCalradiaCampaignSystems/bin/Win64_Shipping_Client/AgesOfCalradia.CampaignSystems.dll')
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Verify-SupplyCapture.ps1') -DiagnosticsAssemblyPath $DiagnosticsAssemblyPath
$framework=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $FrameworkAssemblyPath).Path)
$bridge=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.NavalCashoutObserver',$true)
$api=$framework.GetType('AgesOfCalradia.CampaignSystems.NavalCashoutObservation',$true)
$emit=$api.GetMethod('Emit',$flags)
$path=$capture.GetMethod('BeginSession',$flags).Invoke($null,@([string]$testRoot,[Func[double]]{42.5}))
try {
 $bridge.GetMethod('Start',$flags).Invoke($null,@())|Out-Null
 $bridge.GetMethod('Start',$flags).Invoke($null,@())|Out-Null
 $ship=[Runtime.Serialization.FormatterServices]::GetUninitializedObject([TaleWorlds.CampaignSystem.Naval.Ship])
 # Exercise the real observer hook binding and nested correlation scope without
 # inventing a live campaign or changing a real wallet.
 $saleObserver=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.NavalSaleObserver',$true)
 $saleHarmony=New-Object HarmonyLib.Harmony('aoc.naval.sale.fixture')
 try {
  $saleObserver.GetMethod('Install',$flags).Invoke($null,@($saleHarmony.PSObject.BaseObject))|Out-Null
  $nativeSale=[HarmonyLib.AccessTools]::Method([TaleWorlds.CampaignSystem.Actions.ChangeShipOwnerAction],'ApplyInternal')
  $salePatches=[HarmonyLib.Harmony]::GetPatchInfo($nativeSale)
  if(@($salePatches.Prefixes|Where-Object owner -eq 'aoc.naval.sale.fixture').Count -ne 1 -or
     @($salePatches.Finalizers|Where-Object owner -eq 'aoc.naval.sale.fixture').Count -ne 1){throw 'Sale operation hooks missing'}
 } finally {$saleHarmony.UnpatchAll('aoc.naval.sale.fixture')}
 $instanceFlags=[Reflection.BindingFlags]'Instance,NonPublic'
 $callType=$saleObserver.GetNestedType('Call',[Reflection.BindingFlags]::NonPublic)
 $outer=[Activator]::CreateInstance($callType,$true)
 $callType.GetField('Id',$instanceFlags).SetValue($outer,'outer-sale')
 $inner=[Activator]::CreateInstance($callType,$true)
 $callType.GetField('Id',$instanceFlags).SetValue($inner,'inner-sale')
 $callType.GetField('Previous',$instanceFlags).SetValue($inner,$outer)
 $saleObserver.GetField('_current',$flags).SetValue($null,$inner)
 $emit.Invoke($null,@('quote',$ship,'revision=fixture; route=battle; nativeBase=10000; effectiveBase=100; hull=10000; hullBasis=.01; alreadyDiscounted=False; returnedQuote=15000; basisValid=True'))|Out-Null
 $bridge.GetMethod('Stop',$flags).Invoke($null,@())|Out-Null
 $emit.Invoke($null,@('quote',$ship,'must_not_be_recorded'))|Out-Null
} finally {
 $capture.GetMethod('Stop',$flags).Invoke($null,@('fixture_complete'))|Out-Null
}
$nativeError=New-Object InvalidOperationException('native-sentinel')
$saleObserver.GetMethod('After',$flags).Invoke($null,@($inner,$false,$nativeError.PSObject.BaseObject))|Out-Null
if($saleObserver.GetProperty('Context',$flags).GetValue($null) -ne '; navalSale=outer-sale'){throw 'Sale finalizer failed to restore nested scope after capture closes'}
$saleObserver.GetMethod('Reset',$flags).Invoke($null,@())|Out-Null
if($saleObserver.GetProperty('HasOpenScope',$flags).GetValue($null)){throw 'Sale scope leaked after reset'}
$rows=@(Import-Csv -LiteralPath $path -Delimiter "`t")
$decisions=@($rows|Where-Object kind -eq NAVAL_POLICY_DECISION)
if($decisions.Count -ne 1){throw 'Observer duplicated or failed to unsubscribe'}
if($decisions[0].detail -notmatch '; ship=\d+'){throw 'Missing canonical ship identity join'}
if($decisions[0].detail -notmatch '; navalSale=inner-sale'){throw 'Missing native sale operation correlation'}
if(@($rows|Where-Object kind -eq NAVAL_POLICY_STATUS).Count -ne 2){throw 'Missing policy status receipts'}
'PASS: real observer ABI subscribes idempotently, joins canonical ship identity, and detaches; fixture only.'
