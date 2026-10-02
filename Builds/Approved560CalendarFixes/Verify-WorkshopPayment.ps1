param(
 [string]$BannerlordDir='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord',
 [string]$ModuleRoot=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
 [string]$HarmonyPath=(Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.2\lib\net472\0Harmony.dll')
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Verify-Approved560CalendarFixes.ps1') -BannerlordDir $BannerlordDir -ModuleRoot $ModuleRoot -HarmonyPath $HarmonyPath
$type=$sidecar.GetType('AgesOfCalradia.Approved560CalendarFixes.WorkshopPaymentConservationFix',$true)
$flags=[Reflection.BindingFlags]'NonPublic,Static'
$bound=$type.GetMethod('BoundPayment',$flags)
foreach($case in @(@(228,5,5),@(364,268,268),@(622,627,622),@(499,767,499),@(100,0,0),@(100,-5,0),@(0,20,0),@(-5,20,-5),@(100,100,100))){
 $paid=[int]$bound.Invoke($null,@([int]$case[0],[int]$case[1]))
 if($paid -ne $case[2]){throw "Payment cap failed: $case"}
 if($case[0] -ge 0 -and $case[1] -ge 0 -and (($case[1]-$paid)+$paid -ne $case[1])){throw 'Paired payment created money'}
}
foreach($case in @(@(627,622,228,627),@(767,499,364,767))){
 $cash=[int]$case[0]; $credit=0
 foreach($quote in @($case[1],$case[2])){
  $paid=[int]$bound.Invoke($null,@([int]$quote,$cash)); $cash-=$paid; $credit+=$paid
 }
 if($cash -ne 0 -or $credit -ne $case[3]){throw 'Sequential output payment replay failed'}
}
$target=$type.GetMethod('TargetMethod',$flags).Invoke($null,@())
if(-not ([HarmonyLib.Harmony]::GetPatchInfo($target).Owners -contains $owner)){throw 'Native payment fix not installed'}
$original=[Collections.Generic.List[HarmonyLib.CodeInstruction]]::new()
foreach($instruction in [HarmonyLib.PatchProcessor]::GetOriginalInstructions($target)){$original.Add($instruction)}
$transform=$type.GetMethod('Transpiler',$flags)
$changed=@($transform.Invoke($null,(,$original)))
if($changed.Count -ne $original.Count+3){throw 'Payment fix changed more than the batch quote/cash cap'}
$capIndex=-1
for($i=0;$i -lt $changed.Count;$i++){if($changed[$i].operand -is [Reflection.MethodInfo] -and $changed[$i].operand.Name -eq 'BoundToTown'){$capIndex=$i}}
if($capIndex -lt 2 -or $changed[$capIndex-1].opcode -ne [Reflection.Emit.OpCodes]::Ldarg_2 -or $changed[$capIndex-2].opcode -ne [Reflection.Emit.OpCodes]::Ldarg_1){throw 'Wrong output/workshop arguments'}
$rest=@(for($i=0;$i -lt $changed.Count;$i++){if($i -lt $capIndex-2 -or $i -gt $capIndex){$changed[$i]}})
for($i=0;$i -lt $original.Count;$i++){
 if($rest[$i].opcode -ne $original[$i].opcode -or $rest[$i].operand -ne $original[$i].operand -or ($rest[$i].labels -join ',') -ne ($original[$i].labels -join ',') -or ($rest[$i].blocks -join ',') -ne ($original[$i].blocks -join ',')){throw 'Native quote, event, branch or mutation changed'}
}
$mismatch=[Collections.Generic.List[HarmonyLib.CodeInstruction]]::new()
foreach($instruction in $original){$mismatch.Add([HarmonyLib.CodeInstruction]::new($instruction))}
# Replace the workshop payment load with a different local: reject, never guess.
$minIndex=$capIndex-3
$mismatch[$minIndex+3]=[HarmonyLib.CodeInstruction]::new([Reflection.Emit.OpCodes]::Ldloc_S,[byte]99)
foreach($bad in @($mismatch,[Collections.Generic.List[HarmonyLib.CodeInstruction]]::new([HarmonyLib.CodeInstruction[]]$changed),[Collections.Generic.List[HarmonyLib.CodeInstruction]]::new())){
 $rejected=$false
 try{$transform.Invoke($null,(,$bad))|Out-Null}catch{$rejected=$true}
 if(-not $rejected){throw 'Changed or already-capped payment pattern accepted'}
}
'PASS: both captured shortfalls replayed; full/empty/negative cash, sequential payments, exact native shared-local contract, unchanged native instructions, mismatch and double-cap rejection.'
'NOT_EXERCISED: live campaign and other mods patching the payment body.'
