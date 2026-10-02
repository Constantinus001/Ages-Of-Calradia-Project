param([string]$DiagnosticsAssemblyPath=(Join-Path (Split-Path -Parent $PSScriptRoot) 'bin/Win64_Shipping_Client/AgesOfCalradia.SoakDiagnostics.dll'))
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Verify-SupplyCapture.ps1') -DiagnosticsAssemblyPath $DiagnosticsAssemblyPath
$cash=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.SupplyCashObserver',$true)
$refs=@([AppDomain]::CurrentDomain.GetAssemblies()|Where-Object {$_.GetName().Name -like 'TaleWorlds.*'}|ForEach-Object {$_.Location})
$refs+='C:/Program Files (x86)/Reference Assemblies/Microsoft/Framework/.NETFramework/v4.7.2/Facades/netstandard.dll'
Add-Type -ReferencedAssemblies $refs -TypeDefinition @'
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
public sealed class WorkshopCashFixture {
 public static void SetCapital(Workshop shop, int value) { typeof(Workshop).GetProperty("Capital").SetValue(shop,value,null); }
 [MethodImpl(MethodImplOptions.NoInlining)] public void HandleDailyExpense(Workshop shop) {
  // The direct field/property write deliberately bypasses ChangeGold.
  SetCapital(shop, shop.Capital-23);
 }
 [MethodImpl(MethodImplOptions.NoInlining)] public void Withdrawal(Hero hero, ref ExplainedNumber result, bool apply) {
  result.Add(7);
  if(!apply) return;
  var shop=hero.OwnedWorkshops[0];
  // Exercise a recorded endpoint plus an unhooked remainder in one boundary.
  typeof(Workshop).GetMethod("ChangeGold").Invoke(shop,new object[]{-10});
  SetCapital(shop,shop.Capital-15);
 }
 public void Run(Hero hero, bool apply) {
  var result=new ExplainedNumber(100);
  Withdrawal(hero,ref result,apply);
  if(result.ResultNumber!=107) throw new Exception("Native ref result changed");
 }
}
'@
$h=[HarmonyLib.Harmony]::new('aoc.workshop.cash.boundary.fixture')
try {
 $cash.GetMethod('Install',$flags).Invoke($null,@($h))|Out-Null
 $withdrawal=$cash.GetMethod('WithdrawalTarget',$flags).Invoke($null,@())
 if(-not ([HarmonyLib.Harmony]::GetPatchInfo($withdrawal).Owners -contains $h.Id)){throw 'Missing native withdrawal hook'}
 $wp=$cash.GetMethod('WithdrawalBefore',$flags)
 if(@($wp.GetParameters()|Where-Object Name -eq '__args').Count){throw 'Withdrawal hook must not marshal native ref arguments'}
 $before=[HarmonyLib.HarmonyMethod]::new($cash.GetMethod('BoundaryBefore',$flags))
 $after=[HarmonyLib.HarmonyMethod]::new($cash.GetMethod('BoundaryAfter',$flags))
 $h.Patch([WorkshopCashFixture].GetMethod('HandleDailyExpense'),$before,$null,$null,$after)|Out-Null
 $h.Patch([WorkshopCashFixture].GetMethod('Withdrawal'),[HarmonyLib.HarmonyMethod]::new($wp),$null,$null,$after)|Out-Null
 $shop=[Runtime.Serialization.FormatterServices]::GetUninitializedObject([TaleWorlds.CampaignSystem.Settlements.Workshops.Workshop])
 $hero=[Runtime.Serialization.FormatterServices]::GetUninitializedObject([TaleWorlds.CampaignSystem.Hero])
 $owned=@($hero.GetType().GetFields([Reflection.BindingFlags]'Instance,NonPublic')|Where-Object Name -match 'ownedWorkshops')
 if($owned.Count -ne 1){throw 'Native owned workshops field changed'}
 $list=[Activator]::CreateInstance($owned[0].FieldType)
 $list.Add($shop)
 $owned[0].SetValue($hero,$list)
 [WorkshopCashFixture]::SetCapital($shop,100)
 $path=$capture.GetMethod('BeginSession',$flags).Invoke($null,@([string]$testRoot,[Func[double]]{42.5}))
 $fixture=[WorkshopCashFixture]::new()
 $fixture.HandleDailyExpense($shop)
 $fixture.Run($hero,$false)
 if($shop.Capital -ne 77){throw 'Finance preview mutated shop cash'}
 $fixture.Run($hero,$true)
 if($shop.Capital -ne 52){throw 'Observed boundaries changed native arithmetic'}
 $capture.GetMethod('Stop',$flags).Invoke($null,@('synthetic_workshop_cash_complete'))|Out-Null
 $rows=@(Import-Csv -LiteralPath $path -Delimiter "`t")
 $changes=@($rows|Where-Object {$_.kind -eq 'WALLET_CHANGE' -and $_.owner -like 'Workshop/*'})
 $net=0;foreach($row in $changes){$net += [double]$row.after-[double]$row.before}
 if($net -ne -48){throw "Nested workshop cash double-counted or lost: $net"}
 $purposeEnds=@($rows|Where-Object kind -eq 'CASH_PURPOSE_END')
 if($purposeEnds.Count -ne 2 -or @($changes|Where-Object {$_.detail -notmatch 'cashPurposeOperation=[a-f0-9]{32}'}).Count){throw 'Nested cash lost its purpose boundary link'}
 $purposeContext=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.CashPurposeContext',$true)
 if($purposeContext.GetProperty('Context',$flags).GetValue($null,$null) -ne '; cashPurposeOperation=none'){throw 'Cash purpose context leaked after original completed'}
 $receipts=@($rows|Where-Object kind -eq 'WORKSHOP_CASH_BOUNDARY')
 if($receipts.Count -ne 2 -or @($receipts|Where-Object metric -eq 'owner_payout').Count -ne 1 -or @($receipts|Where-Object metric -eq 'operating_expense').Count -ne 1){throw 'Missing boundaries or finance preview misreported as withdrawal'}
 if(@($receipts|Where-Object {$_.detail -notmatch 'originalRan=True'}).Count){throw 'Executed-boundary evidence missing'}
 Write-Output 'PASS: native targets, unchanged ref finance result, preview exclusion, bypassed expense and nested withdrawal reconcile to -48 without double-counting.'
}finally{$h.UnpatchAll($h.Id)}
