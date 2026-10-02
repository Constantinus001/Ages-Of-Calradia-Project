param(
 [string]$BannerlordDir='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord',
 [string]$ModuleRoot=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
 [string]$HarmonyPath=(Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.2\lib\net472\0Harmony.dll'),
 [string]$SidecarPath=(Join-Path $PSScriptRoot 'bin\Win64_Shipping_Client\AgesOfCalradia.Approved560CalendarFixes.dll'),
 [switch]$WithDiagnostics
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Verify-SettlementPayment.ps1') -BannerlordDir $BannerlordDir -ModuleRoot $ModuleRoot -HarmonyPath $HarmonyPath -SidecarPath $SidecarPath
$type=$sidecar.GetType('AgesOfCalradia.Approved560CalendarFixes.AiSettlementSaleQuantityFix',$true)
$flags=[Reflection.BindingFlags]'NonPublic,Static'
$target=$type.GetMethod('TargetMethod',$flags).Invoke($null,@())
$transform=$type.GetMethod('Transpiler',$flags)
$original=[Collections.Generic.List[HarmonyLib.CodeInstruction]]::new()
foreach($instruction in [HarmonyLib.PatchProcessor]::GetOriginalInstructions($target)){$original.Add($instruction)}
$generator=[Reflection.Emit.DynamicMethod]::new('VerifySale',[void],[Type[]]@()).GetILGenerator()
$changed=[Collections.Generic.List[HarmonyLib.CodeInstruction]]::new([HarmonyLib.CodeInstruction[]]@($transform.Invoke($null,@($original,$generator))))
if($changed.Count -ne $original.Count+6){throw 'Sale guard changed unexpected instructions'}
$guardIndex=-1
for($i=0;$i -lt $changed.Count;$i++){
 if($changed[$i].operand -is [Reflection.MethodInfo] -and $changed[$i].operand.Name -eq 'CanTransfer'){$guardIndex=$i}
}
if($guardIndex -lt 4){throw 'Sale guard not present'}
$remaining=@(for($i=0;$i -lt $changed.Count;$i++){
 if($i -lt $guardIndex-4 -or $i -gt $guardIndex+1){$changed[$i]}
})
for($i=0;$i -lt $original.Count;$i++){
 if($remaining[$i].opcode -ne $original[$i].opcode -or $remaining[$i].operand -ne $original[$i].operand){throw 'Native sale instruction altered'}
}
$mismatch=[Collections.Generic.List[HarmonyLib.CodeInstruction]]::new()
foreach($instruction in $original){$mismatch.Add([HarmonyLib.CodeInstruction]::new($instruction))}
$mismatch[$guardIndex-4]=[HarmonyLib.CodeInstruction]::new([Reflection.Emit.OpCodes]::Ldloc_2)
foreach($bad in @($changed,$mismatch,[Collections.Generic.List[HarmonyLib.CodeInstruction]]::new())){
 $rejected=$false
 try {$transform.Invoke($null,@($bad,$generator))|Out-Null} catch {$rejected=$true}
 if(-not $rejected){throw 'Changed or duplicate sale guard accepted'}
}
$affordable=$type.GetMethod('Affordable',$flags)
foreach($case in @(@(180,120,180,$false),@(0,80,79,$false),@(80,100,180,$true),@(0,0,0,$true),@([int]::MaxValue,1,[int]::MaxValue,$false))){
 if($affordable.Invoke($null,@([int]$case[0],[int]$case[1],[int]$case[2])) -ne $case[3]){throw 'Affordability boundary failed'}
}
Add-Type -Path (Join-Path $PSScriptRoot 'NativeAiSaleFixture.cs') -ReferencedAssemblies $references
$observer=$null
if($WithDiagnostics){
 $sourceRoot=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
 $assembly=[Reflection.Assembly]::LoadFrom((Join-Path $sourceRoot 'Modules\AgesOfCalradiaSoakDiagnostics\bin\Win64_Shipping_Client\AgesOfCalradia.SoakDiagnostics.dll'))
 $observer=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.SupplyChainObserver',$true)
}
try {
 $install=$null
 if($observer){$install=[Action]{$observer.GetMethod('Install',$flags).Invoke($null,@())|Out-Null}}
 [NativeAiSaleFixture]::Run($install)
} catch { Write-Output $_.Exception.ToString(); throw }
finally {if($observer){$observer.GetMethod('Uninstall',$flags).Invoke($null,@())|Out-Null}}
'LIMIT: deterministic price/role/event boundaries. Live campaign behavior and active-capture coverage are not certified.'
