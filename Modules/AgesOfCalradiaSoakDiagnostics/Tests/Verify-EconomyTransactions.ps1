param([string]$BannerlordDir='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord')
$ErrorActionPreference='Stop'
$moduleRoot=Split-Path -Parent $PSScriptRoot
$gameBin=Join-Path $BannerlordDir 'bin\Win64_Shipping_Client'
foreach($name in @('TaleWorlds.Library','TaleWorlds.DotNet','TaleWorlds.ScreenSystem','TaleWorlds.Localization','TaleWorlds.ObjectSystem','TaleWorlds.SaveSystem','TaleWorlds.Core','TaleWorlds.Engine','TaleWorlds.CampaignSystem','TaleWorlds.MountAndBlade')){
    [Reflection.Assembly]::LoadFrom((Join-Path $gameBin ($name+'.dll'))) | Out-Null
}
[Reflection.Assembly]::LoadFrom((Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.2\lib\net472\0Harmony.dll'))|Out-Null
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $moduleRoot 'bin\Win64_Shipping_Client\AgesOfCalradia.SoakDiagnostics.dll'))
$static=[Reflection.BindingFlags]'Static,NonPublic'
$captureLimit=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.CalendarSoakBehavior',$true).GetMethod('CaptureLimitReached',$static)
foreach($case in @(@(179.9,2.9,268435455,$false),@(180,0,0,$true),@(0,3,0,$true),@(0,0,268435456,$true))){
 if($captureLimit.Invoke($null,@([double]$case[0],[double]$case[1],[long]$case[2])) -ne $case[3]){throw 'Short raw capture limit failed'}
}
$callerType=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.EconomyCallerDiagnostics',$true)
$shouldCapture=$callerType.GetMethod('ShouldCapture',$static)
foreach($case in @(@($true,$true,'ApplyBetweenCharacters',$true),@($false,$true,'ApplyBetweenCharacters',$false),@($true,$false,'ApplyBetweenCharacters',$false),@($true,$true,'RunTownWorkshop',$false),@($true,$true,'set_Gold',$true))){
 if($shouldCapture.Invoke($null,@($case[0],$case[1],$case[2])) -ne $case[3]){throw 'Raw money caller capture crossed mode/scope boundary'}
}
Add-Type -TypeDefinition @'
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
public static class EconomyCallerProbe {
 [MethodImpl(MethodImplOptions.NoInlining)]
 public static string KnownGrantCaller(MethodInfo capture) { return (string)capture.Invoke(null, null); }
}
'@
$callerText=[EconomyCallerProbe]::KnownGrantCaller($callerType.GetMethod('Capture',$static))
if($callerText -notmatch 'EconomyCallerProbe.KnownGrantCaller' -or $callerText -match '[A-Za-z]:\\'){throw 'Caller evidence omitted test grant or captured filesystem paths'}
$instance=[Reflection.BindingFlags]'Instance,NonPublic'
$patches=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.EconomyDiagnosticPatches',$true)
$targets=@($patches.GetMethod('Targets',$static).Invoke($null,@()))
if($targets.Count -lt 30){throw 'Native transaction target coverage incomplete'}
foreach($target in $targets){if(-not $target.GetMethodBody()){throw "Missing managed native body: $target"}}
$ledgerType=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.EconomyLedger',$true)
$ledger=[Activator]::CreateInstance($ledgerType,$true)
$observe=$ledgerType.GetMethod('Observe',$instance)
$reconcile=$ledgerType.GetMethod('Reconcile',$instance)
if($observe.Invoke($ledger,@('hero:a|gold',[double]100,[double]75)) -ne 0){throw 'Initial transaction rejected'}
if($reconcile.Invoke($ledger,@('hero:a|gold',[double]75)) -ne 0){throw 'Valid transaction failed reconciliation'}
if($reconcile.Invoke($ledger,@('hero:a|gold',[double]80)) -ne 5){throw 'Missing mutation was not detected'}
$invalidRejected=$false
try{$observe.Invoke($ledger,@('bad',[double]::NaN,[double]0))|Out-Null}catch{$invalidRejected=$true}
if(-not $invalidRejected){throw 'Nonfinite balance accepted'}
$observe.Invoke($ledger,@('roster:a|item:grain',[double]3,[double]0))|Out-Null
$observe.Invoke($ledger,@('roster:b|item:iron',[double]5,[double]7))|Out-Null
$keys=@($ledgerType.GetMethod('Keys',$instance).Invoke($ledger,@('roster:a|item:')))
if($keys.Count -ne 1 -or $keys[0] -ne 'roster:a|item:grain'){throw 'Inventory index omitted zeroed item or crossed owners'}
$historyType=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.EconomyStateHistory',$true)
$history=[Activator]::CreateInstance($historyType,$true)
$sample=$historyType.GetMethod('Observe',$instance)
foreach($case in @(@(1,0,0),@(3,0,2),@(4,7,0),@(5,0,0),@(7,0,2))){
    if($sample.Invoke($history,@('grain',[double]$case[0],[double]$case[1])) -ne $case[2]){throw 'Sampled shortage duration/reset incorrect'}
}
$rejected=$false
try{$sample.Invoke($history,@('grain',[double]6,[double]0))|Out-Null}catch{$rejected=$true}
if(-not $rejected){throw 'Backward stock history accepted'}
$residual=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.EconomyFlowDiagnostics',$true).GetMethod('Residual',$static)
$identityType=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.EconomyObjectIdentity',$true)
$identity=[Activator]::CreateInstance($identityType,$true)
$getIdentity=$identityType.GetMethod('Get',$instance)
$partyA=New-Object object;$partyB=New-Object object
$a=$getIdentity.Invoke($identity,@($partyA));$b=$getIdentity.Invoke($identity,@($partyB))
if($a -eq $b -or $a -ne $getIdentity.Invoke($identity,@($partyA))){throw 'Object lifetime identities collided or changed'}
$clamp=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.EconomyFlowDiagnostics',$true).GetMethod('MatchesSettlementClamp',$static)
if(-not $clamp.Invoke($null,@([int]-247,[double]247,[double]-224,[double]224)) -or $clamp.Invoke($null,@([int]-247,[double]247,[double]-223,[double]224))){throw 'Native clamp classifier hid an unexplained residual'}
if($residual.Invoke($null,@([double]-20,[double]20,$false)) -ne 0 -or $residual.Invoke($null,@([double]-20,[double]15,$false)) -ne -5 -or $residual.Invoke($null,@([double]5,[double]5,$true)) -ne 5){throw 'Endpoint reconciliation or alias handling incorrect'}

Add-Type -ReferencedAssemblies @('C:/Program Files (x86)/Reference Assemblies/Microsoft/Framework/.NETFramework/v4.7.2/Facades/netstandard.dll',(Join-Path $gameBin 'TaleWorlds.CampaignSystem.dll'),(Join-Path $gameBin 'TaleWorlds.Localization.dll'),(Join-Path $gameBin 'TaleWorlds.Library.dll')) -TypeDefinition @'
using System;
using System.Runtime.CompilerServices;
public class EconomyObserverFixture {
    private int wallet;
    public int TributeWallet {
        [MethodImpl(MethodImplOptions.NoInlining)] get { return wallet; }
        [MethodImpl(MethodImplOptions.NoInlining)] set { if(value==999) throw new InvalidOperationException("original-error"); wallet=Math.Max(0,value); }
    }
    [MethodImpl(MethodImplOptions.NoInlining)] public void Outer(int value) { TributeWallet=value; }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Transfer(EconomyObserverFixture receiver, int value) { receiver.TributeWallet=value; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void Finance(ref TaleWorlds.CampaignSystem.ExplainedNumber goldChange, bool applyWithdrawals) { goldChange.Add(-7); }
    public void RunFinance(bool apply) { var number=new TaleWorlds.CampaignSystem.ExplainedNumber(100); Finance(ref number,apply); if(number.ResultNumber!=93) throw new Exception("changed-finance"); }
    public int Evaluations;
    [MethodImpl(MethodImplOptions.NoInlining)] public bool Probe() { Evaluations++; return false; }
    [MethodImpl(MethodImplOptions.NoInlining)] public TaleWorlds.CampaignSystem.ExplainedNumber NativeSpeed() { Evaluations++; return new TaleWorlds.CampaignSystem.ExplainedNumber(0.25f); }
    [MethodImpl(MethodImplOptions.NoInlining)] public float NativeDemand(string category) { Evaluations++; return 3.5f; }
}
'@
$diagnostics=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.EconomyTransactionDiagnostics',$true)
$trace=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.EconomyTrace',$true)
$testRoot=Join-Path $env:TEMP ('aoc-economy-verifier-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$trace.GetField('Failure',$static).SetValue($null,[Action[string]]{param($message)})
$path=$diagnostics.GetMethod('BeginSession',$static).Invoke($null,@([string]$testRoot,[Func[double]]{42.5}))
$harmony=New-Object HarmonyLib.Harmony('aoc.economy.fixture')
$prefix=New-Object HarmonyLib.HarmonyMethod($diagnostics.GetMethod('Before',$static))
$finalizer=New-Object HarmonyLib.HarmonyMethod($diagnostics.GetMethod('After',$static))
try {
    foreach($name in @('set_TributeWallet','Outer','Transfer','Finance')){
        $selectedFinalizer=if($name -eq 'Finance'){New-Object HarmonyLib.HarmonyMethod($diagnostics.GetMethod('FinanceAfter',$static))}else{$finalizer}
        $harmony.Patch([EconomyObserverFixture].GetMethod($name),$prefix,$null,$null,$selectedFinalizer)|Out-Null
    }
    foreach($entry in @(@('Probe','BooleanResult'),@('NativeSpeed','SpeedResult'),@('NativeDemand','DemandResult'))){
        $postfix=New-Object HarmonyLib.HarmonyMethod($diagnostics.GetMethod($entry[1],$static))
        $harmony.Patch([EconomyObserverFixture].GetMethod($entry[0]),$prefix,$postfix,$null,$finalizer)|Out-Null
    }
    $fixture=New-Object EconomyObserverFixture
    [EconomyObserverFixture]::Transfer($fixture,100)
    $fixture.Outer(-5)
    $fixture.RunFinance($true)
    $fixture.RunFinance($false)
    if($fixture.Probe() -ne $false -or $fixture.NativeSpeed().ResultNumber -ne 0.25 -or $fixture.NativeDemand('grain') -ne 3.5 -or $fixture.Evaluations -ne 3){throw 'Result observer changed outcome or re-executed native model'}
    if($fixture.TributeWallet -ne 0){throw 'Observer changed native clamp behavior'}
    $trace.GetMethod('Flush',$static).Invoke($null,@($true))|Out-Null
    $rows=@(Import-Csv -LiteralPath $path -Delimiter "`t")
    $money=@($rows|Where-Object kind -eq WALLET)
    if(@($rows|Where-Object kind -eq RECIPE_GATE).Count -ne 1 -or @($rows|Where-Object kind -eq FINANCE_RESULT).Count -ne 1){throw 'Result callbacks did not receive prefix state and emit observations'}
    if(@($rows|Where-Object {$_.kind -eq 'MARKET_DEMAND' -and [double]$_.after -eq 3.5}).Count -ne 1){throw 'Float callback lost state/result'}
    if($money.Count -ne 2 -or [double]$money[0].delta -ne 100 -or [double]$money[1].delta -ne -100){throw 'Ledger recorded requested amount instead of actual balance delta'}
    if(@($money|Where-Object parent -eq '0').Count){throw 'Nested transaction lost parent correlation'}
    if(@($rows|Where-Object kind -eq BEGIN).Count -ne @($rows|Where-Object kind -eq END).Count){throw 'Unbalanced successful scopes'}
    $finance=@($rows|Where-Object kind -eq FINANCE_COMPONENT)
    if($finance.Count -ne 1 -or [double]$finance[0].delta -ne -7){throw 'Ref finance changes or preview filtering were not preserved'}
    $threw=$false
    try{$fixture.Outer(999)}catch{if($_.Exception.ToString().Contains('original-error')){$threw=$true}}
    if(-not $threw){throw 'Observer suppressed or replaced native exception'}
    if($trace.GetProperty('Healthy',$static).GetValue($null)){throw 'Native exception did not invalidate coverage'}
    $closedPath=$diagnostics.GetMethod('BeginSession',$static).Invoke($null,@([string]$testRoot,[Func[double]]{42.5}))
    $diagnostics.GetMethod('Stop',$static).Invoke($null,@())|Out-Null
    $diagnostics.GetMethod('Stop',$static).Invoke($null,@())|Out-Null
    $closedRows=@(Import-Csv -LiteralPath $closedPath -Delimiter "`t")
    if(@($closedRows|Where-Object kind -eq SESSION_END).Count -ne 1 -or $closedRows[-1].kind -ne 'SESSION_END'){throw 'Stop must durably close a healthy session exactly once'}
    $controller=Get-Content -Raw -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'CalendarSoakBehavior.cs')
    if($controller -notmatch '(?s)EconomyTransactionDiagnostics\.Stop\(\);\s*SoakLog\.Write\("QUIT_AFTER_SAVE".*?Utilities\.QuitGame\(\)'){throw 'Evidence must close before native quit, not just on module unload'}
} finally {
    $harmony.UnpatchAll('aoc.economy.fixture')
    $trace.GetMethod('Close',$static).Invoke($null,@())|Out-Null
}
Write-Output "PASS: $($targets.Count) installed-version targets resolve; actual delta/clamping, parent correlation, reconciliation, ref finance/preview preservation, nonfinite rejection, original exceptions and idempotent session closure. Synthetic evidence: $testRoot"
Write-Output 'NOT_EXERCISED: real wages, tribute, food supply, workshop flows and runtime reconciliation. Run the short coverage gate before approving a long soak.'
