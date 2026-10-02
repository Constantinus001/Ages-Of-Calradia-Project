param([string]$DiagnosticsAssemblyPath=(Join-Path (Split-Path -Parent $PSScriptRoot) 'bin/Win64_Shipping_Client/AgesOfCalradia.SoakDiagnostics.dll'))
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Verify-WorkshopCashBoundaries.ps1') -DiagnosticsAssemblyPath $DiagnosticsAssemblyPath
$iflags=[Reflection.BindingFlags]'Instance,Public,NonPublic'
$recorder=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.IncidentRecorder',$true)
$incidentRoot=Join-Path $testRoot 'recorder'
New-Item -ItemType Directory -Path $incidentRoot|Out-Null
$constructor=$recorder.GetConstructor($iflags,$null,[Type[]]@([string],[string]),$null)
$rec=$constructor.Invoke(@([string]$incidentRoot,[string]'fixture'))
function Observe-Row([string]$kind,[int]$seq,[double]$before=0,[double]$after=0,[string]$detail=''){
 $f=[string[]]@('utc','fixture',"$seq",'42', $kind,'0','0','wallet','Gold',"$before","$after",$detail)
 $recorder.GetMethod('Observe',$iflags).Invoke($rec,@(($f -join "`t"),$f))|Out-Null
}
Observe-Row 'SESSION_START' 1 0 0 'diagnosticsMvid=fixture'
Observe-Row 'CORE_SYSTEMS' 2 0 0 'policyRevision=fixture'
Observe-Row 'WALLET_CHANGE' 3 0 5
Observe-Row 'WALLET_CHECK' 4 5 7
$packages=@(Get-ChildItem -LiteralPath (Join-Path $incidentRoot 'AocIncidents') -Filter '*.json')
if($packages.Count -ne 1){throw 'First incident not durable immediately'}
$first=Get-Content -LiteralPath $packages[0].FullName -Raw|ConvertFrom-Json
if($first.evidenceRows.Count -ne 4 -or $first.identityRows.Count -ne 2 -or $first.status -ne 'post_window_pending'){throw 'Recorder lost prehistory or provenance'}
Observe-Row 'WALLET_CHECK' 5 5 7
for($n=6;$n -le 70;$n++){Observe-Row 'WALLET_CHANGE' $n 7 7}
$recorder.GetMethod('Flush',$iflags).Invoke($rec,@('rotation'))|Out-Null
$done=Get-Content -LiteralPath $packages[0].FullName -Raw|ConvertFrom-Json
if($done.occurrences -ne 2 -or $done.status -ne 'post_window_complete' -or $done.evidenceRows.Count -ne 68){throw 'Recorder recurrence/post window not bounded correctly'}
Observe-Row 'OTHER' 71 0 0 ('x'*9000)
for($n=72;$n -le 500;$n++){Observe-Row 'OTHER' $n 0 0 ('x'*4000)}
$status=$recorder.GetMethod('StatusJson',$iflags).Invoke($rec,@())|ConvertFrom-Json
if($status.ringBytes -gt 131072 -or $status.omittedExtraRows -lt 1){throw 'Recorder memory/oversized row bound failed'}
# Simulate exhausted owned storage without allocating a large file.
$recorder.GetField('_diskBytes',$iflags).SetValue($rec,[long]33554432)
$recorder.GetMethod('Trigger',$iflags).Invoke($rec,@('second','bounded evidence'))|Out-Null
if(($recorder.GetMethod('StatusJson',$iflags).Invoke($rec,@())|ConvertFrom-Json).status -ne 'storage_or_issue_budget_exhausted'){throw 'Storage cap silently reported healthy'}
if((Get-Content -LiteralPath $packages[0].FullName -Raw) -notmatch 'firstDivergence'){throw 'Existing incident was destroyed'}
$failureRoot=Join-Path $testRoot 'not-a-directory'; [IO.File]::WriteAllText($failureRoot,'fixture')
$failed=$constructor.Invoke(@([string]$failureRoot,[string]'failure'))
if(($recorder.GetMethod('StatusJson',$iflags).Invoke($failed,@())|ConvertFrom-Json).status -ne 'failed'){throw 'Recorder IO failure concealed'}

# A long snapshot sweep must not erase the affected wallet's recent transactions.
$contextRoot=Join-Path $testRoot 'wallet-context'
$rec=$constructor.Invoke(@([string]$contextRoot,[string]'context'))
Observe-Row 'WALLET_BASELINE' 1 100 100
Observe-Row 'WALLET_CHANGE' 2 100 110 'source=synthetic_transaction'
for($n=3;$n -le 303;$n++){Observe-Row 'WALLET_CHECK' $n 110 110}
Observe-Row 'WALLET_CHECK' 304 110 115
$contextFile=Get-ChildItem (Join-Path $contextRoot 'AocIncidents') -Filter '*.json'|Select-Object -First 1
$context=Get-Content -LiteralPath $contextFile.FullName -Raw|ConvertFrom-Json
if(@($context.walletHistoryRows|Where-Object {$_ -match 'synthetic_transaction'}).Count -ne 1){throw 'Snapshot sweep erased relevant wallet history'}
# Normal incidents cannot consume reserved critical slots or disk budget.
$priorityRoot=Join-Path $testRoot 'priority-recorder'
$priority=$constructor.Invoke(@([string]$priorityRoot,[string]'priority'))
for($n=1;$n -le 29;$n++){$recorder.GetMethod('Trigger',$iflags).Invoke($priority,@([string]('family'+$n),[string]'synthetic'))|Out-Null}
$recorder.GetMethod('Trigger',$iflags).Invoke($priority,@([string]'capture_failure:writer',[string]'synthetic critical failure'))|Out-Null
$health=$recorder.GetMethod('StatusJson',$iflags).Invoke($priority,@())|ConvertFrom-Json
if($health.incidents -ne 29 -or $health.criticalIncidents -ne 1 -or $health.issueCapSuppressedOccurrences -ne 1){throw 'Critical slot reservation failed'}
if(-not (Get-ChildItem (Join-Path $priorityRoot 'AocIncidents') -Filter '*.json'|Select-String -SimpleMatch 'capture_failure:writer')){throw 'Critical failure package missing'}
$recorder.GetField('_diskBytes',$iflags).SetValue($priority,[long](33554432-524288))
$recorder.GetMethod('Trigger',$iflags).Invoke($priority,@([string]'capture_failure:second',[string]'synthetic reserved disk'))|Out-Null
if(-not (Get-ChildItem (Join-Path $priorityRoot 'AocIncidents') -Filter '*.json'|Select-String -SimpleMatch 'capture_failure:second')){throw 'Critical disk reservation failed'}
foreach($label in @('third','fourth','fifth')){$recorder.GetMethod('Trigger',$iflags).Invoke($priority,@([string]('capture_failure:'+$label),[string]'synthetic critical bound'))|Out-Null}
$health=$recorder.GetMethod('StatusJson',$iflags).Invoke($priority,@())|ConvertFrom-Json
if($health.incidents -ne 32 -or $health.criticalIncidents -ne 4 -or $health.issueCapSuppressedOccurrences -ne 2){throw 'Absolute incident bound failed'}
$familyRoot=Join-Path $testRoot 'family-recorder'
$family=$constructor.Invoke(@([string]$familyRoot,[string]'family'))
for($n=1;$n -le 9;$n++){$recorder.GetMethod('Trigger',$iflags).Invoke($family,@([string]('family:wallet'+$n),[string]'synthetic'))|Out-Null}
$health=$recorder.GetMethod('StatusJson',$iflags).Invoke($family,@())|ConvertFrom-Json
if($health.incidents -ne 8 -or $health.familyCapSuppressedOccurrences -ne 1){throw 'Representative family bound failed'}
$historyType=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.WalletIncidentHistory',$true)
$history=[Activator]::CreateInstance($historyType,$true)
for($n=1;$n -le 600;$n++){$historyType.GetMethod('Observe',$iflags).Invoke($history,@([string]('wallet'+$n),[string]('x'*4000)))|Out-Null}
if($historyType.GetProperty('Bytes',$iflags).GetValue($history) -gt 1048576 -or $historyType.GetProperty('EvictedRows',$iflags).GetValue($history) -eq 0){throw 'Wallet history global bound failed'}

Add-Type -ReferencedAssemblies $refs -TypeDefinition @'
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
public sealed class BrokenCashObserverFixture {
 [MethodImpl(MethodImplOptions.NoInlining)] public void NativeExpense(Workshop shop) { shop.ChangeGold(-7); }
 [MethodImpl(MethodImplOptions.NoInlining)] public void NoopExpense(Workshop shop) { }
}
'@
$h=[HarmonyLib.Harmony]::new('aoc.broken.cash.observer.fixture')
try {
 $cash.GetMethod('Install',$flags).Invoke($null,@($h))|Out-Null
 $before=[HarmonyLib.HarmonyMethod]::new($cash.GetMethod('BoundaryBefore',$flags))
 $after=[HarmonyLib.HarmonyMethod]::new($cash.GetMethod('BoundaryAfter',$flags))
 foreach($name in @('NativeExpense','NoopExpense')){$h.Patch([BrokenCashObserverFixture].GetMethod($name),$before,$null,$null,$after)|Out-Null}
 $shop=[Runtime.Serialization.FormatterServices]::GetUninitializedObject([TaleWorlds.CampaignSystem.Settlements.Workshops.Workshop])
 [WorkshopCashFixture]::SetCapital($shop,100)
 $path=$capture.GetMethod('BeginSession',$flags).Invoke($null,@([string]$testRoot,[Func[double]]{42.5}))
 $fixture=[BrokenCashObserverFixture]::new()
 $fixture.NativeExpense($shop)
 $fixture.NoopExpense($shop)
 # Deliberately remove a native observer, not just invent a missing receipt.
 $endpoint=[HarmonyLib.AccessTools]::Method($shop.GetType(),'ChangeGold',[Type[]]@([int]))
 $h.Unpatch($endpoint,[HarmonyLib.HarmonyPatchType]::All,$h.Id)
 $fixture.NativeExpense($shop)
 $capture.GetMethod('Stop',$flags).Invoke($null,@('broken_observer_fixture'))|Out-Null
 $rows=@(Import-Csv -LiteralPath $path -Delimiter "`t")
 $witnesses=@($rows|Where-Object kind -eq 'OBSERVER_COVERAGE')
 if(($witnesses.metric -join ',') -ne 'nested_activity_reconciled,no_net_activity,outer_boundary_recovered_activity'){throw 'Cannot distinguish functioning, inactive and broken observer'}
 $changes=@($rows|Where-Object kind -eq 'WALLET_CHANGE');$net=0
 foreach($r in $changes){$net += [double]$r.after-[double]$r.before}
 if($net -ne -14 -or $shop.Capital -ne 86){throw 'Broken observer was not recovered without changing gameplay'}
 $python=Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
 $report=Join-Path $testRoot 'broken-observer-report'
 & $python (Join-Path $PSScriptRoot 'Analyze-CausalEvidence.py') $path --session $rows[0].session --output $report
 if($LASTEXITCODE -ne 0){throw 'Broken observer analyzer failed'}
 $analysis=Get-Content -LiteralPath ($report+'.json') -Raw|ConvertFrom-Json
 if($analysis.acceptance_checklist.scenarios.nested_wallet_observer_coverage.status -ne 'failed'){throw 'Report missed actual removed observer'}
 $purpose=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.MoneyPurposeObserver',$true)
 foreach($m in @($purpose.GetMethod('Targets',$flags).Invoke($null,@()))){
  if(-not ([HarmonyLib.Harmony]::GetPatchInfo($m).Owners -contains $h.Id)){throw "Money purpose target missing: $m"}
 }
 $native=@($purpose.GetMethod('Targets',$flags).Invoke($null,@())|Where-Object Name -eq 'DailyTickClan')[0]
 $original=[HarmonyLib.PatchProcessor]::GetOriginalInstructions($native)
 $tapped=@($purpose.GetMethod('Tap',$flags).Invoke($null,@($original,[Reflection.MethodBase]$native)))
 if($tapped.Count -ne @($original).Count){throw 'Money callsite tap changed instruction count'}
 $rejected=$false
 try{$purpose.GetMethod('Tap',$flags).Invoke($null,@([System.Collections.Generic.List[HarmonyLib.CodeInstruction]]$tapped,[Reflection.MethodBase]$native))|Out-Null}catch{$rejected=$true}
 if(-not $rejected){throw 'Already replaced native money pattern accepted'}
 # Execute the real grant wrapper on a native wallet, preserving existing hooks.
 $kingdom=[Runtime.Serialization.FormatterServices]::GetUninitializedObject([TaleWorlds.CampaignSystem.Kingdom])
 $path=$capture.GetMethod('BeginSession',$flags).Invoke($null,@([string]$testRoot,[Func[double]]{42.5}))
 $purpose.GetMethod('Grant',$flags).Invoke($null,@($kingdom,[int]120))|Out-Null
 $capture.GetMethod('Stop',$flags).Invoke($null,@('grant_fixture'))|Out-Null
 $rows=@(Import-Csv -LiteralPath $path -Delimiter "`t")
 if($kingdom.KingdomBudgetWallet -ne 120 -or @($rows|Where-Object {$_.kind -eq 'CASH_PURPOSE_END' -and $_.metric -eq 'native_kingdom_budget_grant'}).Count -ne 1){throw 'Native grant execution witness failed'}
 $expense=@($purpose.GetMethod('Targets',$flags).Invoke($null,@())|Where-Object Name -eq 'AddPartyExpense')[0]
 $originalWage=[HarmonyLib.PatchProcessor]::GetOriginalInstructions([Reflection.MethodBase]$expense)
 $wageCode=@($purpose.GetMethod('WageTap',$flags).Invoke($null,(,$originalWage)))
 if($wageCode.Count -ne @($originalWage).Count){throw 'Wage tap changed instruction count'}
 $rejected=$false
 try{$purpose.GetMethod('WageTap',$flags).Invoke($null,(,[System.Collections.Generic.List[HarmonyLib.CodeInstruction]]$wageCode))|Out-Null}catch{$rejected=$true}
 if(-not $rejected){throw 'Already replaced wage pattern accepted'}
 $hero=[Runtime.Serialization.FormatterServices]::GetUninitializedObject([TaleWorlds.CampaignSystem.Hero])
 $path=$capture.GetMethod('BeginSession',$flags).Invoke($null,@([string]$testRoot,[Func[double]]{42.5}))
 $purpose.GetMethod('WageHero',$flags).Invoke($null,@($hero,[int]83))|Out-Null
 $capture.GetMethod('Stop',$flags).Invoke($null,@('wage_fixture'))|Out-Null
 $rows=@(Import-Csv -LiteralPath $path -Delimiter "`t")
 if($hero.Gold -ne 83 -or @($rows|Where-Object {$_.kind -eq 'CASH_PURPOSE_END' -and $_.metric -eq 'native_party_wage_payment'}).Count -ne 1){throw 'Native wage setter execution failed'}
}finally{$h.UnpatchAll($h.Id)}
'PASS: bounded before/after incidents, grouped recurrence, IO/budget failures, real removed-observer detection/recovery, exact finance IL rejection and native grant execution. No live game acceptance claimed.'
