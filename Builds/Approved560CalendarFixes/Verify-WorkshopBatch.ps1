param(
 [string]$BannerlordDir='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord',
 [string]$ModuleRoot=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
 [string]$HarmonyPath=(Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.2\lib\net472\0Harmony.dll')
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Verify-WorkshopApprovalQuote.ps1') -BannerlordDir $BannerlordDir -ModuleRoot $ModuleRoot -HarmonyPath $HarmonyPath
$instanceFlags=[Reflection.BindingFlags]'Instance,NonPublic,Public'
$batchType=$quote.GetNestedType('Batch',[Reflection.BindingFlags]'NonPublic')
$payment=$sidecar.GetType('AgesOfCalradia.Approved560CalendarFixes.WorkshopPaymentConservationFix',$true)
$pay=$payment.GetMethod('BoundToTown',$flags)
function New-Uninitialized([Type]$Type){[Runtime.Serialization.FormatterServices]::GetUninitializedObject($Type)}
$town=New-Uninitialized ([TaleWorlds.CampaignSystem.Settlements.Town])
$settlement=New-Uninitialized ([TaleWorlds.CampaignSystem.Settlements.Settlement])
$settlement.GetType().GetField('<SettlementComponent>k__BackingField',$instanceFlags).SetValue($settlement,$town)
$settlement.Town=$town
$shop=New-Uninitialized ([TaleWorlds.CampaignSystem.Settlements.Workshops.Workshop])
$shop.GetType().GetField('_settlement',$instanceFlags).SetValue($shop,$settlement)
$townGold=[TaleWorlds.CampaignSystem.Settlements.SettlementComponent].GetProperty('Gold')
$capital=$shop.GetType().GetField('<Capital>k__BackingField',$instanceFlags)
$element=[Activator]::CreateInstance([TaleWorlds.Core.EquipmentElement])
function Make-Batch([int[]]$Prices){
 $batch=[Activator]::CreateInstance($batchType,$true)
 $batchType.GetField('Shop',$instanceFlags).SetValue($batch,$shop)
 foreach($price in $Prices){
  $batchType.GetField('Items',$instanceFlags).GetValue($batch).Add($element)
  $batchType.GetField('Prices',$instanceFlags).GetValue($batch).Add($price)
 }
 $batchType.GetField('Ready',$instanceFlags).SetValue($batch,$true)
 return $batch
}
# End-to-end deterministic batch fixture: approval, input consumption, output
# production and cash settlement. Real batch/payment helpers and native gold
# mutations; inventory/model/event simulation is explicit, NOT a live campaign.
foreach($case in @(
 @{Name='exact cash, prices rise';Cash=100;Cost=10;Prices=@(40,60);Live=@(400,600);Accepted=$true;Warehouse=$false},
 @{Name='one short, no inputs consumed';Cash=99;Cost=10;Prices=@(40,60);Live=@(400,600);Accepted=$false;Warehouse=$false},
 @{Name='prices fall';Cash=100;Cost=10;Prices=@(40,60);Live=@(4,6);Accepted=$true;Warehouse=$false},
 @{Name='zero cash';Cash=0;Cost=10;Prices=@(40,60);Live=@(40,60);Accepted=$false;Warehouse=$false},
 @{Name='player warehouse bypass';Cash=0;Cost=0;Prices=@(40,60);Live=@(400,600);Accepted=$true;Warehouse=$true}
)){
 $townGold.SetValue($town,[int]$case.Cash,$null); $capital.SetValue($shop,1000)
 $batch=Make-Batch $case.Prices
 $quote.GetField('CurrentBatch',$flags).SetValue($null,$batch)
 $total=[int](($case.Prices|Measure-Object -Sum).Sum)
 $accepted=$case.Warehouse -or ($case.Cash -ge $total -and $total -gt $case.Cost)
 $inputs=1; $outputs=0; $warehouse=0
 if($accepted){
  $inputs--
  $shop.ChangeGold(-[int]$case.Cost); $town.ChangeGold([int]$case.Cost)
  for($i=0;$i -lt $case.Prices.Count;$i++){
   if($case.Warehouse){$warehouse++; continue}
   $amount=[int]$pay.Invoke($null,@([int]$case.Live[$i],$element,$shop))
   if($amount -ne $case.Prices[$i]){throw "Locked quote failed: $($case.Name)"}
   $outputs++; $shop.ChangeGold($amount); $town.ChangeGold(-$amount)
  }
 }
 if($accepted -ne $case.Accepted){throw "Approval failed: $($case.Name)"}
 if(($town.Gold+$shop.Capital) -ne ($case.Cash+1000)){throw 'Batch created or destroyed cash'}
 if(-not $accepted -and ($inputs -ne 1 -or $outputs -ne 0 -or $shop.Capital -ne 1000 -or $town.Gold -ne $case.Cash)){throw 'Rejected batch mutated inputs/output/cash'}
 if($accepted -and -not $case.Warehouse -and ($outputs -ne 2 -or $shop.Capital -ne 1000-$case.Cost+$total)){throw 'Accepted batch not fully paid'}
 if($case.Warehouse -and ($warehouse -ne 2 -or $outputs -ne 0 -or $town.Gold -ne 0 -or $shop.Capital -ne 1000)){throw 'Warehouse-only path used town payment'}
}
# Unrelated workshop and player market payments must not consume another plan.
$batch=Make-Batch @(40)
$quote.GetField('CurrentBatch',$flags).SetValue($null,$batch)
$other=New-Uninitialized ([TaleWorlds.CampaignSystem.Settlements.Workshops.Workshop])
if($quote.GetMethod('ResolvePayment',$flags).Invoke($null,@(90,$element,$other)) -ne 90){throw 'Other workshop inherited locked quote'}
if($batchType.GetField('Paid',$instanceFlags).GetValue($batch) -ne 0){throw 'Other workshop consumed batch'}
# Unexpected cash removal still conserves money; it is not full-payment success.
$townGold.SetValue($town,5,$null)
if($pay.Invoke($null,@(400,$element,$shop)) -ne 5){throw 'External cash drain bypassed safety cap'}
$quote.GetField('CurrentBatch',$flags).SetValue($null,$null)
# Player town sales remain native quoted, with the conservation safety cap.
$townGold.SetValue($town,100,$null)
if($pay.Invoke($null,@(70,$element,$shop)) -ne 70){throw 'Player town payment changed quote'}
# Exercise actual collection boundaries: nested/unrelated calls cannot steal the
# outer plan, and a skipped original cannot mark an incomplete plan ready.
$batch=[Activator]::CreateInstance($batchType,$true)
$batchType.GetField('Shop',$instanceFlags).SetValue($batch,$shop)
$quote.GetField('CurrentBatch',$flags).SetValue($null,$batch)
$quote.GetField('CapitalCycle',$flags).SetValue($null,$true)
$argsOuter=[object[]]@($shop,$null)
$quote.GetMethod('Prefix',$flags).Invoke($null,$argsOuter)|Out-Null
$argsNested=[object[]]@($shop,$null)
$quote.GetMethod('Prefix',$flags).Invoke($null,$argsNested)|Out-Null
if($argsNested[1].GetType().GetField('Selected',$instanceFlags).GetValue($argsNested[1]) -ne $null){throw 'Recursive collection acquired outer batch'}
$quote.GetMethod('Finalizer',$flags).Invoke($null,@($argsNested[1]))|Out-Null
$batchType.GetField('Items',$instanceFlags).GetValue($batch).Add($element)
$batchType.GetField('Prices',$instanceFlags).GetValue($batch).Add(40)
$results=[Collections.Generic.List[TaleWorlds.Core.EquipmentElement]]::new(); $results.Add($element)
$quote.GetMethod('Postfix',$flags).Invoke($null,@($results,$argsOuter[1]))|Out-Null
$quote.GetMethod('Finalizer',$flags).Invoke($null,@($argsOuter[1]))|Out-Null
if(-not $batchType.GetField('Ready',$instanceFlags).GetValue($batch)){throw 'Matching native output list did not seal batch'}
if($batchType.GetField('Collecting',$instanceFlags).GetValue($batch)){throw 'Collection context leaked'}
$quote.GetField('CurrentBatch',$flags).SetValue($null,$null)
$quote.GetField('CapitalCycle',$flags).SetValue($null,$false)
'PASS: complete simulated batches using real quote/payment helpers and native cash mutations; exact/insufficient/zero cash, rising/falling prices, no input loss on rejection, warehouse fixture, unrelated shops, player market and external-drain safety.'
'LIMIT: inventory, event and approval orchestration are simulated; full native campaign behavior still requires live validation.'
