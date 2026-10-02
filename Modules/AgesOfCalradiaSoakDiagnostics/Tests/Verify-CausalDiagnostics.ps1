param(
 [string]$DiagnosticsAssemblyPath=(Join-Path (Split-Path -Parent $PSScriptRoot) 'bin\Win64_Shipping_Client\AgesOfCalradia.SoakDiagnostics.dll'),
 [string]$ProcurementAssemblyPath=(Join-Path $PSScriptRoot '..\..\AgesOfCalradiaWorkshopProcurement\bin\Win64_Shipping_Client\AgesOfCalradia.WorkshopProcurement.dll')
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Verify-SupplyCash.ps1') -DiagnosticsAssemblyPath $DiagnosticsAssemblyPath
$townCash=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.SupplyTownCashObserver',$true)
$target=$townCash.GetMethod('Target',$flags).Invoke($null,@())
$h=[HarmonyLib.Harmony]::new('aoc.causality.fixture')
try {
 $townCash.GetMethod('Install',$flags).Invoke($null,@($h))|Out-Null
 if(-not ([HarmonyLib.Harmony]::GetPatchInfo($target).Owners -contains 'aoc.causality.fixture')){throw 'Town cash target missing'}
 $original=[HarmonyLib.PatchProcessor]::GetOriginalInstructions($target)
 $tapped=@($townCash.GetMethod('Tap',$flags).Invoke($null,(,$original)))
 if($tapped.Count -ne @($original).Count+2){throw 'Town cash observation changed more than DUP and void tap'}
 $rejected=$false
 try {$townCash.GetMethod('Tap',$flags).Invoke($null,(,[System.Collections.Generic.List[HarmonyLib.CodeInstruction]]$tapped))|Out-Null}
 catch {$rejected=$true}
 if(-not $rejected){throw 'Duplicate town cash tap accepted'}
} finally {$h.UnpatchAll('aoc.causality.fixture')}

# Execute the real observer finalizer over native Town instances without a campaign.
$instanceFlags=[Reflection.BindingFlags]'Instance,Public,NonPublic'
$town=[Runtime.Serialization.FormatterServices]::GetUninitializedObject([TaleWorlds.CampaignSystem.Settlements.Town])
$goldField=[HarmonyLib.AccessTools]::Field([TaleWorlds.CampaignSystem.Settlements.SettlementComponent],'<Gold>k__BackingField')
if($null -eq $goldField){throw 'Native gold backing field changed'}
$callType=$townCash.GetNestedType('Call',[Reflection.BindingFlags]'NonPublic')
foreach($delta in @(10,-10,0)) {
 $path=$capture.GetMethod('BeginSession',$flags).Invoke($null,@([string]$testRoot,[Func[double]]{42.5}))
 $state=[Activator]::CreateInstance($callType,$true)
 foreach($pair in @(@('Town',$town),@('Id',[long]1),@('Before',[int]100),@('ModelResult',[int]$delta),@('ModelCalls',[int]1))){$callType.GetField($pair[0],$instanceFlags).SetValue($state,$pair[1])}
 $townCash.GetField('_current',$flags).SetValue($null,$state)
 $goldField.SetValue($town,[int](100+$delta))
 $townCash.GetMethod('After',$flags).Invoke($null,@($state,$true,$null))|Out-Null
 if($townCash.GetField('_current',$flags).GetValue($null)){throw 'Town operation context leaked'}
 $capture.GetMethod('Stop',$flags).Invoke($null,@('fixture_complete'))|Out-Null
 $row=@(Import-Csv -LiteralPath $path -Delimiter "`t"|Where-Object kind -eq 'TOWN_CASH_END')
 if($row.Count -ne 1 -or [int]$row[0].after -ne 100+$delta -or $row[0].detail -notmatch ('modelResult='+$delta+';')){throw 'Actual town cash witness incorrect'}
}

$coverage=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.DiagnosticEventCoverage',$true)
$coverage.GetMethod('Reset',$flags).Invoke($null,@())|Out-Null
foreach($seq in @(4,9)){$coverage.GetMethod('Observe',$flags).Invoke($null,@('WALLET_CHANGE',[long]$seq))|Out-Null}
$json=$coverage.GetMethod('Json',$flags).Invoke($null,@())|ConvertFrom-Json
if($json.families.WALLET_CHANGE.count -ne 2 -or $json.families.WALLET_CHANGE.firstSequence -ne 4 -or $json.families.WALLET_CHANGE.lastSequence -ne 9){throw 'Execution coverage lost row references'}
$coverage.GetMethod('Reset',$flags).Invoke($null,@())|Out-Null
if(($coverage.GetMethod('Json',$flags).Invoke($null,@())|ConvertFrom-Json).families.WALLET_CHANGE){throw 'Session coverage leaked'}

$cost=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.DiagnosticObserverCost',$true)
$cost.GetMethod('Reset',$flags).Invoke($null,@())|Out-Null
$outer=$cost.GetMethod('Measure',$flags).Invoke($null,@('outer'))
$inner=$cost.GetMethod('Measure',$flags).Invoke($null,@('inner'))
$inner.Dispose(); $outer.Dispose(); $outer.Dispose()
$measured=$cost.GetMethod('Json',$flags).Invoke($null,@())|ConvertFrom-Json
if($measured.outer.calls -ne 1 -or $measured.inner.calls -ne 1 -or $measured.outer.exclusiveSeconds -lt 0 -or $measured.inner.exclusiveSeconds -lt 0){throw 'Nested timing or idempotent disposal incorrect'}
$cost.GetMethod('Reset',$flags).Invoke($null,@())|Out-Null
if($cost.GetMethod('Json',$flags).Invoke($null,@()) -ne '{}'){throw 'Observer timing leaked across sessions'}

[Reflection.Assembly]::LoadFrom((Join-Path (Split-Path $ProcurementAssemblyPath) 'AgesOfCalradia.CampaignSystems.dll'))|Out-Null
$procAssembly=[Reflection.Assembly]::LoadFrom((Resolve-Path $ProcurementAssemblyPath).Path)
$abi=$procAssembly.GetType('AgesOfCalradia.WorkshopProcurement.ProcurementDiagnostics',$true)
Add-Type -TypeDefinition @'
using System.Collections.Generic;
public static class CandidateWitnessFixture {
 public static List<string> Rows = new List<string>();
 public static void Record(string shop,string reason,string detail) { Rows.Add(reason+"; "+detail); }
}
'@
$handler=[Delegate]::CreateDelegate([Action[string,string,string]],[CandidateWitnessFixture].GetMethod('Record'))
$event=$abi.GetEvent('CandidateObserved')
$event.AddEventHandler($null,$handler)
try {
 $type=$procAssembly.GetType('AgesOfCalradia.WorkshopProcurement.ProcurementWitnesses',$true)
 $witness=[Activator]::CreateInstance($type,$instanceFlags,$null,@('fixture',[double]10),$null)
 for($i=0;$i -lt 40;$i++){$type.GetMethod('Record',$instanceFlags).Invoke($witness,@([int]$i,'source',[int]3,'reserve',[object[]]@('stock',10)))|Out-Null}
 $type.GetMethod('Finish',$instanceFlags).Invoke($witness,@())|Out-Null
 if([CandidateWitnessFixture]::Rows.Count -ne 33 -or [CandidateWitnessFixture]::Rows[32] -notmatch 'emitted=32; omitted=8;'){throw 'Witness bound or omission receipt failed'}
} finally {$event.RemoveEventHandler($null,$handler)}
'PASS: town-cash target/tap preflight, real native town +/-/zero witnesses, context cleanup, committed event coverage, bounded optional supplier ABI. No game launched.'
